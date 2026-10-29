using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Detector.Core;
namespace Detector.History;

public sealed class HistoryWriter : IAsyncDisposable
{
    sealed record Work(DateTime Requested,Measurement? Frame=null,HistoryState? State=null,string? Reason=null,TaskCompletionSource? Clear=null,bool Begin=false,bool Tick=false,long Bytes=0,PrivacySelection? Selection=null){public long Enqueued { get; }=Stopwatch.GetTimestamp();}
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
    readonly PrivacyWindow privacy=new();
    volatile Preferences privacyOptions=new();
    public string PrivacyStatus{get{lock(admission)return privacy.Status+" Disk mode: "+(privacyOptions.ContinuousNetwork?"continuous network seconds.":"sensor-triggered network seconds.")+(Healthy?"":" NOT LOGGING: rolling RAM context continues but disk persistence is unavailable.");}}
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
        privacyOptions=preferences;file=path;this.capacity=capacity;byteLimit=maximumQueuedBytes;
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
                async Task RecordLifecycle(DateTime utc,HistoryState state,string reason)
                {
                    await Execute(()=>database.RecordState(utc,state,reason));
                    await Execute(()=>database.AppendDetectorActivity(new(utc,utc,"Lifecycle / coverage","Monitoring runtime",state.ToString(),reason+"; only observed coverage is recorded. RAM prehistory is unavailable before this runtime started.")));
                }
                await RecordLifecycle(DateTime.UtcNow,HistoryState.Recovery,"History writer started; startup/restart can leave unavailable intervals before observation resumes");
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
                            if(desired!=null){database.ConfigureProfile(new(BuildIdentity.Version,BuildIdentity.SourceRevision,sourceKind,BuildIdentity.IntegrityModel,Aggregator.RecommendedLatenessSeconds,desired.Indefinite,desired.HourlyReports,desired.DetailedDiagnostics,desired.AttributionRecordedDays));database.DetailedDiagnostics=desired.DetailedDiagnostics;database.AttributionRecordedDays=desired.AttributionRecordedDays;database.Indefinite=desired.Indefinite;database.GenerateHourly=desired.HourlyReports;await Execute(()=>database.AppendDetectorActivity(new(work.Requested,work.Requested,"Configuration","Cylon Detector","Settings applied",$"Continuous network persistence={desired.ContinuousNetwork}; report interval={desired.ReportIntervalMinutes} minutes; reports enabled={desired.HourlyReports}; indefinite retention={desired.Indefinite}. Five-minute RAM context remains enabled.")));}
                            long loss=Dropped,controls=Interlocked.Read(ref controlLoss);
                            if(controls!=reportedControls){await RecordLifecycle(work.Requested,HistoryState.WriterGap,$"Lifecycle queue overflow: {controls-reportedControls} boundary events omitted");reportedControls=controls;}
                            if(loss!=reported){long count,first,last;lock(admission){count=pendingLost;first=firstLost;last=lastLost;pendingLost=0;firstLost=lastLost=0;}if(count>0)await RecordLifecycle(new DateTime(first,DateTimeKind.Utc),HistoryState.WriterGap,$"Persistence queue overflow: disk-queue admission rejected {count} measurements between UTC ticks {first} and {last}; recoverable RAM context may later persist. This admission rejection does not establish permanent loss; inspect retained coverage and RAM-recovery observations. Missing intervals are not zero");reported=loss;}
                            if(work.Begin){await Execute(()=>database.Begin(work.Requested));await Execute(()=>database.AppendDetectorActivity(new(work.Requested,work.Requested,"Lifecycle / coverage","Monitoring runtime","Monitoring requested","New monitoring session; earlier unobserved time is not zero. Existing RAM context remains limited to this runtime.")));}
                            if(work.State!=null)await RecordLifecycle(work.Requested,work.State.Value,work.Reason??work.State.Value.ToString());
                            if(work.Frame!=null){
                                if(work.Frame.Status.Contains("SourceDiscontinuity=",StringComparison.Ordinal))await RecordLifecycle(DateTime.UtcNow,HistoryState.Interrupted,work.Frame.Status);
                                if(work.Frame.Status.Contains("TransportDropped=",StringComparison.Ordinal))await RecordLifecycle(DateTime.UtcNow,HistoryState.WriterGap,work.Frame.Status);
                                if(work.Selection is {} selected){
                                    foreach(var activity in selected.Activities)await Execute(()=>database.AppendDetectorActivity(activity));
                                    foreach(var selectedFrame in selected.Frames){await Execute(()=>database.Append(selectedFrame));await Execute(()=>database.ObserveDetectorSecond(selectedFrame));}
                                }
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
                    if(healthy)status=queue.Reader.Count>8?"Persistence catching up — capture continues; queued data is not missing":database.LastFreeBytes<256*1024*1024?"Logging — LOW DISK SPACE":reported+reportedControls==0?"Logging — durable local SQLite history":"Logging with explicit disk-queue admission rejections; inspect retained coverage and RAM recovery";
                }
                PrivacySelection final;
                lock(admission){final=privacy.Finish(privacyOptions.ContinuousNetwork);privacy.Accepted(final);}
                foreach(var activity in final.Activities)await Execute(()=>database.AppendDetectorActivity(activity));
                foreach(var frame in final.Frames){await Execute(()=>database.Append(frame));await Execute(()=>database.ObserveDetectorSecond(frame));}
                if(final.Frames.Length>0){var first=final.Frames.Min(frame=>frame.StartUtc.ToUniversalTime());var last=final.Frames.Max(frame=>frame.StartUtc.ToUniversalTime());await Execute(()=>database.AppendDetectorActivity(new(DateTime.UtcNow,first,"Persistence / coverage","Network RAM context","Deferred RAM context persisted",$"Orderly shutdown persisted {final.Frames.Length:N0} deferred network records from UTC {first:O} through {last:O}. Recent RAM context may recover earlier disk-queue admission rejections; this does not recover unobserved data or permanent resource-budget omissions.")));}
                await RecordLifecycle(DateTime.UtcNow,HistoryState.Stopped,"Application exited / history writer closed; pending RAM-only observations end here");await Execute(database.Commit,true);database.CheckpointPassive();
            }catch(Exception ex){
                healthy=false;status="NOT LOGGING: "+ex.GetType().Name+": "+ex.Message;queue.Writer.TryComplete(ex);
                while(queue.Reader.TryRead(out var item))item.Clear?.TrySetException(ex);
            }finally{store?.Dispose();}
        });
    }
    public static long EstimatedBytes(Measurement frame)=>256+(frame.Sensors is {} sensors?256L+2L*sensors.Status.Length+sensors.Uses.Sum(use=>192L+2L*(use.Kind.Length+use.Application.Length+(use.Path?.Length??0)+use.Evidence.Length+use.Scope.Length)):0)+192L*frame.Attribution.Length+256L*(frame.Diagnostics==null?0:1)+frame.Rows.Sum(row=>192L+2L*((row.Process?.Name.Length??0)+(row.Process?.Path?.Length??0)));
    public bool Append(Measurement frame)
    {
        lock(admission){
            var selection=privacy.Observe(frame,privacyOptions.ContinuousNetwork);
            if(!healthy)return false;
            long available=byteLimit-queuedBytes;long bytes=EstimatedBytes(frame);
            var activities=new List<DetectorActivityRow>();foreach(var item in selection.Activities){long charge=256L+2L*(item.Detail.Length+item.Application.Length);if(bytes+charge>available)break;activities.Add(item);bytes+=charge;}
            var frames=new List<Measurement>();foreach(var selectedFrame in selection.Frames){long charge=ReferenceEquals(selectedFrame,frame)?0:EstimatedBytes(selectedFrame);if(bytes+charge>available)break;frames.Add(selectedFrame);bytes+=charge;}
            var admitted=new PrivacySelection(frames.ToArray(),activities.ToArray());
            if(frameCount<capacity&&bytes<=available&&queue.Writer.TryWrite(new(DateTime.UtcNow,Frame:frame,Bytes:bytes,Selection:admitted))){privacy.Accepted(admitted);queuedBytes+=bytes;frameCount++;Maximum(ref peakBytes,queuedBytes);return true;}
            pendingLost++;long utc=frame.StartUtc.ToUniversalTime().Ticks;if(firstLost==0)firstLost=utc;lastLost=utc;Interlocked.Increment(ref dropped);
        }
        status="Persistence queue full — disk-queue admission rejected; recent RAM context may later persist. Unobserved gaps remain explicit, never zero";return false;
    }
    public void Tick()=>Control(new(DateTime.UtcNow,Tick:true));
    public void Begin()=>Control(new(DateTime.UtcNow,Begin:true));
    public void State(HistoryState state,string reason)=>Control(new(DateTime.UtcNow,State:state,Reason:reason));
    public void Configure(Preferences settings){privacyOptions=settings;Interlocked.Exchange(ref pendingSettings,settings);Control(new(DateTime.UtcNow));}
    void Control(Work work){if(!healthy)return;if(!queue.Writer.TryWrite(work)&& (work.State!=null||work.Begin))Interlocked.Increment(ref controlLoss);}
    public async Task ClearAsync(){lock(admission)privacy.Clear();if(!healthy)throw new IOException(status);var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);await queue.Writer.WriteAsync(new(DateTime.UtcNow,Clear:completion));await completion.Task;}
    public async ValueTask DisposeAsync(){queue.Writer.TryComplete();await worker;}
    static bool IsBusy(SqliteException ex)=>ex.SqliteErrorCode is 5 or 6;
    static void Maximum(ref long location,long value){long old;do{old=Interlocked.Read(ref location);if(value<=old)return;}while(Interlocked.CompareExchange(ref location,value,old)!=old);}
}
