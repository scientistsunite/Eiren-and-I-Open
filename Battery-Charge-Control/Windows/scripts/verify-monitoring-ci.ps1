# HOST supervisor: never launches either application. Only the approved CI fixture may be installed.
param([Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ci-fixture-gate.ps1')
. (Join-Path $PSScriptRoot 'ci-fixture-diagnostics.ps1')
Assert-CiFixtureApproval $Architecture # Before account creation, ACL changes or process creation.
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$head = git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -cne $env:GITHUB_SHA) { throw 'Checkout is not the approved source commit' }
python (Join-Path $PSScriptRoot 'check-simulation.py')
if ($LASTEXITCODE -ne 0) { throw 'Simulation isolation failed' }
python (Join-Path $PSScriptRoot 'check-ci-fixture.py')
if ($LASTEXITCODE -ne 0) { throw 'CI fixture isolation failed' }
$output = Join-Path $root "artifacts/ci-fixture/$Architecture"
$manifest = Read-CiFixtureManifest $output $Architecture
$reportDirectory = Join-Path $env:RUNNER_TEMP "bcc-ci-report-$Architecture"
if (Test-Path $reportDirectory) { throw 'Report directory already exists; fresh job required' }
New-Item -ItemType Directory -Path $reportDirectory | Out-Null
Copy-Item (Join-Path $output 'fixture-manifest.json') $reportDirectory
$stage = Join-Path $env:RUNNER_TEMP ('bcc-ci-stage-'+[Guid]::NewGuid().ToString('N'))
$userName = 'BCCci'+[Guid]::NewGuid().ToString('N').Substring(0,12)
$account = $null
$child = $null
$childExitCode = $null
$failed = $false
$diagnostics = [Collections.Generic.List[object]]::new()
$phase='Create temporary account'
function Grant-FixtureAccess([string]$Path,[string]$Rights) {
  & icacls.exe $Path /grant:r "*$($account.SID.Value):(OI)(CI)$Rights" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Cannot scope temporary user access' }
}
try {
  # The runner token is usually administrative; never mistake it for an ordinary user.
  $secret = ConvertTo-SecureString ('BcC!a9'+[Guid]::NewGuid().ToString('N')) -AsPlainText -Force
  $account = New-LocalUser -Name $userName -Password $secret -AccountNeverExpires -Description 'Ephemeral BCC no-firmware CI test account'
  $usersGroup = Get-LocalGroup -SID 'S-1-5-32-545'
  Add-LocalGroupMember -Group $usersGroup -Member $account
  $phase='Stage fixture'
  New-Item -ItemType Directory -Path $stage | Out-Null
  Copy-Item (Join-Path $output $manifest.InstallerName) $stage
  Copy-Item (Join-Path $output 'fixture-manifest.json') $stage
  Copy-Item (Join-Path $PSScriptRoot 'ci-fixture-gate.ps1') $stage
  Copy-Item (Join-Path $PSScriptRoot 'ci-fixture-profile.ps1') $stage
  Copy-Item (Join-Path $PSScriptRoot 'ci-fixture-diagnostics.ps1') $stage
  Copy-Item (Join-Path $PSScriptRoot 'test-ci-fixture-user.ps1') $stage
  Grant-FixtureAccess $stage 'RX'
  Grant-FixtureAccess $reportDirectory 'M'
  $credential = [PSCredential]::new("$env:COMPUTERNAME\$userName",$secret)
  $arguments = '-NoLogo -NoProfile -NonInteractive -File "'+(Join-Path $stage 'test-ci-fixture-user.ps1')+
    '" -StageDirectory "'+$stage+'" -ReportDirectory "'+$reportDirectory+'" -Architecture '+$Architecture+
    ' -ExpectedUserSid '+$account.SID.Value
  $phase='Launch standard-user worker'
  $child = Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList $arguments -Credential $credential -LoadUserProfile `
    -WorkingDirectory $stage -RedirectStandardOutput (Join-Path $reportDirectory 'worker-stdout.log') `
    -RedirectStandardError (Join-Path $reportDirectory 'worker-stderr.log') -PassThru
  if (-not $child.WaitForExit(480000)) { $child.Kill($true); throw 'Standard-user fixture tests timed out; no success recorded' }
  $child.Refresh()
  $childExitCode = $child.ExitCode
  if ($childExitCode -ne 0) { throw 'BCC_WORKER_EXIT_NONZERO' }
  $phase='Validate worker results'
  $report = Get-Content -LiteralPath (Join-Path $reportDirectory 'results.json') -Raw | ConvertFrom-Json
  $expectedChecks = @('Ordinary user','Profile initialization','Fresh install','Registration and shortcuts','Modify add desktop',
    'Modify remove desktop','Repair missing payload','Uninstall and cleanup','Reinstall','Second cleanup')
  if ($report.Status -cne 'Passed' -or @($report.Checks).Count -ne $expectedChecks.Count -or
      @($report.Checks | Where-Object { $_.Status -cne 'Passed' -or $_.Name -cnotin $expectedChecks }).Count -ne 0 -or
      @($report.Checks.Name | Select-Object -Unique).Count -ne $expectedChecks.Count -or
      @($report.Commands).Count -ne 7 -or
      @($report.Commands | Where-Object { $_.Status -cne 'Passed' -or $_.ExitCode -ne 0 }).Count -ne 0 -or
      $report.IsAdministrator -ne $false -or $report.Architecture -cne $Architecture -or
      $report.ProfileInitialization.Status -cne 'Passed' -or
      $report.ProfileInitialization.PathAuthority -cne 'AuthenticatedSidProfileList' -or
      $report.ProfileInitialization.RegistrationReadback -cne 'Verified' -or
      $report.UserSid -cne $account.SID.Value -or $report.SourceCommit -cne $head) {
    throw 'Worker report is incomplete or disagrees with exit code/identity/source'
  }
} catch {
  $failed = $true; $diagnostics.Add((Get-CiFixtureFailureDiagnostic -Record $_ -Phase $phase))
} finally {
  if ($null -ne $child) { $child.Dispose() }
  if ($null -ne $account) {
    try {
      # Only this newly created SID; never enumerate/delete unrelated users or profiles.
      $profiles = @()
      for ($i=0; $i -lt 40; $i++) {
        $profiles = @(Get-CimInstance Win32_UserProfile -Filter "SID='$($account.SID.Value)'")
        if (@($profiles | Where-Object Loaded).Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
      }
      foreach ($profile in $profiles) {
        if ($profile.Special -or $profile.Loaded) { throw 'Temporary profile still loaded/special; cleanup failed' }
        Remove-CimInstance -InputObject $profile
      }
      Remove-LocalUser -SID $account.SID
      if (Get-LocalUser -SID $account.SID -ErrorAction SilentlyContinue) { throw 'Temporary account remains' }
    } catch { $failed = $true; $diagnostics.Add((Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Account/profile cleanup')) }
  }
  try { if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force } }
  catch { $failed = $true; $diagnostics.Add((Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Staging cleanup')) }
  $status = if ($failed) { 'Failed' } else { 'Passed' }
  [ordered]@{Status=$status; SourceCommit=$head; Architecture=$Architecture; RunnerImage=$env:ImageVersion;
    WorkerExitCode=$childExitCode; Diagnostics=$diagnostics.ToArray()} |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $reportDirectory 'supervisor.json') -Encoding utf8
  if (Test-Path (Join-Path $reportDirectory 'results.md')) {
    Get-Content (Join-Path $reportDirectory 'results.md') | Add-Content $env:GITHUB_STEP_SUMMARY
  }
  "`nSupervisor: **$status**. Account/profile and staging cleanup are included. See supervisor.json and worker results.json for sanitized phase/script/line diagnostics." |
    Add-Content $env:GITHUB_STEP_SUMMARY
}
if ($failed) { throw 'BCC_CI_FIXTURE_FAILED: inspect supervisor.json and worker results.json in the diagnostic artifact' }
Write-Host 'PASS: CI fixture lifecycle and temporary-account cleanup. Production behavior was not tested.'
