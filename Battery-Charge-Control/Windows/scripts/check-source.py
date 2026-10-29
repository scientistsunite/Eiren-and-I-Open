"""Static packaging checks: NOT a Windows build, firmware test, or security certification."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
required = [
    'src/BCC.App/Program.cs', 'src/BCC.App/MainWindow.cs',
    'src/BCC.App/AgentCommands.cs', 'src/BCC.App/WindowsAgentService.cs',
    'src/BCC.App/app.manifest','src/BCC.App/app.ico',
    'src/BCC.Core/DeviceAndBattery.cs', 'src/BCC.Controller.Asus/AsusController.cs',
    'installer/BCC.iss', 'scripts/build-windows.ps1',
]
for file in required:
    p = root / file
    assert p.exists() and p.stat().st_size > 0, f'Missing: {file}'
app = (root / 'src/BCC.App/MainWindow.cs').read_text()
agent = (root / 'src/BCC.App/AgentCommands.cs').read_text() + (root / 'src/BCC.Core/ChargingOperations.cs').read_text() + (root / 'src/BCC.Core/MonitoringSession.cs').read_text()
installer = (root / 'installer/BCC.iss').read_text()
for term in ['CrossBootDisclosure','Uninstall...','Prepare for another OS','Verb = "runas"','Export compatibility report']:
    assert term in app, term
for term in ['OriginalLimit','SecurePolicyDirectory','--uninstall-service-and-restore','Restore(disablePolicy: false)']:
    assert term in agent + (root/'src/BCC.App/WindowsAgentService.cs').read_text(), term
assert 'No uninstaller Start menu shortcut' in installer
assert 'Name: "{group}\\Battery Charge Control"' in installer
assert 'PrivilegesRequired=admin' in installer
assert 'TaskName:' not in installer
print('PASS: expected files, read-before-write controls, installer and disclosure references exist.')
print('NOT TESTED: C# compilation, Inno Setup compilation, Windows service operation, firmware behavior.')
