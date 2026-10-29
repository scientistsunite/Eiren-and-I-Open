using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Raider.History;
namespace Raider.Service;

public sealed record BootConfiguration(string OwnerSid,int OwnerPid,Preferences Options)
{
    public int Schema {get;init;}=1;
    public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"EirenAndI-CylonRaider");
    public static string FilePath=>Path.Combine(Root,"boot-service.json");
    public static string DefaultLogs=>Path.Combine(Root,"Logs");
    public static string DefaultReports=>Path.Combine(Root,"Reports");
    static readonly SecurityIdentifier Admin=new(WellKnownSidType.BuiltinAdministratorsSid,null),SystemSid=new(WellKnownSidType.LocalSystemSid,null);
    public static BootConfiguration Load()
    {
        StoragePaths.LocalDirectory(Root);VerifyDirectory(Root);
        if(new FileInfo(FilePath).Length>65536)throw new InvalidDataException("Boot configuration exceeds its size bound");
        using var input=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        var value=JsonSerializer.Deserialize<BootConfiguration>(input)??throw new InvalidDataException("Missing boot configuration");VerifyFile(FilePath,value.OwnerSid);value.Validate();VerifyDirectory(Root,value.OwnerSid);VerifyDirectory(value.Options.LogDirectory??DefaultLogs,value.OwnerSid);VerifyDirectory(value.Options.ReportDirectory??DefaultReports,value.OwnerSid);foreach(string name in new[]{"history.db","history.db-wal","history.db-shm"}){string file=Path.Combine(value.Options.LogDirectory??DefaultLogs,name);if(System.IO.File.Exists(file))VerifyFile(file,value.OwnerSid);}
        return value;
    }
    public static BootConfiguration? ForCurrentUser()
    {
        if(NativeService.ProcessId() is null)return null;
        var value=Load();return value.OwnerSid==WindowsIdentity.GetCurrent().User?.Value?value:throw new UnauthorizedAccessException("Boot history belongs to another Windows account");
    }
    public void Validate()
    {
        if(Schema!=1||Options.Startup!=StartupMode.Boot||string.IsNullOrWhiteSpace(OwnerSid))throw new InvalidDataException("Invalid boot configuration/owner");
        _=new SecurityIdentifier(OwnerSid);
        string logs=Destination(Options.LogDirectory??DefaultLogs),reports=Destination(Options.ReportDirectory??DefaultReports);
        if(logs.Equals(reports,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Detailed logs and hourly reports require separate directories");
    }
    public static string Destination(string path)
    {
        string full=StoragePaths.LocalDirectory(path),root=Path.GetFullPath(Root)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Boot destinations must be separate subdirectories of "+Root+" so unprivileged accounts cannot redirect SYSTEM writes");return full;
    }
    static DirectorySecurity Security(string owner)
    {
        var acl=new DirectorySecurity();acl.SetOwner(Admin);acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{Admin,SystemSid})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(owner),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));return acl;
    }
    public static void VerifyDirectory(string path,string? reader=null)
    {
        var acl=new DirectoryInfo(path).GetAccessControl();var owner=acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier??throw new UnauthorizedAccessException("Directory has no identifiable owner");
        if(!owner.Equals(Admin)&&!owner.Equals(SystemSid))throw new UnauthorizedAccessException("Existing boot storage is not administrator-owned; preserved for review");
        var writes=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){
            if(rule.AccessControlType!=AccessControlType.Allow||rule.IdentityReference.Equals(Admin)||rule.IdentityReference.Equals(SystemSid))continue;
            if((rule.FileSystemRights&writes)!=0||(reader!=null&&rule.IdentityReference.Value!=reader))throw new UnauthorizedAccessException("Boot storage grants access beyond its authorized owner");
        }
    }
    public static void VerifyFile(string path,string? reader=null)
    {
        if((System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Protected application file is a reparse point");
        var acl=new FileInfo(path).GetAccessControl();var owner=acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if(owner==null||(!owner.Equals(Admin)&&!owner.Equals(SystemSid)))throw new UnauthorizedAccessException("Protected application file has an unprivileged owner");
        var writes=FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){if(rule.AccessControlType!=AccessControlType.Allow||rule.IdentityReference.Equals(Admin)||rule.IdentityReference.Equals(SystemSid))continue;if((rule.FileSystemRights&writes)!=0||(reader!=null&&rule.IdentityReference.Value!=reader))throw new UnauthorizedAccessException("Protected file grants unauthorized access");}
    }
    public void SaveProtected()
    {
        Validate();if(NativeService.ProcessOwner(OwnerPid)!=OwnerSid)throw new UnauthorizedAccessException("Configuration owner does not match the requesting UI process");
        if(System.IO.File.Exists(FilePath)){
            VerifyDirectory(Root,OwnerSid);VerifyFile(FilePath,OwnerSid);using var existing=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(JsonSerializer.Deserialize<BootConfiguration>(existing)?.OwnerSid!=OwnerSid)throw new UnauthorizedAccessException("Boot history already belongs to another account; no reassignment performed");
        }
        if(Directory.Exists(Root))VerifyDirectory(Root,OwnerSid);else new DirectoryInfo(Root).Create(Security(OwnerSid));
        VerifyDirectory(Root,OwnerSid);
        foreach(string target in new[]{Options.LogDirectory??DefaultLogs,Options.ReportDirectory??DefaultReports}){
            string path=Destination(target);var parts=new Stack<string>();for(string? at=path;at!=null&&!at.Equals(Root,StringComparison.OrdinalIgnoreCase);at=Path.GetDirectoryName(at))parts.Push(at);
            while(parts.TryPop(out var at)){if(Directory.Exists(at))VerifyDirectory(at,OwnerSid);else new DirectoryInfo(at).Create(Security(OwnerSid));VerifyDirectory(at,OwnerSid);}
        }
        NativeService.WriteProtectedJson(FilePath,this,OwnerSid);
    }
    public static void RequireInstalledBinary()
    {
        string binary=Path.GetFullPath(Environment.ProcessPath!);string programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)+Path.DirectorySeparatorChar;
        if(!binary.StartsWith(programFiles,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(binary)!,"installed.marker")))throw new UnauthorizedAccessException("Boot monitoring requires the normal administrator-owned Program Files installation");
        StoragePaths.LocalDirectory(Path.GetDirectoryName(binary)!);VerifyDirectory(Path.GetDirectoryName(binary)!);foreach(string file in Directory.EnumerateFiles(Path.GetDirectoryName(binary)!))VerifyFile(file);
    }
}
