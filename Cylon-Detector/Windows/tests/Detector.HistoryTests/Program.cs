using Detector.Core;
using Detector.History;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
if(args.Length==2&&args[0]=="--crash-fixture"){
    if(!new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(args[1]))!).Name.StartsWith("DetectorHistoryTests-",StringComparison.Ordinal))throw new Exception("Isolated fixture path required");
    using var fixture=new HistoryStore(args[1],TimeZoneInfo.Utc);var at=new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc);fixture.Begin(at);
    fixture.Append(new(1,at,1,true,0,0,[new(null,111,222)],"fixture"));fixture.Commit();fixture.Append(new(1,at.AddSeconds(1),1,true,0,0,[new(null,333,444)],"uncommitted fixture"));Console.WriteLine("READY");Console.Out.Flush();Thread.Sleep(Timeout.Infinite);return;
}
if(args.Length==3&&args[0]=="--await-recording"){
    long started=long.Parse(args[2],System.Globalization.CultureInfo.InvariantCulture);var deadline=Stopwatch.StartNew();
    while(deadline.Elapsed<TimeSpan.FromSeconds(25)){
        try{if(File.Exists(args[1])&&new HistoryReader(args[1]).Sessions().Any(s=>s.RequestedTicks>=started&&s.FirstCoverageTicks>=started&&s.EndedTicks==null&&s.SourceSession!=null)){Console.WriteLine("PASS new installed UI session has genuinely committed monitoring coverage before uninstall");return;}}
        catch(Exception ex)when(ex is IOException or SqliteException){}
        await Task.Delay(50);
    }
    throw new TimeoutException("New installed UI did not commit coverage within25s; uninstall acceptance unavailable");
}
if(args.Length==2&&args[0]=="--inspect"){
    var inspection=new HistoryReader(args[1]);var dates=inspection.Days();if(dates.Length==0||inspection.Hours(dates[0]).Sum(h=>h.CoverageSeconds)<=0)throw new Exception("Installed application failed to persist real monitoring coverage");
    Console.WriteLine("PASS installed application history contains real coverage after process exit; days="+dates.Length);return;
}
if(args.Length==1&&args[0]=="--extended-benchmarks"){
    var benchRoot=Path.Combine(Path.GetTempPath(),"DetectorHistoryTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(benchRoot);
    try {
        foreach(var scenario in new[]{(Name:"busy-1h-16-apps",Days:1,Seconds:3600,Apps:16,Idle:false,Commit:2),(Name:"confirmed-idle-15d",Days:15,Seconds:86400,Apps:0,Idle:true,Commit:60),(Name:"sparse-365-recorded-days",Days:365,Seconds:120,Apps:6,Idle:false,Commit:2)}){
            var origin=new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc);string file=Path.Combine(benchRoot,scenario.Name+".db");long expectedRx=0,expectedTx=0;long heapBefore=GC.GetTotalMemory(true);long cpuBefore=Process.GetCurrentProcess().TotalProcessorTime.Ticks;var timer=Stopwatch.StartNew();
            using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){
                store.Indefinite=scenario.Days>15;
                for(int day=0;day<scenario.Days;day++){
                    var start=origin.AddDays(day);store.Begin(start);
                    var identities=Enumerable.Range(0,scenario.Apps).SelectMany(app=>Enumerable.Range(0,2).Select(slot=>new Identity(1000+app*2+slot,start.AddTicks(slot).Ticks,"Synthetic application "+app,@"C:\Synthetic\Program "+app+@"\application.exe"))).ToArray();
                    for(int second=0;second<scenario.Seconds;second++){
                        var traffic=Enumerable.Range(0,scenario.Apps).Select(app=>new Row(identities[app*2+second%2],(second%17+1)*64+app,(second%13+1)*32+app)).ToArray();
                        foreach(var row in traffic){expectedRx+=row.Incoming;expectedTx+=row.Outgoing;}
                        store.Append(new(1,start.AddSeconds(second),1,true,0,0,traffic,"observed synthetic fixture"));
                        if(second%scenario.Commit==scenario.Commit-1)store.Commit();
                    }
                    store.RecordState(start.AddSeconds(scenario.Seconds),HistoryState.Stopped,"Synthetic session ended; remaining day is unmonitored");store.Retain();store.Commit();
                }
                store.FinalizeHours(origin.AddDays(scenario.Days));store.Commit();
            }
            double wall=timer.Elapsed.TotalSeconds,cpu=(Process.GetCurrentProcess().TotalProcessorTime.Ticks-cpuBefore)/(double)TimeSpan.TicksPerMillisecond;long heap=GC.GetTotalMemory(true)-heapBefore,working=Process.GetCurrentProcess().WorkingSet64;
            var read=new HistoryReader(file);var dates=read.Days();var hours=dates.SelectMany(read.Hours).ToArray();long secondsExpected=(long)scenario.Days*scenario.Seconds;
            if(dates.Length!=scenario.Days||hours.Sum(h=>h.Incoming)!=expectedRx||hours.Sum(h=>h.Outgoing)!=expectedTx||hours.Sum(h=>h.CoverageSeconds)!=secondsExpected)throw new Exception("Extended benchmark exact coverage/totals failed: "+scenario.Name);
            foreach(var at in new[]{origin,origin.AddDays(scenario.Days-1).AddSeconds(scenario.Seconds-1)}){
                var rows=new HistoryReader(file).Timeline(at.Ticks,at.AddSeconds(1).Ticks).ToArray();int second=(int)(at-at.Date).TotalSeconds;
                if(scenario.Idle){if(rows.Length!=1||rows[0].Incoming!=0||rows[0].State!=HistoryState.Complete)throw new Exception("Idle coverage oracle failed");}
                else for(int app=0;app<scenario.Apps;app++){var row=rows.Single(r=>r.Application=="Synthetic application "+app);if(row.Incoming!=(second%17+1)*64+app||row.Outgoing!=(second%13+1)*32+app||row.CreationTicks!=at.Date.AddTicks(second%2).Ticks)throw new Exception("Extended exact-second/PID identity oracle failed");}
            }
            using(var inspect=new HistoryStore(file,TimeZoneInfo.Utc)){if(inspect.Count("traffic")!=secondsExpected*scenario.Apps||inspect.Count("apps")!=scenario.Apps+1)throw new Exception("Extended dictionary/sparse count failed");inspect.Commit();}
            Console.WriteLine(JsonSerializer.Serialize(new{benchmark="production history extended",scenario=scenario.Name,recordedDays=dates.Length,confirmedSeconds=secondsExpected,nonzeroProcessSeconds=secondsExpected*scenario.Apps,applications=scenario.Apps,databaseBytes=new FileInfo(file).Length,cpuMs=cpu,wallSeconds=wall,managedMemoryDelta=heap,processWorkingSet=working,commitEverySimulatedSeconds=scenario.Commit,exactOracle="passed",note="Synthetic accelerated workload; idle-15d uses minute commits to bound CI fsync cost, unlike runtime two-second cadence; other scenarios use two-second commits. Process CPU includes fixture generation; working set includes runtime/native SQLite/harness, not isolated logger attribution. Sparse year observes only 120 seconds/day; intervening time is unmonitored, not zero. No physical duration or sustained busy year claim."}));
        }
    }finally{SqliteConnection.ClearAllPools();Directory.Delete(benchRoot,true);}return;
}
int checks=0;
void Check(bool result,string name){if(!result)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
var root=Path.Combine(Path.GetTempPath(),"DetectorHistoryTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string FileFor(string name)=>Path.Combine(root,name+".db");
var t=new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc);
var process=new Identity(42,t.AddMinutes(-1).Ticks,"Synthetic application",@"C:\Synthetic\app.exe");
Measurement Frame(DateTime utc,long incoming=0,long outgoing=0,bool complete=true,double coverage=1,Identity? identity=null)=>new(1,utc,coverage,complete,complete?0:1,0,incoming==0&&outgoing==0?[]:[new(identity??process,incoming,outgoing)],complete?"observed":"partial");
try {
    string path=FileFor("exact");
    using(var store=new HistoryStore(path,TimeZoneInfo.Utc)){
        store.Begin(t);for(int i=0;i<3600;i++)store.Append(Frame(t.AddSeconds(i),i<2?9007199254740993L+i:0,i<2?100+i:0));
        store.Append(Frame(t.AddHours(1),123,456,false));store.Append(Frame(t.AddHours(1).AddSeconds(1),0,0,false,0));store.FinalizeHours(t.AddHours(2));store.Commit();
        Check(store.Count("traffic")==3,"sparse idle excludes redundant zero traffic rows");Check(store.Count("coverage")==3,"coverage ranges compact while preserving partial/unavailable states");
        Check(store.Count("paths")==2&&store.Count("names")==2&&store.Count("apps")==2&&store.Count("processes")==2,"dictionary metadata stored once");
    }
    var reader=new HistoryReader(path);var rows=reader.Timeline(t.Ticks,t.AddHours(2).Ticks).ToArray();
    Check(rows.Length==3602,"each recorded second reconstructed exactly");Check(rows[0].Incoming==9007199254740993L&&rows[0].Outgoing==100,"64-bit byte precision exceeds display double precision");
    Check(rows[20].Incoming==0&&rows[20].State==HistoryState.Complete,"confirmed idle reconstructs zero");Check(rows[^1].Incoming is null&&rows[^1].State==HistoryState.Unavailable,"unavailable never reconstructs zero");
    var hours=reader.Hours(20260101);Check(hours.Length==2&&hours.All(h=>h.Finalized),"hour boundaries and finalization persistent");
    Check(hours[0].Incoming==18014398509481987L&&hours[0].PeakIncoming==9007199254740994L&&hours[0].CoverageSeconds==3600,"hour totals peaks actual coverage preserve integers");
    Check(hours[1].CoverageSeconds==1&&hours[1].Incomplete==2,"partial hour never claims unobserved time");
    string csv=Path.Combine(root,"exact.csv");reader.ExportCsv(csv,t.Ticks,t.AddSeconds(3).Ticks);Check(System.IO.File.ReadAllText(csv).Contains("9007199254740993")&&System.IO.File.ReadAllText(csv).Contains(@"C:\Synthetic\app.exe"),"CSV expands dictionary names/paths without rounding");
    using(var store=new HistoryStore(path,TimeZoneInfo.Utc)){var now=DateTime.UtcNow;Check(new HistoryReader(path).Events(now.Year*10000+now.Month*100+now.Day).Any(e=>e.State==HistoryState.Recovery),"unclean writer epoch produces explicit recovery event");store.Begin(t.AddDays(1));store.Append(Frame(t.AddDays(1),10,20,identity:process with{StartUtcTicks=t.Ticks}));store.Commit();}
    var reused=new HistoryReader(path).Timeline(t.AddDays(1).Ticks,t.AddDays(1).AddSeconds(1).Ticks).Single();Check(reused.CreationTicks==t.Ticks&&reused.ProcessInstance!=rows[0].ProcessInstance,"PID reuse and restart epochs cannot collapse process identities");
    string retention=FileFor("retention");
    using(var store=new HistoryStore(retention,TimeZoneInfo.Utc)){
        for(int i=0;i<16;i++){store.Begin(t.AddDays(i*3));store.Append(Frame(t.AddDays(i*3)));store.RecordState(t.AddDays(i*3).AddSeconds(1),HistoryState.Stopped,"Stopped");}
        store.Retain();store.Commit();Check(store.Count("recorded_days")==15,"15 recorded dates includes zero-only days and skips no-monitoring dates");
        Check(!new HistoryReader(retention).Days().Contains(20260101),"16th distinct date removes oldest date");
        store.Indefinite=true;for(int i=16;i<40;i++){store.Begin(t.AddDays(i*3));store.Append(Frame(t.AddDays(i*3)));}store.Retain();store.Commit();Check(store.Count("recorded_days")==39,"indefinite mode never performs age deletion");
        store.Begin(t.AddDays(200));store.Append(Frame(t.AddDays(200),0,0,false,0));store.Commit();Check(store.Count("recorded_days")==39,"unavailable-only date not counted as recorded coverage");
        store.Clear();Check(store.Count("recorded_days")==0&&store.Count("traffic")==0,"manual clear removes history consistently");
    }
    string sliced=FileFor("sliced-retention");
    using(var store=new HistoryStore(sliced,TimeZoneInfo.Utc)){
        for(int day=0;day<16;day++){store.Begin(t.AddDays(day));for(int second=0;second<4;second++)store.Append(Frame(t.AddDays(day).AddSeconds(second),1));}
        store.Commit();long before=store.Count("traffic");store.RetainStep(1);store.Commit();
        Check(store.Count("traffic")==before-1&&new HistoryReader(sliced).Days().Length==15,"bounded retention hides expired date before physical deletion finishes");
        Check(!new HistoryReader(sliced).Timeline(t.Ticks,t.AddSeconds(4).Ticks).Any(),"partly purged expired date cannot export misleading remaining seconds");
        store.Indefinite=true;before=store.Count("traffic");store.RetainStep(1);store.Commit();Check(store.Count("traffic")==before,"indefinite policy immediately stops incremental age deletion");
        store.Indefinite=false;int steps=0;while(store.RetainStep(1)){store.Commit();if(++steps>1000)throw new Exception("Retention failed to finish bounded slices");}store.Commit();
        Check(store.Count("recorded_days")==15&&store.Count("traffic")==60,"incremental retention completes without deleting retained measurements");
    }
    // Detector produces activity summaries on a configurable cadence rather than
    // the copied Raider contract of immediately exporting finalized clock hours.
    string reportFile=FileFor("report-files"),reportDirectory=Path.Combine(root,"separate-reports");
    long reportClock=t.AddHours(2).Ticks;
    DateTime ReportNow()=>new(Interlocked.Read(ref reportClock),DateTimeKind.Utc);
    void AdvanceReportClock()=>Interlocked.Add(ref reportClock,TimeSpan.TicksPerMinute+TimeSpan.TicksPerSecond);
    async Task WaitReport(Func<bool> condition,Func<string> status){var wait=Stopwatch.StartNew();while(!condition()){if(wait.Elapsed>TimeSpan.FromSeconds(15))throw new Exception(status());await Task.Delay(25);}}
    var summaryOptions=new Preferences{ReportIntervalMinutes=1,Indefinite=true};
    using(var store=new HistoryStore(reportFile,TimeZoneInfo.Utc)){
        store.Begin(t);store.Append(Frame(t,123,456));
        store.AppendDetectorActivity(new(ReportNow().AddSeconds(30),ReportNow().AddSeconds(29),"microphone","Synthetic recorder","Observed active","Synthetic Windows capability metadata; no audio source was opened."));
        store.Commit();
    }
    var reportStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseReport=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    DateTime? lastReport=null;
    await using(var reports=new HourlyReportWriter(reportFile,reportDirectory,summaryOptions,beforeReport:()=>{reportStarted.TrySetResult();return releaseReport.Task;},utcNow:ReportNow)){
        try{
            await WaitReport(()=>reports.Status.StartsWith("Activity summaries"),()=>reports.Status);
            Check(reports.LastGeneratedUtc==null&&!Directory.EnumerateFiles(reportDirectory,"*.csv").Any(),"summary cadence does not fabricate an immediate report before its configured interval");
            AdvanceReportClock();await reportStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Check(reports.LastGeneratedUtc==null,"an in-flight activity summary has no fabricated completion timestamp");
            Task clear=reports.ClearAsync(()=>{using var store=new HistoryStore(reportFile,TimeZoneInfo.Utc);store.Clear();return Task.CompletedTask;});
            Check(!clear.IsCompleted,"confirmed clear waits behind an in-flight summary without racing main-log deletion");
            releaseReport.TrySetResult();await clear.WaitAsync(TimeSpan.FromSeconds(15));
            Check(reports.LastGeneratedUtc==null,"confirmed summary clear removes its persisted completion timestamp");
            Check(!Directory.EnumerateFiles(reportDirectory,"*.csv").Any()&&new HistoryReader(reportFile).Days().Length==0,"serialized clear removes generated summaries and main observations in separate destinations");
            DateTime observed=ReportNow().AddSeconds(30);
            using(var store=new HistoryStore(reportFile,TimeZoneInfo.Utc)){
                store.Begin(observed);var selected=Frame(observed,9,8);store.Append(selected);store.ObserveDetectorSecond(selected);
                store.AppendDetectorActivity(new(observed,observed,"Anomaly observation","Synthetic application","First observed in this monitoring session","An unfamiliar application is an indicator for review, not a threat determination."));store.Commit();
            }
            AdvanceReportClock();await WaitReport(()=>Directory.EnumerateFiles(reportDirectory,"*.csv").Any()&&reports.LastGeneratedUtc!=null,()=>reports.Status);
            string generated=System.IO.File.ReadAllText(Directory.EnumerateFiles(reportDirectory,"*.csv").Single());
            Check(generated.Contains("Anomaly observation")&&generated.Contains("Synthetic application")&&generated.Contains("Persisted network context"),"activity summaries resume after clear and combine awareness with selected network context");
            lastReport=reports.LastGeneratedUtc;Check(lastReport==ReportNow(),"summary completion metadata records the successfully completed configured-clock interval");
        }finally{releaseReport.TrySetResult();}
    }
    await using(var reopenedReports=new HourlyReportWriter(reportFile,reportDirectory,summaryOptions,utcNow:ReportNow)){
        await WaitReport(()=>reopenedReports.LastGeneratedUtc!=null,()=>reopenedReports.Status);
        Check(reopenedReports.LastGeneratedUtc==lastReport&&Directory.EnumerateFiles(reportDirectory,"*.csv").Count()==1,"summary metadata survives reopening without inventing a generation or duplicating output");
    }
    string reportCsv=Directory.EnumerateFiles(reportDirectory,"*.csv").Single();byte[] reportHash=System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(reportCsv));
    using(var legacyIndex=new SqliteConnection("Data Source="+Path.Combine(reportDirectory,"CylonDetector-report-index.sqlite")+";Pooling=False")){legacyIndex.Open();using var q=legacyIndex.CreateCommand();q.CommandText="DROP TABLE report_state;PRAGMA user_version=1";q.ExecuteNonQuery();}
    await using(var legacyReports=new HourlyReportWriter(reportFile,reportDirectory,summaryOptions,utcNow:ReportNow)){
        await WaitReport(()=>legacyReports.Status.StartsWith("Activity summaries"),()=>legacyReports.Status);
        Check(legacyReports.LastGeneratedUtc==null&&reportHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(reportCsv))),"legacy Detector summary-index migration preserves CSV bytes and leaves old completion time explicitly unknown");
    }
    string futureReports=Path.Combine(root,"future-report-index");Directory.CreateDirectory(futureReports);string futureIndex=Path.Combine(futureReports,"CylonDetector-report-index.sqlite");
    using(var db=new SqliteConnection("Data Source="+futureIndex+";Pooling=False")){db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA application_id=1129923664;PRAGMA user_version=99;CREATE TABLE preserve(value TEXT)";q.ExecuteNonQuery();}
    byte[] futureHash=System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(futureIndex));
    await using(var futureWriter=new HourlyReportWriter(reportFile,futureReports,summaryOptions,utcNow:ReportNow)){
        await WaitReport(()=>futureWriter.Status.Contains("Unsupported report index version"),()=>futureWriter.Status);
        Check(futureHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(futureIndex))),"future Detector summary index is rejected without changing the original database");
    }
    await using(var deniedReports=new HourlyReportWriter(reportFile,Path.Combine(root,"denied-report-fixture"),summaryOptions,beforeReport:()=>throw new UnauthorizedAccessException("deliberate report permission fixture"),utcNow:ReportNow)){
        await WaitReport(()=>deniedReports.Status.StartsWith("Activity summaries"),()=>deniedReports.Status);AdvanceReportClock();
        await WaitReport(()=>deniedReports.Status.Contains("deliberate report permission fixture"),()=>deniedReports.Status);
        using(var continuing=new HistoryStore(reportFile,TimeZoneInfo.Utc)){continuing.Append(Frame(t.AddDays(3),17,19));continuing.Commit();}
        Check(deniedReports.Status.Contains("REPORT DELAY/FAILURE")&&new HistoryReader(reportFile).Hours(20260104).Sum(h=>h.Incoming)==17,"summary destination failure remains visible while independent main logging continues");
    }
    string unrelated=Path.Combine(root,"unrelated");Directory.CreateDirectory(unrelated);System.IO.File.WriteAllText(Path.Combine(unrelated,"personal.txt"),"preserve");
    try{StoragePaths.DedicatedDirectory(unrelated);throw new Exception("Unrelated destination accepted");}catch(IOException){Check(System.IO.File.ReadAllText(Path.Combine(unrelated,"personal.txt"))=="preserve","unrelated destination files remain untouched");}
    string link=Path.Combine(root,"linked-destination");Directory.CreateSymbolicLink(link,unrelated);
    try{StoragePaths.LocalDirectory(Path.Combine(link,"child"));throw new Exception("Linked destination accepted");}catch(IOException){Check(true,"reparse ancestor rejected before accessing descendant storage");}finally{Directory.Delete(link);}
    string zeroEntities=FileFor("zero-entities");using(var store=new HistoryStore(zeroEntities,TimeZoneInfo.Utc)){for(int i=0;i<16;i++){store.Begin(t.AddDays(i));store.Append(new(1,t.AddDays(i),1,true,0,0,[new(process,0,0)],"confirmed idle identity"));}store.Retain();store.Commit();var latest=new HistoryReader(zeroEntities).Hours(20260116).Single();Check(store.Count("recorded_days")==15&&new HistoryReader(zeroEntities).Applications(latest.Id).Single().Application==process.Name,"retention preserves application dictionaries referenced by zero-only hourly aggregates");var exactZero=new HistoryReader(zeroEntities).Timeline(t.AddDays(15).Ticks,t.AddDays(15).AddSeconds(1).Ticks).Single();Check(exactZero.Application==process.Name&&exactZero.Pid==process.Pid&&exactZero.CreationTicks==process.StartUtcTicks&&exactZero.Incoming==0,"explicit zero-byte process observation retains its exact identity rather than becoming system-only idle");}
    string peak=FileFor("peaks");using(var store=new HistoryStore(peak,TimeZoneInfo.Utc)){
        store.Begin(t);store.Append(new(1,t,1,true,0,0,[new(process,10,20),new(process with{Pid=43},30,40)],"ok"));store.Commit();
        var apps=new HistoryReader(peak).Applications(new HistoryReader(peak).Hours(20260101).Single().Id);Check(apps.Single().PeakIncoming==40&&apps.Single().PeakOutgoing==60,"hourly application peaks sum sibling processes per second");
        store.Append(new(1,t.AddSeconds(1),1,true,0,0,[new(process with{Pid=44,Path=null},2,3),new(process with{Pid=45,Path=null},2,3),new(null,1,1)],"ok"));store.Commit();Check(store.Count("names")==2&&store.Count("apps")==4,"repeated names intern once while unknown applications remain distinct");
        store.Begin(t);store.Append(Frame(t,4,5));store.Commit();Check(new HistoryReader(peak).Hours(20260101).Single().Ambiguous,"overlapping clock epochs marked ambiguous without overwriting");
    }
    var tz=TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()?"Eastern Standard Time":"America/New_York");string dst=FileFor("dst");
    using(var store=new HistoryStore(dst,tz)){store.Begin(new DateTime(2026,11,1,5,30,0,DateTimeKind.Utc));store.Append(Frame(new(2026,11,1,5,30,0,DateTimeKind.Utc),10));store.Append(Frame(new(2026,11,1,6,30,0,DateTimeKind.Utc),20));store.Commit();}
    var repeated=new HistoryReader(dst).Hours(20261101);Check(repeated.Length==2&&repeated[0].OffsetMinutes!=repeated[1].OffsetMinutes,"DST repeated local hours remain distinct with offsets");
    string corrupt=FileFor("corrupt");System.IO.File.WriteAllText(corrupt,"not a database");try{using var store=new HistoryStore(corrupt);throw new Exception("corruption accepted");}catch(SqliteException){Check(System.IO.File.ReadAllText(corrupt)=="not a database","corrupt database preserved without destructive reset");}
    string newer=FileFor("newer");using(var db=new SqliteConnection("Data Source="+newer)){db.Open();using var c=db.CreateCommand();c.CommandText="PRAGMA application_id=1129923656;PRAGMA user_version=99";c.ExecuteNonQuery();}
    try{using var store=new HistoryStore(newer);throw new Exception("future schema accepted");}catch(InvalidDataException){Check(true,"unsupported schema migration fails safely");}
    try{using var store=new HistoryStore(FileFor("full"),availableBytes:()=>0);store.Begin(t);throw new Exception("disk full accepted");}catch(IOException){Check(true,"disk-full injected before write; no false logging success");}
    var settings=new SettingsFile(root);var options=new Preferences{Indefinite=true,HourlyReports=false,Startup=StartupMode.SignIn};settings.Save(options);Check(settings.Load()==options,"versioned preferences survive restart");settings.Save(options with{StartInTray=true});System.IO.File.WriteAllText(Path.Combine(root,"options.json"),"bad");Check(settings.Load()==options&&settings.Warning!=null,"configuration backup recovery disclosed");
    // Legacy writer admission/lifecycle fixtures explicitly persist every network frame.
    // Detector's production privacy default remains conditional and is tested separately.
    await using(var writer=new HistoryWriter(FileFor("writer"),new(){ContinuousNetwork=true})){writer.Begin();for(int i=0;i<1000;i++)writer.Append(Frame(t.AddSeconds(i),1,2));await Task.Delay(100);Check(writer.Dropped>0,"bounded writer overflow is explicit");}
    using(var openGate=new ManualResetEventSlim(false)){
        string limitedPath=FileFor("byte-budget");var limited=new HistoryWriter(limitedPath,new(){ContinuousNetwork=true},capacity:128,maximumQueuedBytes:512,factory:p=>{openGate.Wait();return new HistoryStore(p,TimeZoneInfo.Utc);});
        var rejectedFrame=Frame(t.AddSeconds(2),33,3,complete:false,coverage:.5);
        bool accepted=limited.Append(Frame(t))&&limited.Append(Frame(t.AddSeconds(1)));bool omitted=!limited.Append(rejectedFrame);
        Check(accepted&&omitted&&limited.Dropped==1&&limited.PeakQueuedBytes==512,"byte budget rejects excess admission before frame-count limit");
        openGate.Set();await limited.DisposeAsync();var detail=new HistoryReader(limitedPath);
        var recovered=detail.Timeline(t.Ticks,t.AddSeconds(4).Ticks).OrderBy(row=>row.UtcTicks).ToArray();
        Check(recovered.Length==3&&recovered.Select(row=>row.UtcTicks).SequenceEqual(new[]{t.Ticks,t.AddSeconds(1).Ticks,rejectedFrame.StartUtc.Ticks})&&
            recovered[0].Incoming==0&&recovered[0].Outgoing==0&&recovered[0].State==HistoryState.Complete&&recovered[0].CoverageSeconds==1&&
            recovered[1].Incoming==0&&recovered[1].Outgoing==0&&recovered[1].State==HistoryState.Complete&&recovered[1].CoverageSeconds==1&&
            recovered[2].Incoming==33&&recovered[2].Outgoing==3&&recovered[2].State==HistoryState.Partial&&recovered[2].CoverageSeconds==.5,
            "shutdown recovers a queue-rejected RAM frame with its exact nonzero bytes and partial coverage, without invented extra seconds");
        Check(detail.Events(20260101).Any(e=>e.State==HistoryState.WriterGap&&e.Reason.Contains(rejectedFrame.StartUtc.Ticks.ToString())),
            "a recovered frame does not erase the original queue-admission failure evidence");
        Check(new DetectorActivityReader(limitedPath).Recent().Any(row=>row.Category=="Persistence / coverage"&&row.Change=="Deferred RAM context persisted"&&
            row.ActivityUtc==rejectedFrame.StartUtc&&row.Detail.Contains(rejectedFrame.StartUtc.ToString("O"))),
            "the unified activity journal explicitly identifies the measured RAM interval recovered during shutdown");
    }
    using(var openGate=new ManualResetEventSlim(false)){
        string expiryPath=FileFor("rejected-ram-expiry");
        var delayed=new HistoryWriter(expiryPath,new(){ContinuousNetwork=true,Indefinite=true},capacity:128,maximumQueuedBytes:512,factory:p=>{openGate.Wait();return new HistoryStore(p,TimeZoneInfo.Utc);});
        Check(delayed.Append(Frame(t))&&delayed.Append(Frame(t.AddSeconds(1))),"RAM-expiry fixture fills its byte budget with two genuine observed idle seconds");
        for(int second=2;second<600;second++)if(delayed.Append(Frame(t.AddSeconds(second),second,second+1)))throw new Exception("Blocked byte-budget fixture unexpectedly admitted a frame");
        openGate.Set();await delayed.DisposeAsync();
        var expiryReader=new HistoryReader(expiryPath);var retained=expiryReader.Timeline(t.Ticks,t.AddSeconds(600).Ticks).OrderBy(row=>row.UtcTicks).ToArray();
        var expectedTicks=new[]{t.Ticks,t.AddSeconds(1).Ticks}.Concat(Enumerable.Range(295,305).Select(second=>t.AddSeconds(second).Ticks)).ToArray();
        Check(retained.Select(row=>row.UtcTicks).SequenceEqual(expectedTicks)&&retained.Skip(2).All(row=>{
            long second=(row.UtcTicks-t.Ticks)/TimeSpan.TicksPerSecond;return row.Incoming==second&&row.Outgoing==second+1&&row.State==HistoryState.Complete&&row.CoverageSeconds==1;
        }),"expired queue-rejected RAM observations remain absent while the exact bounded recent source observations recover on shutdown");
        Check(expiryReader.Events(20260101).Any(e=>e.State==HistoryState.WriterGap&&e.Reason.Contains(t.AddSeconds(2).Ticks.ToString())),"permanent RAM-expiry gaps retain their original queue-admission interval evidence");
    }
    await using(var failed=new HistoryWriter(FileFor("denied"),new(){ContinuousNetwork=true},factory:_=>throw new UnauthorizedAccessException("fixture"))){await Task.Delay(100);Check(!failed.Healthy&&failed.Status.Contains("NOT LOGGING")&&!failed.Append(Frame(t)),"writer permission failure leaves explicit unavailable logging state");}
    System.IO.File.WriteAllText(Path.Combine(root,"options.json"),"{\"Schema\":99}");var futureSettings=new SettingsFile(root);futureSettings.Load();try{futureSettings.Save(new());throw new Exception("future settings overwritten");}catch(InvalidOperationException){Check(System.IO.File.ReadAllText(Path.Combine(root,"options.json"))=="{\"Schema\":99}","future settings remain read-only without downgrade");}
    var changingZone=TimeZoneInfo.Utc;string zoneFile=FileFor("timezone-change");using(var store=new HistoryStore(zoneFile,timeZoneProvider:()=>changingZone)){store.Begin(t);store.Append(Frame(t,10));changingZone=TimeZoneInfo.CreateCustomTimeZone("Fixture plus two",TimeSpan.FromHours(2),"Fixture","Fixture");store.Append(Frame(t.AddSeconds(1),20));store.Commit();var changed=new HistoryReader(zoneFile).Hours(20260101);Check(changed.Length==2&&changed.Any(h=>h.Ambiguous),"timezone changes preserve offsets and mark buffered boundary ambiguity");}
    using(var releaseGate=new ManualResetEventSlim(false)){
        string queueSettings=FileFor("queued-settings");var delayed=new HistoryWriter(queueSettings,new(){ContinuousNetwork=true},1,factory:p=>{releaseGate.Wait();return new HistoryStore(p,TimeZoneInfo.Utc);});delayed.Begin();delayed.Configure(new(){Indefinite=true,ContinuousNetwork=true});releaseGate.Set();
        for(int i=0;i<16;i++){int attempt=0;while(!delayed.Append(Frame(t.AddDays(i)))){if(++attempt>100)throw new Exception("writer did not recover");await Task.Delay(10);}await Task.Delay(10);}
        await delayed.DisposeAsync();
        var retainedDays=new HistoryReader(queueSettings).Days();
        // Detector journals real-time lifecycle/aggregate metadata in addition to
        // the sixteen synthetic dates. Assert every seeded date survives rather
        // than requiring an exact count that excludes that legitimate extra day.
        var seededDays=Enumerable.Range(0,16).Select(day=>int.Parse(t.AddDays(day).ToString("yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Check(seededDays.All(retainedDays.Contains),"indefinite policy change preserves every one of sixteen seeded dates despite a full data queue");
    }
    string disabled=FileFor("disabled-reports");using(var store=new HistoryStore(disabled,TimeZoneInfo.Utc)){store.GenerateHourly=false;store.Begin(t);store.Append(Frame(t,10));store.Append(Frame(t.AddHours(1),20));store.Commit();Check(new HistoryReader(disabled).Hours(20260101).All(h=>!h.Finalized),"disabled automatic reports do not finalize");store.GenerateHourly=true;store.FinalizeHours(t.AddHours(2));store.Commit();Check(new HistoryReader(disabled).Hours(20260101).Length==2&&new HistoryReader(disabled).Hours(20260101).All(h=>h.Finalized),"report recovery finalizes without duplication");}
    string killed=FileFor("killed");var childInfo=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardOutput=true};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")childInfo.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);childInfo.ArgumentList.Add("--crash-fixture");childInfo.ArgumentList.Add(killed);
    using(var child=Process.Start(childInfo)!){if(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15))!="READY")throw new Exception("Crash fixture not ready");child.Kill(true);await child.WaitForExitAsync();}
    using(var recovered=new HistoryStore(killed,TimeZoneInfo.Utc)){recovered.Commit();var saved=new HistoryReader(killed).Timeline(t.Ticks,t.AddSeconds(2).Ticks).ToArray();Check(saved.Length==1&&saved[0].Incoming==111&&saved[0].Outgoing==222,"abrupt process kill recovers committed WAL and never invents uncommitted seconds");}
    string locked=FileFor("locked");using(var original=new HistoryStore(locked,TimeZoneInfo.Utc)){original.Begin(t);original.Append(Frame(t,10));original.Commit();}
    using(var locker=new SqliteConnection("Data Source="+locked+";Pooling=False")){locker.Open();using var command=locker.CreateCommand();command.CommandText="BEGIN IMMEDIATE";command.ExecuteNonQuery();try{using var blocked=new HistoryStore(locked,TimeZoneInfo.Utc);throw new Exception("Writer lock ignored");}catch(SqliteException ex)when(ex.SqliteErrorCode==5){Check(true,"database lock fails explicitly with original committed history retained");}command.CommandText="ROLLBACK";command.ExecuteNonQuery();}
    string precision=FileFor("precision");using(var store=new HistoryStore(precision,TimeZoneInfo.Utc)){store.Begin(t);store.Append(Frame(t,1,2,false,.3333333));store.Commit();Check(new HistoryReader(precision).Timeline(t.Ticks,t.AddSeconds(1).Ticks).Single().CoverageSeconds==.3333333,"coverage preserves original 100-nanosecond interval precision");}
    string migration=FileFor("migration");using(var store=new HistoryStore(migration,TimeZoneInfo.Utc)){store.Begin(t);store.Append(Frame(t,123,456,false,.25));store.Commit();}
    using(var legacy=new SqliteConnection("Data Source="+migration+";Pooling=False")){legacy.Open();using var command=legacy.CreateCommand();command.CommandText="ALTER TABLE coverage RENAME COLUMN coverage_ticks TO coverage_us;ALTER TABLE hours RENAME COLUMN coverage_ticks TO coverage_us;UPDATE coverage SET coverage_us=coverage_us/10;UPDATE hours SET coverage_us=coverage_us/10;PRAGMA user_version=1";command.ExecuteNonQuery();}
    using(var migrated=new HistoryStore(migration,TimeZoneInfo.Utc)){migrated.Commit();var data=new HistoryReader(migration).Timeline(t.Ticks,t.AddSeconds(1).Ticks).Single();Check(data.Incoming==123&&data.Outgoing==456&&data.CoverageSeconds==.25&&System.IO.File.Exists(migration+".pre-schema2.bak"),"transactional schema-1 migration preserves exact counts/coverage with consistent backup");migrated.Clear();Check(!System.IO.File.Exists(migration+".pre-schema2.bak")&&migrated.Count("coverage")==0,"confirmed Clear removes migrated history and its known backup");}
    var watch=Stopwatch.StartNew();long memoryBefore=GC.GetTotalMemory(true);string bench=FileFor("benchmark");long cpuBefore=Process.GetCurrentProcess().TotalProcessorTime.Ticks;
    using(var store=new HistoryStore(bench,TimeZoneInfo.Utc)){store.Begin(t);for(int i=0;i<86400;i++){store.Append(Frame(t.AddSeconds(i),i%60==0?1000:0,i%60==0?2000:0));if(i%60==59)store.Commit();}store.FinalizeHours(t.AddDays(1));store.Commit();Check(store.Count("coverage")==24&&store.Count("traffic")==1440,"actual SQLite writer compact 24-hour sparse dataset");}
    double writerWall=watch.Elapsed.TotalSeconds;double writerCpuMs=(Process.GetCurrentProcess().TotalProcessorTime.Ticks-cpuBefore)/(double)TimeSpan.TicksPerMillisecond;long writerHeapDelta=GC.GetTotalMemory(true)-memoryBefore;long writerWorkingSet=Process.GetCurrentProcess().WorkingSet64;
    var baseline=Path.Combine(root,"verbose-baseline.jsonl");var baselineClock=Stopwatch.StartNew();using(var text=new StreamWriter(baseline)){for(int i=0;i<86400;i++)text.WriteLine(JsonSerializer.Serialize(Frame(t.AddSeconds(i),i%60==0?1000:0,i%60==0?2000:0)));}
    Console.WriteLine(JsonSerializer.Serialize(new{benchmark="production history writer synthetic 24h",databaseBytes=new FileInfo(bench).Length,verboseJsonBytes=new FileInfo(baseline).Length,verboseJsonWallSeconds=baselineClock.Elapsed.TotalSeconds,cpuMs=writerCpuMs,wallSeconds=writerWall,managedMemoryDelta=writerHeapDelta,processWorkingSet=writerWorkingSet,note="Synthetic dataset processing, not 24h physical acceptance; process memory excludes native SQLite attribution"}));
    Console.WriteLine($"{checks} history checks passed");
}finally{SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException){}}
