# Pure profile planning/initialization decisions. No host registry, account or process operations.
function Resolve-CiFixtureProfilePaths {
  param([AllowNull()][AllowEmptyString()][string]$ProfilePath, [System.Collections.IDictionary]$KnownFolders=@{})
  if ([string]::IsNullOrWhiteSpace($ProfilePath)) { throw 'BCC_PROFILE_ROOT_MISSING' }
  if ($ProfilePath -notmatch '^[A-Za-z]:\\' -or $ProfilePath.Contains('%') -or
      -not [IO.Path]::IsPathFullyQualified($ProfilePath)) { throw 'BCC_PROFILE_ROOT_INVALID' }
  $profile = [IO.Path]::GetFullPath($ProfilePath).TrimEnd('\')
  if ($profile.Length -le 3) { throw 'BCC_PROFILE_ROOT_INVALID' }
  $paths = [ordered]@{ UserProfile=$profile }
  $missing = [Collections.Generic.List[string]]::new()
  foreach ($name in @('LocalApplicationData','ApplicationData','Programs','DesktopDirectory')) {
    $value = [string]$KnownFolders[$name]
    if ([string]::IsNullOrWhiteSpace($value)) {
      $missing.Add($name)
      $value = switch ($name) {
        LocalApplicationData { [IO.Path]::Combine($profile,'AppData','Local') }
        ApplicationData { [IO.Path]::Combine($profile,'AppData','Roaming') }
        Programs { [IO.Path]::Combine($paths.ApplicationData,'Microsoft','Windows','Start Menu','Programs') }
        DesktopDirectory { [IO.Path]::Combine($profile,'Desktop') }
      }
    }
    if ($value -notmatch '^[A-Za-z]:\\' -or $value.Contains('%') -or
        -not [IO.Path]::IsPathFullyQualified($value)) { throw "BCC_PROFILE_FOLDER_INVALID:$name" }
    $value = [IO.Path]::GetFullPath($value).TrimEnd('\')
    if (-not $value.StartsWith($profile+'\',[StringComparison]::OrdinalIgnoreCase)) {
      throw "BCC_PROFILE_FOLDER_OUTSIDE_USER:$name"
    }
    $paths[$name]=$value
  }
  $paths['Temp']=[IO.Path]::Combine($paths.LocalApplicationData,'Temp')
  return [pscustomobject]@{ Paths=$paths; MissingKnownFolders=$missing.ToArray() }
}

function Initialize-CiFixtureProfilePaths {
  param([Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][scriptblock]$EnsureDirectory,
        [Parameter(Mandatory)][scriptblock]$WriteShellFolders,
        [Parameter(Mandatory)][scriptblock]$ReadShellFolders)
  # Complete all validation before a callback may create a directory or change the new user's hive.
  $verified=Resolve-CiFixtureProfilePaths -ProfilePath $Plan.Paths.UserProfile -KnownFolders $Plan.Paths
  foreach ($path in $verified.Paths.Values) { & $EnsureDirectory $path }
  $shellFolders=[ordered]@{
    'Local AppData'=$verified.Paths.LocalApplicationData; 'AppData'=$verified.Paths.ApplicationData
    'Programs'=$verified.Paths.Programs; 'Desktop'=$verified.Paths.DesktopDirectory
    'Start Menu'=[IO.Path]::GetDirectoryName($verified.Paths.Programs)
  }
  & $WriteShellFolders $shellFolders
  $registered=& $ReadShellFolders
  Assert-CiFixtureProfileRegistration -Plan $verified -RegisteredFolders $registered
  return $Plan
}

function Assert-CiFixtureProfileRegistration {
  param([Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][System.Collections.IDictionary]$RegisteredFolders)
  # Readback is not bootstrap input: missing entries must NOT receive planner defaults.
  foreach ($name in @('LocalApplicationData','ApplicationData','Programs','DesktopDirectory','StartMenu')) {
    if ([string]::IsNullOrWhiteSpace([string]$RegisteredFolders[$name])) {
      throw "BCC_PROFILE_REGISTRATION_MISSING:$name"
    }
  }
  $verified=Resolve-CiFixtureProfilePaths -ProfilePath $Plan.Paths.UserProfile -KnownFolders $RegisteredFolders
  foreach ($name in @('LocalApplicationData','ApplicationData','Programs','DesktopDirectory')) {
    if (-not [string]::Equals($verified.Paths[$name],$Plan.Paths[$name],[StringComparison]::OrdinalIgnoreCase)) {
      throw "BCC_PROFILE_REGISTRATION_MISMATCH:$name"
    }
  }
  if (-not [string]::Equals([string]$RegisteredFolders['StartMenu'],[IO.Path]::GetDirectoryName($Plan.Paths.Programs),[StringComparison]::OrdinalIgnoreCase)) {
    throw 'BCC_PROFILE_REGISTRATION_MISMATCH:StartMenu'
  }
}
