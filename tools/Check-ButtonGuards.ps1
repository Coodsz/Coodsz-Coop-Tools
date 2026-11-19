<#
.SYNOPSIS
    Release check for the button guard rule: busy, starting and not found each say why a button refuses.

.DESCRIPTION
    Fails (exit 1) when any tool under src has:
      - the mixed guard `_busy || _gameRoot`, which shows "Bannerlord was not found" while the tool is busy;
      - in a tool that has a `_startingServer` flag, an `if (_busy` guard that doesn't also test `_startingServer`,
        so the action can run on the server folder while Start server is preparing or repairing it.
    A guard that really doesn't touch the server folder opts out with a comment on the same line or the line
    above: `// guard ok: <why>`. Run it from the repo root, or pass -Root.

.EXAMPLE
    & 'C:\Users\Admin\Projects\BannerlordCoopLauncher\tools\Check-ButtonGuards.ps1' -Root C:\Users\Admin\Projects\BCL-twoexe
#>
param([string]$Root = (Split-Path $PSScriptRoot -Parent))

$Root = $Root.TrimEnd('\', '/')
$src = Join-Path $Root 'src'
# A wrong -Root must not read as a clean check.
if (-not (Test-Path -LiteralPath $src -PathType Container)) {
    Write-Host "There is no src folder in $Root, so the button guards were not checked." -ForegroundColor Red
    exit 2
}
$hits = New-Object System.Collections.Generic.List[string]
$files = Get-ChildItem -LiteralPath $src -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

# Tools (project folders) that have a starting flag at all.
$starting = $files | Where-Object { Select-String -LiteralPath $_.FullName -SimpleMatch '_startingServer' -Quiet } |
    ForEach-Object { $_.Directory.FullName } | Sort-Object -Unique

foreach ($file in $files) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    $hasStarting = $starting | Where-Object { $file.FullName.StartsWith($_ + '\') -or $file.DirectoryName -eq $_ }
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $optOut = $line -match '//\s*guard ok:' -or ($i -gt 0 -and $lines[$i - 1] -match '//\s*guard ok:')
        $where = $file.FullName.Substring($Root.Length + 1) + ':' + ($i + 1)
        if ($line -match '_busy\s*\|\|\s*_gameRoot') {
            $hits.Add("$where mixed busy and not found guard: $($line.Trim())")
        }
        elseif ($hasStarting -and $line -match 'if\s*\(\s*_busy\b' -and $line -notmatch '_startingServer' -and -not $optOut) {
            $hits.Add("$where busy guard without _startingServer: $($line.Trim())")
        }
    }
}

if ($hits.Count -eq 0) {
    Write-Host 'Button guards OK.'
    exit 0
}
Write-Host "Button guard check found $($hits.Count) line(s). Test _startingServer too, or add '// guard ok: <why>' if the action never touches the server folder:"
$hits | ForEach-Object { Write-Host "  $_" }
exit 1
