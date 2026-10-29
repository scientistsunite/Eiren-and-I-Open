namespace BCC.Core;

public enum InstalledServiceState { Absent, Running, StartPending, StopPending, Stopped, DeletePending, Unknown }
public interface IInstallationService
{
    InstalledServiceState ReadState();
    void Stop();
    void Delete();
    void Start();
    void WaitForTransition();
}

// Shared transaction decisions only: no native service calls, file deletion or registry access.
public sealed class InstallationMaintenance(IChargeEnvironment host, IInstallationService service, bool recoveryInformationMissing=false)
{
    private void AwaitState(InstalledServiceState expected)
    {
        for (int i=0; i<40; i++)
        {
            var state=service.ReadState();
            if (state==expected || (expected==InstalledServiceState.Stopped && state==InstalledServiceState.Absent)) return;
            if (state==InstalledServiceState.Unknown) throw new InvalidOperationException("Service state could not be verified; removal is blocked.");
            service.WaitForTransition();
        }
        throw new TimeoutException("Service transition was not verified; retain recovery files and retry maintenance.");
    }

    public void PrepareRemoval()
    {
        var policy=host.ReadPolicy(); // Unreadable/corrupt policy blocks removal; never guess.
        policy.MaintenancePending=true;
        policy.RestorationRequired |= policy.Enabled || recoveryInformationMissing;
        policy.Enabled=false; // Even a legacy agent must not reapply a saved setting on restart.
        host.WritePolicy(policy); // Durable pause before stopping services or changing firmware.
        bool wasRunning=false;
        try
        {
            var state=service.ReadState();
            if (state==InstalledServiceState.Unknown) throw new InvalidOperationException("Service state unknown; removal blocked.");
            wasRunning=state==InstalledServiceState.Running;
            if (wasRunning) service.Stop();
            if (wasRunning || state==InstalledServiceState.StopPending) AwaitState(InstalledServiceState.Stopped);
            if (state==InstalledServiceState.DeletePending) AwaitState(InstalledServiceState.Absent);
            policy=host.ReadPolicy();
            // Inactive monitoring-only installs never discover a battery or load a controller.
            if (policy.Enabled || policy.RestorationRequired) new ChargingOperations(host).Restore(disablePolicy:true);
            policy=host.ReadPolicy();
            if (policy.Enabled || policy.RestorationRequired || !policy.MaintenancePending)
                throw new InvalidOperationException("Recovery/policy completion was not verified; removal blocked.");
            state=service.ReadState();
            if (state==InstalledServiceState.Stopped) service.Delete();
            else if (state!=InstalledServiceState.Absent && state!=InstalledServiceState.DeletePending)
                throw new InvalidOperationException("Service is not stopped; removal blocked.");
            AwaitState(InstalledServiceState.Absent);
            host.Log("Maintenance preparation verified: automatic charging paused, recovery obligations resolved, service absent. Preserve policy and original configuration for retry/repair.");
        }
        catch (Exception failure)
        {
            // Failed recovery must not silently leave a formerly running recovery service stopped.
            // New agents see MaintenancePending and recover only, never reapply a saved limit.
            if (wasRunning)
            {
                try { if (service.ReadState()==InstalledServiceState.Stopped) service.Start(); }
                catch (Exception restart) { host.Log("WARNING: Recovery service could not restart. Retain installed executable/controller/policy; retry maintenance: "+restart); }
            }
            host.Log("WARNING: Maintenance blocked. Recovery files and original policy must be retained: "+failure);
            throw;
        }
    }

    public void CompleteInstallation(bool agentSelected, Action installAgent)
    {
        if (recoveryInformationMissing) throw new InvalidOperationException("Recovery information is missing from this installation; completion is blocked.");
        var policy=host.ReadPolicy();
        if (policy.Enabled || policy.RestorationRequired)
            throw new InvalidOperationException("Recovery remains unresolved; installation cannot complete.");
        if (agentSelected)
        {
            installAgent();
            AwaitState(InstalledServiceState.Running);
        }
        else AwaitState(InstalledServiceState.Absent);
        policy.MaintenancePending=false;
        host.WritePolicy(policy); // Failure retains the durable pause. Never auto-resume control.
    }
}
