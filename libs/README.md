# /libs - modding-stack DLLs (you copy these in once)

The build references these DLLs but they aren't committed (licenses/size). Source them from
your own client install - you need these mods installed to playtest anyway.

Easiest route (r2modman or Thunderstore App):

1. Create a profile for Valheim, install these packages:
   - `denikson-BepInExPack_Valheim`
   - `ValheimModding-Jotunn`
   - `MSchmoecker-MultiUserChest`
2. Profile folder → copy into this `libs/` folder:

| File | From (inside the profile) |
|---|---|
| `BepInEx.dll` | `BepInEx/core/BepInEx.dll` |
| `0Harmony.dll` | `BepInEx/core/0Harmony.dll` |
| `Jotunn.dll` | `BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll` |
| `MultiUserChest.dll` | `BepInEx/plugins/MSchmoecker-MultiUserChest/MultiUserChest.dll` |

(Manual BepInEx install works too - same files, `BepInEx/core` + `BepInEx/plugins` in the game folder.)

Only `BepInEx.dll` is needed for the P0 smoke build; the other three become required in P1.
