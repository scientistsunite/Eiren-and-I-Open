using System.Globalization;
using Microsoft.Data.Sqlite;
using Detector.Core;
namespace Detector.History;

public sealed record DetectorActivityRow(DateTime ObservedUtc,DateTime? ActivityUtc,string Category,string Application,string Change,string Detail);
public sealed record PrivacySelection(Measurement[] Frames,DetectorActivityRow[] Activities);

// Admission-side rolling cache: persistence stalls never prevent refreshing the five-minute RAM window.
// Sensor timestamps are evidence timestamps; windows begin at the time a changed snapshot is observed.
public sealed class PrivacyWindow
{
    public const long MaximumBufferBytes=128L*1024*1024;
    readonly List<Measurement> buffer=[];
    readonly HashSet<string> buffered=[],queued=[],retiredSources=[];
    string? activeSource;
    readonly Dictionary<string,SensorUse> uses=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> familiar=new(StringComparer.OrdinalIgnoreCase);
    readonly List<DetectorActivityRow> pending=[];
    DateTime lastSnapshot=DateTime.MinValue,afterFrom=DateTime.MaxValue;
    DateTime frontier=DateTime.MinValue,recordFrom=DateTime.MaxValue,recordUntil=DateTime.MinValue;
    double average;int samples;long bufferBytes,minuteRx,minuteTx,minuteSamples,cacheOmissions;DateTime cacheIncompleteUntil=DateTime.MinValue;DateTime minuteStart=DateTime.MinValue;string sensorStatus="Not yet observed";long omitted;
    public int BufferedSeconds=>buffer.Count;
    public long BufferedBytes=>bufferBytes;
    public bool HasActiveSensors=>uses.Values.Any(use=>use.StoppedUtcTicks is null or <=0);
    public string Status=>$"Five-minute RAM context (+bounded delivery overlap): {buffer.Count:N0} one-second records, estimated {BufferedBytes/1048576d:F1} MiB. "+(frontier<cacheIncompleteUntil?$"RAM context incomplete: {cacheOmissions} resource-budget omissions remain within the recent horizon. ":"")+(recordUntil>frontier?"Sensor-triggered network capture window is open. ":"No current sensor-triggered network capture window; continuous persistence is a separate option. ")+(HasActiveSensors?"A prior sensor marker remains unresolved; stale/unavailable observations do not prove present hardware use or inactivity. ":"")+"Sensor evidence: "+sensorStatus;
    static string Key(Measurement frame)=>(frame.SourceSession??"legacy")+":"+frame.StartUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    static string UseKey(SensorUse use)=>use.Scope+"|"+use.Kind+"|"+(use.Path??use.Application)+"|"+use.StartedUtcTicks;
    void Event(DetectorActivityRow item){if(pending.Count<4096)pending.Add(item);else omitted++;}
    public PrivacySelection Observe(Measurement frame,bool continuous)
    {
        DateTime time=frame.StartUtc.ToUniversalTime();
        string source=frame.SourceSession??"legacy";
        if(activeSource!=null&&source!=activeSource){
            if(retiredSources.Contains(source)){Event(new(DateTime.UtcNow,time,"Coverage","Collector","Retired source frame omitted","Late frame from a prior collector source cannot reopen an earlier clock era; no zero coverage is fabricated."));return Select(continuous);}
            retiredSources.Add(activeSource);if(retiredSources.Count>64){retiredSources.Clear();Event(new(DateTime.UtcNow,time,"Coverage","Collector","Source baseline reset","64 retired source identities exceeded bounded tracking; stale-source detection restarted."));}
            if(frontier!=DateTime.MinValue&&time<frontier.AddMinutes(-5)){
                int lost=buffer.Count(item=>!queued.Contains(Key(item)));buffer.Clear();buffered.Clear();queued.Clear();uses.Clear();bufferBytes=0;recordFrom=DateTime.MaxValue;recordUntil=frontier=DateTime.MinValue;lastSnapshot=DateTime.MinValue;afterFrom=DateTime.MaxValue;minuteStart=DateTime.MinValue;minuteRx=minuteTx=minuteSamples=0;
                Event(new(DateTime.UtcNow,time,"Coverage","Collector","Clock/source boundary reset",$"New collector source moved event time backward. Five-minute RAM ordering restarted; {lost} previously unqueued frames cannot be recovered across this boundary. Prior disk history remains. Missing prehistory is not zero."));
            }
        }
        activeSource=source;
        if(frontier!=DateTime.MinValue&&time<frontier-TimeSpan.FromMinutes(5)){
            Event(new(DateTime.UtcNow,time,"Coverage","Collector","Stale/clock-shifted network frame","Frame is outside the live five-minute event-time window; no current coverage is fabricated."));
            return Select(continuous);
        }
        if(afterFrom!=DateTime.MaxValue&&!HasActiveSensors&&time>=recordUntil){var tail=buffer.Where(item=>item.StartUtc>=afterFrom&&item.StartUtc<recordUntil).ToArray();Event(new(DateTime.UtcNow,recordUntil,"Network correlation","All observed applications","After sensor observation",$"Observed post-activity context: {tail.Length} received buckets, download {tail.Sum(item=>item.Rows.Sum(row=>row.Incoming)):N0} B; upload {tail.Sum(item=>item.Rows.Sum(row=>row.Outgoing)):N0} B. Coverage may be incomplete; concurrent traffic does not prove transmitted audio/video or causation."));afterFrom=DateTime.MaxValue;}
        if(time>frontier)frontier=time;
        DateTime cutoff=frontier.AddSeconds(-Aggregator.RecommendedLatenessSeconds)-TimeSpan.FromMinutes(5);
        foreach(var old in buffer.Where(old=>old.StartUtc.ToUniversalTime()<cutoff).ToArray()){buffer.Remove(old);bufferBytes-=HistoryWriter.EstimatedBytes(old);buffered.Remove(Key(old));queued.Remove(Key(old));}
        if(!buffered.Add(Key(frame)))return Select(continuous);
        buffer.Add(frame);bufferBytes+=HistoryWriter.EstimatedBytes(frame);
        long omittedFrames=0;while(buffer.Count>512||bufferBytes>MaximumBufferBytes){var old=buffer[0];buffer.RemoveAt(0);bufferBytes-=HistoryWriter.EstimatedBytes(old);buffered.Remove(Key(old));queued.Remove(Key(old));omittedFrames++;}
        if(omittedFrames>0){cacheOmissions+=omittedFrames;cacheIncompleteUntil=frontier.AddMinutes(5);Event(new(DateTime.UtcNow,time,"Coverage","Five-minute RAM cache","Resource-budget omission",$"{omittedFrames} oldest frames omitted at the 128 MiB / 512-frame cache budget. Requested five-minute context is incomplete; missing activity is not zero."));sensorStatus="RAM context incomplete at resource budget; "+sensorStatus;}
        DateTime minute=new(time.Ticks-time.Ticks%TimeSpan.TicksPerMinute,DateTimeKind.Utc);
        if(minuteStart!=DateTime.MinValue&&minute!=minuteStart){Event(new(DateTime.UtcNow,minuteStart,"Network aggregate","All observed applications","Minute-level context",$"Observed aggregate download {minuteRx:N0} B; upload {minuteTx:N0} B across {minuteSamples} received one-second buckets. This aggregate is not a complete second-by-second recording outside selected capture windows; unavailable seconds are not zero."));minuteRx=minuteTx=minuteSamples=0;}
        minuteStart=minute;minuteRx+=frame.Rows.Sum(row=>row.Incoming);minuteTx+=frame.Rows.Sum(row=>row.Outgoing);minuteSamples++;
        if(frame.Sensors is {} snapshot){
            sensorStatus=snapshot.Status;
            foreach(var use in snapshot.Uses){
                if(use.StartedUtcTicks<=0||use.StartedUtcTicks>DateTime.MaxValue.Ticks)continue;
                string key=UseKey(use);bool active=use.StoppedUtcTicks is null or <=0;
                bool existed=uses.TryGetValue(key,out var prior);
                if(!existed){
                    foreach(var superseded in uses.Where(pair=>pair.Key!=key&&pair.Value.Kind==use.Kind&&pair.Value.Scope==use.Scope&&string.Equals(pair.Value.Path??pair.Value.Application,use.Path??use.Application,StringComparison.OrdinalIgnoreCase)&&pair.Value.StartedUtcTicks<use.StartedUtcTicks).Select(pair=>pair.Key).ToArray()){
                        var priorUse=uses[superseded];if(priorUse.StoppedUtcTicks is null or <=0)Event(new(snapshot.ObservedUtc,null,"Coverage",use.Application,"Earlier activity marker superseded","A later Windows start marker replaced an earlier unresolved marker; its actual stop time is unknown."));uses.Remove(superseded);
                    }
                    if(uses.Count>=4096){Event(new(snapshot.ObservedUtc,null,"Coverage","Sensor baseline","Sensor identity capacity reached","4,096 tracked sensor identities reached; newly observed evidence is recorded but active-state tracking is incomplete."));continue;}
                    uses[key]=use;
                    // Initial ended registry records are historical evidence, not fresh live transitions.
                    Event(new(snapshot.ObservedUtc,new DateTime(use.StartedUtcTicks,DateTimeKind.Utc),use.Kind,use.Application,active?"Observed active":"Historical use evidence",$"{use.Evidence}; scope={use.Scope}; path={use.Path??"unavailable"}; stop={use.StoppedUtcTicks?.ToString()??"not established"}. Windows evidence may omit unsupported/direct device access."));
                    if(active)Trigger(snapshot.ObservedUtc);
                    else if(lastSnapshot!=DateTime.MinValue&&use.StoppedUtcTicks>lastSnapshot.Ticks){Trigger(snapshot.ObservedUtc,new DateTime(use.StoppedUtcTicks!.Value,DateTimeKind.Utc).AddMinutes(5));afterFrom=new DateTime(use.StoppedUtcTicks.Value,DateTimeKind.Utc);}
                }else if(prior!.StoppedUtcTicks!=use.StoppedUtcTicks){
                    uses[key]=use;Event(new(snapshot.ObservedUtc,use.StoppedUtcTicks>0&&use.StoppedUtcTicks<=DateTime.MaxValue.Ticks?new DateTime(use.StoppedUtcTicks.Value,DateTimeKind.Utc):null,use.Kind,use.Application,active?"Activity changed":"Observed stopped",use.Evidence+"; unavailable or omitted evidence is never treated as a stop."));Trigger(snapshot.ObservedUtc,active?null:new DateTime(use.StoppedUtcTicks!.Value,DateTimeKind.Utc).AddMinutes(5));if(!active)afterFrom=new DateTime(use.StoppedUtcTicks!.Value,DateTimeKind.Utc);
                }
            }
            if(!snapshot.Available)EventOnStatus(snapshot);
            else if(lastUnavailable!=null){Event(new(snapshot.ObservedUtc,null,"Coverage","Microphone / camera","Observation available",snapshot.Status));lastUnavailable=null;}
            lastSnapshot=snapshot.ObservedUtc;
            bool fresh=snapshot.ObservedUtc>=time.AddSeconds(-1)&&snapshot.ObservedUtc<=time.AddSeconds(Aggregator.RecommendedLatenessSeconds+2);
            if(fresh&&snapshot.Uses.Any(use=>use.StoppedUtcTicks is null or <=0))recordUntil=Max(recordUntil,snapshot.ObservedUtc.AddMinutes(5));
            // Keep no more than the recent observation horizon for ended sensor identities.
            foreach(var key in uses.Where(pair=>pair.Value.StoppedUtcTicks is >0&&pair.Value.StoppedUtcTicks<cutoff.AddMinutes(-10).Ticks).Select(pair=>pair.Key).ToArray())uses.Remove(key);
        }
        // Missing sensor snapshots do not silently stop a previously observed session.

        foreach(var row in frame.Rows){
            string identity=row.Process?.Path??(row.Process is {} p?$"PID {p.Pid} created {p.StartUtcTicks}":"unattributed");
            if(familiar.Add(identity)){
                Event(new(DateTime.UtcNow,time,"Anomaly observation",row.Process?.Name??"Unattributed","First observed in this monitoring session",$"{identity}; observed download {row.Incoming} B, upload {row.Outgoing} B. Unfamiliar does not imply a threat."));
                if(familiar.Count>8192){familiar.Clear();Event(new(DateTime.UtcNow,time,"Coverage","Anomaly baseline","Familiarity cache reset","8,192 identities exceeded the bounded session baseline; future first observations may repeat. No threat conclusion."));}
            }
        }
        long rate=frame.Rows.Sum(row=>row.Incoming+row.Outgoing);
        if(samples>=30&&rate>=1024*1024&&rate>Math.Max(1,average)*8)Event(new(DateTime.UtcNow,time,"Anomaly observation","Network","Unusual observed byte-rate change",$"Observed {rate:N0} B/s versus recent moving average {average:N0} B/s; a heuristic change indicator, not a threat classification."));
        average=samples==0?rate:average*.95+rate*.05;samples++;
        if(!frame.Complete||frame.CoverageSeconds<=0)Event(new(DateTime.UtcNow,time,"Coverage","Collector","Partial or unavailable network measurement",frame.Status+" Missing coverage is not zero traffic."));
        return Select(continuous);
    }
    string? lastUnavailable;
    void EventOnStatus(SensorSnapshot snapshot){if(lastUnavailable==snapshot.Status)return;lastUnavailable=snapshot.Status;Event(new(snapshot.ObservedUtc,null,"Coverage","Microphone / camera","Observation unavailable or partial",snapshot.Status+" An absent observation does not establish inactivity."));}
    void Trigger(DateTime time,DateTime? deadline=null){var before=buffer.Where(frame=>frame.StartUtc<time&&frame.StartUtc>=time.AddMinutes(-5)).ToArray();Event(new(time,time,"Network correlation","All observed applications","Before sensor observation",$"Five-minute RAM context: {before.Length} received buckets, observed download {before.Sum(frame=>frame.Rows.Sum(row=>row.Incoming)):N0} B; upload {before.Sum(frame=>frame.Rows.Sum(row=>row.Outgoing)):N0} B. Correlation is not proof of which app caused traffic; gaps and late delivery may reduce this context."));recordFrom=time>recordUntil?time.AddMinutes(-5):Min(recordFrom,time.AddMinutes(-5));recordUntil=Max(recordUntil,deadline??time.AddMinutes(5));Event(new(time,time,"Network capture","Sensor correlation","Triggered or extended network context","Retaining observed network seconds from five minutes before this observation through five minutes after the last established sensor activity; gaps are explicit."));}
    PrivacySelection Select(bool continuous){if(omitted>0&&pending.Count<4096){pending.Add(new(DateTime.UtcNow,null,"Coverage","Activity journal","Activity queue omissions",$"{omitted} observations omitted while persistence admission was unavailable."));omitted=0;}return new(buffer.Where(frame=>!queued.Contains(Key(frame))&&(continuous||(frame.StartUtc>=recordFrom&&frame.StartUtc<recordUntil))).ToArray(),pending.ToArray());}
    public PrivacySelection Finish(bool continuous)
    {
        if(minuteSamples>0){Event(new(DateTime.UtcNow,minuteStart,"Network aggregate","All observed applications","Final partial-minute context",$"Observed aggregate download {minuteRx:N0} B; upload {minuteTx:N0} B across {minuteSamples} received buckets before runtime stop. Partial minute; missing seconds are not zero."));minuteSamples=minuteRx=minuteTx=0;}
        return Select(continuous);
    }
    public void Accepted(PrivacySelection selection){foreach(var frame in selection.Frames)queued.Add(Key(frame));pending.RemoveRange(0,Math.Min(pending.Count,selection.Activities.Length));}
    public void Clear(){buffer.Clear();buffered.Clear();queued.Clear();retiredSources.Clear();activeSource=null;pending.Clear();uses.Clear();familiar.Clear();recordFrom=DateTime.MaxValue;recordUntil=frontier=DateTime.MinValue;average=0;samples=0;bufferBytes=minuteRx=minuteTx=minuteSamples=0;minuteStart=DateTime.MinValue;lastSnapshot=DateTime.MinValue;afterFrom=DateTime.MaxValue;cacheOmissions=0;cacheIncompleteUntil=DateTime.MinValue;}
    static DateTime Max(DateTime first,DateTime second)=>first>second?first:second;
    static DateTime Min(DateTime first,DateTime second)=>first<second?first:second;
}

public sealed class DetectorActivityReader(string path)
{
    public DetectorActivityRow[] Recent(int limit=500)
    {
        limit=Math.Clamp(limit,1,5000);using var db=Open(path);using var command=db.CreateCommand();command.CommandText="SELECT observed,activity,category,application,change,detail FROM detector_activity ORDER BY observed DESC,id DESC LIMIT $a";command.Parameters.AddWithValue("$a",limit);
        var list=new List<DetectorActivityRow>();using var rows=command.ExecuteReader();while(rows.Read())list.Add(new(new DateTime(rows.GetInt64(0),DateTimeKind.Utc),rows.IsDBNull(1)?null:new DateTime(rows.GetInt64(1),DateTimeKind.Utc),rows.GetString(2),rows.GetString(3),rows.GetString(4),rows.GetString(5)));return list.ToArray();
    }
    internal static SqliteConnection Open(string path){var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=StoragePaths.LocalDirectory(Path.GetFullPath(path)),Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1}.ToString());db.Open();return db;}
}

public sealed partial class HistoryStore
{
    void InitializeDetector(){Exec("CREATE TABLE IF NOT EXISTS detector_activity(id INTEGER PRIMARY KEY AUTOINCREMENT,observed INTEGER NOT NULL,activity INTEGER,day INTEGER NOT NULL,category TEXT NOT NULL,application TEXT NOT NULL,change TEXT NOT NULL,detail TEXT NOT NULL);CREATE INDEX IF NOT EXISTS detector_activity_time ON detector_activity(observed);CREATE INDEX IF NOT EXISTS detector_activity_day ON detector_activity(day);CREATE TABLE IF NOT EXISTS detector_observed_seconds(id INTEGER PRIMARY KEY AUTOINCREMENT,token TEXT NOT NULL,ticks INTEGER NOT NULL,day INTEGER NOT NULL,rx INTEGER NOT NULL,tx INTEGER NOT NULL,complete INTEGER NOT NULL,coverage REAL NOT NULL,UNIQUE(token,ticks));CREATE INDEX IF NOT EXISTS detector_seconds_day ON detector_observed_seconds(day);CREATE INDEX IF NOT EXISTS detector_seconds_time ON detector_observed_seconds(ticks)");}
    public void AppendDetectorActivity(DetectorActivityRow row){BeginWrite();var local=TimeZoneInfo.ConvertTimeFromUtc(row.ObservedUtc.ToUniversalTime(),zone);int day=local.Year*10000+local.Month*100+local.Day;Exec("INSERT OR IGNORE INTO recorded_days VALUES($a)",day);Exec("INSERT INTO detector_activity(observed,activity,day,category,application,change,detail) VALUES($a,$b,$c,$d,$e,$f,$g)",row.ObservedUtc.ToUniversalTime().Ticks,row.ActivityUtc?.ToUniversalTime().Ticks,day,row.Category,row.Application,row.Change,row.Detail);}
    public void ObserveDetectorSecond(Measurement frame){BeginWrite();DateTime local=TimeZoneInfo.ConvertTimeFromUtc(frame.StartUtc.ToUniversalTime(),zone);int day=local.Year*10000+local.Month*100+local.Day;Exec("INSERT OR IGNORE INTO recorded_days VALUES($a)",day);Exec("INSERT OR IGNORE INTO detector_observed_seconds(token,ticks,day,rx,tx,complete,coverage) VALUES($a,$b,$c,$d,$e,$f,$g)",frame.SourceSession??"legacy",frame.StartUtc.ToUniversalTime().Ticks,day,frame.Rows.Sum(row=>row.Incoming),frame.Rows.Sum(row=>row.Outgoing),frame.Complete?1:0,frame.CoverageSeconds);}
    void RetainDetector(){if(Indefinite)return;long cutoff=Scalar("SELECT COALESCE(MIN(day),0) FROM (SELECT day FROM recorded_days ORDER BY day DESC LIMIT 15)");if(cutoff<=0)return;BeginWrite();Exec("DELETE FROM detector_activity WHERE id IN(SELECT id FROM detector_activity WHERE day<$a LIMIT 256)",cutoff);Exec("DELETE FROM detector_observed_seconds WHERE id IN(SELECT id FROM detector_observed_seconds WHERE day<$a LIMIT 256)",cutoff);}
}
