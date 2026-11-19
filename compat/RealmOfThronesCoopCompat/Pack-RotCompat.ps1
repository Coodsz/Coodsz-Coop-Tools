<#
Packs the committed module\ folder for players, without building anything. Under -OutDir it writes:

  RealmOfThrones.CoopCompat-v<version>.zip      the standalone zip: one RealmOfThrones.CoopCompat folder with
                                                module\ in it, nothing else (no source or obj)
  RealmOfThrones.CoopCompat-workshop\           the Steam Workshop package
    RealmOfThrones.CoopCompat\                  the module folder the uploader sends
    thumbnail.png, workshop-description.txt
    WorkshopCreate.xml                          makes the item, Private, with the full description
    WorkshopUpdate.xml                          only with -ItemId: updates that item
    upload.cmd                                  runs the game's uploader with one of the two

The version comes from module\SubModule.xml. Build.cmd runs this after it fills module\. Only Coodsz uploads.

  powershell -ExecutionPolicy Bypass -File Pack-RotCompat.ps1 [-OutDir <folder>] [-ItemId <id>] [-ChangeNotes <text>]
#>
param(
    [string]$OutDir = '',
    [string]$ItemId = '',
    [string]$ChangeNotes = ''
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$module = Join-Path $here 'module'
$id = 'RealmOfThrones.CoopCompat'
if (-not $OutDir) { $OutDir = Join-Path $here '..\..\artifacts\compat' }
# Relative paths are read from where the PowerShell prompt is, not from the process's folder.
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
if ($ItemId -and $ItemId -notmatch '^[0-9]+$') { throw "-ItemId must be the Workshop item number, not $ItemId" }

$xml = [IO.File]::ReadAllText((Join-Path $module 'SubModule.xml'))
$match = [regex]::Match($xml, '<Version value="v([0-9.]+)"/>')
if (-not $match.Success) { throw "No version in $module\SubModule.xml" }
$version = $match.Groups[1].Value

$workshop = Join-Path $OutDir "$id-workshop"
$package = Join-Path $workshop $id
if (Test-Path -LiteralPath $workshop) { Remove-Item -LiteralPath $workshop -Recurse -Force }
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -Path (Join-Path $module '*') -Destination $package -Recurse -Force

# Windows' own tar writes / in zip entry names. PowerShell's ZipFile and Compress-Archive on
# Windows PowerShell 5 write \, which breaks the zip elsewhere.
$zip = Join-Path $OutDir "$id-v$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
& "$env:SystemRoot\System32\tar.exe" -a -c -f $zip -C $workshop $id
if ($LASTEXITCODE -ne 0) { throw "tar could not write $zip" }

$source = Join-Path $here 'workshop'
foreach ($file in 'thumbnail.png', 'workshop-description.txt', 'upload.cmd') { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $workshop }

# The uploader takes paths as written, so the task files get this package's full paths. It reads the first
# node's children as tasks, so the files start with <Tasks> and carry no <?xml?> line or comment.
function Escape([string]$text) { [Security.SecurityElement]::Escape($text.Replace("`r`n", "`n")).Replace("`n", '&#10;') }
function Fill([string]$name, [string]$target, [hashtable]$values) {
    $text = [IO.File]::ReadAllText((Join-Path $source $name))
    foreach ($key in $values.Keys) { $text = $text.Replace('{' + $key + '}', $values[$key]) }
    if ($text -match '\{[A-Z_]+\}') { throw "$name still has $($Matches[0]) after filling it" }
    [IO.File]::WriteAllText((Join-Path $workshop $target), $text, (New-Object Text.UTF8Encoding $false))
}
$description = [IO.File]::ReadAllText((Join-Path $source 'workshop-description.txt')).Trim()
Fill 'WorkshopCreate.xml' 'WorkshopCreate.xml' @{ PACKAGE = (Escape $package); IMAGE = (Escape (Join-Path $workshop 'thumbnail.png')); DESCRIPTION = (Escape $description) }
if ($ItemId) {
    if (-not $ChangeNotes) { $ChangeNotes = "Realm of Thrones Coop Compat v$version." }
    Fill 'WorkshopUpdate.xml' 'WorkshopUpdate.xml' @{ ITEM_ID = $ItemId; PACKAGE = (Escape $package); CHANGE_NOTES = (Escape $ChangeNotes) }
}

Write-Output "Zip: $zip"
Write-Output "Workshop package: $workshop"
