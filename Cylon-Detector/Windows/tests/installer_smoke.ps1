<#
Disposable GitHub-hosted Windows installer lifecycle fixture only.
Launches the normal UI with fresh defaults (AutoMonitor=false). Never passes
--offline-smoke, starts ETW, enables a sensor, or registers a boot service.
No application data, window content, screenshots or machine inventory is emitted.
#>
[CmdletBinding()]
param(
    [string]$Installer = (Join-Path $PSScriptRoot '../release-assets/CylonDetector-Setup-0.1.0-initial.exe'),
    [switch]$BlockOutbound
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Installer fixture requires a disposable GitHub-hosted Windows runner; ordinary desktops and self-hosted runners are refused.'
}
$Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$Principal = [Security.Principal.WindowsPrincipal]::new($Identity)
if (!$Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The disposable hosted installer fixture requires its existing administrator token; no UAC automation is attempted.'
}
$Identity.Dispose()

$DetectorKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1D725EE6-2618-469E-A3DF-D25A568FCF38}_is1'
$DetectorService = 'HKLM:\SYSTEM\CurrentControlSet\Services\EirenCylonDetectorV01'
$InstallRoot = Join-Path $env:ProgramFiles 'Cylon Detector'
$ProfileRoot = Join-Path $env:LOCALAPPDATA 'EirenAndI/CylonDetector'
$BootRoot = Join-Path $env:ProgramData 'EirenAndI-CylonDetector'
$Shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Cylon Detector.lnk'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$FixtureRuleNames = [Collections.Generic.List[string]]::new()
$UiProcess = $null
$InstallAttempted = $false
$ProfileOwned = $false
$Failure = $null
$CleanupFailures = [Collections.Generic.List[string]]::new()

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Output "PASS $Message"
}

function Assert-NoLinks([string]$Path) {
    $Probe = [IO.Path]::GetFullPath($Path)
    while ($Probe) {
        if (Test-Path -LiteralPath $Probe) {
            if (((Get-Item -LiteralPath $Probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Fixture path contains a reparse point; operation refused.'
            }
        }
        $Parent = [IO.Path]::GetDirectoryName($Probe)
        if ($Parent -eq $Probe) { break }
        $Probe = $Parent
    }
}

function Remove-OwnedTree([string]$Path, [string]$ExpectedPath) {
    if (![IO.Path]::GetFullPath($Path).Equals([IO.Path]::GetFullPath($ExpectedPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup target is outside its exact owned directory.'
    }
    if (!(Test-Path -LiteralPath $Path)) { return }
    Assert-NoLinks $Path
    $Pending = [Collections.Generic.Queue[string]]::new()
    $Pending.Enqueue($Path)
    while ($Pending.Count -gt 0) {
        foreach ($Entry in Get-ChildItem -LiteralPath $Pending.Dequeue() -Force) {
            if (($Entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Fixture cleanup refused a linked descendant.' }
            if ($Entry.PSIsContainer) { $Pending.Enqueue($Entry.FullName) }
        }
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Invoke-Installer([string]$Executable) {
    $Operation = Start-Process -FilePath $Executable -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-') -PassThru
    try {
        if (!$Operation.WaitForExit(120000)) {
            $Operation.Kill($true)
            throw 'The fixture-owned setup operation exceeded two minutes.'
        }
        if ($Operation.ExitCode -ne 0) { throw 'The fixture-owned setup operation failed.' }
    } finally { $Operation.Dispose() }
}

function Registry-Snapshot([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return 'absent' }
    $Root = Get-Item -LiteralPath $Path
    $Keys = @($Root) + @(Get-ChildItem -LiteralPath $Path -Recurse | Sort-Object Name)
    $Rows = foreach ($Key in $Keys) {
        $Values = foreach ($Name in ($Key.GetValueNames() | Sort-Object)) {
            $Value = $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            [ordered]@{ name=$Name; kind=$Key.GetValueKind($Name).ToString(); value=$Value }
        }
        [ordered]@{ key=$Key.Name; values=@($Values) }
    }
    return ConvertTo-Json -InputObject @($Rows) -Depth 12 -Compress
}

function Raider-Snapshot {
    $Uninstall = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{C6235B22-2E4E-4C4D-BA36-1B0F34E26420}_is1'
    $Service = 'HKLM:\SYSTEM\CurrentControlSet\Services\EirenCylonRaiderV02'
    $Locations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$Locations.Add((Join-Path $env:ProgramFiles 'Cylon Raider'))
    if (Test-Path -LiteralPath $Uninstall) {
        $Location = (Get-Item -LiteralPath $Uninstall).GetValue('InstallLocation', '')
        if ($Location -is [string] -and ![string]::IsNullOrWhiteSpace($Location)) { [void]$Locations.Add([IO.Path]::GetFullPath($Location)) }
    }
    $Trees = foreach ($Location in ($Locations | Sort-Object)) {
        if (!(Test-Path -LiteralPath $Location)) { [ordered]@{ location=$Location; state='absent' }; continue }
        Assert-NoLinks $Location
        $Entries = [Collections.Generic.List[object]]::new()
        $Pending = [Collections.Generic.Queue[string]]::new()
        $Pending.Enqueue($Location)
        while ($Pending.Count -gt 0) {
            $Directory = $Pending.Dequeue()
            foreach ($Entry in (Get-ChildItem -LiteralPath $Directory -Force | Sort-Object Name)) {
                if (($Entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Existing Raider installation contains a link; fixture refuses ambiguous snapshot traversal.' }
                if ($Entry.PSIsContainer) {
                    $Pending.Enqueue($Entry.FullName)
                    $Entries.Add([ordered]@{ path=[IO.Path]::GetRelativePath($Location,$Entry.FullName); directory=$true })
                } else {
                    $Entries.Add([ordered]@{ path=[IO.Path]::GetRelativePath($Location,$Entry.FullName); bytes=$Entry.Length; sha256=(Get-FileHash -LiteralPath $Entry.FullName -Algorithm SHA256).Hash })
                }
            }
        }
        [ordered]@{ location=$Location; state='present'; entries=@($Entries.ToArray()) }
    }
    # This read-only baseline stays in memory and is never printed or uploaded.
    return ConvertTo-Json -InputObject ([ordered]@{ uninstall=(Registry-Snapshot $Uninstall); service=(Registry-Snapshot $Service); installations=@($Trees) }) -Depth 20 -Compress
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DetectorInstallerFixtureWindow {
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static IntPtr Find(int processId) {
        IntPtr found=IntPtr.Zero; int matches=0;
        EnumWindows((window, parameter)=> {
            uint owner; GetWindowThreadProcessId(window,out owner);
            if(owner!=(uint)processId || !IsWindowVisible(window)) return true;
            var text=new StringBuilder(256); GetWindowText(window,text,text.Capacity);
            if(text.ToString()=="Cylon Detector v0.1") { found=window; matches++; }
            return true;
        },IntPtr.Zero);
        return matches==1 ? found : IntPtr.Zero;
    }
    public static bool Responds(IntPtr window) {
        UIntPtr ignored;
        return window!=IntPtr.Zero && SendMessageTimeout(window,0,UIntPtr.Zero,IntPtr.Zero,2,2000,out ignored)!=IntPtr.Zero;
    }
    public static bool Exit(IntPtr window) { return window!=IntPtr.Zero && PostMessage(window,0x8000+143,IntPtr.Zero,IntPtr.Zero); }
}
'@

foreach ($Path in @($InstallRoot, $ProfileRoot, $BootRoot, $Shortcut)) {
    Assert-NoLinks $Path
    if (Test-Path -LiteralPath $Path) { throw 'Existing Cylon Detector installation, profile, boot data or shortcut found; fixture refuses interference.' }
}
if ((Test-Path -LiteralPath $DetectorKey) -or (Test-Path -LiteralPath $DetectorService) -or @(Get-Process -Name 'CylonDetector', 'CylonDetector.Collector', 'CylonDetector.Service' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Existing Cylon Detector registration or process found; fixture refuses interference.'
}
if ((Test-Path -LiteralPath $RunKey) -and (Get-Item -LiteralPath $RunKey).GetValueNames().Contains('EirenCylonDetector')) {
    throw 'Existing Cylon Detector sign-in configuration found; fixture refuses interference.'
}
$RaiderBefore = Raider-Snapshot

try {
    if ($BlockOutbound) {
        if (!(Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) { throw 'Requested fixture outbound blocks are unavailable; application was not launched.' }
        foreach ($Binary in @('CylonDetector.exe', 'CylonDetector.Collector.exe', 'CylonDetector.Service.exe')) {
            $RuleName = 'DetectorInstallerFixture-' + [Guid]::NewGuid().ToString('N')
            $FixtureRuleNames.Add($RuleName)
            New-NetFirewallRule -Name $RuleName -DisplayName $RuleName -Direction Outbound -Action Block -Program (Join-Path $InstallRoot $Binary) -Profile Any -Enabled True | Out-Null
        }
    }
    $InstallAttempted = $true
    Invoke-Installer $Installer
    Assert-True ((Test-Path -LiteralPath $DetectorKey) -and (Test-Path -LiteralPath (Join-Path $InstallRoot 'installed.marker'))) 'Detector registers its independent application identity in the normal installation directory.'
    Assert-True (Test-Path -LiteralPath $Shortcut) 'Detector creates its independent Start menu shortcut.'
    Assert-True (!(Test-Path -LiteralPath $DetectorService)) 'Default installation does not register a boot service.'
    $ProfileOwned = $true
    $UiProcess = Start-Process -FilePath (Join-Path $InstallRoot 'CylonDetector.exe') -WorkingDirectory $InstallRoot -PassThru
    $Deadline = [Diagnostics.Stopwatch]::StartNew()
    $Window = [IntPtr]::Zero
    while ($Deadline.Elapsed.TotalSeconds -lt 45) {
        if ($UiProcess.HasExited) { throw 'Normal installed Detector UI exited before becoming ready.' }
        $Window = [DetectorInstallerFixtureWindow]::Find($UiProcess.Id)
        $DatabaseReady = (Test-Path -LiteralPath (Join-Path $ProfileRoot 'history.db')) -and (Get-Item -LiteralPath (Join-Path $ProfileRoot 'history.db')).Length -ge 100
        if ([DetectorInstallerFixtureWindow]::Responds($Window) -and $DatabaseReady) { break }
        Start-Sleep -Milliseconds 200
    }
    Assert-True ([DetectorInstallerFixtureWindow]::Responds($Window)) 'Normal installed Detector opens one responsive application window.'
    $Database = Join-Path $ProfileRoot 'history.db'
    Assert-True ((Test-Path -LiteralPath $Database) -and (Get-Item -LiteralPath $Database).Length -ge 100) 'Normal launch creates the unified local main database without monitoring.'
    Assert-True (@(Get-Process -Name 'CylonDetector.Collector', 'CylonDetector.Service' -ErrorAction SilentlyContinue).Count -eq 0) 'Fresh default UI launch starts no collector or service process.'
    Assert-True ([DetectorInstallerFixtureWindow]::Exit($Window)) 'The owned application window accepts its full-exit message.'
    Assert-True ($UiProcess.WaitForExit(45000)) 'Normal installed application fully exits and flushes its store.'
    Assert-True ($UiProcess.ExitCode -eq 0) 'Normal installed application exits successfully.'
    $UiProcess.Dispose(); $UiProcess = $null
    $OptionsFile = Join-Path $ProfileRoot 'options.json'
    Assert-True (Test-Path -LiteralPath $OptionsFile) 'Normal exit saves local preferences.'
    $Options = Get-Content -LiteralPath $OptionsFile -Raw | ConvertFrom-Json
    Assert-True (!$Options.AutoMonitor -and $Options.Startup -eq 0) 'Fresh defaults retain monitoring off and boot/sign-in startup disabled.'
    $SavedFiles = @('history.db', 'options.json') | ForEach-Object { [ordered]@{ name=$_; hash=(Get-FileHash -LiteralPath (Join-Path $ProfileRoot $_) -Algorithm SHA256).Hash } }
    Invoke-Installer (Join-Path $InstallRoot 'unins000.exe')
    Assert-True (!(Test-Path -LiteralPath $DetectorKey) -and !(Test-Path -LiteralPath (Join-Path $InstallRoot 'CylonDetector.exe')) -and !(Test-Path -LiteralPath $Shortcut)) 'Silent uninstall removes Detector registration, installed UI and shortcut.'
    foreach ($Saved in $SavedFiles) {
        $Preserved = Join-Path $ProfileRoot $Saved.name
        Assert-True ((Test-Path -LiteralPath $Preserved) -and (Get-FileHash -LiteralPath $Preserved -Algorithm SHA256).Hash -eq $Saved.hash) 'Silent uninstall preserves the fixture-created main database and settings by default.'
    }
    Assert-True (!(Test-Path -LiteralPath $DetectorService) -and !(Test-Path -LiteralPath $BootRoot)) 'Installer lifecycle creates no boot registration or protected boot data.'
    Assert-True ((Raider-Snapshot) -ceq $RaiderBefore) 'Raider application registration, service configuration and installation files remain unchanged.'
} catch {
    $Failure = $_
} finally {
    if ($null -ne $UiProcess) {
        try {
            if (!$UiProcess.HasExited) {
                [void][DetectorInstallerFixtureWindow]::Exit([DetectorInstallerFixtureWindow]::Find($UiProcess.Id))
                if (!$UiProcess.WaitForExit(15000)) { $UiProcess.Kill($true); [void]$UiProcess.WaitForExit(10000) }
            }
            $UiProcess.Dispose()
        } catch { $CleanupFailures.Add('Fixture-owned UI process cleanup failed.') }
    }
    if ($InstallAttempted) {
        try {
            if (Test-Path -LiteralPath (Join-Path $InstallRoot 'unins000.exe')) { Invoke-Installer (Join-Path $InstallRoot 'unins000.exe') }
            if ((Test-Path -LiteralPath $DetectorKey) -or (Test-Path -LiteralPath $DetectorService)) { throw 'Detector registration remains; no recursive installation cleanup was attempted.' }
            Remove-OwnedTree $InstallRoot (Join-Path $env:ProgramFiles 'Cylon Detector')
            if (Test-Path -LiteralPath $Shortcut) { Assert-NoLinks $Shortcut; Remove-Item -LiteralPath $Shortcut -Force }
        } catch { $CleanupFailures.Add('Fixture-owned installation cleanup failed; review disposable runner outcome.') }
    }
    if ($ProfileOwned) {
        try { Remove-OwnedTree $ProfileRoot (Join-Path $env:LOCALAPPDATA 'EirenAndI/CylonDetector') }
        catch { $CleanupFailures.Add('Fixture-created Detector profile cleanup failed.') }
    }
    foreach ($RuleName in $FixtureRuleNames) {
        try { Get-NetFirewallRule -Name $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop }
        catch { $CleanupFailures.Add('Fixture-owned outbound-block rule cleanup failed.') }
    }
    try { if ((Raider-Snapshot) -cne $RaiderBefore) { $CleanupFailures.Add('Raider read-only baseline differs after fixture cleanup.') } }
    catch { $CleanupFailures.Add('Raider final read-only baseline comparison failed.') }
}
if ($null -ne $Failure) {
    if ($CleanupFailures.Count -gt 0) { throw [InvalidOperationException]::new(('Installer fixture failed and cleanup was incomplete: ' + ($CleanupFailures -join ' ')), $Failure.Exception) }
    throw $Failure
}
if ($CleanupFailures.Count -gt 0) { throw ($CleanupFailures -join ' ') }
Write-Output 'PASS disposable hosted installer lifecycle only: fresh install, normal non-monitoring launch, responsive window, full exit, default data-preserving uninstall, isolated cleanup and Raider non-interference. No physical devices, ETW capture, boot-service or upgrade acceptance was tested.'
