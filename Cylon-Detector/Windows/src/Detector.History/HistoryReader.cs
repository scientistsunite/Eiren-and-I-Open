using System.Globalization;
using Microsoft.Data.Sqlite;
namespace Detector.History;

public sealed class HistoryReader(string path)
{
    SqliteConnection Open(){StoragePaths.LocalDirectory(path);var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=2}.ToString());db.Open();return db;}
    static SqliteCommand Query(SqliteConnection db,string sql,params object[] values){var c=db.CreateCommand();c.CommandText=sql;for(int i=0;i<values.Length;i++)c.Parameters.AddWithValue("$"+(char)('a'+i),values[i]);return c;}
    public string HistoryId(){using var db=Open();using var q=Query(db,"SELECT value FROM metadata WHERE key='history_id'");return (string)q.ExecuteScalar()!;}
    long DiagnosticRevision(){using var db=Open();using var q=Query(db,"SELECT COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='diagnostic_revision'),0)");return Convert.ToInt64(q.ExecuteScalar());}
    public int[] Days(){using var db=Open();using var c=Query(db,"SELECT day FROM recorded_days WHERE day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY day DESC");using var r=c.ExecuteReader();var result=new List<int>();while(r.Read())result.Add(r.GetInt32(0));return result.ToArray();}
    public HourRow[] Hours(int day)
    {
        using var db=Open();using var c=Query(db,"SELECT h.id,h.start_ticks,h.day,h.offset,z.name,h.rx,h.tx,h.peak_rx,h.peak_tx,h.coverage_ticks,h.incomplete,h.finalized,h.ambiguous,h.revision FROM hours h JOIN zones z ON z.id=h.zone WHERE h.day=$a AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY h.start_ticks",day);using var r=c.ExecuteReader();var list=new List<HourRow>();
        while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetInt64(9)/(double)TimeSpan.TicksPerSecond,r.GetInt64(10),r.GetBoolean(11),r.GetBoolean(12),r.GetInt64(13)));return list.ToArray();
    }
    public ApplicationHour[] Applications(long hour)=>ApplicationPage(hour,-1,10000).Select(x=>x.Value).ToArray();
    List<(long Id,ApplicationHour Value)> ApplicationPage(long hour,long after,int limit)
    {
        using var db=Open();using var c=Query(db,"SELECT a.id,n.value,p.value,h.rx,h.tx,h.peak_rx,h.peak_tx FROM hour_apps h JOIN apps a ON a.id=h.app JOIN names n ON n.id=a.name JOIN paths p ON p.id=a.path WHERE h.hour=$a AND a.id>$b ORDER BY a.id LIMIT $c",hour,after,limit);using var r=c.ExecuteReader();var list=new List<(long,ApplicationHour)>();while(r.Read())list.Add((r.GetInt64(0),new(hour,r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6))));return list;
    }
    public HistoryEvent[] Events(int day)
    {
        using var db=Open();using var c=Query(db,"SELECT e.ticks,e.epoch,e.state,r.message FROM events e JOIN reasons r ON r.id=e.reason WHERE day=$a AND day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY ticks LIMIT 10000",day);using var r=c.ExecuteReader();var list=new List<HistoryEvent>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),(HistoryState)r.GetInt32(2),r.GetString(3)));return list.ToArray();
    }
    public AttributionEvidence[] Attribution(int day,int reason=-1,int protocol=-1,int rawPid=-1,long from=0,long to=long.MaxValue)
    {
        using var db=Open();using var version=Query(db,"PRAGMA user_version");if(Convert.ToInt64(version.ExecuteScalar())<4)return [];
        using var q=Query(db,"SELECT a.minute,a.epoch,a.reason,a.protocol,a.raw_pid,a.identified,a.events,a.rx,a.tx,a.first_event,a.last_event,a.first_receipt,a.last_receipt FROM attribution a JOIN hours h ON h.id=a.hour WHERE h.day=$a AND ($b=-1 OR a.reason=$b) AND ($c=-1 OR a.protocol=$c) AND ($d=-1 OR a.raw_pid=$d) AND a.minute>=$e AND a.minute<$f AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY a.minute LIMIT 10000",day,reason,protocol,rawPid,from,to);
        using var r=q.ExecuteReader();var rows=new List<AttributionEvidence>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetInt64(1),(Detector.Core.AttributionReason)r.GetInt32(2),(Detector.Core.TrafficProtocol)r.GetInt32(3),r.GetInt32(4)==0?null:r.GetInt32(4),r.GetBoolean(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetInt64(9),r.GetInt64(10),r.GetInt64(11),r.GetInt64(12)));return rows.ToArray();
    }
    public void ExportAttribution(string destination,int day,int reason=-1,int protocol=-1,int rawPid=-1,long from=0,long to=long.MaxValue)=>AtomicCsv(destination,file=>{
        file.WriteLine("minute_utc_ticks,epoch,reason,protocol,raw_pid,identified_process,events,incoming_bytes,outgoing_bytes,first_event_ticks,last_event_ticks,first_receipt_ticks,last_receipt_ticks");
        foreach(var r in AttributionRows(day,reason,protocol,rawPid,from,to))file.WriteLine(string.Join(",",new object?[]{r.MinuteTicks,r.Epoch,r.Reason,r.Protocol,r.RawPid,r.IdentifiedProcess,r.Events,r.Incoming,r.Outgoing,r.FirstEventTicks,r.LastEventTicks,r.FirstReceiptTicks,r.LastReceiptTicks}.Select(Csv)));
    });
    // Bounded pages; no SQLite cursor/transaction survives a slow caller.
    public IEnumerable<AttributionEvidence> AttributionRows(int day,int reason=-1,int protocol=-1,int rawPid=-1,long from=0,long to=long.MaxValue)
    {
        using(var db=Open())using(var version=Query(db,"PRAGMA user_version"))if(Convert.ToInt64(version.ExecuteScalar())<4)yield break;
        long diagnostics=DiagnosticRevision();var revision=Hours(day).ToDictionary(h=>h.Id,h=>h.Revision);long minute=-1,epoch=-1,code=-1,proto=-1,pid=-1,identified=-1;
        for(;;){var page=new List<AttributionEvidence>();using(var db=Open())using(var q=Query(db,"SELECT a.minute,a.epoch,a.reason,a.protocol,a.raw_pid,a.identified,a.events,a.rx,a.tx,a.first_event,a.last_event,a.first_receipt,a.last_receipt FROM attribution a JOIN hours h ON h.id=a.hour WHERE h.day=$a AND ($b=-1 OR a.reason=$b) AND ($c=-1 OR a.protocol=$c) AND ($d=-1 OR a.raw_pid=$d) AND a.minute>=$e AND a.minute<$f AND (a.minute,a.epoch,a.reason,a.protocol,a.raw_pid,a.identified)>($g,$h,$i,$j,$k,$l) AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY a.minute,a.epoch,a.reason,a.protocol,a.raw_pid,a.identified LIMIT 512",day,reason,protocol,rawPid,from,to,minute,epoch,code,proto,pid,identified))using(var r=q.ExecuteReader()){while(r.Read())page.Add(new(r.GetInt64(0),r.GetInt64(1),(Detector.Core.AttributionReason)r.GetInt32(2),(Detector.Core.TrafficProtocol)r.GetInt32(3),r.GetInt32(4)==0?null:r.GetInt32(4),r.GetBoolean(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetInt64(9),r.GetInt64(10),r.GetInt64(11),r.GetInt64(12)));}
            if(page.Count==0)break;
            if(diagnostics!=DiagnosticRevision()||!revision.OrderBy(x=>x.Key).SequenceEqual(Hours(day).ToDictionary(h=>h.Id,h=>h.Revision).OrderBy(x=>x.Key)))throw new IOException("Selected history changed during export; retry a completed interval");
            foreach(var r in page)yield return r;
            var last=page[^1];minute=last.MinuteTicks;epoch=last.Epoch;code=(int)last.Reason;proto=(int)last.Protocol;pid=last.RawPid??0;identified=last.IdentifiedProcess?1:0;
        }
        if(diagnostics!=DiagnosticRevision()||!revision.OrderBy(x=>x.Key).SequenceEqual(Hours(day).ToDictionary(h=>h.Id,h=>h.Revision).OrderBy(x=>x.Key)))throw new IOException("Selected history changed during export; original destination preserved");
    }
    public RecordingSession[] Sessions()
    {
        using var db=Open();using var version=Query(db,"PRAGMA user_version");bool modern=Convert.ToInt64(version.ExecuteScalar())>=4;
        using var q=Query(db,modern?"SELECT e.id,e.token,e.requested_ticks,e.first_coverage_ticks,e.ended_ticks,(SELECT p.json FROM epoch_profiles x JOIN profiles p ON p.id=x.profile WHERE x.epoch=e.id ORDER BY x.ticks LIMIT 1),(SELECT token FROM epoch_sources WHERE epoch=e.id) FROM epochs e WHERE e.id<>0 ORDER BY e.id DESC LIMIT 1000":"SELECT id,token,requested_ticks,first_coverage_ticks,ended_ticks,NULL,NULL FROM epochs WHERE id<>0 ORDER BY id DESC LIMIT 1000");
        using var r=q.ExecuteReader();var rows=new List<RecordingSession>();while(r.Read()){string? json=r.IsDBNull(5)?null:r.GetString(5);var profile=json==null?null:System.Text.Json.JsonSerializer.Deserialize<RecordingProfile>(json);rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetInt64(4),profile?.ApplicationVersion,profile?.SourceRevision,profile?.SourceKind,profile?.IntegrityModel,json,r.IsDBNull(6)?null:r.GetString(6)));}return rows.ToArray();
    }
    public IntegrityEvidence[] Integrity(int day)=>IntegrityPage(day,-1,-1,10000);
    IntegrityEvidence[] IntegrityPage(int day,long afterTicks,long afterEpoch,int limit)
    {
        using var db=Open();using var version=Query(db,"PRAGMA user_version");if(Convert.ToInt64(version.ExecuteScalar())<4)return [];
        using var c=Query(db,"SELECT i.ticks,i.epoch,i.flags,i.os_lost,i.late,i.malformed,i.capacity,i.discard_rx,i.discard_tx,i.affected_from,i.affected_to,i.uncertain_through,i.earlier_unknown,i.unsupported FROM integrity i JOIN hours h ON h.id=i.hour WHERE h.day=$a AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) AND (i.ticks,i.epoch)>($b,$c) ORDER BY i.ticks,i.epoch LIMIT $d",day,afterTicks,afterEpoch,limit);
        using var r=c.ExecuteReader();var rows=new List<IntegrityEvidence>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetInt64(1),(Detector.Core.IntegrityFlags)r.GetInt32(2),r.GetInt64(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetInt64(9),r.GetInt64(10),r.GetInt64(11),r.GetBoolean(12),r.GetInt64(13)));return rows.ToArray();
    }
    public void ExportIntegrity(string destination,int day)=>AtomicCsv(destination,file=>{
        file.WriteLine("evidence_utc_ticks,epoch,flags,os_lost_delta,late_delta,malformed_delta,capacity_delta,discarded_rx,discarded_tx,affected_from_ticks,affected_to_ticks,uncertain_through_ticks,earlier_impact_unknown,unsupported_delta");
        var revision=Hours(day).ToDictionary(h=>h.Id,h=>h.Revision);long ticks=-1,epoch=-1;
        void Check(){if(!revision.OrderBy(x=>x.Key).SequenceEqual(Hours(day).ToDictionary(h=>h.Id,h=>h.Revision).OrderBy(x=>x.Key)))throw new IOException("Selected history changed during export; original destination preserved");}
        for(;;){var page=IntegrityPage(day,ticks,epoch,512);if(page.Length==0)break;Check();
            foreach(var r in page)file.WriteLine(string.Join(",",new object[]{r.UtcTicks,r.Epoch,(int)r.Flags,r.OsLost,r.Late,r.Malformed,r.Capacity,r.DiscardedIncoming,r.DiscardedOutgoing,r.AffectedFromTicks,r.AffectedToTicks,r.UncertainThroughTicks,r.EarlierImpactUnknown,r.Unsupported}.Select(Csv)));
            ticks=page[^1].UtcTicks;epoch=page[^1].Epoch;
        }Check();
    });
    sealed record Range(long Id,long Epoch,long First,long Seconds,HistoryState State,double Coverage,int Day,int Offset,string Zone);
    List<Range> Ranges(long from,long to,long first,long id,long watermark,long? hour)
    {
        using var db=Open();using var c=Query(db,"SELECT c.id,c.epoch,c.first_ticks,c.seconds,c.state,c.coverage_ticks,h.day,h.offset,z.name FROM coverage c JOIN hours h ON h.id=c.hour JOIN zones z ON z.id=h.zone WHERE c.first_ticks>=$a-36000000000 AND c.first_ticks<$b AND c.first_ticks+c.seconds*10000000>$a AND ($c=-1 OR c.hour=$c) AND c.id<=$d AND (c.first_ticks>$e OR (c.first_ticks=$e AND c.id>$f)) AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) ORDER BY c.first_ticks,c.id LIMIT 64",from,to,hour??-1,watermark,first,id);using var r=c.ExecuteReader();var list=new List<Range>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetInt64(3),(HistoryState)r.GetInt32(4),r.GetInt64(5)/(double)TimeSpan.TicksPerSecond,r.GetInt32(6),r.GetInt32(7),r.GetString(8)));return list;
    }
    void CheckRange(long id){using var db=Open();using var c=Query(db,"SELECT COUNT(*) FROM coverage c JOIN hours h ON h.id=c.hour WHERE c.id=$a AND h.retiring=0 AND h.day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0)",id);if(Convert.ToInt64(c.ExecuteScalar())!=1)throw new IOException("Selected history expired during export; retry a retained interval");}
    List<(long Second,TimelineRow Row)> Samples(Range range,long afterSecond,long afterProcess)
    {
        CheckRange(range.Id);using var db=Open();using var c=Query(db,"SELECT t.second,p.id,n.value,x.value,p.pid,p.created_ticks,t.rx,t.tx FROM traffic t JOIN processes p ON p.id=t.process JOIN apps a ON a.id=p.app JOIN names n ON n.id=a.name JOIN paths x ON x.id=a.path WHERE t.coverage=$a AND (t.second>$b OR (t.second=$b AND t.process>$c)) ORDER BY t.second,t.process LIMIT 512",range.Id,afterSecond,afterProcess);using var r=c.ExecuteReader();var list=new List<(long,TimelineRow)>();while(r.Read()){long second=r.GetInt64(0),pid=r.GetInt64(1);list.Add((second,new(range.First+second*TimeSpan.TicksPerSecond,range.Day,range.Offset,range.Zone,range.Epoch,pid,r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),pid==0?null:r.GetInt32(4),pid==0?null:r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),range.State,range.Coverage)));}return list;
    }
    // All read cursors close before yielding to a possibly slow file destination.
    IEnumerable<(long Second,TimelineRow Row)> SamplePages(Range range)
    {
        long second=-1,process=-1;for(;;){var page=Samples(range,second,process);if(page.Count==0)yield break;CheckRange(range.Id);foreach(var item in page)yield return item;second=page[^1].Second;process=page[^1].Row.ProcessInstance;}
    }
    public IEnumerable<TimelineRow> Timeline(long from,long to,int maximum=10000,long? hourId=null)
    {
        long watermark;using(var db=Open())using(var c=Query(db,"SELECT COALESCE(MAX(id),0) FROM coverage"))watermark=Convert.ToInt64(c.ExecuteScalar());
        long first=-1,id=-1;int emitted=0;
        for(;;){var ranges=Ranges(from,to,first,id,watermark,hourId);if(ranges.Count==0)yield break;
            foreach(var range in ranges){
                using var samples=SamplePages(range).GetEnumerator();bool next=samples.MoveNext();
                for(long second=0;second<range.Seconds;second++){
                    long ticks=range.First+second*TimeSpan.TicksPerSecond;bool any=false;
                    while(next&&samples.Current.Second==second){if(ticks>=from&&ticks<to){if(emitted++>=maximum)yield break;yield return samples.Current.Row;}any=true;next=samples.MoveNext();}
                    if(!any&&ticks>=from&&ticks<to){if(emitted++>=maximum)yield break;yield return new(ticks,range.Day,range.Offset,range.Zone,range.Epoch,0,"System (no stored traffic)",null,null,null,range.State==HistoryState.Complete?0:null,range.State==HistoryState.Complete?0:null,range.State,range.Coverage);}
                }
                CheckRange(range.Id);
            }
            first=ranges[^1].First;id=ranges[^1].Id;
        }
    }
    public void ExportCsv(string destination,long from,long to,long? hourId=null)
    {
        AtomicCsv(destination,file=>{file.WriteLine("utc,local_date,offset_minutes,zone,epoch,process_instance,application,path,pid,creation_utc_ticks,incoming_bytes,outgoing_bytes,state,coverage_seconds");foreach(var row in Timeline(from,to,int.MaxValue,hourId))file.WriteLine(string.Join(",",new object?[]{new DateTime(row.UtcTicks,DateTimeKind.Utc).ToString("O"),row.Day,row.OffsetMinutes,row.Zone,row.Epoch,row.ProcessInstance,row.Application,row.Path,row.Pid,row.CreationTicks,row.Incoming,row.Outgoing,row.State,row.CoverageSeconds.ToString("R",CultureInfo.InvariantCulture)}.Select(Csv)));});
    }
    public void ExportHours(string destination,int day)=>AtomicCsv(destination,file=>{HourHeader(file);foreach(var h in Hours(day))WriteHour(file,h);});
    public void ExportHour(string destination,long hour,int day)=>AtomicCsv(destination,file=>{HourHeader(file);WriteHour(file,Hours(day).Single(h=>h.Id==hour));});
    static void HourHeader(TextWriter file)=>file.WriteLine("hour_utc,local_date,offset_minutes,zone,application,path,incoming_bytes,outgoing_bytes,peak_incoming_bytes_per_second,peak_outgoing_bytes_per_second,coverage_seconds,incomplete_buckets,finalized,clock_ambiguity");
    void CheckHour(HourRow h){using var db=Open();using var q=Query(db,"SELECT COUNT(*) FROM hours WHERE id=$a AND revision=$b AND retiring=0 AND day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0)",h.Id,h.Revision);if(Convert.ToInt64(q.ExecuteScalar())!=1)throw new IOException("Hour changed or expired during export; retry a completed retained hour");}
    void WriteHour(TextWriter file,HourRow h)
    {
        void Row(ApplicationHour a)=>file.WriteLine(string.Join(",",new object?[]{new DateTime(h.UtcTicks,DateTimeKind.Utc).ToString("O"),h.Day,h.OffsetMinutes,h.Zone,a.Application,a.Path,h.CoverageSeconds<=0&&a.Incoming==0?(object?)null:a.Incoming,h.CoverageSeconds<=0&&a.Outgoing==0?(object?)null:a.Outgoing,h.CoverageSeconds<=0?(object?)null:a.PeakIncoming,h.CoverageSeconds<=0?(object?)null:a.PeakOutgoing,h.CoverageSeconds.ToString("R",CultureInfo.InvariantCulture),h.Incomplete,h.Finalized,h.Ambiguous}.Select(Csv)));
        CheckHour(h);Row(new(h.Id,"SYSTEM OBSERVED TOTAL",null,h.Incoming,h.Outgoing,h.PeakIncoming,h.PeakOutgoing));long after=-1;
        for(;;){var page=ApplicationPage(h.Id,after,512);if(page.Count==0)break;CheckHour(h);foreach(var a in page)Row(a.Value);after=page[^1].Id;}CheckHour(h);
    }
    public static void AtomicCsv(string destination,Action<TextWriter> write)
    {
        string path=Path.GetFullPath(destination);StoragePaths.LocalDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var file=new StreamWriter(stream,new System.Text.UTF8Encoding(true))){write(file);file.Flush();stream.Flush(true);}File.Move(temporary,path,true);}finally{File.Delete(temporary);}
    }
    static string Csv(object? value)=>"\""+(Convert.ToString(value,CultureInfo.InvariantCulture)??"").Replace("\"","\"\"")+"\"";
}
