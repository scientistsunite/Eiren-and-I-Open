; Enforce compiler version from the compiler itself, not the ISCC executable resource.
#if VER < EncodeVer(6, 7, 1) || VER >= EncodeVer(6, 7, 2)
  #error Reproducible builds require Inno Setup 6.7.1.
#endif
; Battery Charge Control for Windows — x64 preview installer (Inno Setup 6.4+)
; The universal installer recognizes 15 vendors. v0.1 ships the ASUS hardware controller only.
#define MyAppName "Battery Charge Control"
#define MyVersion "0.1.0"
#define MyExe "BatteryChargeControl.exe"

[Setup]
AppId={{4E1E310B-AC37-4E62-928A-63E4E1271001}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppPublisher=Battery Charge Control
DefaultDirName={autopf}\Battery Charge Control
DefaultGroupName=Battery Charge Control
UninstallDisplayName=Battery Charge Control
UninstallDisplayIcon={app}\{#MyExe}
OutputDir=..\artifacts
OutputBaseFilename=Battery-Charge-Control-Windows-v0.1-Setup-x64
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
SetupIconFile=..\src\BCC.App\app.ico
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "agent"; Description: "Install optional boot/shutdown restoration agent (recommended on supported ASUS hardware)"; GroupDescription: "Charging persistence:"; Check: IsAsusSelected

[Files]
; Reuse this single packaged executable for temporary maintenance and normal installation.
Source: "..\artifacts\publish\win-x64\BatteryChargeControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\publish\win-x64\Controllers\ASUS\BCC.Controller.Asus.dll"; DestDir: "{tmp}"; DestName: "BCCMaintenance.Asus.dll"; Flags: dontcopy
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Excludes: "BatteryChargeControl.exe,Controllers\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\win-x64\Controllers\ASUS\*"; DestDir: "{app}\Controllers\ASUS"; Check: IsAsusSelected; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Discard previously selected optional controller when switching to monitoring-only installation.
Type: files; Name: "{app}\Controllers\ASUS\BCC.Controller.Asus.dll"
; Remove only this app's deselected shortcut, after maintenance preparation has succeeded.
Type: files; Name: "{autodesktop}\Battery Charge Control.lnk"

[Icons]
Name: "{group}\Battery Charge Control"; Filename: "{app}\{#MyExe}"
Name: "{autodesktop}\Battery Charge Control"; Filename: "{app}\{#MyExe}"; Tasks: desktopicon
; No uninstaller Start menu shortcut. The app has an in-app Uninstall action.

[Run]
Filename: "{app}\{#MyExe}"; Description: "Launch Battery Charge Control"; Flags: nowait postinstall skipifsilent; Check: IsInstallationComplete

; No UninstallRun cleanup: InitializeUninstall must verify recovery/service removal BEFORE deletion.

[Code]
var
  InstallationComplete: Boolean;
  VendorPage: TWizardPage;
  VendorLabel: TNewStaticText;
  VendorChoice: TNewComboBox;
  DisclosurePage: TWizardPage;
  DisclosureText: TNewMemo;
  Agreed: TNewCheckBox;

function IsInstallationComplete(): Boolean;
begin
  Result := InstallationComplete;
end;

function GetCustomSetupExitCode(): Integer;
begin
  Result := 0;
  if not InstallationComplete then Result := 10;
end;

function ReadMaker(): String;
begin
  Result := '';
  RegQueryStringValue(HKLM, 'HARDWARE\DESCRIPTION\System\BIOS', 'SystemManufacturer', Result);
end;

function IsAsusMachine(): Boolean;
var s: String;
begin
  s := Uppercase(ReadMaker());
  Result := (Pos('ASUS', s) > 0) or (Pos('ASUSTEK', s) > 0);
end;

function IsAsusSelected(): Boolean;
begin
  Result := ((VendorChoice.ItemIndex = 0) and IsAsusMachine()) or
     ((VendorChoice.ItemIndex = 2) and IsAsusMachine());
end;

procedure InitializeWizard();


begin
  VendorPage := CreateCustomPage(wpWelcome, 'Hardware detection', 'Choose your computer manufacturer.');
  VendorLabel := TNewStaticText.Create(VendorPage);
  VendorLabel.Parent := VendorPage.Surface;
  VendorLabel.Top := ScaleY(8);
  VendorLabel.Left := ScaleX(0);
  VendorLabel.AutoSize := True;
  VendorLabel.Caption := 'Detected: ' + ReadMaker() + #13#10 +
    'Automatic is recommended. v0.1 controls ASUS only; other brands use monitoring.';
  VendorChoice := TNewComboBox.Create(VendorPage);
  VendorChoice.Parent := VendorPage.Surface;
  VendorChoice.Left := ScaleX(0);
  VendorChoice.Top := VendorLabel.Top + ScaleY(50);
  VendorChoice.Width := ScaleX(330);
  VendorChoice.Style := csDropDownList;
  VendorChoice.Items.Add('Automatic (recommended)');
  VendorChoice.Items.Add('Monitoring only (no hardware controller)');
  VendorChoice.Items.Add('ASUS');
  VendorChoice.Items.Add('Lenovo');
  VendorChoice.Items.Add('HP');
  VendorChoice.Items.Add('Dell');
  VendorChoice.Items.Add('Acer');
  VendorChoice.Items.Add('MSI');
  VendorChoice.Items.Add('Samsung');
  VendorChoice.Items.Add('Microsoft Surface');
  VendorChoice.Items.Add('LG');
  VendorChoice.Items.Add('Huawei');
  VendorChoice.Items.Add('Xiaomi');
  VendorChoice.Items.Add('Razer');
  VendorChoice.Items.Add('Gigabyte');
  VendorChoice.Items.Add('Framework');
  VendorChoice.Items.Add('Dynabook');
  VendorChoice.ItemIndex := 0;

  DisclosurePage := CreateCustomPage(VendorPage.ID, 'Important charging-control information',
    'Please read this before installing hardware charging features.');
  DisclosureText := TNewMemo.Create(DisclosurePage);
  DisclosureText.Parent := DisclosurePage.Surface;
  DisclosureText.Top := ScaleY(4);
  DisclosureText.Left := ScaleX(0);
  DisclosureText.Width := DisclosurePage.SurfaceWidth;
  DisclosureText.Height := ScaleY(190);
  DisclosureText.ScrollBars := ssVertical;
  DisclosureText.ReadOnly := True;
  DisclosureText.Text :=
    'The charging limit is saved for this Windows installation across sign-ins, restarts and shutdowns.'+#13#10+#13#10+
    'When supported and installed, a small agent will ATTEMPT to restore the original firmware setting at normal Windows shutdown and reapply the saved limit on the next boot.'+#13#10+#13#10+
    'IMPORTANT: This cannot be guaranteed when starting Linux or another Windows installation, or after a crash, power loss, hibernation, Fast Startup or firmware/driver limitations. The firmware limit may persist on another boot source. Windows software cannot run after another operating system has taken over.'+#13#10+#13#10+
    'If the original configuration cannot be reliably identified, charging-control writes are blocked. Manual Restore and Prepare for Another OS options are available when supported.'+#13#10+#13#10+
    'ASUS control is experimental and unverified for your exact computer. Firmware readback does not establish physical battery behavior.';
  Agreed := TNewCheckBox.Create(DisclosurePage);
  Agreed.Parent := DisclosurePage.Surface;
  Agreed.Top := DisclosureText.Top + DisclosureText.Height + ScaleY(12);
  Agreed.Left := ScaleX(0);
  Agreed.Width := DisclosurePage.SurfaceWidth;
  Agreed.Caption := 'I understand the limitations of cross-boot charging restoration.';
  Agreed.Checked := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = DisclosurePage.ID) and IsAsusSelected() and not Agreed.Checked then
  begin
    MsgBox('Please read and acknowledge the limitations before installing charging controls. ' +
       'You may instead use monitoring-only installation.', mbInformation, MB_OK);
    Result := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DisclosurePage.ID then
    Agreed.Visible := IsAsusSelected();
end;

function RunMaintenance(const Executable, Parameters: String): Boolean;
var ResultCode: Integer;
begin
  Result := False;
  if not FileExists(Executable) then exit;
  if not Exec(Executable, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then exit;
  Result := ResultCode = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Helper, ControllerDir, InstalledExe: String;
begin
  Result := 'Maintenance was not completed. No application files may be removed or replaced. ' +
    'Original settings and recovery information are retained. Retry Setup/Repair; inspect the agent log. ' +
    'The recovery service may be stopped if restarting it failed.';
  if WizardSilent then begin
    Result := 'Silent production installation/repair is disabled: interactive maintenance consent is required.';
    exit;
  end;
  if MsgBox('Setup/Repair will pause charging changes, stop the existing agent and, if control is active, ' +
    'attempt verified restoration before removing optional components. If recovery or service removal fails, ' +
    'Setup will stop and retain recovery files. Cancellation after preparation leaves charging paused; ' +
    'rerun Setup/Repair to finish. Continue?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then exit;
  ExtractTemporaryFile('BatteryChargeControl.exe');
  ExtractTemporaryFile('BCCMaintenance.Asus.dll');
  ControllerDir := ExpandConstant('{tmp}\Controllers\ASUS');
  if not ForceDirectories(ControllerDir) then exit;
  if not CopyFile(ExpandConstant('{tmp}\BCCMaintenance.Asus.dll'), ControllerDir + '\BCC.Controller.Asus.dll', False) then exit;
  Helper := ExpandConstant('{tmp}\BatteryChargeControl.exe');
  InstalledExe := ExpandConstant('{app}\{#MyExe}');
  if RunMaintenance(Helper, '--prepare-maintenance "' + InstalledExe + '"') then Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Parameters: String;
begin
  if CurStep = ssPostInstall then begin
    Parameters := '--finish-install-monitoring';
    if IsAsusSelected() and WizardIsTaskSelected('agent') then Parameters := '--finish-install-agent';
    if not RunMaintenance(ExpandConstant('{app}\{#MyExe}'), Parameters) then
      RaiseException('Installation did not complete. Charging remains paused and recovery information is retained. ' +
        'Do not delete recovery files. Retry Setup/Repair and inspect the agent log.');
    InstallationComplete := True;
  end;
end;

function InitializeUninstall(): Boolean;
var Executable: String;
begin
  Result := False;
  if UninstallSilent then begin
    Log('Silent production uninstall refused: interactive recovery consent is required.');
    exit;
  end;
  if MsgBox('Before removal, charging changes will be paused and any active charging policy must be restored ' +
    'with matching readback. Failure stops removal and keeps the recovery program, controller and original policy. ' +
    'If you cancel later, charging stays paused; rerun Setup/Repair to finish. ' +
    'Saved recovery information is retained after uninstall. Continue?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then exit;
  Executable := ExpandConstant('{app}\{#MyExe}');
  Result := RunMaintenance(Executable, '--prepare-maintenance "' + Executable + '"');
  if not Result then
    MsgBox('Uninstallation stopped: restoration, policy persistence or service removal could not be verified. ' +
      'Recovery files and original configuration have NOT been intentionally removed. The service may be stopped ' +
      'if restart failed. Do not manually delete files. Use Restore or rerun the trusted Setup/Repair installer to retry. ' +
      'Inspect the agent log in ProgramData\Battery Charge Control.', mbError, MB_OK);
end;
