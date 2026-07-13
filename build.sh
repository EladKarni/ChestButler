#!/usr/bin/env bash
# Build + auto-install into the Thunderstore profile. Output: dist/ChestButler.dll
set -e
export DOTNET_ROOT="$HOME/dotnet/usr/lib/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:$PATH"
cd "$(dirname "$0")"
# fail loudly if the three version declarations ever drift apart
CSPROJ=$(grep -oPm1 '(?<=<Version>)[^<]+' src/ChestButler/ChestButler.csproj)
PLUGIN=$(grep -oPm1 '(?<=ModVersion = ")[^"]+' src/ChestButler/Plugin.cs)
MANIFEST=$(grep -oPm1 '(?<="version_number": ")[^"]+' pkg/manifest.json)
if [ "$CSPROJ" != "$PLUGIN" ] || [ "$CSPROJ" != "$MANIFEST" ]; then
  echo "VERSION MISMATCH: csproj=$CSPROJ plugin=$PLUGIN manifest=$MANIFEST" >&2
  exit 1
fi
dotnet build src/ChestButler/ChestButler.csproj -c Release --no-incremental "$@"
# keep only our artifacts in dist (framework refs get copy-localed due to FrameworkPathOverride)
find dist -type f ! -name "ChestButler.dll" ! -name "ChestButler.pdb" -delete
# auto-install into the game profile if mounted (and clean up the old mod name)
PLUGINS="/sessions/trusting-eloquent-euler/mnt/profiles/Default/BepInEx/plugins"
MGR="$PLUGINS/EK_Solutions-ChestButler"
if [ -d "$MGR" ]; then
  # single copy inside the manager's package folder (avoids a duplicate-GUID loose DLL)
  cp dist/ChestButler.dll "$MGR/" && echo "-> installed into manager folder"
elif [ -d "$PLUGINS" ]; then
  rm -f "$PLUGINS/ProjectSorter.dll" "$PLUGINS/ProjectSorter.pdb"
  cp dist/ChestButler.dll "$PLUGINS/" && echo "-> installed to profile plugins (loose)"
fi
echo "-> dist/ChestButler.dll"
