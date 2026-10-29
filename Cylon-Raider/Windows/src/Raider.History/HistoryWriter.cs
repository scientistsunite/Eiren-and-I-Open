using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Raider.Core;
namespace Raider.History;

public sealed class HistoryWriter : IAsyncDisposable
{
    sealed record Work(DateTime Requested,Measurement? Frame=null,HistoryState? State=null,string? Reason=null,TaskCompletionSource? Clear=null,bool Begin=false,bool Tick=false,long Bytes=0){public long Enqueued { get; }=Stopwatch.GetTimestamp();}
    readonly Channel<Work> queue;
    readonly Task worker;
    readonly object admission=new();
    readonly string file;
    readonly int capacity;
    readonly long byteLimit;
    volatile string status="Opening local history";
    volatile bool healthy=true;
    long dropped,pendingLost,firstLost,lastLost,controlLoss,queuedBytes,peakBytes,maxDelayTicks,maxWriteTicks,maxMaintenanceTicks;
    int frameCount;
    Preferences? pendingSettings;
    public string Status=>status;
    public bool Healthy=>healthy;
    public long Dropped=>Interlocked.Read(ref dropped);
    public int QueuedFrames=>Volatile.Read(ref frameCount);
    // Conservative admission charge, not a measurement of actual managed/native heap.
    public long QueuedBytes=>Interlocked.Read(ref queuedBytes);
    public long PeakQueuedBytes=>Interlocked.Read(ref peakBytes);
    public double MaximumQueueDelayMs=>Interlocked.Read(ref maxDelayTicks)/(double)TimeSpan.TicksPerMillisecond;
    public double MaximumWriteMs=>Interlocked.Read(ref maxWriteTicks)/(double)TimeSpan.TicksPerMillisecond;
    public double MaximumMaintenanceMs=>Interlocked.Read(ref maxMaintenanceTicks)/(double)TimeSpan.TicksPerMillisecond;
    public string FilePath=>file;
    public HistoryWriter(string path,Preferences preferences,int capacity=128,Func<string,HistoryStore>? factory=null,long maximumQueuedBytes=64*1024*1024,Func<Task>? beforeMaintenance=null,string sourceKind="desktop-collector")
    {
        file=path;this.capacity=capacity;byteLimit=maximumQueuedBytes;
        queue=Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity+16){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
        worker=Task.Run(async()=>{
            HistoryStore? store=null;
            try {
                var opening=Stopwatch.StartNew();
                while(store==null)try{store=factory?.Invoke(path)??new HistoryStore(path,availableBytes:()=>new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace);}
                    catch(SqliteException ex)when(IsBusy(ex)&&opening.Elapsed<TimeSpan.FromMinutes(2)){status="Persistence delayed — database opening is contended; capture queue remains bounded";await Task.Delay(50);}
                var database=store??throw new IOException("History did not open");
                database.ConfigureProfile(new(BuildIdentity.Version,BuildIdentity.SourceRevision,sourceKind,BuildIdentity.IntegrityModel,Aggregator.RecommendedLatenessSeconds,preferences.Indefinite,preferences.HourlyReports,preferences.DetailedDiagnostics,preferences.AttributionRecordedDays));
                database.DetailedDiagnostics=preferences.DetailedDiagnostics;database.AttributionRecordedDays=preferences.AttributionRecordedDays;database.Indefinite=preferences.Indefinite;database.GenerateHourly=preferences.HourlyReports;
                status="History ready — durable batches about every two seconds";
                long reported=0,reportedControls=0;var commitClock=Stopwatch.StartNew();var maintenanceClock=Stopwatch.StartNew();var checkpointClock=Stopwatch.StartNew();
                async Task Execute(Action action,bool committing=false)
                {
                    var delay=Stopwatch.StartNew();
                    for(;;)try{action();return;}
                    catch(SqliteException ex)when(IsBusy(ex)&&(committing||!database.HasTransaction)&&delay.Elapsed<TimeSpan.FromMinutes(2)){
                        status="Persistence delayed by SQLite contention — collector continues; waiting within bounded queue";await Task.Delay(50);
                    }
                }
                while(true){
                    Work? work=null;
                    if(queue.Reader.TryRead(out var next))work=next;
                    else {
                        if(queue.Reader.Completion.IsCompleted)break;
                        using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        try{if(await queue.Reader.WaitToReadAsync(timeout.Token))queue.Reader.TryRead(out work);}catch(OperationCanceledException){}
                    }
                    if(work!=null){
                        Maximum(ref maxDelayTicks,Stopwatch.GetElapsedTime(work.Enqueued).Ticks);
                        var elapsed=Stopwatch.StartNew();
                        try{
                            var desired=Interlocked.Exchange(ref pendingSettings,null);
                            if(desired!=null){database.ConfigureProfile(new(BuildIdentity.Version,BuildIdentity.SourceRevision,sourceKind,BuildIdentity.IntegrityModel,Aggregator.RecommendedLatenessSeconds,desired.Indefinite,desired.HourlyReports,desired.DetailedDiagnostics,desired.AttributionRecordedDays));database.DetailedDiagnostics=desired.DetailedDiagnostics;database.AttributionRecordedDays=desired.AttributionRecordedDays;database.Indefinite=desired.Indefinite;database.GenerateHourly=desired.HourlyReports;}
                            long loss=Dropped,controls=Interlocked.Read(ref controlLoss);
                            if(controls!=reportedControls){await Execute(()=>database.RecordState(work.Requested,HistoryState.WriterGap,$"Lifecycle queue overflow: {controls-reportedControls} boundary events omitted"));reportedControls=controls;}
                            if(loss!=reported){long count,first,last;lock(admission){count=pendingLost;first=firstLost;last=lastLost;pendingLost=0;firstLost=lastLost=0;}if(count>0)await Execute(()=>database.RecordState(new DateTime(first,DateTimeKind.Utc),HistoryState.WriterGap,$"Persistence queue overflow: {count} measurements omitted between UTC ticks {first} and {last}; missing intervals are not zero"));reported=loss;}
                            if(work.Begin)await Execute(()=>database.Begin(work.Requested));
                            if(work.State!=null)await Execute(()=>database.RecordState(work.Requested,work.State.Value,work.Reason??work.State.Value.ToString()));
                            if(work.Frame!=null){
                                if(work.Frame.Status.Contains("SourceDiscontinuity=",StringComparison.Ordinal))await Execute(()=>database.RecordState(work.Frame.StartUtc,HistoryState.Interrupted,work.Frame.Status));
                                if(work.Frame.Status.Contains("TransportDropped=",StringComparison.Ordinal))await Execute(()=>database.RecordState(work.Frame.StartUtc,HistoryState.WriterGap,work.Frame.Status));
                                await Execute(()=>database.Append(work.Frame!));
                            }
                            if(work.Clear!=null){await Execute(database.Clear);work.Clear.TrySetResult();}
                            if(work.State!=null||work.Begin||work.Clear!=null||commitClock.Elapsed>=TimeSpan.FromSeconds(2)){
                                await Execute(database.Commit,true);commitClock.Restart();
                            }
                        }catch(Exception ex){work.Clear?.TrySetException(ex);throw;}
                        finally{if(work.Frame!=null)lock(admission){queuedBytes-=work.Bytes;frameCount--;}}
                        Maximum(ref maxWriteTicks,elapsed.Elapsed.Ticks);
                    }
                    // Never hold an idle write transaction while an export or maintenance sleeps.
                    if(commitClock.Elapsed>=TimeSpan.FromSeconds(2)){await Execute(database.Commit,true);commitClock.Restart();}
                    if(queue.Reader.Count==0&&maintenanceClock.Elapsed>=TimeSpan.FromSeconds(1)){
                        await Execute(database.Commit,true);
                        var elapsed=Stopwatch.StartNew();
                        if(beforeMaintenance!=null)await beforeMaintenance();
                        try{
                            if(database.GenerateHourly)database.FinalizeObservedHours(DateTime.UtcNow.AddSeconds(-5));
                            database.RetainStep();database.Commit();
                            if(checkpointClock.Elapsed>=TimeSpan.FromSeconds(30)){database.CheckpointPassive();checkpointClock.Restart();}
                        }catch(SqliteException ex)when(IsBusy(ex)&&!database.HasTransaction){status="Maintenance deferred by SQLite contention; persistence has priority";}
                        Maximum(ref maxMaintenanceTicks,elapsed.Elapsed.Ticks);maintenanceClock.Restart();
                    }
                    if(healthy)status=queue.Reader.Count>8?"Persistence catching up — capture continues; queued data is not missing":database.LastFreeBytes<256*1024*1024?"Logging — LOW DISK SPACE":reported+reportedControls==0?"Logging — durable local SQLite history":"Logging with explicit persistence gaps; inspect history events";
                }
                database.RecordState(DateTime.UtcNow,HistoryState.Stopped,"Application exited / history writer closed");await Execute(database.Commit,true);database.CheckpointPassive();
            }catch(Exception ex){
                healthy=false;status="NOT LOGGING: "+ex.GetType().Name+": "+ex.Message;queue.Writer.TryComplete(ex);
                while(queue.Reader.TryRead(out var item))item.Clear?.TrySetException(ex);
            }finally{store?.Dispose();}
        });
    }
    public static long EstimatedBytes(Measurement frame)=>256+192L*frame.Attribution.Length+256L*(frame.Diagnostics==null?0:1)+frame.Rows.Sum(row=>192L+2L*((row.Process?.Name.Length??0)+(row.Process?.Path?.Length??0)));
    public bool Append(Measurement frame)
    {
        if(!healthy)return false;
        long bytes=EstimatedBytes(frame);
        lock(admission){
            if(frameCount<capacity&&bytes<=byteLimit-queuedBytes&&queue.Writer.TryWrite(new(DateTime.UtcNow,Frame:frame,Bytes:bytes))){queuedBytes+=bytes;frameCount++;Maximum(ref peakBytes,queuedBytes);return true;}
            pendingLost++;long utc=frame.StartUtc.ToUniversalTime().Ticks;if(firstLost==0)firstLost=utc;lastLost=utc;Interlocked.Increment(ref dropped);
        }
        status="Persistence queue full — missing history is explicitly recorded, never zero";return false;
    }
    public void Tick()=>Control(new(DateTime.UtcNow,Tick:true));
    public void Begin()=>Control(new(DateTime.UtcNow,Begin:true));
    public void State(HistoryState state,string reason)=>Control(new(DateTime.UtcNow,State:state,Reason:reason));
    public void Configure(Preferences settings){Interlocked.Exchange(ref pendingSettings,settings);Control(new(DateTime.UtcNow));}
    void Control(Work work){if(!healthy)return;if(!queue.Writer.TryWrite(work)&& (work.State!=null||work.Begin))Interlocked.Increment(ref controlLoss);}
    public async Task ClearAsync(){if(!healthy)throw new IOException(status);var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);await queue.Writer.WriteAsync(new(DateTime.UtcNow,Clear:completion));await completion.Task;}
    public async ValueTask DisposeAsync(){queue.Writer.TryComplete();await worker;}
    static bool IsBusy(SqliteException ex)=>ex.SqliteErrorCode is 5 or 6;
    static void Maximum(ref long location,long value){long old;do{old=Interlocked.Read(ref location);if(value<=old)return;}while(Interlocked.CompareExchange(ref location,value,old)!=old);}
}
