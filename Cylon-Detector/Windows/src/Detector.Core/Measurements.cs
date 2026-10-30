namespace Detector.Core;

public enum Direction { Incoming, Outgoing }
public sealed record Identity(int Pid, long StartUtcTicks, string Name, string? Path);
public sealed record Traffic(DateTime Utc, Identity? Process, Direction Direction, long Bytes, DateTime? ReceivedUtc=null,AttributionReason Reason=AttributionReason.None,int? RawPid=null,TrafficProtocol Protocol=TrafficProtocol.Unknown);
public sealed record Row(Identity? Process, long Incoming, long Outgoing);
// Observed counts remain usable lower bounds when Complete=false; never silently turn gaps into zero.
public sealed record Measurement(int Schema, DateTime StartUtc, double CoverageSeconds,
    bool Complete, long LostEvents, long LateEvents, Row[] Rows, string Status)
{
    public string? SourceSession {get;init;}
    public SensorSnapshot? Sensors {get;init;}
    public AttributionSummary[] Attribution {get;init;}=[];
    public IntervalIntegrity? Quality {get;init;}
    public CollectorDiagnostics? Diagnostics {get;init;}
}
public interface IMeasurementSource : IDisposable
{
    event Action<Measurement>? Measured;
    void Start();
    void Stop();
}

public enum AttributionReason { None=0, MissingPid=1, ProcessExited=2, AccessDenied=3, PathUnavailable=4, IdentityAmbiguous=5, Malformed=6, Unknown=7 }
public enum TrafficProtocol { Unknown=0,TcpIpv4=1,TcpIpv6=2,UdpIpv4=3,UdpIpv6=4 }
public sealed record AttributionSummary(AttributionReason Reason,TrafficProtocol Protocol,int? RawPid,bool IdentifiedProcess,long Events,long Incoming,long Outgoing,long FirstEventTicks,long LastEventTicks,long FirstReceiptTicks,long LastReceiptTicks);

[Flags]
public enum IntegrityFlags { None=0, Late=1, OsLoss=2, Malformed=4, Capacity=8, Discontinuity=16, Recovery=32, Unsupported=64, CaptureStopped=128 }
public sealed record IntervalIntegrity(IntegrityFlags Flags,long OsLostDelta,long LateDelta,long MalformedDelta,long CapacityDelta,
    long DiscardedIncoming,long DiscardedOutgoing,long AffectedFromTicks,long AffectedToTicks,long UncertainThroughTicks,bool EarlierImpactUnknown,long UnsupportedDelta=0);
public sealed record CollectorDiagnostics(int LatenessSeconds,long Events,long Malformed,long CapacityRejected,long DiscardedIncoming,long DiscardedOutgoing,
    long IdentityUnavailable,long[] DeliveryBins,double MaximumDeliveryMs,double MaximumCallbackMs,double MaximumLockWaitMs,double MaximumResolutionMs,long UnsupportedEvents=0);

// Event-time [second,second+1) resolution, independently configurable bounded display delay.
// Complete means no known defect under this delivery policy, not a guarantee of NIC/wire totals.
public sealed class Aggregator
{
    public const int RecommendedLatenessSeconds=4;
    readonly SortedDictionary<long,Dictionary<Identity,(long In,long Out)>> buckets=new();
    readonly Dictionary<long,(long In,long Out)> unknown=new();
    readonly Dictionary<long,IntegrityFlags> defects=new();
    readonly Dictionary<long,Dictionary<(AttributionReason Reason,TrafficProtocol Protocol,int Pid,bool Identified),AttributionSummary>> attribution=new();
    readonly Dictionary<long,HashSet<(AttributionReason,TrafficProtocol,int,bool)>> detailPids=new();
    public bool DetailedDiagnostics {get;set;}
    readonly DateTime started;
    readonly int lateness;
    DateTime receipt;
    long next,lost,late,malformed,capacity,osDelta,lateDelta,malformedDelta,capacityDelta,discardIn,discardOut,fromTicks,toTicks;
    long unsupportedDelta;
    long totalDiscardIn,totalDiscardOut;
    public long DiscardedIncoming=>totalDiscardIn;
    public long DiscardedOutgoing=>totalDiscardOut;
    long uncertainThrough=-1;
    IntegrityFlags uncertainty;
    bool earlierUnknown,recovering;
    public Aggregator(DateTime start,int latenessSeconds=2)
    {
        if(latenessSeconds is <2 or >4)throw new ArgumentOutOfRangeException(nameof(latenessSeconds));
        started=start.ToUniversalTime();receipt=started;next=started.Ticks/TimeSpan.TicksPerSecond;lateness=latenessSeconds;
    }
    public long LostEvents=>lost;
    public long LateEvents=>late;
    public long Malformed=>malformed;
    public long CapacityRejected=>capacity;
    public void Loss(long count,DateTime? observedUtc=null)
    {
        if(count<=0)return;lost+=count;osDelta+=count;earlierUnknown=true;
        Uncertain(IntegrityFlags.OsLoss,observedUtc??receipt);Evidence(Math.Max(0,next-lateness)*TimeSpan.TicksPerSecond,(observedUtc??receipt).ToUniversalTime().Ticks);
    }
    void Evidence(long first,long last){if(fromTicks==0||first<fromTicks)fromTicks=first;toTicks=Math.Max(toTicks,last);}
    void Uncertain(IntegrityFlags reason,DateTime at)
    {
        long second=at.ToUniversalTime().Ticks/TimeSpan.TicksPerSecond;
        uncertainThrough=Math.Max(uncertainThrough,second+lateness);uncertainty|=reason;recovering=true;
    }
    public void UnsupportedEvent(DateTime utc,DateTime received)
    {
        unsupportedDelta++;long key=utc.ToUniversalTime().Ticks/TimeSpan.TicksPerSecond;
        if(key>=next&&key<=next+lateness+4)defects[key]=defects.GetValueOrDefault(key)|IntegrityFlags.Unsupported;else Uncertain(IntegrityFlags.Unsupported,received);
        Evidence(key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond);
    }
    public void MalformedEvent(DateTime utc,DateTime received)
    {
        malformed++;malformedDelta++;long key=utc.ToUniversalTime().Ticks/TimeSpan.TicksPerSecond;
        if(key>=next&&key<=next+lateness+4)defects[key]=defects.GetValueOrDefault(key)|IntegrityFlags.Malformed;
        else Uncertain(IntegrityFlags.Malformed,received);
        Evidence(key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond);
    }
    public void Add(Traffic e)
    {
        DateTime callback=e.ReceivedUtc?.ToUniversalTime()??(e.Utc>receipt?e.Utc:receipt);DateTime at=callback>receipt?callback:receipt;receipt=at;
        if(e.Bytes<0){MalformedEvent(e.Utc,at);return;}
        long key=e.Utc.ToUniversalTime().Ticks/TimeSpan.TicksPerSecond;
        if(key<next){late++;lateDelta++;Discard(e);Uncertain(IntegrityFlags.Late,at);Evidence(key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond);return;}
        if(key>next+lateness+4){capacity++;capacityDelta++;Discard(e);Uncertain(IntegrityFlags.Discontinuity,at);Evidence(key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond);return;}
        if(e.Process is null){unknown.TryGetValue(key,out var u);unknown[key]=e.Direction==Direction.Incoming?(checked(u.In+e.Bytes),u.Out):(u.In,checked(u.Out+e.Bytes));Attribute(e,key,callback);return;}
        if(!buckets.TryGetValue(key,out var rows))buckets[key]=rows=new();
        if(!rows.ContainsKey(e.Process)&&rows.Count>=8192){capacity++;capacityDelta++;Discard(e);defects[key]=defects.GetValueOrDefault(key)|IntegrityFlags.Capacity;Evidence(key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond);return;}
        rows.TryGetValue(e.Process,out var v);rows[e.Process]=e.Direction==Direction.Incoming?(checked(v.In+e.Bytes),v.Out):(v.In,checked(v.Out+e.Bytes));Attribute(e,key,callback);
    }
    void Attribute(Traffic e,long second,DateTime received)
    {
        var reason=e.Reason;if(reason==AttributionReason.None){if(e.Process==null)reason=AttributionReason.Unknown;else if(e.Process.Path==null)reason=AttributionReason.PathUnavailable;else return;}
        if((uint)reason>(uint)AttributionReason.Unknown)reason=AttributionReason.Unknown;
        var protocol=(uint)e.Protocol<=(uint)TrafficProtocol.UdpIpv6?e.Protocol:TrafficProtocol.Unknown;
        int pid=0;if(DetailedDiagnostics&&e.RawPid is >0){long minute=second/60;if(!detailPids.TryGetValue(minute,out var known))detailPids[minute]=known=new();var detail=(reason,protocol,e.RawPid.Value,e.Process!=null);if(known.Contains(detail)||known.Count<64){known.Add(detail);pid=e.RawPid.Value;}}
        if(!attribution.TryGetValue(second,out var map))attribution[second]=map=new();var key=(reason,protocol,pid,e.Process!=null);
        // At most64 detailed reason/protocol/identity slots plus80 aggregate keys;
        // cap combinations as well as PIDs so optional detail cannot multiply frame size.
        long rx=e.Direction==Direction.Incoming?e.Bytes:0,tx=e.Direction==Direction.Outgoing?e.Bytes:0;
        if(map.TryGetValue(key,out var prior))map[key]=prior with{Events=prior.Events+1,Incoming=checked(prior.Incoming+rx),Outgoing=checked(prior.Outgoing+tx),FirstEventTicks=Math.Min(prior.FirstEventTicks,e.Utc.Ticks),LastEventTicks=Math.Max(prior.LastEventTicks,e.Utc.Ticks),FirstReceiptTicks=Math.Min(prior.FirstReceiptTicks,received.Ticks),LastReceiptTicks=Math.Max(prior.LastReceiptTicks,received.Ticks)};
        else map[key]=new(reason,protocol,pid==0?null:pid,e.Process!=null,1,rx,tx,e.Utc.Ticks,e.Utc.Ticks,received.Ticks,received.Ticks);
    }
    AttributionSummary[] TakeAttribution(long key)
    {
        foreach(long minute in detailPids.Keys.Where(m=>m<key/60-1).ToArray())detailPids.Remove(minute);
        return attribution.Remove(key,out var map)?map.Values.ToArray():[];
    }
    void Discard(Traffic e){if(e.Direction==Direction.Incoming){discardIn=checked(discardIn+e.Bytes);totalDiscardIn=checked(totalDiscardIn+e.Bytes);}else {discardOut=checked(discardOut+e.Bytes);totalDiscardOut=checked(totalDiscardOut+e.Bytes);}}
    // Called only after event callbacks and the ticker have stopped. Preserve decoded
    // bytes still behind the watermark without asserting that ETW delivered its tail.
    public Measurement[] DrainKnown()
    {
        var keys=buckets.Keys.Concat(unknown.Keys).Concat(defects.Keys).Concat(attribution.Keys).Distinct().Order().ToList();
        if(keys.Count==0&&osDelta+lateDelta+malformedDelta+capacityDelta+unsupportedDelta>0)keys.Add(next);
        var result=new List<Measurement>();
        foreach(long key in keys){
            var rows=buckets.Remove(key,out var b)?b.Select(x=>new Row(x.Key,x.Value.In,x.Value.Out)).ToList():new List<Row>();
            if(unknown.Remove(key,out var u))rows.Add(new(null,u.In,u.Out));
            var flags=IntegrityFlags.CaptureStopped|uncertainty|(defects.Remove(key,out var defect)?defect:IntegrityFlags.None);
            var quality=new IntervalIntegrity(flags,osDelta,lateDelta,malformedDelta,capacityDelta,discardIn,discardOut,fromTicks,toTicks,(uncertainThrough+1)*TimeSpan.TicksPerSecond,earlierUnknown,unsupportedDelta);
            result.Add(new(1,new DateTime(key*TimeSpan.TicksPerSecond,DateTimeKind.Utc),0,false,lost,late,rows.ToArray(),"Known ETW bytes only; capture stopped before delivery watermark — tail coverage unavailable"){Quality=quality,Attribution=TakeAttribution(key)});
            unsupportedDelta=osDelta=lateDelta=malformedDelta=capacityDelta=discardIn=discardOut=fromTicks=toTicks=0;earlierUnknown=false;
        }
        detailPids.Clear();return result.ToArray();
    }
    public Measurement[] Advance(DateTime now)
    {
        receipt=now.ToUniversalTime();long end=receipt.Ticks/TimeSpan.TicksPerSecond-lateness;var result=new List<Measurement>();
        bool gap=end-next>8;
        if(gap){
            // Keep known bytes at their original event times; do not invent observed silence.
            foreach(long key in buckets.Keys.Concat(unknown.Keys).Distinct().Order()){
                var retained=buckets.TryGetValue(key,out var entries)?entries.Select(x=>new Row(x.Key,x.Value.In,x.Value.Out)).ToList():new List<Row>();
                if(unknown.TryGetValue(key,out var u))retained.Add(new(null,u.In,u.Out));
                result.Add(new(1,new DateTime(key*TimeSpan.TicksPerSecond,DateTimeKind.Utc),0,false,lost,late,retained.ToArray(),"Known ETW bytes only; suspension/clock scheduling gap") {Quality=new(IntegrityFlags.Discontinuity,0,0,0,0,0,0,key*TimeSpan.TicksPerSecond,(key+1)*TimeSpan.TicksPerSecond,0,false),Attribution=TakeAttribution(key)});
            }
            buckets.Clear();unknown.Clear();defects.Clear();attribution.Clear();detailPids.Clear();next=end-1;Uncertain(IntegrityFlags.Discontinuity,receipt);
        }
        while(next<end){
            long key=next++;var start=new DateTime(key*TimeSpan.TicksPerSecond,DateTimeKind.Utc);
            var rows=buckets.Remove(key,out var b)?b.Select(x=>new Row(x.Key,x.Value.In,x.Value.Out)).ToList():new List<Row>();
            if(unknown.Remove(key,out var u))rows.Add(new(null,u.In,u.Out));
            IntegrityFlags flags=defects.Remove(key,out var defect)?defect:IntegrityFlags.None;
            if(key<=uncertainThrough)flags|=uncertainty;
            else if(recovering){flags|=IntegrityFlags.Recovery;recovering=false;uncertainty=0;}
            double coverage=gap?0:Math.Clamp((start.AddSeconds(1)-started).TotalSeconds,0,1);
            bool complete=(flags&~IntegrityFlags.Recovery)==0&&coverage==1;
            IntervalIntegrity? quality=flags!=0||osDelta+lateDelta+malformedDelta+capacityDelta+unsupportedDelta>0?new(flags,osDelta,lateDelta,malformedDelta,capacityDelta,discardIn,discardOut,fromTicks,toTicks,(uncertainThrough+1)*TimeSpan.TicksPerSecond,earlierUnknown,unsupportedDelta):null;
            result.Add(new(1,start,coverage,complete,lost,late,rows.ToArray(),complete?"ETW observed bytes — complete under bounded delivery policy":"ETW observed lower bounds — partial/uncertain delivery") {Quality=quality,Attribution=TakeAttribution(key)});
            unsupportedDelta=osDelta=lateDelta=malformedDelta=capacityDelta=discardIn=discardOut=fromTicks=toTicks=0;earlierUnknown=false;
        }
        return result.ToArray();
    }
}

public sealed record ApplicationTraffic(string Key, string Name, Row[] Processes)
{
    public long Incoming => Processes.Sum(r => r.Incoming);
    public long Outgoing => Processes.Sum(r => r.Outgoing);
}
public static class ApplicationGrouping
{
    public static ApplicationTraffic[] Group(IEnumerable<Row> rows) => rows
        .GroupBy(r => r.Process?.Path?.ToUpperInvariant() ?? (r.Process is null ? "?" : $"unresolved:{r.Process.Pid}:{r.Process.StartUtcTicks}"))
        .Select(g => new ApplicationTraffic(g.Key,g.First().Process?.Name ?? "Unattributed — process unavailable",g.ToArray())).ToArray();
}
