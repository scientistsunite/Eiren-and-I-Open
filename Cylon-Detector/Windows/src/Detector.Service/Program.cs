using System.Security.Principal;
using System.Diagnostics;
using Microsoft.Win32;
using System.ServiceProcess;
using System.Text.Json;
using Detector.History;
using Detector.Service;

if(args.Length==0){ServiceBase.Run(new DetectorBootService());return 0;}
try{
    using var identity=WindowsIdentity.GetCurrent();
    if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)&&!identity.IsSystem)throw new UnauthorizedAccessException("An explicit administrator/UAC invocation is required");
    if(args.Length==1&&args[0]=="/delete-personal-data"){
        if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"installed.marker")))throw new UnauthorizedAccessException("Normal installed mode required");
        DeletePersonalData();return 0;
    }
    BootConfiguration.RequireInstalledBinary();
    if(args.Length==2&&(args[0]=="/enable"||args[0]=="/configure")){
        if(new FileInfo(args[1]).Length>65536)throw new InvalidDataException("Configuration exceeds its size bound");
        var config=JsonSerializer.Deserialize<BootConfiguration>(File.ReadAllText(args[1]))??throw new InvalidDataException("Missing configuration");config.Validate();
        if(NativeService.ProcessOwner(config.OwnerPid)!=config.OwnerSid)throw new UnauthorizedAccessException("UI owner mismatch");
        if(args[0]=="/configure"){
            var old=BootConfiguration.Load();if(old.OwnerSid!=config.OwnerSid||old.Options.LogDirectory!=config.Options.LogDirectory||old.Options.ReportDirectory!=config.Options.ReportDirectory)throw new UnauthorizedAccessException("Destination/owner changes require explicit service reconfiguration");
            config.SaveProtected();return 0;
        }
        Stop();config.SaveProtected();
        if(NativeService.ProcessId()==null)NativeService.Sc("create",NativeService.Name,"binPath=","\""+Environment.ProcessPath+"\"","start=","auto","obj=","LocalSystem","DisplayName=","Cylon Detector pre-login monitoring");
        NativeService.Sc("privs",NativeService.Name,"privs=","SeChangeNotifyPrivilege/SeSystemProfilePrivilege/SeImpersonatePrivilege");
        NativeService.Sc("sidtype",NativeService.Name,"unrestricted");
        NativeService.Sc("sdset",NativeService.Name,"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;LC;;;AU)");
        NativeService.Sc("failure",NativeService.Name,"reset=","86400","actions=","restart/30000/restart/60000/none/0");
        NativeService.Sc("failureflag",NativeService.Name,"1");
        NativeService.Sc("description",NativeService.Name,"Optional local privacy activity observation, buffered network monitoring and durable local history before sign-in. No audio/video contents or application network communication.");Start();
    }else if(args.Length==1&&args[0]=="/stop")Stop();
    else if(args.Length==1&&args[0]=="/remove"){Stop();if(NativeService.ProcessId()!=null)NativeService.Sc("delete",NativeService.Name);RemoveStartup();}
    else if(args.Length==1&&args[0]=="/restart-if-enabled"){if(NativeService.ProcessId()!=null){Stop();Start();}}
    else if(args.Length==1&&args[0]=="/clear-history"){
        var config=BootConfiguration.Load();
        if(NativeService.ProcessId()==null){DeleteBootData(false);}
        else{
            using var service=new ServiceController(NativeService.Name);if(service.Status==ServiceControllerStatus.Stopped)DeleteBootData(false);
            else if(service.Status==ServiceControllerStatus.Running){
                string id=Guid.NewGuid().ToString("N");NativeService.WriteProtectedJson(Path.Combine(BootConfiguration.Root,"clear-request.json"),new ClearRequest(id),config.OwnerSid);service.ExecuteCommand(128);var elapsed=Stopwatch.StartNew();
                for(;;){string result=Path.Combine(BootConfiguration.Root,"clear-result.json");if(File.Exists(result)){var value=JsonSerializer.Deserialize<ClearResult>(File.ReadAllText(result));if(value?.Id==id){if(!value.Success)throw new IOException(value.Message);break;}}if(elapsed.Elapsed>TimeSpan.FromSeconds(120))throw new System.TimeoutException("Clear is still pending; live capture remains independent");await Task.Delay(100);}
            }else throw new IOException("Service is changing state; retry after it reaches Running or Stopped");
        }
    }
    else if(args.Length==1&&args[0]=="/delete-data"){if(NativeService.ProcessId()!=null)throw new IOException("Remove boot service before deleting its data");DeleteBootData();}
    else throw new InvalidDataException("Unsupported administrative operation");
    return 0;
}catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}

static void Stop()
{
    if(NativeService.ProcessId()==null)return;
    using var service=new ServiceController(NativeService.Name);
    if(service.Status==ServiceControllerStatus.StartPending)service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
    if(service.Status!=ServiceControllerStatus.Stopped&&service.Status!=ServiceControllerStatus.StopPending)service.Stop();
    service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(120));
}
static void Start(){using var service=new ServiceController(NativeService.Name);service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));}
static void DeleteBootData(bool removeConfiguration=true)
{
    if(!File.Exists(BootConfiguration.FilePath))return;
    var config=BootConfiguration.Load();
    foreach(string name in new[]{"history.db","history.db-wal","history.db-shm","history.db.pre-schema2.bak"})File.Delete(Path.Combine(config.Options.LogDirectory??BootConfiguration.DefaultLogs,name));
    string reports=config.Options.ReportDirectory??BootConfiguration.DefaultReports;
    foreach(var file in Directory.EnumerateFiles(reports,"CylonDetector-*")){
        var name=Path.GetFileName(file);if(name.Length>"CylonDetector-".Length+32&&Guid.TryParseExact(name.Substring("CylonDetector-".Length,32),"N",out _)&&(name.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)))File.Delete(file);
    }
    foreach(string name in new[]{"CylonDetector-report-index.sqlite","CylonDetector-report-index.sqlite-wal","CylonDetector-report-index.sqlite-shm"})File.Delete(Path.Combine(reports,name));
    if(removeConfiguration){File.Delete(BootConfiguration.FilePath);File.Delete(Path.Combine(BootConfiguration.Root,"clear-request.json"));File.Delete(Path.Combine(BootConfiguration.Root,"clear-result.json"));
        foreach(string target in new[]{config.Options.LogDirectory??BootConfiguration.DefaultLogs,reports}){for(string? folder=target;folder!=null&&folder.StartsWith(BootConfiguration.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);folder=Path.GetDirectoryName(folder)){if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);else break;}}
        if(Directory.Exists(BootConfiguration.Root)&&!Directory.EnumerateFileSystemEntries(BootConfiguration.Root).Any())Directory.Delete(BootConfiguration.Root);
    }
}

static void RemoveStartup()
{
    string expected=Path.Combine(AppContext.BaseDirectory,"CylonDetector.exe");
    foreach(string sid in Registry.Users.GetSubKeyNames().Where(s=>s.StartsWith("S-1-",StringComparison.Ordinal)&&!s.EndsWith("_Classes",StringComparison.Ordinal))){
        try{using var key=Registry.Users.OpenSubKey(sid+@"\Software\Microsoft\Windows\CurrentVersion\Run",true);string? value=key?.GetValue("EirenCylonDetector") as string;if(value!=null&&value.StartsWith("\""+expected+"\"",StringComparison.OrdinalIgnoreCase))key!.DeleteValue("EirenCylonDetector",false);}catch(UnauthorizedAccessException){}
    }
}

static void DeletePersonalData()
{
    string fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EirenAndI","CylonDetector");
    if(!Directory.Exists(fallback))return;StoragePaths.LocalDirectory(fallback);var settings=new SettingsFile(fallback);var options=settings.Load();if(!settings.Writable)throw new InvalidDataException("Unsupported settings preserved; custom destinations require explicit review");
    if(options.Startup==StartupMode.Boot)return;
    string logs=StoragePaths.Logs(options,fallback),reports=StoragePaths.Reports(options,fallback);StoragePaths.ValidateDatabase(logs);StoragePaths.ValidateDatabase(reports,true);
    string file=Path.Combine(logs,"history.db");string? id=File.Exists(file)?new HistoryReader(file).HistoryId():null;
    if(id!=null&&!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Unrecognized history identity; custom data preserved");
    if(id!=null&&Directory.Exists(reports))foreach(string report in Directory.EnumerateFiles(reports,"CylonDetector-"+id+"-*")){if((File.GetAttributes(report)&FileAttributes.ReparsePoint)!=0)throw new IOException("Report reparse point preserved");if(report.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||report.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))File.Delete(report);}
    string indexPath=Path.Combine(reports,"CylonDetector-report-index.sqlite");
    if(File.Exists(indexPath)&&id!=null){
        using var index=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=indexPath,Pooling=false}.ToString());index.Open();using var query=index.CreateCommand();
        query.CommandText="PRAGMA user_version";if(Convert.ToInt64(query.ExecuteScalar())>3)throw new InvalidDataException("Unsupported summary index preserved; custom data requires review");
        query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='report_state'";bool hasState=Convert.ToInt64(query.ExecuteScalar())==1;
        query.CommandText="DELETE FROM files WHERE history=$a;DELETE FROM cursors WHERE history=$a"+(hasState?";DELETE FROM report_state WHERE history=$a":"");query.Parameters.AddWithValue("$a",id);query.ExecuteNonQuery();
        query.CommandText="SELECT (SELECT COUNT(*) FROM files)+(SELECT COUNT(*) FROM cursors)"+(hasState?"+(SELECT COUNT(*) FROM report_state)":"");bool empty=Convert.ToInt64(query.ExecuteScalar())==0;index.Close();if(empty)foreach(string name in new[]{"CylonDetector-report-index.sqlite","CylonDetector-report-index.sqlite-wal","CylonDetector-report-index.sqlite-shm"})File.Delete(Path.Combine(reports,name));
    }
    foreach(string name in new[]{"history.db","history.db-wal","history.db-shm","history.db.pre-schema2.bak"})File.Delete(Path.Combine(logs,name));
    foreach(string folder in new[]{logs,reports})if(!folder.Equals(fallback,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
}
