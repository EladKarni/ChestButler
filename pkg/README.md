# ChestButler

Sorts your storage for you. Mark a chest as a Sorter, dump everything into it, close the lid, and the items get moved to the right chests around your base. Press **Organize** and the whole base tidies itself. Press **Gather** at a crafting station and the missing ingredients come to you.

## Features

**Sorter chests**
Toggle any chest into a dump chest with one button, or build the craftable **Sorter Chest**, which is one out of the box. Anything inside gets distributed to nearby chests the moment you close it. Items that no chest wants stay in the sorter, so nothing is ever lost or dropped.

**Organize your whole base**
Sorter chests get an **Organize** button. One press scans every chest in range and previews what would move. Press again and every item type consolidates into its best home, in place, while you carry nothing. Homes persist: chests keep their assigned role between runs, so a tidy base stays tidy and a second press right after the first has little or nothing left to do. Weapons, armor and tools get homes of their own, and small odds and ends share a misc chest instead of each claiming their own.

**Gather at the crafting station** *(new in 2.0)*
A **Gather** button under Craft pulls the selected recipe's missing ingredients from chests in range, scaled to your craft multiplier. Green `(N)` counts show what storage holds. Craft, then dump what's left into a Sorter chest and it flows back to where it belongs.

**Sign control, now with areas** *(area-off new in 2.0)*
A sign next to a chest labels it: `sort: wood` claims a group, `sort: off` opts the chest out entirely. New in 2.0: add a number on its own line and the `off` covers every chest within that radius. One sign protects a whole room.

**Processor-aware routing** *(changed in 2.0)*
During Organize, a chest next to a smelter or blast furnace attracts ores and fuel, and one next to a fermenter attracts meads. Crafting stations (forge, workbench, cauldron and so on) no longer attract chests by default. With Gather you fetch what a recipe needs instead of storing materials beside the bench. Any station can be re-added in config.

**Pin filters & Pull to restock**
Put sample items in a chest and press Pin. That chest now claims those item types even when empty. Pull fetches one stack of each saved item from surrounding storage.

**Safe in multiplayer**
Every transfer (sorting, Pull, Gather and Organize) runs through MultiUserChest's networking, so only the actual owner of a chest modifies it, and the completion message counts only transfers the network confirmed. No duped stacks, no vanishing items.

## Before your first Organize: protect curated chests

Organize thinks in item types. A hand-curated chest that mixes types, like a "meals for the trip" chest or an adventure kit, is a player concept it cannot see, and it will be reorganized like everything else. Before your first press, protect what you've curated:

| You want | Do this | Effect |
| -------- | ------- | ------ |
| "Never touch this chest / room" | Sign: `sort: off` (add a number line for a radius) | Fully ignored: never read, never filled. Pull still works on click. |
| "This is THE chest for these types" | Pin the contents (Auto) | Protected, and it attracts all of those types in range. |
| "Don't auto-fill it" | Manual toggle | Not filled automatically, but Organize may still take from it. Manual does not protect contents. |

## Quick Start

1. Open a chest near your storage and click `Sorter: OFF` so it reads `Sorter: ON`, or build a **Sorter Chest** (Furniture tab, near the end).
2. Dump your inventory into it and close the chest. Items fly to whichever chests already hold those types.
3. Protect any curated chests (see above), then press `Organize` in the sorter chest. It previews ("move 340 items across 12 chests"), the button turns into `Confirm?`, and a second press runs it.
4. At a crafting station, select a recipe and press `Gather` to pull the missing ingredients from storage.
5. For a dedicated chest (carrots and onions near the cauldron): put samples in, press `Pin`. Press `Pull` any time to restock it from storage.

## Installing

### With a mod manager (recommended)

Install **ChestButler** with the Install button on this page or through r2modman / Thunderstore App. The dependencies (BepInEx, Jötunn, MultiUserChest) install automatically. Launch the game with **Start modded**.

### With a profile code (server groups)

Playing on a server that runs ChestButler? In r2modman or the Thunderstore App go to **Import / Update**, pick **Import new profile**, choose **From code** and paste the code your server admin gave you.

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

The mod must be installed on the server and on every client, at the same version. 2.0 and 1.1.x cannot mix; anyone mismatched gets a clear message at connect telling them what to fix. Crossplay must be disabled; modded servers are Steam only.

On a managed host (for example CubeCoders AMP): enable the BepInEx option in the instance configuration, then upload `ChestButler.dll`, `Jotunn.dll` and `MultiUserChest.dll` to `BepInEx/plugins/` via the file manager.

## Usage

Buttons in the chest UI:

| Button | Appears on | What it does |
| ------ | ---------- | ------------ |
| `Sorter: ON/OFF` | any chest | makes it a dump chest; contents distribute when you close it |
| `Organize` | sorter chests | sweeps every chest in range and consolidates each item type into its home; first press previews and turns the button into `Confirm?`, second press (within 5 s) runs it |
| `Pin` / `Auto (n)` / `Manual (n)` | normal chests | Pin saves the current contents as filters. After that the same button toggles Auto (sorter fills this chest) vs Manual (only Pull fills it) |
| `Clear` | chests with filters | erases the saved filters |
| `Pull` | chests with filters | fetches one stack of each saved item type from nearby chests |

And in the crafting panel:

| Button | Where | What it does |
| ------ | ----- | ------------ |
| `Gather (N)` | under Craft | pulls the selected recipe's missing ingredients from chests in range; `(N)` per ingredient shows what storage holds. Greyed out means you already carry what the recipe needs (it re-arms as crafting consumes materials) |

### Signs

Place a sign next to a chest (the sign binds to its nearest chest). Lines starting with `sort:` are read; everything else is your label text.

```
sort: wood, hides       <- this chest claims the wood and hides groups
sort: p2                <- priority 2 (higher wins ties)
sort: off               <- opt this chest out: never read, never filled
sort: off
10                      <- area mode: every chest within 10 m is opted out (max 32)
```

Group names and item names (wildcards allowed) both work as tokens. A number line only means something together with `off`.

### How a target chest is picked

For each item, in order: a chest that pins the item wins first, then a chest whose sign or group covers it, then (during Organize) a chest next to a mapped processor, then an established home from a previous run, then any chest that already contains some. Ties go to higher sign priority, then to the chest holding the most of that item, then to the nearest one.

### Organize: one-press base cleanup

Each item type gets exactly one winning home, chests are claimed by volume, and the assignment is remembered on the chests. The next Organize keeps established homes in place instead of reshuffling. A category only relocates when it has outgrown its chest and a nearer chest can hold all of it.

Default station map (editable in config under `[Stations]`), feed-in processors only since 2.0:

| Station | Attracts |
| ------- | -------- |
| Smelter | ores, fuel |
| Blast furnace | ores, fuel |
| Fermenter | meads |

Good to know:

* A chest counts as "next to" a station within 8 m (`StationRange`). The nearest mapped station wins.
* Crafting stations (forge, workbench, cauldron and the rest) attract nothing by default since 2.0. To restore any of them: `CustomStations = $piece_forge=metals,ores; $piece_cauldron=cooking,meat,seeds`.
* Weapons, armor and tools are organized into their own homes since 2.0. Set `[Organize] IncludeGear = false` for the old leave-my-gear-alone behavior.
* Carts and ships are transport, not storage: everything ignores their inventories (`VehiclesAreStorage` to opt in). The Obliterator is never a target.
* Sorter chests are sources only. Organize empties them but never fills them.
* The completion message counts confirmed transfers only; anything that could not move says so ("N could not move") and is picked up by the next press.

## Configuration

The config file is `BepInEx/config/eksolutions.chestbutler.cfg`, generated on first launch. On a server, the server's values are authoritative and sync to all clients (the two speed knobs below marked *client-side* are each player's own).

| Setting | Default | Description |
| ------- | ------- | ----------- |
| [Sorting] Radius | 128 | how far (meters) a sorter looks for target chests, which is also the Organize sweep range (5 to 128) |
| [Sorting] TransferInterval | 1.0 | seconds between transfer ticks per sorter (1 to 10), *client-side* |
| [Sorting] StacksPerTick | 2 | item stacks moved per tick (1 to 8), *client-side* |
| [Sorting] ContainsFallback | true | route items to chests that already contain them when no filter matches |
| [Sorting] VehiclesAreStorage | false | treat cart and ship inventories as storage again |
| [Organize] MovesPerSecond | 25 | transfer rate while an Organize run executes |
| [Organize] MaxMovesPerRun | 500 | safety cap per run; the message tells you to press again if it was hit |
| [Organize] StationRange | 8 | how close (meters) a chest must be to a station to inherit its materials (1 to 20) |
| [Organize] IncludeGear | true | give weapons/armor/tools their own homes during Organize |
| [Organize] MiscPromoteSlots | 24 | an unlisted item type gets its own chest only above this volume; below it, it shares the misc home |
| [Gather] Enabled | true | show the Gather button |
| [Gather] ShowStorageCounts | true | show the green (N) storage counts in the recipe list |

The `[ItemGroups]` section defines the item groups: stone, wood, ores, metals, cooking, meat, seeds, trophies, valuables, meads, ammo, hides, fuel. Every group is a comma separated list of item name tokens (wildcards allowed).

The `[Stations]` section maps processors to the groups they attract during Organize. For anything else, crafting stations you want back or modded stations, use `CustomStations` with the format `token=group1,group2; token2=group3`. The exact token is printed to `BepInEx/LogOutput.log` whenever a chest's nearest station has no mapping.

**Upgrading from 1.1.x:** stored `[Stations]` lines for the removed crafting-station defaults become inert automatically, and the old `[Organize] MovesPerTick` key is ignored (replaced by `MovesPerSecond`). No manual migration needed.

## Compatibility

* Requires and works together with [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/), which also lets several players open the same chest at once.
* [Quick Stack Store Sort Trash Restock](https://thunderstore.io/c/valheim/p/Goldenrevolver/Quick_Stack_Store_Sort_Trash_Restock/) works alongside it and covers player-inventory quick stacking.
* Mods that MultiUserChest lists as incompatible (QuickStore, QuickStack, SimpleSort) will not run with this stack either.
* Chest-size mods work fine. Item mods work in routing and can be added to the config groups by name.
* Controller: ChestButler's buttons are reachable on gamepad since 2.0.

## FAQ

**Can items get duplicated or lost?**
No. An item never exists in two places: each move is packed into a request, executed by the one client that owns the chest, and confirmed back. Since 2.0 the completion message counts only those confirmations. If a transfer cannot complete, the items stay where they were.

**I pressed Organize and it rearranged a chest I had set up by hand.**
Working as designed, unfortunately. Organize can't tell a curated mixed chest from clutter, so protect it first (see **Before your first Organize** above). Established single-type chests are safe, since homes persist between runs.

**Gather is greyed out.**
Greyed means you already carry everything the selected recipe needs at the current multiplier. It re-arms by itself as crafting consumes your materials.

**I pressed Organize and nothing moved.**
The first press is a preview. The button turns into `Confirm?` and a second press within 5 seconds runs it. "Nothing to organize" means every item is already in its home. Organize never moves an item to a random chest.

**Where is the Sorter Chest in the build menu?**
Hammer, Furniture tab, near the end. (It gets a proper spot next to the vanilla chests, and its own icon, in 2.1.)

**Can I remove the mod (or roll back) mid-playthrough?**
Mostly yes: sorter flags, pins and homes are stored on the chests and are simply ignored without the mod. The one exception is the **Sorter Chest piece**. It is a custom prefab, and the game deletes placed ones (with contents) on load if the mod is missing. Empty them first.

**My chest next to the smelter does not attract anything.**
The chest must be within 8 m of the station (`StationRange`). After pressing Organize, `BepInEx/LogOutput.log` names each chest's nearest station; a "has NO [Stations] mapping" line means it needs a `CustomStations` entry. Windmills cannot be detected at all (the game gives them no station identity), so pin a chest for barley and flour instead.

**My item will not route into a group chest.**
Group lists match item names. Some items have internal names that differ from the display name; pin a sample of the item once and the pinned name shown in the message is the exact token to use in the config groups.

## Links

* [GitHub repository](https://github.com/EladKarni/ChestButler) (source, issues, feature requests)
* [Report a bug](https://github.com/EladKarni/ChestButler/issues)
* Changelog: see the Changelog tab on this page

## Credits

Item transfer networking is built on [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/) by MSchmoecker.
