#!/usr/bin/env bash
# Build + auto-install into the Thunderstore profile. Output: dist/ChestButler.dll
set -e
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd "$(dirname "$0")"

# Locate the .NET SDK. Prefer one already on PATH; otherwise try known install roots
# (the CI/sandbox layout first for backward compat, then a per-user Windows/host install).
if ! command -v dotnet >/dev/null 2>&1; then
  for d in "$DOTNET_ROOT" "$HOME/dotnet/usr/lib/dotnet" "$HOME/.dotnet" "$HOME/dotnet"; do
    if [ -n "$d" ] && { [ -x "$d/dotnet" ] || [ -x "$d/dotnet.exe" ]; }; then
      export DOTNET_ROOT="$d"; export PATH="$d:$PATH"; break
    fi
  done
fi

# fail loudly if the three version declarations ever drift apart (sed = locale-independent)
CSPROJ=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/ChestButler/ChestButler.csproj | head -1)
PLUGIN=$(sed -n 's/.*ModVersion = "\([^"]*\)".*/\1/p' src/ChestButler/Plugin.cs | head -1)
MANIFEST=$(sed -n 's/.*"version_number": "\([^"]*\)".*/\1/p' pkg/manifest.json | head -1)
if [ "$CSPROJ" != "$PLUGIN" ] || [ "$CSPROJ" != "$MANIFEST" ]; then
  echo "VERSION MISMATCH: csproj=$CSPROJ plugin=$PLUGIN manifest=$MANIFEST" >&2
  exit 1
fi

dotnet build src/ChestButler/ChestButler.csproj -c Release --no-incremental "$@"
# keep only our artifacts in dist (framework refs get copy-localed due to FrameworkPathOverride)
find dist -type f ! -name "ChestButler.dll" ! -name "ChestButler.pdb" -delete

# auto-install into the dedicated TEST profile only (leaves Default / Karnimor Server untouched).
TEST_PROFILE="Test"
PROFILES_ROOT=""
for d in \
  "/sessions/trusting-eloquent-euler/mnt/profiles" \
  "$HOME/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles" \
  "$HOME/AppData/Roaming/r2modmanPlus-local/Valheim/profiles" ; do
  if [ -d "$d" ]; then PROFILES_ROOT="$d"; break; fi
done
if [ -n "$PROFILES_ROOT" ]; then
  MGR="$PROFILES_ROOT/$TEST_PROFILE/BepInEx/plugins/EK_Solutions-ChestButler"
  if [ -d "$MGR" ]; then
    if cp dist/ChestButler.dll "$MGR/" 2>/dev/null; then
      echo "-> installed into '$TEST_PROFILE' profile"
    else
      echo "-> SKIPPED (locked - game running?) '$TEST_PROFILE'"
    fi
  else
    echo "-> test profile '$TEST_PROFILE' not found under $PROFILES_ROOT"
  fi
fi

# Thunderstore Mod Manager / r2modman deploy mods from their CACHE and overwrite a hand-copied
# profile DLL on launch. Sync the cached copies too so the freshly built DLL actually loads
# (the manager's displayed version label stays cosmetic; verify by the in-game "loaded" log line).
for TMM in \
  "$HOME/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim" \
  "$HOME/AppData/Roaming/r2modmanPlus-local/Valheim" ; do
  CACHE="$TMM/cache/EK_Solutions-ChestButler"
  if [ -d "$CACHE" ]; then
    find "$CACHE" -name "ChestButler.dll" -exec cp dist/ChestButler.dll {} \; -exec echo "-> synced manager cache: {}" \;
  fi
done
echo "-> dist/ChestButler.dll"
