# Initial Windows build, regression checks and disposable hosted installer smoke test.
# The installed UI fixture uses monitoring-off defaults; no real sensor/ETW capture or boot-service registration is exercised.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AppRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $AppRoot
try {
    $SourceSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source revision' }
    $env:SourceRevisionId = $SourceSha
    function Invoke-Checked {
        param([string]$Tool, [string[]]$Arguments)
        & $Tool @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Tool failed ($LASTEXITCODE): $Arguments" }
    }
    $TestFailures = [Collections.Generic.List[string]]::new()
    foreach ($Suite in @('Detector.Tests', 'Detector.HistoryTests', 'Detector.QualityTests', 'Detector.UiTests', 'Detector.UxTests', 'Detector.SensorTests', 'Detector.PrivacyTests', 'Detector.IdentityTests', 'Detector.ReportTests')) {
        try { Invoke-Checked dotnet @('run', '--project', "tests/$Suite", '-c', 'Release') }
        catch { $TestFailures.Add($Suite); Write-Output ("FAILED SUITE: $Suite : " + $_.Exception.Message) }
    }
    try { Invoke-Checked dotnet @('run', '--no-build', '--project', 'tests/Detector.UxTests', '-c', 'Release', '--', '--close-probe') }
    catch { $TestFailures.Add('Native tray-close'); Write-Output ("FAILED SUITE: Native tray-close : " + $_.Exception.Message) }
    if ($TestFailures.Count -gt 0) { throw ("Required suites failed: " + ($TestFailures -join ', ')) }
    Invoke-Checked python @('tests/offline_audit.py')
    Invoke-Checked python @('tests/detector_privacy_audit.py')
    $Payload = Join-Path $AppRoot 'publish'
    $Output = Join-Path $AppRoot 'release-assets'
    New-Item $Payload, $Output -ItemType Directory -Force | Out-Null
    foreach ($Project in @('Detector.App', 'Detector.Collector', 'Detector.Service')) {
        Invoke-Checked dotnet @('publish', "src/$Project", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $Payload)
    }
    foreach ($Binary in @('CylonDetector.exe', 'CylonDetector.Collector.exe', 'CylonDetector.Service.exe')) {
        if (!(Test-Path (Join-Path $Payload $Binary))) { throw "Missing payload: $Binary" }
    }
    Copy-Item README.md $Payload
    Copy-Item docs $Payload -Recurse -Force
    $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    if (!(Test-Path $Iscc)) { throw 'The hosted Windows image must include Inno Setup 6' }
    Invoke-Checked $Iscc @("/DPayloadDir=$Payload", "/DOutputDirOverride=$Output", '/DAppVer=0.1.0', (Join-Path $PSScriptRoot 'installer.iss'))
    Invoke-Checked pwsh @('-NoProfile', '-File', (Join-Path $AppRoot 'tests/installer_smoke.ps1'))
    Set-Content (Join-Path $Payload 'portable.marker') '0.1.0-initial' -Encoding utf8
    Compress-Archive -Path (Join-Path $Payload '*') -DestinationPath (Join-Path $Output 'CylonDetector-Portable-0.1.0-initial-Windows-x64.zip') -CompressionLevel Optimal
    Remove-Item (Join-Path $Payload 'portable.marker')
    # Export only the reviewed Cylon Detector material and its dedicated public recipe.
    # Git metadata, unrelated applications and local build files are excluded.
    Invoke-Checked git @('-C', (Join-Path $AppRoot '../..'), 'archive', '--format=zip', "--output=$(Join-Path $Output 'CylonDetector-Source-0.1.0-initial.zip')", 'HEAD', 'Cylon-Detector/Windows', '.github/workflows/cylon-detector-v01.yml')
    $RunUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    [ordered]@{
        version = '0.1.0-initial'
        source_sha = $SourceSha
        workflow_run = $RunUrl
        dotnet_sdk = (& dotnet --version).Trim()
        operating_system = [Environment]::OSVersion.ToString()
        validation = @('Deterministic measurement tests', 'History regression tests', 'Quality and provenance tests', 'Synthetic sensor-observation contract tests', 'Privacy window and event-triggered persistence tests', 'Collector requester-identity isolation tests', 'Configurable summary-report tests', 'Native Options lifecycle and layout tests', 'Native unified navigation and session-table tests', 'Native tray-close visibility, stable HWND and restore checks in normal/maximized/minimized states', 'Runtime network API and Detector privacy source guards', 'Self-contained Windows x64 app, collector and service compilation', 'Inno Setup installer compilation', 'Disposable hosted installer/default non-monitoring launch/full exit/data-preserving uninstall and Raider isolation fixture')
        limits = 'Initial Windows release with limited Windows sensor observability. Installer lifecycle evidence is limited to a disposable hosted Windows fixture with monitoring off. No physical-device, physical-machine installer lifecycle, real ETW, boot-service, upgrade, long-duration, multi-monitor or actual display-DPI acceptance is claimed by this workflow.'
        distribution = 'Actions test artifact only; no existing release, tag or asset is modified.'
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Output 'BUILD-PROVENANCE.json') -Encoding utf8
    $Readme = @'
Cylon Detector 0.1.0 initial - Windows x64

Run CylonDetector-Setup-0.1.0-initial.exe for the normal installation.
The installer uses an application ID independent of Cylon Raider and preserves Detector data by default when uninstalling.
The portable ZIP uses its own separate portable data location.

Cylon Detector observes microphone/camera access metadata available through Windows and correlates local per-application network measurements. Device-access visibility depends on Windows and application support; missing evidence does not prove inactivity.
While monitoring, a five-minute network context buffer stays in RAM. By default, observed events select detail for the main local database with post-event context; continuous network detail is optional. Configurable interval summaries use separate report files.
Detector captures no audio, video or packet contents and makes no external lookups or telemetry requests. Monitoring starts only when requested; optional startup and boot-service modes require explicit configuration.
Verify SHA256SUMS.txt before testing. BUILD-PROVENANCE.json identifies the exact source and validation limits.
This initial installer has no publisher code signature. Windows may show an unknown-publisher prompt.
Keep a backup of important history before testing an initial version.
'@
    Set-Content (Join-Path $Output 'READ-ME-FIRST.txt') $Readme -Encoding utf8
    $Sums = Get-ChildItem $Output -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    Set-Content (Join-Path $Output 'SHA256SUMS.txt') $Sums -Encoding ascii
    $Sums | Write-Output
} finally {
    Pop-Location
}
