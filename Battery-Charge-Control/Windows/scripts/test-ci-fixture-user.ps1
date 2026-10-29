# Runs ONLY as the temporary non-administrator; never launches any application payload.
param(
  [Parameter(Mandatory)][string]$StageDirectory,
  [Parameter(Mandatory)][string]$ReportDirectory,
  [Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture,
  [Parameter(Mandatory)][ValidatePattern('^S-1-5-21-[0-9-]+$')][string]$ExpectedUserSid
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ci-fixture-gate.ps1')
. (Join-Path $PSScriptRoot 'ci-fixture-profile.ps1')
. (Join-Path $PSScriptRoot 'ci-fixture-diagnostics.ps1')
$manifest=$null
$checks = [ordered]@{}
foreach ($name in @('Ordinary user','Profile initialization','Fresh install','Registration and shortcuts','Modify add desktop',
    'Modify remove desktop','Repair missing payload','Uninstall and cleanup','Reinstall','Second cleanup')) {
  $checks[$name] = [ordered]@{Name=$name;Status='NotRun';Detail=''}
}
$commands = [Collections.Generic.List[object]]::new()
$failed = $false
$attempted = $false
$uninstallerHash = $null
$identity = $null
$sid = $null
$isAdministrator = $null
$profileInitialization = [ordered]@{Status='NotRun'; InitializedDefaults=@();PathAuthority='AuthenticatedSidProfileList';RegistrationReadback='NotRun';ReadbackContext='NotRun'}
$diagnostics=[Collections.Generic.List[object]]::new()
$phase='Approval verification'
$folder=$null; $group=$null; $desktop=$null; $uninstaller=$null
function Require($Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Check([string]$Name,[scriptblock]$Action) {
  $script:phase=$Name
  try { & $Action; $checks[$Name].Status='Passed' }
  catch {
    $failure=Get-CiFixtureFailureDiagnostic -Record $_ -Phase $Name
    $checks[$Name].Status='Failed';$checks[$Name].Detail="$($failure.Code) at $($failure.Script):$($failure.Line)";throw
  }
}
function Initialize-WorkerProfile {
  # -LoadUserProfile loads the hive; it does not make inherited environment or shell folders reliable.
  # Use the exact authenticated SID's OS profile record, never the runner's USERPROFILE or a guessed C:\Users name.
  $profilePath=[Microsoft.Win32.Registry]::GetValue(
    'HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$sid,'ProfileImagePath',$null)
  # This is a newly created test account. Bootstrap from its OS profile record only.
  # Ambient GetFolderPath results can reflect pre-initialization shell state and are NOT authority.
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $profilePath
  $profileRoot=$plan.Paths.UserProfile
  if (-not (Test-Path -LiteralPath $profileRoot -PathType Container) -or
      -not (Test-Path -LiteralPath ('Registry::HKEY_USERS\'+$sid))) { throw 'BCC_PROFILE_NOT_LOADED' }
  $env:USERPROFILE=$profileRoot
  $env:HOMEDRIVE=[IO.Path]::GetPathRoot($profileRoot).TrimEnd('\')
  $env:HOMEPATH=$profileRoot.Substring($env:HOMEDRIVE.Length)
  $env:LOCALAPPDATA=$plan.Paths.LocalApplicationData
  $env:APPDATA=$plan.Paths.ApplicationData
  $env:TEMP=$plan.Paths.Temp
  $env:TMP=$plan.Paths.Temp
  $plan=Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {
    param($path)
    # Reject profile/ancestor junctions before and after creation; never traverse to another user's directory.
    foreach ($pass in 1..2) {
      $ancestor=$path
      while ($ancestor -and $ancestor.Length -ge $profileRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
          throw 'BCC_PROFILE_REPARSE_POINT'
        }
        $ancestor=[IO.Path]::GetDirectoryName($ancestor)
      }
      if ($pass -eq 1) { [void][IO.Directory]::CreateDirectory($path) }
    }
  } -WriteShellFolders {
    param($values)
    # Initialize only this new standard account's known-folder registration, so Inno resolves the same paths.
    $hive=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid,$true)
    if ($null -eq $hive) { throw 'BCC_PROFILE_HIVE_UNAVAILABLE' }
    $key=$null
    try {
      $key=$hive.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders')
      foreach ($entry in $values.GetEnumerator()) { $key.SetValue($entry.Key,$entry.Value,[Microsoft.Win32.RegistryValueKind]::ExpandString) }
    } finally { if ($null -ne $key) { $key.Dispose() };$hive.Dispose() }
  } -ReadShellFolders { $profileInitialization.ReadbackContext='SidHive';Read-WorkerShellFolders -ForAuthenticatedSid }
  # The installer uses the current-user hive. Require it to agree with the SID-bound readback;
  # do not assume that a process/environment change repaired a different or cached user context.
  $profileInitialization.ReadbackContext='CurrentUser'
  $currentUserRegistration=Read-WorkerShellFolders
  Assert-CiFixtureProfileRegistration -Plan $plan -RegisteredFolders $currentUserRegistration
  $profileInitialization.RegistrationReadback='Verified'
  $profileInitialization.ReadbackContext='BothVerified'
  return $plan
}
function Read-WorkerShellFolders {
  param([switch]$ForAuthenticatedSid)
  $key=$null
  $hive=$null
  try {
    if ($ForAuthenticatedSid) {
      $hive=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid)
      if ($null -eq $hive) { throw 'BCC_PROFILE_HIVE_UNAVAILABLE' }
    } else { $hive=[Microsoft.Win32.Registry]::CurrentUser }
    $key=$hive.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders')
    if ($null -eq $key) { throw 'BCC_PROFILE_REGISTRATION_UNAVAILABLE' }
    $values=@{}
    $names=@{LocalApplicationData='Local AppData';ApplicationData='AppData';Programs='Programs';DesktopDirectory='Desktop';StartMenu='Start Menu'}
    foreach ($entry in $names.GetEnumerator()) {
      # Initialization writes absolute SID-local values. Do not expand inherited environment input here.
      $values[$entry.Key]=$key.GetValue($entry.Value,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
    return $values
  } finally {
    if ($null -ne $key) { $key.Dispose() }
    if ($ForAuthenticatedSid -and $null -ne $hive) { $hive.Dispose() }
  }
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A57C22D3-BE65-48A8-BC98-BC94AE2AC101}_is1'
function Run-Fixture([bool]$Uninstall,[string]$TaskOption='') {
  $file = $setup
  if ($Uninstall) {
    Require ($null -ne $script:uninstallerHash) 'Uninstaller was not observed in a verified fixture installation'
    Require ((Get-FileHash -LiteralPath $uninstaller).Hash -eq $script:uninstallerHash) 'Uninstaller changed unexpectedly'
    $file = $uninstaller
  } else {
    Require ((Get-FileHash -LiteralPath $setup).Hash -eq $manifest.InstallerSha256) 'Installer hash changed'
  }
  $log = Join-Path $ReportDirectory ("inno-$($commands.Count+1).log")
  $info = [Diagnostics.ProcessStartInfo]::new($file)
  $info.UseShellExecute=$false
  foreach ($argument in @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG='+$log))) {
    $info.ArgumentList.Add($argument)
  }
  if ($TaskOption) {
    Require ($TaskOption -cin @('/TASKS=desktopicon','/TASKS=')) 'Unexpected fixture task option'
    $info.ArgumentList.Add($TaskOption)
  }
  $record = [ordered]@{Executable=$(if ($Uninstall) {'CI fixture uninstaller'} else {$manifest.InstallerName});
    TaskOption=$TaskOption;ExitCode=$null;Log=[IO.Path]::GetFileName($log);Status='Failed'}
  $commands.Add($record)
  $process = [Diagnostics.Process]::Start($info)
  try {
    if (-not $process.WaitForExit(90000)) { $process.Kill($true);throw 'Fixture process timeout; no success assumed' }
    $record.ExitCode=$process.ExitCode
    Require ($process.ExitCode -eq 0) "Fixture process failed: exit code $($process.ExitCode)"
    Require (Test-Path -LiteralPath $log) 'Inno diagnostic log missing'
    $record.Status='Passed'
  } finally { $process.Dispose() }
}
function Verify-Link([string]$Path) {
  Require (Test-Path -LiteralPath $Path) 'Fixture shortcut missing'
  $shell = New-Object -ComObject WScript.Shell
  $link = $null
  try {
    $link = $shell.CreateShortcut($Path)
    Require ($link.TargetPath -eq $exe -and [string]::IsNullOrEmpty($link.Arguments)) 'Shortcut target/arguments outside fixture'
  } finally {
    if ($null -ne $link) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
  }
}
function Verify-Installed([bool]$DesktopExpected) {
  Require ((Get-FileHash -LiteralPath $exe).Hash -eq $manifest.PayloadSha256) 'Installed payload differs from inspected no-firmware payload'
  $files = @(Get-ChildItem -LiteralPath $folder -Recurse -File | ForEach-Object Name | Sort-Object)
  Require (($files -join '|') -ceq 'BCC.NoFirmwarePreview.exe|unins000.dat|unins000.exe') 'Unexpected installed payload/controller/files'
  Require (@(Get-ChildItem -LiteralPath $folder -Directory).Count -eq 0) 'Unexpected installed directories'
  $script:uninstallerHash = (Get-FileHash -LiteralPath $uninstaller).Hash
  $entry = Get-ItemProperty -LiteralPath $key
  Require ($entry.DisplayName -ceq 'BCC CI No-Firmware Fixture' -and $entry.DisplayVersion -ceq '0.1.0') 'Installed apps registration mismatch'
  Require ($entry.UninstallString -ceq ('"'+$uninstaller+'"')) 'Uninstall registration points outside fixture'
  Require ($entry.QuietUninstallString -ceq ('"'+$uninstaller+'" /SILENT')) 'Quiet uninstall registration mismatch'
  $links = @(Get-ChildItem -LiteralPath $group -Recurse -File)
  Require ($links.Count -eq 1 -and $links[0].Name -ceq 'BCC CI No-Firmware Fixture.lnk') 'Unexpected Start menu contents'
  Verify-Link $links[0].FullName
  Require ((Test-Path -LiteralPath $desktop) -eq $DesktopExpected) 'Optional desktop selection mismatch'
  if ($DesktopExpected) { Verify-Link $desktop }
}
function Verify-Clean {
  for ($i=0; $i -lt 40; $i++) {
    if (-not ((Test-Path $folder) -or (Test-Path $group) -or (Test-Path $desktop) -or (Test-Path $key))) { return }
    Start-Sleep -Milliseconds 250
  }
  throw 'Fixture files, shortcuts or Installed apps registration remain after uninstall'
}
try {
  Assert-CiFixtureApproval $Architecture
  $phase='Manifest verification'
  $manifest = Read-CiFixtureManifest $StageDirectory $Architecture
  $setup = Join-Path $StageDirectory $manifest.InstallerName
  Check 'Ordinary user' {
    $script:identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $script:sid = $identity.User.Value
    Require ($sid -ceq $ExpectedUserSid) 'Worker identity is not the newly created test account'
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $script:isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Require (-not $isAdministrator) 'Worker is elevated'
    Require ('S-1-5-32-544' -notin @($identity.Groups | ForEach-Object Value)) 'Filtered administrator is not an ordinary user'
  }
  Check 'Profile initialization' {
    $plan=Initialize-WorkerProfile
    $script:folder=Join-Path $plan.Paths.LocalApplicationData 'BCC CI No-Firmware Fixture'
    $script:group=Join-Path $plan.Paths.Programs 'BCC CI No-Firmware Fixture'
    $script:desktop=Join-Path $plan.Paths.DesktopDirectory 'BCC CI No-Firmware Fixture.lnk'
    $script:exe=Join-Path $folder 'BCC.NoFirmwarePreview.exe'
    $script:uninstaller=Join-Path $folder 'unins000.exe'
    $profileInitialization.Status='Passed'
    $profileInitialization.InitializedDefaults=$plan.MissingKnownFolders
    Verify-Clean
  }
  Check 'Fresh install' { $script:attempted=$true; Run-Fixture $false; Verify-Installed $false }
  Check 'Registration and shortcuts' { Verify-Installed $false }
  Check 'Modify add desktop' { Run-Fixture $false '/TASKS=desktopicon';Verify-Installed $true }
  Check 'Modify remove desktop' { Run-Fixture $false '/TASKS=';Verify-Installed $false }
  Check 'Repair missing payload' {
    Remove-Item -LiteralPath $exe # Controlled fixture file loss, not crash/interruption testing.
    Run-Fixture $false '/TASKS=';Verify-Installed $false
  }
  Check 'Uninstall and cleanup' { Run-Fixture $true;Verify-Clean }
  Check 'Reinstall' { Run-Fixture $false;Verify-Installed $false }
  Check 'Second cleanup' { Run-Fixture $true;Verify-Clean }
} catch {
  $failed=$true
  if ($phase -eq 'Profile initialization') { $profileInitialization.Status='Failed' }
  $failure=Get-CiFixtureFailureDiagnostic -Record $_ -Phase $phase
  $diagnostics.Add($failure)
  Write-Host "FAIL: $($failure.Phase): $($failure.Code) at $($failure.Script):$($failure.Line) [$($failure.ExceptionType)]"
}
finally {
  # Best-effort fixture-only recovery cannot turn a failed check into a passing test.
  if ($attempted -and (Test-Path $uninstaller)) {
    try { Run-Fixture $true;Verify-Clean }
    catch { $failed=$true;$diagnostics.Add((Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Fixture cleanup')) }
  }
  if (@($checks.Values | Where-Object { $_.Status -ne 'Passed' }).Count -ne 0) { $failed=$true }
  $status=if ($failed) {'Failed'} else {'Passed'}
  [ordered]@{Status=$status;SourceCommit=$env:GITHUB_SHA;Architecture=$Architecture;UserSid=$sid;
    IsAdministrator=$isAdministrator;ProfileInitialization=$profileInitialization;Diagnostics=$diagnostics.ToArray();Checks=@($checks.Values);Commands=$commands.ToArray();
    Limits='CI no-firmware fixture only. No payload/production GUI launch, service registration, firmware, real interruption or physical compatibility test.'} |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ReportDirectory 'results.json') -Encoding utf8
  $lines=@("# CI no-firmware fixture: $Architecture — $status",'',"Source: $env:GITHUB_SHA",'',
    '| Check | Result | Detail |','|---|---|---|')
  foreach ($check in $checks.Values) { $lines += "| $($check.Name) | $($check.Status) | $($check.Detail.Replace('|','/')) |" }
  $lines += @('','Fixture lifecycle only; production installers, GUI, services and battery hardware are excluded.',
    'Missing-payload repair is not genuine process/power interruption testing. ARM64 is not x64 Windows 11 coverage.')
  $lines | Set-Content (Join-Path $ReportDirectory 'results.md') -Encoding utf8
  if ($null -ne $identity) { $identity.Dispose() }
}
if ($failed) { exit 1 }
exit 0
