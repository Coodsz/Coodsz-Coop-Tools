@echo off
rem Builds Coodsz.CoopLink.dll into artifacts\compat\Coodsz.CoopLink. A library our compat modules ship in
rem their own bin folders, so no module folder and no zip. Pass CoopBin=<folder> to build against another Coop bin.
setlocal
cd /d "%~dp0"
set "OUT=%~dp0..\..\artifacts\compat"
set "DEST=%OUT%\Coodsz.CoopLink"
set "LOG=%OUT%\coodszcooplink-build.log"
set "PROPS="
if not "%~1"=="" set "PROPS=-p:%~1"
if not exist "%OUT%" mkdir "%OUT%"
echo Building Coodsz.CoopLink... > "%LOG%"
dotnet build CoodszCoopLink.csproj -c Release -o "%~dp0bin\Release" -nodeReuse:false %PROPS% >> "%LOG%" 2>&1
if errorlevel 1 goto fail
rem The server exits with code 4 on a second copy of a pinned Coop assembly, so none may sit next to the link.
for %%F in (Common GameInterface Coop.Core Coop.Steam LiteNetLib protobuf-net protobuf-net.Core) do (
  if exist "bin\Release\%%F.dll" (
    echo A copy of %%F.dll is in the output >> "%LOG%"
    goto fail
  )
)
if exist "%DEST%" rmdir /s /q "%DEST%"
mkdir "%DEST%"
copy /y "bin\Release\Coodsz.CoopLink.dll" "%DEST%\" >> "%LOG%"
copy /y README.txt "%DEST%\" >> "%LOG%"
echo BUILD OK >> "%LOG%"
echo Done. %DEST%\Coodsz.CoopLink.dll
pause
exit /b 0
:fail
echo BUILD FAILED >> "%LOG%"
echo Build failed. See %LOG%
pause
exit /b 1
