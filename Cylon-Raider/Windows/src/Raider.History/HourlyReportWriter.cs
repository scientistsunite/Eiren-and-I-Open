using System.Diagnostics;
using Microsoft.Data.Sqlite;
namespace Raider.History;

// Independent files and database connection. Never called synchronously by capture/history.
public sealed class HourlyReportWriter : IAsyncDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Task worker;
    volatile Preferences options;
    volatile string status="Waiting for hourly reports";
    sealed class ClearWork(Func<Task>? details){public readonly TaskCompletionSource Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);public readonly Func<Task>? Details=details;public bool DetailsDone;}
    readonly object clearGate=new();
    long maximumTicks,lastGeneratedTicks;ClearWork? clearRequest;
    public string Status=>status;
    public DateTime? LastGeneratedUtc{get{long ticks=Interlocked.Read(ref lastGeneratedTicks);return ticks>0?new DateTime(ticks,DateTimeKind.Utc):null;}}
    public double MaximumReportMs=>Interlocked.Read(ref maximumTicks)/(double)TimeSpan.TicksPerMillisecond;
    public HourlyReportWriter(string historyFile,string directory,Preferences preferences,Func<Task>? beforeReport=null,Action<string>? prepareDirectory=null)
    {
        options=preferences;
        worker=Task.Run(async()=>{
            bool prepared=false;string? loadedHistory=null;
            try{
                while(!stop.IsCancellationRequested){
                    if(!options.HourlyReports&&Volatile.Read(ref clearRequest)==null){status="Automatic report files disabled; detailed history continues";await Task.Delay(500,stop.Token);continue;}
                    ClearWork? activeClear=null;
                    try{
                        var clearing=Volatile.Read(ref clearRequest);activeClear=clearing;
                        if(clearing!=null&&!clearing.DetailsDone){if(clearing.Details!=null)await clearing.Details();clearing.DetailsDone=true;}
                        string root=StoragePaths.LocalDirectory(directory);if(!prepared){prepareDirectory?.Invoke(root);prepared=true;}Directory.CreateDirectory(root);
                        var reader=new HistoryReader(historyFile);string history=reader.HistoryId();
                        if(!Guid.TryParseExact(history,"N",out _))throw new InvalidDataException("Invalid history identity");
                        using var index=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=StoragePaths.LocalDirectory(Path.Combine(root,"CylonRaider-report-index.sqlite")),Pooling=false,DefaultTimeout=1}.ToString());index.Open();
                        using(var create=index.CreateCommand()){
                            create.CommandText="PRAGMA application_id";long id=Convert.ToInt64(create.ExecuteScalar());
                            create.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";long tables=Convert.ToInt64(create.ExecuteScalar());
                            if(id!=0x43595250&&(id!=0||tables!=0))throw new InvalidDataException("Unrecognized report index; file preserved");
                            if(id==0){create.CommandText="PRAGMA application_id=1129927248;PRAGMA user_version=1;CREATE TABLE cursors(history TEXT PRIMARY KEY,sequence INTEGER NOT NULL,hour INTEGER NOT NULL);CREATE TABLE files(history TEXT NOT NULL,hour INTEGER NOT NULL,day INTEGER NOT NULL,name TEXT NOT NULL,PRIMARY KEY(history,hour));";create.ExecuteNonQuery();}
                            create.CommandText="PRAGMA user_version";long version=Convert.ToInt64(create.ExecuteScalar());
                            if(version is <1 or >2)throw new InvalidDataException("Unsupported report index version; file preserved");
                            create.CommandText="PRAGMA journal_mode=WAL;PRAGMA synchronous=FULL";create.ExecuteNonQuery();
                            if(version==1){create.CommandText="BEGIN IMMEDIATE;CREATE TABLE report_state(history TEXT PRIMARY KEY,last_ticks INTEGER NOT NULL);PRAGMA user_version=2;COMMIT";create.ExecuteNonQuery();}
                        }
                        if(loadedHistory!=history){using var stamp=index.CreateCommand();stamp.CommandText="SELECT last_ticks FROM report_state WHERE history=$a";stamp.Parameters.AddWithValue("$a",history);long ticks=Convert.ToInt64(stamp.ExecuteScalar());if(ticks<0||ticks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid recorded report timestamp");Interlocked.Exchange(ref lastGeneratedTicks,ticks);loadedHistory=history;}
                        if(clearing!=null){
                            for(;;){var names=new List<string>();using(var query=index.CreateCommand()){query.CommandText="SELECT name FROM files WHERE history=$a LIMIT 256";query.Parameters.AddWithValue("$a",history);using var rows=query.ExecuteReader();while(rows.Read())names.Add(rows.GetString(0));}if(names.Count==0)break;
                                foreach(string name in names){if(Path.GetFileName(name)!=name||!name.StartsWith("CylonRaider-"+history+"-",StringComparison.Ordinal))throw new InvalidDataException("Unrecognized report filename");File.Delete(Path.Combine(root,name));using var query=index.CreateCommand();query.CommandText="DELETE FROM files WHERE history=$a AND name=$b";query.Parameters.AddWithValue("$a",history);query.Parameters.AddWithValue("$b",name);query.ExecuteNonQuery();}
                            }
                            using(var query=index.CreateCommand()){query.CommandText="DELETE FROM cursors WHERE history=$a;DELETE FROM report_state WHERE history=$a";query.Parameters.AddWithValue("$a",history);query.ExecuteNonQuery();}Interlocked.Exchange(ref lastGeneratedTicks,0);Interlocked.CompareExchange(ref clearRequest,null,clearing);clearing.Completion.TrySetResult();
                        }
                        if(!options.HourlyReports){await Task.Delay(500,stop.Token);continue;}
                        long sequence=0,hour=0;using(var cursor=index.CreateCommand()){cursor.CommandText="SELECT sequence,hour FROM cursors WHERE history=$a";cursor.Parameters.AddWithValue("$a",history);using var row=cursor.ExecuteReader();if(row.Read()){sequence=row.GetInt64(0);hour=row.GetInt64(1);}}
                        var jobs=new List<(long Id,long Sequence,int Day)>();
                        using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=historyFile,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1}.ToString())){
                            source.Open();using var query=source.CreateCommand();query.CommandText="SELECT id,report_sequence,day FROM hours WHERE finalized=1 AND retiring=0 AND day>=COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='retention_cutoff'),0) AND (report_sequence>$a OR (report_sequence=$a AND id>$b)) ORDER BY report_sequence,id LIMIT 8";query.Parameters.AddWithValue("$a",sequence);query.Parameters.AddWithValue("$b",hour);using var rows=query.ExecuteReader();while(rows.Read())jobs.Add((rows.GetInt64(0),rows.GetInt64(1),rows.GetInt32(2)));
                        }
                        foreach(var job in jobs){
                            stop.Token.ThrowIfCancellationRequested();if(!options.HourlyReports)break;
                            var elapsed=Stopwatch.StartNew();if(beforeReport!=null)await beforeReport();
                            string name=$"CylonRaider-{history}-{job.Day}-{job.Id}.csv";
                            reader.ExportHour(Path.Combine(root,name),job.Id,job.Day);
                            long generated=DateTime.UtcNow.Ticks;using var commit=index.CreateCommand();commit.CommandText="BEGIN IMMEDIATE;INSERT INTO files VALUES($a,$b,$c,$d) ON CONFLICT(history,hour) DO UPDATE SET day=excluded.day,name=excluded.name;INSERT INTO cursors VALUES($a,$e,$b) ON CONFLICT(history) DO UPDATE SET sequence=excluded.sequence,hour=excluded.hour;INSERT INTO report_state VALUES($a,$f) ON CONFLICT(history) DO UPDATE SET last_ticks=excluded.last_ticks;COMMIT";
                            commit.Parameters.AddWithValue("$a",history);commit.Parameters.AddWithValue("$b",job.Id);commit.Parameters.AddWithValue("$c",job.Day);commit.Parameters.AddWithValue("$d",name);commit.Parameters.AddWithValue("$e",job.Sequence);commit.Parameters.AddWithValue("$f",generated);commit.ExecuteNonQuery();Interlocked.Exchange(ref lastGeneratedTicks,generated);
                            long ticks=elapsed.Elapsed.Ticks,old;do{old=Interlocked.Read(ref maximumTicks);if(ticks<=old)break;}while(Interlocked.CompareExchange(ref maximumTicks,ticks,old)!=old);
                        }
                        if(!options.Indefinite){
                            var days=reader.Days();if(days.Length>=15){int cutoff=days.OrderDescending().Take(15).Min();var expired=new List<(long Hour,string Name)>();using(var q=index.CreateCommand()){q.CommandText="SELECT hour,name FROM files WHERE history=$a AND day<$b LIMIT 32";q.Parameters.AddWithValue("$a",history);q.Parameters.AddWithValue("$b",cutoff);using var r=q.ExecuteReader();while(r.Read())expired.Add((r.GetInt64(0),r.GetString(1)));}
                                foreach(var old in expired){if(Path.GetFileName(old.Name)!=old.Name||!old.Name.StartsWith("CylonRaider-"+history+"-",StringComparison.Ordinal))throw new InvalidDataException("Invalid report index filename");File.Delete(Path.Combine(root,old.Name));using var q=index.CreateCommand();q.CommandText="DELETE FROM files WHERE history=$a AND hour=$b";q.Parameters.AddWithValue("$a",history);q.Parameters.AddWithValue("$b",old.Hour);q.ExecuteNonQuery();}
                            }
                        }
                        status=jobs.Count==8?"Hourly reports catching up in bounded batches — detailed history continues":"Hourly report files up to date — separate from detailed history";
                    }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or InvalidOperationException){status="REPORT DELAY/FAILURE (history independent): "+ex.Message;if(activeClear!=null&&ReferenceEquals(Interlocked.CompareExchange(ref clearRequest,null,activeClear),activeClear))activeClear.Completion.TrySetException(ex);}
                    await Task.Delay(TimeSpan.FromSeconds(5),stop.Token);
                }
            }catch(OperationCanceledException)when(stop.IsCancellationRequested){}finally{Interlocked.Exchange(ref clearRequest,null)?.Completion.TrySetCanceled();}
        });
    }
    public void Configure(Preferences value)=>options=value;
    public Task ClearAsync(Func<Task>? clearDetails=null){lock(clearGate){clearRequest??=new ClearWork(clearDetails);return clearRequest.Completion.Task;}}
    public async ValueTask DisposeAsync(){stop.Cancel();await worker;stop.Dispose();}
}
