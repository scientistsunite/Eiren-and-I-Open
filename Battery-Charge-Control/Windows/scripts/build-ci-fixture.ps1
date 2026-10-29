# Compilation/inspection only. No installer or preview execution.
param([Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
python scripts/check-simulation.py
if ($LASTEXITCODE -ne 0) { throw 'Simulation source boundary failed' }
python scripts/check-ci-fixture.py
if ($LASTEXITCODE -ne 0) { throw 'CI fixture boundary failed' }
if ((dotnet --version) -ne '10.0.402') { throw 'Pinned SDK 10.0.402 required' }
$output = Join-Path $root "artifacts/ci-fixture/$Architecture"
if (Test-Path $output) { throw 'Fixture output must be fresh; never reuse an earlier payload' }
$payload = Join-Path $output 'payload'
dotnet publish tests/BCC.MonitoringPreview/BCC.MonitoringPreview.csproj -c Release -r "win-$Architecture" --self-contained true --source https://api.nuget.org/v3/index.json `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output $payload
if ($LASTEXITCODE -ne 0) { throw 'CI no-firmware payload compilation failed' }
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'BCC.NoFirmwarePreview.exe') { throw 'Unexpected files in fixture payload' }
$bytes = [IO.File]::ReadAllBytes($files[0].FullName)
$pe = [BitConverter]::ToInt32($bytes,0x3c)
$machine = [BitConverter]::ToUInt16($bytes,$pe+4)
$expectedMachine = if ($Architecture -eq 'arm64') { 0xaa64 } else { 0x8664 }
if ($machine -ne $expectedMachine) { throw 'Payload PE architecture mismatch' }
. (Join-Path $PSScriptRoot 'inno-compiler.ps1')
$iscc = Find-InnoCompiler
if (-not $iscc) { throw 'Reviewed Inno Setup 6.7.1 compiler required; no automatic tool installation here' }
& $iscc "/DFixtureArchitecture=$Architecture" installer/MonitoringOnlyCI.iss
if ($LASTEXITCODE -ne 0) { throw 'CI fixture installer compilation failed' }
$installerName = "BCC-CI-NoFirmware-$Architecture-Setup.exe"
$commit = git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Source commit unavailable' }
$manifest = [ordered]@{
  SchemaVersion=1; Fixture='BCC-CI-NoFirmware'; Architecture=$Architecture; SourceCommit=$commit
  InstallerName=$installerName; InstallerSha256=(Get-FileHash (Join-Path $output $installerName)).Hash
  PayloadSha256=(Get-FileHash $files[0].FullName).Hash; PayloadMachine=$machine
  DotnetSdk='10.0.402'; InnoSetup='6.7.1'; CompilerSha256=(Get-FileHash $iscc).Hash
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $output 'fixture-manifest.json') -Encoding utf8
Write-Host "PASS: compiled/inspected $Architecture CI fixture only; no installer or application executed."
