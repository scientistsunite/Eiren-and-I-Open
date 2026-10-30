using BCC.Core;
using BCC.Simulation;

internal static partial class Program
{
    private static void RunMaintenanceTests()
    {
        Test("monitoring-only maintenance never discovers battery or controller", () => {
            var env=new SimulationEnvironment(); var service=new SimulatedInstallationService();
            new InstallationMaintenance(env,service).PrepareRemoval();
            Check(env.ReadPolicy().MaintenancePending); Equal(0,env.Transport.Writes.Count);
            Check(!env.Events.Any(x=>x is "device.read" or "battery.read" or "controller.load"));
            new InstallationMaintenance(env,service).CompleteInstallation(false,()=>throw new Exception("Agent must not be installed"));
            Check(!env.ReadPolicy().MaintenancePending);
        });
        Test("prepare verifies original and absence before allowing destructive installer phase", () => {
            var env=Saved(); env.Transport.Raw=65536+60; var service=new SimulatedInstallationService { State=InstalledServiceState.Running };
            service.BeforeDelete=()=> { var p=env.ReadPolicy(); Check(p.MaintenancePending&&!p.Enabled&&!p.RestorationRequired); Equal(65536+100,env.Transport.Raw); };
            new InstallationMaintenance(env,service).PrepareRemoval();
            Equal(InstalledServiceState.Absent,service.State); Check(env.ReadPolicy().MaintenancePending);
            Equal(100,env.ReadPolicy().OriginalLimit); Equal(60,env.ReadPolicy().SelectedLimit);
        });
        Test("unverified restoration blocks service deletion and preserves recovery", () => {
            var env=Saved(); env.Transport.Raw=65536+60; env.Transport.FailingLimits.Add(100);
            var service=new SimulatedInstallationService { State=InstalledServiceState.Running };
            Throws<InvalidOperationException>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
            Check(!service.Events.Contains("delete")); Equal(InstalledServiceState.Running,service.State);
            Check(env.ReadPolicy().MaintenancePending&&env.ReadPolicy().RestorationRequired&&!env.ReadPolicy().Enabled); Equal(100,env.ReadPolicy().OriginalLimit);
        });
        Test("unverified readback cannot be interpreted as removal permission", () => {
            var env=Saved(); env.ControllerOverride=new UnverifiedRecoveryController {Current=60,RecoveryReadback=null};
            var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<InvalidOperationException>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
            Check(!service.Events.Contains("delete")); Check(env.ReadPolicy().RestorationRequired&&env.ReadPolicy().MaintenancePending);
        });
        Test("failed restore policy save blocks deletion even after firmware readback matched", () => {
            var env=Saved(); env.Transport.Raw=65536+60; env.FailingPolicySaves.Add(env.PolicySaveAttempts+2);
            var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<IOException>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
            Equal(65536+100,env.Transport.Raw); Check(!service.Events.Contains("delete")); Check(env.ReadPolicy().RestorationRequired&&env.ReadPolicy().MaintenancePending);
        });
        Test("failed pause persistence touches neither firmware nor service", () => {
            var env=Saved(); env.FailPolicySave=true; var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<IOException>(()=>new InstallationMaintenance(env,service).PrepareRemoval()); Equal(0,service.Events.Count); Equal(0,env.Transport.Writes.Count);
        });
        Test("pause save that commits then throws still retains recovery duty without service changes",()=>{
            var env=Saved();env.CommitThenFailPolicySaves.Add(env.PolicySaveAttempts+1);
            var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<IOException>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
            var p=env.ReadPolicy();Check(p.MaintenancePending&&p.RestorationRequired&&!p.Enabled);Equal(0,service.Events.Count);Equal(0,env.Transport.Writes.Count);
        });
        Test("completion save that commits then throws reports failure only after safe state is verified",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval();env.CommitThenFailPolicySaves.Add(env.PolicySaveAttempts+1);
            Throws<IOException>(()=>maintenance.CompleteInstallation(false,()=>{}));
            Check(!env.ReadPolicy().Enabled&&!env.ReadPolicy().RestorationRequired);Equal(InstalledServiceState.Absent,service.State);Equal(0,env.Transport.Writes.Count);
        });
        Test("corrupt policy blocks cleanup before service removal", () => {
            var env=Saved(); env.CorruptPolicy=true; var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<InvalidDataException>(()=>new InstallationMaintenance(env,service).PrepareRemoval()); Equal(0,service.Events.Count);
        });
        Test("missing original with active duty never permits removal", () => {
            var env=new SimulationEnvironment(); env.WritePolicy(new MachinePolicy {RestorationRequired=true});
            var service=new SimulatedInstallationService();
            Throws<InvalidOperationException>(()=>new InstallationMaintenance(env,service).PrepareRemoval()); Check(!service.Events.Contains("delete")); Equal(0,env.Transport.Writes.Count);
        });
        Test("missing recovery file on an existing installation cannot masquerade as monitoring-only",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            var maintenance=new InstallationMaintenance(env,service,recoveryInformationMissing:true);
            Throws<InvalidOperationException>(maintenance.PrepareRemoval);
            var p=env.ReadPolicy();Check(p.MaintenancePending&&p.RestorationRequired&&p.OriginalLimit is null);
            Check(!service.Events.Contains("delete"));Equal(0,env.Transport.Writes.Count);
            Throws<InvalidOperationException>(maintenance.PrepareRemoval); // Persisted uncertainty survives retry.
            Throws<InvalidOperationException>(()=>maintenance.CompleteInstallation(false,()=>{}));
        });
        foreach(string fault in new[]{"stop", "stop timeout", "delete", "delete timeout", "unknown", "foreign service"})
        {
            var name=fault;
            Test("service removal failure remains a blocking maintenance error: "+name,()=>{
                var env=Saved(); var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
                service.ThrowStop=name=="stop"; service.NeverStop=name=="stop timeout";
                service.ThrowDelete=name=="delete"; service.NeverDelete=name=="delete timeout";
                if(name=="unknown")service.State=InstalledServiceState.Unknown;
                if(name=="foreign service")service.ThrowRead=true;
                Throws<Exception>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
                Check(env.ReadPolicy().MaintenancePending); Equal(100,env.ReadPolicy().OriginalLimit);
                if(name is "stop" or "stop timeout" or "unknown" or "foreign service")Check(!service.Events.Contains("delete"));
            });
        }
        Test("restart failure after recovery failure is explicit and does not delete service",()=>{
            var env=Saved(); env.Transport.Raw=65536+60; env.Transport.FailingLimits.Add(100);
            var service=new SimulatedInstallationService {State=InstalledServiceState.Running,ThrowStart=true};
            Throws<InvalidOperationException>(()=>new InstallationMaintenance(env,service).PrepareRemoval());
            Check(env.Events.Any(x=>x.Contains("Recovery service could not restart"))); Check(!service.Events.Contains("delete"));
        });
        Test("retry after restoration failure resolves original before removal",()=>{
            var env=Saved(); env.Transport.Raw=65536+60; env.Transport.FailingLimits.Add(100);
            var service=new SimulatedInstallationService {State=InstalledServiceState.Running}; var maintenance=new InstallationMaintenance(env,service);
            Throws<InvalidOperationException>(maintenance.PrepareRemoval); env.Transport.FailingLimits.Clear(); maintenance.PrepareRemoval();
            Equal(InstalledServiceState.Absent,service.State); Check(!env.ReadPolicy().Enabled&&!env.ReadPolicy().RestorationRequired);
            Equal(65536+100,env.Transport.Raw);
        });
        Test("cancel/interruption after preparation leaves charging paused and recovery records intact",()=>{
            var env=Saved(); var service=new SimulatedInstallationService(); new InstallationMaintenance(env,service).PrepareRemoval();
            var p=env.ReadPolicy(); Check(p.MaintenancePending&&!p.Enabled); Equal(100,p.OriginalLimit); Equal(60,p.SelectedLimit);
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Resume());
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).SetNewLimit(80));
            Check(!new MonitoringSession(env).CanRequestLimit);
        });
        Test("startup during interrupted maintenance restores instead of reapplying saved preference",()=>{
            var env=Saved(); var p=env.ReadPolicy();p.MaintenancePending=true;env.WritePolicy(p);env.Transport.Raw=65536+60;
            new AgentLifecycle(env).Handle(AgentEvent.Startup);
            Equal(65536+100,env.Transport.Raw);Check(!env.ReadPolicy().Enabled&&env.ReadPolicy().MaintenancePending);
        });
        Test("retry after interruption is idempotent and repair does not auto-resume charging",()=>{
            var env=Saved(); var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval(); maintenance.PrepareRemoval(); maintenance.CompleteInstallation(false,()=>throw new Exception("Unexpected agent"));
            Check(!env.ReadPolicy().MaintenancePending&&!env.ReadPolicy().Enabled); Equal(60,env.ReadPolicy().SelectedLimit);
        });
        Test("monitoring-only completion refuses a leftover service",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService {State=InstalledServiceState.Running};
            Throws<TimeoutException>(()=>new InstallationMaintenance(env,service).CompleteInstallation(false,()=>{}));
        });
        Test("optional agent installation failure is propagated and pause retained",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval();Throws<InvalidOperationException>(()=>maintenance.CompleteInstallation(true,()=>throw new InvalidOperationException("Simulated registration failure")));
            Check(env.ReadPolicy().MaintenancePending);
        });
        Test("service start must be observed before marking installation complete",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval();Throws<TimeoutException>(()=>maintenance.CompleteInstallation(true,()=>{})); Check(env.ReadPolicy().MaintenancePending);
            maintenance.CompleteInstallation(true,()=>service.State=InstalledServiceState.Running);Check(!env.ReadPolicy().MaintenancePending);
        });
        Test("start-pending is not successful installation until running is observed",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval();
            Throws<TimeoutException>(()=>maintenance.CompleteInstallation(true,()=>service.State=InstalledServiceState.StartPending));
            Check(env.ReadPolicy().MaintenancePending);
            service.OnWait=()=>service.State=InstalledServiceState.Running;
            maintenance.CompleteInstallation(true,()=>{});Check(!env.ReadPolicy().MaintenancePending);
        });
        Test("final installation save failure retains pause for recovery retry",()=>{
            var env=new SimulationEnvironment();var service=new SimulatedInstallationService();var maintenance=new InstallationMaintenance(env,service);
            maintenance.PrepareRemoval();env.FailPolicySave=true;
            Throws<IOException>(()=>maintenance.CompleteInstallation(false,()=>{}));Check(env.ReadPolicy().MaintenancePending);
            env.FailPolicySave=false;maintenance.CompleteInstallation(false,()=>{});Check(!env.ReadPolicy().MaintenancePending);
        });
        Test("unresolved active control cannot be finalized as a monitoring-only installation",()=>{
            var env=Saved();var service=new SimulatedInstallationService();
            Throws<InvalidOperationException>(()=>new InstallationMaintenance(env,service).CompleteInstallation(false,()=>{}));
        });
    }
}

internal sealed class SimulatedInstallationService : IInstallationService
{
    public InstalledServiceState State {get;set;}=InstalledServiceState.Absent;
    public List<string> Events {get;}=[];
    public bool ThrowStop, ThrowDelete, ThrowStart, ThrowRead, NeverStop, NeverDelete;
    public Action? BeforeDelete;
    public Action? OnWait;
    public InstalledServiceState ReadState() {Events.Add("query");if(ThrowRead)throw new InvalidOperationException("Simulated foreign/conflicting service");return State;}
    public void Stop() {Events.Add("stop");if(ThrowStop)throw new InvalidOperationException("Simulated stop failure");State=NeverStop?InstalledServiceState.StopPending:InstalledServiceState.Stopped;}
    public void Delete() {BeforeDelete?.Invoke();Events.Add("delete");if(ThrowDelete)throw new InvalidOperationException("Simulated removal failure");State=NeverDelete?InstalledServiceState.DeletePending:InstalledServiceState.Absent;}
    public void Start() {Events.Add("start");if(ThrowStart)throw new InvalidOperationException("Simulated restart failure");State=InstalledServiceState.Running;}
    public void WaitForTransition() {Events.Add("wait");OnWait?.Invoke();}
}
