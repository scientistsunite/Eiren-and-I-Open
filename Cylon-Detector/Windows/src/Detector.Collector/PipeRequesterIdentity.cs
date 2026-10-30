using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

internal static class PipeRequesterIdentity
{
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("advapi32.dll",SetLastError=true)]
    static extern bool OpenProcessToken(SafeProcessHandle process,uint access,out SafeAccessTokenHandle token);

    internal static string OwnerSid(NamedPipeClientStream pipe)
    {
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint pid)||pid==0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot identify the local Detector requesting process");
        using var process=OpenProcess(0x1000,false,pid);
        if(process.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot verify the Detector requester's account");
        if(!OpenProcessToken(process,0x0008,out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot read the Detector requesting process token");
        using(token)
        using(var identity=new WindowsIdentity(token.DangerousGetHandle()))
        {
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint current)||current!=pid)
                throw new UnauthorizedAccessException("Detector pipe requester changed during account verification");
            return identity.User?.Value??throw new UnauthorizedAccessException("Detector requester has no Windows user SID");
        }
    }
}
