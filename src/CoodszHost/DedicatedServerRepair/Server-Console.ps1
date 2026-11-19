param(
    [string] $Command,
    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 30
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$serverRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$dataRoot = Join-Path $serverRoot 'server-data'
$queueRoot = Join-Path $dataRoot 'console-queue'
$resultRoot = Join-Path $dataRoot 'console-results'
[void](New-Item -ItemType Directory -Path $queueRoot -Force)
[void](New-Item -ItemType Directory -Path $resultRoot -Force)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-ServerConsoleCommand {
    param([Parameter(Mandatory = $true)][string] $Text)

    $trimmed = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return
    }
    if ($trimmed.Length -gt 2048) {
        throw 'Commands may not exceed 2048 characters.'
    }

    $id = [Guid]::NewGuid().ToString('N')
    $temporaryPath = Join-Path $queueRoot ($id + '.tmp')
    $commandPath = Join-Path $queueRoot ($id + '.cmd')
    $resultPath = Join-Path $resultRoot ($id + '.result')
    [System.IO.File]::WriteAllText($temporaryPath, $trimmed, $utf8NoBom)
    Move-Item -LiteralPath $temporaryPath -Destination $commandPath

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            $result = [System.IO.File]::ReadAllText($resultPath, $utf8NoBom)
            Remove-Item -LiteralPath $resultPath -Force
            if ($result.StartsWith('OK|', [StringComparison]::Ordinal)) {
                Write-Host $result.Substring(3) -ForegroundColor Green
                return
            }
            if ($result.StartsWith('ERROR|', [StringComparison]::Ordinal)) {
                Write-Host $result.Substring(6) -ForegroundColor Red
                return
            }
            Write-Host $result
            return
        }
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $commandPath -PathType Leaf) {
        Remove-Item -LiteralPath $commandPath -Force
    }
    throw 'The server did not answer. Make sure it is running and has reached the campaign.'
}

if (-not [string]::IsNullOrWhiteSpace($Command)) {
    Invoke-ServerConsoleCommand -Text $Command
    exit 0
}

Write-Host 'Bannerlord Coop Server Console' -ForegroundColor Yellow
Write-Host 'Enter a Bannerlord command. Type exit to close this window.'
Write-Host 'Examples: launcher.time.set Pause  |  launcher.time.set Play'
Write-Host ''
while ($true) {
    $line = Read-Host 'server'
    if ($null -eq $line -or
        $line.Trim().Equals('exit', [StringComparison]::OrdinalIgnoreCase)) {
        break
    }
    try {
        Invoke-ServerConsoleCommand -Text $line
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
}
