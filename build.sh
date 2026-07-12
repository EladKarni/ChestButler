#!/usr/bin/env bash
# Build + auto-install into the Thunderstore profile. Output: dist/ChestButler.dll
set -e
export DOTNET_ROOT="$HOME/dotnet/usr/lib/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:$PATH"
cd "$(dirname "$0")"
dotnet build src/ChestButler/ChestButler.csproj -c Release "$@"
# keep only our artifacts in dist (framework refs get copy-localed due to FrameworkPathOverride)
find dist -type f ! -name "ChestButler.dll" ! -name "ChestButler.pdb" -delete
# auto-install into the game profile if mounted (and clean up the old mod name)
PLUGINS="/sessions/trusting-eloquent-euler/mnt/profiles/Default/BepInEx/plugins"
if [ -d "$PLUGINS" ]; then
  rm -f "$PLUGINS/ProjectSorter.dll" "$PLUGINS/ProjectSorter.pdb"
  cp dist/ChestButler.dll dist/ChestButler.pdb "$PLUGINS/" && echo "→ installed to profile plugins"
fi
echo "→ dist/ChestButler.dll"
