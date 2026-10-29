using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Raider.App;
internal sealed record HostedService(string Name,string DisplayName,string Description);
internal static class ServiceHosts
{
    sealed class ServiceHandle:SafeHandleZeroOrMinusOneIsInvalid{public ServiceHandle():base(true){}protected override bool ReleaseHandle()=>CloseServiceHandle(handle);}
    [StructLayout(LayoutKind.Sequential)]struct Status{public uint Type,State,Accepted,Exit,SpecificExit,Checkpoint,Wait,Pid,Flags;}
    [StructLayout(LayoutKind.Sequential)]struct Entry{public IntPtr Name,Display;public Status Status;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ServiceHandle OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool EnumServicesStatusEx(ServiceHandle manager,int level,uint type,uint state,IntPtr buffer,int size,out int needed,out int count,ref int resume,string? group);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ServiceHandle OpenService(ServiceHandle manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatusEx(ServiceHandle service,int level,out Status status,int size,out int needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryServiceConfig2(ServiceHandle service,uint level,IntPtr buffer,int size,out int needed);
    [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
    public static HostedService[] ForProcess(int pid,long created)
    {
        using var process=Process.GetProcessById(pid);if(process.StartTime.ToUniversalTime().Ticks!=created)throw new IOException("Process instance changed; service attribution unavailable");
        using var manager=OpenSCManager(null,null,5);if(manager.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        int size=65536;var result=new List<HostedService>();
        for(int attempt=0;attempt<2;attempt++){
            IntPtr buffer=Marshal.AllocHGlobal(size);
            try{int resume=0;if(!EnumServicesStatusEx(manager,0,0x30,3,buffer,size,out int needed,out int count,ref resume,null)){int error=Marshal.GetLastWin32Error();if(error==234&&needed>size&&needed<=4*1024*1024){size=needed;continue;}throw new System.ComponentModel.Win32Exception(error);}
                int stride=Marshal.SizeOf<Entry>();if(count<0||count>size/stride)throw new IOException("Unexpected SCM response size");
                for(int i=0;i<count;i++){var entry=Marshal.PtrToStructure<Entry>(buffer+i*stride);if(entry.Status.Pid!=pid)continue;string name=Marshal.PtrToStringUni(entry.Name)??"";using var service=OpenService(manager,name,5);if(service.IsInvalid||!QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<Status>(),out _)||status.Pid!=pid)continue;string description="Description unavailable";IntPtr details=Marshal.AllocHGlobal(8192);try{if(QueryServiceConfig2(service,1,details,8192,out _))description=Marshal.PtrToStringUni(Marshal.ReadIntPtr(details))??"";}finally{Marshal.FreeHGlobal(details);}result.Add(new(name,Marshal.PtrToStringUni(entry.Display)??name,description));}break;
            }finally{Marshal.FreeHGlobal(buffer);}
        }
        process.Refresh();if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=created)throw new IOException("Process exited/reused during SCM inspection");return result.ToArray();
    }
}
