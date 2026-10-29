namespace BCC.Core;

// Explicit dependencies: no default platform factory, native API, disk or process access.
public interface IChargeEnvironment
{
    DeviceInfo ReadDevice();
    BatterySnapshot ReadBattery();
    IChargingController? LoadController(DeviceInfo device);
    MachinePolicy ReadPolicy();
    void WritePolicy(MachinePolicy policy);
    void Log(string message);
    void SecurePolicyDirectory();
}

public sealed class ChargingOperations(IChargeEnvironment host)
{
    private readonly IChargeEnvironment _host = host;
    private IChargingController GetController(DeviceInfo device) => _host.LoadController(device)
        ?? throw new NotSupportedException("No installed charging controller for this device. Monitoring remains available.");

    // Write-ahead recovery obligation, independent of the automatic reapplication preference.
    // Failure here must abort before any firmware write. Already-armed recovery can run even
    // when storage is currently unwritable; never require another save just to attempt restore.
    private void ArmRestoration(MachinePolicy policy)
    {
        if (policy.RestorationRequired) return;
        policy.RestorationRequired = true;
        _host.WritePolicy(policy);
    }

    private static void VerifyOriginal(IChargingController controller, int original)
    {
        if (controller.ReadLimit() != original)
            throw new InvalidOperationException("Original-setting readback could not be verified. Restoration remains required; physical charging behavior is unknown.");
    }

    private void RecoverOriginal(IChargingController controller, int original)
    {
        try
        {
            if (controller.ReadLimit() != original) controller.SetLimit(original);
            VerifyOriginal(controller, original);
            _host.Log("Recovery original-setting readback verified. Durable restoration obligation retained until Restore completes and saves policy; physical charging cutoff remains unverified.");
        }
        catch (Exception recovery)
        {
            _host.Log("WARNING: Rollback failed or original-setting readback was unverified. Restoration remains required: " + recovery);
        }
        // Do not overwrite the last durable preference with a failed operation's in-memory
        // changes. Even verified recovery conservatively retains the write-ahead marker.
    }

    public void SetNewLimit(int requested)
    {
        _host.SecurePolicyDirectory();
        var device = _host.ReadDevice();
        if (!_host.ReadBattery().Present) throw new InvalidOperationException("No battery detected");
        var controller = GetController(device);
        var probe = controller.Probe();
        if (!probe.CandidateLimits.Contains(requested)) throw new NotSupportedException("Unsupported candidate limit for this device");
        if (probe.CurrentLimit is null) throw new InvalidOperationException("Original setting unreadable; hardware write blocked");
        var policy = _host.ReadPolicy();
        if (policy.MaintenancePending) throw new InvalidOperationException("Installation maintenance is pending. Complete repair before changing charging settings.");
        string identity = DeviceInfo.Identity(device);
        if (policy.OriginalLimit is null && (policy.Enabled || policy.RestorationRequired))
            throw new InvalidOperationException("Original setting is missing from an active/recovery policy; no new baseline may be assumed.");
        if (policy.OriginalLimit is not null && policy.MachineIdentity != identity)
            throw new InvalidOperationException("Saved policy belongs to a different computer. Clear it using the supported recovery procedure.");
        if (policy.OriginalLimit is not null && policy.BiosVersion != device.BiosVersion)
            throw new InvalidOperationException("BIOS version changed. Recheck the original setting before requesting charging changes.");
        int baseline = policy.OriginalLimit ?? probe.CurrentLimit.Value;
        if (!probe.CandidateLimits.Contains(baseline))
            throw new InvalidOperationException("Original limit is outside verified restorable values. Hardware write blocked.");
        if (policy.OriginalLimit is null)
        {
            policy.OriginalLimit = baseline;
            policy.OriginalSource = "ASUS WMI DSTS readback before first change";
            policy.MachineIdentity = identity;
            policy.Brand = device.Brand;
            policy.BiosVersion = device.BiosVersion;
            policy.Enabled = false;
        }
        ArmRestoration(policy); // Baseline AND recovery obligation durable before first write.
        try
        {
            controller.SetLimit(requested);
            policy.SelectedLimit = requested;
            policy.Enabled = true;
            _host.WritePolicy(policy);
            _host.Log($"Applied candidate limit {requested}%; firmware readback matched. Actual physical charging cutoff not independently confirmed.");
        }
        catch
        {
            RecoverOriginal(controller, baseline);
            throw;
        }
    }

    public void Restore(bool disablePolicy)
    {
        var policy = _host.ReadPolicy();
        if (policy.OriginalLimit is null)
        {
            if (policy.Enabled || policy.RestorationRequired)
                throw new InvalidOperationException("Original setting is unknown; restoration cannot be verified. Recovery obligation was not cleared.");
            if (disablePolicy) { policy.Enabled = false; _host.WritePolicy(policy); }
            return;
        }
        var device = _host.ReadDevice();
        if (DeviceInfo.Identity(device) != policy.MachineIdentity)
            throw new InvalidOperationException("Hardware identity has changed. No reset attempted.");
        if (policy.BiosVersion != device.BiosVersion)
            throw new InvalidOperationException("BIOS version changed. Original-setting restoration is paused.");
        if (!_host.ReadBattery().Present) throw new InvalidOperationException("No battery detected");
        var controller = GetController(device);
        // An already-durable Enabled preference protects legacy policies too. Do not let
        // an unavailable policy disk prevent shutdown restoration of such a policy.
        if (!policy.Enabled) ArmRestoration(policy);
        int? current = controller.ReadLimit();
        if (current is null) throw new InvalidOperationException("Cannot read firmware limit; restore cannot be verified.");
        if (current != policy.OriginalLimit) controller.SetLimit(policy.OriginalLimit.Value);
        VerifyOriginal(controller, policy.OriginalLimit.Value);
        policy.RestorationRequired = false;
        if (disablePolicy) policy.Enabled = false;
        _host.WritePolicy(policy); // Failure leaves durable Enabled/RestorationRequired intact.
        _host.Log($"Original charging limit {policy.OriginalLimit}% restored/read back; policy enabled={policy.Enabled}");
    }

    public void Resume() => ApplySaved(_host.ReadPolicy(), enablePreference: true);

    public void ApplySaved() => ApplySaved(_host.ReadPolicy(), enablePreference: false);

    private void ApplySaved(MachinePolicy policy, bool enablePreference)
    {
        if (policy.MaintenancePending) throw new InvalidOperationException("Maintenance is pending; charging reapplication is blocked.");
        if (policy.SelectedLimit is null || policy.SelectedLimit.Value < 20 || policy.SelectedLimit.Value > 100 || policy.OriginalLimit is null)
            throw new InvalidOperationException("No saved limit and recoverable baseline to resume");
        var device = _host.ReadDevice();
        if (DeviceInfo.Identity(device) != policy.MachineIdentity ||
            !string.Equals(device.BiosVersion, policy.BiosVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Startup reapplication paused: hardware or BIOS version changed. Recheck compatibility.");
        var controller = GetController(device);
        if (!_host.ReadBattery().Present) throw new InvalidOperationException("No battery detected");
        int? current = controller.ReadLimit();
        if (current is null) throw new InvalidOperationException("Current setting unreadable; startup reapplication blocked.");
        ArmRestoration(policy);
        try
        {
            if (current != policy.SelectedLimit.Value) controller.SetLimit(policy.SelectedLimit.Value);
            if (enablePreference) policy.Enabled = true;
            _host.WritePolicy(policy);
            _host.Log("Startup/resume charging limit request verified by firmware readback; physical cutoff not proven.");
        }
        catch
        {
            RecoverOriginal(controller, policy.OriginalLimit.Value);
            throw;
        }
    }
}
