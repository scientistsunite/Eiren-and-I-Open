using BCC.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BCC.App;

/// <summary>
/// Optional Windows Service: no timer or polling. At boot, requests saved firmware setting.
/// At preshutdown, attempts original-setting restoration. Both are best-effort only.
/// This agent cannot run if the user starts another OS without an orderly Windows shutdown.
/// </summary>
internal static class WindowsAgentService
{
    private const int SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const int SERVICE_STOPPED = 1;
    private const int SERVICE_START_PENDING = 2;
    private const int SERVICE_STOP_PENDING = 3;
    private const int SERVICE_RUNNING = 4;
    private const int SERVICE_CONTROL_STOP = 1;
    private const int SERVICE_CONTROL_SHUTDOWN = 5;
    private const int SERVICE_CONTROL_PRESHUTDOWN = 15;
    private const int SERVICE_ACCEPT_STOP = 1;
    private const int SERVICE_ACCEPT_SHUTDOWN = 4;
    private const int SERVICE_ACCEPT_PRESHUTDOWN = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public ServiceMainCallback? Main;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode,
            ServiceSpecificExitCode, CheckPoint, WaitHint;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainCallback(int argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int HandlerCallback(int control, int eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] entries);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerCallback handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);

    private static readonly ManualResetEventSlim StopEvent = new(false);
    private static readonly ServiceMainCallback MainDelegate = ServiceMain;
    private static readonly HandlerCallback HandlerDelegate = ControlHandler;
    private static IntPtr _handle;
    private static int _stopping;
    private static readonly object HardwareLock = new();

    public static int Run()
    {
        var entries = new[] { new ServiceTableEntry { Name = "BatteryChargeControlAgent", Main = MainDelegate }, new ServiceTableEntry() };
        if (!StartServiceCtrlDispatcher(entries))
        {
            MachineStore.Log("Service dispatcher failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return 1;
        }
        return 0;
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        _handle = RegisterServiceCtrlHandlerEx("BatteryChargeControlAgent", HandlerDelegate, IntPtr.Zero);
        if (_handle == IntPtr.Zero) return;
        SetStatus(SERVICE_START_PENDING, 0, 1, 10000);
        SetStatus(SERVICE_RUNNING, SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_PRESHUTDOWN);
        Task.Run(() =>
        {
            try
            {
                lock (HardwareLock)
                {
                    if (Volatile.Read(ref _stopping) != 0) return;
                    new AgentLifecycle(new AgentCommands.WindowsEnvironment()).Handle(AgentEvent.Startup);
                }
            }
            catch (Exception ex) { MachineStore.Log("Startup reapplication failed: " + ex); }
        });
        StopEvent.Wait();
    }

    private static int ControlHandler(int code, int eventType, IntPtr eventData, IntPtr context)
    {
        if (code is SERVICE_CONTROL_PRESHUTDOWN or SERVICE_CONTROL_SHUTDOWN or SERVICE_CONTROL_STOP)
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return 0;
            SetStatus(SERVICE_STOP_PENDING, 0, 1, 10000);
            Task.Run(() =>
            {
                try
                {
                    // Ordinary manual service STOP should not modify user-selected hardware settings.
                    if (code is SERVICE_CONTROL_PRESHUTDOWN or SERVICE_CONTROL_SHUTDOWN)
                    {
                        lock (HardwareLock)
                        {
                            new AgentLifecycle(new AgentCommands.WindowsEnvironment()).Handle(AgentEvent.Shutdown);
                        }
                    }
                }
                catch (Exception ex) { MachineStore.Log("Shutdown restoration failed: " + ex); }
                finally { SetStatus(SERVICE_STOPPED, 0); StopEvent.Set(); }
            });
        }
        return 0;
    }

    private static void SetStatus(int state, int accepted, int checkpoint = 0, int waitHint = 0)
    {
        var value = new ServiceStatus
        {
            ServiceType = SERVICE_WIN32_OWN_PROCESS, CurrentState = state,
            ControlsAccepted = accepted, Win32ExitCode = 0, CheckPoint = checkpoint, WaitHint = waitHint
        };
        if (_handle != IntPtr.Zero) SetServiceStatus(_handle, ref value);
    }
}
