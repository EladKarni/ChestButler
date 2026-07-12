#!/usr/bin/env bash
#
# ChestButler server auto-updater for self-hosted AMP on Linux.
#
# Polls the GitHub release feed. When a new ChestButler.dll is published it
# backs up the current one, swaps in the new DLL, and restarts the AMP
# instance. Run it from cron or a systemd timer; it does nothing when the
# server is already up to date.
#
# One-time setup:
#   1. Edit the CONFIG block below (instance name + plugins path).
#   2. chmod +x update-chestbutler.sh
#   3. Add a cron entry (see bottom of this file).
#
set -euo pipefail

### CONFIG ##############################################################
REPO="EladKarni/ChestButler"          # GitHub owner/repo that publishes releases
INSTANCE="Valheim01"                  # AMP instance name (as shown by: ampinstmgr status)
# Path to the server's BepInEx plugins folder. Confirm the real path with:
#   find "$HOME/.ampdata" -type d -name plugins 2>/dev/null
PLUGINS_DIR="$HOME/.ampdata/instances/$INSTANCE/Valheim/896660/BepInEx/plugins"

PATCH_ONLY=true                       # only auto-deploy patch updates (1.0.X).
                                      # A minor/major bump locks out players who
                                      # haven't updated, so those are skipped and
                                      # left for you to roll out with the group.

STATE_FILE="$HOME/.chestbutler_deployed"   # remembers the last deployed tag
LOG="$HOME/chestbutler-update.log"
#########################################################################

log(){ echo "$(date '+%F %T')  $*" | tee -a "$LOG" >&2; }

# --- read the latest release tag + the ChestButler.dll asset URL ---
api="https://api.github.com/repos/$REPO/releases/latest"
json="$(curl -fsSL -H 'Accept: application/vnd.github+json' "$api")" \
  || { log "ERROR: GitHub API unreachable"; exit 1; }

tag="$(printf '%s' "$json"    | grep -oP '"tag_name":\s*"\K[^"]+' | head -1)"
dll_url="$(printf '%s' "$json" | grep -oP '"browser_download_url":\s*"\K[^"]*ChestButler\.dll' | head -1)"

if [[ -z "${tag:-}" || -z "${dll_url:-}" ]]; then
  log "ERROR: could not parse release (tag='${tag:-}' url='${dll_url:-}')"; exit 1
fi

current="$(cat "$STATE_FILE" 2>/dev/null || echo none)"
if [[ "$tag" == "$current" ]]; then
  exit 0    # already current; stay quiet for cron
fi

# --- patch-only safety guard ---
mm(){ printf '%s' "${1#v}" | cut -d. -f1-2; }   # -> major.minor
if [[ "$PATCH_ONLY" == "true" && "$current" != "none" && "$(mm "$tag")" != "$(mm "$current")" ]]; then
  log "New release $tag changes the minor/major version (currently $current)."
  log "Players on the old version would be locked out, so this update is being skipped."
  log "Update the clients, then deploy manually or set PATCH_ONLY=false for one run."
  exit 0
fi

# --- download, verify, back up, swap ---
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
curl -fsSL -o "$tmp" "$dll_url"
[[ -s "$tmp" ]] || { log "ERROR: downloaded DLL is empty"; exit 1; }

mkdir -p "$PLUGINS_DIR"
if [[ -f "$PLUGINS_DIR/ChestButler.dll" ]]; then
  cp -f "$PLUGINS_DIR/ChestButler.dll" "$PLUGINS_DIR/ChestButler.dll.bak"
fi
install -m 644 "$tmp" "$PLUGINS_DIR/ChestButler.dll"

# clean up the pre-rename plugin if it is still lying around
rm -f "$PLUGINS_DIR/ProjectSorter.dll" "$PLUGINS_DIR/ProjectSorter.pdb"

# --- restart the AMP instance so the new DLL loads ---
log "Installed $tag. Restarting AMP instance '$INSTANCE'..."
if ! ampinstmgr restart "$INSTANCE"; then
  log "ERROR: ampinstmgr restart failed. New DLL is in place but the server was not bounced."
  exit 1
fi

echo "$tag" > "$STATE_FILE"
log "Done. Server is now on ChestButler $tag."

# ----------------------------------------------------------------------
# Cron example (check every 30 minutes, as the user that runs AMP):
#   crontab -e
#   */30 * * * * /home/amp/update-chestbutler.sh >/dev/null 2>&1
#
# Dependencies (Jotunn, MultiUserChest) change rarely and are not touched
# by this script; install those once by hand. This updater only manages
# ChestButler.dll.
# ----------------------------------------------------------------------
