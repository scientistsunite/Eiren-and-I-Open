using Microsoft.Data.Sqlite;
using Raider.Core;
namespace Raider.History;

// One writer connection, bounded page cache and indexed readers. No ETW or network API dependency.
public sealed class HistoryStore : IDisposable
{
    readonly SqliteConnection db;
    TimeZoneInfo zone;
    readonly Func<TimeZoneInfo> zoneProvider;
    int timezoneBoundary,gcStage;
    long gcCursor,gcMaximum;
    readonly Func<long>? freeSpace;
    string? incomingSource;
    long profileId;
    long epoch, lastTicks, rangeId, rangeFirst, rangeSeconds, rangeHour, rangeCoverage, lastLost, lastLate;
    HistoryState rangeState;
    bool transaction,epochHasCoverage;
    readonly Dictionary<Identity,(long Id,long App)> processCache=new();
    long zoneId,cachedHourId,cachedHourTicks=long.MinValue,lastFinalize=-1;
    int cachedOffset,lastRecordedDay;
    public string FilePath { get; }
    public bool HasTransaction=>transaction;
    public long? LastFreeBytes { get; private set; }
    public bool Indefinite { get; set; }
    public bool DetailedDiagnostics {get;set;}
    public int AttributionRecordedDays {get;set;}=15;
    public bool GenerateHourly { get; set; } = true;
    public HistoryStore(string path,TimeZoneInfo? timeZone=null,Func<long>? availableBytes=null,Func<TimeZoneInfo>? timeZoneProvider=null)
    {
        FilePath=StoragePaths.LocalDirectory(Path.GetFullPath(path));zoneProvider=timeZoneProvider??(()=>timeZone??TimeZoneInfo.Local);zone=zoneProvider();freeSpace=availableBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        db=new(new SqliteConnectionStringBuilder{DataSource=FilePath,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false,DefaultTimeout=2}.ToString());
        try {
            db.Open();
            long version=Scalar("PRAGMA user_version");
            if(version>4) throw new InvalidDataException("History format is newer than this application; database was not reset");
            Exec("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA cache_size=-2048; PRAGMA wal_autocheckpoint=0;");
            if(version==0){if(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table'")>0)throw new InvalidDataException("Unrecognized database; original preserved");Migrate();}
            else {if(version==1) MigrateCoveragePrecision(); if(version<3) MigrateReports();}
            // Additive schema4: sparse integer integrity evidence, no rewrite/scan of old measurements.
            MigrateDiagnostics();
            // Foreign-key/orphan checks must not rescan all retained hourly aggregates
            // for every candidate. Existing experimental databases add this once.
            Exec("CREATE INDEX IF NOT EXISTS hour_apps_app ON hour_apps(app,hour)");
            // SQLite performs WAL recovery at open. Full integrity scans are an explicit
            // offline diagnostic, never an unbounded startup scan ahead of capture.
            lastTicks=Scalar("SELECT COALESCE(first_ticks+(seconds-1)*10000000,0) FROM coverage ORDER BY first_ticks DESC LIMIT 1");
            // An open epoch proves a missing orderly shutdown, never observed zero traffic.
            using var query=Command("SELECT id FROM epochs WHERE ended_ticks IS NULL");
            var interrupted=new List<long>();using(var reader=query.ExecuteReader())while(reader.Read())interrupted.Add(reader.GetInt64(0));
            foreach(var id in interrupted){epoch=id;RecordState(DateTime.UtcNow,HistoryState.Recovery,"Previous writer ended without an orderly stop");Exec("UPDATE epochs SET ended_ticks=$a WHERE id=$b",DateTime.UtcNow.Ticks,id);}
            epoch=0;Commit();
        } catch {db.Dispose();throw;}
    }
    void MigrateDiagnostics()
    {
        Exec("BEGIN IMMEDIATE");
        try{
            Exec("CREATE TABLE IF NOT EXISTS integrity(hour INTEGER NOT NULL REFERENCES hours(id) ON DELETE CASCADE,epoch INTEGER NOT NULL REFERENCES epochs(id),ticks INTEGER NOT NULL,flags INTEGER NOT NULL,os_lost INTEGER NOT NULL,late INTEGER NOT NULL,malformed INTEGER NOT NULL,capacity INTEGER NOT NULL,discard_rx INTEGER NOT NULL,discard_tx INTEGER NOT NULL,affected_from INTEGER NOT NULL,affected_to INTEGER NOT NULL,uncertain_through INTEGER NOT NULL,earlier_unknown INTEGER NOT NULL,PRIMARY KEY(epoch,ticks)) WITHOUT ROWID;CREATE INDEX IF NOT EXISTS integrity_hour ON integrity(hour,ticks);CREATE TABLE IF NOT EXISTS epoch_sources(epoch INTEGER PRIMARY KEY REFERENCES epochs(id) ON DELETE CASCADE,token TEXT NOT NULL);CREATE TABLE IF NOT EXISTS profiles(id INTEGER PRIMARY KEY,json TEXT NOT NULL UNIQUE);CREATE TABLE IF NOT EXISTS epoch_profiles(epoch INTEGER NOT NULL REFERENCES epochs(id) ON DELETE CASCADE,ticks INTEGER NOT NULL,profile INTEGER NOT NULL REFERENCES profiles(id),PRIMARY KEY(epoch,ticks)) WITHOUT ROWID;CREATE INDEX IF NOT EXISTS epoch_profiles_profile ON epoch_profiles(profile,epoch);CREATE TABLE IF NOT EXISTS attribution(hour INTEGER NOT NULL REFERENCES hours(id) ON DELETE CASCADE,epoch INTEGER NOT NULL REFERENCES epochs(id),minute INTEGER NOT NULL,reason INTEGER NOT NULL,protocol INTEGER NOT NULL,raw_pid INTEGER NOT NULL,identified INTEGER NOT NULL,events INTEGER NOT NULL,rx INTEGER NOT NULL,tx INTEGER NOT NULL,first_event INTEGER NOT NULL,last_event INTEGER NOT NULL,first_receipt INTEGER NOT NULL,last_receipt INTEGER NOT NULL,PRIMARY KEY(epoch,minute,reason,protocol,raw_pid,identified)) WITHOUT ROWID;CREATE INDEX IF NOT EXISTS attribution_hour ON attribution(hour,minute);");
            if(Scalar("SELECT COUNT(*) FROM pragma_table_info('integrity') WHERE name='unsupported'")==0)Exec("ALTER TABLE integrity ADD COLUMN unsupported INTEGER NOT NULL DEFAULT 0");
            Exec("PRAGMA user_version=4;COMMIT");
        }catch{Exec("ROLLBACK");throw;}
    }
    void Migrate()
    {
        Exec("BEGIN IMMEDIATE");
        try {
            Exec("""
            CREATE TABLE paths(id INTEGER PRIMARY KEY AUTOINCREMENT,value TEXT UNIQUE COLLATE NOCASE);
            INSERT INTO paths VALUES(0,NULL);
            CREATE TABLE names(id INTEGER PRIMARY KEY AUTOINCREMENT,value TEXT UNIQUE NOT NULL);
            INSERT INTO names VALUES(0,'Unattributed');
            CREATE TABLE apps(id INTEGER PRIMARY KEY AUTOINCREMENT,key TEXT NOT NULL UNIQUE,name INTEGER NOT NULL REFERENCES names(id),path INTEGER NOT NULL REFERENCES paths(id));
            CREATE INDEX apps_name ON apps(name); CREATE INDEX apps_path ON apps(path);
            INSERT INTO apps VALUES(0,'?',0,0);
            CREATE TABLE epochs(id INTEGER PRIMARY KEY AUTOINCREMENT,token TEXT UNIQUE NOT NULL,requested_ticks INTEGER NOT NULL,first_coverage_ticks INTEGER,ended_ticks INTEGER,monotonic INTEGER NOT NULL,frequency INTEGER NOT NULL);
            INSERT INTO epochs(id,token,requested_ticks,ended_ticks,monotonic,frequency) VALUES(0,'unknown',0,0,0,1);
            CREATE TABLE processes(id INTEGER PRIMARY KEY AUTOINCREMENT,app INTEGER NOT NULL REFERENCES apps(id),epoch INTEGER NOT NULL REFERENCES epochs(id),pid INTEGER NOT NULL,created_ticks INTEGER NOT NULL,UNIQUE(epoch,pid,created_ticks));
            INSERT INTO processes VALUES(0,0,0,0,0);
            CREATE INDEX processes_app ON processes(app);
            CREATE TABLE zones(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL);
            CREATE TABLE hours(id INTEGER PRIMARY KEY AUTOINCREMENT,start_ticks INTEGER NOT NULL,day INTEGER NOT NULL,offset INTEGER NOT NULL,zone INTEGER NOT NULL REFERENCES zones(id),rx INTEGER NOT NULL DEFAULT 0,tx INTEGER NOT NULL DEFAULT 0,peak_rx INTEGER NOT NULL DEFAULT 0,peak_tx INTEGER NOT NULL DEFAULT 0,coverage_ticks INTEGER NOT NULL DEFAULT 0,incomplete INTEGER NOT NULL DEFAULT 0,finalized INTEGER NOT NULL DEFAULT 0,ambiguous INTEGER NOT NULL DEFAULT 0,report_sequence INTEGER NOT NULL DEFAULT 0,retiring INTEGER NOT NULL DEFAULT 0,revision INTEGER NOT NULL DEFAULT 0,UNIQUE(start_ticks,offset,zone));
            CREATE INDEX hours_day ON hours(day,start_ticks);
            CREATE INDEX hours_open ON hours(start_ticks) WHERE finalized=0;
            CREATE TABLE coverage(id INTEGER PRIMARY KEY AUTOINCREMENT,epoch INTEGER NOT NULL REFERENCES epochs(id),first_ticks INTEGER NOT NULL,seconds INTEGER NOT NULL CHECK(seconds BETWEEN 1 AND 3600),state INTEGER NOT NULL,coverage_ticks INTEGER NOT NULL,hour INTEGER NOT NULL REFERENCES hours(id) ON DELETE CASCADE,lost INTEGER NOT NULL,late INTEGER NOT NULL,UNIQUE(epoch,first_ticks));
            CREATE INDEX coverage_time ON coverage(first_ticks);
            CREATE INDEX coverage_hour ON coverage(hour);
            CREATE TABLE traffic(coverage INTEGER NOT NULL REFERENCES coverage(id) ON DELETE CASCADE,second INTEGER NOT NULL,process INTEGER NOT NULL REFERENCES processes(id),rx INTEGER NOT NULL,tx INTEGER NOT NULL,PRIMARY KEY(coverage,second,process)) WITHOUT ROWID;
            CREATE INDEX traffic_process ON traffic(process,coverage);
            CREATE TABLE hour_apps(hour INTEGER NOT NULL REFERENCES hours(id) ON DELETE CASCADE,app INTEGER NOT NULL REFERENCES apps(id),rx INTEGER NOT NULL,tx INTEGER NOT NULL,peak_rx INTEGER NOT NULL,peak_tx INTEGER NOT NULL,PRIMARY KEY(hour,app)) WITHOUT ROWID;
            CREATE TABLE reasons(id INTEGER PRIMARY KEY AUTOINCREMENT,message TEXT UNIQUE NOT NULL);
            CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,epoch INTEGER NOT NULL REFERENCES epochs(id),ticks INTEGER NOT NULL,day INTEGER NOT NULL,state INTEGER NOT NULL,reason INTEGER NOT NULL REFERENCES reasons(id));
            CREATE INDEX events_day ON events(day,ticks);
            CREATE INDEX events_reason ON events(reason); CREATE INDEX events_epoch ON events(epoch);
            CREATE TABLE recorded_days(day INTEGER PRIMARY KEY) WITHOUT ROWID;
            CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL) WITHOUT ROWID;
            INSERT INTO metadata VALUES('report_seq','0');
            INSERT INTO metadata VALUES('retention_cutoff','0');
            INSERT INTO metadata VALUES('history_id',$a);
            CREATE INDEX hours_reports ON hours(report_sequence,id) WHERE finalized=1;
            PRAGMA user_version=3;
            COMMIT;
            """,Guid.NewGuid().ToString("N"));
        } catch {Exec("ROLLBACK");throw;}
    }
    void MigrateReports()
    {
        Exec("BEGIN IMMEDIATE");
        try {
            if(Scalar("SELECT COUNT(*) FROM pragma_table_info('hours') WHERE name='report_sequence'")==0)Exec("ALTER TABLE hours ADD COLUMN report_sequence INTEGER NOT NULL DEFAULT 0");
            if(Scalar("SELECT COUNT(*) FROM pragma_table_info('hours') WHERE name='revision'")==0)Exec("ALTER TABLE hours ADD COLUMN revision INTEGER NOT NULL DEFAULT 0");
            if(Scalar("SELECT COUNT(*) FROM pragma_table_info('hours') WHERE name='retiring'")==0)Exec("ALTER TABLE hours ADD COLUMN retiring INTEGER NOT NULL DEFAULT 0");
            Exec("CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL) WITHOUT ROWID;INSERT OR IGNORE INTO metadata VALUES('report_seq','0');INSERT OR IGNORE INTO metadata VALUES('retention_cutoff','0');CREATE INDEX IF NOT EXISTS hours_reports ON hours(report_sequence,id) WHERE finalized=1;PRAGMA user_version=3");
            Exec("INSERT OR IGNORE INTO metadata VALUES('history_id',$a)",Guid.NewGuid().ToString("N"));Exec("COMMIT");
        } catch {Exec("ROLLBACK");throw;}
    }
    void MigrateCoveragePrecision()
    {
        if(Scalar("SELECT COUNT(*) FROM pragma_table_info('coverage') WHERE name='coverage_us'")!=1||Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='names'")!=1)throw new InvalidDataException("Unrecognized experimental schema 1; original database preserved");
        if(Scalar("SELECT COUNT(*) FROM coverage WHERE coverage_us<0 OR coverage_us>1000000")>0||Scalar("SELECT COUNT(*) FROM hours WHERE coverage_us<0 OR coverage_us>922337203685477580")>0)throw new InvalidDataException("Invalid coverage prevents exact migration");
        string backup=FilePath+".pre-schema2.bak";if(File.Exists(backup))throw new IOException("Migration backup already exists; preserve/review it before retrying migration");
        using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString())){destination.Open();db.BackupDatabase(destination);}
        Exec("BEGIN IMMEDIATE");
        try{Exec("ALTER TABLE coverage RENAME COLUMN coverage_us TO coverage_ticks;ALTER TABLE hours RENAME COLUMN coverage_us TO coverage_ticks;UPDATE coverage SET coverage_ticks=coverage_ticks*10;UPDATE hours SET coverage_ticks=coverage_ticks*10;PRAGMA user_version=2;COMMIT;");}
        catch{Exec("ROLLBACK");throw;}
    }
    void BeginWrite()
    {
        if(!transaction){LastFreeBytes=freeSpace?.Invoke();if(LastFreeBytes<16*1024*1024)throw new IOException("Insufficient free disk space; live monitoring may continue without logging");Exec("BEGIN IMMEDIATE");transaction=true;}
    }
    public void ConfigureProfile(RecordingProfile profile)
    {
        string value=System.Text.Json.JsonSerializer.Serialize(profile);BeginWrite();Exec("INSERT OR IGNORE INTO profiles(json) VALUES($a)",value);long id=Scalar("SELECT id FROM profiles WHERE json=$a",value);
        if(id==profileId)return;profileId=id;if(epoch!=0)Exec("INSERT OR REPLACE INTO epoch_profiles VALUES($a,$b,$c)",epoch,DateTime.UtcNow.Ticks,profileId);
    }
    public void Begin(DateTime utc,string? token=null)
    {
        BeginWrite();
        Exec("INSERT INTO epochs(token,requested_ticks,monotonic,frequency) VALUES($a,$b,$c,$d)",token??Guid.NewGuid().ToString("N"),utc.ToUniversalTime().Ticks,System.Diagnostics.Stopwatch.GetTimestamp(),System.Diagnostics.Stopwatch.Frequency);
        epoch=Scalar("SELECT last_insert_rowid()");incomingSource=null;if(profileId!=0)Exec("INSERT INTO epoch_profiles VALUES($a,$b,$c)",epoch,utc.ToUniversalTime().Ticks,profileId);rangeId=0;epochHasCoverage=false;processCache.Clear();
        RecordState(utc,HistoryState.Unavailable,"Waiting for actual collector measurements");
    }
    public void RecordState(DateTime utc,HistoryState state,string message)
    {
        BeginWrite();
        Exec("INSERT OR IGNORE INTO reasons(message) VALUES($a)",message);
        long reason=Scalar("SELECT id FROM reasons WHERE message=$a",message);
        int day=Day(TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(),zone));
        Exec("INSERT INTO events(epoch,ticks,day,state,reason) VALUES($a,$b,$c,$d,$e)",epoch,utc.ToUniversalTime().Ticks,day,(int)state,reason);
        if(state is HistoryState.Stopped or HistoryState.Paused or HistoryState.Interrupted){Exec("UPDATE epochs SET ended_ticks=$a WHERE id=$b",utc.ToUniversalTime().Ticks,epoch);epoch=0;rangeId=0;processCache.Clear();cachedHourId=0;}
    }
    static int Day(DateTime date)=>date.Year*10000+date.Month*100+date.Day;
    (long Id,long App) Entity(Identity? process)
    {
        if(process is null) return (0,0);
        if(processCache.TryGetValue(process,out var cached))return cached;
        long path=0;
        if(!string.IsNullOrEmpty(process.Path)) {Exec("INSERT OR IGNORE INTO paths(value) VALUES($a)",process.Path);path=Scalar("SELECT id FROM paths WHERE value=$a COLLATE NOCASE",process.Path);}
        string key=path==0?$"unresolved:{epoch}:{process.Pid}:{process.StartUtcTicks}":"path:"+path;
        Exec("INSERT OR IGNORE INTO names(value) VALUES($a)",process.Name);long name=Scalar("SELECT id FROM names WHERE value=$a",process.Name);
        Exec("INSERT OR IGNORE INTO apps(key,name,path) VALUES($a,$b,$c)",key,name,path);
        long app=Scalar("SELECT id FROM apps WHERE key=$a",key);
        Exec("INSERT OR IGNORE INTO processes(app,epoch,pid,created_ticks) VALUES($a,$b,$c,$d)",app,epoch,process.Pid,process.StartUtcTicks);
        long id=Scalar("SELECT id FROM processes WHERE epoch=$a AND pid=$b AND created_ticks=$c",epoch,process.Pid,process.StartUtcTicks);
        if(processCache.Count<8192)processCache[process]=(id,app);return (id,app);
    }
    public void Append(Measurement frame)
    {
        BeginWrite();if(epoch==0)Begin(frame.StartUtc);
        if(frame.SourceSession is {} token&&token!=incomingSource){
            if(incomingSource!=null){RecordState(frame.StartUtc,HistoryState.Interrupted,"Collector source session changed; independent recording epoch");Begin(frame.StartUtc);}
        }
        if(frame.Schema!=1 || frame.CoverageSeconds<0 || frame.CoverageSeconds>1)throw new InvalidDataException("Unsupported or invalid collector measurement");
        var currentZone=zoneProvider();
        if(!zone.Equals(currentZone)){if(cachedHourId!=0)Exec("UPDATE hours SET ambiguous=1,revision=revision+1,finalized=0 WHERE id=$a",cachedHourId);zone=currentZone;zoneId=0;cachedHourId=0;timezoneBoundary=3;RecordState(DateTime.UtcNow,HistoryState.ClockChange,"Timezone changed; buffered boundary has explicit ambiguity");}
        var utc=frame.StartUtc.ToUniversalTime();
        var local=TimeZoneInfo.ConvertTime(new DateTimeOffset(utc),zone);
        int day=Day(local.DateTime),offset=(int)local.Offset.TotalMinutes;
        long hourTicks=new DateTimeOffset(local.Year,local.Month,local.Day,local.Hour,0,0,local.Offset).UtcTicks;
        if(zoneId==0){Exec("INSERT OR IGNORE INTO zones(name) VALUES($a)",zone.Id);zoneId=Scalar("SELECT id FROM zones WHERE name=$a",zone.Id);}
        if(cachedHourTicks!=hourTicks||cachedOffset!=offset||cachedHourId==0){Exec("INSERT OR IGNORE INTO hours(start_ticks,day,offset,zone) VALUES($a,$b,$c,$d)",hourTicks,day,offset,zoneId);cachedHourId=Scalar("SELECT id FROM hours WHERE start_ticks=$a AND offset=$b AND zone=$c",hourTicks,offset,zoneId);cachedHourTicks=hourTicks;cachedOffset=offset;}
        long hour=cachedHourId;
        if(timezoneBoundary>0){Exec("UPDATE hours SET ambiguous=1 WHERE id=$a",hour);timezoneBoundary--;}
        if(utc.Ticks<=lastTicks){
            RecordState(utc,HistoryState.ClockChange,"Overlapping wall-clock interval; epochs remain distinct and coverage may overlap");
            Exec("UPDATE hours SET ambiguous=1 WHERE id=$a",hour);
            // A repeated timestamp within an epoch becomes a distinct integrity epoch, not an overwrite.
            if(Scalar("SELECT COUNT(*) FROM coverage WHERE epoch=$a AND first_ticks<=$b AND first_ticks+seconds*10000000>$b",epoch,utc.Ticks)>0)Begin(utc);
        }
        // Attach after clock-overlap handling: Begin may have opened another epoch.
        if(frame.SourceSession is {} source&&source!=incomingSource){Exec("INSERT OR REPLACE INTO epoch_sources VALUES($a,$b)",epoch,source);incomingSource=source;}
        if(frame.Quality is {} q&&(q.OsLostDelta+q.LateDelta+q.MalformedDelta+q.CapacityDelta+q.UnsupportedDelta>0||(q.Flags&(IntegrityFlags.Recovery|IntegrityFlags.Discontinuity))!=0)){
            Exec("INSERT INTO integrity(hour,epoch,ticks,flags,os_lost,late,malformed,capacity,discard_rx,discard_tx,affected_from,affected_to,uncertain_through,earlier_unknown,unsupported) VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n,$o)",hour,epoch,utc.Ticks,(int)q.Flags,q.OsLostDelta,q.LateDelta,q.MalformedDelta,q.CapacityDelta,q.DiscardedIncoming,q.DiscardedOutgoing,q.AffectedFromTicks,q.AffectedToTicks,q.UncertainThroughTicks,q.EarlierImpactUnknown?1:0,q.UnsupportedDelta);
        }
        foreach(var evidence in frame.Attribution){
            long minute=utc.Ticks/TimeSpan.TicksPerMinute*TimeSpan.TicksPerMinute;
            int pid=DetailedDiagnostics?evidence.RawPid??0:0;
            Exec("INSERT INTO attribution VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n) ON CONFLICT(epoch,minute,reason,protocol,raw_pid,identified) DO UPDATE SET events=events+excluded.events,rx=rx+excluded.rx,tx=tx+excluded.tx,first_event=MIN(first_event,excluded.first_event),last_event=MAX(last_event,excluded.last_event),first_receipt=MIN(first_receipt,excluded.first_receipt),last_receipt=MAX(last_receipt,excluded.last_receipt)",hour,epoch,minute,(int)evidence.Reason,(int)evidence.Protocol,pid,evidence.IdentifiedProcess?1:0,evidence.Events,evidence.Incoming,evidence.Outgoing,evidence.FirstEventTicks,evidence.LastEventTicks,evidence.FirstReceiptTicks,evidence.LastReceiptTicks);
        }
        lastTicks=utc.Ticks;
        var state=frame.CoverageSeconds<=0?HistoryState.Unavailable:frame.Complete&&frame.CoverageSeconds==1?HistoryState.Complete:HistoryState.Partial;
        long observedTicks=(long)Math.Round(frame.CoverageSeconds*TimeSpan.TicksPerSecond);
        bool extend=rangeId!=0 && utc.Ticks==rangeFirst+rangeSeconds*TimeSpan.TicksPerSecond && rangeHour==hour && rangeState==state && rangeCoverage==observedTicks && lastLost==frame.LostEvents && lastLate==frame.LateEvents;
        long second;
        if(extend){second=rangeSeconds++;Exec("UPDATE coverage SET seconds=seconds+1 WHERE id=$a",rangeId);}
        else {
            Exec("INSERT INTO coverage(epoch,first_ticks,seconds,state,coverage_ticks,hour,lost,late) VALUES($a,$b,1,$c,$d,$e,$f,$g)",epoch,utc.Ticks,(int)state,observedTicks,hour,frame.LostEvents,frame.LateEvents);
            rangeId=Scalar("SELECT last_insert_rowid()");rangeFirst=utc.Ticks;rangeSeconds=1;rangeHour=hour;rangeState=state;rangeCoverage=observedTicks;lastLost=frame.LostEvents;lastLate=frame.LateEvents;second=0;
        }
        long rx=0,tx=0;var apps=new Dictionary<long,(long In,long Out)>();
        foreach(var row in frame.Rows){
            if(row.Incoming<0||row.Outgoing<0)throw new InvalidDataException("Negative byte count");
            var entity=Entity(row.Process);long pid=entity.Id,app=entity.App;
            Exec("INSERT INTO traffic VALUES($a,$b,$c,$d,$e) ON CONFLICT(coverage,second,process) DO UPDATE SET rx=rx+excluded.rx,tx=tx+excluded.tx",rangeId,second,pid,row.Incoming,row.Outgoing);
            rx=checked(rx+row.Incoming);tx=checked(tx+row.Outgoing);apps.TryGetValue(app,out var count);apps[app]=(checked(count.In+row.Incoming),checked(count.Out+row.Outgoing));
        }
        Exec("UPDATE hours SET rx=rx+$a,tx=tx+$b,peak_rx=MAX(peak_rx,$a),peak_tx=MAX(peak_tx,$b),coverage_ticks=coverage_ticks+$c,incomplete=incomplete+$d,finalized=0,revision=revision+1 WHERE id=$e",rx,tx,observedTicks,frame.Complete?0:1,hour);
        foreach(var entry in apps)Exec("INSERT INTO hour_apps VALUES($a,$b,$c,$d,$c,$d) ON CONFLICT(hour,app) DO UPDATE SET rx=rx+excluded.rx,tx=tx+excluded.tx,peak_rx=MAX(peak_rx,excluded.peak_rx),peak_tx=MAX(peak_tx,excluded.peak_tx)",hour,entry.Key,entry.Value.In,entry.Value.Out);
        if(observedTicks>0&&!epochHasCoverage){Exec("UPDATE epochs SET first_coverage_ticks=$a WHERE id=$b",utc.Ticks+TimeSpan.TicksPerSecond-observedTicks,epoch);epochHasCoverage=true;}
        if(observedTicks>0&&lastRecordedDay!=day){Exec("INSERT OR IGNORE INTO recorded_days VALUES($a)",day);lastRecordedDay=day;}
        if(GenerateHourly&&lastFinalize!=utc.Ticks/TimeSpan.TicksPerHour){FinalizeObservedHours(utc);lastFinalize=utc.Ticks/TimeSpan.TicksPerHour;}
    }
    public void FinalizeObservedHours(DateTime now)
    {
        // Runtime watermark: wall time alone cannot race delayed frame persistence.
        long horizon=epoch!=0?Math.Min(now.ToUniversalTime().Ticks,lastTicks):now.ToUniversalTime().Ticks;
        if(horizon>0)FinalizeHours(new DateTime(horizon,DateTimeKind.Utc));
    }
    public void FinalizeHours(DateTime now)
    {
        long end=now.ToUniversalTime().Ticks-TimeSpan.TicksPerHour;
        if(Scalar("SELECT EXISTS(SELECT 1 FROM hours WHERE finalized=0 AND start_ticks<=$a)",end)==0)return;
        BeginWrite();Exec("UPDATE metadata SET value=CAST(value AS INTEGER)+1 WHERE key='report_seq'");
        long sequence=Scalar("SELECT value FROM metadata WHERE key='report_seq'");
        Exec("UPDATE hours SET finalized=1,report_sequence=$a WHERE id IN (SELECT id FROM hours WHERE finalized=0 AND start_ticks<=$b ORDER BY start_ticks LIMIT 64)",sequence,end);
    }
    // The hot writer performs one bounded slice, never cascades an entire busy day.
    public bool RetainStep(int limit=256)
    {
        if(AttributionRecordedDays>0){
            long diagnosticsCutoff=Scalar("SELECT COALESCE(MIN(day),0) FROM (SELECT day FROM recorded_days ORDER BY day DESC LIMIT $a)",AttributionRecordedDays);
            if(diagnosticsCutoff>0){BeginWrite();Exec("DELETE FROM attribution WHERE (epoch,minute,reason,protocol,raw_pid,identified) IN (SELECT a.epoch,a.minute,a.reason,a.protocol,a.raw_pid,a.identified FROM attribution a JOIN hours h ON h.id=a.hour WHERE h.day<$a LIMIT $b)",diagnosticsCutoff,limit);if(Scalar("SELECT changes()")>0)Exec("INSERT INTO metadata VALUES('diagnostic_revision','1') ON CONFLICT(key) DO UPDATE SET value=CAST(value AS INTEGER)+1");}
        }
        if(Indefinite){SetRetentionCutoff(0);return false;}
        long cutoff=Scalar("SELECT COALESCE(MIN(day),0) FROM (SELECT day FROM recorded_days ORDER BY day DESC LIMIT 15)");
        if(cutoff==0||Scalar("SELECT EXISTS(SELECT 1 FROM recorded_days WHERE day<$a)",cutoff)==0)return false;
        SetRetentionCutoff(cutoff);BeginWrite();
        Exec("UPDATE hours SET retiring=1 WHERE id IN (SELECT id FROM hours WHERE day<$a AND retiring=0 LIMIT 64)",cutoff);
        Exec("DELETE FROM traffic WHERE (coverage,second,process) IN (SELECT t.coverage,t.second,t.process FROM coverage c JOIN traffic t ON t.coverage=c.id WHERE c.hour IN (SELECT id FROM hours WHERE day<$a AND retiring=1) LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT changes()")>0)return true;
        Exec("DELETE FROM coverage WHERE id IN (SELECT c.id FROM coverage c JOIN hours h ON h.id=c.hour WHERE h.day<$a AND h.retiring=1 AND NOT EXISTS(SELECT 1 FROM traffic WHERE coverage=c.id) LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT changes()")>0){rangeId=0;return true;}
        Exec("DELETE FROM hour_apps WHERE (hour,app) IN (SELECT a.hour,a.app FROM hour_apps a JOIN hours h ON h.id=a.hour WHERE h.day<$a AND h.retiring=1 LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT changes()")>0)return true;
        Exec("DELETE FROM attribution WHERE (epoch,minute,reason,protocol,raw_pid,identified) IN (SELECT a.epoch,a.minute,a.reason,a.protocol,a.raw_pid,a.identified FROM attribution a JOIN hours h ON h.id=a.hour WHERE h.day<$a LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT changes()")>0)return true;
        Exec("DELETE FROM integrity WHERE (epoch,ticks) IN (SELECT i.epoch,i.ticks FROM integrity i JOIN hours h ON h.id=i.hour WHERE h.day<$a LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT changes()")>0)return true;
        Exec("DELETE FROM hours WHERE id IN (SELECT id FROM hours WHERE day<$a AND NOT EXISTS(SELECT 1 FROM coverage WHERE hour=hours.id) AND NOT EXISTS(SELECT 1 FROM hour_apps WHERE hour=hours.id) LIMIT $b)",cutoff,limit);
        Exec("DELETE FROM events WHERE id IN (SELECT id FROM events WHERE day<$a LIMIT $b)",cutoff,limit);
        if(Scalar("SELECT EXISTS(SELECT 1 FROM hours WHERE day<$a) OR EXISTS(SELECT 1 FROM events WHERE day<$a)",cutoff)>0)return true;
        // Advance a primary-key cursor; never scan a large dictionary looking for orphans.
        string[] tables=["processes","apps","paths","names","epochs","reasons","profiles"];
        string[] predicates=["id<>0 AND NOT EXISTS(SELECT 1 FROM traffic WHERE process=processes.id)","id<>0 AND NOT EXISTS(SELECT 1 FROM processes WHERE app=apps.id) AND NOT EXISTS(SELECT 1 FROM hour_apps WHERE app=apps.id)","id<>0 AND NOT EXISTS(SELECT 1 FROM apps WHERE path=paths.id)","id<>0 AND NOT EXISTS(SELECT 1 FROM apps WHERE name=names.id)","id<>0 AND id<>"+epoch+" AND NOT EXISTS(SELECT 1 FROM coverage WHERE epoch=epochs.id) AND NOT EXISTS(SELECT 1 FROM processes WHERE epoch=epochs.id) AND NOT EXISTS(SELECT 1 FROM events WHERE epoch=epochs.id)","NOT EXISTS(SELECT 1 FROM events WHERE reason=reasons.id)","id<>"+profileId+" AND NOT EXISTS(SELECT 1 FROM epoch_profiles WHERE profile=profiles.id)"];
        string table=tables[gcStage];if(gcCursor==0)gcMaximum=Scalar("SELECT COALESCE(MAX(id),0) FROM "+table);
        long next=Scalar("SELECT COALESCE(MAX(id),0) FROM (SELECT id FROM "+table+" WHERE id>$a AND id<=$b ORDER BY id LIMIT $c)",gcCursor,gcMaximum,limit);
        if(next>0){Exec("DELETE FROM "+table+" WHERE id>$a AND id<=$b AND "+predicates[gcStage],gcCursor,next);gcCursor=next;processCache.Clear();return true;}
        gcCursor=0;if(++gcStage<tables.Length)return true;
        gcStage=0;Exec("DELETE FROM recorded_days WHERE day<$a",cutoff);processCache.Clear();rangeId=0;cachedHourId=0;lastRecordedDay=0;return false;
    }
    public void CheckpointPassive(){if(transaction)throw new InvalidOperationException("Commit before checkpoint");Exec("PRAGMA wal_checkpoint(PASSIVE)");}
    void SetRetentionCutoff(long day)
    {
        if(Scalar("SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0)")==day)return;
        BeginWrite();Exec("INSERT INTO metadata VALUES('retention_cutoff',$a) ON CONFLICT(key) DO UPDATE SET value=excluded.value",day.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public void Retain()
    {
        if(Indefinite){SetRetentionCutoff(0);return;}
        BeginWrite();var days=new List<long>();using(var command=Command("SELECT day FROM recorded_days ORDER BY day DESC LIMIT -1 OFFSET 15"))using(var reader=command.ExecuteReader())while(reader.Read())days.Add(reader.GetInt64(0));
        if(days.Count>0){long cutoff=Scalar("SELECT MIN(day) FROM (SELECT day FROM recorded_days ORDER BY day DESC LIMIT 15)");SetRetentionCutoff(cutoff);Exec("DELETE FROM hours WHERE day<$a",cutoff);Exec("DELETE FROM events WHERE day<$a",cutoff);Exec("DELETE FROM recorded_days WHERE day<$a",cutoff);}
        if(days.Count>0){Exec("DELETE FROM processes WHERE id<>0 AND NOT EXISTS(SELECT 1 FROM traffic WHERE process=processes.id)");Exec("DELETE FROM apps WHERE id<>0 AND NOT EXISTS(SELECT 1 FROM processes WHERE app=apps.id) AND NOT EXISTS(SELECT 1 FROM hour_apps WHERE app=apps.id)");Exec("DELETE FROM paths WHERE id<>0 AND NOT EXISTS(SELECT 1 FROM apps WHERE path=paths.id)");Exec("DELETE FROM names WHERE id<>0 AND NOT EXISTS(SELECT 1 FROM apps WHERE name=names.id)");Exec("DELETE FROM epochs WHERE id<>0 AND id<>$a AND NOT EXISTS(SELECT 1 FROM coverage WHERE epoch=epochs.id) AND NOT EXISTS(SELECT 1 FROM processes WHERE epoch=epochs.id) AND NOT EXISTS(SELECT 1 FROM events WHERE epoch=epochs.id)",epoch);Exec("DELETE FROM reasons WHERE NOT EXISTS(SELECT 1 FROM events WHERE reason=reasons.id)");rangeId=0;processCache.Clear();cachedHourId=0;}
    }
    public void Clear()
    {
        BeginWrite();SetRetentionCutoff(0);Exec("DELETE FROM hours;DELETE FROM events;DELETE FROM processes WHERE id<>0;DELETE FROM apps WHERE id<>0;DELETE FROM paths WHERE id<>0;DELETE FROM names WHERE id<>0;DELETE FROM recorded_days;DELETE FROM epochs WHERE id<>0;DELETE FROM reasons;DELETE FROM profiles WHERE id<>$a;",profileId);epoch=0;rangeId=0;lastTicks=0;cachedHourId=0;lastRecordedDay=0;epochHasCoverage=false;processCache.Clear();Commit();Exec("PRAGMA wal_checkpoint(PASSIVE)");File.Delete(FilePath+".pre-schema2.bak");
    }
    public void Commit(){if(transaction){Exec("COMMIT");transaction=false;}}
    public void Dispose(){if(transaction){Exec("ROLLBACK");transaction=false;}db.Dispose();}
    public long Count(string table){if(!new[]{"paths","names","apps","processes","coverage","traffic","hours","hour_apps","recorded_days","epochs","events","integrity","profiles","epoch_profiles","epoch_sources","attribution"}.Contains(table))throw new ArgumentException("Unknown table");return Scalar("SELECT COUNT(*) FROM "+table);}
    SqliteCommand Command(string sql,params object?[] values){var command=db.CreateCommand();command.CommandText=sql;for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue("$"+(char)('a'+i),values[i]??DBNull.Value);return command;}
    void Exec(string sql,params object?[] values){using var command=Command(sql,values);command.ExecuteNonQuery();}
    long Scalar(string sql,params object?[] values){using var command=Command(sql,values);return Convert.ToInt64(command.ExecuteScalar()??0);}
    long Scalar(string sql,bool text){using var command=Command(sql);return command.ExecuteScalar()?.ToString()=="ok"?1:0;}
}
