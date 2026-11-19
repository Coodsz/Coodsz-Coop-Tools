<#
.SYNOPSIS
Reports which Coodsz build marks are inside a DLL or exe.

.DESCRIPTION
    powershell -ExecutionPolicy Bypass -File tools\Find-CopyMarks.ps1 "C:\path\to\Suspect.dll"

The marks and the module each belongs to are the table in docs\design\copy-marks.md. Each mark is searched as
UTF-8 (the attribute and the type name) and as UTF-16 (the const strings), anywhere in the file. It is the same
search Sign-ModuleDlls.ps1 runs before it signs or records a DLL (Find-CopyMarksIn in ReleaseAssets.ps1). The exit
code is 0 when at least one mark was found, 1 when none was, 2 when the file or the list is missing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Write-Host "No such file: $Path" -ForegroundColor Red; exit 2 }
try { $rows = @(Get-CopyMarkRows) } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }
if ($rows.Count -eq 0) { Write-Host 'No marks found in docs\design\copy-marks.md' -ForegroundColor Red; exit 2 }
$hits = @(Find-CopyMarksIn $Path $rows)

Write-Host (Split-Path -Leaf $Path) -ForegroundColor Cyan
if ($hits.Count -eq 0) {
    Write-Host '  No Coodsz mark found.' -ForegroundColor Yellow
    exit 1
}
foreach ($group in ($hits | Group-Object Module)) {
    Write-Host "  $($group.Name) ($($group.Group[0].Dll)): $($group.Count) of 4 marks" -ForegroundColor Green
    foreach ($hit in $group.Group) { Write-Host "    $($hit.Kind)  $($hit.Mark)" }
}
exit 0
