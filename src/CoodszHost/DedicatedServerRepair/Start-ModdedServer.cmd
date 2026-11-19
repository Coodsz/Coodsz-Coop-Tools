@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-ModdedServer.ps1" %*
set "SERVER_EXIT_CODE=%ERRORLEVEL%"
if not "%SERVER_EXIT_CODE%"=="0" (
    echo.
    echo Modded server exited with code %SERVER_EXIT_CODE%.
    echo Press any key to close . . .
    pause >nul
)
exit /b %SERVER_EXIT_CODE%
