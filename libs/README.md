# /libs

DLLs the build references but that aren't committed. Copy them in once from your own
client install. You need these mods installed to playtest anyway.

Using r2modman or the Thunderstore app:

1. Make a Valheim profile and install `denikson-BepInExPack_Valheim`, `ValheimModding-Jotunn` and `MSchmoecker-MultiUserChest`.
2. Open the profile folder (Settings, "Browse profile folder") and copy these four files into `libs/`:

| File | Where it lives in the profile |
|---|---|
| `BepInEx.dll` | `BepInEx/core/` |
| `0Harmony.dll` | `BepInEx/core/` |
| `Jotunn.dll` | `BepInEx/plugins/ValheimModding-Jotunn/` |
| `MultiUserChest.dll` | `BepInEx/plugins/MSchmoecker-MultiUserChest/` |

A manual BepInEx install works the same way, the folders are just inside the game
directory instead.
