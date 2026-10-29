using System.Diagnostics;
using System.ServiceProcess;
using TimeoutException=System.TimeoutException;
using System.Text.Json;
using Raider.Core;
using Raider.Engine;
using Raider.History;
namespace Raider.Service;

public sealed record ClearRequest(string Id);
public sealed record ClearResult(string Id,bool Success,string Message);

public sealed record ServicePreview(int Schema,Measurement? Frame,string Capture,string Persistence,string Reports,long Dropped)
{
    public int? QueuedFrames {get;init;}
    public long? QueuedBytes {get;init;}
    public double? MaximumQueueDelayMs {get;init;}
    public bool? WriterHealthy {get;init;}
    public long? LastReportUtcTicks {get;init;}
}

public sealed class RaiderBootService : ServiceBase
{
    readonly CancellationTokenSource stop=new();
    readonly ManualResetEventSlim started=new();
    Thread? captureThread;
    Exception? startupError;
    volatile Measurement? latest;
    volatile string captureStatus="Starting capture";
    long lastArrival;
    readonly SourceContinuity continuity=new();
    int clearBusy;
    HistoryWriter? history;
    HourlyReportWriter? reports;
    public RaiderBootService(){ServiceName=NativeService.Name;CanStop=true;CanShutdown=true;CanHandlePowerEvent=true;AutoLog=true;}
    protected override void OnStart(string[] arguments)
    {
        var configuration=BootConfiguration.Load();
        captureThread=new Thread(()=>Capture(configuration)){IsBackground=true,Name="Cylon Raider boot capture"};captureThread.Start();
        if(!started.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException("Boot capture did not initialize");
        if(startupError!=null)throw new InvalidOperationException("Boot capture failed",startupError);
    }
    void Capture(BootConfiguration config)
    {
        using var ownership=new Mutex(true,@"Global\Eiren.CylonRaider.Collector.v01",out bool owned);
        if(!owned){startupError=new IOException("Another private collector owns capture; no duplicate started");started.Set();return;}
        Task? listener=null;
        try{
            string logs=config.Options.LogDirectory??BootConfiguration.DefaultLogs,output=config.Options.ReportDirectory??BootConfiguration.DefaultReports;
            history=new HistoryWriter(Path.Combine(logs,"history.db"),config.Options,sourceKind:"boot-service");
            reports=new HourlyReportWriter(history.FilePath,output,config.Options);
            using var collector=new EtwCollector(config.Options.DetailedDiagnostics);
            collector.Measured+=frame=>{var safe=continuity.Observe(frame);if(safe==null)return;history.Append(safe);latest=safe;captureStatus=safe.Status;Interlocked.Exchange(ref lastArrival,Stopwatch.GetTimestamp());};
            history.Begin();collector.Start();Interlocked.Exchange(ref lastArrival,Stopwatch.GetTimestamp());
            listener=Task.Run(()=>Serve(config.OwnerSid,stop.Token));started.Set();
            int restarts=0;var recoveryWindow=Stopwatch.StartNew();var policyTime=File.GetLastWriteTimeUtc(BootConfiguration.FilePath);
            while(!stop.Token.WaitHandle.WaitOne(1000)){
                WindowsTimeZone.RefreshIfChanged();
                var changed=File.GetLastWriteTimeUtc(BootConfiguration.FilePath);
                if(changed!=policyTime){var updated=BootConfiguration.Load();if(updated.OwnerSid!=config.OwnerSid||updated.Options.LogDirectory!=config.Options.LogDirectory||updated.Options.ReportDirectory!=config.Options.ReportDirectory)throw new UnauthorizedAccessException("Unexpected privileged destination change");collector.DetailedDiagnostics=updated.Options.DetailedDiagnostics;history.Configure(updated.Options);reports.Configure(updated.Options);policyTime=changed;}
                if(recoveryWindow.Elapsed>TimeSpan.FromMinutes(10)){restarts=0;recoveryWindow.Restart();}
                if(!continuity.RestartRequested&&!continuity.HeartbeatExpired)continue;
                captureStatus="Collector interruption — missing intervals remain unobserved";history.State(HistoryState.Interrupted,captureStatus);latest=null;
                collector.Stop();if(++restarts>3)throw new IOException("Collector watchdog exhausted bounded recovery attempts");
                if(!continuity.RestartRequested)continuity.RequestRestart(captureStatus);continuity.ResetAfterRestart();history.Begin();collector.Start();Interlocked.Exchange(ref lastArrival,Stopwatch.GetTimestamp());
            }
            collector.Stop();
        }catch(Exception ex){
            captureStatus="Capture unavailable: "+ex.Message;
            if(!started.IsSet){startupError=ex;started.Set();}
            else{ExitCode=1066;ThreadPool.QueueUserWorkItem(_=>Stop());}
        }finally{
            stop.Cancel();
            if(history!=null)history.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(reports!=null)try{reports.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}catch(TimeoutException){}
            if(listener!=null)try{listener.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}catch(Exception ex)when(ex is OperationCanceledException or TimeoutException){}
            ownership.ReleaseMutex();
        }
    }
    async Task Serve(string owner,CancellationToken token)
    {
        while(!token.IsCancellationRequested)try{
            using var pipe=NativeService.CreatePipe(owner);
            while(!token.IsCancellationRequested){
                try{
                    await pipe.WaitForConnectionAsync(token);
                    var hello=new byte[1];if(await pipe.ReadAsync(hello,token).AsTask().WaitAsync(TimeSpan.FromSeconds(5),token)!=1||hello[0]!=0x52||!NativeService.Authorized(pipe,owner))throw new UnauthorizedAccessException("Unauthorized local preview client");
                    using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};long prior=-1;
                    while(!token.IsCancellationRequested){
                        var frame=latest;long stamp=Interlocked.Read(ref lastArrival);var message=new ServicePreview(1,stamp==prior?null:frame,captureStatus,history?.Status??"Opening history",reports?.Status??"Opening reports",history?.Dropped??0){QueuedFrames=history?.QueuedFrames,QueuedBytes=history?.QueuedBytes,MaximumQueueDelayMs=history?.MaximumQueueDelayMs,WriterHealthy=history?.Healthy,LastReportUtcTicks=reports?.LastGeneratedUtc?.Ticks};prior=stamp;
                        string line=JsonSerializer.Serialize(message);
                        if(line.Length>2_000_000)line=JsonSerializer.Serialize(message with{Frame=null,Capture="Live preview exceeds bounded pipe size; independent history continues"});
                        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));await writer.WriteLineAsync(line.AsMemory(),timeout.Token);await Task.Delay(1000,token);
                    }
                }catch(OperationCanceledException)when(!token.IsCancellationRequested){}
                catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException){/* Preview failure never interrupts capture/history. */}
                finally{if(pipe.IsConnected)pipe.Disconnect();}
            }
        }catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
        catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception){await Task.Delay(2000,token);}
    }
    protected override void OnCustomCommand(int command)
    {
        if(command!=128||Interlocked.CompareExchange(ref clearBusy,1,0)!=0)return;
        _=Task.Run(async()=>{
            string id="";
            try{
                string request=Path.Combine(BootConfiguration.Root,"clear-request.json");BootConfiguration.VerifyFile(request);
                if(new FileInfo(request).Length>4096)throw new InvalidDataException("Clear request too large");
                id=JsonSerializer.Deserialize<ClearRequest>(File.ReadAllText(request))?.Id??"";if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid clear request");
                if(history==null||reports==null)throw new IOException("Boot persistence is not ready");
                await reports.ClearAsync(()=>history.ClearAsync());var config=BootConfiguration.Load();NativeService.WriteProtectedJson(Path.Combine(BootConfiguration.Root,"clear-result.json"),new ClearResult(id,true,"Detailed history and automatic reports cleared; new monitoring continues"),config.OwnerSid);
            }catch(Exception ex){try{var config=BootConfiguration.Load();NativeService.WriteProtectedJson(Path.Combine(BootConfiguration.Root,"clear-result.json"),new ClearResult(id,false,ex.Message),config.OwnerSid);}catch(IOException){}}
            finally{Interlocked.Exchange(ref clearBusy,0);}
        });
    }
    protected override bool OnPowerEvent(PowerBroadcastStatus state){if(state is PowerBroadcastStatus.Suspend or PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend){continuity.RequestRestart("Windows power transition "+state+"; no coverage is asserted while suspended");history?.State(HistoryState.Interrupted,"Windows power transition: "+state);}return true;}
    protected override void OnStop(){RequestAdditionalTime(120000);stop.Cancel();if(captureThread!=null&&!captureThread.Join(TimeSpan.FromSeconds(120)))throw new TimeoutException("History is still draining; application files must be preserved");}
    protected override void OnShutdown()=>OnStop();
}
