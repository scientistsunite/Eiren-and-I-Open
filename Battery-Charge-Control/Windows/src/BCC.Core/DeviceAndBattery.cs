using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Text.Json;

namespace BCC.Core;

public sealed partial record DeviceInfo
{
    public static DeviceInfo Detect()
    {
        static string Read(string name) => (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", name, "")?.ToString() ?? "").Trim();
        return new DeviceInfo(Read("SystemManufacturer"), Read("SystemProductName"),
            Read("BIOSVersion"), RuntimeInformation.OSArchitecture.ToString());
    }

}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

public sealed partial record BatterySnapshot
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static BatterySnapshot Read()
    {
        bool success = GetSystemPowerStatus(out var status);
        return Decode(success, status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent);
    }
}

public static class MachineStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Battery Charge Control");
    public static string PolicyFile => Path.Combine(Folder, "policy.json");
    public static string LogFile => Path.Combine(Folder, "agent.log");
    public static string Identity(DeviceInfo d) => DeviceInfo.Identity(d);
    public static MachinePolicy Read()
    {
        if (!File.Exists(PolicyFile)) return new MachinePolicy();
        try { return JsonSerializer.Deserialize<MachinePolicy>(File.ReadAllText(PolicyFile)) ?? new MachinePolicy(); }
        catch { throw new InvalidDataException("Machine policy unreadable: charging control paused. Repair or inspect the policy; no automatic reset was attempted."); }
    }
    public static void Write(MachinePolicy policy)
    {
        Directory.CreateDirectory(Folder);
        policy.LastChangedUtc = DateTimeOffset.UtcNow;
        var tmp = PolicyFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, PolicyFile, true);
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(LogFile, $"{DateTimeOffset.Now:u} | {message}{Environment.NewLine}");
        }
        catch { /* Logging must not cause new hardware writes. */ }
    }
}

public static class ControllerRegistry
{
    public static IChargingController? Load(DeviceInfo device, string executableDirectory)
    {
        // All 15 recognized manufacturers use the same plug-in convention;
        // v0.1 deliberately includes only a genuine ASUS implementation.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASUS", "Dell", "Lenovo", "HP", "MSI", "Acer", "Samsung", "Microsoft", "LG",
            "Huawei", "Xiaomi", "Razer", "Gigabyte", "Framework", "Dynabook"
        };
        if (!known.Contains(device.Brand)) return null;
        string brand = device.Brand;
        string dll = Path.Combine(executableDirectory, "Controllers", brand, $"BCC.Controller.{brand}.dll");
        if (!File.Exists(dll)) return null;
        try
        {
            var assembly = System.Reflection.Assembly.LoadFrom(dll);
            var type = assembly.GetType($"BCC.Controller.{brand}.{brand}Controller", throwOnError: true)!;
            return (IChargingController)Activator.CreateInstance(type, device)!;
        }
        catch (Exception ex)
        {
            MachineStore.Log("Cannot load controller for " + brand + ": " + ex.Message);
            return null;
        }
    }
}
