# Locate only. The compiler's VER preprocessor value enforces 6.7.1 in both .iss files.
# ISCC 6.7.1's help banner and executable metadata do not reliably expose its patch version.
function Find-InnoCompiler {
  foreach ($candidate in @(
    [Environment]::ExpandEnvironmentVariables('%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe'),
    [Environment]::ExpandEnvironmentVariables('%ProgramFiles%\Inno Setup 6\ISCC.exe')
  )) {
    if (Test-Path -LiteralPath $candidate) { return $candidate }
  }
  return $null
}
