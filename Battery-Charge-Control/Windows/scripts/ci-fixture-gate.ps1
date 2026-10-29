# Shared fail-closed CI gate. This is not a replacement for the manual VM review.
function Assert-CiFixtureApproval([string]$Architecture) {
  if (-not [OperatingSystem]::IsWindows() -or $env:GITHUB_ACTIONS -ne 'true' -or
      $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:GITHUB_EVENT_NAME -ne 'workflow_dispatch' -or
      $env:GITHUB_REF -ne 'refs/heads/main' -or
      $env:GITHUB_WORKFLOW_REF -notlike '*/.github/workflows/bcc-installer-lifecycle.yml@refs/heads/main') {
    throw 'Only the separately approved manual GitHub-hosted CI workflow may execute this fixture'
  }
  if ($env:BCC_INSTALLER_CI_ENABLED -ne 'true' -or
      $env:BCC_INSTALLER_CI_APPROVED_COMMIT -cnotmatch '^[0-9a-f]{40}$' -or
      $env:BCC_INSTALLER_CI_APPROVED_COMMIT -cne $env:GITHUB_SHA -or
      [string]::IsNullOrWhiteSpace($env:BCC_INSTALLER_CI_APPROVAL_REFERENCE) -or
      $env:BCC_INSTALLER_CI_APPROVAL_REFERENCE -cne $env:BCC_INSTALLER_CI_REQUESTED_APPROVAL) {
    throw 'Installer execution disabled or source/owner approval mismatch'
  }
  $expected = if ($Architecture -eq 'arm64') { 'Arm64' } else { 'X64' }
  if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -cne $expected -or
      [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -cne $expected) {
    throw 'Native runner/PowerShell architecture mismatch; emulated payload tests cannot certify this target'
  }
  $os = Get-CimInstance Win32_OperatingSystem
  if (($Architecture -eq 'arm64' -and ($os.ProductType -ne 1 -or $os.Caption -notlike '*Windows 11*')) -or
      ($Architecture -eq 'x64' -and ($os.ProductType -eq 1 -or $os.Caption -notlike '*Windows Server 2025*'))) {
    throw 'Unexpected runner OS; only Windows Server 2025 x64 and Windows 11 ARM64 were reviewed'
  }
}

function Read-CiFixtureManifest([string]$Directory,[string]$Architecture) {
  $manifest = Get-Content -LiteralPath (Join-Path $Directory 'fixture-manifest.json') -Raw | ConvertFrom-Json
  $name = "BCC-CI-NoFirmware-$Architecture-Setup.exe"
  if ($manifest.SchemaVersion -ne 1 -or $manifest.Fixture -cne 'BCC-CI-NoFirmware' -or
      $manifest.Architecture -cne $Architecture -or $manifest.SourceCommit -cne $env:GITHUB_SHA -or
      $manifest.InstallerName -cne $name -or $manifest.DotnetSdk -cne '10.0.402' -or $manifest.InnoSetup -cne '6.7.1' -or
      $manifest.InstallerSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $manifest.PayloadSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw 'Unrecognized CI fixture manifest or source binding'
  }
  if ((Get-FileHash -LiteralPath (Join-Path $Directory $name)).Hash -ne $manifest.InstallerSha256) {
    throw 'CI fixture installer hash mismatch'
  }
  return $manifest
}
