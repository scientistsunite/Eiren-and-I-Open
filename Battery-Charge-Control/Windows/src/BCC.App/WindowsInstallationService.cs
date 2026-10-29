using BCC.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BCC.App;

// Native adapter. Never linked into the simulation assembly or preview.
internal sealed class WindowsInstallationService(string expectedExecutable) : IInstallationService
{
    private const string Name="BatteryChargeControlAgent";
    private const uint QueryStatus=4, StopAccess=0x20, StartAccess=0x10, DeleteAccess=0x10000;
    [StructLayout(LayoutKind.Sequential)]
    private struct Status { public uint Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, WaitHint; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool QueryServiceStatus(IntPtr service, out Status status);
    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool ControlService(IntPtr service, uint control, out Status status);
    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool DeleteService(IntPtr service);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool StartService(IntPtr service, uint count, IntPtr arguments);
    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    private void VerifyOwner()
    {
        string? image=Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\"+Name,"ImagePath",null) as string;
        string expected=$"\"{Path.GetFullPath(expectedExecutable)}\" --service";
        if (!string.Equals(image,expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Service executable does not match this installation. No service change attempted; resolve the conflict before retrying.");
    }
    private IntPtr Open(uint access, out int error)
    {
        var manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr handle;
        try { handle=OpenService(manager,Name,access); error=handle==IntPtr.Zero?Marshal.GetLastWin32Error():0; }
        finally { CloseServiceHandle(manager); }
        if(handle!=IntPtr.Zero)
        {
            try { VerifyOwner(); } catch { CloseServiceHandle(handle); throw; }
        }
        return handle;
    }
    public InstalledServiceState ReadState()
    {
        var handle=Open(QueryStatus,out int error);
        if(handle==IntPtr.Zero)
        {
            if(error==1060) return InstalledServiceState.Absent;
            if(error==1072) return InstalledServiceState.DeletePending;
            throw new Win32Exception(error);
        }
        try
        {
            if(!QueryServiceStatus(handle,out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return status.State switch { 1=>InstalledServiceState.Stopped, 2=>InstalledServiceState.StartPending,
                3=>InstalledServiceState.StopPending, 4=>InstalledServiceState.Running, _=>InstalledServiceState.Unknown };
        }
        finally { CloseServiceHandle(handle); }
    }
    public void Stop()
    {
        var handle=Open(StopAccess,out int error);
        if(handle==IntPtr.Zero) { if(error==1060)return; throw new Win32Exception(error); }
        try
        {
            if(!ControlService(handle,1,out _)) { error=Marshal.GetLastWin32Error(); if(error!=1062)throw new Win32Exception(error); }
        }
        finally { CloseServiceHandle(handle); }
    }
    public void Delete()
    {
        var handle=Open(DeleteAccess,out int error);
        if(handle==IntPtr.Zero) { if(error is 1060 or 1072)return; throw new Win32Exception(error); }
        try
        {
            if(!DeleteService(handle)) { error=Marshal.GetLastWin32Error(); if(error!=1072)throw new Win32Exception(error); }
        }
        finally { CloseServiceHandle(handle); }
    }
    public void Start()
    {
        var handle=Open(StartAccess,out int error);
        if(handle==IntPtr.Zero)throw new Win32Exception(error);
        try
        {
            if(!StartService(handle,0,IntPtr.Zero)) { error=Marshal.GetLastWin32Error(); if(error!=1056)throw new Win32Exception(error); }
        }
        finally { CloseServiceHandle(handle); }
    }
    public void WaitForTransition() => Thread.Sleep(250);
}
