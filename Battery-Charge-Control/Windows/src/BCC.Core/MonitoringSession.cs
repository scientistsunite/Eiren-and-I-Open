namespace BCC.Core;

// Shared presentation state. Initializing/refreshing it never requests elevation or a write.
public sealed class MonitoringSession
{
    private readonly IChargeEnvironment _host;
    public DeviceInfo Device { get; }
    public IChargingController? Controller { get; }
    public BatterySnapshot Battery { get; private set; } = new(false, null, "Unknown", "Unknown");
    public ControllerProbe? Probe { get; private set; }
    public MachinePolicy? Policy { get; private set; }
    public string? PolicyError { get; private set; }
    public bool OperationInProgress { get; set; }
    public ControlAvailability Availability => Probe?.Availability ?? ControlAvailability.Unsupported;
    private bool BaselineMatchesDevice => Policy?.OriginalLimit is null ||
        (Policy.MachineIdentity == DeviceInfo.Identity(Device) && Policy.BiosVersion == Device.BiosVersion);
    public bool CanRequestLimit => !OperationInProgress && Battery.Present && PolicyError is null &&
        Policy?.MaintenancePending != true &&
        !(Policy is { Enabled: false, RestorationRequired: true }) &&
        BaselineMatchesDevice &&
        Probe is { CurrentLimit: not null, Availability: ControlAvailability.Unverified or ControlAvailability.Available } &&
        Probe.CandidateLimits.Contains(Probe.CurrentLimit.Value);
    public bool CanRestore => !OperationInProgress && Battery.Present && Controller is not null && PolicyError is null &&
        Policy?.OriginalLimit is not null && BaselineMatchesDevice;
    public bool CanResume => CanRestore && CanRequestLimit && Policy?.SelectedLimit is not null &&
        Probe!.CandidateLimits.Contains(Policy.SelectedLimit.Value) && Policy.Enabled == false;

    public MonitoringSession(IChargeEnvironment host)
    {
        _host = host;
        Device = host.ReadDevice();
        Controller = host.LoadController(Device);
        Refresh();
    }

    public BatterySnapshot RefreshBattery() => Battery = _host.ReadBattery();

    public void Refresh()
    {
        RefreshBattery();
        Probe = Controller?.Probe();
        Policy = null;
        PolicyError = null;
        try { Policy = _host.ReadPolicy(); }
        catch (Exception ex) { PolicyError = ex.Message; }
    }
}

public static class CommandAccess
{
    public static bool RequiresAdministrator(string[] args) => args.Length != 0;
}

public enum AgentEvent { Startup, Stop, Shutdown }

// No SCM registration/dispatcher here. Native service synchronization remains in the adapter.
public sealed class AgentLifecycle(IChargeEnvironment host)
{
    public void Handle(AgentEvent notification)
    {
        if (notification == AgentEvent.Stop) return;
        var policy = host.ReadPolicy();
        var operations = new ChargingOperations(host);
        if (notification == AgentEvent.Startup)
        {
            if (policy.MaintenancePending)
            {
                if (policy.Enabled || policy.RestorationRequired) operations.Restore(disablePolicy: true);
            }
            else if (policy.RestorationRequired && !policy.Enabled) operations.Restore(disablePolicy: true);
            else if (policy.Enabled) operations.ApplySaved();
        }
        else if (notification == AgentEvent.Shutdown && (policy.Enabled || policy.RestorationRequired))
            operations.Restore(disablePolicy: false);
    }
}
