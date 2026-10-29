using Microsoft.Data.Sqlite;
using Raider.History;

// Test-only, read-only evidence. A live helper is insufficient without a new,
// open recording epoch containing genuinely committed collector coverage.
internal sealed record ReadinessSession(long Epoch,long RequestedTicks,long? FirstCoverageTicks,long? EndedTicks,string? SourceSession)
{
    public string[] Rejections(long creation)=>new[]{
        RequestedTicks<creation?"session predates UI":null,
        FirstCoverageTicks==null?"no committed coverage":FirstCoverageTicks<creation?"coverage predates UI":null,
        EndedTicks!=null?"session ended":null,
        SourceSession==null?"collector source token absent":null
    }.OfType<string>().ToArray();
}
internal sealed record ReadinessHistory(string Path,bool Exists,int? Schema,ReadinessSession[] Sessions,string? Error)
{
    public double ReadMilliseconds {get;init;}
    public ReadinessEvent[] Events {get;init;}=[];
    public bool Ready(long creation)=>Error==null&&Sessions.Any(s=>s.Rejections(creation).Length==0);
}
internal sealed record ReadinessEvent(long Ticks,long Epoch,int State,string Reason);
internal static class IntegrationReadiness
{
    public static string DataRoot(string executable)
    {
        // AppContext.BaseDirectory includes its final separator. Preserve it in
        // the portable hash exactly as the production PlatformPaths does.
        string folder=Path.GetDirectoryName(Path.GetFullPath(executable))!+Path.DirectorySeparatorChar;
        string name=File.Exists(Path.Combine(folder,"installed.marker"))?"CylonRaider":Path.Combine("CylonRaider-Portable",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant())))[..12]);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EirenAndI",name);
    }
    public static ReadinessHistory Read(string path)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        if(!File.Exists(path))return new(path,false,null,[],"history file absent");
        try{
            StoragePaths.LocalDirectory(path);
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1}.ToString());db.Open();
            using var q=db.CreateCommand();q.CommandText="PRAGMA user_version";int schema=Convert.ToInt32(q.ExecuteScalar());
            q.CommandText=schema>=4?"SELECT e.id,e.requested_ticks,e.first_coverage_ticks,e.ended_ticks,(SELECT token FROM epoch_sources WHERE epoch=e.id) FROM epochs e WHERE e.id<>0 ORDER BY e.id DESC LIMIT 8":"SELECT id,requested_ticks,first_coverage_ticks,ended_ticks,NULL FROM epochs WHERE id<>0 ORDER BY id DESC LIMIT 8";
            using var r=q.ExecuteReader();var sessions=new List<ReadinessSession>();
            while(r.Read())sessions.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetString(4)));
            // Last control reasons distinguish waiting, pause and interruption;
            // they do not turn collector liveness into measured coverage.
            using var events=db.CreateCommand();events.CommandText="SELECT e.ticks,e.epoch,e.state,r.message FROM events e JOIN reasons r ON r.id=e.reason ORDER BY e.rowid DESC LIMIT 8";
            using var er=events.ExecuteReader();var recent=new List<ReadinessEvent>();while(er.Read())recent.Add(new(er.GetInt64(0),er.GetInt64(1),er.GetInt32(2),er.GetString(3)[..Math.Min(er.GetString(3).Length,512)]));
            return new(path,true,schema,sessions.ToArray(),null){ReadMilliseconds=clock.Elapsed.TotalMilliseconds,Events=recent.ToArray()};
        }catch(Exception ex)when(ex is IOException or SqliteException or UnauthorizedAccessException){return new(path,true,null,[],ex.GetType().Name+": "+ex.Message[..Math.Min(ex.Message.Length,512)]){ReadMilliseconds=clock.Elapsed.TotalMilliseconds};}
    }
}
