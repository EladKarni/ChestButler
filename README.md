# Project Sorter

Valheim mod: designate any chest as a **Sorter** — dump your inventory into it and items
auto-distribute to filtered chests around your base. Multiplayer-safe by construction
(all cross-chest moves are routed through the chest owner via MultiUserChest).

Full build plan with architecture diagrams: [`plan/valheim_sorter_mod_plan.html`](plan/valheim_sorter_mod_plan.html)

## Layout

| Path | What |
|---|---|
| `src/ProjectSorter/` | mod source (BepInEx plugin, net472) |
| `Managed/` | game assemblies copied from `valheim_Data\Managed` (not committed) |
| `libs/` | BepInEx / Jötunn / MultiUserChest DLLs — see `libs/README.md` (not committed) |
| `dist/` | build output → drop `ProjectSorter.dll` into `BepInEx/plugins/` |
| `build.sh` | build script (sandbox/Linux) |

## Testing a build

1. Copy `dist/ProjectSorter.dll` into your client profile's `BepInEx/plugins/`.
2. Launch the game, then check `BepInEx/LogOutput.log` for:
   `[Info : Project Sorter] Project Sorter 0.1.0 loaded`

## Stack

BepInEx 5.4.23.x · HarmonyX · Jötunn · MultiUserChest (hard dependency, MIT) ·
targets `net472`, framework refs resolved from the game's own `Managed/` folder.
