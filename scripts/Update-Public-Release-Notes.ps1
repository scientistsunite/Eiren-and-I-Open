# Update public GitHub Release DESCRIPTIONS ONLY (never assets/tags).
# Windows PowerShell 5.1+; GitHub CLI authenticated as scientistsunite.
# No changes without -Apply. Dry-run by default.
[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = 'scientistsunite/Eiren-and-I-Open'
$legacy = 'No project-wide open-source license has yet been selected.'
$otherLegacy = 'No new project-wide open-source reuse license is granted.'
$entries = @(
    @{ Id=427952986; Tag='archive-reader-windows-v0.11'; App='Archive-Reader'; Purpose='Browse and search locally exported ChatGPT conversation archives, including large histories, using the portable Python-based Windows viewer.' },
    @{ Id=427953070; Tag='archive-reader-ubuntu-v0.11'; App='Archive-Reader'; Purpose='Browse and search locally exported ChatGPT conversation archives using the Ubuntu installable or portable distribution.' },
    @{ Id=427953166; Tag='battery-charge-control-ubuntu-5-point-offset'; App='Battery-Charge-Control'; Purpose='Manage model-supported laptop charging thresholds on Ubuntu in five-percentage-point steps. Confirm hardware compatibility and restoration behavior before changing firmware settings.' },
    @{ Id=427953241; Tag='silent-meteor-windows-v0.4.0'; App='Silent-Meteor'; Purpose='Display customizable ambient meteor animations and idle/screensaver visuals on Windows.' },
    @{ Id=427954499; Tag='silent-meteor-ubuntu-v0.5.16'; App='Silent-Meteor'; Purpose='Display configurable meteor animations and supported media screensaver visuals on Ubuntu.' },
    @{ Id=427954573; Tag='system-chronicle-windows-v0.4.4'; App='System-Chronicle'; Purpose='Track locally available system performance and temperature readings over time to investigate load and overheating on Windows.' },
    @{ Id=427955050; Tag='system-chronicle-ubuntu-v0.4'; App='System-Chronicle'; Purpose='Track locally available system performance and temperature readings over time to investigate load and overheating on Ubuntu.' },
    @{ Id=427955131; Tag='cylon-raider-windows-v0.4.0'; App='Cylon-Raider'; Purpose='Observe locally measured network traffic by application, inspect live activity and review history and reports, without Raider itself initiating network connections.' }
)

$login = (& gh.exe api user --jq '.login')
if ($LASTEXITCODE -ne 0 -or ($login.Trim() -cne 'scientistsunite')) { throw 'Unexpected GitHub account.' }
$full = (& gh.exe api "repos/$repo" --jq '.full_name')
if ($LASTEXITCODE -ne 0 -or ($full.Trim() -cne $repo)) { throw 'Unexpected repository.' }

# This script is intended to run only AFTER the app license PR is merged.
foreach ($app in @('Archive-Reader','Battery-Charge-Control','Cylon-Detector','Cylon-Raider','Silent-Meteor','System-Chronicle')) {
    $null = & gh.exe api "repos/$repo/contents/$app/LICENSE?ref=main" --jq '.sha' 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Missing app MIT LICENSE on main: $app. Merge the documentation PR first." }
}

$items = @()
foreach ($e in $entries) {
    $raw = & gh.exe api "repos/$repo/releases/$($e.Id)"
    if ($LASTEXITCODE -ne 0) { throw "Could not read release: $($e.Tag)" }
    $r = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($r.tag_name -cne $e.Tag -or $r.draft -or [string]::IsNullOrEmpty($r.body)) {
        throw "Release identity/body mismatch: $($e.Tag)"
    }
    $before = [string]$r.body
    $after = $before
    $url = "https://github.com/$repo/blob/main/$($e.App)/LICENSE"
    $notice = "Project-owned material in the public repository is under the [MIT License]($url), subject to separate third-party rights. Historical downloadable packages retain their own included notices; this text update neither modifies their files nor automatically relicenses them."
    $after = $after.Replace($legacy,$notice).Replace($otherLegacy,$notice)
    if ($e.App -eq 'Cylon-Raider') {
        $after = $after.Replace('Original v0.4.0 release notes, preserved verbatim','Historical v0.4.0 release notes (licensing statement updated)')
    }
    if (-not $after.Contains('## Intended use')) {
        $nl = [string][char]10
        if ($after.Contains(([string][char]13 + [string][char]10))) { $nl = [string][char]13 + [string][char]10 }
        $pos = $after.IndexOf($nl)
        if ($pos -lt 0) { throw "Missing heading: $($e.Tag)" }
        $after = $after.Insert($pos + $nl.Length, ($nl + '## Intended use' + $nl + $e.Purpose + $nl))
    }
    if ($after.Contains($legacy) -or $after.Contains($otherLegacy) -or -not $after.Contains($url)) {
        throw "Licensing replacement incomplete: $($e.Tag)"
    }
    $items += [PSCustomObject]@{ Id=$e.Id; Tag=$e.Tag; Original=$before; Updated=$after; Changed=($before -cne $after) }
}

$pending = @($items | Where-Object { $_.Changed })
Write-Host "PASS: $($items.Count) public release IDs/tags verified; $($pending.Count) descriptions need editing."
foreach ($p in $pending) { Write-Host "  $($p.Tag)" }
if (-not $Apply) { Write-Host 'DRY RUN. Use -Apply after review to update release descriptions.'; return }

foreach ($p in $pending) {
    # Never overwrite edits made between preflight and PATCH.
    $liveRaw = & gh.exe api "repos/$repo/releases/$($p.Id)"
    if ($LASTEXITCODE -ne 0) { throw "Pre-PATCH recheck failed: $($p.Tag)" }
    $live = ($liveRaw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($live.tag_name -cne $p.Tag -or [string]$live.body -cne $p.Original) {
        throw "Release description changed since preflight; stop: $($p.Tag)"
    }

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('eiren-release-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $json = @{ body = $p.Updated } | ConvertTo-Json -Depth 4
        [IO.File]::WriteAllText($temp,$json,(New-Object System.Text.UTF8Encoding($false)))
        $responseRaw = & gh.exe api --method PATCH "repos/$repo/releases/$($p.Id)" --input $temp
        if ($LASTEXITCODE -ne 0) { throw "PATCH failed: $($p.Tag)" }
        $response = ($responseRaw -join [Environment]::NewLine) | ConvertFrom-Json
        if ($response.tag_name -cne $p.Tag -or [string]$response.body -cne $p.Updated) {
            throw "Post-PATCH verification failed: $($p.Tag)"
        }
        Write-Host "UPDATED: $($p.Tag)" -ForegroundColor Green
    } finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    }
}
Write-Host 'PASS: Existing Release description text updated; assets, tags and downloads untouched.' -ForegroundColor Green
