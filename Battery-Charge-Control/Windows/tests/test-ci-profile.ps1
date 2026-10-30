# Safe Windows unit tests: pure planning and in-memory callbacks only.
# Never dot-source the worker, launch installers, read/write a user hive or create an account/profile.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../scripts/ci-fixture-profile.ps1')
. (Join-Path $PSScriptRoot '../scripts/ci-fixture-diagnostics.ps1')
$passed=0
function Require($Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Code([scriptblock]$Action,[string]$Code) {
  $caught=$false
  try { & $Action | Out-Null }
  catch { $caught=$true;Require ($_.Exception.Message -like "$Code*") 'Wrong failure classification' }
  Require $caught 'Expected failure was not raised'
}
function Test([string]$Name,[scriptblock]$Action) {
  try { & $Action;$script:passed++;Write-Host "PASS: $Name" }
  catch { throw "Profile regression failed: $Name" }
}
function Registration-ForPlan($Plan) {
  @{
    LocalApplicationData=$Plan.Paths.LocalApplicationData;ApplicationData=$Plan.Paths.ApplicationData
    Programs=$Plan.Paths.Programs;DesktopDirectory=$Plan.Paths.DesktopDirectory
    StartMenu=[IO.Path]::GetDirectoryName($Plan.Paths.Programs)
  }
}
$root='C:\Users\BCCci-UnitFixture'
foreach ($value in @($null,'','   ')) {
  $sample=$value
  Test "missing/empty/whitespace profile root #$passed" {
    Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $sample } 'BCC_PROFILE_ROOT_MISSING'
  }
}
foreach ($value in @('C:relative','relative','\\server\share','C:\')) {
  $sample=$value
  Test "invalid profile root #$passed" {
    Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $sample } 'BCC_PROFILE_ROOT_INVALID'
  }
}
Test 'all unresolved special folders use only the verified SID profile root' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders @{
    LocalApplicationData='';ApplicationData=$null;Programs=' ';DesktopDirectory=''
  }
  Require ($plan.Paths.LocalApplicationData -ceq "$root\AppData\Local") 'Local app data default incorrect'
  Require ($plan.Paths.ApplicationData -ceq "$root\AppData\Roaming") 'Roaming app data default incorrect'
  Require ($plan.Paths.Programs -ceq "$root\AppData\Roaming\Microsoft\Windows\Start Menu\Programs") 'Start menu default incorrect'
  Require ($plan.Paths.DesktopDirectory -ceq "$root\Desktop") 'Desktop default incorrect'
  Require ($plan.Paths.Temp -ceq "$root\AppData\Local\Temp") 'Temporary path escaped the standard user profile'
  Require ($plan.MissingKnownFolders.Count -eq 4) 'Missing folder diagnostics incomplete'
}
foreach ($name in @('LocalApplicationData','ApplicationData','Programs','DesktopDirectory')) {
  $field=$name
  Test "empty $field is initialized rather than passed to Join-Path" {
    $known=@{LocalApplicationData="$root\Local";ApplicationData="$root\Roaming";Programs="$root\Programs";DesktopDirectory="$root\Desk"}
    $known[$field]=''
    $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders $known
    Require (-not [string]::IsNullOrWhiteSpace($plan.Paths[$field])) 'Empty path escaped'
    Require ($plan.MissingKnownFolders -contains $field) 'Missing field not recorded'
  }
  Test "inherited administrator path for $field is rejected" {
    $known=@{};$known[$field]='C:\Users\runneradmin\Data'
    Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders $known } 'BCC_PROFILE_FOLDER_OUTSIDE_USER'
  }
}
Test 'similarly prefixed sibling profile is not inside the user profile' {
  Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders @{Programs=($root+'-other\Programs')} } 'BCC_PROFILE_FOLDER_OUTSIDE_USER'
}
Test 'parent traversal and unresolved environment expansion cannot escape validation' {
  Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders @{DesktopDirectory="$root\..\Other\Desktop"} } 'BCC_PROFILE_FOLDER_OUTSIDE_USER'
  Expect-Code { Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders @{Programs='%APPDATA%\Programs'} } 'BCC_PROFILE_FOLDER_INVALID'
}
Test 'Programs fallback follows validated nondefault roaming location' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders @{ApplicationData="$root\RoamingCustom"}
  Require ($plan.Paths.Programs -ceq "$root\RoamingCustom\Microsoft\Windows\Start Menu\Programs") 'Roaming root ignored'
}
Test 'bootstrap creates required directories before publishing matching per-user shell folders' {
  $events=[Collections.Generic.List[string]]::new()
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $result=Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {
    param($path);$events.Add('directory')
    Require ($path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) 'Wrong profile used'
  } -WriteShellFolders {
    param($values);$events.Add('registration')
    Require ($events.Count -eq 7) 'Registration preceded directory initialization'
    Require ($values['Local AppData'] -ceq $plan.Paths.LocalApplicationData) 'Installer path mismatch'
    Require ($values['Programs'] -ceq $plan.Paths.Programs) 'Programs path mismatch'
    Require ($values.Count -eq 5) 'Unexpected shell-folder entries'
  } -ReadShellFolders { $events.Add('readback');Registration-ForPlan $plan }
  Require ($events.Count -eq 8 -and $events[7] -ceq 'readback') 'Readback was not required'
  Require ($result.MissingKnownFolders.Count -eq 4) 'Initialization evidence lost'
}
Test 'missing root aborts before any initialization callback' {
  $events=[Collections.Generic.List[string]]::new()
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan ([pscustomobject]@{Paths=@{UserProfile=''}}) `
      -EnsureDirectory {$events.Add('unexpected')} -WriteShellFolders {$events.Add('unexpected')} `
      -ReadShellFolders {$events.Add('unexpected')}
  } 'BCC_PROFILE_ROOT_MISSING'
  Require ($events.Count -eq 0) 'Side effect occurred before root validation'
}
Test 'directory creation failure prevents shell registration' {
  $events=[Collections.Generic.List[string]]::new()
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {throw 'BCC_TEST_DIRECTORY_DENIED'} `
      -WriteShellFolders {$events.Add('unexpected')} -ReadShellFolders {$events.Add('unexpected')}
  } 'BCC_TEST_DIRECTORY_DENIED'
  Require ($events.Count -eq 0) 'Failure was ignored'
}
Test 'experiment 2 unsafe ambient LocalApplicationData is not bootstrap authority' {
  $ambient=@{LocalApplicationData='C:\Users\runneradmin\AppData\Local'}
  # The validator still rejects this observed class of input; no recovery/bypass switch is added.
  Expect-Code {Resolve-CiFixtureProfilePaths -ProfilePath $root -KnownFolders $ambient} 'BCC_PROFILE_FOLDER_OUTSIDE_USER:LocalApplicationData'
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $state=@{Registration=@{};Writes=0;Reads=0}
  $result=Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {
    param($path);Require ($path.StartsWith($root+'\') -or $path -ceq $root) 'Unsafe inherited path used'
  } -WriteShellFolders {
    param($values);$state.Writes++;$state.Registration=Registration-ForPlan $plan
    Require ($values['Local AppData'] -ceq "$root\AppData\Local") 'Registration used ambient path'
  } -ReadShellFolders {$state.Reads++;$state.Registration}
  Require ($result.Paths.LocalApplicationData -ceq "$root\AppData\Local") 'SID-local bootstrap failed'
  Require ($state.Writes -eq 1 -and $state.Reads -eq 1) 'Registration not written then verified'
}
Test 'invalid externally supplied plan cannot initialize outside the verified profile' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $plan.Paths.LocalApplicationData='C:\Users\runneradmin\AppData\Local'
  $events=[Collections.Generic.List[string]]::new()
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {$events.Add('unexpected')} `
      -WriteShellFolders {$events.Add('unexpected')} -ReadShellFolders {$events.Add('unexpected')}
  } 'BCC_PROFILE_FOLDER_OUTSIDE_USER:LocalApplicationData'
  Require ($events.Count -eq 0) 'Side effect preceded validation'
}
Test 'unsafe SID registration readback remains a blocking outside-profile error' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {} -WriteShellFolders {param($values)} `
      -ReadShellFolders {$values=Registration-ForPlan $plan;$values.LocalApplicationData='C:\Users\runneradmin\AppData\Local';$values}
  } 'BCC_PROFILE_FOLDER_OUTSIDE_USER:LocalApplicationData'
}
Test 'missing registration readback must not be replaced with planner defaults' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {} -WriteShellFolders {param($values)} `
      -ReadShellFolders {$values=Registration-ForPlan $plan;$values.LocalApplicationData='';$values}
  } 'BCC_PROFILE_REGISTRATION_MISSING:LocalApplicationData'
}
Test 'different profile-local registration still cannot be reported as the planned path' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $values=Registration-ForPlan $plan;$values.LocalApplicationData="$root\DifferentLocal"
  Expect-Code {Assert-CiFixtureProfileRegistration -Plan $plan -RegisteredFolders $values} 'BCC_PROFILE_REGISTRATION_MISMATCH:LocalApplicationData'
}
Test 'current-user view must pass the same SID-plan registration validation' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $sidView=Registration-ForPlan $plan
  Assert-CiFixtureProfileRegistration -Plan $plan -RegisteredFolders $sidView
  $currentUserView=Registration-ForPlan $plan;$currentUserView.DesktopDirectory='C:\Users\runneradmin\Desktop'
  Expect-Code {Assert-CiFixtureProfileRegistration -Plan $plan -RegisteredFolders $currentUserView} 'BCC_PROFILE_FOLDER_OUTSIDE_USER:DesktopDirectory'
}
Test 'registration write failure prevents verification or success' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  $state=@{Read=$false}
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {} `
      -WriteShellFolders {param($values);throw 'BCC_TEST_REGISTRATION_DENIED'} -ReadShellFolders {$state.Read=$true}
  } 'BCC_TEST_REGISTRATION_DENIED'
  Require (-not $state.Read) 'Readback ignored a failed registration write'
}
Test 'unreadable registration cannot be reported as initialized' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath $root
  Expect-Code {
    Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {} -WriteShellFolders {param($values)} `
      -ReadShellFolders {throw 'BCC_TEST_READBACK_DENIED'}
  } 'BCC_TEST_READBACK_DENIED'
}
Test 'SID profile on a different volume is used without username or drive guessing' {
  $plan=Resolve-CiFixtureProfilePaths -ProfilePath 'D:\IsolatedProfiles\AuthenticatedTestAccount'
  Initialize-CiFixtureProfilePaths -Plan $plan -EnsureDirectory {} -WriteShellFolders {param($values)} `
    -ReadShellFolders {Registration-ForPlan $plan} | Out-Null
  Require ($plan.Paths.LocalApplicationData -ceq 'D:\IsolatedProfiles\AuthenticatedTestAccount\AppData\Local') 'Profile root was guessed'
}
Test 'diagnostics preserve named source location and omit raw private exception content' {
  try { throw 'BCC_PROFILE_ROOT_MISSING fake-password-SENTINEL C:\private-user\sensitive' }
  catch { $diagnostic=Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Profile initialization' }
  $json=$diagnostic | ConvertTo-Json
  Require ($diagnostic.Code -ceq 'BCC_PROFILE_ROOT_MISSING') 'Failure code lost'
  Require ($diagnostic.Script -ceq 'test-ci-profile.ps1' -and $diagnostic.Line -gt 0) 'Source location missing'
  Require ($json -notmatch 'SENTINEL|private-user|sensitive') 'Raw exception content leaked'
}
Test 'original empty Join-Path error becomes a source-addressed sanitized diagnostic' {
  try { Join-Path -Path '' -ChildPath 'Fixture' | Out-Null;throw 'Missing binder failure' }
  catch { $diagnostic=Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Profile initialization' }
  Require ($diagnostic.Line -gt 0 -and $diagnostic.ErrorId -match 'EmptyString') 'Binder source classification missing'
}
Test 'unknown thrown strings cannot leak through PowerShell error identifiers' {
  try { throw 'fakePasswordSENTINEL' }
  catch { $diagnostic=Get-CiFixtureFailureDiagnostic -Record $_ -Phase 'Profile initialization' }
  Require (($diagnostic | ConvertTo-Json) -notmatch 'SENTINEL') 'Thrown-string error identifier leaked'
  Require ($diagnostic.Code -ceq 'BCC_UNEXPECTED_FAILURE') 'Unknown failure classification incorrect'
}
Write-Host "RESULT: $passed profile/diagnostic tests passed; 0 failed; no installer, account, profile or registry operations executed."
