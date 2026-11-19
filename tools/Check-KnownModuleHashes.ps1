<#
.SYNOPSIS
Rechecks every line of KnownModuleHashes.txt against the DLL builds kept in git history.

.DESCRIPTION
    powershell -ExecutionPolicy Bypass -File tools\Check-KnownModuleHashes.ps1

The reports call a module DLL "our build" when its SHA256 is in the tools' list, so a line nobody can trace back
would make a copy look like ours. This hashes every DLL blob in the history of all branches (git log --all) and
says for each line of src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt whether such a build exists,
with the same file name.

Builds of the Workshop only modules (workshopModuleDlls in tools\release-assets.json: Diplomacy, Limitless and
Stuck Battle Fix Coop Compat) are packed by their Build.cmd and never committed, so their lines are listed as
Workshop and do not fail the check; compare them with the uploaded package when in doubt. Every other line must
match a blob. The exit code is 1 when one does not, else 0.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')
$root = Split-Path -Parent $PSScriptRoot
$list = Join-Path $root 'src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt'
$workshop = @((Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json).workshopModuleDlls)

# Every DLL blob in the history, with the file names it had. -m takes merges too: a build first committed in a merge is in no other diff.
$blobs = @{}
foreach ($line in (& git -C $root log --all -m --format= --raw --no-abbrev -- '*.dll')) {
    $parts = $line -split '\s+'
    if ($parts.Count -lt 6 -or $parts[3] -match '^0+$') { continue }
    $name = Split-Path -Leaf $parts[5]
    if (-not $blobs.ContainsKey($parts[3])) { $blobs[$parts[3]] = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase) }
    [void] $blobs[$parts[3]].Add($name)
}

# Their SHA256, read from git cat-file as bytes (a PowerShell pipe would turn them into text).
$history = @{}
$sha = [System.Security.Cryptography.SHA256]::Create()
foreach ($blob in $blobs.Keys) {
    $start = New-Object System.Diagnostics.ProcessStartInfo('git', "-C `"$root`" cat-file blob $blob")
    $start.RedirectStandardOutput = $true
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    $hash = [System.BitConverter]::ToString($sha.ComputeHash($process.StandardOutput.BaseStream)).Replace('-', '').ToLowerInvariant()
    $process.WaitForExit()
    foreach ($name in $blobs[$blob]) { $history["$hash  $name"] = $true }
}

$missing = 0
foreach ($line in [System.IO.File]::ReadAllLines($list)) {
    $text = $line.Trim()
    if (-not $text -or $text.StartsWith('#')) { continue }
    $parts = @($text -split '\s+', 2)
    # A line with a hash and no file name can't be traced to a build, so it counts as missing.
    if ($parts.Count -lt 2) { Write-Host "  MISSING   $text has no file name" -ForegroundColor Red; $missing++; continue }
    $name = $parts[1]
    if ($history.ContainsKey($parts[0].ToLowerInvariant() + '  ' + $name)) { continue }
    if ($workshop -contains $name) { Write-Host "  Workshop  $text (a Build.cmd package, not committed)" -ForegroundColor DarkGray; continue }
    Write-Host "  MISSING   $text has no DLL build with that hash and name in git history" -ForegroundColor Red
    $missing++
}
if ($missing -gt 0) { Write-Host "$missing line(s) of KnownModuleHashes.txt match no build in git history." -ForegroundColor Red; exit 1 }
Write-Host 'Every line of KnownModuleHashes.txt matches a build in git history (or is a Workshop package build).' -ForegroundColor Green
exit 0
