# Checks the modules a program carries inside it (src\CoodszPayload.targets). The build writes a copy of
# each payload zip when it gets -p:CoodszPayloadCopyTo=<folder>. For every payload this checks that the zip
# holds exactly the files of its source folders with the same SHA256, and that those very zip bytes are
# inside the built exe or dll, so a release can never carry a payload from an older build.
# Dot-source it from a release script; it needs a Fail function.

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PayloadSha256([System.IO.Stream] $stream) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose() }
}

# $sources: pairs of @(<path inside the payload, '' or ending in />, <source folder or file>).
function Assert-Payload([string] $program, [string] $zipFolder, [string] $name, [object[]] $sources) {
    $zipPath = Join-Path $zipFolder "$name.zip"
    if (-not (Test-Path -LiteralPath $program)) { Fail "$program was not built." }
    if (-not (Test-Path -LiteralPath $zipPath)) { Fail "The build made no $name payload for $(Split-Path -Leaf $program)." }

    $expected = @{}
    foreach ($pair in $sources) {
        $prefix = $pair[0]; $source = $pair[1]
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $expected[$prefix + (Split-Path -Leaf $source)] = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
            continue
        }
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { Fail "The source of the $name payload is missing: $source" }
        $full = (Resolve-Path -LiteralPath $source).Path.TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
            $relative = $file.FullName.Substring($full.Length).Replace('\', '/')
            $expected[$prefix + $relative] = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
        }
    }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $found = @{}
        foreach ($entry in $zip.Entries) {
            if ($entry.Name.Length -eq 0) { continue }
            $stream = $entry.Open()
            try { $found[$entry.FullName.Replace('\', '/')] = Get-PayloadSha256 $stream } finally { $stream.Dispose() }
        }
    }
    finally { $zip.Dispose() }

    foreach ($path in $expected.Keys) {
        if (-not $found.ContainsKey($path)) { Fail "The $name payload of $(Split-Path -Leaf $program) lacks $path." }
        if ($found[$path] -ne $expected[$path]) { Fail "The $name payload of $(Split-Path -Leaf $program) has an old or different $path." }
    }
    foreach ($path in $found.Keys) {
        if (-not $expected.ContainsKey($path)) { Fail "The $name payload of $(Split-Path -Leaf $program) has $path, which its sources do not." }
    }

    # The exact zip must be the one inside the program (single file publishes keep the managed dll uncompressed).
    $latin = [System.Text.Encoding]::GetEncoding(28591)
    $programText = $latin.GetString([System.IO.File]::ReadAllBytes($program))
    $zipText = $latin.GetString([System.IO.File]::ReadAllBytes($zipPath))
    if ($programText.IndexOf($zipText, [StringComparison]::Ordinal) -lt 0) {
        Fail "$(Split-Path -Leaf $program) does not carry this build's $name payload."
    }
    Write-Host ("  " + (Split-Path -Leaf $program) + " carries " + $name + " (" + $expected.Count + " files)") -ForegroundColor DarkGray
}
