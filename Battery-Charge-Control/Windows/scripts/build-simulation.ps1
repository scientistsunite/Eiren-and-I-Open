# Compilation only. Never run either generated executable here.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
python scripts/check-simulation.py
if ($LASTEXITCODE -ne 0) { throw 'Simulation boundary check failed' }
dotnet publish tests/BCC.MonitoringPreview/BCC.MonitoringPreview.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output artifacts/simulation/payload
if ($LASTEXITCODE -ne 0) { throw 'Simulation preview compilation failed' }
. (Join-Path $PSScriptRoot 'inno-compiler.ps1')
$iscc = Find-InnoCompiler
if (-not $iscc) { throw 'Inno Setup 6.7.1 required' }
& $iscc installer/MonitoringOnly.iss
if ($LASTEXITCODE -ne 0) { throw 'Simulation installer compilation failed' }
Get-FileHash artifacts/simulation/BCC-NoFirmware-MonitoringOnly-Test-Setup.exe -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLowerInvariant())  BCC-NoFirmware-MonitoringOnly-Test-Setup.exe" } |
  Set-Content artifacts/simulation/installer-sha256.txt -Encoding utf8
