; Enforce compiler version from the compiler itself, not the ISCC executable resource.
#if VER < EncodeVer(6, 7, 1) || VER >= EncodeVer(6, 7, 2)
  #error Reproducible builds require Inno Setup 6.7.1.
#endif
; Separate simulation fixture, NOT the production installer and NOT production runtime validation.
; Compile only in CI. Execution requires separate review and a disposable Windows 11 VM.
#define PreviewExe "BCC.NoFirmwarePreview.exe"
[Setup]
AppId={{BB4BCC13-06A6-482B-A914-3FA0D38E6E0F}
AppName=BCC No-Firmware Simulation Preview
AppVersion=0.1.0
AppPublisher=Battery Charge Control Test Harness
DefaultDirName={localappdata}\BCC No-Firmware Simulation Preview
DefaultGroupName=BCC No-Firmware Simulation Preview
OutputDir=..\artifacts\simulation
OutputBaseFilename=BCC-NoFirmware-MonitoringOnly-Test-Setup
UninstallDisplayName=BCC No-Firmware Simulation Preview
UninstallDisplayIcon={app}\{#PreviewExe}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=..\docs\monitoring-preview-notice.txt
CloseApplications=no
Uninstallable=yes
CreateUninstallRegKey=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create simulation preview desktop shortcut"; Flags: unchecked
[Files]
Source: "..\artifacts\simulation\payload\{#PreviewExe}"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\BCC No-Firmware Simulation Preview"; Filename: "{app}\{#PreviewExe}"
Name: "{autodesktop}\BCC No-Firmware Simulation Preview"; Filename: "{app}\{#PreviewExe}"; Tasks: desktopicon
; Intentionally NO [Run], [UninstallRun], [Registry], [Code], service task, drivers or controller DLLs.
; Inno Setup owns removal of its payload, shortcuts and HKCU Installed apps registration.
[InstallDelete]
; Allow modification to deselect the fixture-only desktop icon; never touch production shortcuts.
Type: files; Name: "{autodesktop}\BCC No-Firmware Simulation Preview.lnk"
