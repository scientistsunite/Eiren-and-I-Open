using BCC.Core;
using BCC.Controller.Asus;
using BCC.Simulation;

internal static partial class Program
{
    private static int _passed, _failed;
    private static void Check(bool value, string why = "Assertion failed")
    { if (!value) throw new Exception(why); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Test(string name, Action action)
    {
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static SimulationEnvironment Saved(bool enabled = true)
    {
        var env = new SimulationEnvironment();
        env.WritePolicy(new MachinePolicy { OriginalLimit = 100, SelectedLimit = 60, Enabled = enabled,
            MachineIdentity = DeviceInfo.Identity(env.Device), BiosVersion = env.Device.BiosVersion });
        env.Events.Clear();
        return env;
    }
    private static void RunPersistenceTests()
    {
        foreach (string operation in new[] { "set", "resume", "saved" })
        {
            string name=operation;
            Test(name + ": every request has a durable original and restoration duty before it runs", () => {
                var env=Saved(false);
                env.Transport.BeforeWrite = _ => {
                    var persisted=env.ReadPolicy(); Check(persisted.RestorationRequired); Equal(100,persisted.OriginalLimit);
                };
                var ops=new ChargingOperations(env);
                if(name=="set") ops.SetNewLimit(60); else if(name=="resume") ops.Resume(); else ops.ApplySaved();
                Check(env.Transport.Writes.Count>0);
            });
            Test(name + ": write-ahead policy failure prevents firmware request", () => {
                var env=Saved(false); env.FailPolicySave=true;
                var ops=new ChargingOperations(env);
                Throws<IOException>(() => { if(name=="set") ops.SetNewLimit(60); else if(name=="resume") ops.Resume(); else ops.ApplySaved(); });
                Equal(0,env.Transport.Writes.Count); Check(!env.ReadPolicy().RestorationRequired); Equal(65536+100,env.Transport.Raw);
            });
            Test(name + ": post-change save failure retains disabled restoration obligation", () => {
                var env=Saved(false); env.FailingPolicySaves.Add(env.PolicySaveAttempts+2);
                var ops=new ChargingOperations(env);
                Throws<IOException>(() => { if(name=="set") ops.SetNewLimit(60); else if(name=="resume") ops.Resume(); else ops.ApplySaved(); });
                var durable=env.ReadPolicy(); Check(!durable.Enabled && durable.RestorationRequired);
                Equal(100,durable.OriginalLimit); Equal(65536+100,env.Transport.Raw);
                Check(env.Events.Any(x=>x.Contains("Recovery original-setting readback verified")));
                Check(!new MonitoringSession(env).CanResume);
            });
            Test(name + ": failed rollback cannot suppress shutdown restore even with unwritable policy", () => {
                var env=Saved(false); env.FailingPolicySaves.Add(env.PolicySaveAttempts+2); env.Transport.FailingLimits.Add(100);
                var ops=new ChargingOperations(env);
                Throws<IOException>(() => { if(name=="set") ops.SetNewLimit(60); else if(name=="resume") ops.Resume(); else ops.ApplySaved(); });
                Check(!env.ReadPolicy().Enabled && env.ReadPolicy().RestorationRequired); Equal(65536+60,env.Transport.Raw);
                Check(env.Events.Any(x=>x.Contains("WARNING: Rollback failed")));
                env.Transport.FailingLimits.Clear(); env.FailPolicySave=true;
                Throws<IOException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Shutdown));
                Equal(65536+100,env.Transport.Raw); Check(env.ReadPolicy().RestorationRequired);
                env.FailPolicySave=false;
                new AgentLifecycle(env).Handle(AgentEvent.Shutdown);
                Check(!env.ReadPolicy().Enabled && !env.ReadPolicy().RestorationRequired);
            });
        }
        Test("first-ever change saves baseline and restoration duty before firmware, even when final save and rollback fail", () => {
            var env=new SimulationEnvironment(); env.FailingPolicySaves.Add(2); env.Transport.FailingLimits.Add(100);
            Throws<IOException>(()=>new ChargingOperations(env).SetNewLimit(60));
            var p=env.ReadPolicy(); Check(p.RestorationRequired && !p.Enabled); Equal(100,p.OriginalLimit); Check(p.SelectedLimit is null);
            Equal(65536+60,env.Transport.Raw);
            env.Transport.FailingLimits.Clear();
            new AgentLifecycle(env).Handle(AgentEvent.Startup); // Restart: recover, never reapply an uncommitted preference.
            Equal(65536+100,env.Transport.Raw); Check(!env.ReadPolicy().Enabled && !env.ReadPolicy().RestorationRequired);
        });
        Test("write-ahead save that commits then throws aborts without firmware and remains recoverable", () => {
            var env=Saved(false); env.CommitThenFailPolicySaves.Add(env.PolicySaveAttempts+1);
            Throws<IOException>(()=>new ChargingOperations(env).Resume());
            Equal(0,env.Transport.Writes.Count); Check(env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
            new AgentLifecycle(env).Handle(AgentEvent.Startup);
            Check(!env.ReadPolicy().RestorationRequired); Equal(0,env.Transport.Writes.Count);
        });
        Test("disabled pending recovery retries at startup and remains pending after unsuccessful attempt", () => {
            var env=Saved(false); var p=env.ReadPolicy(); p.RestorationRequired=true; env.WritePolicy(p);
            env.Transport.Raw=65536+60; env.Transport.FailingLimits.Add(100);
            Throws<InvalidOperationException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Startup));
            Check(env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
            env.Transport.FailingLimits.Clear(); new AgentLifecycle(env).Handle(AgentEvent.Shutdown);
            Equal(65536+100,env.Transport.Raw); Check(!env.ReadPolicy().RestorationRequired);
        });
        foreach (bool enabled in new[] { false, true })
        {
            bool initial=enabled;
            Test("restore succeeds but clearing policy fails; durable scheduling survives, enabled="+initial, () => {
                var env=Saved(initial); var p=env.ReadPolicy(); p.RestorationRequired=true; env.WritePolicy(p);
                env.Transport.Raw=65536+60; env.FailPolicySave=true;
                Throws<IOException>(()=>new ChargingOperations(env).Restore(true));
                Equal(65536+100,env.Transport.Raw); Check(env.ReadPolicy().RestorationRequired); Equal(initial,env.ReadPolicy().Enabled);
                Check(!env.Events.Any(x=>x.StartsWith("log:Original charging limit")));
                env.FailPolicySave=false; new ChargingOperations(env).Restore(true);
                Check(!env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
            });
        }
        Test("legacy enabled policy still attempts shutdown restoration on unwritable disk", () => {
            var env=Saved(); Check(!env.ReadPolicy().RestorationRequired); env.Transport.Raw=65536+60; env.FailPolicySave=true;
            Throws<IOException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Shutdown));
            Equal(65536+100,env.Transport.Raw); Check(env.ReadPolicy().Enabled);
        });
        foreach (int? readback in new int?[] { null, 80 })
        {
            int? value=readback;
            Test("restore returning without verified readback cannot clear pending state: "+value, () => {
                var env=Saved(false); var p=env.ReadPolicy(); p.RestorationRequired=true; env.WritePolicy(p);
                var controller=new UnverifiedRecoveryController { Current=60, RecoveryReadback=value }; env.ControllerOverride=controller;
                Throws<InvalidOperationException>(()=>new ChargingOperations(env).Restore(true));
                Check(env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
                Check(!env.Events.Any(x=>x.StartsWith("log:Original charging limit")));
            });
            Test("policy-save failure plus unverified recovery must not log restoration success: "+value, () => {
                var env=Saved(false); env.FailingPolicySaves.Add(env.PolicySaveAttempts+2);
                var controller=new UnverifiedRecoveryController { Current=100, RecoveryReadback=value }; env.ControllerOverride=controller;
                Throws<IOException>(()=>new ChargingOperations(env).Resume());
                Check(env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
                Check(env.Events.Any(x=>x.Contains("WARNING: Rollback failed")));
                Check(!env.Events.Any(x=>x.Contains("Recovery original-setting readback verified")));
                controller.Current=60; controller.RecoveryReadback=100;
                new AgentLifecycle(env).Handle(AgentEvent.Shutdown);
                Equal(100,controller.Current); Check(!env.ReadPolicy().RestorationRequired);
            });
        }
        Test("ambiguous final resume save failure after replacement still preserves restore scheduling", () => {
            var env=Saved(false); env.CommitThenFailPolicySaves.Add(env.PolicySaveAttempts+2); env.Transport.FailingLimits.Add(100);
            Throws<IOException>(()=>new ChargingOperations(env).Resume());
            Check(env.ReadPolicy().RestorationRequired && env.ReadPolicy().Enabled); Equal(65536+60,env.Transport.Raw);
            env.Transport.FailingLimits.Clear(); new AgentLifecycle(env).Handle(AgentEvent.Shutdown);
            Equal(65536+100,env.Transport.Raw);
        });
        Test("clearing save that commits then throws is safe only after original readback verified", () => {
            var env=Saved(false); var p=env.ReadPolicy(); p.RestorationRequired=true; env.WritePolicy(p); env.Transport.Raw=65536+60;
            env.CommitThenFailPolicySaves.Add(env.PolicySaveAttempts+1);
            Throws<IOException>(()=>new ChargingOperations(env).Restore(true));
            Equal(65536+100,env.Transport.Raw); Check(!env.ReadPolicy().RestorationRequired && !env.ReadPolicy().Enabled);
        });
        Test("pending obligation with missing original is never cleared or replaced by a guessed baseline", () => {
            var env=new SimulationEnvironment(); env.WritePolicy(new MachinePolicy { RestorationRequired=true });
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Restore(true));
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).SetNewLimit(60));
            Check(env.ReadPolicy().RestorationRequired && env.ReadPolicy().OriginalLimit is null); Equal(0,env.Transport.Writes.Count);
        });
        Test("manual service stop leaves pending recovery scheduled without hardware access", () => {
            var env=Saved(false); var p=env.ReadPolicy(); p.RestorationRequired=true; env.WritePolicy(p); env.Events.Clear();
            new AgentLifecycle(env).Handle(AgentEvent.Stop); Equal(0,env.Events.Count);
        });
    }

    private static int Main(string[] args)
    {
        if (!SimulationEntry.Accepts(args)) { Console.Error.WriteLine("Simulation takes no arguments; all commands rejected."); return 2; }
        foreach (var pair in new (string maker, string brand)[] {
            ("ASUSTeK COMPUTER INC.", "ASUS"), ("asus", "ASUS"), ("Dell Inc.", "Dell"), ("LENOVO", "Lenovo"),
            ("Micro-Star International", "MSI"), ("MSI", "MSI"), ("Hewlett-Packard", "HP"), ("HP", "HP"),
            ("Acer", "Acer"), ("Samsung", "Samsung"), ("Microsoft", "Microsoft"), ("LG", "LG"),
            ("Huawei", "Huawei"), ("Redmi", "Xiaomi"), ("Xiaomi", "Xiaomi"), ("Razer", "Razer"),
            ("Gigabyte", "Gigabyte"), ("Framework", "Framework"), ("Toshiba", "Dynabook"), ("Dynabook", "Dynabook"),
            ("Unsupported Example", "Unsupported Example"), ("", "Unknown") })
        {
            var sample = pair;
            Test("manufacturer: " + sample.maker, () => Equal(sample.brand, new DeviceInfo(sample.maker, "test", "test", "sim").Brand));
        }
        foreach (string maker in new[] { "Dell Inc.", "LENOVO", "MSI", "Unsupported Example", "" })
        {
            string sample = maker;
            Test("monitor-only initialization: " + sample, () => {
                var env = new SimulationEnvironment { Device = new(sample, "test", "test", "sim") };
                var session = new MonitoringSession(env);
                Equal(ControlAvailability.Unsupported, session.Availability);
                Check(!session.CanRequestLimit && !session.CanRestore && !session.CanResume);
                Equal(65, session.Battery.Percent); Equal(0, env.Transport.Writes.Count);
                Check(!env.Events.Contains("simulation.read"));
                Check(!env.Events.Any(x => x.StartsWith("policy.save") || x.Contains("acl")));
            });
        }
        Test("ASUS initialization never writes and candidates remain unverified", () => {
            var env = new SimulationEnvironment(); var session = new MonitoringSession(env);
            Equal(ControlAvailability.Unverified, session.Availability); Check(session.CanRequestLimit);
            Check(session.Probe!.CandidateLimits.SequenceEqual(Enumerable.Range(0, 15).Select(i => 30 + 5*i)));
            Check(!session.Probe.CandidateLimitsVerified); Equal(0, env.Transport.Writes.Count);
            Check(!env.Events.Any(x => x.StartsWith("policy.save") || x.Contains("acl")));
        });
        Test("non-FA607 ASUS presets", () => {
            var env = new SimulationEnvironment { Device = new("ASUS", "OtherModel", "test", "sim") };
            Check(new MonitoringSession(env).Probe!.CandidateLimits.SequenceEqual(new[] {60, 80, 100}));
        });
        Test("battery refresh consumes injected values only", () => {
            var env = new SimulationEnvironment(); var session = new MonitoringSession(env);
            env.Battery = BatterySnapshot.Decode(true, 0, 0, 24); session.RefreshBattery();
            Equal(24, session.Battery.Percent); Equal("On battery", session.Battery.Power);
            Equal(0, env.Transport.Writes.Count);
        });
        Test("desktop: no battery disables controls and blocks changes", () => {
            var env = new SimulationEnvironment { Battery = BatterySnapshot.Decode(true, 1, 128, 255) };
            var session = new MonitoringSession(env); Check(!session.Battery.Present && !session.CanRequestLimit);
            Throws<InvalidOperationException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
        });
        Test("unknown battery percentage and AC remain unknown", () => {
            var b = BatterySnapshot.Decode(true, 255, 0, 255); Check(b.Present); Check(b.Percent is null); Equal("Unknown AC state", b.Power);
        });
        Test("unavailable battery API does not invent telemetry", () => {
            var b = BatterySnapshot.Decode(false, 1, 8, 65); Check(!b.Present && b.Percent is null); Equal("Unknown", b.Power);
        });
        Test("missing ASUS plugin", () => {
            var env = new SimulationEnvironment { ControllerInstalled = false }; var s = new MonitoringSession(env);
            Equal(ControlAvailability.Unsupported, s.Availability); Check(!s.CanRequestLimit);
            Throws<NotSupportedException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
        });
        Test("missing ASUS driver", () => {
            var env = new SimulationEnvironment(); env.Transport.MissingDriver = true; var s = new MonitoringSession(env);
            Equal(ControlAvailability.TemporarilyInaccessible, s.Availability); Check(!s.CanRequestLimit);
            Check(s.Probe!.Description.Contains("official ASUS support")); Equal(0, env.Transport.Writes.Count);
        });
        foreach (int raw in new[] { 0, 1, 65536, 65555, 65637, int.MaxValue })
        {
            int value = raw;
            Test("unrecognized original blocks changes: " + value, () => {
                var env = new SimulationEnvironment(); env.Transport.Raw = value;
                Check(!new MonitoringSession(env).CanRequestLimit);
                Throws<NotSupportedException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
            });
        }
        Test("original outside restorable model candidates blocks change", () => {
            var env = new SimulationEnvironment { Device = new("ASUS", "OtherModel", "test", "sim") }; env.Transport.Raw = 65536 + 55;
            Throws<InvalidOperationException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
        });
        Test("baseline is persisted before first simulated change", () => {
            var env = new SimulationEnvironment(); new ChargingOperations(env).SetNewLimit(60);
            var p = env.ReadPolicy(); Equal(100, p.OriginalLimit); Equal(60, p.SelectedLimit); Check(p.Enabled);
            Check(env.Events.IndexOf("policy.save:100:False") < env.Events.IndexOf("simulation.write:60"));
            Check(env.Transport.Writes.SequenceEqual(new[] {60}));
        });
        Test("existing baseline is preserved", () => {
            var env = Saved(); env.Transport.Raw = 65536+60; new ChargingOperations(env).SetNewLimit(80);
            Equal(100, env.ReadPolicy().OriginalLimit);
        });
        Test("failed change rolls back but does not claim success", () => {
            var env = new SimulationEnvironment(); env.Transport.FailingLimits.Add(60);
            Throws<InvalidOperationException>(() => new ChargingOperations(env).SetNewLimit(60));
            Check(env.Transport.Writes.SequenceEqual(new[] {60})); Check(!env.ReadPolicy().Enabled);
            Equal(100, env.ReadPolicy().OriginalLimit); Check(env.ReadPolicy().SelectedLimit is null);
        });
        Test("rollback failure is visible and baseline retained", () => {
            var env = new SimulationEnvironment(); env.Transport.ThrowAfterWriteLimits.Add(60); env.Transport.FailingLimits.Add(100);
            Throws<IOException>(() => new ChargingOperations(env).SetNewLimit(60));
            Check(env.Events.Any(x => x.Contains("WARNING: Rollback failed"))); Equal(100, env.ReadPolicy().OriginalLimit);
        });
        Test("baseline save failure prevents any request", () => {
            var env = new SimulationEnvironment { FailPolicySave = true };
            Throws<IOException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
        });
        Test("corrupt policy preserves monitoring and blocks writes", () => {
            var env = new SimulationEnvironment { CorruptPolicy = true }; var s = new MonitoringSession(env);
            Check(s.PolicyError is not null && !s.CanRequestLimit && !s.CanRestore && !s.CanResume); Equal(65, s.Battery.Percent);
            Throws<InvalidDataException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
        });
        Test("different machine refuses saved baseline", () => {
            var env = Saved(); env.Device = env.Device with { Model = "FA607-other" };
            Throws<InvalidOperationException>(() => new ChargingOperations(env).SetNewLimit(60)); Equal(0, env.Transport.Writes.Count);
            Throws<InvalidOperationException>(() => new ChargingOperations(env).Restore(true)); Equal(0, env.Transport.Writes.Count);
        });
        Test("prepare for other OS restores and disables reapplication", () => {
            var env = Saved(); env.Transport.Raw=65536+60; new ChargingOperations(env).Restore(true);
            Check(!env.ReadPolicy().Enabled); Equal(60, env.ReadPolicy().SelectedLimit); Equal(100, env.ReadPolicy().OriginalLimit);
            Check(new MonitoringSession(env).CanResume);
        });
        Test("restore never guesses missing original", () => {
            var env = new SimulationEnvironment(); new ChargingOperations(env).Restore(true); Equal(0,env.Transport.Writes.Count);
            Check(env.ReadPolicy().OriginalLimit is null);
        });
        Test("failed restore leaves enabled state intact and reports failure", () => {
            var env=Saved(); env.Transport.Raw=65536+60; env.Transport.FailingLimits.Add(100);
            Throws<InvalidOperationException>(() => new ChargingOperations(env).Restore(true)); Check(env.ReadPolicy().Enabled);
        });
        Test("resume saved setting enables only after success", () => {
            var env=Saved(false); new ChargingOperations(env).Resume(); Check(env.ReadPolicy().Enabled); Equal(65536+60,env.Transport.Raw);
        });
        Test("failed resume does not enable policy", () => {
            var env=Saved(false); env.Transport.FailingLimits.Add(60);
            Throws<InvalidOperationException>(() => new ChargingOperations(env).Resume()); Check(!env.ReadPolicy().Enabled);
        });
        Test("resume without baseline is blocked", () => {
            var env=new SimulationEnvironment(); Throws<InvalidOperationException>(()=>new ChargingOperations(env).Resume()); Equal(0,env.Transport.Writes.Count);
        });
        Test("BIOS change pauses startup reapplication", () => {
            var env=Saved(); env.Device=env.Device with { BiosVersion="changed" };
            Throws<InvalidOperationException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Startup)); Equal(0,env.Transport.Writes.Count);
        });
        Test("startup applies enabled saved policy", () => {
            var env=Saved(); new AgentLifecycle(env).Handle(AgentEvent.Startup); Check(env.Transport.Writes.SequenceEqual(new[]{60}));
        });
        Test("shutdown restores without losing saved preference", () => {
            var env=Saved(); env.Transport.Raw=65536+60; new AgentLifecycle(env).Handle(AgentEvent.Shutdown);
            Check(env.Transport.Writes.SequenceEqual(new[]{100})); Check(env.ReadPolicy().Enabled); Equal(60,env.ReadPolicy().SelectedLimit);
        });
        Test("service stop does not read or modify simulated hardware", () => {
            var env=Saved(); new AgentLifecycle(env).Handle(AgentEvent.Stop); Equal(0,env.Events.Count);
        });
        Test("disabled policy prevents startup and shutdown requests", () => {
            var env=Saved(false); var life=new AgentLifecycle(env); life.Handle(AgentEvent.Startup); life.Handle(AgentEvent.Shutdown);
            Equal(0,env.Transport.Writes.Count); Check(!env.Events.Contains("simulation.read"));
        });
        Test("matching startup readback avoids redundant requests", () => {
            var env=Saved(); env.Transport.Raw=65536+60; new AgentLifecycle(env).Handle(AgentEvent.Startup); Equal(0,env.Transport.Writes.Count);
        });
        Test("ASUS rejects non-ASUS construction",()=>Throws<NotSupportedException>(()=>new AsusController(new("Dell","test","test","sim"),new SimulationEnvironment().Transport)));
        Test("ASUS rejects unsupported percentage before transport",()=>{
            var env=new SimulationEnvironment(); var c=new AsusController(env.Device,env.Transport);
            Throws<NotSupportedException>(()=>c.SetLimit(56)); Throws<ArgumentOutOfRangeException>(()=>c.SetLimit(101)); Equal(0,env.Transport.Writes.Count);
        });
        Test("ASUS acknowledgment failure is not success",()=>{
            var env=new SimulationEnvironment(); env.Transport.Acknowledgment=0;
            Throws<InvalidOperationException>(()=>new AsusController(env.Device,env.Transport).SetLimit(60));
        });
        Test("ASUS mismatching readback is not success",()=>{
            var env=new SimulationEnvironment(); env.Transport.MismatchedReadback=true;
            Throws<InvalidOperationException>(()=>new AsusController(env.Device,env.Transport).SetLimit(60));
        });
        foreach (var availability in Enum.GetValues<ControlAvailability>())
        {
            var value = availability;
            Test("compatibility control gating: " + value, () => {
                var env = new SimulationEnvironment { ControllerOverride = new FixedController(value) };
                var session = new MonitoringSession(env);
                Equal(value, session.Availability);
                Equal(value is ControlAvailability.Available or ControlAvailability.Unverified, session.CanRequestLimit);
            });
        }
        Test("startup refuses unreadable current setting", () => {
            var env=Saved(); env.Transport.Raw=0;
            Throws<InvalidOperationException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Startup)); Equal(0,env.Transport.Writes.Count);
        });
        Test("direct ASUS operation refuses unreadable current setting", () => {
            var env=new SimulationEnvironment(); env.Transport.Raw=0;
            Throws<InvalidOperationException>(()=>new AsusController(env.Device,env.Transport).SetLimit(60)); Equal(0,env.Transport.Writes.Count);
        });
        Test("no battery blocks saved resume and restoration", () => {
            var env=Saved(); env.Battery=BatterySnapshot.Decode(true,1,128,255);
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Resume());
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Restore(true)); Equal(0,env.Transport.Writes.Count);
        });
        Test("BIOS change blocks stale baseline reuse and restore", () => {
            var env=Saved(); env.Device=env.Device with { BiosVersion="changed" };
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).SetNewLimit(80));
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Restore(true)); Equal(0,env.Transport.Writes.Count);
        });
        Test("service startup propagates corrupt policy without resetting it", () => {
            var env=Saved(); env.CorruptPolicy=true;
            Throws<InvalidDataException>(()=>new AgentLifecycle(env).Handle(AgentEvent.Startup)); Equal(0,env.Transport.Writes.Count);
        });
        Test("restore with unreadable current setting never writes", () => {
            var env=Saved(); env.Transport.Raw=0;
            Throws<InvalidOperationException>(()=>new ChargingOperations(env).Restore(true)); Equal(0,env.Transport.Writes.Count);
        });
        Test("stale policy disables presentation controls", () => {
            var env=Saved(false); env.Device=env.Device with { BiosVersion="changed" };
            var session=new MonitoringSession(env); Check(!session.CanRequestLimit && !session.CanResume && !session.CanRestore);
        });
        Test("battery removal updates control decisions on monitor refresh", () => {
            var env=Saved(false); var session=new MonitoringSession(env); Check(session.CanResume);
            env.Battery=BatterySnapshot.Decode(true,1,128,255); session.RefreshBattery();
            Check(!session.CanRequestLimit && !session.CanResume && !session.CanRestore); Equal(0,env.Transport.Writes.Count);
        });
        Test("failed policy commit after request restores saved baseline", () => {
            var env=Saved(); env.FailingPolicySaves.Add(env.PolicySaveAttempts+2);
            Throws<IOException>(()=>new ChargingOperations(env).SetNewLimit(80));
            Check(env.Transport.Writes.SequenceEqual(new[]{80,100})); Equal(60,env.ReadPolicy().SelectedLimit);
        });
        Test("unknown simulation switches never become production switches", () => {
            foreach(string[] arguments in new[]{new[]{"--test"},new[]{"--simulation"},new[]{"--plugin","external.dll"},new[]{"--set","60"}})
                Check(!SimulationEntry.Accepts(arguments));
        });
        Test("monitor refresh cannot reenable controls during an active operation", () => {
            var env=Saved(false); var session=new MonitoringSession(env); Check(session.CanResume);
            session.OperationInProgress=true; session.Refresh();
            Check(!session.CanRequestLimit && !session.CanRestore && !session.CanResume);
            session.OperationInProgress=false; Check(session.CanResume);
        });
        Test("ordinary GUI initialization requires no admin",()=>Check(!CommandAccess.RequiresAdministrator([])));
        foreach(string cmd in new[]{"--init","--set","--restore","--prepare","--resume","--install-service","--uninstall-service-and-restore","--service"})
        {
            string value=cmd;
            Test("simulation rejects privileged command " + value,()=>{
                Check(!SimulationEntry.Accepts([value])); Check(CommandAccess.RequiresAdministrator([value]));
            });
        }
        Test("disclosure retains cross-boot and readback limitations",()=>{
            foreach(string term in new[]{"another Windows","external drive","crash","power loss","hibernation/Fast Startup","100%","refuses the hardware change","does NOT prove","Prepare for another OS"})
                Check(SafetyDisclosure.CrossBoot.Contains(term),term);
        });
        Test("simulation assembly excludes every native adapter",()=>{
            var assembly=typeof(SimulationEnvironment).Assembly;
            foreach(var t in assembly.GetTypes())
            {
                Check(!new[]{"WindowsEnvironment","WindowsAgentService","WindowsAsusTransport","MachineStore","ControllerRegistry"}.Contains(t.Name),t.Name);
                foreach(var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance))
                    Check(!m.GetCustomAttributesData().Any(a=>a.AttributeType.Name=="DllImportAttribute"),m.Name);
            }
            Check(typeof(DeviceInfo).GetMethod("Detect") is null);
            Check(typeof(BatterySnapshot).GetMethod("Read") is null);
            Check(typeof(AsusController).GetConstructors().All(c=>c.GetParameters().Length==2));
        });
        RunPersistenceTests();
        RunMaintenanceTests();
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed, 0 skipped. In-memory simulation only.");
        return _failed==0?0:1;
    }
}

internal sealed class FixedController(ControlAvailability availability) : IChargingController
{
    public string Brand => "SIMULATED";
    public ControllerProbe Probe() => new(availability, "Synthetic classification fixture", 100, new[] {60,80,100}, false);
    public int? ReadLimit() => 100;
    public void SetLimit(int percent) => throw new InvalidOperationException("Read-only classification fixture");
}

// Deliberately violates the controller's SetLimit readback contract, so the recovery
// coordinator must independently verify rather than trusting an apparent success.
internal sealed class UnverifiedRecoveryController : IChargingController
{
    public int? Current { get; set; }
    public int? RecoveryReadback { get; set; }
    public string Brand => "SIMULATED";
    public ControllerProbe Probe() => new(ControlAvailability.Unverified,"Synthetic recovery failure",Current,new[]{60,80,100},false);
    public int? ReadLimit() => Current;
    public void SetLimit(int percent) => Current = percent==100 ? RecoveryReadback : percent;
}
