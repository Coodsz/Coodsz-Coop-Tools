<#
.SYNOPSIS
Sets the GitHub labels of a published release's files from tools\release-assets.json.

.DESCRIPTION
GitHub sorts release files by name, so the TEMPORARY legacy zips show above the
Coodsz-<Product>-Tools zip. Their label (the text GitHub shows instead of the name)
tells new players they don't need them. Run it after uploading, then run
Check-ReleaseAssets.ps1:

    .\tools\Set-ReleaseLabels.ps1 -Repo Coodsz/Coodsz-TAOM -Tag v1.2.0
    .\tools\Set-ReleaseLabels.ps1 -Repo Coodsz/Coodsz-TAOM -Tag v1.2.0 -WhatIf   (only prints)

It only ever sends a label, never a name: the updaters find their zip by its name, and
they never read the label. A file that already has its label is left alone, so it is
safe to run twice. The exit code is 1 when a labelled file is not on the release or gh
fails, else 0.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # owner/name, for example Coodsz/Coodsz-TAOM.
    [Parameter(Mandatory = $true)] [string] $Repo,

    # Release tag, for example v1.2.0.
    [Parameter(Mandatory = $true)] [string] $Tag
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Write-Host 'The GitHub CLI (gh) is not installed.' -ForegroundColor Red; exit 1 }

# The REST asset ids (gh release view only gives node ids, and the PATCH needs the number).
$raw = & gh api "repos/$Repo/releases/tags/$Tag"
if ($LASTEXITCODE -ne 0) { Write-Host "gh could not read $Repo $Tag." -ForegroundColor Red; exit 1 }
$release = ($raw -join "`n") | ConvertFrom-Json
$published = @{}
foreach ($asset in @($release.assets)) { $published[[string] $asset.name] = $asset }

Write-Host "$Repo $Tag" -ForegroundColor Cyan
$failed = $false
foreach ($asset in @(Get-ReleaseAssetSpec $Repo $Tag | Where-Object { $_.Label })) {
    if (-not $published.ContainsKey($asset.Name)) {
        Write-Host "  MISSING  $($asset.Name) is not on the release, so it has no label." -ForegroundColor Red
        $failed = $true
        continue
    }
    $current = [string] (Get-JsonValue $published[$asset.Name] 'label' '')
    if ($current -eq $asset.Label) { Write-Host "  OK       $($asset.Name) already shows ""$($asset.Label)""" -ForegroundColor Green; continue }
    if (-not $PSCmdlet.ShouldProcess("$($asset.Name) (asset $($published[$asset.Name].id))", "label ""$($asset.Label)"", was ""$current""")) {
        Write-Host "  WOULD SET $($asset.Name) (asset $($published[$asset.Name].id)) to ""$($asset.Label)"", now ""$current""" -ForegroundColor Yellow
        continue
    }
    # Only the label field: a name field here would rename the zip and break every installed updater.
    & gh api -X PATCH "repos/$Repo/releases/assets/$($published[$asset.Name].id)" -f "label=$($asset.Label)" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  FAILED   $($asset.Name): gh could not set the label." -ForegroundColor Red; $failed = $true; continue }
    Write-Host "  SET      $($asset.Name) now shows ""$($asset.Label)""" -ForegroundColor Green
}

if ($failed) { exit 1 }
exit 0
