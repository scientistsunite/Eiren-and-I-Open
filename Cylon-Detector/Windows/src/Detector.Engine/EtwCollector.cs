using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Detector.Core;
namespace Detector.Engine;

public sealed class EtwCollector(bool detailedDiagnostics=false,string? sensorOwnerSid=null) : IMeasurementSource
{
    readonly SensorObserver sensors=new(sensorOwnerSid);
    int detailsEnabled=detailedDiagnostics?1:0;
    public bool DetailedDiagnostics {get=>Volatile.Read(ref detailsEnabled)!=0;set=>Volatile.Write(ref detailsEnabled,value?1:0);}
    readonly object gate=new();
    readonly Dictionary<int,(Identity Identity,SafeProcessHandle Handle,AttributionReason Reason)> processes=new();
    static readonly double[] DelayBounds=[1,10,50,100,250,500,1000,2000,3000,4000];
    readonly long[] delayBins=new long[11];
    long unsupported;
    long events,malformed,unavailable,maximumDeliveryTicks,maxCallback,maxWait,maxResolve;int invalidateIdentities;
    public CollectorDiagnostics Diagnostics=>new(Aggregator.RecommendedLatenessSeconds,Interlocked.Read(ref events),Interlocked.Read(ref malformed),aggregate?.CapacityRejected??0,aggregate?.DiscardedIncoming??0,aggregate?.DiscardedOutgoing??0,Interlocked.Read(ref unavailable),Enumerable.Range(0,delayBins.Length).Select(i=>Interlocked.Read(ref delayBins[i])).ToArray(),Interlocked.Read(ref maximumDeliveryTicks)/(double)TimeSpan.TicksPerMillisecond,Interlocked.Read(ref maxCallback)*1000d/Stopwatch.Frequency,Interlocked.Read(ref maxWait)*1000d/Stopwatch.Frequency,Interlocked.Read(ref maxResolve)*1000d/Stopwatch.Frequency,Interlocked.Read(ref unsupported));
    TraceEventSession? session;
    Task? consumer, ticker;
    CancellationTokenSource? cancel;
    Aggregator? aggregate;string sourceSession="";
    public bool IsStopped=>session==null&&consumer?.IsCompleted!=false&&ticker?.IsCompleted!=false&&sensors.IsStopped;
    public event Action<Measurement>? Measured;
    // Optional local diagnostic observer; raw payload PID also covers already-exited processes.
    public event Action<int,long>? NetworkObserved;
    public void Start()
    {
        if(session!=null||consumer?.IsCompleted==false||ticker?.IsCompleted==false||!sensors.IsStopped) throw new InvalidOperationException("Already collecting or stopping");
        ClearIdentities();
        sourceSession=Guid.NewGuid().ToString("N");Array.Clear(delayBins);unsupported=events=malformed=unavailable=maximumDeliveryTicks=maxCallback=maxWait=maxResolve=0;invalidateIdentities=0;
        aggregate=new(DateTime.UtcNow,Aggregator.RecommendedLatenessSeconds){DetailedDiagnostics=DetailedDiagnostics}; cancel=new();
        try {
            sensors.Start();
            session=new TraceEventSession("Eiren-CylonDetector-"+Guid.NewGuid().ToString("N"));
            session.StopOnDispose=true;
            session.BufferSizeMB=16;
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP|KernelTraceEventParser.Keywords.Process);
            var activeSession=session;var activeToken=cancel.Token;string activeSource=sourceSession;
            var k=activeSession.Source.Kernel;
            k.ProcessStart+=e=>Forget(e.ProcessID);
            k.ProcessStop+=e=>Forget(e.ProcessID);
            k.TcpIpSend+=e=>Accept(e,Direction.Outgoing,TrafficProtocol.TcpIpv4);
            k.TcpIpRecv+=e=>Accept(e,Direction.Incoming,TrafficProtocol.TcpIpv4);
            k.TcpIpSendIPV6+=e=>Accept(e,Direction.Outgoing,TrafficProtocol.TcpIpv6);
            k.TcpIpRecvIPV6+=e=>Accept(e,Direction.Incoming,TrafficProtocol.TcpIpv6);
            k.UdpIpSend+=e=>Accept(e,Direction.Outgoing,TrafficProtocol.UdpIpv4);
            k.UdpIpRecv+=e=>Accept(e,Direction.Incoming,TrafficProtocol.UdpIpv4);
            k.UdpIpSendIPV6+=e=>Accept(e,Direction.Outgoing,TrafficProtocol.UdpIpv6);
            k.UdpIpRecvIPV6+=e=>Accept(e,Direction.Incoming,TrafficProtocol.UdpIpv6);
            consumer=Task.Run(()=>activeSession.Source.Process());var processing=consumer;
            ticker=Task.Run(async()=>{
                long priorLoss=0;
                try {
                    using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
                    while(await timer.WaitForNextTickAsync(activeToken)) {
                        Measurement[] frames;
                        lock(gate) {
                            long loss=activeSession.Source.EventsLost;
                            aggregate.Loss(Math.Max(0,loss-priorLoss),DateTime.UtcNow);
                            if(loss!=priorLoss)Interlocked.Exchange(ref invalidateIdentities,1);
                            priorLoss=loss;
                            frames=aggregate.Advance(DateTime.UtcNow);
                        }
                        foreach(var frame in frames) Measured?.Invoke(frame with{Diagnostics=Diagnostics,SourceSession=activeSource,Sensors=sensors.Snapshot()});
                        if(processing.IsCompleted) throw new IOException("ETW consumer interrupted",processing.Exception);
                    }
                } catch(OperationCanceledException) {} catch(Exception ex) {
                    try { Measured?.Invoke(new Measurement(1,DateTime.UtcNow,0,false,aggregate.LostEvents,aggregate.LateEvents,[],"Collector interrupted: "+ex.Message){SourceSession=activeSource,Diagnostics=Diagnostics,Sensors=sensors.Snapshot()}); } finally { activeSession.Dispose(); }
                }
            });
        } catch { Stop(); throw; }
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(SafeProcessHandle process,uint milliseconds);
    void Forget(int pid){if(processes.Remove(pid,out var entry))entry.Handle.Dispose();}
    void ClearIdentities(){foreach(var entry in processes.Values)entry.Handle.Dispose();processes.Clear();}
    static AttributionReason ProcessFailure(int code)=>code switch{5=>AttributionReason.AccessDenied,87 or 1168=>AttributionReason.ProcessExited,_=>AttributionReason.Unknown};
    (Identity? Identity,AttributionReason Reason) Resolve(int pid,DateTime stamp)
    {
        if(pid<=0)return (null,AttributionReason.MissingPid);
        if(Interlocked.Exchange(ref invalidateIdentities,0)!=0)ClearIdentities();
        if(processes.TryGetValue(pid,out var cached)){
            // The cached process HANDLE identifies an instance, even after Windows reuses its PID.
            try{if(WaitForSingleObject(cached.Handle,0)==258)return cached.Identity.StartUtcTicks<=stamp.Ticks?(cached.Identity,cached.Reason):(null,AttributionReason.IdentityAmbiguous);}
            catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){}
            Forget(pid);
        }
        Process? process=null;SafeProcessHandle? instance=null;
        try{
            instance=OpenProcess(0x1000|0x100000,false,pid); // QUERY_LIMITED_INFORMATION | SYNCHRONIZE; never ALL_ACCESS.
            if(instance.IsInvalid)return (null,ProcessFailure(Marshal.GetLastWin32Error()));
            if(!GetProcessTimes(instance,out long created,out _,out _,out _))return (null,Marshal.GetLastWin32Error()==5?AttributionReason.AccessDenied:AttributionReason.Unknown);
            var start=DateTime.FromFileTimeUtc(created);process=Process.GetProcessById(pid);
            if(process.StartTime.ToUniversalTime()!=start)return (null,AttributionReason.IdentityAmbiguous);uint state=WaitForSingleObject(instance,0);if(state==0)return (null,AttributionReason.ProcessExited);if(state!=258)return (null,AttributionReason.Unknown);if(start>stamp)return (null,AttributionReason.IdentityAmbiguous);
            string? path=null;try{path=process.MainModule?.FileName;}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){}
            var reason=path==null?AttributionReason.PathUnavailable:AttributionReason.None;string name="Process name unavailable";
            try{name=process.ProcessName;}catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){reason=ex is System.ComponentModel.Win32Exception win&&win.NativeErrorCode==5?AttributionReason.AccessDenied:AttributionReason.Unknown;}
            var identity=new Identity(pid,start.Ticks,name,path);
            if(processes.Count<2048){processes[pid]=(identity,instance,reason);instance=null;}
            return (identity,reason);
        }catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return (null,ex is System.ComponentModel.Win32Exception win?ProcessFailure(win.NativeErrorCode):AttributionReason.ProcessExited);}
        finally{process?.Dispose();instance?.Dispose();}
    }
    void Accept(TraceEvent e,Direction direction,TrafficProtocol protocol)
    {
        long beginning=Stopwatch.GetTimestamp();DateTime received=DateTime.UtcNow,stamp=e.TimeStamp.ToUniversalTime();
        long delay=Math.Max(0,(received-stamp).Ticks);Maximum(ref maximumDeliveryTicks,delay);
        double ms=delay/(double)TimeSpan.TicksPerMillisecond;int bin=0;while(bin<DelayBounds.Length&&ms>DelayBounds[bin])bin++;Interlocked.Increment(ref delayBins[bin]);Interlocked.Increment(ref events);
        try{
            // Preserve the tested event subscriptions and payload-PID rule. Metadata work
            // is outside the aggregation lock, so process inspection cannot stall the ticker.
            if(e.Version is <2 or >3){Interlocked.Increment(ref unsupported);lock(gate)aggregate!.UnsupportedEvent(stamp,received);return;}
            int minimum=protocol is TrafficProtocol.TcpIpv6 or TrafficProtocol.UdpIpv6?44:20;
            if(e.EventDataLength<minimum){Interlocked.Increment(ref malformed);lock(gate)aggregate!.MalformedEvent(stamp,received);return;}
            int bytes=Marshal.ReadInt32(e.DataStart,4);if(bytes<0){Interlocked.Increment(ref malformed);lock(gate)aggregate!.MalformedEvent(stamp,received);return;}
            int pid=Marshal.ReadInt32(e.DataStart);NetworkObserved?.Invoke(pid,bytes);
            long resolving=Stopwatch.GetTimestamp();var resolution=Resolve(pid,stamp);Identity? identity=resolution.Identity;Maximum(ref maxResolve,Stopwatch.GetTimestamp()-resolving);if(identity==null)Interlocked.Increment(ref unavailable);
            long waiting=Stopwatch.GetTimestamp();lock(gate){Maximum(ref maxWait,Stopwatch.GetTimestamp()-waiting);aggregate!.DetailedDiagnostics=DetailedDiagnostics;aggregate.Add(new(stamp,identity,direction,bytes,received,resolution.Reason,pid>0?pid:null,protocol));}
        }finally{Maximum(ref maxCallback,Stopwatch.GetTimestamp()-beginning);}
    }
    static void Maximum(ref long location,long value){long old;do{old=Interlocked.Read(ref location);if(value<=old)return;}while(Interlocked.CompareExchange(ref location,value,old)!=old);}
    public void Stop()
    {
        cancel?.Cancel(); session?.Dispose();sensors.Stop();
        try { Task.WaitAll(new[]{consumer,ticker}.OfType<Task>().ToArray(),TimeSpan.FromSeconds(5)); } catch(AggregateException) {}
        // A deadline is not proof a worker exited. Keep unfinished task references so
        // Start refuses to replace state that an old callback could still access.
        if(consumer?.IsCompleted!=false&&ticker?.IsCompleted!=false&&aggregate!=null){
            Measurement[] tail;lock(gate)tail=aggregate.DrainKnown();
            foreach(var frame in tail)Measured?.Invoke(frame with{Diagnostics=Diagnostics,SourceSession=sourceSession,Sensors=sensors.Snapshot()});
        }
        if(consumer?.IsCompleted!=false){ClearIdentities();consumer=null;}
        if(ticker?.IsCompleted!=false)ticker=null;
        session=null;cancel?.Dispose();cancel=null;
    }
    public void Dispose()=>Stop();
}
