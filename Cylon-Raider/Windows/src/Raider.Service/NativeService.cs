using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace Raider.Service;

public static class NativeService
{
    public const string Name="EirenCylonRaiderV02",PipeName="CylonRaider-Service-v02";
    [StructLayout(LayoutKind.Sequential)]struct Attributes{public int Length;public IntPtr Descriptor;public int Inherit;}
    [StructLayout(LayoutKind.Sequential)]struct ServiceStatus{public uint Type,State,Accepted,Exit,Specific,Checkpoint,Wait,Pid,Flags;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatusEx(IntPtr service,int level,out ServiceStatus status,int bytes,out int needed);
    [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string s,uint version,out IntPtr descriptor,out uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafePipeHandle CreateNamedPipe(string name,uint mode,uint pipeMode,uint instances,uint output,uint input,uint timeout,ref Attributes security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint sharing,ref Attributes security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr value);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    public static uint? ProcessId()
    {
        IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();
        try{IntPtr service=OpenService(manager,Name,4);if(service==IntPtr.Zero){if(Marshal.GetLastWin32Error()==1060)return null;throw new Win32Exception();}try{if(!QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<ServiceStatus>(),out _))throw new Win32Exception();return status.State==4?status.Pid:0;}finally{CloseServiceHandle(service);}}finally{CloseServiceHandle(manager);}
    }
    public static string ProcessOwner(int pid)
    {
        IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)throw new Win32Exception();
        try{if(!OpenProcessToken(process,8,out var token))throw new Win32Exception();try{using var identity=new WindowsIdentity(token);return identity.User?.Value??throw new UnauthorizedAccessException("Process has no user identity");}finally{CloseHandle(token);}}finally{CloseHandle(process);}
    }
    public static NamedPipeServerStream CreatePipe(string owner)
    {
        _=new SecurityIdentifier(owner);
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;"+owner+")(A;;GA;;;BA)(A;;GA;;;SY)",1,out var descriptor,out _))throw new Win32Exception();
        try{var security=new Attributes{Length=Marshal.SizeOf<Attributes>(),Descriptor=descriptor};var handle=CreateNamedPipe(@"\\.\pipe\"+PipeName,0x40000003|0x00080000,0x8,1,4096,4096,0,ref security);if(handle.IsInvalid){handle.Dispose();throw new Win32Exception();}try{return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);}catch{handle.Dispose();throw;}}
        finally{LocalFree(descriptor);}
    }
    public static bool Authorized(NamedPipeServerStream pipe,string owner)
    {
        bool result=false;pipe.RunAsClient(()=>{using var identity=WindowsIdentity.GetCurrent();result=identity.User?.Value==owner||identity.IsSystem||new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);});return result;
    }
    public static async Task<NamedPipeClientStream> Connect(CancellationToken token)
    {
        uint? expected=ProcessId();if(expected is null or 0)throw new IOException("Boot monitoring is configured but its service is not running; use Options to re-enable it with UAC");
        var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
        try{await pipe.ConnectAsync(10000,token);if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint actual)||actual!=expected||ProcessId()!=actual)throw new UnauthorizedAccessException("Boot pipe server does not match the SCM-owned service process");await pipe.WriteAsync(new byte[]{0x52},token);return pipe;}catch{pipe.Dispose();throw;}
    }
    public static void WriteProtectedJson(string path,object value,string reader)
    {
        using var identity=WindowsIdentity.GetCurrent();string owner=identity.IsSystem?"SY":"BA";
        string sddl="O:"+owner+"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;"+new SecurityIdentifier(reader).Value+")";
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl,1,out var descriptor,out _))throw new Win32Exception();
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            var security=new Attributes{Length=Marshal.SizeOf<Attributes>(),Descriptor=descriptor};
            using(var handle=CreateFile(temporary,0x40000000,0,ref security,1,0x80,IntPtr.Zero)){
                if(handle.IsInvalid)throw new Win32Exception();using var stream=new FileStream(handle,FileAccess.Write);var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);stream.Write(bytes);stream.Flush(true);
            }
            File.Move(temporary,path,true);
        }finally{LocalFree(descriptor);File.Delete(temporary);}
    }
    public static void Sc(params string[] arguments)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string value in arguments)info.ArgumentList.Add(value);
        using var process=Process.Start(info)??throw new IOException("Cannot start Windows service control");string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new IOException("Windows service control failed ("+process.ExitCode+"): "+output+error);
    }
}
