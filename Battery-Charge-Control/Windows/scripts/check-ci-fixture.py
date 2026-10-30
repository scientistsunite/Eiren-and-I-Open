"""Source-only review aid. Does not launch installers or claim to be a sandbox/attestation."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

if not __debug__:
    raise RuntimeError('Safety checks require assertions enabled')
root = Path(__file__).resolve().parents[1]
repo = root.parents[1]
fixture = (root / 'installer/MonitoringOnlyCI.iss').read_text()
assert set(re.findall(r'^\[([^]]+)\]', fixture, re.M)) == {
    'Setup', 'Languages', 'Tasks', 'Files', 'Icons', 'InstallDelete'}
assert not re.search(r'(?im)^\s*#(?:include|expr|emit)\b', fixture)
assert 'PrivilegesRequired=lowest' in fixture and 'CloseApplications=no' in fixture
assert 'AppId={{A57C22D3-BE65-48A8-BC98-BC94AE2AC101}' in fixture
assert 'DefaultDirName={localappdata}\\BCC CI No-Firmware Fixture' in fixture
assert 'ArchitecturesAllowed=arm64' in fixture and 'ArchitecturesAllowed=x64os' in fixture
assert 'CreateUninstallRegKey=yes' in fixture and 'Uninstallable=yes' in fixture
assert [s for s in fixture.splitlines() if s.startswith('Source:')] == [
    'Source: "..\\artifacts\\ci-fixture\\{#FixtureArchitecture}\\payload\\BCC.NoFirmwarePreview.exe"; DestDir: "{app}"; Flags: ignoreversion']
assert [s for s in fixture.splitlines() if s.startswith('Type:')] == [
    'Type: files; Name: "{autodesktop}\\BCC CI No-Firmware Fixture.lnk"']
assert len(re.findall(r'^Name: "\{group\}', fixture, re.M)) == 1
assert 'Flags: unchecked' in fixture
assert not any(s in fixture for s in ('regserver', 'sharedfile', 'external', 'BatteryChargeControl.exe', 'Controllers\\'))

# No repository/ancestor build injections or alternate NuGet sources were reviewed.
for project in (root / 'tests').glob('*/*.csproj'):
    for parent in project.parents:
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config', 'nuget.config'):
            assert not (parent / name).exists(), f'Unreviewed inherited build input: {parent / name}'
    xml = ET.parse(project).getroot()
    assert xml.attrib == {'Sdk': 'Microsoft.NET.Sdk'}
    assert all(n.tag in ('PropertyGroup', 'ItemGroup') for n in xml)
    assert all(n.tag in ('Compile', 'ProjectReference') for n in xml.findall('ItemGroup/*'))
    permitted = {'TargetFramework','Nullable','ImplicitUsings','EnableDefaultCompileItems','TreatWarningsAsErrors',
                 'OutputType','UseWindowsForms','EnableWindowsTargeting','AssemblyName','ApplicationManifest'}
    assert all(n.tag in permitted and not n.attrib for n in xml.findall('PropertyGroup/*'))
sdk = json.loads((root / 'global.json').read_text())['sdk']
assert sdk == {'version': '10.0.402', 'rollForward': 'disable', 'allowPrerelease': False}

build = (root / 'scripts/build-ci-fixture.ps1').read_text()
for required in ('check-simulation.py','check-ci-fixture.py','BCC.MonitoringPreview.csproj',
                 'MonitoringOnlyCI.iss','$files.Count -ne 1','$machine -ne $expectedMachine',
                 'InstallerSha256','PayloadSha256','CompilerSha256'):
    assert required in build, required
assert not re.search(r'(?im)^\s*(Start-Process|Install-Package|choco|&\s*\$installer)', build)
assert 'build-windows.ps1' not in build and 'installer/BCC.iss' not in build
gate = (root / 'scripts/ci-fixture-gate.ps1').read_text()
for required in ('BCC_INSTALLER_CI_ENABLED','BCC_INSTALLER_CI_APPROVED_COMMIT','BCC_INSTALLER_CI_APPROVAL_REFERENCE',
                 'GITHUB_WORKFLOW_REF','RUNNER_ENVIRONMENT','ProcessArchitecture','OSArchitecture',
                 'Windows Server 2025','Windows 11','InstallerSha256','SourceCommit'):
    assert required in gate, required
host = (root / 'scripts/verify-monitoring-ci.ps1').read_text()
assert host.index('Assert-CiFixtureApproval $Architecture') < host.index('New-LocalUser') < host.index('Start-Process')
for required in ('-Credential $credential -LoadUserProfile','Remove-LocalUser','Remove-CimInstance',
                 '$childExitCode -ne 0',"$report.Status -cne 'Passed'",'supervisor.json','GITHUB_STEP_SUMMARY'):
    assert required in host, required
worker = (root / 'scripts/test-ci-fixture-user.ps1').read_text()
for required in ('ExpectedUserSid',"'S-1-5-32-544' -notin",'$process.ExitCode -eq 0',
                 '$process.WaitForExit(90000)','PayloadSha256','uninstallerHash',
                 "Status='NotRun'","Status='Failed'",'results.json','results.md','Verify-Clean'):
    assert required in worker, required
assert worker.index("try {\n  Assert-CiFixtureApproval") < worker.index("  Check 'Ordinary user'") < worker.index("  Check 'Profile initialization'")
assert "function Folder(" not in worker and "Folder 'UserProfile'" not in worker
assert '::GetFolderPath(' not in worker  # Ambient shell resolution is not SID-bound bootstrap input.
assert '-KnownFolders $known' not in worker
for required in ('ProfileList\\', 'ProfileImagePath', 'BCC_PROFILE_NOT_LOADED',
                 'Resolve-CiFixtureProfilePaths', 'Initialize-CiFixtureProfilePaths',
                 'RegistryValueKind]::ExpandString', 'ProfileInitialization=$profileInitialization',
                 'Get-CiFixtureFailureDiagnostic'):
    assert required in worker, required
for name in ('ci-fixture-profile.ps1','ci-fixture-diagnostics.ps1'):
    assert name in worker and name in host, name
profile=(root/'scripts/ci-fixture-profile.ps1').read_text()
for required in ('IsNullOrWhiteSpace($ProfilePath)', 'BCC_PROFILE_ROOT_MISSING',
                 'IsPathFullyQualified', 'BCC_PROFILE_FOLDER_OUTSIDE_USER',
                 'OrdinalIgnoreCase', '$EnsureDirectory', '$WriteShellFolders'):
    assert required in profile, required
for required in ('Assert-CiFixtureProfileRegistration', '$ReadShellFolders',
                 'BCC_PROFILE_REGISTRATION_MISSING', 'BCC_PROFILE_REGISTRATION_MISMATCH'):
    assert required in profile, required
assert profile.index('& $WriteShellFolders') < profile.index('$registered=& $ReadShellFolders') < profile.index('Assert-CiFixtureProfileRegistration -Plan $verified')
for required in ('Read-WorkerShellFolders -ForAuthenticatedSid',
                 'Assert-CiFixtureProfileRegistration -Plan $plan -RegisteredFolders $currentUserRegistration',
                 'RegistryValueOptions]::DoNotExpandEnvironmentNames',"RegistrationReadback='Verified'"):
    assert required in worker, required
assert "PathAuthority -cne 'AuthenticatedSidProfileList'" in host
assert "RegistrationReadback -cne 'Verified'" in host
assert '$env:' not in profile  # Pure planner cannot silently inherit an administrator's environment.
diagnostics=(root/'scripts/ci-fixture-diagnostics.ps1').read_text()
assert 'ScriptLineNumber' in diagnostics and 'GetFileName' in diagnostics
for text in (worker,host):
    assert '$_.Exception.Message' not in text  # No raw credential/argument/path dumps into artifacts.
profile_tests=(root/'tests/test-ci-profile.ps1').read_text()
assert not re.search(r'(?:Start-Process|Process\]::Start|New-LocalUser|\[IO\.Directory\]|Registry\]::)',profile_tests)
for text in (host, worker, gate):
    assert not re.search(r'(?i)(?:New-Service|sc\.exe|--service|--set|--restore|--resume|--prepare-maintenance|BatteryChargeControl\.exe|AsusWmi|GetSystemPowerStatus)', text)

workflow = (repo / '.github/workflows/bcc-installer-lifecycle.yml').read_text()
assert re.findall(r'^  (workflow_dispatch|push|pull_request|schedule|workflow_run|workflow_call):', workflow, re.M) == ['workflow_dispatch']
assert 'needs: approve' in workflow and "e['ENABLED'] == 'true'" in workflow
assert "e['APPROVED_COMMIT'] == e['REQUESTED_COMMIT'] == e['GITHUB_SHA']" in workflow
assert "e['GITHUB_REF'] == 'refs/heads/main'" in workflow
assert 'BCC_INSTALLER_CI_APPROVAL_REFERENCE' in workflow
assert 'permissions:\n  contents: read' in workflow and 'write' not in workflow
assert 'runner: windows-2025' in workflow and 'runner: windows-11-arm' in workflow
assert 'persist-credentials: false' in workflow and 'if: always()' in workflow
assert 'continue-on-error' not in workflow and 'build-windows.ps1' not in workflow
assert 'verify-monitoring-vm.ps1' not in workflow and 'MonitoringOnly.iss' not in workflow
assert not re.search(r'(?im)^\s*(pull_request_target|environment):', workflow)
review = (repo / '.github/workflows/bcc-fixture-review.yml').read_text()
assert 'verify-monitoring-ci.ps1' not in review and 'test-ci-fixture-user.ps1' not in review
assert 'build-ci-fixture.ps1' in review and 'runner: windows-11-arm' in review
assert './tests/test-ci-profile.ps1' in review
for text in (workflow, review):
    assert not re.search(r'(?im)self-hosted|secrets\.', text)
    assert all(re.fullmatch(r'actions/(?:checkout|setup-dotnet|upload-artifact)@[0-9a-f]{40}', s)
               for s in re.findall(r'uses:\s*(\S+)', text))
# Manual VM and ordinary compilation paths remain separate; they cannot dispatch CI execution.
vm = (root / 'scripts/verify-monitoring-vm.ps1').read_text()
assert "$env:GITHUB_ACTIONS -eq 'true'" in vm and '$review.NoHardwarePassthrough' in vm
ordinary = (repo / '.github/workflows/windows-build.yml').read_text()
assert 'verify-monitoring-ci.ps1' not in ordinary and 'test-ci-fixture-user.ps1' not in ordinary
print('PASS: separate commit-approved manual CI path, synthetic-only fixture contents, pinned build inputs, standard-user execution and honest failure reports (source checks only).')
