using Raider.Core;
using Raider.History;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
// Fixture authorization cannot be relaxed just because a Windows machine is registered.
var fixtureEnv=new Dictionary<string,string>{{"GITHUB_ACTIONS","true"},{"RUNNER_ENVIRONMENT","self-hosted"},{"RUNNER_NAME","EIREN-WIN-CI"},{"RAIDER_CI_FIXTURE","EIREN-WIN-CI-v04"},{"GITHUB_REPOSITORY","scientistsunite/Eiren-and-I-Open"},{"GITHUB_ACTOR","scientistsunite"}};
Check(FixtureEnvironment.Allowed(k=>fixtureEnv.GetValueOrDefault(k)),"explicit owner-authorized dedicated VM admitted");
foreach(string key in fixtureEnv.Keys.ToArray()) { string saved=fixtureEnv[key];fixtureEnv[key]="other";Check(!FixtureEnvironment.Allowed(k=>fixtureEnv.GetValueOrDefault(k)),"fixture rejects mismatched "+key);fixtureEnv[key]=saved; }
Check(FixtureEnvironment.Allowed(k=>k=="GITHUB_ACTIONS"?"true":k=="RUNNER_ENVIRONMENT"?"github-hosted":null),"disposable hosted fixture path preserved");
Check(!FixtureEnvironment.Allowed(_=>null),"ordinary desktop execution rejected");
string? expectedRevision=Environment.GetEnvironmentVariable("APPROVED_SHA")??Environment.GetEnvironmentVariable("SourceRevisionId");
if(expectedRevision is {Length:40})Check(BuildIdentity.SourceRevision==expectedRevision,"compiled recording provenance matches the actual selected source SHA");
var t=new DateTime(2026,10,27,10,0,0,DateTimeKind.Utc);var process=new Identity(123,t.Ticks,"controlled",@"C:\Fixture\sender.exe");
var a=new Aggregator(t);a.Advance(t.AddSeconds(3));a.Add(new(t,process,Direction.Incoming,99,t.AddSeconds(3)));
var frames=Enumerable.Range(4,14).SelectMany(i=>a.Advance(t.AddSeconds(i))).ToArray();
Check(frames[0].Quality?.LateDelta==1&&frames[0].Quality?.DiscardedIncoming==99,"late target and exact discarded direction/bytes preserved once");
Check(frames.Skip(1).All(f=>f.Quality?.LateDelta is null or 0)&&frames.All(f=>f.LateEvents==1)&&a.LateEvents==1,"cumulative late counter never becomes repeated new error");
Check(frames.Any(f=>!f.Complete)&&frames.Skip(8).All(f=>f.Complete),"single late event recovers only after explicit finite uncertainty boundary");
Check(frames.Any(f=>f.Complete&&f.Quality?.Flags==IntegrityFlags.Recovery),"recovery is an explicit event, not evidence erasure");
var loss=new Aggregator(t);loss.Advance(t.AddSeconds(3));loss.Loss(7,t.AddSeconds(3));frames=Enumerable.Range(4,15).SelectMany(i=>loss.Advance(t.AddSeconds(i))).ToArray();
Check(frames[0].Quality?.OsLostDelta==7&&frames[0].Quality?.EarlierImpactUnknown==true&&loss.LostEvents==7,"OS loss precision limitation and delta recorded");Check(frames.Skip(8).All(f=>f.Complete&&f.LostEvents==7),"OS loss recovery retains cumulative original evidence");
var adjacent=new Aggregator(t);adjacent.Advance(t.AddSeconds(3));adjacent.Add(new(t,null,Direction.Outgoing,12,t.AddSeconds(3)));adjacent.Advance(t.AddSeconds(5));adjacent.Add(new(t.AddSeconds(1),null,Direction.Incoming,34,t.AddSeconds(5)));frames=Enumerable.Range(6,14).SelectMany(i=>adjacent.Advance(t.AddSeconds(i))).ToArray();Check(frames.First().Quality?.DiscardedIncoming==34&&frames.Take(4).All(f=>!f.Complete)&&frames.Last().Complete,"adjacent late signals extend rather than prematurely reset recovery");
var capacity=new Aggregator(t);for(int i=0;i<8200;i++)capacity.Add(new(t,process with{Pid=i},Direction.Incoming,10));frames=capacity.Advance(t.AddSeconds(4));Check(!frames[0].Complete&&frames[0].Quality?.CapacityDelta==8&&frames[0].LostEvents==0&&frames[1].Complete,"known capacity rejection damages its bucket, distinct from OS loss");
var suspension=new Aggregator(t);suspension.Add(new(t.AddSeconds(1),process,Direction.Outgoing,456));frames=suspension.Advance(t.AddSeconds(100));Check(frames.Sum(f=>f.Rows.Sum(r=>r.Outgoing))==456&&frames.All(f=>!f.Complete&&f.CoverageSeconds==0),"suspension preserves known bytes without fabricated observed zero intervals");
// Identical controlled event-time/receipt-time replay: compare old sticky model and bounded windows.
object Replay(int window){var agg=new Aggregator(t,window);var watch=Stopwatch.StartNew();long cpu=Process.GetCurrentProcess().TotalProcessorTime.Ticks;var outputs=new List<Measurement>();int lateEvents=0;for(int second=0;second<120;second++){if(second<100){double delay=second%10==0?3.5:.05;int due=(int)Math.Ceiling(second+delay);_ = due;}foreach(int source in Enumerable.Range(0,Math.Min(100,second+1)).Where(i=>(int)Math.Ceiling(i+(i%10==0?3.5:.05))==second))agg.Add(new(t.AddSeconds(source),source%7==0?null:process,Direction.Incoming,1000,t.AddSeconds(second)));outputs.AddRange(agg.Advance(t.AddSeconds(second)));}lateEvents=(int)(outputs.LastOrDefault()?.LateEvents??0);long complete=outputs.LongCount(f=>f.Complete);return new{window,seconds=outputs.Count,complete,partial=outputs.Count-complete,lateEvents,captured=outputs.Sum(f=>f.Rows.Sum(r=>r.Incoming)),discarded=outputs.Sum(f=>f.Quality?.DiscardedIncoming??0),cpuMs=(Process.GetCurrentProcess().TotalProcessorTime.Ticks-cpu)/(double)TimeSpan.TicksPerMillisecond,wallMs=watch.Elapsed.TotalMilliseconds};}
Console.WriteLine(JsonSerializer.Serialize(new{evidence="synthetic deterministic identical latency replay; not physical ETW",twoSeconds=Replay(2),fourSeconds=Replay(4),oldStickyConsequence="After first late event the v0.3 damage flag never recovers; finite model does. No NIC-total equivalence asserted."}));
var attributed=new Aggregator(t){DetailedDiagnostics=true};
attributed.Add(new(t,null,Direction.Incoming,100,t.AddMilliseconds(1),AttributionReason.MissingPid));
attributed.Add(new(t,process with{Path=null},Direction.Outgoing,200,t.AddMilliseconds(2),AttributionReason.PathUnavailable,123,TrafficProtocol.TcpIpv4));
attributed.Add(new(t,null,Direction.Outgoing,50,t.AddMilliseconds(3),AttributionReason.IdentityAmbiguous,123,TrafficProtocol.TcpIpv4));
var mixed=attributed.Advance(t.AddSeconds(3)).Single();
Check(mixed.Rows.Where(r=>r.Process==null).Sum(r=>r.Incoming+r.Outgoing)==150&&mixed.Rows.Sum(r=>r.Incoming+r.Outgoing)==350,"observed equals reliably identified plus unattributed bytes exactly once");
Check(mixed.Complete&&mixed.Attribution.Any(x=>x.Reason==AttributionReason.PathUnavailable&&x.IdentifiedProcess&&x.Outgoing==200),"path unavailable preserves process instance and is independent of observation completeness");
Check(mixed.Attribution.Where(x=>!x.IdentifiedProcess).Sum(x=>x.Incoming+x.Outgoing)==150,"unattributed failure summaries reconcile with process-null traffic");
var churn=new Aggregator(t){DetailedDiagnostics=true};var stress=Stopwatch.StartNew();for(int i=0;i<100000;i++)churn.Add(new(t,null,Direction.Incoming,1,t.AddMilliseconds(i%999),AttributionReason.ProcessExited,i+1,TrafficProtocol.UdpIpv4));
var crowded=churn.Advance(t.AddSeconds(3)).Single();Check(crowded.Rows.Sum(r=>r.Incoming)==100000&&crowded.Attribution.Length<=65&&crowded.Attribution.Sum(x=>x.Events)==100000,"100k short-lived unknown processes stay bounded to64 detail PIDs plus exact aggregate");
Console.WriteLine(JsonSerializer.Serialize(new{scenario="100k unknown PID events, bounded minute detail",summaryRows=crowded.Attribution.Length,events=crowded.Attribution.Sum(x=>x.Events),bytes=crowded.Rows.Sum(x=>x.Incoming),wallMs=stress.Elapsed.TotalMilliseconds}));
string root=Path.Combine(Path.GetTempPath(),"RaiderQuality-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
 string file=Path.Combine(root,"history.db");using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){store.Begin(t);store.Append(new(1,t,1,true,0,0,[],"historical unknown version"));store.Commit();}
 // Simulate an actual schema3 database with no provenance/quality tables.
 using(var db=new SqliteConnection("Data Source="+file+";Pooling=False")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE integrity;DROP TABLE epoch_profiles;DROP TABLE profiles;PRAGMA user_version=3";cmd.ExecuteNonQuery();}
 Check(!new HistoryReader(file).AttributionRows(20261027).Any(),"legacy diagnostic exports remain empty/unknown without querying absent schema4 tables");
 using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){
  Check(new HistoryReader(file).Sessions().All(s=>s.ApplicationVersion==null),"old sessions retain unknown version after additive schema3 upgrade");
  store.ConfigureProfile(new(BuildIdentity.Version,"unknown","desktop-collector",BuildIdentity.IntegrityModel,4,false,true));store.Begin(t.AddHours(1));
  store.Append(mixed with{StartUtc=t.AddHours(1)});
  var agg=new Aggregator(t.AddHours(1));agg.Advance(t.AddHours(1).AddSeconds(3));agg.Add(new(t.AddHours(1),process,Direction.Incoming,25,t.AddHours(1).AddSeconds(3)));
  foreach(var frame in Enumerable.Range(4,14).SelectMany(i=>agg.Advance(t.AddHours(1).AddSeconds(i))))store.Append(frame);
  store.FinalizeObservedHours(t.AddHours(3));store.Commit();Check(!new HistoryReader(file).Hours(20261027).Single(h=>h.UtcTicks==t.AddHours(1).Ticks).Finalized,"wall clock cannot finalize an active hour ahead of persisted capture watermark");
  store.RecordState(t.AddHours(1).AddSeconds(20),HistoryState.Stopped,"controlled fixture ended");store.FinalizeObservedHours(t.AddHours(3));store.Commit();
  var reader=new HistoryReader(file);var evidence=reader.Integrity(20261027);Check(reader.Attribution(20261027).Where(x=>!x.IdentifiedProcess).Sum(x=>x.Incoming+x.Outgoing)==150,"attribution reason totals survive reopen/SQLite without claiming missing bytes");Check(evidence.Sum(q=>q.Late)==1&&evidence.Sum(q=>q.DiscardedIncoming)==25,"sparse integer integrity history preserves late delta and discarded bytes once");
  Check(reader.Sessions().Any(s=>s.ApplicationVersion==BuildIdentity.Version&&s.SourceKind=="desktop-collector")&&reader.Sessions().Any(s=>s.ApplicationVersion==null),"new provenance does not invent earlier recording version");
  Check(reader.Hours(20261027).Any(h=>h.Finalized&&h.Incomplete>0),"finalized hourly aggregation is independent of measurement completeness");
  reader.ExportIntegrity(Path.Combine(root,"integrity.csv"),20261027);Check(File.ReadAllText(Path.Combine(root,"integrity.csv")).Contains("discarded_rx"),"explicit integrity evidence exports separately without silently rewriting finalized traffic");
  store.Clear();Check(store.Count("integrity")==0&&store.Count("epoch_profiles")==0,"clear cascades new evidence/session links along with original history");
 }
}finally{Directory.Delete(root,true);}
ReadinessRegression.Run(Check);
AdditionalRegression.Run(Check);
TailRegression.Run(Check);
Console.WriteLine($"{checks} quality checks passed; synthetic only");
