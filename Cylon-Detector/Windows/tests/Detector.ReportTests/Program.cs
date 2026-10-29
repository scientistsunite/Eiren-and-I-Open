using Detector.Core;
using Detector.History;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

int checks=0;
void Check(bool result,string description){if(!result)throw new InvalidOperationException(description);checks++;Console.WriteLine("PASS "+description);}
async Task Wait(Func<bool> condition,Func<string> why){var timer=Stopwatch.StartNew();while(!condition()){if(timer.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException(why());await Task.Delay(25);}}
string root=Path.Combine(Path.GetTempPath(),"DetectorReportTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
DateTime epoch=new(2026,1,1,12,0,0,DateTimeKind.Utc);
Measurement Frame(int seconds,long bytes,string token="report-fixture")=>new(1,epoch.AddSeconds(seconds),1,true,0,0,[new(null,bytes,bytes/2)],"synthetic report coverage"){SourceSession=token};
try{
    string main=Path.Combine(root,"main.db");
    using(var store=new HistoryStore(main,TimeZoneInfo.Utc)){
        store.AppendDetectorActivity(new(epoch.AddSeconds(-1),epoch,"camera","Excluded","Historical use evidence","outside requested interval"));
        store.AppendDetectorActivity(new(epoch.AddSeconds(5),epoch.AddSeconds(5),"microphone","=2+2","Observed active","Synthetic metadata; no audio contents"));
        store.AppendDetectorActivity(new(epoch.AddSeconds(8),epoch.AddSeconds(8),"microphone","=2+2","Observed stopped","Synthetic stop evidence"));
        store.AppendDetectorActivity(new(epoch.AddSeconds(10),epoch.AddSeconds(10),"Anomaly observation","  @SUM(1)","First observed in this session","Unfamiliar does not imply a threat"));
        store.AppendDetectorActivity(new(epoch.AddSeconds(12),epoch,"Network aggregate","All observed applications","Minute-level context","Observed aggregate download 1000 B; no raw second-by-second recording outside selected windows"));
        store.ObserveDetectorSecond(Frame(4,10));store.ObserveDetectorSecond(Frame(6,20));store.ObserveDetectorSecond(Frame(9,30));store.Commit();
    }
    string manual=Path.Combine(root,"manual.csv");HourlyReportWriter.WriteSummary(main,manual,epoch,epoch.AddSeconds(20));string text=File.ReadAllText(manual);
    Check(text.Contains("\"'=2+2\"")&&text.Contains("\"'  @SUM(1)\""),"Untrusted formula-leading and whitespace-prefixed metadata is escaped before CSV quotation");
    Check(!text.Contains("Excluded")&&text.Contains("Unfamiliar does not imply a threat"),"Summary respects requested observation interval and preserves anomaly caveats");
    Check(text.Contains("Minute-level context")&&text.Contains("download 1000 B"),"RAM-derived minute aggregate context is reported without inventing raw network logs");
    Check(text.Contains("Before microphone transition")&&text.Contains("After microphone transition")&&text.Contains("During observed microphone interval"),"Summary explicitly labels before/during/after device-time associations");
    Check(text.Contains("not proof this application transferred camera/microphone content")&&text.Contains("unknown/unrecorded context"),"Temporal comparisons explain unavailable coverage and avoid content or causation claims");
    Check(text.Contains("3 persisted buckets")&&text.Contains("observed download 60 B"),"Network context conserves all selected-frame bytes exactly");
    File.WriteAllText(manual,"preserve");bool invalid=false;try{HourlyReportWriter.WriteSummary(main,manual,epoch,epoch);}catch(ArgumentException){invalid=true;}
    Check(invalid&&File.ReadAllText(manual)=="preserve"&&!Directory.EnumerateFiles(root,"*.tmp-*").Any(),"Invalid report range preserves an existing destination and leaves no temporary file");

    long ticks=epoch.Ticks;DateTime Clock()=>new(Interlocked.Read(ref ticks),DateTimeKind.Utc);
    string reports=Path.Combine(root,"summaries");
    await using(var writer=new HourlyReportWriter(main,reports,new Preferences{ReportIntervalMinutes=1,Indefinite=true},utcNow:Clock)){
        await Wait(()=>writer.Status.StartsWith("Activity summaries"),()=>writer.Status);
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(30).Ticks);await Task.Delay(1200);
        Check(!Directory.EnumerateFiles(reports,"*.csv").Any(),"Automatic summary waits for the full configured interval");
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(61).Ticks);await Wait(()=>writer.LastGeneratedUtc==Clock(),()=>writer.Status);
        Check(Directory.EnumerateFiles(reports,"*.csv").Count()==1,"A one-minute cadence creates exactly one completed report");
        using(var store=new HistoryStore(main,TimeZoneInfo.Utc)){
            // Both timestamps precede the previous report boundary, but commit IDs are newer.
            store.AppendDetectorActivity(new(epoch.AddSeconds(11),epoch.AddSeconds(9),"camera","Late fixture","Observed active","late-commit-evidence"));
            store.ObserveDetectorSecond(Frame(3,666,"late-source"));store.Commit();
        }
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(122).Ticks);await Wait(()=>writer.LastGeneratedUtc==Clock(),()=>writer.Status);
        string second=File.ReadAllText(Directory.EnumerateFiles(reports,"*.csv").Order().Last());
        Check(second.Contains("late-commit-evidence")&&second.Contains("Late-committed observation from an earlier report interval"),"Late-committed device evidence remains reportable after its timestamp interval has passed");
        Check(second.Contains("1 persisted buckets")&&second.Contains("observed download 666 B"),"Late/prebuffered network commits are included by insertion ID without losing earlier event-time buckets");
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(183).Ticks);await Wait(()=>writer.LastGeneratedUtc==Clock(),()=>writer.Status);
        string third=File.ReadAllText(Directory.EnumerateFiles(reports,"*.csv").Order().Last());
        Check(!third.Contains("late-commit-evidence")&&third.Contains("0 persisted buckets"),"Committed-ID cursors avoid repeating already reported activity and network totals");
        Check(third.Contains("Observation interval"),"A scheduled summary is generated even with no new device observations or selected network seconds");
        writer.Configure(new Preferences{ReportIntervalMinutes=5,Indefinite=true});
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(482).Ticks);await Task.Delay(1200);
        Check(Directory.EnumerateFiles(reports,"*.csv").Count()==3,"Changing cadence to five minutes does not retain the prior one-minute schedule");
        Interlocked.Exchange(ref ticks,epoch.AddSeconds(484).Ticks);await Wait(()=>writer.LastGeneratedUtc==Clock(),()=>writer.Status);
        Check(Directory.EnumerateFiles(reports,"*.csv").Count()==4,"Changed summary cadence takes effect without reopening main logging");
        string matured=File.ReadAllText(Directory.EnumerateFiles(reports,"*.csv").Order().Last());
        Check(matured.Contains("After microphone transition")&&matured.Contains(epoch.AddSeconds(308).ToString("O")),"A later summary revisits an earlier stop and includes its completed five-minute post-activity comparison");
        writer.Configure(new Preferences{HourlyReports=false,ReportIntervalMinutes=1,Indefinite=true});await Wait(()=>writer.Status.StartsWith("Automatic activity summaries disabled"),()=>writer.Status);
        Interlocked.Exchange(ref ticks,epoch.AddHours(2).Ticks);await Task.Delay(1200);
        Check(Directory.EnumerateFiles(reports,"*.csv").Count()==4,"Disabling automatic summaries stops report creation independently of the retained main log");
    }
    string indexFile=Path.Combine(reports,"CylonDetector-report-index.sqlite");
    using(var db=new SqliteConnection("Data Source="+indexFile+";Pooling=False")){db.Open();using var query=db.CreateCommand();query.CommandText="PRAGMA application_id";Check(Convert.ToInt64(query.ExecuteScalar())==0x43594450,"Report index carries Detector's independent application identity");query.CommandText="SELECT last_activity_id,last_network_id FROM report_state";using var rows=query.ExecuteReader();rows.Read();Check(rows.GetInt64(0)>0&&rows.GetInt64(1)==4,"Report completion stores both committed activity and selected-network watermarks");}

    string retentionMain=Path.Combine(root,"retention.db"),retentionReports=Path.Combine(root,"retention-reports");
    using(var store=new HistoryStore(retentionMain,TimeZoneInfo.Utc)){for(int day=0;day<16;day++)store.AppendDetectorActivity(new(epoch.AddDays(day),null,"Coverage","Fixture","Observed","metadata"));store.Commit();}
    long retentionTicks=epoch.AddDays(16).Ticks;
    await using(var writer=new HourlyReportWriter(retentionMain,retentionReports,new Preferences{Indefinite=true},utcNow:()=>new DateTime(retentionTicks,DateTimeKind.Utc))){
        await Wait(()=>writer.Status.StartsWith("Activity summaries"),()=>writer.Status);
        string historyId=new HistoryReader(retentionMain).HistoryId(),oldName="CylonDetector-"+historyId+"-old.csv",old=Path.Combine(retentionReports,oldName),foreign=Path.Combine(retentionReports,"unrelated.csv");File.WriteAllText(old,"old Detector report");File.WriteAllText(foreign,"preserve unrelated report");
        using(var db=new SqliteConnection("Data Source="+Path.Combine(retentionReports,"CylonDetector-report-index.sqlite")+";Pooling=False")){db.Open();using var query=db.CreateCommand();query.CommandText="INSERT INTO files VALUES($a,1,20260101,$b)";query.Parameters.AddWithValue("$a",historyId);query.Parameters.AddWithValue("$b",oldName);query.ExecuteNonQuery();}
        await Task.Delay(1200);Check(File.Exists(old),"Indefinite retention preserves an older indexed summary report");
        writer.Configure(new Preferences{Indefinite=false});await Wait(()=>!File.Exists(old),()=>writer.Status);
        Check(File.ReadAllText(foreign)=="preserve unrelated report","Recorded-day retention removes only indexed Detector reports and preserves unrelated files");
    }
    Console.WriteLine($"PASS {checks} Detector report assertions. Synthetic SQLite metadata and fake clock only; no collector, service, device or user profile opened.");
}finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
