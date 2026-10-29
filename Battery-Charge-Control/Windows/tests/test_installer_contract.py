"""Source contracts only: never invoke an installer, application or SCM operation."""
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[1]

class InstallerContracts(unittest.TestCase):
    def setUp(self):
        self.installer=(ROOT/'installer/BCC.iss').read_text()
        self.commands=(ROOT/'src/BCC.App/AgentCommands.cs').read_text()
    def test_uninstall_requires_blocking_predeletion_gate(self):
        self.assertNotIn('[UninstallRun]',self.installer)
        body=self.installer.split('function InitializeUninstall(): Boolean;',1)[1]
        self.assertIn('Result := False;',body)
        self.assertIn('if UninstallSilent then',body)
        self.assertIn('MB_YESNO or MB_DEFBUTTON2',body)
        self.assertIn("Result := RunMaintenance(Executable, '--prepare-maintenance",body)
        self.assertIn('if not Result then',body)
    def test_setup_gates_component_removal_and_checks_final_exit_code(self):
        self.assertIn('function PrepareToInstall(var NeedsRestart: Boolean): String;',self.installer)
        self.assertIn("if WizardSilent then",self.installer)
        self.assertIn("if RunMaintenance(Helper, '--prepare-maintenance",self.installer)
        self.assertIn("if CurStep = ssPostInstall then",self.installer)
        self.assertIn('if not RunMaintenance',self.installer)
        self.assertIn("RaiseException('Installation did not complete.",self.installer)
        self.assertIn('Result := ResultCode = 0;',self.installer)
        self.assertIn('if not InstallationComplete then Result := 10;',self.installer)
        self.assertIn('Check: IsInstallationComplete',self.installer)
    def test_bootstrap_helper_is_embedded_not_registered_as_service(self):
        self.assertIn("ExtractTemporaryFile('BatteryChargeControl.exe');",self.installer)
        self.assertNotIn('DestName: "BCCMaintenance.exe"',self.installer)
        self.assertIn("ExtractTemporaryFile('BCCMaintenance.Asus.dll')",self.installer)
        self.assertIn("RunMaintenance(ExpandConstant('{app}\\{#MyExe}'), Parameters)",self.installer)
        self.assertIn('Maintenance(args[1]).PrepareRemoval();',self.commands)
        self.assertIn('CompleteInstallation(true, InstallService)',self.commands)
    def test_production_shortcuts_are_app_scoped_and_no_policy_deletion(self):
        section=self.installer.split('[InstallDelete]',1)[1].split('[Icons]',1)[0]
        self.assertIn('"{autodesktop}\\Battery Charge Control.lnk"',section)
        self.assertNotIn('ProgramData',section)
        self.assertNotIn('policy.json',section)
        icons=self.installer.split('[Icons]',1)[1].split('[Run]',1)[0]
        self.assertEqual(1,len(re.findall(r'^Name: "\{group\}',icons,re.M)))
        self.assertNotIn('unins000',icons)
    def test_old_uninstall_command_uses_strict_gate_not_catch_and_delete(self):
        body=self.commands.split('case "--uninstall-service-and-restore":',1)[1].split('case ',1)[0]
        self.assertIn('PrepareRemoval();',body)
        self.assertNotIn('catch',body)
        self.assertNotIn('DeleteService()',body)
    def test_native_service_adapter_checks_owner_and_does_not_accept_delete_pending_as_absent(self):
        native=(ROOT/'src/BCC.App/WindowsInstallationService.cs').read_text()
        self.assertIn('VerifyOwner();',native)
        self.assertIn('StringComparison.OrdinalIgnoreCase',native)
        self.assertIn('if(error==1072) return InstalledServiceState.DeletePending;',native)
        self.assertIn('if(error==1060) return InstalledServiceState.Absent;',native)
    def test_fixture_remains_non_elevated_and_never_auto_executes(self):
        fixture=(ROOT/'installer/MonitoringOnly.iss').read_text()
        self.assertIn('PrivilegesRequired=lowest',fixture)
        self.assertNotRegex(fixture,r'(?m)^\[(Run|UninstallRun|Code|Registry)\]')
    def test_vm_script_requires_external_review_binding_and_reinstall_cleanup(self):
        script=(ROOT/'scripts/verify-monitoring-vm.ps1').read_text()
        for required in ('[Parameter(Mandatory)][string]$ReviewRecord','$review.NoHardwarePassthrough',
                         '$review.SnapshotId','$review.IndependentReviewer','$review.VmUuid -ne $vm.UUID',
                         '$review.InstallerSha256 -ne $ExpectedSha256',"$os.Caption -notmatch 'Windows 11'",'Reinstallation cleanup incomplete.'):
            self.assertIn(required,script)
        self.assertNotIn('verify-monitoring-vm.ps1',(ROOT.parents[1]/'.github/workflows/windows-build.yml').read_text())

if __name__=='__main__': unittest.main()
