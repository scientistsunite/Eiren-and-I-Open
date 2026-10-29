using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Detector.History;
namespace Detector.App;
internal static class PlatformPaths
{
    public const string WindowTitle="Cylon Detector v0.1";
    public const int RestoreMessage=0x8000+142, ShutdownMessage=0x8000+143;
    public static bool Installed=>File.Exists(Path.Combine(AppContext.BaseDirectory,"installed.marker"));
    public static string DataRoot {
        get {
            string name=Installed?"CylonDetector":Path.Combine("CylonDetector-Portable",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant())))[..12]);
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EirenAndI",name);RequireLocal(path);return path;
        }
    }
    public static void RequireLocal(string path)
    {
        _=StoragePaths.LocalDirectory(path);
    }
    static bool AlreadyPrivate(string directory)
    {
        if(!Directory.Exists(directory))return false;
        var acl=new DirectoryInfo(directory).GetAccessControl();string? owner=acl.GetOwner(typeof(SecurityIdentifier))?.Value;
        string me=WindowsIdentity.GetCurrent().User!.Value;
        if(owner!=me&&owner!="S-1-5-18"&&owner!="S-1-5-32-544")return false;
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))){
            if(rule.AccessControlType!=AccessControlType.Allow)continue;string sid=rule.IdentityReference.Value;
            if(sid==me||sid=="S-1-5-18"||sid=="S-1-5-32-544")continue;
            if(sid=="S-1-3-0"&&(rule.PropagationFlags&PropagationFlags.InheritOnly)!=0)continue;
            return false;
        }
        return true;
    }
    public static void PrepareStorage(string directory,bool reports=false)
    {
        RequireLocal(directory);
        if(AlreadyPrivate(directory)){StoragePaths.ValidateDatabase(directory,reports);return;}
        StoragePaths.DedicatedDirectory(directory,reports);Protect(directory);
    }
    public static void Protect(string directory)
    {
        RequireLocal(directory);if(AlreadyPrivate(directory))return;Directory.CreateDirectory(directory);
        if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("History directory cannot be a reparse point");
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
    }
    public static Icon OfficialIcon()=>File.Exists(Path.Combine(AppContext.BaseDirectory,"assets","CylonDetector.ico"))?new Icon(Path.Combine(AppContext.BaseDirectory,"assets","CylonDetector.ico")):SystemIcons.Application;
    public static void Startup(StartupMode mode)
    {
        if(mode==StartupMode.Boot)throw new NotSupportedException("Boot monitoring requires the explicit UAC service configuration flow");
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(mode==StartupMode.SignIn)key.SetValue("EirenCylonDetector","\""+Environment.ProcessPath+"\" --startup");else key.DeleteValue("EirenCylonDetector",false);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string? className,string title);
    [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,int message,IntPtr w,IntPtr l);
    public static string? RegisteredUninstaller()
    {
        if(!Installed)return null;
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
        using var key=machine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{1D725EE6-2618-469E-A3DF-D25A568FCF38}_is1");
        string value=(key?.GetValue("UninstallString") as string)??"";
        string candidate=value.StartsWith('"')?value.Split('"').ElementAtOrDefault(1)??"":value;
        string expected=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"unins000.exe"));
        return candidate.Length>0&&Path.GetFullPath(candidate).Equals(expected,StringComparison.OrdinalIgnoreCase)&&File.Exists(expected)?expected:null;
    }
    public static void OpenExistingFolder(string path){RequireLocal(path);if(!Directory.Exists(path))throw new DirectoryNotFoundException("Folder does not exist");var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};info.ArgumentList.Add(Path.GetFullPath(path));Process.Start(info);}
}
