#!/bin/sh
# Builds CoopSaveRescue.dll (module DedicatedServer.CoopSaveRescue) against netstandard 2.0 and the dedicated server's game DLLs
# (pass the folder with TaleWorlds.*.dll from DedicatedServer\engine\bin\Win64_Shipping_Server).
# Harmony is used through reflection, so no Harmony reference is needed.
set -e
GAME="$1"; NETSTD="${NETSTD:-$(dirname "$(command -v dotnet)")/sdk/*/ref/netstandard.dll}"
CSC=$(ls "$(dirname "$(readlink -f "$(command -v dotnet)")")"/sdk/*/Roslyn/bincore/csc.dll | head -1)
cd "$(dirname "$0")"
dotnet "$CSC" -nologo -target:library -nostdlib -noconfig -langversion:latest -optimize -out:CoopSaveRescue.dll \
  -r:$(ls $NETSTD | head -1) \
  $(for d in TaleWorlds.Library TaleWorlds.Core TaleWorlds.MountAndBlade TaleWorlds.Localization TaleWorlds.CampaignSystem TaleWorlds.ObjectSystem TaleWorlds.SaveSystem; do echo "-r:$GAME/$d.dll"; done) \
  SubModule.cs CopyMark.cs
PAYLOAD=../../src/CoodszHost/SaveRescue/DedicatedServer.CoopSaveRescue
mkdir -p "$PAYLOAD/bin/Win64_Shipping_Server"
cp CoopSaveRescue.dll "$PAYLOAD/bin/Win64_Shipping_Server/"
cp SubModule.xml README.txt LICENSE.txt "$PAYLOAD/"
