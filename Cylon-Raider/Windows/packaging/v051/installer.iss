#ifndef OfficialIcon
  #define OfficialIcon "..\..\src\Raider.App\assets\CylonRaider.ico"
#endif
#ifndef AppVer
  #define AppVer "0.5.1"
#endif
#ifndef OutputDirOverride
  #define OutputDirOverride "..\..\release-assets"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\..\publish"
#endif
[Setup]
AppId={{C6235B22-2E4E-4C4D-BA36-1B0F34E26420}
AppName=Cylon Raider
AppVersion={#AppVer}
AppPublisher=Eiren & I Apps
DefaultDirName={autopf}\Cylon Raider
DefaultGroupName=Cylon Raider
AllowUNCPath=no
AllowNetworkDrive=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirOverride}
OutputBaseFilename=CylonRaider-Setup-0.5.1-prerelease
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CylonRaider.exe
UninstallDisplayName=Cylon Raider
CloseApplications=yes
RestartApplications=no
#ifdef OfficialIcon
SetupIconFile={#OfficialIcon}
#endif
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.marker"
[UninstallDelete]
Type: files; Name: "{app}\installed.marker"
[Icons]
Name: "{commonprograms}\Cylon Raider"; Filename: "{app}\CylonRaider.exe"; WorkingDir: "{app}"
[Code]
const
  RestoreMessage = $8000 + 42;
  ShutdownMessage = $8000 + 43;
var
  DeleteData: Boolean;
function CloseRaider(): Boolean;
var Window: HWND; Count: Integer;
begin
  Window := FindWindowByWindowName('Cylon Raider v0.5');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.4');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.3');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.2 — Persistent Surveillance');
  if Window <> 0 then PostMessage(Window, ShutdownMessage, 0, 0);
  Count := 0;
  while (Window <> 0) and (Count < 150) do begin
    Sleep(100); Count := Count + 1;
    Window := FindWindowByWindowName('Cylon Raider v0.5');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.4');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.3');
  if Window = 0 then Window := FindWindowByWindowName('Cylon Raider v0.2 — Persistent Surveillance');
  end;
  Result := Window = 0;
end;
function InitializeSetup(): Boolean;
begin
  Result := CloseRaider();
  if not Result then SuppressibleMsgBox('Cylon Raider is still flushing history. Exit it and retry setup.', mbError, MB_OK, IDOK);
end;
function ServiceRegistered(): Boolean;
begin Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\EirenCylonRaiderV02'); end;
function ServiceOperation(Operation: String): Boolean;
var Code: Integer; Binary: String;
begin
  Binary := ExpandConstant('{app}\CylonRaider.Service.exe');
  Result := FileExists(Binary) and Exec(Binary, Operation, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);
  if Result then Result := Code = 0;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Image: String;
begin
  Result := '';
  if ServiceRegistered() then begin
    RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\EirenCylonRaiderV02', 'ImagePath', Image);
    if CompareText(Image, '"' + ExpandConstant('{app}\CylonRaider.Service.exe') + '"') <> 0 then begin
      Result := 'Use the existing Cylon Raider directory or disable boot monitoring before changing the installation location.'; exit;
    end;
    if not ServiceOperation('/stop') then Result := 'Boot monitoring could not flush/stop. No application files were replaced.';
  end;
end;
procedure ProtectInstalledFiles();
var Code: Integer; Tool, Path: String;
begin
  Tool := ExpandConstant('{sys}\icacls.exe'); Path := '"' + ExpandConstant('{app}') + '"';
  if not Exec(Tool, Path + ' /setowner "*S-1-5-32-544" /T /Q', '', SW_HIDE, ewWaitUntilTerminated, Code) then RaiseException('Cannot secure application ownership');
  if Code <> 0 then RaiseException('Application ownership could not be secured');
  if not Exec(Tool, Path + ' /reset /T /Q', '', SW_HIDE, ewWaitUntilTerminated, Code) then RaiseException('Cannot reset application permissions');
  if Code <> 0 then RaiseException('Application permission reset failed');
  if not Exec(Tool, Path + ' /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" /Q', '', SW_HIDE, ewWaitUntilTerminated, Code) then RaiseException('Cannot protect application files');
  if Code <> 0 then RaiseException('Application protection failed');
  { Protect the root, then let children inherit it. Removing inheritance recursively
    would leave files with no applicable container-only access rules. }
  if not Exec(Tool, '"' + ExpandConstant('{app}') + '\*" /reset /T /Q', '', SW_HIDE, ewWaitUntilTerminated, Code) then RaiseException('Cannot secure inherited application permissions');
  if Code <> 0 then RaiseException('Application inheritance could not be secured');
end;
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    SaveStringToFile(ExpandConstant('{app}\installed.marker'), '{#AppVer}', False); ProtectInstalledFiles();
    if ServiceRegistered() and not ServiceOperation('/restart-if-enabled') then RaiseException('Boot monitoring could not restart; data was preserved.');
  end;
end;
function InitializeUninstall(): Boolean;
begin
  DeleteData := False;
  Result := CloseRaider();
  if not Result then begin SuppressibleMsgBox('Cylon Raider is still running. Exit it and retry uninstall.', mbError, MB_OK, IDOK); exit; end;
  if UninstallSilent then DeleteData := ExpandConstant('{param:DELETEUSERDATA|0}') = '1'
  else begin
    if MsgBox('Preserve your existing Cylon Raider logs and settings? Choose Yes to preserve (recommended/default).', mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
      DeleteData := MsgBox('Permanently delete this Windows account''s Cylon Raider history and settings? This cannot be undone.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;
#include "StorageGuard.iss"
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Data: String;
begin
  if CurUninstallStep = usUninstall then begin
    if DeleteData and not ServiceOperation('/delete-personal-data') then RaiseException('Custom personal data could not be safely deleted; files were preserved for review.');
    if ServiceRegistered() and not ServiceOperation('/remove') then RaiseException('Boot monitoring is still running; application files were preserved.');
    if DeleteData and FileExists(ExpandConstant('{commonappdata}\EirenAndI-CylonRaider\boot-service.json')) then
      if not ServiceOperation('/delete-data') then RaiseException('Protected boot data could not be deleted; no unsafe cleanup was attempted.');
  end;
  if CurUninstallStep = usPostUninstall then begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EirenCylonRaider');
    if DeleteData then begin
      Data := ExpandConstant('{localappdata}\EirenAndI\CylonRaider');
      Log('Raider personal data directory candidate: ' + Data);
      if SafeDirectory(Data) then begin
        { Delete only known app-owned files. Never recursively delete a user-writable tree. }
        DeleteFile(Data + '\history.db'); DeleteFile(Data + '\history.db-wal'); DeleteFile(Data + '\history.db-shm'); DeleteFile(Data + '\history.db.pre-schema2.bak');
        DeleteFile(Data + '\options.json'); DeleteFile(Data + '\options.json.bak'); DeleteFile(Data + '\options.json.tmp');
        RemoveDir(Data);
      end else NotifyPersonalDataPreserved();
    end;
  end;
end;
