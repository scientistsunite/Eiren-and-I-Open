# Run from PowerShell on Windows 11, with .NET 10 SDK installed.
# Win x64 and ARM64 are compiled into separate payload folders; installer recipe currently x64.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the official .NET 10 SDK before building.' }
$ver = dotnet --version
if (-not $ver.StartsWith('10.')) { throw "Require .NET 10 SDK; detected $ver" }
foreach ($rid in @('win-x64','win-arm64')) {
  $dest = Join-Path $root "artifacts\publish\$rid"
  dotnet publish '.\src\BCC.App\BCC.App.csproj' --configuration Release --runtime $rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:TreatWarningsAsErrors=true --output $dest
  if ($LASTEXITCODE -ne 0) { throw "App publishing failed: $rid" }
  dotnet build '.\src\BCC.Controller.Asus\BCC.Controller.Asus.csproj' --configuration Release -p:TreatWarningsAsErrors=true
  if ($LASTEXITCODE -ne 0) { throw 'ASUS controller build failed' }
  $driverDest = Join-Path $dest 'Controllers\ASUS'
  New-Item -ItemType Directory -Path $driverDest -Force | Out-Null
  Copy-Item '.\src\BCC.Controller.Asus\bin\Release\net10.0-windows\BCC.Controller.Asus.dll' $driverDest -Force
}
Write-Host 'Published win-x64 and win-arm64 application payloads. Hardware operations NOT tested.'
. (Join-Path $PSScriptRoot 'inno-compiler.ps1')
$iscc = Find-InnoCompiler
if (-not $iscc) { throw 'Inno Setup 6.7.1 required to compile the x64 installer.' }
& $iscc '.\installer\BCC.iss'
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compiler failed.' }
Write-Host 'Windows x64 installer created in artifacts folder.'
