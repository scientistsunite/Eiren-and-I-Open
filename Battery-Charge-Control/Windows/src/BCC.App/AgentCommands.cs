using BCC.Core;
using System.Diagnostics;

namespace BCC.App;

internal static class AgentCommands
{
    public static int Run(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "--init":
                    SecurePolicyDirectory();
                    break;
                case "--set" when args.Length == 2:
                    if (!int.TryParse(args[1], out int requested)) throw new ArgumentException("Invalid limit");
                    SetNewLimit(requested);
                    break;
                case "--restore":
                case "--prepare":
                    Restore(disablePolicy: true);
                    break;
                case "--resume":
                    Resume();
                    break;
                case "--install-service":
                    SecurePolicyDirectory();
                    InstallService();
                    break;
                case "--prepare-maintenance" when args.Length == 2:
                    SecurePolicyDirectory();
                    Maintenance(args[1]).PrepareRemoval();
                    break;
                case "--uninstall-service-and-restore": // Compatibility with existing uninstall logs.
                    Maintenance(Environment.ProcessPath!).PrepareRemoval();
                    break;
                case "--finish-install-agent":
                    SecurePolicyDirectory();
                    Maintenance(Environment.ProcessPath!).CompleteInstallation(true, InstallService);
                    break;
                case "--finish-install-monitoring":
                    SecurePolicyDirectory();
                    Maintenance(Environment.ProcessPath!).CompleteInstallation(false, InstallService);
                    break;
                default: throw new ArgumentException("Unknown agent command");
            }
            return 0;
        }
        catch (Exception ex)
        {
            MachineStore.Log(args[0] + " failed: " + ex);
            return 1;
        }
    }

    internal sealed class WindowsEnvironment : IChargeEnvironment
    {
        public DeviceInfo ReadDevice() => DeviceInfo.Detect();
        public BatterySnapshot ReadBattery() => BatterySnapshot.Read();
        public IChargingController? LoadController(DeviceInfo device) => ControllerRegistry.Load(device, AppContext.BaseDirectory);
        public MachinePolicy ReadPolicy() => MachineStore.Read();
        public void WritePolicy(MachinePolicy policy) => MachineStore.Write(policy);
        public void Log(string message) => MachineStore.Log(message);
        public void SecurePolicyDirectory() => AgentCommands.SecurePolicyDirectory();
    }

    private static readonly ChargingOperations Operations = new(new WindowsEnvironment());
    public static void SetNewLimit(int requested) => Operations.SetNewLimit(requested);
    public static void Restore(bool disablePolicy) => Operations.Restore(disablePolicy);
    public static void Resume() => Operations.Resume();
    public static void ApplySaved() => Operations.ApplySaved();

    private static InstallationMaintenance Maintenance(string installedExecutable)
    {
        var service=new WindowsInstallationService(installedExecutable);
        bool missingRecovery=!File.Exists(MachineStore.PolicyFile) &&
            (File.Exists(installedExecutable) || service.ReadState()!=InstalledServiceState.Absent);
        return new(new WindowsEnvironment(),service,missingRecovery);
    }

    private static void InstallService()
    {
        string exe=Environment.ProcessPath ?? throw new InvalidOperationException("Application executable path unavailable");
        var service=new WindowsInstallationService(exe);
        if(service.ReadState()!=InstalledServiceState.Absent)
            throw new InvalidOperationException("Previous service was not removed. Retry Setup/Repair before registering a new agent.");
        RunSc("create", "BatteryChargeControlAgent", "binPath=", $"\"{exe}\" --service", "start=", "auto", "DisplayName=", "Battery Charge Control Agent");
        RunSc("description", "BatteryChargeControlAgent", "Restores original battery charging limit at Windows preshutdown and reapplies saved Windows limit on next boot.");
        service.Start(); // No swallowed failures; caller verifies Running with a bounded wait.
    }

    private static void RunSc(params string[] args)
    {
        var info = new ProcessStartInfo("sc.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start sc.exe");
        var output=process.StandardOutput.ReadToEndAsync();
        var error=process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(12000))
        {
            process.Kill(entireProcessTree:true);
            throw new TimeoutException("sc.exe timed out; service state must be verified before retry.");
        }
        if(process.ExitCode!=0) throw new InvalidOperationException("sc.exe failure: "+output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
    }

    private static void SecurePolicyDirectory()
    {
        Directory.CreateDirectory(MachineStore.Folder);
        var info = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(MachineStore.Folder);
        info.ArgumentList.Add("/inheritance:r");
        info.ArgumentList.Add("/grant:r");
        info.ArgumentList.Add("*S-1-5-18:(OI)(CI)F");   // LocalSystem
        info.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)F"); // Administrators
        info.ArgumentList.Add("*S-1-5-32-545:(OI)(CI)RX"); // Users read-only
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot secure policy folder");
        var output=process.StandardOutput.ReadToEndAsync();
        var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(12000))
        {
            process.Kill(entireProcessTree:true);
            throw new TimeoutException("Policy ACL setup timed out; installation remains incomplete.");
        }
        if(process.ExitCode!=0) throw new InvalidOperationException("Cannot set protected policy ACL: "+output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
    }
}
