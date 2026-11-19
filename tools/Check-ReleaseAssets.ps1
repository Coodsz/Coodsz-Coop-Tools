<#
.SYNOPSIS
Checks published GitHub releases against tools\release-assets.json.

.DESCRIPTION
Run it after every upload. A release only counts as done when it passes.

    .\tools\Check-ReleaseAssets.ps1                                       (latest release of every repo in the json)
    .\tools\Check-ReleaseAssets.ps1 -Repo Coodsz/Coodsz-TAOM              (latest release of one repo)
    .\tools\Check-ReleaseAssets.ps1 -Repo Coodsz/Coodsz-TAOM -Tag v1.1.45 (one release)

It reads the release's files with the GitHub CLI (gh), and for every file in the
json says OK, MISSING, WRONG (the file's SHA256 on GitHub is not the one in
the json), UNCHECKED (the json pins a SHA256 but GitHub shows none) or CONTENTS (a zip
does not hold the files the json lists: a two exe zip must hold exactly its two exes, a
legacy zip the old exe names; it downloads those zips to check, -SkipZipContents leaves that
out, but not for a release whose signatures must be checked: that release is WRONG, and
-RequireSigned with -SkipZipContents stops before any check). Legacy zips are marked TEMPORARY. Optional files only give a WARNING.

Signing: from the release named by signedFromVersion in the json (any release with -RequireSigned), every exe inside
the two exe zip and the legacy zips must have a Valid Authenticode signature from the json's signedBy name
(CN=Youssef Osseili) and a timestamp, otherwise WRONG. A release page body that says UNSIGNED TEST BUILD
(made with -SkipSigning) is always WRONG. Older, unsigned releases pass because signedFromVersion skips them.

Module payloads: the exes of the two exe zip carry the compat modules as zips inside them. From the json's
moduleLicenseFromVersion every module folder there that holds a Coodsz module DLL (moduleDlls) must carry
LICENSE.txt, and from signedDllsFromVersion (any release with -RequireSigned) every one of those DLLs must be
there with a Valid signature from signedBy and a timestamp, otherwise WRONG.

It also checks the release page, so new players take the right zip. WRONG when an asset with a
label in the json (every legacy zip and the TAOM CompatOnly zips) shows no label or another one on GitHub (run
Set-ReleaseLabels.ps1 first), when the body's first line is not the json's downloadLine, or when a
download picture in the body (images/Coodsz-<Product>-new-download.png) does not show both the
Server Tool and the Client Launcher banner colour of the icons on at least 200 pixels each.

The exit code is 1 when any required file or page check is missing or wrong, 2 when gh could not read
a release or the switches do not go together, else 0.
#>
[CmdletBinding()]
param(
    # owner/name, for example Coodsz/Coodsz-TAOM. Empty: every repo in release-assets.json.
    [string] $Repo = "",

    # Release tag, for example v1.1.45. Empty: the latest release. Needs -Repo.
    [string] $Tag = "",

    # Do not download the zips to check what they hold. Refused when exe signatures must be checked
    # (-RequireSigned, or a release from signedFromVersion on), since they are read from those zips.
    [switch] $SkipZipContents,

    # Check the exe signatures of any release, also one before signedFromVersion in release-assets.json.
    [switch] $RequireSigned
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')

if ($Tag -and -not $Repo) { Write-Host '-Tag needs -Repo.' -ForegroundColor Red; exit 2 }
$refusal = Get-SkipZipContentsRefusal $RequireSigned.IsPresent $SkipZipContents.IsPresent
if ($refusal) { Write-Host "-RequireSigned: $refusal" -ForegroundColor Red; exit 2 }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Write-Host 'The GitHub CLI (gh) is not installed.' -ForegroundColor Red; exit 2 }

# @() around the whole if keeps one repo an array; assigning the if alone unwraps it to a string.
$repos = @(if ($Repo) { $Repo } else { Get-ReleaseAssetRepos })
if ($repos.Count -eq 0) { Write-Host 'release-assets.json lists no repositories.' -ForegroundColor Red; exit 2 }
$failed = $false
$unreadable = $false

foreach ($name in $repos) {
    $arguments = @('release', 'view')
    if ($Tag) { $arguments += $Tag }
    $arguments += @('-R', $name, '--json', 'tagName,assets,body')
    $raw = & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$name : gh could not read the release." -ForegroundColor Red
        $unreadable = $true
        continue
    }
    $release = ($raw -join "`n") | ConvertFrom-Json
    $published = @{}
    $labels = @{}
    foreach ($asset in @($release.assets)) {
        $digest = [string] (Get-JsonValue $asset 'digest' '')
        $published[[string] $asset.name] = $digest.ToLowerInvariant() -replace '^sha256:', ''
        $labels[[string] $asset.name] = [string] (Get-JsonValue $asset 'label' '')
    }

    Write-Host ''
    Write-Host "$name $($release.tagName)" -ForegroundColor Cyan
    $problems = 0
    $spec = @(Get-ReleaseAssetSpec $name $release.tagName)
    $checkSignatures = Test-SignatureRuleApplies (Get-ReleaseRepoInfo $name $release.tagName) $release.tagName -Force:$RequireSigned
    $signedBy = (Get-ReleaseRepoInfo $name $release.tagName).SignedBy
    $issuerPrefix = [string] (Get-JsonValue (Get-SigningConfig) 'issuerCommonNamePrefix' '')
    # The module payloads inside the exes: LICENSE.txt from moduleLicenseFromVersion, signed module DLLs from
    # signedDllsFromVersion (any release with -RequireSigned), both read from the two exe zip.
    $repoInfo = Get-ReleaseRepoInfo $name $release.tagName
    $checkLicense = Test-FromVersion $repoInfo.ModuleLicenseFromVersion $release.tagName
    $checkDlls = [bool] $repoInfo.SignedBy -and $repoInfo.ModuleDlls.Count -gt 0 -and ($RequireSigned -or (Test-FromVersion $repoInfo.SignedDllsFromVersion $release.tagName))
    # A check that must run can't be skipped: the release fails instead of passing with its exes unchecked.
    $refusal = Get-SkipZipContentsRefusal ($checkSignatures -or $checkDlls) $SkipZipContents.IsPresent
    if ($refusal) { Write-Host "  WRONG    $refusal" -ForegroundColor Red; $problems++ }
    if (-not $checkSignatures) { Write-Host '  (exe signatures are not checked for this release: it is before signedFromVersion)' -ForegroundColor DarkGray }
    foreach ($asset in $spec) {
        if (-not $published.ContainsKey($asset.Name)) {
            if ($asset.Required) {
                Write-Host "  MISSING  $($asset.Name)" -ForegroundColor Red
                Write-Host "           $($asset.Why)" -ForegroundColor DarkGray
                $problems++
            }
            else { Write-Host "  WARNING  $($asset.Name) is not attached (optional)" -ForegroundColor Yellow }
            continue
        }
        $digest = $published[$asset.Name]
        if ($asset.Sha256 -and $digest -and $digest -ne $asset.Sha256) {
            Write-Host "  WRONG    $($asset.Name)  SHA256 $digest, expected $($asset.Sha256)" -ForegroundColor Red
            $problems++
        }
        elseif ($asset.Sha256 -and -not $digest) {
            # A pinned file is only right when its bytes are, so no digest is not a pass.
            Write-Host "  UNCHECKED $($asset.Name)  GitHub shows no SHA256 for it (update gh, or upload it again)" -ForegroundColor Red
            $problems++
        }
        elseif ($asset.Sha256) { Write-Host "  OK       $($asset.Name)  SHA256 matches" -ForegroundColor Green }
        else { Write-Host "  OK       $($asset.Name)" -ForegroundColor Green }
        if ($asset.Legacy) { Write-Host "           TEMPORARY legacy zip for tools installed before the two exe release." -ForegroundColor DarkGray }

        # The label tells new players to skip a legacy zip, which sorts above the Tools zip.
        if ($asset.Label -or $asset.Legacy) {
            if ($labels[$asset.Name] -ne $asset.Label) {
                Write-Host "  WRONG    $($asset.Name)  label ""$($labels[$asset.Name])"", expected ""$($asset.Label)"" (run tools\Set-ReleaseLabels.ps1 -Repo $name -Tag $($release.tagName))" -ForegroundColor Red
                $problems++
            }
            else { Write-Host "           label ""$($asset.Label)""" -ForegroundColor Green }
        }

        # What the zip holds: download it once and open it.
        if ((Test-HasZipRule $asset) -and -not $SkipZipContents) {
            $download = Join-Path ([System.IO.Path]::GetTempPath()) ('coodsz-release-check-' + [guid]::NewGuid().ToString('N'))
            try {
                New-Item -ItemType Directory -Force -Path $download | Out-Null
                & gh release download $release.tagName -R $name -p $asset.Name -D $download --clobber
                $zip = Join-Path $download $asset.Name
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $zip)) {
                    Write-Host "  CONTENTS $($asset.Name) could not be downloaded to check what it holds." -ForegroundColor Red
                    $problems++
                }
                else {
                    $wrong = @(Test-ZipContents $zip $asset.Exactly $asset.Holds)
                    foreach ($problem in $wrong) { Write-Host "  CONTENTS $problem" -ForegroundColor Red }
                    if ($wrong.Count -gt 0) { $problems++ }
                    elseif ($asset.Exactly.Count -gt 0) { Write-Host "           holds exactly $($asset.Exactly -join ', ')" -ForegroundColor Green }
                    else { Write-Host "           holds $($asset.Holds -join ', ')" -ForegroundColor Green }
                    if ($checkSignatures -and $wrong.Count -eq 0) {
                        $signatureProblems = @(Test-ZipSignatures $zip $signedBy $issuerPrefix)
                        foreach ($problem in $signatureProblems) { Write-Host "  WRONG    signature: $problem" -ForegroundColor Red }
                        if ($signatureProblems.Count -gt 0) { $problems++ }
                        else { Write-Host "           every exe is signed by $signedBy with a timestamp" -ForegroundColor Green }
                    }
                    if ($asset.Exactly.Count -gt 0 -and $wrong.Count -eq 0 -and ($checkLicense -or $checkDlls)) {
                        $moduleProblems = @(Test-ZipModulePayloads $zip $repoInfo -CheckLicense:$checkLicense -CheckSignatures:$checkDlls -IssuerPrefix $issuerPrefix)
                        foreach ($problem in $moduleProblems) { Write-Host "  WRONG    module: $problem" -ForegroundColor Red }
                        if ($moduleProblems.Count -gt 0) { $problems++ }
                        else {
                            if ($checkLicense) { Write-Host "           every module folder carries LICENSE.txt" -ForegroundColor Green }
                            if ($checkDlls) { Write-Host "           every module DLL is signed by $signedBy with a timestamp" -ForegroundColor Green }
                        }
                    }
                }
            }
            finally { Remove-Item -LiteralPath $download -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
    $known = @($spec | ForEach-Object { $_.Name })
    foreach ($extra in ($published.Keys | Where-Object { $known -notcontains $_ } | Sort-Object)) {
        Write-Host "  extra    $extra (not in release-assets.json)" -ForegroundColor DarkGray
    }

    # The release page: the download line first, and pictures in the icon colours.
    $body = [string] (Get-JsonValue $release 'body' '')
    if ($body.Contains($script:UnsignedMarker)) { Write-Host "  WRONG    body: this release was made with -SkipSigning ($($script:UnsignedMarker)). Build it again signed." -ForegroundColor Red; $problems++ }
    $wrong = @(Test-ReleaseBodyStart $body $name $release.tagName)
    foreach ($problem in $wrong) { Write-Host "  WRONG    body: $problem" -ForegroundColor Red }
    if ($wrong.Count -gt 0) { $problems++ }
    else { Write-Host "  OK       the body starts with the download line" -ForegroundColor Green }
    $pictures = @(Get-DownloadPictureUrls $body $name)
    if ($pictures.Count -eq 0) { Write-Host "  WRONG    body has no download picture" -ForegroundColor Red; $problems++ }
    foreach ($url in $pictures) {
        $png = Join-Path ([System.IO.Path]::GetTempPath()) ('coodsz-release-picture-' + [guid]::NewGuid().ToString('N') + '.png')
        try {
            try { Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $png }
            catch { Write-Host "  WRONG    $url could not be downloaded: $($_.Exception.Message)" -ForegroundColor Red; $problems++; continue }
            $wrong = @(Test-DownloadPicture $png $name)
            foreach ($problem in $wrong) { Write-Host "  WRONG    $problem ($url)" -ForegroundColor Red }
            if ($wrong.Count -gt 0) { $problems++ }
            else { Write-Host "  OK       $(Split-Path -Leaf $url) shows both icon colours" -ForegroundColor Green }
        }
        finally { Remove-Item -LiteralPath $png -Force -ErrorAction SilentlyContinue }
    }
    if ($problems -gt 0) {
        Write-Host "  FAIL: $problems problem(s) with the files, labels or body of $name $($release.tagName)." -ForegroundColor Red
        $failed = $true
    }
    else { Write-Host "  PASS" -ForegroundColor Green }
}

Write-Host ''
if ($failed) { Write-Host 'Release check FAILED. Fix every WRONG and MISSING line before telling players about the release.' -ForegroundColor Red; exit 1 }
if ($unreadable) { Write-Host 'Release check could not read every release.' -ForegroundColor Red; exit 2 }
Write-Host 'Release check passed.' -ForegroundColor Green
exit 0
