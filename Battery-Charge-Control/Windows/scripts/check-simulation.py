"""Fail closed if the simulation build gains production adapters or host-operation code.
This structural review aid is not an OS sandbox or a proof against malicious source edits.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

if not __debug__:
    raise RuntimeError('Safety checks must run with Python assertions enabled.')

root = Path(__file__).resolve().parents[1]
allowed = {
    'tests/BCC.Simulation/BCC.Simulation.csproj': {
        '../../src/BCC.Core/Domain.cs', '../../src/BCC.Core/ChargingOperations.cs',
        '../../src/BCC.Core/MonitoringSession.cs', '../../src/BCC.Core/SafetyDisclosure.cs',
        '../../src/BCC.Core/InstallationMaintenance.cs',
        '../../src/BCC.Controller.Asus/AsusController.cs', 'SimulationEnvironment.cs'},
    'tests/BCC.Tests/BCC.Tests.csproj': {'Program.cs', 'MaintenanceTests.cs'},
    'tests/BCC.MonitoringPreview/BCC.MonitoringPreview.csproj': {'Program.cs'},
}
for project, expected in allowed.items():
    path = root / project
    xml = ET.parse(path).getroot()
    assert xml.findtext('PropertyGroup/EnableDefaultCompileItems') == 'false', project
    assert xml.findtext('PropertyGroup/TreatWarningsAsErrors') == 'true', project
    assert {n.attrib['Include'] for n in xml.findall('.//Compile')} == expected, project
    references = [n.attrib['Include'] for n in xml.findall('.//ProjectReference')]
    assert references == ([] if 'BCC.Simulation.csproj' in project else ['../BCC.Simulation/BCC.Simulation.csproj']), project
    assert not xml.findall('.//PackageReference') and not xml.findall('.//Reference'), project
    # No custom targets/imports/Exec commands that could add hidden adapters or launch programs.
    assert not any(xml.findall('.//' + tag) for tag in ('Import', 'Target', 'UsingTask', 'Exec')), project
    for name in expected:
        source = (path.parent / name).read_text()
        for forbidden in (r'\bDllImport\s*\(', r'\bLibraryImport\s*\(', r'\bunsafe\b',
                          r'\bProcess\s*\.', r'\bProcessStartInfo\b', r'\bRegistry\s*\.',
                          r'\bAssembly\s*\.\s*Load', r'\bNativeLibrary\b', r'\bActivator\s*\.',
                          r'\bFile\s*\.', r'\bDirectory\s*\.', r'\bHttpClient\b', r'\bSocket\b',
                          r'\bWindowsIdentity\b', r'\bServiceController\b'):
            assert not re.search(forbidden, source), (name, forbidden)

fixture = (root / 'installer/MonitoringOnly.iss').read_text()
sections = set(re.findall(r'^\[([^]]+)\]', fixture, re.M))
assert sections == {'Setup', 'Languages', 'Tasks', 'Files', 'Icons', 'InstallDelete'}, sections
assert 'PrivilegesRequired=lowest' in fixture
assert 'CreateUninstallRegKey=yes' in fixture and 'Uninstallable=yes' in fixture
assert 'AppId={{BB4BCC13-06A6-482B-A914-3FA0D38E6E0F}' in fixture
assert 'AppId={{4E1E310B-AC37-4E62-928A-63E4E1271001}' not in fixture
file_lines = [s for s in fixture.splitlines() if s.startswith('Source:')]
assert file_lines == ['Source: "..\\artifacts\\simulation\\payload\\{#PreviewExe}"; DestDir: "{app}"; Flags: ignoreversion']
assert fixture.count('Name: "{group}\\') == 1
assert 'Name: "{autodesktop}\\BCC No-Firmware Simulation Preview"' in fixture
assert 'Flags: unchecked' in fixture
assert 'Type: files; Name: "{autodesktop}\\BCC No-Firmware Simulation Preview.lnk"' in fixture
assert '#include' not in fixture and 'BatteryChargeControl.exe' not in fixture

manifest = ET.parse(root / 'src/BCC.App/app.manifest')
level = manifest.find('.//{urn:schemas-microsoft-com:asm.v3}requestedExecutionLevel')
assert level is not None and level.attrib == {'level': 'asInvoker', 'uiAccess': 'false'}
production = (root / 'src/BCC.App/Program.cs').read_text()
assert production.index('if (args.Length > 0)') < production.index('IsAdministrator()') < production.index('Application.Run(')
assert '--test' not in production and '--simulation' not in production
ui = (root / 'src/BCC.App/MainWindow.cs').read_text()
assert ui.count('if (!ShowDisclosure(askConsent: true)) return;') == 2  # Apply and resume.
assert 'MessageBoxDefaultButton.Button2) == DialogResult.Yes' in ui
assert 'SafetyDisclosure.CrossBoot' in ui
installer = (root / 'installer/BCC.iss').read_text()
assert installer.count('Name: "{group}\\') == 1
assert 'Name: "{autodesktop}\\Battery Charge Control"' in installer
assert 'Flags: unchecked' in installer
assert 'Agreed.Checked := False;' in installer
assert 'and not Agreed.Checked then' in installer
assert '[UninstallRun]' not in installer
assert 'function InitializeUninstall(): Boolean;' in installer
assert '--prepare-maintenance' in installer
assert 'if not Result then' in installer
assert 'PrivilegesRequired=admin' in installer
assert 'Uninstallable=no' not in installer and 'CreateUninstallRegKey=no' not in installer
vm_script = (root / 'scripts/verify-monitoring-vm.ps1').read_text()
assert "$env:GITHUB_ACTIONS -eq 'true'" in vm_script
assert '$SafeguardsReviewed' in vm_script and '$DisposableVmAcknowledged' in vm_script
workflow = (root.parents[1] / '.github/workflows/windows-build.yml').read_text()
assert 'verify-monitoring-vm.ps1' not in workflow
assert 'dotnet run --project tests/BCC.Tests/BCC.Tests.csproj' in workflow
assert './scripts/build-simulation.ps1' in workflow
print('PASS: simulation source allowlists, no native/process/file/network adapters, separate non-elevated fixture, manifest and CI execution boundary.')
