using Detector.Core;
using Detector.History;
using Microsoft.Data.Sqlite;

static class AdditionalRegression
{
    static void checkPage(bool value){if(!value)throw new Exception("Large export fixture ended before first512 rows");}
    public static void Run(Action<bool,string> check)
    {
        var t=new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc);
        HourRow Hour(DateTime utc,int offset,int day)=>new(1,utc.Ticks,day,offset,"Fixture zone",0,0,0,0,1,0,false,false);
        var turkey=DiagnosticRange.ForDay(20261027,[Hour(t.AddHours(-3),180,20261027),Hour(t.AddHours(20),180,20261027)]);
        check(turkey.FromUtc==t.AddHours(-3)&&turkey.ToUtc==t.AddHours(21),"UTC diagnostic bounds include both UTC dates of a recorded UTC+3 calendar day");
        var fall=new DateTime(2026,11,1,4,0,0,DateTimeKind.Utc);
        var dst=DiagnosticRange.ForDay(20261101,[Hour(fall,-240,20261101),Hour(fall.AddHours(24),-300,20261101)]);
        check(dst.FromUtc==fall&&dst.ToUtc-fall==TimeSpan.FromHours(25),"recorded fall-back offsets preserve a 25-hour diagnostic date without using the viewer timezone");
        var nepal=t.AddHours(-5).AddMinutes(-45);
        var fractional=DiagnosticRange.ForDay(20261027,[Hour(nepal,345,20261027),Hour(nepal.AddHours(23),345,20261027)]);
        check(fractional.FromUtc==nepal&&fractional.ToUtc==nepal.AddDays(1),"diagnostic ranges retain non-whole-hour historical UTC offsets");
        var identity=new Identity(41,t.Ticks,"fixture",null);
        var ledger=new TrafficLedger();
        var first=new Measurement(1,t,1,true,0,0,[new(null,7,3),new(identity,20,30)],"fixture"){SourceSession="one"};
        for(int i=0;i<40;i++)ledger.Observe(first with{StartUtc=t.AddSeconds(i)});
        var snapshot=ledger.Snapshot();
        check(snapshot.Frames==40&&snapshot.Incoming==1080&&snapshot.Outgoing==1320&&snapshot.UnknownIncoming==280&&snapshot.UnknownOutgoing==120,"ledger accounts every received second through a synthetic 35+ second rendering suspension");
        check(snapshot.Chart.Sum(p=>p.UnknownIncoming+p.UnknownOutgoing)==400&&snapshot.LastCompleteUtc==t.AddSeconds(39),"unknown traffic chart and last-complete time derive from actual received buckets");
        ledger.Observe(first with{StartUtc=t.AddSeconds(39)});
        check(ledger.Snapshot().Frames==40,"duplicate same-source preview cannot double-count session bytes");
        ledger.Observe(first with{StartUtc=t.AddSeconds(39),SourceSession="two",CoverageSeconds=0,Complete=false});
        snapshot=ledger.Snapshot();
        check(snapshot.Frames==41&&snapshot.Unavailable==1&&snapshot.LastCompleteUtc==t.AddSeconds(39)&&snapshot.Processes.Length==2,"reconnection retains separate process epochs and unavailable coverage without replacing last complete evidence");
        ledger.Reset();
        for(int i=0;i<5000;i++)ledger.Observe(first with{StartUtc=t.AddSeconds(i),Rows=[new(identity with{Pid=i+1,Path="C:/fixture/"+i},1,2)]});
        snapshot=ledger.Snapshot();
        check(snapshot.Incoming==5000&&snapshot.Outgoing==10000&&snapshot.Applications.Sum(a=>a.Incoming)==5000&&snapshot.Applications.Length<=1025&&snapshot.Processes.Length<=4096&&snapshot.Chart.Length==120&&snapshot.PresentationLimited,"presentation cardinality stays bounded while exact observed totals survive 5,000 process identities");
        var invalid=new Aggregator(t);invalid.UnsupportedEvent(t,t.AddMilliseconds(1));invalid.MalformedEvent(t,t.AddMilliseconds(2));
        var broken=invalid.Advance(t.AddSeconds(3)).Single();
        check(!broken.Complete&&broken.Rows.Length==0&&broken.Quality?.UnsupportedDelta==1&&broken.Quality?.MalformedDelta==1&&broken.LostEvents==0,"unsupported and malformed events retain distinct uncertainty without invented bytes or OS loss");
        check(invalid.Advance(t.AddSeconds(4)).Single().Complete,"precisely assigned malformed/unsupported bucket defects do not contaminate healthy subsequent buckets");
        var diverse=new Aggregator(t){DetailedDiagnostics=true};long diverseEvents=0;
        foreach(var reason in new[]{AttributionReason.ProcessExited,AttributionReason.AccessDenied,AttributionReason.PathUnavailable,AttributionReason.IdentityAmbiguous,AttributionReason.Unknown})
            foreach(var protocol in Enum.GetValues<TrafficProtocol>())for(int pid=1;pid<=64;pid++){
                diverse.Add(new(t,reason==AttributionReason.PathUnavailable?identity with{Pid=pid}:null,Direction.Incoming,1,t.AddMilliseconds(10),reason,pid,protocol));diverseEvents++;
            }
        var diverseFrame=diverse.Advance(t.AddSeconds(3)).Single();
        check(diverseFrame.Attribution.Length<=144&&diverseFrame.Attribution.Sum(r=>r.Incoming)==diverseEvents&&diverseFrame.Rows.Sum(r=>r.Incoming)==diverseEvents,"detail combination budget stays bounded across PID/reason/protocol diversity without dropping measured bytes");
        string root=Path.Combine(Path.GetTempPath(),"DetectorAdditional-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string source=Path.Combine(root,"source.db");
            using(var store=new HistoryStore(source,TimeZoneInfo.Utc)){
                store.ConfigureProfile(new(BuildIdentity.Version,"unknown","desktop-collector",BuildIdentity.IntegrityModel,4,true,true));
                store.Append(first);store.Append(first);store.Commit();
                var epochs=new HistoryReader(source).Sessions();
                check(epochs.Length==2&&epochs.All(s=>s.SourceSession=="one"),"overlapping wall-clock recording epochs each retain their actual collector source token");
                store.Append(first with{StartUtc=t.AddSeconds(2),SourceSession="two"});store.Commit();
                check(new HistoryReader(source).Sessions().Length==3&&new HistoryReader(source).Sessions()[0].SourceSession=="two","source reconnection begins an independent provenance epoch");
            }
            string file=Path.Combine(root,"large.db");
            using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){
                for(int i=0;i<4;i++)store.Append(first with{StartUtc=t.AddHours(i),Attribution=[]});store.Commit();
            }
            // Direct schema fixtures exercise pagination, not capture throughput or actual byte reconciliation.
            using(var db=new SqliteConnection("Data Source="+file+";Pooling=False")){
                db.Open();using var cmd=db.CreateCommand();cmd.Parameters.AddWithValue("$t",t.Ticks);
                cmd.CommandText="WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<11000) INSERT INTO integrity(hour,epoch,ticks,flags,os_lost,late,malformed,capacity,discard_rx,discard_tx,affected_from,affected_to,uncertain_through,earlier_unknown,unsupported) SELECT (SELECT id FROM hours WHERE start_ticks=$t+(x/3600)*36000000000),1,$t+x*10000000,64,0,0,0,0,0,0,$t+x*10000000,$t+(x+1)*10000000,0,0,1 FROM n";cmd.ExecuteNonQuery();
                cmd.CommandText="WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<11000) INSERT INTO attribution SELECT (SELECT id FROM hours WHERE start_ticks=$t+((x/64)/60)*36000000000),1,$t+(x/64)*600000000,1,3,(x%64)+1,0,1,1,0,$t+(x/64)*600000000,$t+(x/64)*600000000,$t+(x/64)*600000000+10,$t+(x/64)*600000000+10 FROM n";cmd.ExecuteNonQuery();
            }
            var reader=new HistoryReader(file);
            check(reader.Integrity(20261027).Length==10000&&reader.Attribution(20261027).Length==10000,"large integrity and attribution previews enforce their 10,000-row memory limit");
            string csv=Path.Combine(root,"integrity.csv");reader.ExportIntegrity(csv,20261027);
            check(File.ReadLines(csv).Count()==11002&&File.ReadLines(csv).First().Contains("unsupported_delta"),"integrity CSV streams all 11,001 evidence rows and preserves unsupported-event counts");
            csv=Path.Combine(root,"attribution.csv");reader.ExportAttribution(csv,20261027);
            check(File.ReadLines(csv).Count()==11002,"attribution CSV streams beyond preview cap without duplicate or omitted cursor rows");
            reader.ExportAttribution(csv,20261027,1,3,42,t.Ticks,t.AddMinutes(2).Ticks);
            check(File.ReadLines(csv).Count()==3&&reader.Attribution(20261027,1,3,42,t.Ticks,t.AddMinutes(2).Ticks).Length==2,"date/time/protocol/reason/raw-PID filters agree between preview and complete CSV export");
            using(var maintenance=new HistoryStore(file,TimeZoneInfo.Utc){Indefinite=true,AttributionRecordedDays=1}){
                maintenance.Append(first with{StartUtc=t.AddDays(1),Rows=[],Attribution=[]});maintenance.Commit();
                using var paused=reader.AttributionRows(20261027).GetEnumerator();
                for(int i=0;i<512;i++)checkPage(paused.MoveNext());
                // A deliberately stalled export holds only its bounded in-memory page.
                using(var db=new SqliteConnection("Data Source="+file+";Pooling=False;Default Timeout=1")){
                    db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";
                    using var result=command.ExecuteReader();check(result.Read()&&result.GetInt32(0)==0,"paused diagnostic export leaves no SQLite reader lock preventing a WAL checkpoint");
                }
                maintenance.RetainStep();maintenance.Commit();bool rejected=false;
                try{paused.MoveNext();}catch(IOException){rejected=true;}
                check(rejected,"independent diagnostic retention invalidates a paused export even when detailed hourly totals are unchanged");
                string existing=Path.Combine(root,"preserved.csv");File.WriteAllText(existing,"keep original");rejected=false;
                try{HistoryReader.AtomicCsv(existing,output=>{int count=0;foreach(var row in reader.AttributionRows(20261027)){output.WriteLine(row.MinuteTicks);if(++count==512){maintenance.RetainStep();maintenance.Commit();}}});}catch(IOException){rejected=true;}
                check(rejected&&File.ReadAllText(existing)=="keep original"&&!Directory.EnumerateFiles(root,"*.tmp").Any(),"retention during streamed diagnostic export preserves the original destination and removes its incomplete temporary file");
            }
            string retention=Path.Combine(root,"retention.db");
            using(var store=new HistoryStore(retention,TimeZoneInfo.Utc){Indefinite=true,AttributionRecordedDays=1}){
                store.Append(first with{Attribution=[new(AttributionReason.MissingPid,TrafficProtocol.TcpIpv4,123,false,1,7,3,t.Ticks,t.Ticks,t.Ticks,t.Ticks)]});
                store.Append(first with{StartUtc=t.AddDays(1),Rows=[]});store.Commit();store.RetainStep();store.Commit();
                var retained=new HistoryReader(retention);
                check(retained.Days().Length==2&&retained.Attribution(20261027).Length==0&&retained.Hours(20261027).Sum(h=>h.Incoming)==27,"separate diagnostic retention counts zero-only coverage days and preserves indefinite detailed traffic history");
            }
        }finally{Directory.Delete(root,true);}
    }
}
