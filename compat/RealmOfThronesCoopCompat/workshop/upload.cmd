@echo off
rem Sends this Realm of Thrones Coop Compat package to the Steam Workshop on the Steam account that is signed in.
rem   upload.cmd create   makes the item, Private (WorkshopCreate.xml)
rem   upload.cmd update   updates it (WorkshopUpdate.xml, written by Pack-RotCompat.ps1 -ItemId <id>)
rem Run it in a real terminal tab (the uploader waits for a key at the end). Only Coodsz runs it.
setlocal
set "BIN=C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client"
set "TASK="
if /i "%~1"=="create" set "TASK=%~dp0WorkshopCreate.xml"
if /i "%~1"=="update" set "TASK=%~dp0WorkshopUpdate.xml"
rem One line ifs on purpose: the ) in Program Files (x86) would end a ( ) block early.
if not defined TASK echo Say create or update, like: upload.cmd update& exit /b 1
if not exist "%TASK%" echo "%TASK%" is missing. Run Pack-RotCompat.ps1 -ItemId with the Workshop item number first.& exit /b 1
if not exist "%BIN%\TaleWorlds.MountAndBlade.SteamWorkshop.exe" echo The Bannerlord uploader is not in "%BIN%".& exit /b 1
rem The uploader needs the game's bin folder as its working folder (steam_appid.txt).
pushd "%BIN%"
"%BIN%\TaleWorlds.MountAndBlade.SteamWorkshop.exe" "%TASK%"
popd
