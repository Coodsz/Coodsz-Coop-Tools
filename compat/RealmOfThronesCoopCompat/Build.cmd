@echo off
rem Builds RealmOfThrones.CoopCompat, fills module\ and packs the module zip into artifacts\compat.
setlocal
cd /d "%~dp0"
set "VER=0.1.0"
set "OUT=%~dp0..\..\artifacts\compat"
set "SHIP=%~dp0module"
set "LOG=%OUT%\realmofthrones-coopcompat-build.log"
if not exist "%OUT%" mkdir "%OUT%"
echo Building RealmOfThrones.CoopCompat... > "%LOG%"
rem The shared link DLL ships next to ours on both sides; it builds into its own bin.
set "LINK=%~dp0..\CoodszCoopLink"
dotnet build "%LINK%\CoodszCoopLink.csproj" -c Release -nodeReuse:false >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo Build failed. See %LOG%
  pause
  exit /b 1
)
dotnet build RealmOfThronesCoopCompat.csproj -c Release -nodeReuse:false -o "%~dp0bin\Release" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo Build failed. See %LOG%
  pause
  exit /b 1
)
rem The same DLLs go in both bin folders: the game reads Win64_Shipping_Client and the
rem dedicated server reads Win64_Shipping_Server. Coodsz.CoopLink.dll is loaded by hand.
rem module\ is committed: the Realm of Thrones tools bundle it as their ServerAddOns and
rem ClientAddOns copy, so rebuild it here and commit it with the source change.
if exist "%SHIP%" rmdir /s /q "%SHIP%"
mkdir "%SHIP%\bin\Win64_Shipping_Client"
if errorlevel 1 goto fail
mkdir "%SHIP%\bin\Win64_Shipping_Server"
if errorlevel 1 goto fail
mkdir "%SHIP%\ModuleData"
if errorlevel 1 goto fail
copy /y SubModule.xml "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y README.txt "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y LICENSE.txt "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "ModuleData\rot_map.txt" "%SHIP%\ModuleData\" >> "%LOG%"
if errorlevel 1 goto fail
rem The server host needs the staging script too, so it ships in the module folder.
copy /y Stage-RotServerMap.ps1 "%SHIP%\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "bin\Release\RealmOfThrones.CoopCompat.dll" "%SHIP%\bin\Win64_Shipping_Client\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "bin\Release\RealmOfThrones.CoopCompat.dll" "%SHIP%\bin\Win64_Shipping_Server\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "%LINK%\bin\Release\Coodsz.CoopLink.dll" "%SHIP%\bin\Win64_Shipping_Client\" >> "%LOG%"
if errorlevel 1 goto fail
copy /y "%LINK%\bin\Release\Coodsz.CoopLink.dll" "%SHIP%\bin\Win64_Shipping_Server\" >> "%LOG%"
if errorlevel 1 goto fail
rem The standalone zip and the Workshop package come from module\ (Pack-RotCompat.ps1, also runs without a build).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Pack-RotCompat.ps1" -OutDir "%OUT%" >> "%LOG%" 2>&1
if errorlevel 1 goto fail
echo BUILD OK >> "%LOG%"
echo Done. %OUT%\RealmOfThrones.CoopCompat-v%VER%.zip and %OUT%\RealmOfThrones.CoopCompat-workshop
pause
exit /b 0

rem A failed copy, xcopy or tar never ends in BUILD OK: the module and the zip would be stale.
:fail
echo PACKING FAILED >> "%LOG%"
echo Packing the module failed. See %LOG%
pause
exit /b 1
