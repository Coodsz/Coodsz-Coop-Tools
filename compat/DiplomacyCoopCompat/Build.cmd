@echo off
rem Builds Diplomacy.CoopCompat and packs the module zip into artifacts\compat.
setlocal
cd /d "%~dp0"
set "OUT=%~dp0..\..\artifacts\compat"
set "MOD=%OUT%\Diplomacy.CoopCompat"
set "LOG=%OUT%\diplomacy-coopcompat-build.log"
if not exist "%OUT%" mkdir "%OUT%"
echo Building Diplomacy.CoopCompat... > "%LOG%"
dotnet build DiplomacyCoopCompat.csproj -c Release -o "%~dp0bin\Release" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo Build failed. See %LOG%
  pause
  exit /b 1
)
if exist "%MOD%" rmdir /s /q "%MOD%"
mkdir "%MOD%\bin\Win64_Shipping_Client"
mkdir "%MOD%\bin\Win64_Shipping_Server"
copy /y SubModule.xml "%MOD%\" >> "%LOG%"
copy /y README.txt "%MOD%\" >> "%LOG%"
copy /y LICENSE.txt "%MOD%\" >> "%LOG%"
copy /y "bin\Release\Diplomacy.CoopCompat.dll" "%MOD%\bin\Win64_Shipping_Client\" >> "%LOG%"
copy /y "bin\Release\Diplomacy.CoopCompat.dll" "%MOD%\bin\Win64_Shipping_Server\" >> "%LOG%"
rem Records the DLL hashes in KnownModuleHashes.txt, so the tools' reports list this build. Build.cmd sign
rem signs the DLLs first, for a Workshop upload (tools\Sign-ModuleDlls.ps1, signing needs az login).
set "SIGN=-RecordOnly"
if /i "%~1"=="sign" set "SIGN="
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\tools\Sign-ModuleDlls.ps1" "%MOD%" %SIGN%
if errorlevel 1 (
  echo SIGNING FAILED >> "%LOG%"
  echo Signing or recording the DLLs failed. See the lines above.
  pause
  exit /b 1
)
if exist "%OUT%\Diplomacy.CoopCompat-v1.1.1.zip" del /q "%OUT%\Diplomacy.CoopCompat-v1.1.1.zip"
rem Windows' own tar writes / in zip entry names. PowerShell's ZipFile and
rem Compress-Archive on Windows PowerShell 5 write \, which breaks the zip elsewhere.
"%SystemRoot%\System32\tar.exe" -a -c -f "%OUT%\Diplomacy.CoopCompat-v1.1.1.zip" -C "%OUT%" Diplomacy.CoopCompat >> "%LOG%" 2>&1
echo BUILD OK >> "%LOG%"
echo Done. %OUT%\Diplomacy.CoopCompat-v1.1.1.zip
pause
