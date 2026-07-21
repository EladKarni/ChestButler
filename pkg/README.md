# ChestButler

Sorts your storage for you. Mark a chest as a Sorter, dump everything into it, close the lid, and the items get moved to the right chests around your base. Or press **Organize** and tidy the whole base in one go.

## Features

**Sorter chests**
Toggle any chest into a dump chest with one button. Anything inside gets distributed to nearby chests the moment you close it. Items that no chest wants stay in the sorter, so nothing is ever lost or dropped.

**Organize your whole base** *(new in 1.1.0)*
Sorter chests get an **Organize** button. One press scans every chest in range and previews what would move; press again and every item type consolidates into its best home, in place — you carry nothing. Perfect for adopting the mod on an established, messy base.

**Station-aware routing** *(new in 1.1.0)*
During Organize, a chest sitting next to a crafting station attracts that station's materials: metals and ores pool by the forge, cooking ingredients by the cauldron, wood by the workbench, ore and coal by the smelter, meads by the fermenter. The map is editable in config, and modded stations can be added without a rebuild.

**Works with zero setup**
Chests attract items they already hold, fullest chest first. Dump ore after a mining trip and it lands wherever your ore already lives.

**Pin filters**
Put sample items in a chest and press Pin. That chest now claims those item types even when it is empty. An Auto/Manual toggle controls whether the sorter fills it automatically or leaves it alone.

**Pull to restock**
Chests with filters get a Pull button that fetches one stack of each saved item from surrounding storage. Useful for a cooking chest next to the cauldron: press Pull, start cooking.

**Safe in multiplayer**
Every transfer — sorting, Pull, and Organize — runs through MultiUserChest's networking, so only the actual owner of a chest modifies it. No duped stacks, no vanishing items, even with several people using the same storage room.

## Quick Start

1. Open a chest near your storage area and click `Sorter: OFF` so it reads `Sorter: ON`.
2. Dump your whole inventory into it and close the chest.
3. Watch the items fly to whichever chests already hold that item type.
4. For a dedicated chest (for example carrots and onions near the cauldron): put samples in, press `Pin`, and from then on the sorter routes those items there. Press `Pull` any time to grab a stack of each back from storage.
5. Messy base already? Press `Organize` in the sorter chest. It previews how much would move ("move 340 items across 12 chests"), the button turns into `Confirm?`, and a second press runs it — every item type consolidates into its best chest, station materials pool next to their stations.

## Installing

### With a mod manager (recommended)

Install **ChestButler** with the Install button on this page or through r2modman / Thunderstore App. The dependencies (BepInEx, Jötunn, MultiUserChest) install automatically. Launch the game with **Start modded**.

### With a profile code (server groups)

Playing on a server that runs ChestButler? The whole mod setup can be shared as one profile code. In r2modman or the Thunderstore App go to **Import / Update**, pick **Import new profile**, choose **From code** and paste the code your server admin gave you. That installs every mod the server runs, at the exact versions.

The code for the EK_Solutions server is:

```
019f56be-5a3d-1f95-1b1e-9c8aa52a8a6b
```

### Manual

Extract `ChestButler.dll` into `BepInEx/plugins/`. You also need [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/), [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) and [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/) installed the same way.

```
BepInEx/
    plugins/
        ChestButler.dll
        Jotunn.dll
        MultiUserChest.dll
```

### Multiplayer and dedicated servers

The mod must be installed on the server and on every client, at the same version. Anyone missing it or on a different version gets a clear message at connect telling them what to fix, so a half-modded lobby cannot corrupt anything. Crossplay must be disabled; modded servers are Steam only.

On a managed host (for example CubeCoders AMP): enable the BepInEx option in the instance configuration, then upload `ChestButler.dll`, `Jotunn.dll` and `MultiUserChest.dll` to `BepInEx/plugins/` via the file manager.

## Usage

Buttons appear at the bottom of the chest UI:

| Button | Appears on | What it does |
| ------ | ---------- | ------------ |
| `Sorter: ON/OFF` | any chest | makes it a dump chest; contents distribute when you close it |
| `Organize` | sorter chests | sweeps every chest in range and consolidates each item type into its best home; first press previews and turns the button into `Confirm?`, second press (within 5 s) runs it |
| `Pin` / `Auto (n)` / `Manual (n)` | normal chests | Pin saves the current contents as filters. After that the same button toggles Auto (sorter fills this chest) vs Manual (only Pull fills it) |
| `Clear` | chests with filters | erases the saved filters |
| `Pull` | chests with filters | fetches one stack of each saved item type from nearby chests |

### How a target chest is picked

For each item, in order: a chest that pins the item wins first, then a chest whose group covers it, then any chest that already contains some. Ties go to higher sign priority, then to the chest holding the most of that item, then to the nearest one. If the best chest only has room for part of a stack, it gets topped off and the rest re-routes to the next candidate.

### Organize: one-press base cleanup

Organize uses the same ranking with one extra tier: after pins and groups, a chest sitting next to a **crafting station** claims that station's materials, and only then does the most-held rule apply. Each item type gets exactly one winning chest, so nothing ping-pongs.

Default station map (editable in config under `[Stations]`):

| Station | Attracts |
| ------- | -------- |
| Forge | metals, ores |
| Workbench | wood, hides |
| Stonecutter | stone |
| Cauldron | cooking, meat, seeds |
| Fermenter | meads |
| Black forge | metals, valuables |
| Galdr table | valuables, meads |
| Smelter / Blast furnace | ores, fuel (coal) |

Good to know:

* A chest counts as "next to" a station within 8 m (`StationRange` setting). The nearest mapped station wins.
* Tools, armor and other non-stackables stay where they are unless a chest pins them — same rule as the sorter.
* The preview capacity check is exact at plan time; anything that can no longer move when you confirm (a chest filled up meanwhile, or another player is using it) simply stays put. Nothing is ever dropped or lost.
* Sorter chests are sources only — Organize empties them but never fills them.
* Kilns, eitr refineries and cooking stations are detected but unmapped by default; windmills cannot be detected at all (the game gives them no station identity) — pin a chest for those instead.
* Modded stations: press Organize once, copy the station token from `BepInEx/LogOutput.log` (`chest near station '$piece_...'`), and add it to the `CustomStations` setting.

## Configuration

The config file is `BepInEx/config/eksolutions.chestbutler.cfg`, generated on first launch. On a server, the server's values are authoritative and sync to all clients.

| Setting | Default | Description |
| ------- | ------- | ----------- |
| [Sorting] Radius | 128 | how far (meters) a sorter looks for target chests — also the Organize sweep range (5 to 128) |
| [Sorting] TransferInterval | 1.0 | seconds between transfer ticks per sorter (0.2 to 10) |
| [Sorting] StacksPerTick | 2 | item stacks moved per tick (1 to 8); raise both for faster sorting |
| [Sorting] ContainsFallback | true | route items to chests that already contain them when no filter matches |
| [Organize] MovesPerTick | 4 | item moves per frame while an Organize run executes (1 to 16) |
| [Organize] StationRange | 8 | how close (meters) a chest must be to a station to inherit its materials (1 to 20) |

The `[ItemGroups]` section defines the item groups: stone, wood, ores, metals, cooking, meat, seeds, trophies, valuables, meads, ammo, hides, fuel. Every group is a comma separated list of item name tokens (wildcards allowed), so you can edit them or add entries for modded items.

The `[Stations]` section maps station names to the groups they attract during Organize (for example `$piece_forge = metals, ores`). To cover a modded station, use the `CustomStations` entry with the format `token=group1,group2; token2=group3` — the exact token is printed to `BepInEx/LogOutput.log` whenever a chest's nearest station has no mapping.

## Compatibility

* Requires and works together with [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/), which also lets several players open the same chest at once.
* [Quick Stack Store Sort Trash Restock](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) works alongside it and covers player-inventory quick stacking.
* Mods that MultiUserChest lists as incompatible (QuickStore, QuickStack, SimpleSort) will not run with this stack either.
* Chest-size mods work fine. Item mods work in routing and can be added to the config groups by name.

## FAQ

**Can items get duplicated or lost?**
No. An item never exists in two places: each move is packed into a request, executed by the one client that owns the target chest, and confirmed back. If a transfer cannot complete, the items stay where they were. Anything the sorter cannot place simply waits in the sorter chest.

**Does sorting happen while nobody is near the base?**
No. Valheim only simulates loaded areas, so sorting runs while someone is around, which in practice is exactly when you are dumping loot.

**Do tools, armor and other non-stackable items get moved?**
Only if a chest explicitly claims them via Pin. The contains rule ignores non-stackables on purpose, so your spare gear does not wander around. Organize follows the same rule.

**I pressed Organize and nothing moved.**
The first press is a preview — the button turns into `Confirm?` and a second press within 5 seconds runs it. If it says "Nothing to organize" instead, every item is already in its best spot, or the scattered items have no home yet: no chest pins them, no mapped station is near a chest, and no other chest already holds that type. Organize never moves an item to a random chest.

**My chest next to the smelter (or forge) does not attract anything.**
The chest must be within 8 m of the station (`StationRange`). After pressing Organize, `BepInEx/LogOutput.log` shows one line per chest: the nearest station that matched, or the nearest unmapped one if none did. A "has NO [Stations] mapping" line means that station has no default mapping — a modded station, or a kiln / eitr refinery / cooking station: copy the token from that line into the `CustomStations` setting. A mapped station closer to the chest wins and can hide an unmapped station's token, so read the log from a chest that sits nearest to the unmapped one. Windmills cannot be detected (the game gives them no station identity); pin a chest for barley and flour instead.

**Can I add or remove the mod mid-playthrough?**
Yes. Sorter flags and pinned filters are stored on the chests themselves and survive restarts; without the mod they are simply ignored.

**My item will not route into a group chest.**
Group lists match item names. Some items have internal names that differ from the display name; pin a sample of the item once and the pinned name shown in the message is the exact token to use in the config groups.

## Links

* [GitHub repository](https://github.com/EladKarni/ChestButler) (source, issues, feature requests)
* [Report a bug](https://github.com/EladKarni/ChestButler/issues)
* Changelog: see the Changelog tab on this page

## Credits

Item transfer networking is built on [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/) by MSchmoecker.
