using BCC.Core;
using BCC.Controller.Asus;
using System.Text.Json;

namespace BCC.Simulation;

// The only environment shipped with the harness/preview. No host discovery or fallback.
public sealed class SimulationEnvironment : IChargeEnvironment
{
    public DeviceInfo Device { get; set; } = new("ASUSTeK COMPUTER INC.", "FA607NUQ", "TEST-BIOS", "SIMULATED-x64");
    public BatterySnapshot Battery { get; set; } = BatterySnapshot.Decode(true, 1, 8, 65);
    public SimulatedAsusTransport Transport { get; }
    public List<string> Events { get; } = [];
    public bool ControllerInstalled { get; set; } = true;
    public IChargingController? ControllerOverride { get; set; }
    public bool CorruptPolicy { get; set; }
    public bool FailPolicySave { get; set; }
    public int PolicySaveAttempts { get; private set; }
    public HashSet<int> FailingPolicySaves { get; } = [];
    public HashSet<int> CommitThenFailPolicySaves { get; } = [];
    private MachinePolicy _policy = new();
    public SimulationEnvironment() => Transport = new(Events);
    public DeviceInfo ReadDevice() { Events.Add("device.read"); return Device; }
    public BatterySnapshot ReadBattery() { Events.Add("battery.read"); return Battery; }
    public IChargingController? LoadController(DeviceInfo device)
    {
        Events.Add("controller.load");
        return ControllerOverride ?? (ControllerInstalled && device.Brand == "ASUS" ? new AsusController(device, Transport) : null);
    }
    public MachinePolicy ReadPolicy()
    {
        Events.Add("policy.read");
        if (CorruptPolicy) throw new InvalidDataException("Simulated unreadable policy; no reset attempted.");
        return Copy(_policy);
    }
    public void WritePolicy(MachinePolicy policy)
    {
        Events.Add($"policy.save:{policy.OriginalLimit}:{policy.Enabled}");
        PolicySaveAttempts++;
        if (FailPolicySave || FailingPolicySaves.Contains(PolicySaveAttempts)) throw new IOException("Simulated policy save failure");
        _policy = Copy(policy);
        if (CommitThenFailPolicySaves.Contains(PolicySaveAttempts)) throw new IOException("Simulated failure after policy replacement");
    }
    private static MachinePolicy Copy(MachinePolicy value) =>
        JsonSerializer.Deserialize<MachinePolicy>(JsonSerializer.Serialize(value))!;
    public void Log(string message) => Events.Add("log:" + message);
    public void SecurePolicyDirectory() => Events.Add("simulation.acl.noop");
}

public sealed class SimulatedAsusTransport(List<string> events) : IAsusTransport
{
    public int Raw { get; set; } = 65536 + 100;
    public bool MissingDriver { get; set; }
    public bool MismatchedReadback { get; set; }
    public int Acknowledgment { get; set; } = 1;
    public HashSet<int> FailingLimits { get; } = [];
    public HashSet<int> ThrowAfterWriteLimits { get; } = [];
    public List<int> Writes { get; } = [];
    public Action<int>? BeforeWrite { get; set; }
    public int Read()
    {
        events.Add("simulation.read");
        if (MissingDriver) throw new InvalidOperationException("Simulated missing ASUS System Control Interface driver");
        return Raw;
    }
    public int Write(int percent)
    {
        BeforeWrite?.Invoke(percent);
        events.Add("simulation.write:" + percent);
        Writes.Add(percent);
        if (FailingLimits.Contains(percent)) throw new InvalidOperationException("Simulated rejected operation");
        if (Acknowledgment == 1 && !MismatchedReadback) Raw = 65536 + percent;
        if (ThrowAfterWriteLimits.Contains(percent)) throw new IOException("Simulated transport failure after firmware changed");
        return Acknowledgment;
    }
}

public static class SimulationEntry
{
    // No flags, executable/plugin paths, service commands or external scenario files accepted.
    public static bool Accepts(string[] args) => args.Length == 0;
}
