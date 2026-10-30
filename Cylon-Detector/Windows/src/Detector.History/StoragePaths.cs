namespace Detector.History;

public static class StoragePaths
{
    public static string LocalDirectory(string path)
    {
        string full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal)||new DriveInfo(Path.GetPathRoot(full)!).DriveType==DriveType.Network)
            throw new IOException("Only local disk destinations are supported");
        var ancestors=new Stack<string>();for(string? current=full;current!=null;current=Path.GetDirectoryName(current))ancestors.Push(current);
        while(ancestors.TryPop(out string? current))try{
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Storage destinations and ancestors must not be reparse points");
        }catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
        return full;
    }
    public static void DedicatedDirectory(string path,bool reports=false)
    {
        LocalDirectory(path);if(!Directory.Exists(path))return;
        foreach(string entry in Directory.EnumerateFileSystemEntries(path)){
            if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw new IOException("Dedicated storage contains a reparse point; preserved for review");
            string name=Path.GetFileName(entry);
            bool known=reports?(name.StartsWith("CylonDetector-",StringComparison.Ordinal)&& (name.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)||name.StartsWith("CylonDetector-report-index.sqlite",StringComparison.Ordinal))):name is "history.db" or "history.db-wal" or "history.db-shm" or "history.db.pre-schema2.bak" or "options.json" or "options.json.bak" or "options.json.tmp" or "Reports"||name.StartsWith("boot-request-",StringComparison.Ordinal);
            if(!known)throw new IOException("Choose an empty or dedicated Cylon Detector folder; unrelated files and their permissions are preserved");
        }
        ValidateDatabase(path,reports);
    }
    public static void ValidateDatabase(string path,bool reports=false)
    {
        LocalDirectory(path);
        string database=Path.Combine(path,reports?"CylonDetector-report-index.sqlite":"history.db");
        LocalDirectory(database);if(!File.Exists(database))return;
        using var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=database,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();using var query=db.CreateCommand();
        query.CommandText="PRAGMA application_id";
        long value=Convert.ToInt64(query.ExecuteScalar());if(reports?value!=0x43594450:value!=0x43594448)throw new InvalidDataException("Unrecognized database preserved; select a dedicated Cylon Detector destination");
    }
    public static string Logs(Preferences options,string fallback)=>LocalDirectory(string.IsNullOrWhiteSpace(options.LogDirectory)?fallback:options.LogDirectory);
    public static string Reports(Preferences options,string fallback)=>LocalDirectory(string.IsNullOrWhiteSpace(options.ReportDirectory)?Path.Combine(fallback,"Reports"):options.ReportDirectory);
}
