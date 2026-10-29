using Detector.Core;
using Detector.History;
using Microsoft.Data.Sqlite;

int checks=0;void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);checks++;Console.WriteLine("PASS "+reason);}
DateTime start=new(2026,10,30,0,0,0,DateTimeKind.Utc);
SensorUse Active(int second,string kind="microphone")=>new(kind,"Synthetic capture app",@"C:\Synthetic\capture.exe",start.AddSeconds(second).Ticks,null,"Synthetic Windows marker; not hardware proof","synthetic-owner");
Measurement Frame(int second,SensorSnapshot? sensors=null,int identity=1,long bytes=100)=>new(1,start.AddSeconds(second),1,true,0,0,[new(new Identity(identity,start.Ticks,"Synthetic app "+identity,@"C:\Synthetic\app-"+identity+".exe"),bytes,bytes/2)],"Synthetic privacy fixture"){SourceSession="synthetic",Sensors=sensors};
SensorSnapshot Snapshot(int second,params SensorUse[] uses)=>new(start.AddSeconds(second),uses,true,"Synthetic observation available");
var window=new PrivacyWindow();
for(int i=0;i<600;i++){var value=window.Observe(Frame(i,Snapshot(i)),false);Check(value.Frames.Length==0,"Default noncontinuous network remains RAM-only before sensor behavior");window.Accepted(value);}
Check(window.BufferedSeconds==305,"Always-on RAM buffer retains five minutes plus four-second source-delivery/current-bucket overlap");
var triggered=window.Observe(Frame(600,Snapshot(600,Active(600))),false);
Check(triggered.Frames.Length==301&&triggered.Frames.Min(frame=>frame.StartUtc)==start.AddSeconds(300),"Sensor start flushes all available prelude buckets including current bucket");
Check(triggered.Activities.Any(row=>row.Category=="microphone"&&row.Change=="Observed active"),"Microphone start is journaled with observable evidence");
window.Accepted(triggered);Check(window.Observe(Frame(600,Snapshot(600,Active(600))),false).Frames.Length==0,"Repeated frame is not persisted twice");
var missing=window.Observe(Frame(601,new(start.AddSeconds(601),[],false,"Synthetic access denied")),false);
Check(missing.Frames.Length==1&&window.HasActiveSensors,"Unavailable sensor observation does not fabricate a stop");window.Accepted(missing);
var ended=Active(600) with{StoppedUtcTicks=start.AddSeconds(602).Ticks};var stopped=window.Observe(Frame(602,Snapshot(602,ended)),false);window.Accepted(stopped);
Check(!window.HasActiveSensors,"Only explicit stopped metadata ends active state");
for(int i=603;i<902;i++){var selected=window.Observe(Frame(i,Snapshot(i,ended)),false);Check(selected.Frames.Length==1,"Each observed post-activity bucket before five-minute deadline is retained");window.Accepted(selected);}
var outside=window.Observe(Frame(902,Snapshot(902,ended)),false);Check(outside.Frames.Length==0,"Bucket beginning exactly at post-activity deadline is outside half-open capture window");Check(outside.Activities.Any(row=>row.Change=="After sensor observation"),"Post-activity context is explicitly summarized without causation claims");window.Accepted(outside);
for(int i=903;i<1500;i++){var selected=window.Observe(Frame(i,Snapshot(i)),false);window.Accepted(selected);}
var delayed=window.Observe(Frame(1500,Snapshot(1504,Active(1504,"camera"))),false);
Check(delayed.Frames.All(frame=>frame.StartUtc>=start.AddSeconds(1204)),"A later disjoint trigger with four-second ETW delivery delay never leaks older prelude seconds");
window.Accepted(delayed);
var another=window.Observe(Frame(1501,Snapshot(1505,Active(1504,"camera"),Active(1505))),false);Check(another.Frames.Length==1,"Overlapping microphone/camera activity extends capture without duplicate network rows");window.Accepted(another);
var continuous=new PrivacyWindow();var all=continuous.Observe(Frame(0),true);Check(all.Frames.Length==1,"Opt-in continuous network persistence works without sensor evidence");continuous.Accepted(all);
var off=continuous.Observe(Frame(1,identity:42),false);Check(off.Frames.Length==0&&off.Activities.Any(row=>row.Category=="Anomaly observation"),"Unfamiliar-process anomaly observations run outside sensor and continuous logging windows");continuous.Accepted(off);
for(int i=2;i<40;i++){var selection=continuous.Observe(Frame(i),false);continuous.Accepted(selection);}var unusual=continuous.Observe(Frame(40,bytes:20*1024*1024),false);Check(unusual.Activities.Any(row=>row.Change=="Unusual observed byte-rate change"),"Unusual-rate observations do not require microphone/camera activity");
var baseline=new PrivacyWindow();var historical=baseline.Observe(Frame(20,Snapshot(20,Active(0) with{StoppedUtcTicks=start.AddSeconds(5).Ticks})),false);Check(historical.Frames.Length==0,"Initial ended historical usage is evidence, not a fabricated live trigger");baseline.Accepted(historical);
var betweenPolls=baseline.Observe(Frame(22,Snapshot(22,Active(21) with{StoppedUtcTicks=start.AddSeconds(22).Ticks})),false);Check(betweenPolls.Frames.Length>0,"A complete new usage interval observed between polls triggers network context");
var large=new PrivacyWindow();string huge=new('x',300000);bool omitted=false;for(int i=0;i<300;i++){var frame=Frame(i) with{Rows=[new(new Identity(i,start.Ticks,huge,huge),1,1)]};var selection=large.Observe(frame,false);omitted|=selection.Activities.Any(row=>row.Change=="Resource-budget omission");large.Accepted(selection);}Check(large.BufferedBytes<=PrivacyWindow.MaximumBufferBytes&&omitted,"RAM cache overload is bounded with explicit context omissions");
var sensorFrame=Frame(0,new(start,[new("camera",huge,huge,start.Ticks,null,huge,huge)],true,huge));Check(HistoryWriter.EstimatedBytes(sensorFrame)>3_000_000,"Queue estimates charge all sensor record/string metadata");
var clockWindow=new PrivacyWindow();var firstEra=clockWindow.Observe(Frame(7200),false);clockWindow.Accepted(firstEra);
var secondEra=clockWindow.Observe(Frame(3600) with{SourceSession="new-source-after-clock-reset"},false);
Check(clockWindow.BufferedSeconds==1&&secondEra.Activities.Any(row=>row.Change=="Clock/source boundary reset"),"A new collector source after backward clock change restarts current RAM observation with an explicit coverage boundary");clockWindow.Accepted(secondEra);
var afterClock=clockWindow.Observe(Frame(3601,identity:123) with{SourceSession="new-source-after-clock-reset"},false);Check(afterClock.Activities.Any(row=>row.Category=="Anomaly observation"),"Anomaly observations continue immediately after backward-clock source restart");
string root=Path.Combine(Path.GetTempPath(),"Detector-Privacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
 string file=Path.Combine(root,"history.db");
 await using(var writer=new HistoryWriter(file,new Preferences{HourlyReports=false})){writer.Append(Frame(0,Snapshot(0)));writer.Append(Frame(1,Snapshot(1)));}
 Check(new HistoryReader(file).Days().Length>0,"Activity-only observation days participate in economical retention");
 using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString())){db.Open();using var query=db.CreateCommand();query.CommandText="SELECT COUNT(*) FROM detector_observed_seconds";Check(Convert.ToInt64(query.ExecuteScalar())==0,"Default policy writes no per-second network totals outside capture windows");query.CommandText="SELECT COUNT(*) FROM detector_activity";Check(Convert.ToInt64(query.ExecuteScalar())>0,"Device/anomaly activity uses the same main database despite no selected network seconds");}
}finally{Directory.Delete(root,true);}
Console.WriteLine($"PASS {checks} Detector privacy-policy checks; synthetic metadata only, no devices or network used");
