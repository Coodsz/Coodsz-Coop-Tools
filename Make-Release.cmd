@echo off
rem Double-click to build the Coop and Friends release zip into artifacts\release-<version>.
rem Bundled (non-Workshop) mods come from artifacts\bundled-mods when that folder exists,
rem otherwise from the v1.2.23 release folder. Add -IncludeTaom to also build the TAOM zips.
cd /d "%~dp0"
set "MODS=artifacts\bundled-mods"
if not exist "%MODS%\" set "MODS=artifacts\v1223\friends-stage\Coodsz Launcher and Server\Coodsz Coop and Friends"
if not exist "%MODS%\" (
  echo Could not find the bundled mods. Put the mod folders in artifacts\bundled-mods and try again.
  echo.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\Make-Release.ps1" -BundledMods "%MODS%" %*
echo.
pause
