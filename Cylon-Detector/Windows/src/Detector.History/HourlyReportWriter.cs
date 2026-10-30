using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
namespace Detector.History;

// Retains the established lifecycle API. Report watermarks follow committed row IDs,
// independently of event timestamps, so delayed main-log commits remain reportable.
public sealed class HourlyReportWriter : IAsyncDisposable
{
    readonly CancellationTokenSource stop=new();readonly Task worker;volatile Preferences options;
    volatile string status="Waiting for activity summary interval";long lastGeneratedTicks,maximumTicks;
    readonly object gate=new();TaskCompletionSource? clear;Func<Task>? clearDetails;
    public string Status=>status;
    public DateTime? LastGeneratedUtc=>Interlocked.Read(ref lastGeneratedTicks)>0?new DateTime(Interlocked.Read(ref lastGeneratedTicks),DateTimeKind.Utc):null;
    public double MaximumReportMs=>Interlocked.Read(ref maximumTicks)/(double)TimeSpan.TicksPerMillisecond;
    public HourlyReportWriter(string historyFile,string directory,Preferences preferences,Func<Task>? beforeReport=null,Action<string>? prepareDirectory=null,Func<DateTime>? utcNow=null)
    {
        options=preferences;Func<DateTime> clock=utcNow??(()=>DateTime.UtcNow);
        DateTime Now()=>clock().ToUniversalTime();
        worker=Task.Run(async()=>{
            DateTime started=Now();bool prepared=false;
            while(!stop.IsCancellationRequested){
                try{
                    TaskCompletionSource? clearing;Func<Task>? details;lock(gate){clearing=clear;details=clearDetails;}
                    if(!options.HourlyReports&&clearing==null){status="Automatic activity summaries disabled; RAM observation and main logging continue";await Task.Delay(500,stop.Token);continue;}
                    string root=StoragePaths.LocalDirectory(directory);if(!prepared){prepareDirectory?.Invoke(root);prepared=true;}Directory.CreateDirectory(root);
                    string history=new HistoryReader(historyFile).HistoryId();if(!Guid.TryParseExact(history,"N",out _))throw new InvalidDataException("Invalid main log identity");
                    using var index=OpenIndex(root);
                    long activityCursor=0,networkCursor=0;
                    using(var query=index.CreateCommand()){query.CommandText="SELECT last_ticks,last_activity_id,last_network_id FROM report_state WHERE history=$a";query.Parameters.AddWithValue("$a",history);using var rows=query.ExecuteReader();if(rows.Read()){if(rows.GetInt64(0)>0)Interlocked.Exchange(ref lastGeneratedTicks,rows.GetInt64(0));activityCursor=rows.GetInt64(1);networkCursor=rows.GetInt64(2);}}
                    if(clearing!=null){
                        if(details!=null)await details();
                        var names=new List<string>();using(var query=index.CreateCommand()){query.CommandText="SELECT name FROM files WHERE history=$a";query.Parameters.AddWithValue("$a",history);using var rows=query.ExecuteReader();while(rows.Read())names.Add(rows.GetString(0));}
                        foreach(string name in names){ValidateName(name,history);File.Delete(Path.Combine(root,name));}
                        using(var query=index.CreateCommand()){query.CommandText="DELETE FROM files WHERE history=$a;DELETE FROM report_state WHERE history=$a;DELETE FROM cursors WHERE history=$a";query.Parameters.AddWithValue("$a",history);query.ExecuteNonQuery();}
                        Interlocked.Exchange(ref lastGeneratedTicks,0);started=Now();activityCursor=networkCursor=0;lock(gate){if(ReferenceEquals(clear,clearing)){clear=null;clearDetails=null;}}clearing.TrySetResult();
                    }
                    if(!options.HourlyReports){await Task.Delay(500,stop.Token);continue;}
                    DateTime from=LastGeneratedUtc??started,now=Now();int interval=Math.Clamp(options.ReportIntervalMinutes,1,1440);
                    if(now-from>=TimeSpan.FromMinutes(interval)){
                        var elapsed=Stopwatch.StartNew();if(beforeReport!=null)await beforeReport();
                        string name=$"CylonDetector-{history}-{now:yyyyMMddTHHmmssfffffffZ}.csv";
                        var consumed=WriteSummaryCore(historyFile,Path.Combine(root,name),from,now,(activityCursor,networkCursor));
                        DateTime local=now.ToLocalTime();int day=local.Year*10000+local.Month*100+local.Day;
                        using var query=index.CreateCommand();query.CommandText="BEGIN IMMEDIATE;INSERT INTO files VALUES($a,$b,$c,$d);INSERT INTO report_state(history,last_ticks,last_activity_id,last_network_id) VALUES($a,$b,$e,$f) ON CONFLICT(history) DO UPDATE SET last_ticks=excluded.last_ticks,last_activity_id=excluded.last_activity_id,last_network_id=excluded.last_network_id;COMMIT";query.Parameters.AddWithValue("$a",history);query.Parameters.AddWithValue("$b",now.Ticks);query.Parameters.AddWithValue("$c",day);query.Parameters.AddWithValue("$d",name);query.Parameters.AddWithValue("$e",consumed.Activity);query.Parameters.AddWithValue("$f",consumed.Network);query.ExecuteNonQuery();
                        Interlocked.Exchange(ref lastGeneratedTicks,now.Ticks);Interlocked.Exchange(ref maximumTicks,elapsed.Elapsed.Ticks);
                    }
                    if(!options.Indefinite){
                        int[] days=new HistoryReader(historyFile).Days();if(days.Length>=15){int cutoff=days.OrderDescending().Take(15).Min();var expired=new List<(long Slot,string Name)>();using(var query=index.CreateCommand()){query.CommandText="SELECT hour,name FROM files WHERE history=$a AND day<$b LIMIT 32";query.Parameters.AddWithValue("$a",history);query.Parameters.AddWithValue("$b",cutoff);using var rows=query.ExecuteReader();while(rows.Read())expired.Add((rows.GetInt64(0),rows.GetString(1)));}
                            foreach(var old in expired){ValidateName(old.Name,history);File.Delete(Path.Combine(root,old.Name));using var query=index.CreateCommand();query.CommandText="DELETE FROM files WHERE history=$a AND hour=$b";query.Parameters.AddWithValue("$a",history);query.Parameters.AddWithValue("$b",old.Slot);query.ExecuteNonQuery();}}
                    }
                    status=$"Activity summaries every {interval} minutes; destination independent of main log";
                }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or InvalidOperationException){status="SUMMARY REPORT DELAY/FAILURE (main logging independent): "+ex.Message;lock(gate){clear?.TrySetException(ex);clear=null;clearDetails=null;}}
                try{await Task.Delay(1000,stop.Token);}catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            }
            lock(gate)clear?.TrySetCanceled();
        });
    }
    static void ValidateName(string name,string history){if(Path.GetFileName(name)!=name||!name.StartsWith("CylonDetector-"+history+"-",StringComparison.Ordinal))throw new InvalidDataException("Unrecognized summary filename preserved");}
    static SqliteConnection OpenIndex(string root)
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=StoragePaths.LocalDirectory(Path.Combine(root,"CylonDetector-report-index.sqlite")),Pooling=false,DefaultTimeout=1}.ToString());db.Open();
        try{
            using var query=db.CreateCommand();query.CommandText="PRAGMA application_id";long id=Convert.ToInt64(query.ExecuteScalar());query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";long tables=Convert.ToInt64(query.ExecuteScalar());
            if(id!=0x43594450&&(id!=0||tables!=0))throw new InvalidDataException("Foreign report index preserved");
            query.CommandText="PRAGMA user_version";long version=Convert.ToInt64(query.ExecuteScalar());if(version>3||version<0)throw new InvalidDataException("Unsupported report index version; original database preserved");
            query.CommandText="PRAGMA application_id=1129923664;PRAGMA journal_mode=WAL;PRAGMA synchronous=FULL;CREATE TABLE IF NOT EXISTS cursors(history TEXT PRIMARY KEY,sequence INTEGER NOT NULL,hour INTEGER NOT NULL);CREATE TABLE IF NOT EXISTS files(history TEXT NOT NULL,hour INTEGER NOT NULL,day INTEGER NOT NULL,name TEXT NOT NULL,PRIMARY KEY(history,hour));CREATE TABLE IF NOT EXISTS report_state(history TEXT PRIMARY KEY,last_ticks INTEGER NOT NULL,last_activity_id INTEGER NOT NULL DEFAULT 0,last_network_id INTEGER NOT NULL DEFAULT 0);";query.ExecuteNonQuery();
            foreach(string column in new[]{"last_activity_id","last_network_id"}){query.CommandText=$"SELECT COUNT(*) FROM pragma_table_info('report_state') WHERE name='{column}'";if(Convert.ToInt64(query.ExecuteScalar())==0){query.CommandText=$"ALTER TABLE report_state ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0";query.ExecuteNonQuery();}}
            query.CommandText="PRAGMA user_version=3";query.ExecuteNonQuery();return db;
        }catch{db.Dispose();throw;}
    }
    public static void WriteSummary(string historyFile,string destination,DateTime fromUtc,DateTime toUtc)=>WriteSummaryCore(historyFile,destination,fromUtc,toUtc,null);
    static (long Activity,long Network) WriteSummaryCore(string historyFile,string destination,DateTime fromUtc,DateTime toUtc,(long Activity,long Network)? cursors)
    {
        fromUtc=fromUtc.ToUniversalTime();toUtc=toUtc.ToUniversalTime();if(toUtc<=fromUtc)throw new ArgumentException("Summary interval must have positive duration");
        string path=StoragePaths.LocalDirectory(Path.GetFullPath(destination)),temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
        try{
            using var db=DetectorActivityReader.Open(historyFile);using var snapshot=db.BeginTransaction(deferred:true);
            long Maximum(string table){using var query=db.CreateCommand();query.Transaction=snapshot;query.CommandText=$"SELECT COALESCE(MAX(id),0) FROM {table}";return Convert.ToInt64(query.ExecuteScalar());}
            long activityMaximum=Maximum("detector_activity"),networkMaximum=Maximum("detector_observed_seconds");
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false),65536,true)){
                writer.WriteLine("Observed UTC,Activity UTC,Category,Application,Change,Detail");
                void Row(params string?[] values)=>writer.WriteLine(string.Join(",",values.Select(Csv)));
                Row(toUtc.ToString("O"),null,"Summary","Cylon Detector","Observation interval",$"{fromUtc:O} through {toUtc:O}. Local metadata only. No audio/video contents, packet contents, endpoint history or DNS lookups. Windows evidence may omit unsupported/direct device access. Unfamiliar or unusual behavior is not automatically a threat. Late main-log commits are included by committed row ID, while their actual observation/activity times remain unchanged.");
                var anchors=new List<(long Observed,long? Activity,string Kind,string Application,string Change)>();
                using(var query=db.CreateCommand()){
                    query.Transaction=snapshot;query.CommandText="SELECT observed,activity,category,application,change,detail FROM detector_activity WHERE "+(cursors.HasValue?"id>$a AND id<=$b":"observed>=$a AND observed<$b")+" ORDER BY id LIMIT 10001";query.Parameters.AddWithValue("$a",cursors?.Activity??fromUtc.Ticks);query.Parameters.AddWithValue("$b",cursors.HasValue?activityMaximum:toUtc.Ticks);
                    using var rows=query.ExecuteReader();int count=0;
                    while(rows.Read()){
                        if(count++==10000){Row(toUtc.ToString("O"),null,"Coverage","Summary","Detail budget reached","More than 10,000 newly committed activity events; additional event details remain in the main SQLite log. This summary explicitly omits excess detail.");break;}
                        long observed=rows.GetInt64(0);long? activity=rows.IsDBNull(1)?null:rows.GetInt64(1);string category=rows.GetString(2),application=rows.GetString(3),change=rows.GetString(4),detail=rows.GetString(5);
                        if(observed<fromUtc.Ticks)detail="Late-committed observation from an earlier report interval. "+detail;else if(observed>=toUtc.Ticks)detail="Committed after this report timestamp cutoff; actual observation time is retained. "+detail;
                        Row(new DateTime(observed,DateTimeKind.Utc).ToString("O"),activity.HasValue?new DateTime(activity.Value,DateTimeKind.Utc).ToString("O"):null,category,application,change,detail);
                        if((category.Equals("microphone",StringComparison.OrdinalIgnoreCase)||category.Equals("camera",StringComparison.OrdinalIgnoreCase))&&!change.Contains("Historical",StringComparison.OrdinalIgnoreCase))anchors.Add((observed,activity,category,application,change));
                    }
                }
                using(var query=db.CreateCommand()){
                    query.Transaction=snapshot;query.CommandText="SELECT COUNT(*),COALESCE(SUM(rx),0),COALESCE(SUM(tx),0),COALESCE(SUM(coverage),0),COALESCE(SUM(CASE WHEN complete=0 THEN 1 ELSE 0 END),0),MIN(ticks),MAX(ticks) FROM detector_observed_seconds WHERE "+(cursors.HasValue?"id>$a AND id<=$b":"ticks>=$a AND ticks<$b");query.Parameters.AddWithValue("$a",cursors?.Network??fromUtc.Ticks);query.Parameters.AddWithValue("$b",cursors.HasValue?networkMaximum:toUtc.Ticks);using var rows=query.ExecuteReader();
                    if(rows.Read())Row(toUtc.ToString("O"),null,"Persisted network context","Selected capture windows","Newly committed recorded coverage",$"{rows.GetInt64(0)} persisted buckets, observed download {rows.GetInt64(1)} B, upload {rows.GetInt64(2)} B, coverage {rows.GetDouble(3):F3} seconds, partial/unavailable buckets {rows.GetInt64(4)}. Event-time range {(rows.IsDBNull(5)?"none":new DateTime(rows.GetInt64(5),DateTimeKind.Utc).ToString("O"))} through {(rows.IsDBNull(6)?"none":new DateTime(rows.GetInt64(6),DateTimeKind.Utc).ToString("O"))}; delayed/prebuffer commits may precede the report interval. Outside selected windows only minute-level aggregate context is retained; missing seconds are not zero. Correlation does not establish causation.");
                }
                // Revisit recent transitions after their first report so a five-minute
                // post-stop window can mature in a later report without losing its anchor.
                using(var query=db.CreateCommand()){
                    query.Transaction=snapshot;query.CommandText="SELECT observed,activity,category,application,change FROM detector_activity WHERE observed>=$a AND lower(category) IN ('microphone','camera') AND change NOT LIKE '%Historical%' ORDER BY id DESC LIMIT 64";
                    query.Parameters.AddWithValue("$a",(fromUtc>DateTime.MinValue.AddMinutes(5)?fromUtc.AddMinutes(-5).Ticks:0));
                    using var rows=query.ExecuteReader();while(rows.Read()){
                        var anchor=(Observed:rows.GetInt64(0),Activity:rows.IsDBNull(1)?(long?)null:rows.GetInt64(1),Kind:rows.GetString(2),Application:rows.GetString(3),Change:rows.GetString(4));
                        if(!anchors.Contains(anchor))anchors.Add(anchor);
                    }
                }
                foreach(var anchor in anchors.Take(16)){
                    DateTime at=new(anchor.Activity??anchor.Observed,DateTimeKind.Utc);DateTime before=at<=DateTime.MinValue.AddMinutes(5)?DateTime.SpecifyKind(DateTime.MinValue,DateTimeKind.Utc):at.AddMinutes(-5),after=at>=DateTime.MaxValue.AddMinutes(-5)?DateTime.SpecifyKind(DateTime.MaxValue,DateTimeKind.Utc):at.AddMinutes(5);
                    Context("Before "+anchor.Kind+" transition",before,at,anchor.Application);
                    Context("After "+anchor.Kind+" transition",at,after<toUtc?after:toUtc,anchor.Application);
                    if(anchor.Change.Contains("active",StringComparison.OrdinalIgnoreCase)){
                        var next=anchors.Where(other=>other.Kind==anchor.Kind&&other.Application==anchor.Application&&other.Change.Contains("stopped",StringComparison.OrdinalIgnoreCase)&&(other.Activity??other.Observed)>at.Ticks).OrderBy(other=>other.Activity??other.Observed).FirstOrDefault();
                        DateTime end=next==default?toUtc:new DateTime(next.Activity??next.Observed,DateTimeKind.Utc);if(end>toUtc)end=toUtc;
                        Context("During observed "+anchor.Kind+" interval",at,end,anchor.Application);
                    }
                }
                if(anchors.Count>16)Row(toUtc.ToString("O"),null,"Coverage","Temporal comparisons","Comparison budget reached","Before/during/after numeric comparisons are shown for the first 16 sensor transitions. All retained transition rows remain above or in the main SQLite log.");
                void Context(string label,DateTime start,DateTime end,string application){
                    if(end<=start){Row(toUtc.ToString("O"),start.ToString("O"),"Network temporal association",application,label,"This interval has not yet elapsed at the report cutoff; later summaries may contain its committed network context.");return;}
                    using var query=db.CreateCommand();query.Transaction=snapshot;query.CommandText="SELECT COUNT(*),COALESCE(SUM(rx),0),COALESCE(SUM(tx),0),COALESCE(SUM(coverage),0) FROM detector_observed_seconds WHERE ticks>=$a AND ticks<$b";query.Parameters.AddWithValue("$a",start.Ticks);query.Parameters.AddWithValue("$b",end.Ticks);using var rows=query.ExecuteReader();rows.Read();
                    Row(toUtc.ToString("O"),start.ToString("O"),"Network temporal association",application,label,$"UTC [{start:O}, {end:O}): {rows.GetInt64(0)} retained network buckets, observed download {rows.GetInt64(1)} B, upload {rows.GetInt64(2)} B, measured coverage {rows.GetDouble(3):F3}s. This is device-time association across all observed applications, not proof this application transferred camera/microphone content. No retained buckets means unknown/unrecorded context, not zero traffic; before/after context may be incomplete. Display-name pairing can be ambiguous.");
                }
                writer.Flush();stream.Flush(true);
            }
            snapshot.Commit();File.Move(temporary,path,true);return(activityMaximum,networkMaximum);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static string Csv(string? value){value??="";string start=value.TrimStart();if(value.Length>0&&(value[0] is '\t' or '\r' or '\n'||start.Length>0&&start[0] is '=' or '+' or '-' or '@'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    public void Configure(Preferences value)=>options=value;
    public Task ClearAsync(Func<Task>? clearDetails=null){lock(gate){clear??=new(TaskCreationOptions.RunContinuationsAsynchronously);this.clearDetails??=clearDetails;return clear.Task;}}
    public async ValueTask DisposeAsync(){stop.Cancel();try{await worker;}catch(OperationCanceledException)when(stop.IsCancellationRequested){}stop.Dispose();}
}
