using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace Detector.App;
internal static class LocalPipe
{
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafePipeHandle CreateNamedPipe(string name,uint mode,uint pipeMode,uint instances,uint output,uint input,uint timeout,ref SecurityAttributes security);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    public static NamedPipeServerStream Create(string name)
    {
        var sid=WindowsIdentity.GetCurrent().User?.Value ?? throw new UnauthorizedAccessException("No local user SID");
        // Protected ACL; only this user, administrators and SYSTEM. Reject remote clients in the kernel.
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;"+sid+")(A;;GA;;;BA)(A;;GA;;;SY)",1,out var descriptor,out _)) throw new Win32Exception();
        try {
            var security=new SecurityAttributes{Length=Marshal.SizeOf<SecurityAttributes>(),Descriptor=descriptor};
            var handle=CreateNamedPipe(@"\\.\pipe\"+name,0x40000003|0x00080000,0x8,1,65536,65536,0,ref security);
            if(handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
            try { return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle); }
            catch { handle.Dispose(); throw; }
        } finally { LocalFree(descriptor); }
    }
}
