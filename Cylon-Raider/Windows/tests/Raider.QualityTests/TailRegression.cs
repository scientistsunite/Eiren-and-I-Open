using Raider.Core;
using Raider.History;

static class TailRegression
{
    public static void Run(Action<bool,string> check)
    {
        var t=new DateTime(2026,10,27,23,59,58,DateTimeKind.Utc);
        var identity=new Identity(71,t.Ticks,"tail fixture",null);
        var a=new Aggregator(t,4){DetailedDiagnostics=true};
        a.Add(new(t,identity,Direction.Incoming,11,t));
        var closed=a.Advance(t.AddSeconds(5));
        a.Add(new(t.AddSeconds(2),identity,Direction.Incoming,123,t.AddSeconds(5),AttributionReason.PathUnavailable,71,TrafficProtocol.TcpIpv4));
        a.Add(new(t.AddSeconds(3),null,Direction.Outgoing,234,t.AddSeconds(5),AttributionReason.ProcessExited,72,TrafficProtocol.UdpIpv6));
        var tail=a.DrainKnown();
        check(closed.Sum(m=>m.Rows.Sum(r=>r.Incoming))==11&&tail.Sum(m=>m.Rows.Sum(r=>r.Incoming+r.Outgoing))==357,"shutdown retains known pending bytes without duplicating a closed bucket");
        check(tail.Length==2&&tail.All(m=>m.CoverageSeconds==0&&!m.Complete&&m.Quality!.Flags.HasFlag(IntegrityFlags.CaptureStopped)),"shutdown asserts no tail coverage, including midnight, and creates no empty catch-up seconds");
        check(tail[0].Rows.Single().Process==identity&&tail[1].Rows.Single().Process==null&&tail.Sum(m=>m.Attribution.Sum(e=>e.Incoming+e.Outgoing))==357,"partial process identity and unattributed evidence survive tail draining exactly once");
        check(a.DrainKnown().Length==0&&new Aggregator(t).DrainKnown().Length==0,"repeated and silent Stop cannot fabricate or duplicate tail measurements");
        var late=new Aggregator(t,4);late.Advance(t.AddSeconds(5));late.Add(new(t,null,Direction.Incoming,19,t.AddSeconds(5)));
        var evidence=late.DrainKnown();
        check(evidence.Length==1&&evidence.Single().Quality!.LateDelta==1&&evidence.Single().Quality!.DiscardedIncoming==19&&evidence.Sum(m=>m.Rows.Sum(r=>r.Incoming+r.Outgoing))==0&&late.DrainKnown().Length==0,"shutdown preserves late/discarded evidence without inventing accepted bytes");
        var continuity=new SourceContinuity();var delayed=evidence.Single() with{StartUtc=DateTime.UtcNow.AddSeconds(-30),SourceSession="delayed-tail"};
        var retained=continuity.Observe(delayed);
        check(retained!=null&&retained.CoverageSeconds==0&&!retained.Complete&&retained.SourceSession=="delayed-tail"&&retained.Quality!.DiscardedIncoming==19&&retained.Rows.Length==0,"continuity guard preserves delayed evidence-only tail without manufacturing observed traffic");
        check(continuity.Observe(delayed with{Quality=null})==null,"continuity guard still discards evidence-free empty catch-up buckets");
        string root=Path.Combine(Path.GetTempPath(),"RaiderTail-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string file=Path.Combine(root,"history.db");
            using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){store.Begin(t);foreach(var frame in closed.Concat(tail))store.Append(frame with{SourceSession="tail-session"});store.Commit();}
            var reader=new HistoryReader(file);var rows=reader.Timeline(t.Ticks,t.AddSeconds(10).Ticks).ToArray();
            check(rows.Sum(r=>r.Incoming??0)==134&&rows.Sum(r=>r.Outgoing??0)==234&&rows.Where(r=>r.UtcTicks>=t.AddSeconds(2).Ticks).All(r=>r.State==HistoryState.Unavailable&&r.CoverageSeconds==0),"SQLite reopening retains exact tail bytes and unavailable coverage across midnight");
            check(reader.Attribution(20261028).Sum(r=>r.Incoming+r.Outgoing)==357,"historical diagnostics reconcile with retained midnight tail bytes");
            string csv=Path.Combine(root,"tail.csv");reader.ExportCsv(csv,t.Ticks,t.AddSeconds(10).Ticks);
            check(File.ReadAllText(csv).Contains("Unavailable"),"tail CSV explicitly exports unavailable observation quality rather than confirmed zero");
        }finally{Directory.Delete(root,true);}
    }
}
