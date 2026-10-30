; Separate CI-only fixture. Does not weaken MonitoringOnly.iss or its manual VM gate.
#if VER < EncodeVer(6, 7, 1) || VER >= EncodeVer(6, 7, 2)
  #error Reproducible builds require Inno Setup 6.7.1.
#endif
#ifndef FixtureArchitecture
  #error FixtureArchitecture must be supplied by build-ci-fixture.ps1.
#endif
#if FixtureArchitecture != "x64" && FixtureArchitecture != "arm64"
  #error Only x64 and arm64 fixtures are reviewed.
#endif
[Setup]
AppId={{A57C22D3-BE65-48A8-BC98-BC94AE2AC101}
AppName=BCC CI No-Firmware Fixture
AppVersion=0.1.0
AppPublisher=Battery Charge Control Test Harness
DefaultDirName={localappdata}\BCC CI No-Firmware Fixture
DefaultGroupName=BCC CI No-Firmware Fixture
OutputDir=..\artifacts\ci-fixture\{#FixtureArchitecture}
OutputBaseFilename=BCC-CI-NoFirmware-{#FixtureArchitecture}-Setup
UninstallDisplayName=BCC CI No-Firmware Fixture
UninstallDisplayIcon={app}\BCC.NoFirmwarePreview.exe
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
#if FixtureArchitecture == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=..\docs\ci-fixture-notice.txt
CloseApplications=no
Uninstallable=yes
CreateUninstallRegKey=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create CI fixture desktop shortcut"; Flags: unchecked
[Files]
Source: "..\artifacts\ci-fixture\{#FixtureArchitecture}\payload\BCC.NoFirmwarePreview.exe"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\BCC CI No-Firmware Fixture"; Filename: "{app}\BCC.NoFirmwarePreview.exe"
Name: "{autodesktop}\BCC CI No-Firmware Fixture"; Filename: "{app}\BCC.NoFirmwarePreview.exe"; Tasks: desktopicon
[InstallDelete]
Type: files; Name: "{autodesktop}\BCC CI No-Firmware Fixture.lnk"
; No Run, UninstallRun, Code, Registry, DLL registration, controllers, drivers or service.
