namespace Detector.Core;

public sealed record Usage(string Key,string Name,string? Path,int? Pid,long? CreationTicks,long Incoming,long Outgoing,string? SourceSession=null);
public sealed record TrafficPoint(DateTime Utc,long Incoming,long Outgoing,double Coverage,bool Complete,long UnknownIncoming=0,long UnknownOutgoing=0);
public sealed record LedgerSnapshot(long Incoming,long Outgoing,long UnknownIncoming,long UnknownOutgoing,long Complete,long Partial,long Unavailable,
    long Frames,long SourceLate,long SourceLost,bool PresentationLimited,Usage[] Applications,Usage[] Processes,TrafficPoint[] Chart,AttributionSummary[] Reasons,string[] Alerts,DateTime? LastCompleteUtc=null);

// Presentation accounting only. No UI, I/O, waits or history queries; strict cardinality/ring bounds.
// The pipe reader updates every received frame even when rendering is suspended.
public sealed class TrafficLedger
{
    readonly object gate=new();
    readonly Dictionary<string,Usage> apps=new();readonly Dictionary<(string,int,long),Usage> processes=new();
    readonly Dictionary<(AttributionReason,TrafficProtocol,bool),AttributionSummary> reasons=new();
    readonly Queue<TrafficPoint> chart=new();readonly Queue<string> alerts=new();
    DateTime? lastComplete;
    string? lastSession;long lastTicks=long.MinValue,incoming,outgoing,unknownIn,unknownOut,complete,partial,unavailable,frames,late,lost;bool limited;
    public void Reset(){lock(gate){apps.Clear();processes.Clear();reasons.Clear();chart.Clear();alerts.Clear();lastSession=null;lastTicks=long.MinValue;incoming=outgoing=unknownIn=unknownOut=complete=partial=unavailable=frames=late=lost=0;limited=false;lastComplete=null;}}
    public void Observe(Measurement frame)
    {
        lock(gate){
            if(lastSession==frame.SourceSession&&lastTicks==frame.StartUtc.Ticks)return;
            if(lastSession!=null&&lastSession!=frame.SourceSession)Alert(frame.StartUtc,"Collector source session changed; cumulative counters belong to the new source.");
            lastSession=frame.SourceSession;lastTicks=frame.StartUtc.Ticks;frames++;late=frame.LateEvents;lost=frame.LostEvents;
            if(frame.CoverageSeconds<=0)unavailable++;else if(frame.Complete){complete++;lastComplete=frame.StartUtc;}else partial++;
            long rx=0,tx=0,unknownRx=0,unknownTx=0;
            foreach(var row in frame.Rows){
                rx=checked(rx+row.Incoming);tx=checked(tx+row.Outgoing);
                if(row.Process==null){unknownRx=checked(unknownRx+row.Incoming);unknownTx=checked(unknownTx+row.Outgoing);unknownIn=checked(unknownIn+row.Incoming);unknownOut=checked(unknownOut+row.Outgoing);}
                string key=row.Process?.Path?.ToUpperInvariant()??(row.Process==null?"?":"unresolved:"+(frame.SourceSession??"legacy")+":"+row.Process.Pid+":"+row.Process.StartUtcTicks);
                if(!apps.ContainsKey(key)&&apps.Count>=1024){key="presentation-other";limited=true;}
                var prior=apps.GetValueOrDefault(key)??new Usage(key,key=="presentation-other"?"Additional measured applications":row.Process?.Name??"Unattributed — identity unknown",row.Process?.Path,null,null,0,0);
                apps[key]=prior with{Incoming=checked(prior.Incoming+row.Incoming),Outgoing=checked(prior.Outgoing+row.Outgoing)};
                if(row.Process is {} p){var id=(frame.SourceSession??"legacy",p.Pid,p.StartUtcTicks);if(processes.TryGetValue(id,out var previous)||processes.Count<4096){previous??=new Usage(key,p.Name,p.Path,p.Pid,p.StartUtcTicks,0,0,frame.SourceSession);processes[id]=previous with{Incoming=checked(previous.Incoming+row.Incoming),Outgoing=checked(previous.Outgoing+row.Outgoing)};}else limited=true;}
            }
            incoming=checked(incoming+rx);outgoing=checked(outgoing+tx);
            chart.Enqueue(new(frame.StartUtc,rx,tx,frame.CoverageSeconds,frame.Complete,unknownRx,unknownTx));while(chart.Count>120)chart.Dequeue();
            foreach(var value in frame.Attribution){var id=(value.Reason,value.Protocol,value.IdentifiedProcess);if(reasons.TryGetValue(id,out var prior))reasons[id]=prior with{Events=checked(prior.Events+value.Events),Incoming=checked(prior.Incoming+value.Incoming),Outgoing=checked(prior.Outgoing+value.Outgoing),LastEventTicks=value.LastEventTicks,LastReceiptTicks=value.LastReceiptTicks};else reasons[id]=value with{RawPid=null};}
            if(frame.Quality is {} q&&(q.LateDelta+q.OsLostDelta+q.MalformedDelta+q.CapacityDelta+q.UnsupportedDelta>0||q.Flags.HasFlag(IntegrityFlags.Recovery)))Alert(frame.StartUtc,$"{q.Flags}: new late {q.LateDelta}, OS loss {q.OsLostDelta}, malformed {q.MalformedDelta}, unsupported {q.UnsupportedDelta}, capacity {q.CapacityDelta}; known discarded ↓{q.DiscardedIncoming} ↑{q.DiscardedOutgoing} B");
            if(frame.Status.Contains("SourceDiscontinuity=",StringComparison.Ordinal)||frame.Status.Contains("TransportDropped=",StringComparison.Ordinal))Alert(frame.StartUtc,frame.Status);
        }
    }
    void Alert(DateTime at,string text){alerts.Enqueue(at.ToLocalTime().ToString("HH:mm:ss")+"  "+text);while(alerts.Count>64)alerts.Dequeue();}
    public LedgerSnapshot Snapshot(){lock(gate)return new(incoming,outgoing,unknownIn,unknownOut,complete,partial,unavailable,frames,late,lost,limited,apps.Values.ToArray(),processes.Values.ToArray(),chart.ToArray(),reasons.Values.ToArray(),alerts.ToArray(),lastComplete);}
}
