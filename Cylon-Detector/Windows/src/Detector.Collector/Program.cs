using System.IO.Pipes;
using System.Text.Json;
using System.Security.Principal;
using Detector.Core;
using Detector.Engine;

if((args.Length!=1&&!(args.Length==2&&args[1]=="--diagnostic-details"))||!args[0].StartsWith("CylonDetector-",StringComparison.Ordinal))return 2;
using var singleton=new Mutex(true,@"Global\Eiren.CylonDetector.Collector.v01",out bool owned);
if(!owned)return 3;
try{
    using var pipe=new NamedPipeClientStream(".",args[0],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Anonymous);
    await pipe.ConnectAsync(25000);using var cancel=new CancellationTokenSource();using var sendCancel=new CancellationTokenSource();
    using var writer=new StreamWriter(pipe){AutoFlush=true};
    // UAC may run under alternate administrator credentials. Observe the actual
    // requesting UI account, proven by the connected local pipe server token.
    string sensorOwner=PipeRequesterIdentity.OwnerSid(pipe);
    using var collector=new EtwCollector(args.Length==2,sensorOwner);
    using var lifecycle=new SemaphoreSlim(1);var pending=new MeasurementBuffer();var continuity=new SourceContinuity();
    collector.Measured+=frame=>{var safe=continuity.Observe(frame);if(safe!=null)pending.TryWrite(safe);};
    var send=Task.Run(async()=>{
        long reported=0;
        await foreach(var value in pending.ReadAll(sendCancel.Token)){
            var m=value;long lost=pending.Dropped;if(lost!=reported){m=m with{Status=m.Status+" | TransportDropped="+(lost-reported)};reported=lost;}
            string line=JsonSerializer.Serialize(m);
            if(line.Length>2_000_000)line=JsonSerializer.Serialize(new Measurement(1,m.StartUtc,0,false,m.LostEvents,m.LateEvents,[],"Transport frame exceeds bounded line limit; measurements unavailable"));
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(sendCancel.Token);timeout.CancelAfter(TimeSpan.FromMinutes(2));await writer.WriteLineAsync(line.AsMemory(),timeout.Token);
        }
    });
    collector.Start();
    var watchdog=Task.Run(async()=>{
        try{
            while(!cancel.IsCancellationRequested){
                await Task.Delay(1000,cancel.Token);WindowsTimeZone.RefreshIfChanged();
                if(!continuity.RestartRequested&&!continuity.HeartbeatExpired)continue;
                await lifecycle.WaitAsync(cancel.Token);
                try{collector.Stop();cancel.Token.ThrowIfCancellationRequested();if(!continuity.RestartRequested)continuity.RequestRestart("Collector heartbeat interrupted; missing period remains unavailable");continuity.ResetAfterRestart();collector.Start();}
                finally{lifecycle.Release();}
            }
        }catch(OperationCanceledException)when(cancel.IsCancellationRequested){}
    });
    bool graceful=false,drainFailed=false;
    try{
        var command=new byte[1];var receive=pipe.ReadAsync(command,cancel.Token).AsTask();var completed=await Task.WhenAny(receive,send,watchdog);
        if(completed==send)await send;else if(completed==watchdog)await watchdog;
        else if(await receive!=0){if(command[0]!=1)throw new IOException("Unexpected collector command");graceful=true;}
    }finally{
        cancel.Cancel();await lifecycle.WaitAsync();try{collector.Stop();if(!collector.IsStopped){graceful=false;drainFailed=true;}}finally{lifecycle.Release();}
        pending.Complete();
        // Command1 requests orderly stop. Keep the reader alive until queued frames,
        // including known partial tail bytes, are delivered. A distinct acknowledgement
        // makes a crash/timeout EOF distinguishable from a successful bounded drain.
        try{if(graceful){await send.WaitAsync(TimeSpan.FromSeconds(10));using var ackTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));await writer.WriteLineAsync("CylonDetector.StopComplete.v1".AsMemory(),ackTimeout.Token);}}
        catch(Exception ex)when(ex is IOException or OperationCanceledException or TimeoutException or ObjectDisposedException){drainFailed=true;}
        // StreamWriter must finish/dispose before its underlying pipe. Closing the
        // pipe here makes its later flush throw during otherwise orderly shutdown.
        finally{sendCancel.Cancel();}
        try{await send;}catch(Exception ex)when(ex is IOException or OperationCanceledException or ObjectDisposedException){}
        await watchdog;
    }
    return drainFailed?4:0;
}catch(Exception ex)when(ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Security.SecurityException){return 4;}
