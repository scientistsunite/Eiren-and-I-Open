using Raider.Core;
using Raider.History;

internal static class ReadinessRegression
{
    public static void Run(Action<bool,string> check)
    {
        var at=new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc);
        long created=at.Ticks;
        var valid=new ReadinessSession(1,created+1,created+2,null,"real-source");
        check(valid.Rejections(created).Length==0,"readiness requires new open epoch, coverage and collector source together");
        foreach(var rejected in new[]{valid with{RequestedTicks=created-1},valid with{FirstCoverageTicks=null},valid with{FirstCoverageTicks=created-1},valid with{EndedTicks=created+3},valid with{SourceSession=null}})
            check(rejected.Rejections(created).Length>0,"readiness identifies precise failed epoch predicate without accepting stale or unavailable coverage");
        string root=Path.Combine(Path.GetTempPath(),"RaiderReadiness-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string file=Path.Combine(root,"history.db");
        try{
            check(!IntegrationReadiness.Read(file).Ready(created)&&!File.Exists(file),"readiness absent-file inspection never creates a database");
            using(var store=new HistoryStore(file,TimeZoneInfo.Utc)){
                store.Begin(at.AddSeconds(1));store.Commit();
                var pending=IntegrationReadiness.Read(file);
                check(!pending.Ready(created)&&pending.Sessions.Single().Rejections(created).Contains("no committed coverage"),"committed session initialization alone cannot satisfy readiness");
                store.Append(new Measurement(1,at.AddSeconds(2),1,true,0,0,[],"verified silence"){SourceSession="readiness-source"});
                check(!IntegrationReadiness.Read(file).Ready(created),"readiness rejects coverage still inside uncommitted SQLite transaction");
                store.Commit();
                check(IntegrationReadiness.Read(file).Ready(created),"readiness accepts only genuinely committed new-session verified-zero coverage");
                store.RecordState(at.AddSeconds(3),HistoryState.Stopped,"fixture stop");store.Commit();
                check(!IntegrationReadiness.Read(file).Ready(created),"readiness rejects a stopped session after database reopen");
            }
            string malformed=Path.Combine(root,"invalid.db");File.WriteAllText(malformed,"not SQLite");
            var bad=IntegrationReadiness.Read(malformed);
            check(!bad.Ready(created)&&bad.Error?.Contains("SqliteException")==true,"readiness preserves SQLite initialization/read failures as explicit evidence");
            check(new ReadinessHistory(file,true,4,[],"busy").Ready(created)==false,"SQLite read failure never becomes successful recording acceptance");
        }finally{Directory.Delete(root,true);}
    }
}
