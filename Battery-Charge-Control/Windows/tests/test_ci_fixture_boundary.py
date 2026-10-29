"""Negative source/approval tests. Never execute PowerShell, installers or native APIs."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = ROOT.parents[1] / '.github/workflows'


class CiBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        repo = Path(self.temp.name) / 'repo'
        self.root = repo / 'Battery-Charge-Control/Windows'
        shutil.copytree(ROOT, self.root, ignore=shutil.ignore_patterns('bin', 'obj', 'artifacts', '__pycache__'))
        workflows = repo / '.github/workflows'
        workflows.mkdir(parents=True)
        for name in ('windows-build.yml','bcc-fixture-review.yml','bcc-installer-lifecycle.yml'):
            shutil.copyfile(WORKFLOWS/name, workflows/name)
        self.workflows = workflows

    def check(self):
        return subprocess.run([sys.executable,'-B',str(self.root/'scripts/check-ci-fixture.py')],
                              capture_output=True,text=True,timeout=15)

    def mutate(self, path, before, after):
        source = path.read_text()
        self.assertIn(before, source)
        path.write_text(source.replace(before, after))
        self.assertNotEqual(0, self.check().returncode)

    def test_reviewed_boundary_passes(self):
        result = self.check()
        self.assertEqual(0,result.returncode,result.stderr)

    def test_execution_hook_is_rejected(self):
        path = self.root/'installer/MonitoringOnlyCI.iss'
        path.write_text(path.read_text()+'\n[Run]\nFilename: "{app}\\BCC.NoFirmwarePreview.exe"\n')
        self.assertNotEqual(0,self.check().returncode)

    def test_extra_production_payload_is_rejected(self):
        path = self.root/'installer/MonitoringOnlyCI.iss'
        path.write_text(path.read_text()+'\nSource: "BatteryChargeControl.exe"; DestDir: "{app}"\n')
        self.assertNotEqual(0,self.check().returncode)

    def test_machine_installation_is_rejected(self):
        self.mutate(self.root/'installer/MonitoringOnlyCI.iss','PrivilegesRequired=lowest','PrivilegesRequired=admin')

    def test_inherited_msbuild_execution_is_rejected(self):
        (self.root/'Directory.Build.targets').write_text('<Project><Target Name="Unsafe" BeforeTargets="Build" /></Project>')
        self.assertNotEqual(0,self.check().returncode)

    def test_added_package_dependency_is_rejected(self):
        self.mutate(self.root/'tests/BCC.MonitoringPreview/BCC.MonitoringPreview.csproj','</ItemGroup>',
                    '<PackageReference Include="Unreviewed" Version="1.0.0" /></ItemGroup>')

    def test_automatic_execution_event_is_rejected(self):
        self.mutate(self.workflows/'bcc-installer-lifecycle.yml','  workflow_dispatch:', '  push:\n  workflow_dispatch:')

    def test_removed_commit_gate_is_rejected(self):
        self.mutate(self.workflows/'bcc-installer-lifecycle.yml',
                    "e['APPROVED_COMMIT'] == e['REQUESTED_COMMIT'] == e['GITHUB_SHA']", 'True')

    def test_missing_admin_token_check_is_rejected(self):
        self.mutate(self.root/'scripts/test-ci-fixture-user.ps1',"'S-1-5-32-544' -notin", "'S-1-5-32-544' -in")

    def test_missing_profile_root_guard_is_rejected(self):
        self.mutate(self.root/'scripts/ci-fixture-profile.ps1',
                    '[string]::IsNullOrWhiteSpace($ProfilePath)', '$false')

    def test_raw_error_message_in_worker_is_rejected(self):
        source=self.root/'scripts/test-ci-fixture-user.ps1'
        source.write_text(source.read_text()+'\n# Regression: $_.Exception.Message\n')
        self.assertNotEqual(0,self.check().returncode)

    def test_ambient_shell_resolution_cannot_reenter_bootstrap(self):
        source=self.root/'scripts/test-ci-fixture-user.ps1'
        source.write_text(source.read_text()+'\n# Unreviewed ambient resolution: [Environment]::GetFolderPath(\n')
        self.assertNotEqual(0,self.check().returncode)

    def test_missing_registration_readback_guard_is_rejected(self):
        self.mutate(self.root/'scripts/ci-fixture-profile.ps1',
                    'Assert-CiFixtureProfileRegistration -Plan $verified -RegisteredFolders $registered',
                    '# Registration verification removed')


class ApprovalGateTests(unittest.TestCase):
    def setUp(self):
        source = (WORKFLOWS/'bcc-installer-lifecycle.yml').read_text()
        self.gate = textwrap.dedent(source.split("python - <<'PY'\n",1)[1].split('\n          PY',1)[0])
        self.environment = {
            'GITHUB_EVENT_NAME':'workflow_dispatch','DEFAULT_BRANCH':'main','GITHUB_REF':'refs/heads/main',
            'GITHUB_WORKFLOW_REF':'owner/repo/.github/workflows/bcc-installer-lifecycle.yml@refs/heads/main',
            'GITHUB_SHA':'a'*40,'APPROVED_COMMIT':'a'*40,'REQUESTED_COMMIT':'a'*40,
            'ENABLED':'true','APPROVAL_REFERENCE':'Owner approval PR review 123','REQUESTED_APPROVAL':'Owner approval PR review 123',
        }
        if 'SYSTEMROOT' in os.environ:
            self.environment['SYSTEMROOT'] = os.environ['SYSTEMROOT']

    def run_gate(self, **changes):
        return subprocess.run([sys.executable,'-c',self.gate],env=self.environment|changes,
                              capture_output=True,text=True,timeout=10)

    def test_exact_explicit_approval_passes_only_the_pure_gate(self):
        result = self.run_gate()
        self.assertEqual(0,result.returncode,result.stderr)

    def test_disabled_missing_or_mismatched_approval_fails(self):
        for changes in ({'ENABLED':''},{'ENABLED':'false'},{'APPROVED_COMMIT':''},
                        {'GITHUB_SHA':'b'*40},{'REQUESTED_COMMIT':'b'*40},
                        {'APPROVAL_REFERENCE':''},{'REQUESTED_APPROVAL':'different'}):
            with self.subTest(changes=changes):
                self.assertNotEqual(0,self.run_gate(**changes).returncode)

    def test_non_main_non_dispatch_or_wrong_workflow_fails(self):
        for changes in ({'GITHUB_EVENT_NAME':'pull_request'},{'GITHUB_REF':'refs/heads/unsafe'},
                        {'GITHUB_WORKFLOW_REF':'owner/repo/.github/workflows/unsafe.yml@refs/heads/main'},
                        {'DEFAULT_BRANCH':'other'}):
            with self.subTest(changes=changes):
                self.assertNotEqual(0,self.run_gate(**changes).returncode)


if __name__ == '__main__':
    unittest.main()
