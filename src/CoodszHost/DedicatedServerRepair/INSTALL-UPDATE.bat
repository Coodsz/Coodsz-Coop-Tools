@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title HexTool Server Update Installer
echo ============================================================
echo                 HexTool Server Update Installer
echo ============================================================
echo.
echo Offline installer: no downloads, hidden windows or security changes.
echo Every packaged file is checked against payload-sha256.json first.
echo Saves and existing server settings are preserved; changes are backed up.
echo.

set "POWERSHELL_EXE=pwsh.exe"
set "POWERSHELL_POLICY="
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
    set "POWERSHELL_POLICY=-ExecutionPolicy RemoteSigned"
)

if not exist "%~dp0Install-Update.ps1" (
    echo ERROR: Install-Update.ps1 is missing.
    echo Delete this folder and extract the complete update package again.
    set "RESULT=2"
    goto :finished
)

"%POWERSHELL_EXE%" -NoLogo -NoProfile %POWERSHELL_POLICY% -File "%~dp0Install-Update.ps1" %*
set "RESULT=%ERRORLEVEL%"

:finished
echo.
if "%RESULT%"=="0" (
    echo Update completed successfully.
) else (
    echo Install failed with error code %RESULT%.
    echo If Windows says scripts are disabled, right-click the downloaded RAR,
    echo choose Properties, tick Unblock, then extract it into a new folder.
)
echo.
echo Press any key to close . . .
pause >nul
exit /b %RESULT%
