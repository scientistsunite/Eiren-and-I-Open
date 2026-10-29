namespace Detector.Core;

// Host-level discontinuity guard. The v0.1 ETW engine remains unchanged.
public sealed class SourceContinuity(TimeProvider? time=null)
{
    readonly TimeProvider clock=time??TimeProvider.System;
    readonly object gate=new();
    long last=(time??TimeProvider.System).GetTimestamp();
    bool restart;
    string reason="",pending="";
    public bool RestartRequested {get{lock(gate)return restart;}}
    public bool HeartbeatExpired {get{lock(gate)return clock.GetElapsedTime(last)>TimeSpan.FromSeconds(8);}}
    public void RequestRestart(string message){lock(gate){restart=true;reason=message;}}
    public Measurement? Observe(Measurement frame)
    {
        lock(gate){
            if(!restart&&(clock.GetElapsedTime(last)>TimeSpan.FromSeconds(6)||Math.Abs((clock.GetUtcNow().UtcDateTime-frame.StartUtc.ToUniversalTime()).TotalSeconds)>Aggregator.RecommendedLatenessSeconds+4)){
                restart=true;reason="Suspend, clock jump or capture scheduling discontinuity; unobserved intervals are not zero";
            }
            if(restart){
                // Keep genuinely observed byte/identity lower bounds, but never the engine's
                // empty catch-up buckets as confirmed monitoring during a suspension.
                var quality=frame.Quality;
                bool evidence=quality!=null&&(quality.Flags.HasFlag(IntegrityFlags.CaptureStopped)||quality.OsLostDelta>0||quality.LateDelta>0||quality.MalformedDelta>0||quality.CapacityDelta>0||quality.UnsupportedDelta>0||quality.DiscardedIncoming>0||quality.DiscardedOutgoing>0);
                return frame.Rows.Length==0&&!evidence?null:frame with{CoverageSeconds=0,Complete=false,Status="Known bytes/identities/integrity evidence only; coverage unavailable after source discontinuity"};
            }
            last=clock.GetTimestamp();
            if(pending.Length==0)return frame;
            var result=frame with{Status=frame.Status+" | SourceDiscontinuity="+pending};pending="";return result;
        }
    }
    public void ResetAfterRestart(){lock(gate){pending=reason;reason="";last=clock.GetTimestamp();restart=false;}}
}
