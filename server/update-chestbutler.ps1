<#
    ChestButler server auto-updater (self-hosted AMP, Windows)

    Checks the latest GitHub release for a new ChestButler.dll. If it differs from
    what the server is running, it gracefully stops the AMP instance, swaps the DLL,
    and starts it again. Safe to run on a schedule; it does nothing when already
    up to date, so it will not restart the server for no reason.

    ONE-TIME SETUP: edit the four values in the CONFIG block below, then register
    this script as a scheduled task (see server/README.md).
#>

# ----------------------------- CONFIG (edit these) -----------------------------
$Repo         = "EladKarni/ChestButler"                                   # GitHub owner/repo
$InstanceName = "Valheim01"                                               # your AMP instance name (ampinstmgr list)
$PluginsDir   = "C:\AMP\Instances\Valheim01\Valheim\896660\BepInEx\plugins" # server plugins folder
$AmpInstMgr   = "C:\Program Files\CubeCoders\AMP\ampinstmgr.exe"          # path to ampinstmgr
# -------------------------------------------------------------------------------

$ErrorActionPreference = "Stop"
$LogFile   = Join-Path $PSScriptRoot "update-chestbutler.log"
$DllName   = "ChestButler.dll"
$TargetDll = Join-Path $PluginsDir $DllName

function Log($msg) {
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

try {
    Log "Checking latest release of $Repo ..."
    $headers = @{ "User-Agent" = "ChestButler-Updater"; "Accept" = "application/vnd.github+json" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
    $asset   = $release.assets | Where-Object { $_.name -eq $DllName } | Select-Object -First 1
    if (-not $asset) { Log "No $DllName asset on the latest release ($($release.tag_name)). Nothing to do."; exit 0 }

    # Download the release DLL to a temp file and compare hashes with what's installed.
    $tmp = Join-Path $env:TEMP "ChestButler_latest.dll"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $tmp
    $newHash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    $curHash = if (Test-Path $TargetDll) { (Get-FileHash $TargetDll -Algorithm SHA256).Hash } else { "none" }

    if ($newHash -eq $curHash) {
        Log "Already up to date ($($release.tag_name)). No restart needed."
        Remove-Item $tmp -Force
        exit 0
    }

    Log "New version found ($($release.tag_name)). Stopping instance '$InstanceName'..."
    & $AmpInstMgr stop $InstanceName | Out-Null

    # Wait for the game server process to release the file lock before overwriting.
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try { [IO.File]::Open($TargetDll, 'Open', 'ReadWrite', 'None').Close(); break }
        catch { if (-not (Test-Path $TargetDll)) { break } }  # file gone = also fine
    }

    Copy-Item $tmp $TargetDll -Force
    Log "Swapped in $DllName ($($release.tag_name)). Starting instance..."
    & $AmpInstMgr start $InstanceName | Out-Null

    Remove-Item $tmp -Force
    Log "Done. Server is now on $($release.tag_name)."
}
catch {
    Log "ERROR: $($_.Exception.Message)"
    # Best effort: make sure the instance is running even if the update failed midway.
    try { & $AmpInstMgr start $InstanceName | Out-Null } catch {}
    exit 1
}
