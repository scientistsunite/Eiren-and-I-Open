# Preliminary UI build only. Does not install Raider, start collection or register services.
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
    foreach ($Suite in @('Raider.Tests', 'Raider.HistoryTests', 'Raider.QualityTests', 'Raider.UiTests', 'Raider.UxTests')) {
        Invoke-Checked dotnet @('run', '--project', "tests/$Suite", '-c', 'Release')
    }
    Invoke-Checked dotnet @('run', '--no-build', '--project', 'tests/Raider.UxTests', '-c', 'Release', '--', '--close-probe')
    Invoke-Checked python @('tests/offline_audit.py')
    $Payload = Join-Path $AppRoot 'publish'
    $Output = Join-Path $AppRoot 'release-assets'
    New-Item $Payload, $Output -ItemType Directory -Force | Out-Null
    foreach ($Project in @('Raider.App', 'Raider.Collector', 'Raider.Service')) {
        Invoke-Checked dotnet @('publish', "src/$Project", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $Payload)
    }
    foreach ($Binary in @('CylonRaider.exe', 'CylonRaider.Collector.exe', 'CylonRaider.Service.exe')) {
        if (!(Test-Path (Join-Path $Payload $Binary))) { throw "Missing payload: $Binary" }
    }
    Copy-Item README.md $Payload
    Copy-Item docs $Payload -Recurse -Force
    $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    if (!(Test-Path $Iscc)) { throw 'The hosted Windows image must include Inno Setup 6' }
    Invoke-Checked $Iscc @("/DPayloadDir=$Payload", "/DOutputDirOverride=$Output", '/DAppVer=0.5.2', (Join-Path $PSScriptRoot 'installer.iss'))
    Set-Content (Join-Path $Payload 'portable.marker') '0.5.2-prerelease' -Encoding utf8
    Compress-Archive -Path (Join-Path $Payload '*') -DestinationPath (Join-Path $Output 'CylonRaider-Portable-0.5.2-prerelease-Windows-x64.zip') -CompressionLevel Optimal
    Remove-Item (Join-Path $Payload 'portable.marker')
    # Export only the reviewed Cylon Raider material and its dedicated public recipe.
    # Git metadata, unrelated applications and local build files are excluded.
    Invoke-Checked git @('-C', (Join-Path $AppRoot '../..'), 'archive', '--format=zip', "--output=$(Join-Path $Output 'CylonRaider-Source-0.5.2-prerelease.zip')", 'HEAD', 'Cylon-Raider/Windows', '.github/workflows/cylon-raider-v052-prerelease.yml')
    $RunUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    [ordered]@{
        version = '0.5.2-prerelease'
        source_sha = $SourceSha
        workflow_run = $RunUrl
        dotnet_sdk = (& dotnet --version).Trim()
        operating_system = [Environment]::OSVersion.ToString()
        validation = @('Original deterministic measurement tests', 'History regression tests', 'Quality and provenance tests', 'Native Options lifecycle and layout tests', 'Native unified navigation and session-table tests', 'Native tray-close visibility, stable HWND and restore checks in normal/maximized/minimized states', 'Runtime source network API guard', 'Self-contained Windows x64 app, collector and service compilation', 'Inno Setup installer compilation')
        limits = 'Preliminary UI build. No physical-device, installer lifecycle, real ETW, boot-service, upgrade, long-duration, multi-monitor or actual display-DPI acceptance is claimed by this workflow.'
        distribution = 'Actions test artifact only; no existing release, tag or asset is modified.'
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Output 'BUILD-PROVENANCE.json') -Encoding utf8
    $Readme = @'
Cylon Raider 0.5.2 prerelease - Windows x64

Run CylonRaider-Setup-0.5.2-prerelease.exe for the normal installation.
The installer upgrades the same Cylon Raider application ID and preserves existing data by default.
The portable ZIP uses its own separate portable data location.

This build fixes the window reappearing after close to tray, including maximized and minimized windows. Measurement, history, collector and service behavior are preserved.
Verify SHA256SUMS.txt before testing. BUILD-PROVENANCE.json identifies the exact source and validation limits.
This prerelease installer has no publisher code signature. Windows may show an unknown-publisher prompt.
Keep a backup of important history before testing a prerelease version.
'@
    Set-Content (Join-Path $Output 'READ-ME-FIRST.txt') $Readme -Encoding utf8
    $Sums = Get-ChildItem $Output -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    Set-Content (Join-Path $Output 'SHA256SUMS.txt') $Sums -Encoding ascii
    $Sums | Write-Output
} finally {
    Pop-Location
}
