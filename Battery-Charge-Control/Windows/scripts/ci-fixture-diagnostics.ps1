# Deliberately omit raw exception messages, command arguments, environment and full paths.
function Get-CiFixtureFailureDiagnostic {
  param([Parameter(Mandatory)][System.Management.Automation.ErrorRecord]$Record,
        [Parameter(Mandatory)][string]$Phase)
  $code='BCC_UNEXPECTED_FAILURE'
  if ($Record.Exception.Message -match '\b(BCC_[A-Z_]+(?::[A-Za-z]+)?)\b') { $code=$Matches[1] }
  $script='unknown'
  $line=0
  if ($null -ne $Record.InvocationInfo) {
    if ($Record.InvocationInfo.ScriptName) { $script=[IO.Path]::GetFileName($Record.InvocationInfo.ScriptName) }
    $line=$Record.InvocationInfo.ScriptLineNumber
  }
  # PowerShell can use the ENTIRE thrown string as FullyQualifiedErrorId. Character filtering
  # is insufficient redaction; allow only known framework IDs, otherwise use code/category.
  $errorId=$code
  if ($Record.FullyQualifiedErrorId -match '^(ParameterArgumentValidationErrorEmptyStringNotAllowed|ParameterArgumentValidationErrorNullNotAllowed|PathNotFound)(?:,Microsoft\.PowerShell\.Commands\.[A-Za-z]+Command)?$') {
    $errorId=$Matches[1]
  } elseif ($code -eq 'BCC_UNEXPECTED_FAILURE') { $errorId=$Record.CategoryInfo.Category.ToString() }
  [pscustomobject]@{
    Phase=$Phase; Code=$code; Script=$script; Line=$line
    ExceptionType=$Record.Exception.GetType().FullName
    ErrorId=$errorId
  }
}
