# PREPARED, NOT EXECUTED. Review safeguards first; disposable Windows 11 VM only.
# Runs ONLY the separately compiled no-firmware installer. Never launches a production app.
param(
  [Parameter(Mandatory)][string]$Installer,
  [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedSha256,
  [Parameter(Mandatory)][string]$ReviewRecord,
  [switch]$SafeguardsReviewed,
  [switch]$DisposableVmAcknowledged
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -eq 'true' -or $env:CI -eq 'true') { throw 'Installer execution is prohibited in CI.' }
if (-not $SafeguardsReviewed -or -not $DisposableVmAcknowledged) { throw 'Separate safeguard review and disposable-VM acknowledgment required.' }
if (-not [OperatingSystem]::IsWindowsVersionAtLeast(10, 0, 22000)) { throw 'Disposable Windows 11 VM required.' }
# The record is evidence supplied by an independent review, not VM attestation by this script.
$review = Get-Content -LiteralPath $ReviewRecord -Raw | ConvertFrom-Json
if ($review.SafeguardsApproved -ne $true -or $review.NoHardwarePassthrough -ne $true -or
    $review.DisposableVm -ne $true -or [string]::IsNullOrWhiteSpace($review.SnapshotId) -or
    [string]::IsNullOrWhiteSpace($review.IndependentReviewer) -or
    $review.IndependentReviewer -eq [Environment]::UserName -or
    $review.SourceCommit -notmatch '^[0-9a-fA-F]{40}$' -or
    [DateTimeOffset]::Parse($review.ExpiresUtc) -le [DateTimeOffset]::UtcNow) {
  throw 'Independent, current safeguard/snapshot review is missing or incomplete.'
}
$os = Get-CimInstance Win32_OperatingSystem
if ($os.ProductType -ne 1 -or $os.Caption -notmatch 'Windows 11') { throw 'Windows 11 client VM required; Windows Server runners do not qualify.' }
$vm = Get-CimInstance Win32_ComputerSystemProduct
if ($review.VmUuid -ne $vm.UUID -or $review.InstallerSha256 -ne $ExpectedSha256) {
  throw 'Review record does not match this VM identity and reviewed installer hash.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
  if (([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Use a standard-user, non-elevated PowerShell session.'
  }
} finally { $identity.Dispose() }
$setup = (Resolve-Path -LiteralPath $Installer).Path
if ((Split-Path -Leaf $setup) -ne 'BCC-NoFirmware-MonitoringOnly-Test-Setup.exe') { throw 'Only the separate simulation fixture is accepted.' }
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Installer hash mismatch.' }
$folder = Join-Path $env:LOCALAPPDATA 'BCC No-Firmware Simulation Preview'
$group = Join-Path ([Environment]::GetFolderPath('Programs')) 'BCC No-Firmware Simulation Preview'
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BCC No-Firmware Simulation Preview.lnk'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{BB4BCC13-06A6-482B-A914-3FA0D38E6E0F}_is1'
if ((Test-Path $folder) -or (Test-Path $group) -or (Test-Path $desktop) -or (Test-Path $key)) {
  throw 'Fixture state already exists. Revert VM snapshot; do not overwrite another installation.'
}
function Assert-True($condition, $message) { if (-not $condition) { throw $message } }
function Install-Fixture([string]$taskOption) {
  # Constant arguments only; no /DIR or executable command injection from caller inputs.
  $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', $taskOption) -Wait -PassThru
  Assert-True ($process.ExitCode -eq 0) 'Simulation installer failed.'
}
$installed = $false
try {
  # Mark before invoking so cleanup is attempted after a partial install too.
  $installed = $true
  Install-Fixture '/TASKS=""'
  Assert-True (Test-Path (Join-Path $folder 'BCC.NoFirmwarePreview.exe')) 'Missing simulation payload.'
  Assert-True (-not (Test-Path (Join-Path $folder 'Controllers'))) 'Unexpected controller directory.'
  Assert-True (Test-Path $key) 'Missing HKCU Installed apps entry.'
  Assert-True (-not (Test-Path $desktop)) 'Desktop shortcut must default off.'
  $links = @(Get-ChildItem -LiteralPath $group -Filter '*.lnk')
  Assert-True ($links.Count -eq 1 -and $links[0].BaseName -eq 'BCC No-Firmware Simulation Preview') 'Unexpected Start menu shortcut(s).'
  $shell = New-Object -ComObject WScript.Shell
  try {
    $shortcut = $shell.CreateShortcut($links[0].FullName)
    Assert-True ($shortcut.TargetPath -eq (Join-Path $folder 'BCC.NoFirmwarePreview.exe')) 'Start menu shortcut points outside fixture.'
  } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
  $entry = Get-ItemProperty -LiteralPath $key
  Assert-True ($entry.DisplayName -eq 'BCC No-Firmware Simulation Preview') 'Wrong Installed apps display name.'
  Assert-True ($entry.UninstallString -like ('*' + $folder + '*')) 'Uninstaller path points outside fixture.'
  Install-Fixture '/TASKS="desktopicon"' # Modify: add optional desktop icon.
  Assert-True (Test-Path $desktop) 'Desktop shortcut missing after modification.'
  Remove-Item -LiteralPath (Join-Path $folder 'BCC.NoFirmwarePreview.exe') # Simulated damaged fixture only.
  Install-Fixture '/TASKS=""' # Repair payload and remove deselected fixture shortcut.
  Assert-True (Test-Path (Join-Path $folder 'BCC.NoFirmwarePreview.exe')) 'Repair did not replace fixture payload.'
  Assert-True (-not (Test-Path $desktop)) 'Deselected fixture shortcut was retained.'
} finally {
  if ($installed -and (Test-Path (Join-Path $folder 'unins000.exe'))) {
    $process = Start-Process -FilePath (Join-Path $folder 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    Assert-True ($process.ExitCode -eq 0) 'Simulation uninstaller failed; revert VM snapshot.'
  }
}
Assert-True (-not (Test-Path $key)) 'Installed apps key remains.'
Assert-True (-not (Test-Path $group)) 'Start menu group remains.'
Assert-True (-not (Test-Path $desktop)) 'Desktop shortcut remains.'
Assert-True (-not (Test-Path $folder)) 'Fixture files remain.'
# Fresh reinstallation after complete cleanup; still never launch the preview automatically.
try {
  $installed = $true
  Install-Fixture '/TASKS=""'
  Assert-True (Test-Path $key) 'Reinstallation did not recreate Installed apps registration.'
  Assert-True (Test-Path (Join-Path $folder 'BCC.NoFirmwarePreview.exe')) 'Reinstallation payload missing.'
  Assert-True (-not (Test-Path $desktop)) 'Reinstallation must retain default-off desktop choice.'
} finally {
  if (Test-Path (Join-Path $folder 'unins000.exe')) {
    $process = Start-Process -FilePath (Join-Path $folder 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    Assert-True ($process.ExitCode -eq 0) 'Reinstallation cleanup failed; revert snapshot.'
  }
}
Assert-True (-not (Test-Path $folder) -and -not (Test-Path $key) -and -not (Test-Path $group) -and -not (Test-Path $desktop)) 'Reinstallation cleanup incomplete.'
Write-Host 'PASS: simulation-only install, modify, repair, shortcuts, HKCU registration, uninstall and reinstallation cleanup.'
Write-Host 'Controlled missing-payload repair was tested; genuine process/power interruption and GUI checks remain separate manual cases.'
Write-Host 'Production installer, GUI, service and battery hardware were NOT tested. Revert VM snapshot.'
