@echo off
rem Builds BurningEmpires.CoopCompat, fills module\ and packs the module zip into artifacts\compat.
setlocal
cd /d "%~dp0"
set "VER=1.0.0"
set "OUT=%~dp0..\..\artifacts\compat"
set "MOD=%OUT%\BurningEmpires.CoopCompat"
set "SHIP=%~dp0module"
set "LOG=%OUT%\burningempires-coopcompat-build.log"
if not exist "%OUT%" mkdir "%OUT%"
echo Building BurningEmpires.CoopCompat... > "%LOG%"
dotnet build BurningEmpiresCoopCompat.csproj -c Release -nodeReuse:false -o "%~dp0bin\Release" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo Build failed. See %LOG%
  pause
  exit /b 1
)
rem module\ is committed: the Burning Empires tools bundle it as their ServerAddOns and
rem ClientAddOns copy, so rebuild it here and commit it with the source change.
if exist "%SHIP%" rmdir /s /q "%SHIP%"
mkdir "%SHIP%\bin\Win64_Shipping_Client"
if errorlevel 1 goto fail
mkdir "%SHIP%\bin\Win64_Shipping_Server"
if errorlevel 1 goto fail
copy /y SubModule.xml "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y README.txt "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y LICENSE.txt "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "bin\Release\BurningEmpires.CoopCompat.dll" "%SHIP%\bin\Win64_Shipping_Client\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "bin\Release\BurningEmpires.CoopCompat.dll" "%SHIP%\bin\Win64_Shipping_Server\" >> "%LOG%"
if errorlevel 1 goto fail
rem Records the DLL hashes in KnownModuleHashes.txt, so the tools' reports list this build. Build.cmd sign
rem signs the DLLs first, for a release or Workshop upload (tools\Sign-ModuleDlls.ps1, signing needs az login).
set "SIGN=-RecordOnly"
if /i "%~1"=="sign" set "SIGN="
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\tools\Sign-ModuleDlls.ps1" "%SHIP%" %SIGN%
if errorlevel 1 goto fail
if exist "%MOD%" rmdir /s /q "%MOD%"
xcopy /e /i /q /y "%SHIP%" "%MOD%" >> "%LOG%"
if errorlevel 1 goto fail
if exist "%OUT%\BurningEmpires.CoopCompat-v%VER%.zip" del /q "%OUT%\BurningEmpires.CoopCompat-v%VER%.zip"
rem Windows' own tar writes / in zip entry names. PowerShell's ZipFile and
rem Compress-Archive on Windows PowerShell 5 write \, which breaks the zip elsewhere.
"%SystemRoot%\System32\tar.exe" -a -c -f "%OUT%\BurningEmpires.CoopCompat-v%VER%.zip" -C "%OUT%" BurningEmpires.CoopCompat >> "%LOG%" 2>&1
if errorlevel 1 goto fail
echo BUILD OK >> "%LOG%"
echo Done. %OUT%\BurningEmpires.CoopCompat-v%VER%.zip
pause
exit /b 0

rem A failed copy, xcopy or tar never ends in BUILD OK: the module and the zip would be stale.
:fail
echo PACKING FAILED >> "%LOG%"
echo Packing the module failed. See %LOG%
pause
exit /b 1
