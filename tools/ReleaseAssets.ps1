# Shared by Make-Release.ps1, Make-AD1259-Release.ps1, Make-BurningEmpires-Release.ps1, Make-RealmOfThrones-Release.ps1
# and Check-ReleaseAssets.ps1.
# Dot source it: . (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')
# The list itself is tools\release-assets.json.

# Where the live test lock file is. COODSZ_LIVE_TEST_LOCK overrides the path (for tests).
function Get-LiveTestLockPath {
    if ($env:COODSZ_LIVE_TEST_LOCK) { return $env:COODSZ_LIVE_TEST_LOCK }
    Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Coodsz live-test.lock'
}

# A release build must not run while another chat holds the live test lock (it swaps files the build uses).
# Throws when the lock is held, unless -IgnoreLock (only when the lock holder agreed).
function Assert-LiveTestLockFree([switch] $IgnoreLock) {
    $lock = Get-LiveTestLockPath
    if ($IgnoreLock -or -not (Test-Path -LiteralPath $lock)) { return }
    $holder = (@(Get-Content -LiteralPath $lock -ErrorAction SilentlyContinue) | Select-Object -First 1)
    if (-not $holder) { $holder = 'another chat' }
    throw "The live test lock is held by $holder. Wait until it is free, or pass -IgnoreLock if that chat agreed."
}

# The private folder with the files that are not in the public source (CONTRIBUTING.md, Private files): the
# CoodszPrivateRoot environment variable, or private\ in the repo, the same as src\Directory.Build.props.
function Get-PrivateRoot {
    if ($env:CoodszPrivateRoot) { return $env:CoodszPrivateRoot }
    Join-Path (Split-Path -Parent $PSScriptRoot) 'private'
}

# The prebuilt server DLLs the DedicatedServerRepair payload carries, by their path in it, with the SHA-256
# README.md records for each (Files not built from this repository).
$PrivateRepairDlls = [ordered]@{
    'BannerlordCoop.RuntimeBootstrap.dll' = 'cff7a2456f4fcd6fdc6f6b430fbb315cad69fd425059882a875508e4f793441d'
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.LauncherCheats.dll' = '42c1336bc0e3a9a65fd697b43a9cf2ba2ee1ea0d95919f5a2667da01ae5ab069'
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.LauncherBattleSize.dll' = 'ea46daa410802b1c9afe327496a9cf2173d35c455f807f51c8ad2d20a384ac4f'
}

# A release needs the private files: every tool signs Realm reward codes with the real key, and the tools that
# carry DedicatedServerRepair (-RepairDlls) need its three DLLs, else a player's server misses them.
# Throws on the first missing or wrong file; returns the private folder.
# The last day old Realm reward key codes still check, read from RealmRewardCode.OldCodesAcceptedUntilText.
function Get-OldRealmCodesUntil {
    $source = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\BannerlordCoopLauncher.Core\Realm\RealmRewardCode.cs'
    $match = [regex]::Match([IO.File]::ReadAllText($source), 'OldCodesAcceptedUntilText = "(\d{4}-\d{2}-\d{2})"')
    if (-not $match.Success) { throw "OldCodesAcceptedUntilText is missing from $source." }
    return [datetime]::ParseExact($match.Groups[1].Value, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-PrivateFiles([switch] $RepairDlls) {
    $folder = Get-PrivateRoot
    $help = ' A release build needs the private files (CONTRIBUTING.md, Private files).'
    $key = Join-Path $folder 'realm-reward-key.txt'
    if (-not (Test-Path -LiteralPath $key -PathType Leaf)) { throw "The Realm reward key is missing: $key.$help" }
    if ((Get-Content -Raw -LiteralPath $key).Trim() -notmatch '^[0-9A-Fa-f]{64}$') { throw "$key must hold the key as 64 hex digits.$help" }
    # Until the day in RealmRewardCode.OldCodesAcceptedUntilText, codes from the key before the key change still
    # check, so a release needs that old key too, and it must not be the same as the new one.
    $oldKey = Join-Path $folder 'realm-reward-old-key.txt'
    if ((Get-Date).ToUniversalTime().Date -le (Get-OldRealmCodesUntil)) {
        if (-not (Test-Path -LiteralPath $oldKey -PathType Leaf)) { throw "The old Realm reward key is missing: $oldKey. Codes from older tools must still check until $((Get-OldRealmCodesUntil).ToString('yyyy-MM-dd')).$help" }
        $oldText = (Get-Content -Raw -LiteralPath $oldKey).Trim()
        if ($oldText -notmatch '^[0-9A-Fa-f]{64}$') { throw "$oldKey must hold the key as 64 hex digits.$help" }
        if ($oldText -eq (Get-Content -Raw -LiteralPath $key).Trim()) { throw "$oldKey is the same as $key. The new key must be a new one.$help" }
    }
    if ($RepairDlls) {
        foreach ($relative in $PrivateRepairDlls.Keys) {
            $dll = Join-Path (Join-Path $folder 'DedicatedServerRepair') $relative
            if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "A prebuilt server DLL is missing: $dll.$help" }
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash.ToLowerInvariant()
            if ($hash -ne $PrivateRepairDlls[$relative]) { throw "$dll is not the recorded build (SHA-256 $hash, expected $($PrivateRepairDlls[$relative]))." }
        }
    }
    return $folder
}

function Get-ReleaseAssetsFile {
    Join-Path $PSScriptRoot 'release-assets.json'
}

function Get-JsonValue($object, [string] $name, $default = $null) {
    $property = $object.PSObject.Properties[$name]
    if ($null -eq $property -or $null -eq $property.Value) { return $default }
    return $property.Value
}

# The release-assets.json entry for one repository, with {version} filled in.
# Each asset comes back as name, source, path, sha256 (lower case or ''), required, why, exactly and
# holds (the files a zip must hold, see Test-ZipContents) and legacy (a temporary old name zip).
function Get-ReleaseAssetSpec([string] $Repo, [string] $Version) {
    $file = Get-ReleaseAssetsFile
    if (-not (Test-Path -LiteralPath $file)) { throw "The release asset list is missing: $file" }
    $json = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
    $entry = @($json.repos | Where-Object { $_.repo -eq $Repo })
    if ($entry.Count -ne 1) { throw "release-assets.json has no single entry for $Repo." }
    $version = $Version.TrimStart('v', 'V')
    foreach ($asset in $entry[0].assets) {
        [pscustomobject]@{
            Name     = ([string] $asset.name).Replace('{version}', $version)
            Source   = [string] (Get-JsonValue $asset 'source' 'build')
            Path     = [string] (Get-JsonValue $asset 'path' '')
            Sha256   = (Get-AssetSha256ForVersion $asset $Version)
            Required = [bool] (Get-JsonValue $asset 'required' $true)
            Why      = [string] (Get-JsonValue $asset 'why' '')
            Exactly  = @(Get-JsonValue $asset 'exactly' @() | ForEach-Object { [string] $_ })
            Holds    = @(Get-JsonValue $asset 'holds' @() | ForEach-Object { [string] $_ })
            Legacy   = [bool] (Get-JsonValue $asset 'legacy' $false)
            Label    = [string] (Get-JsonValue $asset 'label' '')
        }
    }
}

# The sha256 an asset must have in the release $Version (lower case or ''). From sha256FromVersion on (or always,
# without it) the pinned sha256; before it the first earlierSha256 row whose fromVersion the release reached (empty
# fromVersion = any older release), so a published release keeps passing with the file it was published with.
function Get-AssetSha256ForVersion($asset, [string] $Version) {
    $from = [string] (Get-JsonValue $asset 'sha256FromVersion' '')
    if (-not $from -or (Test-FromVersion $from $Version)) { return ([string] (Get-JsonValue $asset 'sha256' '')).ToLowerInvariant() }
    foreach ($row in @(Get-JsonValue $asset 'earlierSha256' @())) {
        $rowFrom = [string] (Get-JsonValue $row 'fromVersion' '')
        if (-not $rowFrom -or (Test-FromVersion $rowFrom $Version)) { return ([string] $row.sha256).ToLowerInvariant() }
    }
    return ''
}

# One moduleDlls entry is a path, or {"path": ..., "fromVersion": "v1.2.16"} for a DLL the tool only carries from that
# release on, so a published older release that never carried it keeps passing.
function Get-ModuleDllPath($entry) {
    if ($entry -is [string]) { return $entry }
    $path = [string] (Get-JsonValue $entry 'path' '')
    if (-not $path) { throw "A moduleDlls entry in tools\release-assets.json has no ""path"": $($entry | ConvertTo-Json -Compress)" }
    return $path
}

# True when the entry applies to the release $Version. A version that is not a number (the '0' the signing and the
# build checks pass) takes every entry.
function Test-ModuleDllApplies($entry, [string] $Version) {
    if ($entry -is [string]) { return $true }
    $from = [string] (Get-JsonValue $entry 'fromVersion' '')
    return -not $from -or (Test-FromVersion $from $Version)
}

# The per repository release page fields of release-assets.json: Product (Coop, TAOM or AD1259),
# DownloadLine (the first line of every release body, {version} filled in) and the ServerProject and
# ClientProject csproj paths whose ApplicationIcon gives the colours the download picture must show.
function Get-ReleaseRepoInfo([string] $Repo, [string] $Version) {
    $json = Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json
    $entry = @($json.repos | Where-Object { $_.repo -eq $Repo })
    if ($entry.Count -ne 1) { throw "release-assets.json has no single entry for $Repo." }
    $version = $Version.TrimStart('v', 'V')
    [pscustomobject]@{
        Product       = [string] (Get-JsonValue $entry[0] 'product' '')
        DownloadLine  = ([string] (Get-JsonValue $entry[0] 'downloadLine' '')).Replace('{version}', $version)
        ServerProject = [string] (Get-JsonValue $entry[0] 'serverProject' '')
        ClientProject = [string] (Get-JsonValue $entry[0] 'clientProject' '')
        SignedBy      = [string] (Get-JsonValue $entry[0] 'signedBy' '')
        SignedFromVersion = [string] (Get-JsonValue $entry[0] 'signedFromVersion' '')
        ModuleDlls    = @(Get-JsonValue $entry[0] 'moduleDlls' @() | Where-Object { Test-ModuleDllApplies $_ $Version } | ForEach-Object { Get-ModuleDllPath $_ })
        SignedDllsFromVersion = [string] (Get-JsonValue $entry[0] 'signedDllsFromVersion' '')
        ModuleLicenseFromVersion = [string] (Get-JsonValue $entry[0] 'moduleLicenseFromVersion' '')
    }
}

# docs\release-notes\v<version>-<product>.md, the notes the release body is made from. Throws when it is missing.
function Get-ReleaseNotesFile([string] $Repo, [string] $Version) {
    $info = Get-ReleaseRepoInfo $Repo $Version
    $file = Join-Path (Split-Path -Parent $PSScriptRoot) ("docs\release-notes\v" + $Version.TrimStart('v', 'V') + '-' + $info.Product.ToLowerInvariant() + '.md')
    if (-not (Test-Path -LiteralPath $file)) { throw "Write the release notes first: $file is missing." }
    $file
}

# The release body: the download line first, a blank line, then the release notes without their
# leading <!-- ... --> comment (the publishing steps, which players must not see).
function Get-ReleaseBody([string] $Repo, [string] $Version, [switch] $Unsigned) {
    $notes = Get-Content -Raw -Encoding UTF8 -LiteralPath (Get-ReleaseNotesFile $Repo $Version)
    $notes = [regex]::Replace($notes, '^\s*<!--.*?-->\s*', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $body = (Get-ReleaseRepoInfo $Repo $Version).DownloadLine + "`n`n" + $notes.Replace("`r`n", "`n").TrimEnd() + "`n"
    # A -SkipSigning test build says so on the first line, and Check-ReleaseAssets fails a release with that line.
    if ($Unsigned) { $body = "**$($script:UnsignedMarker) - do not publish.**`n`n" + $body }
    $body
}

# Writes release-body.md (for gh release create --notes-file) and release-labels.json (asset name
# to label, what Set-ReleaseLabels.ps1 sets) into the release folder.
function Write-ReleasePageFiles([string] $Repo, [string] $Version, [string] $Folder, [switch] $Unsigned) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $Folder 'release-body.md'), (Get-ReleaseBody $Repo $Version -Unsigned:$Unsigned), $utf8)
    $labels = [ordered]@{}
    foreach ($asset in (Get-ReleaseAssetSpec $Repo $Version)) {
        if ($asset.Label) { $labels[$asset.Name] = $asset.Label }
    }
    [System.IO.File]::WriteAllText((Join-Path $Folder 'release-labels.json'), ($labels | ConvertTo-Json), $utf8)
    Write-FalsePositiveList $Repo $Version $Folder
    Write-Host "Wrote release-body.md, release-labels.json and false-positive-submission.md to $Folder" -ForegroundColor Green
}

# SHA256 of one zip entry, read straight from the zip.
function Get-ZipEntrySha256($Entry) {
    $stream = $Entry.Open()
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { ([System.BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

# The checklist Coodsz follows after publishing, so antivirus tools stop flagging the new exes: every zip
# in the release folder and every exe inside them with its SHA256 and VirusTotal link, then the false
# positive forms of the vendors players report most.
function Get-FalsePositiveText([string] $Repo, [string] $Version, [string] $Folder) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $version = $Version.TrimStart('v', 'V')
    $rows = @()
    $seen = @{}
    foreach ($zip in @(Get-ChildItem -LiteralPath $Folder -Filter '*.zip' -File | Sort-Object Name)) {
        $hash = Get-Sha256 $zip.FullName
        $rows += "| $($zip.Name) | $hash | https://www.virustotal.com/gui/file/$hash |"
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try {
            foreach ($entry in @($archive.Entries | Where-Object { $_.Name -like '*.exe' } | Sort-Object FullName)) {
                $exeHash = Get-ZipEntrySha256 $entry
                if ($seen.ContainsKey($exeHash)) { continue }
                $seen[$exeHash] = $true
                $rows += "| $($entry.Name) (in $($zip.Name)) | $exeHash | https://www.virustotal.com/gui/file/$exeHash |"
            }
        }
        finally { $archive.Dispose() }
    }
    $lines = @(
        "# False positive submission for $Repo v$version",
        '',
        'Antivirus tools sometimes flag a new exe. Send these files to the vendors below once the',
        'release is published. It takes about 10 minutes.',
        '',
        '| File | SHA256 | VirusTotal |',
        '| --- | --- | --- |'
    ) + $rows + @(
        '',
        'A VirusTotal link only shows a result after the zip was uploaded there.',
        '',
        '## Checklist',
        '',
        '- [ ] Upload each zip to VirusTotal at https://www.virustotal.com/gui/home/upload, then open its link above.',
        '- [ ] Microsoft Defender: https://www.microsoft.com/en-us/wdsi/filesubmission',
        '- [ ] Norton: https://submit.norton.com/',
        '- [ ] Avast: https://www.avast.com/false-positive-file-form.php',
        '',
        'On each form, upload the zip, say it is a false positive, and give the release link',
        "https://github.com/$Repo/releases/tag/v$version."
    )
    ($lines -join "`n") + "`n"
}

function Write-FalsePositiveList([string] $Repo, [string] $Version, [string] $Folder) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $Folder 'false-positive-submission.md'), (Get-FalsePositiveText $Repo $Version $Folder), $utf8)
}

# Prints the exact commands that publish the release: create it with release-body.md, label the
# legacy zips, then check the published page.
function Write-PublishCommands([string] $Repo, [string] $Version, [string] $Folder, [string[]] $Files, [switch] $Unsigned) {
    $version = $Version.TrimStart('v', 'V')
    if ($Unsigned) {
        Write-Host ''
        Write-Host "$($script:UnsignedMarker): these $Repo files must not be published, so no publish commands are printed. Build again without -SkipSigning." -ForegroundColor Red
        return
    }
    # BurningEmpires reads as Burning Empires and RealmOfThrones as Realm of Thrones in the title; TAOM, AD1259 and Coop stay as they are.
    $product = ((Get-ReleaseRepoInfo $Repo $version).Product -creplace '(?<=[a-z])(?=[A-Z])', ' ') -creplace ' Of ', ' of '
    $upload = ($Files | ForEach-Object { '"' + $_ + '"' }) -join ' '
    Write-Host ''
    Write-Host "Publish $Repo v$version with these three commands, in this order:" -ForegroundColor Green
    Write-Host "  gh release create v$version -R $Repo --title ""Coodsz $product Tools v$version"" --notes-file ""$(Join-Path $Folder 'release-body.md')"" $upload"
    Write-Host "  .\tools\Set-ReleaseLabels.ps1 -Repo $Repo -Tag v$version"
    # The build was signed, so the check must look at the signatures too, whatever signedFromVersion says.
    Write-Host "  .\tools\Check-ReleaseAssets.ps1 -Repo $Repo -Tag v$version -RequireSigned"
    Write-Host "Then send the files to the antivirus vendors with the checklist in $(Join-Path $Folder 'false-positive-submission.md') (about 10 minutes)." -ForegroundColor Green
}

# The banner fill of the icon a csproj's ApplicationIcon points to, read from the svg next to that
# .ico (assets\icons, the banner is the path that starts at M24 15). For example #791F1F.
function Get-BannerColour([string] $Project) {
    $path = Join-Path (Split-Path -Parent $PSScriptRoot) $Project
    $icon = [regex]::Match((Get-Content -Raw -LiteralPath $path), '<ApplicationIcon>(?<p>[^<]+)</ApplicationIcon>')
    if (-not $icon.Success) { throw "$Project has no ApplicationIcon." }
    $svg = [System.IO.Path]::ChangeExtension((Join-Path (Split-Path -Parent $path) $icon.Groups['p'].Value), '.svg')
    $fill = [regex]::Match((Get-Content -Raw -LiteralPath $svg), '<path d="M24 15[^"]*" fill="(?<c>#[0-9A-Fa-f]{6})"')
    if (-not $fill.Success) { throw "$svg has no banner path with a fill." }
    $fill.Groups['c'].Value.ToUpperInvariant()
}

# How many fully opaque pixels of $Png are within 8 per channel of each colour (#RRGGBB), so a
# resized, smoothed or JPG copy still counts. The last number is how many pixels the picture has.
function Get-ColourPixelCounts([string] $Png, [string[]] $Colours) {
    if (-not ('CoodszColourCount' -as [type])) {
        Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CoodszColourCount {
    const int Tolerance = 8;
    static bool Near(int a, int b) { return Math.Abs(a - b) <= Tolerance; }
    public static int[] Count(string path, int[] colours) {
        var counts = new int[colours.Length + 1];
        using (var bitmap = new Bitmap(path)) {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                var stride = Math.Abs(data.Stride);
                var bytes = new byte[stride * bitmap.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (var y = 0; y < bitmap.Height; y++)
                    for (var x = 0; x < bitmap.Width; x++) {
                        var at = y * stride + x * 4;
                        if (bytes[at + 3] != 255) continue;
                        for (var i = 0; i < colours.Length; i++)
                            if (Near(bytes[at + 2], (colours[i] >> 16) & 255) && Near(bytes[at + 1], (colours[i] >> 8) & 255) && Near(bytes[at], colours[i] & 255)) counts[i]++;
                    }
            }
            finally { bitmap.UnlockBits(data); }
            counts[colours.Length] = bitmap.Width * bitmap.Height;
        }
        return counts;
    }
}
'@
    }
    $values = [int[]] @($Colours | ForEach-Object { [Convert]::ToInt32($_.TrimStart('#'), 16) })
    , [CoodszColourCount]::Count([System.IO.Path]::GetFullPath($Png), $values)
}

# What is wrong with a download picture: it must show both the Server Tool and the Client Launcher
# banner colour, each on at least 0.1% of its pixels and never fewer than 50. Empty when it is right.
function Test-DownloadPicture([string] $Png, [string] $Repo) {
    $info = Get-ReleaseRepoInfo $Repo ''
    $expected = [ordered]@{ 'Server Tool' = (Get-BannerColour $info.ServerProject); 'Client Launcher' = (Get-BannerColour $info.ClientProject) }
    $counts = Get-ColourPixelCounts $Png @($expected.Values)
    $MinimumPixels = [Math]::Max(50, [int] [Math]::Ceiling($counts[$counts.Count - 1] / 1000.0))
    $problems = @()
    $i = 0
    foreach ($tool in $expected.Keys) {
        if ($counts[$i] -lt $MinimumPixels) { $problems += "the download picture shows the $tool icon colour $($expected[$tool]) on $($counts[$i]) pixels, it needs at least $MinimumPixels" }
        $i++
    }
    return $problems
}

# What is wrong with a published release body: its first line that is not empty must be the
# download line. Empty when it is right.
function Test-ReleaseBodyStart([string] $Body, [string] $Repo, [string] $Version) {
    $line = [string] ($Body -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
    $expected = (Get-ReleaseRepoInfo $Repo $Version).DownloadLine
    if ($line.Trim() -ne $expected) { return @("the release body starts with ""$line"", not ""$expected""") }
    return @()
}

# The download picture links in a release body (raw.githubusercontent.com/.../images/Coodsz-<Product>-new-download.png).
function Get-DownloadPictureUrls([string] $Body, [string] $Repo) {
    $product = [regex]::Escape((Get-ReleaseRepoInfo $Repo '').Product)
    @([regex]::Matches($Body, "https://raw\.githubusercontent\.com/[^\s)""']+/images/Coodsz-$product-new-download\.png") | ForEach-Object { $_.Value } | Select-Object -Unique)
}

# True when release-assets.json says what this zip must hold.
function Test-HasZipRule($Asset) {
    $Asset.Exactly.Count -gt 0 -or $Asset.Holds.Count -gt 0
}

# Every repository with a published release. A repo marked unreleased (its first release is not out yet)
# is left out here, so a check without -Repo does not fail on it; -Repo still checks it.
function Get-ReleaseAssetRepos {
    $json = Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json
    @($json.repos | Where-Object { -not [bool] (Get-JsonValue $_ 'unreleased' $false) } | ForEach-Object { [string] $_.repo })
}

function Get-Sha256([string] $path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

# Gets one source = release asset into $Destination: from $Cache when it is there, otherwise from
# the newest release of $Repo that has a file with that name (and keeps that download in $Cache).
# Throws when the file cannot be found or its SHA256 is not the one in release-assets.json.
function Copy-KeptReleaseAsset($Asset, [string] $Repo, [string] $Cache, [string] $Destination) {
    New-Item -ItemType Directory -Force -Path $Cache | Out-Null
    $cached = Join-Path $Cache $Asset.Name
    # A cached copy with the wrong SHA256 (a broken download) is removed and fetched again once.
    if ($Asset.Sha256 -and (Test-Path -LiteralPath $cached) -and (Get-Sha256 $cached) -ne $Asset.Sha256) {
        Write-Host "The cached $($Asset.Name) has the wrong SHA256. Downloading it again." -ForegroundColor Yellow
        Remove-Item -LiteralPath $cached -Force
    }
    if (-not (Test-Path -LiteralPath $cached)) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw "$($Asset.Name) is not in $Cache and the GitHub CLI (gh) is not installed to download it. Put the file in $Cache and run again."
        }
        Write-Host "Looking for $($Asset.Name) on the $Repo releases..." -ForegroundColor DarkGray
        $tags = @(& gh release list -R $Repo --limit 100 --json tagName --jq '.[].tagName')
        if ($LASTEXITCODE -ne 0) { throw "gh release list -R $Repo failed. Run gh auth login, or put $($Asset.Name) in $Cache." }
        $found = $null
        foreach ($tag in $tags) {
            $names = @(& gh release view $tag -R $Repo --json assets --jq '.assets[].name')
            if ($LASTEXITCODE -eq 0 -and $names -contains $Asset.Name) { $found = $tag; break }
        }
        if (-not $found) { throw "No $Repo release has $($Asset.Name). Put the file in $Cache and run again." }
        Write-Host "Downloading $($Asset.Name) from $Repo $found" -ForegroundColor DarkGray
        & gh release download $found -R $Repo -p $Asset.Name -D $Cache --clobber
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cached)) { throw "Downloading $($Asset.Name) from $Repo $found failed." }
    }
    if ($Asset.Sha256) {
        $hash = Get-Sha256 $cached
        if ($hash -ne $Asset.Sha256) {
            throw "$cached has SHA256 $hash, but release-assets.json says $($Asset.Sha256). The file on GitHub is not the expected one: put the right file in $Cache, or fix the json if the file really changed."
        }
    }
    Copy-Item -LiteralPath $cached -Destination (Join-Path $Destination $Asset.Name) -Force
}

# Stops the release when a file of release-assets.json is missing from $Folder, has the wrong
# SHA256, or is a zip without the files the json says it holds (a two exe zip with anything else
# in it too). Optional files (required false) only print a warning.
function Assert-ReleaseFolder([string] $Repo, [string] $Version, [string] $Folder) {
    $problems = @()
    foreach ($asset in (Get-ReleaseAssetSpec $Repo $Version)) {
        $file = Join-Path $Folder $asset.Name
        if (-not (Test-Path -LiteralPath $file)) {
            if ($asset.Required) { $problems += "MISSING  $($asset.Name)  ($($asset.Why))" }
            else { Write-Host "Warning: optional $($asset.Name) is not in $Folder" -ForegroundColor Yellow }
            continue
        }
        if ($asset.Sha256) {
            $hash = Get-Sha256 $file
            if ($hash -ne $asset.Sha256) { $problems += "WRONG    $($asset.Name)  SHA256 $hash, expected $($asset.Sha256)" }
        }
        if (Test-HasZipRule $asset) {
            foreach ($problem in @(Test-ZipContents $file $asset.Exactly $asset.Holds)) { $problems += "CONTENTS $problem" }
        }
    }
    if ($problems.Count -gt 0) {
        throw ("The $Repo release in $Folder is not complete. Fix these before uploading:`n  " + ($problems -join "`n  "))
    }
    Write-Host "Every required $Repo file is in $Folder." -ForegroundColor Green
}

# Makes $Zip from everything in $Folder (not the folder itself), replacing an older copy.
function New-ReleaseZip([string] $Folder, [string] $Zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Folder, $Zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

# The file entries of a zip with / between folders. Folder entries are left out.
function Get-ZipFileNames([string] $Zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try { @($archive.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { $_.FullName.Replace('\', '/') }) }
    finally { $archive.Dispose() }
}

# What is wrong with a zip, against a release-assets.json "exactly" list (the zip holds these files and
# nothing else) or "holds" list (the zip holds at least these). Empty when it is right.
function Test-ZipContents([string] $Zip, [string[]] $Exactly, [string[]] $Holds) {
    $names = @(Get-ZipFileNames $Zip)
    $problems = @()
    $expected = @($Exactly) + @($Holds) | Where-Object { $_ }
    foreach ($name in $expected) {
        if ($names -notcontains $name) { $problems += "$(Split-Path -Leaf $Zip) does not hold $name" }
    }
    if (@($Exactly | Where-Object { $_ }).Count -gt 0) {
        foreach ($name in $names) {
            if ($Exactly -notcontains $name) { $problems += "$(Split-Path -Leaf $Zip) holds $name, and it may only hold $($Exactly -join ', ')" }
        }
    }
    return $problems
}

# Stops the release unless the two exe zip holds exactly these exes in this one folder and nothing else.
function Assert-ToolsZip([string] $Zip, [string] $Folder, [string[]] $Exes) {
    $problems = @(Test-ZipContents $Zip @($Exes | ForEach-Object { "$Folder/$_" }) @())
    if ($Exes.Count -ne 2) { $problems += "A tools zip holds two exes, not $($Exes.Count)." }
    if ($problems.Count -gt 0) { throw ($problems -join "`n") }
}

# Stops the release unless a legacy zip holds every old exe name an installed updater looks for.
function Assert-LegacyZip([string] $Zip, [string[]] $Files) {
    $problems = @(Test-ZipContents $Zip @() $Files)
    if ($problems.Count -gt 0) { throw ($problems -join "`n") }
}

# The updater every release since v1.2.23 ships (191be48 ToolUpdate.Script) removes every top level
# exe and dll of the install folder that its payload root does not carry. So a zip carries the files
# of each tool that can share a folder with it. Add-SiblingPayload copies every file of $From that
# $To lacks. A file both hold must be the same bytes (the shared dlls are one build), or the release stops.
function Add-SiblingPayload([string] $From, [string] $To) {
    $fromRoot = [System.IO.Path]::GetFullPath($From).TrimEnd('\') + '\'
    $clashes = @()
    foreach ($file in (Get-ChildItem -LiteralPath $From -Recurse -File)) {
        $relative = $file.FullName.Substring($fromRoot.Length)
        $target = Join-Path $To $relative
        if (Test-Path -LiteralPath $target) {
            if ((Get-Sha256 $target) -ne (Get-Sha256 $file.FullName)) { $clashes += $relative }
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    if ($clashes.Count -gt 0) {
        throw ("$From and $To both hold these files with different bytes, so one zip cannot carry both tools:`n  " + ($clashes -join "`n  "))
    }
}

# Opens $Zip and finds each exe's payload root the way the shipped updater does (the folder of the
# shortest path to that exe name), then stops the release when the root is not $Root or lacks one of
# the $Holds names. $Expected is a list of @{ Exe; Root; Holds }, Root relative to the zip with / between folders.
function Assert-PayloadRoots([string] $Zip, $Expected) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $paths = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally { $archive.Dispose() }
    $problems = @()
    foreach ($item in $Expected) {
        $found = @($paths | Where-Object { [System.IO.Path]::GetFileName($_) -eq $item.Exe } | Sort-Object Length)
        if ($found.Count -eq 0) { $problems += "$($item.Exe) is not in the zip"; continue }
        if ($found.Count -gt 1 -and $found[0].Length -eq $found[1].Length) { $problems += "$($item.Exe) has two payload roots of the same length"; continue }
        $root = [System.IO.Path]::GetDirectoryName($found[0]).Replace('\', '/')
        if ($root -ne $item.Root) { $problems += "$($item.Exe) takes $root as its payload root, not $($item.Root)"; continue }
        $prefix = if ($root) { $root + '/' } else { '' }
        $top = @($paths | Where-Object { $_.StartsWith($prefix) -and -not $_.Substring($prefix.Length).Contains('/') } | ForEach-Object { $_.Substring($prefix.Length) })
        foreach ($name in $item.Holds) {
            if ($top -notcontains $name) { $problems += "the payload root of $($item.Exe) ($root) lacks $name" }
        }
    }
    if ($problems.Count -gt 0) {
        throw ("$Zip would let the shipped updater remove a tool that shares the folder:`n  " + ($problems -join "`n  "))
    }
    Write-Host "Every payload root in $(Split-Path -Leaf $Zip) carries its sibling tools." -ForegroundColor Green
}

# The top level exe and dll names of a publish folder, the names the shipped updater would remove.
function Get-TopProgramNames([string] $Folder) {
    @(Get-ChildItem -LiteralPath $Folder -File | Where-Object { $_.Extension -in @('.exe', '.dll') } | ForEach-Object { $_.Name })
}

# ---- Code signing (Microsoft Artifact Signing, formerly Trusted Signing) ----
# The release scripts sign every shipped exe after the single file publish and before any zip is made.
# The pinned signtool and Artifact Signing client packages come from nuget.org into tools\.signing
# (gitignored) and are checked against the hash in release-assets.json "signing". Nothing secret is
# stored anywhere: the dlib asks the Azure CLI login (az login) for a token every run.

# Set by Initialize-Signing. Stays $null for a -SkipSigning test build.
$script:SigningContext = $null

# The marker that stops an unsigned test build from being published.
$script:UnsignedMarker = 'UNSIGNED TEST BUILD'

function Get-SigningConfig {
    $json = Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json
    $config = Get-JsonValue $json 'signing'
    if ($null -eq $config) { throw 'release-assets.json has no signing section.' }
    $config
}

function Get-SigningFolder {
    Join-Path $PSScriptRoot '.signing'
}

# The text of metadata.json for the Artifact Signing dlib. Only the account, profile and endpoint names, and the
# list of credential types to leave out so that only AzureCliCredential (az login) is tried. No secrets.
function Get-SigningMetadataJson($Config) {
    $metadata = [ordered]@{
        Endpoint               = [string] $Config.endpoint
        CodeSigningAccountName = [string] $Config.account
        CertificateProfileName = [string] $Config.profile
        ExcludeCredentials     = @($Config.excludeCredentials | ForEach-Object { [string] $_ })
    }
    $metadata | ConvertTo-Json
}

function New-SigningMetadata($Config, [string] $Path) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, (Get-SigningMetadataJson $Config), $utf8)
}

function Get-FileSha512Base64([string] $Path) {
    $algorithm = [System.Security.Cryptography.SHA512]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { [Convert]::ToBase64String($algorithm.ComputeHash($stream)) }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

# Downloads one pinned package from nuget.org (when it is not there yet), checks its SHA512 against the pin,
# and unpacks it fresh into $Folder\<id>.<version>. Returns the unpacked folder.
function Install-SigningPackage($Package, [string] $Folder) {
    $id = [string] $Package.id
    $version = [string] $Package.version
    $nupkg = Join-Path $Folder "$id.$version.nupkg"
    $target = Join-Path $Folder "$id.$version"
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    if ((Test-Path -LiteralPath $nupkg) -and (Get-FileSha512Base64 $nupkg) -ne [string] $Package.sha512) {
        Remove-Item -LiteralPath $nupkg -Force
    }
    if (-not (Test-Path -LiteralPath $nupkg)) {
        $lower = $id.ToLowerInvariant()
        $url = "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg"
        Write-Host "Downloading $id $version from nuget.org" -ForegroundColor DarkGray
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $nupkg
        $hash = Get-FileSha512Base64 $nupkg
        if ($hash -ne [string] $Package.sha512) {
            Remove-Item -LiteralPath $nupkg -Force
            throw "$id $version from nuget.org has SHA512 $hash, but release-assets.json pins $($Package.sha512). Nothing was signed. Do not use that file."
        }
    }
    # Always unpack fresh from the file whose hash was just checked, never reuse a folder that was there before.
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $target)
    $target
}

# The CN value of a certificate name such as 'CN=Youssef Osseili, O=Youssef Osseili, C=US' ('' when there is none).
# A quoted value may hold commas.
function Get-CertCommonName([string] $DistinguishedName) {
    $match = [regex]::Match($DistinguishedName, '(?:^|,\s*)CN=(?:"(?<quoted>[^"]*)"|(?<plain>[^,]*))')
    if (-not $match.Success) { return '' }
    if ($match.Groups['quoted'].Success) { return $match.Groups['quoted'].Value.Trim() }
    $match.Groups['plain'].Value.Trim()
}

# What is wrong with the signer of a signature object, empty when right: the subject CN must equal the CN of
# $SignedBy exactly (a longer name that merely contains it fails), and when $IssuerPrefix is given the issuer
# CN must start with it (the Microsoft ID Verified CA the Artifact Signing certificate chains to).
function Get-SignerProblem($Signature, [string] $SignedBy, [string] $IssuerPrefix = '') {
    $certificate = $Signature.SignerCertificate
    if ($null -eq $certificate) { return 'it has no signer certificate' }
    $subject = Get-CertCommonName $certificate.Subject
    $wanted = Get-CertCommonName $SignedBy
    if (-not $wanted) { $wanted = $SignedBy }
    if ($subject -ne $wanted) { return "it is signed by $($certificate.Subject), not $SignedBy" }
    if ($IssuerPrefix) {
        $issuer = Get-CertCommonName $certificate.Issuer
        if (-not $issuer.StartsWith($IssuerPrefix, [StringComparison]::Ordinal)) { return "its certificate comes from $($certificate.Issuer), not $IssuerPrefix" }
    }
    return ''
}

# True when Microsoft signed the file (the signtool and the dlib both are): Valid, and the subject CN is exactly Microsoft Corporation.
function Test-MicrosoftSigned([string] $Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $signature.Status -eq 'Valid' -and $null -ne $signature.SignerCertificate -and (Get-CertCommonName $signature.SignerCertificate.Subject) -eq 'Microsoft Corporation'
}

function Get-SigningHelp {
    'Run az login and check you have the Artifact Signing Certificate Profile Signer role on coodsz-signing (Azure portal, Access control (IAM), Add role assignment).'
}

# Gets ready to sign: the Azure CLI is installed and logged in, the pinned tools are restored and checked,
# metadata.json is written. Stops with the steps to take when something is missing.
function Initialize-Signing {
    $config = Get-SigningConfig
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'The Azure CLI (az) is not installed, so the release cannot be signed. Run: winget install Microsoft.AzureCLI, open a new terminal, run: az login. Then check you have the Artifact Signing Certificate Profile Signer role on coodsz-signing, and run the release script again.'
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & az account show --output none 2>$null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) {
        throw ('You are not logged in to Azure, so the release cannot be signed. ' + (Get-SigningHelp))
    }
    $folder = Get-SigningFolder
    $signtool = $null
    $dlib = $null
    foreach ($package in @($config.packages)) {
        $root = Install-SigningPackage $package $folder
        foreach ($name in @('signtool', 'dlib')) {
            $relative = [string] (Get-JsonValue $package $name '')
            if (-not $relative) { continue }
            # A full path, so signtool and the dlib load from the same place whatever the current folder is.
            $file = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not (Test-Path -LiteralPath $file)) { throw "$file is missing from $($package.id) $($package.version). The pinned package is checked by its hash and unpacked fresh every run, so the path to it in release-assets.json signing.packages is wrong. Fix that path." }
            if (-not (Test-MicrosoftSigned $file)) { throw "$file is not signed by Microsoft. Delete $folder and run again; if it happens again, stop and tell Coodsz." }
            if ($name -eq 'signtool') { $signtool = $file } else { $dlib = $file }
        }
    }
    if (-not $signtool -or -not $dlib) { throw 'release-assets.json signing.packages must give a signtool and a dlib.' }
    $metadata = Join-Path $folder 'metadata.json'
    New-SigningMetadata $config $metadata
    $script:SigningContext = [pscustomobject]@{ SignTool = $signtool; Dlib = $dlib; Metadata = $metadata; SignedBy = [string] $config.signer; IssuerPrefix = [string] (Get-JsonValue $config 'issuerCommonNamePrefix' ''); Timestamp = [string] $config.timestampUrl }
    Write-Host "Code signing is ready (account $($config.account), profile $($config.profile))." -ForegroundColor Green
}

# The big warning for a -SkipSigning test build.
function Write-UnsignedWarning {
    Write-Host ''
    Write-Host '################################################################' -ForegroundColor Red
    Write-Host "#  $($script:UnsignedMarker): -SkipSigning was used." -ForegroundColor Red
    Write-Host '#  The exes are NOT signed. Do not publish this build.' -ForegroundColor Red
    Write-Host '#  release-body.md says so and Check-ReleaseAssets fails it.' -ForegroundColor Red
    Write-Host '################################################################' -ForegroundColor Red
    Write-Host ''
}

# signtool verify /pa /v on one file, then the signer name. Throws with a plain message when either fails.
function Assert-SignedFile([string] $File) {
    $context = $script:SigningContext
    & $context.SignTool verify /pa /v $File
    if ($LASTEXITCODE -ne 0) { throw ("The signature on $(Split-Path -Leaf $File) did not verify. " + (Get-SigningHelp)) }
    $signature = Get-AuthenticodeSignature -LiteralPath $File
    $wrong = Get-SignerProblem $signature $context.SignedBy $context.IssuerPrefix
    if ($wrong) { throw "$(Split-Path -Leaf $File): $wrong." }
}

# Signs every file with the Artifact Signing dlib, then verifies each one. Any failure stops the release.
function Invoke-ReleaseSigning([string[]] $Files) {
    $context = $script:SigningContext
    if ($null -eq $context) { throw 'Initialize-Signing has not run, so nothing can be signed.' }
    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file)) { throw "Cannot sign $file, it does not exist." }
        Write-Host "Signing $(Split-Path -Leaf $file)" -ForegroundColor Cyan
        & $context.SignTool sign /v /fd SHA256 /tr $context.Timestamp /td SHA256 /dlib $context.Dlib /dmdf $context.Metadata $file
        if ($LASTEXITCODE -ne 0) { throw ("Signing $(Split-Path -Leaf $file) failed. " + (Get-SigningHelp)) }
        Assert-SignedFile $file
    }
}

# Before a zip is made: every exe in the staged folder (the legacy named copies too) must carry a valid
# signature. Nothing to do for a -SkipSigning build.
function Assert-StagedExesSigned([string] $Folder) {
    if ($null -eq $script:SigningContext) { return }
    $exes = @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Filter '*.exe')
    if ($exes.Count -eq 0) { throw "$Folder holds no exe, so there was no signature to check." }
    foreach ($exe in $exes) { Assert-SignedFile $exe.FullName }
}

# What is wrong with the signatures of the exes inside a zip: each must have a Valid Authenticode signature
# whose signer CN is exactly $SignedBy (and issuer CN starts with $IssuerPrefix), and a timestamp. Empty when it is right.
function Test-ZipSignatures([string] $Zip, [string] $SignedBy, [string] $IssuerPrefix = '') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $folder = Join-Path ([System.IO.Path]::GetTempPath()) ('coodsz-signature-check-' + [guid]::NewGuid().ToString('N'))
    $problems = @()
    $zipName = Split-Path -Leaf $Zip
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $folder)
        $exes = @(Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.exe')
        if ($exes.Count -eq 0) { return @("$zipName holds no exe to check") }
        foreach ($exe in $exes) {
            $signature = Get-AuthenticodeSignature -LiteralPath $exe.FullName
            if ($signature.Status -ne 'Valid') { $problems += "$($exe.Name) in $zipName has no valid signature (status $($signature.Status))"; continue }
            $wrong = Get-SignerProblem $signature $SignedBy $IssuerPrefix
            if ($wrong) { $problems += "$($exe.Name) in ${zipName}: $wrong" }
            if ($null -eq $signature.TimeStamperCertificate) { $problems += "$($exe.Name) in $zipName has no timestamp" }
        }
    }
    finally { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
    return $problems
}

# True when Check-ReleaseAssets must check signatures for this release: the repo has a signedBy rule, and the
# release is at or after signedFromVersion (empty = no release is enforced yet), or -Force.
function Test-SignatureRuleApplies($Info, [string] $Tag, [switch] $Force) {
    if (-not $Info.SignedBy) { return $false }
    if ($Force) { return $true }
    return Test-FromVersion $Info.SignedFromVersion $Tag
}

# True when $Tag is at or after $From. An empty or unreadable $From is never reached (the rule is not enforced yet).
# Both are compared by their first x.y[.z[.w]] part, so a tag like v1.3.16-hotfix or rot-v1.0.2 is checked too, and
# a tag with no version in it at all is checked as well rather than let through.
function Test-FromVersion([string] $From, [string] $Tag) {
    if (-not $From) { return $false }
    $fromVersion = $null
    $tagVersion = $null
    if (-not [version]::TryParse([regex]::Match($From, '\d+(\.\d+){1,3}').Value, [ref] $fromVersion)) { return $false }
    if (-not [version]::TryParse([regex]::Match($Tag, '\d+(\.\d+){1,3}').Value, [ref] $tagVersion)) { return $true }
    return $tagVersion -ge $fromVersion
}

# Signatures are read from the downloaded zips, so a release whose signatures must be checked cannot
# pass with -SkipZipContents. Returns the refusal to print, or $null when the run may go on.
function Get-SkipZipContentsRefusal([bool] $CheckSignatures, [bool] $SkipZipContents) {
    if ($CheckSignatures -and $SkipZipContents) {
        return 'signatures NOT checked: they must be checked here, and -SkipZipContents skips the zips they are read from. Run it again without -SkipZipContents.'
    }
    return $null
}

# The Coodsz built module DLLs a release carries inside its exes (moduleDlls in release-assets.json), as full paths.
# Third party DLLs (Harmony, MCM, UIExtenderEx, the HexTool server submodules) and SkyApolo's AD1259 modules are not listed.
function Get-ModuleDllPaths([string] $Repo) {
    $root = Split-Path -Parent $PSScriptRoot
    @((Get-ReleaseRepoInfo $Repo '0').ModuleDlls | ForEach-Object { Join-Path $root $_ })
}

# True when the file has a Valid signature from $SignedBy (issuer CN starting with $IssuerPrefix) with a timestamp.
function Test-SignedBy([string] $Path, [string] $SignedBy, [string] $IssuerPrefix = '') {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $signature.Status -eq 'Valid' -and -not (Get-SignerProblem $signature $SignedBy $IssuerPrefix) -and $null -ne $signature.TimeStamperCertificate
}

# Every module DLL name the signing scripts may sign: the moduleDlls of every repo in release-assets.json and the
# workshopModuleDlls (the Workshop only modules, packed by their own Build.cmd). Anything else is refused.
function Get-SignableModuleDllNames {
    $json = Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json
    $names = @($json.repos | ForEach-Object { @(Get-JsonValue $_ 'moduleDlls' @()) } | ForEach-Object { Split-Path -Leaf (Get-ModuleDllPath $_) })
    $names += @(Get-JsonValue $json 'workshopModuleDlls' @() | ForEach-Object { [string] $_ })
    @($names | Select-Object -Unique)
}

# The module DLLs of every repo in release-assets.json, as full paths. The release scripts sign them all on every
# run, so a byte equal copy another tool carries (the server settings sync in Coodsz Host and the TAOM War Sync) is
# never left behind unsigned.
function Get-AllModuleDllPaths {
    $json = Get-Content -Raw -LiteralPath (Get-ReleaseAssetsFile) | ConvertFrom-Json
    @($json.repos | ForEach-Object { Get-ModuleDllPaths ([string] $_.repo) } | Select-Object -Unique)
}

# Adds "<sha256>  <file name>" lines to Support\KnownModuleHashes.txt (or $HashList), each once, so the tools'
# reports call those builds ours.
function Add-KnownModuleHashes([string[]] $Lines, [string] $HashList = '') {
    if (-not $HashList) { $HashList = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt' }
    $list = [System.Collections.Generic.List[string]]::new([string[]] [System.IO.File]::ReadAllLines($HashList))
    $added = 0
    foreach ($line in $Lines) { if (-not $list.Contains($line)) { $list.Add($line); $added++ } }
    if ($added -gt 0) { [System.IO.File]::WriteAllText($HashList, (($list -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false))) }
    $added
}

# Set when a run signed or copied a module DLL, so the release scripts repeat the commit reminder at the end.
$script:ModuleDllsChanged = 0

# Signs the Coodsz module DLLs in place, so the payloads carry signed DLLs and the known hash list the Core build
# makes (Support\ModuleFingerprints) holds the signed hashes. Signing changes a DLL's bytes and so its SHA256. The
# release flow signs first (tools\Sign-ModuleDlls.ps1 with no folder), commits, runs the tests and then the release,
# which finds every DLL already signed and leaves it as it is. Files that had the same bytes before signing are
# signed once, and the signed file is copied over every tracked DLL in the repository with those bytes (the server
# settings sync in Coodsz Host and the TAOM War Sync, the Menu Guard in compat\CoodszMenuGuard), so byte equal
# copies stay equal on any run. The pre-sign and the signed hashes go into Support\KnownModuleHashes.txt.
# Nothing to do for a -SkipSigning build. -Sign, -IsSigned, -Tracked and -HashList are for tests.
function Invoke-ModuleDllSigning([string[]] $Paths, [scriptblock] $Sign = $null, [scriptblock] $IsSigned = $null, [string[]] $Tracked = $null, [string] $HashList = '') {
    $context = $script:SigningContext
    # A real signing run (not a test) checks the build marks first, like Sign-ModuleDlls, so a swapped DLL on a
    # moduleDlls path is never signed or recorded as a Coodsz build.
    $markRows = $null
    if ($null -eq $Sign) {
        if ($null -eq $context) { return }
        $markRows = @(Get-CopyMarkRows)
        $Sign = {
            param($file)
            & $context.SignTool sign /v /fd SHA256 /tr $context.Timestamp /td SHA256 /dlib $context.Dlib /dmdf $context.Metadata $file
            if ($LASTEXITCODE -ne 0) { throw ("Signing $(Split-Path -Leaf $file) failed. " + (Get-SigningHelp)) }
            Assert-SignedFile $file
        }
        $IsSigned = { param($file) Test-SignedBy $file $context.SignedBy $context.IssuerPrefix }
    }
    if ($null -eq $IsSigned) { $IsSigned = { param($file) $false } }
    if ($null -eq $Tracked) {
        $root = Split-Path -Parent $PSScriptRoot
        $Tracked = @(& git -C $root ls-files -- '*.dll' | ForEach-Object { Join-Path $root $_ })
    }

    # The bytes of every tracked DLL before anything is signed, to find the copies of each file.
    $byHash = @{}
    foreach ($file in @($Paths) + @($Tracked)) {
        if (-not (Test-Path -LiteralPath $file)) { if ($Paths -contains $file) { throw "Cannot sign $file, it does not exist." } else { continue } }
        $full = [System.IO.Path]::GetFullPath($file)
        $hash = Get-Sha256 $full
        if (-not $byHash.ContainsKey($hash)) { $byHash[$hash] = New-Object System.Collections.Generic.List[string] }
        if (-not $byHash[$hash].Contains($full)) { $byHash[$hash].Add($full) }
    }
    $wanted = @($Paths | ForEach-Object { Get-Sha256 ([System.IO.Path]::GetFullPath($_)) } | Select-Object -Unique)
    $lines = @()
    $changed = 0
    foreach ($hash in $wanted) {
        $group = $byHash[$hash]
        $first = $group[0]
        if (& $IsSigned $first) { Write-Host "$(Split-Path -Leaf $first) is already signed" -ForegroundColor DarkGray }
        else {
            if ($null -ne $markRows) {
                $problem = Get-ModuleDllMarkProblem $first $markRows
                if ($problem) { throw $problem }
            }
            Write-Host "Signing $(Split-Path -Leaf $first)" -ForegroundColor Cyan
            & $Sign $first
            $changed++
        }
        foreach ($copy in ($group | Select-Object -Skip 1)) {
            if ((Get-Sha256 $copy) -ne (Get-Sha256 $first)) { Copy-Item -LiteralPath $first -Destination $copy -Force; $changed++ }
        }
        $name = Split-Path -Leaf $first
        $lines += "$hash  $name"
        $lines += (Get-Sha256 $first) + "  $name"
    }
    [void] (Add-KnownModuleHashes $lines $HashList)
    $script:ModuleDllsChanged += $changed
    if ($changed -gt 0) { Write-Host "Signed or copied $changed module DLLs. Commit them and src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt." -ForegroundColor Yellow }
}

# The reminder the release scripts print beside the publish steps when this run signed module DLLs: the release
# flow signs and commits them before the release, so a run that still had to sign leaves them to commit.
function Write-ModuleDllCommitReminder {
    if ($script:ModuleDllsChanged -gt 0) {
        Write-Host ''
        Write-Host "This run signed or copied $($script:ModuleDllsChanged) module DLLs. Commit them and src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt BEFORE you publish, so the repository holds the DLLs the exes carry." -ForegroundColor Yellow
    }
}

# What is wrong with the Coodsz modules a built exe carries in its payload zips (src\CoodszPayload.targets). A
# single file publish keeps the managed dll and its resources uncompressed, so each payload zip is found by its end
# record in the exe bytes. Every module folder (one with a SubModule.xml) that holds one of $DllNames must hold
# LICENSE.txt with -CheckLicense, and with -CheckSignatures each of those DLLs must carry a Valid signature from
# $SignedBy with a timestamp. Returns Problems (lines to print) and Found (the DLL names seen).
function Test-ExeModulePayloads([string] $Exe, [string[]] $DllNames, [switch] $CheckLicense, [switch] $CheckSignatures, [string] $SignedBy = '', [string] $IssuerPrefix = '') {
    Add-Type -AssemblyName System.IO.Compression
    $exeName = Split-Path -Leaf $Exe
    $problems = New-Object System.Collections.Generic.List[string]
    $found = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $bytes = [System.IO.File]::ReadAllBytes($Exe)
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $marker = 'PK' + [char] 5 + [char] 6
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('coodsz-module-check-' + [guid]::NewGuid().ToString('N'))
    try {
        $at = $text.IndexOf($marker, [StringComparison]::Ordinal)
        while ($at -ge 0 -and $at + 22 -le $bytes.Length) {
            $size = [BitConverter]::ToUInt32($bytes, $at + 12)
            $offset = [BitConverter]::ToUInt32($bytes, $at + 16)
            $start = [long] $at - $size - $offset
            $end = [long] $at + 22 + [BitConverter]::ToUInt16($bytes, $at + 20)
            $zip = $null
            if ($start -ge 0 -and $end -le $bytes.Length -and $text.Substring([int] $start, 4) -eq ('PK' + [char] 3 + [char] 4)) {
                try { $zip = New-Object System.IO.Compression.ZipArchive((New-Object System.IO.MemoryStream($bytes, [int] $start, [int] ($end - $start), $false)), [System.IO.Compression.ZipArchiveMode]::Read) }
                catch { $zip = $null }
            }
            if ($null -ne $zip) {
                try {
                    $names = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
                    foreach ($entry in $zip.Entries) { [void] $names.Add($entry.FullName.Replace('\', '/')) }
                    foreach ($entry in $zip.Entries) {
                        if ($DllNames -notcontains $entry.Name) { continue }
                        [void] $found.Add($entry.Name)
                        $path = $entry.FullName.Replace('\', '/')
                        $bin = $path.IndexOf('/bin/', [StringComparison]::OrdinalIgnoreCase)
                        $folder = if ($bin -gt 0) { $path.Substring(0, $bin) } else { '' }
                        if ($CheckLicense -and $folder -and $names.Contains("$folder/SubModule.xml") -and -not $names.Contains("$folder/LICENSE.txt")) {
                            $line = "$exeName carries the module $folder without its LICENSE.txt"
                            if (-not $problems.Contains($line)) { $problems.Add($line) }
                        }
                        if ($CheckSignatures) {
                            New-Item -ItemType Directory -Force -Path $temp | Out-Null
                            $file = Join-Path $temp ([guid]::NewGuid().ToString('N') + '-' + $entry.Name)
                            $stream = $entry.Open()
                            try { $out = [System.IO.File]::Create($file); try { $stream.CopyTo($out) } finally { $out.Dispose() } } finally { $stream.Dispose() }
                            $signature = Get-AuthenticodeSignature -LiteralPath $file
                            if ($signature.Status -ne 'Valid') { $problems.Add("$path in $exeName has no valid signature (status $($signature.Status))") }
                            else {
                                $wrong = Get-SignerProblem $signature $SignedBy $IssuerPrefix
                                if ($wrong) { $problems.Add("$path in ${exeName}: $wrong") }
                                if ($null -eq $signature.TimeStamperCertificate) { $problems.Add("$path in $exeName has no timestamp") }
                            }
                        }
                    }
                }
                finally { $zip.Dispose() }
            }
            $at = $text.IndexOf($marker, $at + 4, [StringComparison]::Ordinal)
        }
    }
    finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    [pscustomobject]@{ Problems = @($problems); Found = @($found) }
}

# The release scripts' check of their built exes: every Coodsz module folder carries its LICENSE.txt, and when the
# DLLs were signed (not -SkipSigning) every module DLL of moduleDlls is in the exes and signed. Throws on a problem.
function Assert-ModulePayloads([string] $Repo, [string[]] $Exes) {
    $info = Get-ReleaseRepoInfo $Repo '0'
    $names = @($info.ModuleDlls | ForEach-Object { Split-Path -Leaf $_ } | Select-Object -Unique)
    $context = $script:SigningContext
    $found = @()
    foreach ($exe in $Exes) {
        $result = if ($null -eq $context) { Test-ExeModulePayloads $exe $names -CheckLicense }
                  else { Test-ExeModulePayloads $exe $names -CheckLicense -CheckSignatures -SignedBy $context.SignedBy -IssuerPrefix $context.IssuerPrefix }
        if ($result.Problems.Count -gt 0) { throw ($result.Problems -join "`n") }
        $found += $result.Found
    }
    foreach ($name in $names) {
        if ($found -notcontains $name) { throw "$name is in moduleDlls for $Repo, but none of the built exes carries it." }
    }
    Write-Host "  The module payloads carry their LICENSE.txt$(if ($null -ne $context) { ' and signed DLLs' })" -ForegroundColor DarkGray
}

# Check-ReleaseAssets' check of the module payloads inside the exes of a published zip (Test-ExeModulePayloads):
# LICENSE.txt in every module folder with -CheckLicense, and with -CheckSignatures every DLL of moduleDlls is
# carried and signed by signedBy with a timestamp. Empty when it is right.
function Test-ZipModulePayloads([string] $Zip, $Info, [switch] $CheckLicense, [switch] $CheckSignatures, [string] $IssuerPrefix = '') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $names = @($Info.ModuleDlls | ForEach-Object { Split-Path -Leaf $_ } | Select-Object -Unique)
    $folder = Join-Path ([System.IO.Path]::GetTempPath()) ('coodsz-module-zip-' + [guid]::NewGuid().ToString('N'))
    $problems = @()
    $found = @()
    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $folder)
        foreach ($exe in @(Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.exe')) {
            $result = Test-ExeModulePayloads $exe.FullName $names -CheckLicense:$CheckLicense -CheckSignatures:$CheckSignatures -SignedBy $Info.SignedBy -IssuerPrefix $IssuerPrefix
            $problems += $result.Problems
            $found += $result.Found
        }
        if ($CheckSignatures) {
            foreach ($name in $names) { if ($found -notcontains $name) { $problems += "no exe in $(Split-Path -Leaf $Zip) carries $name" } }
        }
    }
    finally { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
    return $problems
}

# The build marks of docs\design\copy-marks.md (or $List): one row per mark with Module, Dll, Kind and Mark.
function Get-CopyMarkRows([string] $List = '') {
    if (-not $List) { $List = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\design\copy-marks.md' }
    if (-not (Test-Path -LiteralPath $List)) { throw "The mark list is missing: $List" }
    foreach ($line in Get-Content -LiteralPath $List) {
        $match = [regex]::Match($line, '^\|\s*(?<module>[^|]+?)\s*\|\s*(?<dll>[^|]+?)\s*\|\s*(?<kind>[^|]+?)\s*\|\s*`(?<mark>[^`]+)`\s*\|\s*$')
        if ($match.Success) { [pscustomobject]@{ Module = $match.Groups['module'].Value; Dll = $match.Groups['dll'].Value; Kind = $match.Groups['kind'].Value; Mark = $match.Groups['mark'].Value } }
    }
}

# The rows of $Rows whose mark is inside the file. Latin-1 keeps one character per byte, so a UTF-8 (ASCII) mark is a
# plain substring, and a UTF-16 mark is the same text with a zero after each character.
function Find-CopyMarksIn([string] $Path, $Rows) {
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($Path))
    foreach ($row in $Rows) {
        $wide = -join ($row.Mark.ToCharArray() | ForEach-Object { [string] $_ + [char] 0 })
        if ($text.Contains($row.Mark) -or $text.Contains($wide)) { $row }
    }
}

# What stops a DLL from being signed or recorded as a Coodsz build: it must carry every mark its name has in the
# mark list, so a renamed copy of someone else's DLL is never signed. Empty when it may go on.
function Get-ModuleDllMarkProblem([string] $Path, $Rows) {
    $name = Split-Path -Leaf $Path
    $own = @($Rows | Where-Object { $_.Dll -eq $name })
    if ($own.Count -eq 0) { return "$name has no build marks in docs\design\copy-marks.md, so it is not signed or recorded." }
    $hits = @(Find-CopyMarksIn $Path $own)
    if ($hits.Count -ne $own.Count) { return "$Path holds $($hits.Count) of the $($own.Count) build marks of $name, so it is not a Coodsz build and is not signed or recorded." }
    return ''
}

# True when $Folder is a module package folder of this repository that Sign-ModuleDlls may take: compat\<Module>\module,
# artifacts\compat\<Module> (what the Workshop Build.cmd files pack), a module folder in Coodsz Host's payloads
# (src\CoodszHost\ServerTools, PatchModules, ImageExporter or SaveRescue) or src\TaomCoopHost\WarSync.
function Test-ModulePackageFolder([string] $Folder, [string] $Root = '') {
    if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
    $full = [System.IO.Path]::GetFullPath($Folder).TrimEnd('\')
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $relative = $full.Substring($base.Length)
    return $relative -match '^(compat\\[^\\]+\\module|artifacts\\compat\\[^\\]+|src\\CoodszHost\\(ServerTools|PatchModules|ImageExporter|SaveRescue)\\[^\\]+|src\\TaomCoopHost\\WarSync)$'
}
