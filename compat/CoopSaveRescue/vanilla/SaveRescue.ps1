# Coop Save Rescue - turns the rescue on or off for the plain Coop dedicated server
# (BannerlordCoopServer.exe). That server only loads Native, SandBox, Coop and
# DedicatedServer.Windows, so the rescue is added as an extra part of
# DedicatedServer.Windows for one start, and removed again afterwards.
param([switch]$Remove)
$ErrorActionPreference = 'Stop'

function Finish([string]$text, [int]$code) {
    Write-Host ''
    Write-Host $text
    Write-Host ''
    Read-Host 'Press Enter to close'
    exit $code
}

$here = $PSScriptRoot
$module = Join-Path $here 'engine\Modules\DedicatedServer.Windows'
$xmlPath = Join-Path $module 'SubModule.xml'
$binDir = Join-Path $module 'bin\Win64_Shipping_Server'
$dllTarget = Join-Path $binDir 'CoopSaveRescue.dll'
$dllSource = Join-Path $here 'CoopSaveRescue.dll'
# A one-start rescue by hand runs every fix (full mode); without this file the module only runs the automatic ones.
$modeFile = Join-Path $binDir 'CoopSaveRescue.mode'

if (-not (Test-Path -LiteralPath (Join-Path $here 'BannerlordCoopServer.exe')) -or -not (Test-Path -LiteralPath $xmlPath)) {
    Finish ("Put these files in the DedicatedServer folder (next to BannerlordCoopServer.exe), then run this again.`nThis folder is: " + $here) 1
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$xml = [System.IO.File]::ReadAllText($xmlPath)
$nl = if ($xml.Contains("`r`n")) { "`r`n" } else { "`n" }
$pattern = '\s*<SubModule>\s*<Name value="Coop Save Rescue"/>[\s\S]*?</SubModule>'

if ($Remove) {
    $clean = [regex]::Replace($xml, $pattern, '')
    if ($clean -ne $xml) { [System.IO.File]::WriteAllText($xmlPath, $clean, $utf8) }
    if (Test-Path -LiteralPath $dllTarget) {
        try { Remove-Item -LiteralPath $dllTarget -Force }
        catch { Finish 'Could not remove CoopSaveRescue.dll. Close the server window first, then run Remove-SaveRescue again.' 1 }
    }
    if (Test-Path -LiteralPath $modeFile) { Remove-Item -LiteralPath $modeFile -Force }
    Finish 'Save Rescue is OFF. Start the server normally. Players can join again.' 0
}

if (-not (Test-Path -LiteralPath $dllSource)) {
    Finish 'CoopSaveRescue.dll is missing next to this script. Unzip the whole Save Rescue zip again.' 1
}
Copy-Item -LiteralPath $dllSource -Destination $dllTarget -Force
try { Unblock-File -LiteralPath $dllTarget } catch { }
[System.IO.File]::WriteAllText($modeFile, 'full', $utf8)
if (-not [regex]::IsMatch($xml, $pattern)) {
    $entry = '        <SubModule>' + $nl +
             '            <Name value="Coop Save Rescue"/>' + $nl +
             '            <DLLName value="CoopSaveRescue.dll"/>' + $nl +
             '            <SubModuleClassType value="CoopSaveRescue.SubModule"/>' + $nl +
             '        </SubModule>' + $nl
    $at = $xml.IndexOf('    </SubModules>')
    if ($at -lt 0) { Finish 'DedicatedServer.Windows\SubModule.xml looks different than expected. Nothing was changed.' 1 }
    [System.IO.File]::WriteAllText($xmlPath, $xml.Insert($at, $entry), $utf8)
}
Finish ("Save Rescue is ON.`n" +
    "1. Start the server like always (BannerlordCoopServer.exe).`n" +
    "2. Wait for the line: [CoopSaveRescue] Report: ...  (about a minute after SERVING)`n" +
    "3. Type  save  in the server window and press Enter.`n" +
    "4. Press F10 to stop the server, then run Remove-SaveRescue.cmd.`n" +
    "Players cannot join while Save Rescue is on.") 0
