# ChestButler

Sorts your storage for you. Mark a chest as a Sorter, dump everything into it, close the lid, and the items get moved to the right chests around your base.

| [Features](#features) | [Quick Start](#quick-start) | [Installing](#installing) | [Usage](#usage) | [Configuration](#configuration) | [Compatibility](#compatibility) | [FAQ](#faq) | [Links](#links) |
| --------------------- | --------------------------- | ------------------------- | --------------- | ------------------------------- | ------------------------------- | ----------- | --------------- |

## Features

**Sorter chests**
Toggle any chest into a dump chest with one button. Anything inside gets distributed to nearby chests the moment you close it. Items that no chest wants stay in the sorter, so nothing is ever lost or dropped.

**Works with zero setup**
Chests attract items they already hold, fullest chest first. Dump ore after a mining trip and it lands wherever your ore already lives.

**Pin filters**
Put sample items in a chest and press Pin. That chest now claims those item types even when it is empty. An Auto/Manual toggle controls whether the sorter fills it automatically or leaves it alone.

**Pull to restock**
Chests with filters get a Pull button that fetches one stack of each saved item from surrounding storage. Useful for a cooking chest next to the cauldron: press Pull, start cooking.

**Safe in multiplayer**
Every transfer runs through MultiUserChest's networking, so only the actual owner of a chest modifies it. No duped stacks, no vanishing items, even with several people using the same storage room.

## Quick Start

1. Open a chest near your storage area and click `Sorter: OFF` so it reads `Sorter: ON`.
2. Dump your whole inventory into it and close the chest.
3. Watch the items fly to whichever chests already hold that item type.
4. For a dedicated chest (for example carrots and onions near the cauldron): put samples in, press `Pin`, and from then on the sorter routes those items there. Press `Pull` any time to grab a stack of each back from storage.

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

Four buttons appear at the bottom of the chest UI:

| Button | Appears on | What it does |
| ------ | ---------- | ------------ |
| `Sorter: ON/OFF` | any chest | makes it a dump chest; contents distribute when you close it |
| `Pin` / `Auto (n)` / `Manual (n)` | normal chests | Pin saves the current contents as filters. After that the same button toggles Auto (sorter fills this chest) vs Manual (only Pull fills it) |
| `Clear` | chests with filters | erases the saved filters |
| `Pull` | chests with filters | fetches one stack of each saved item type from nearby chests |

### How a target chest is picked

For each item, in order: a chest that pins the item wins first, then a chest whose group covers it, then any chest that already contains some. Ties go to the chest holding the most of that item, then to the nearest one. If the best chest only has room for part of a stack, it gets topped off and the rest re-routes to the next candidate.

## Configuration

The config file is `BepInEx/config/eksolutions.chestbutler.cfg`, generated on first launch. On a server, the server's values are authoritative and sync to all clients.

| Setting | Default | Description |
| ------- | ------- | ----------- |
| Radius | 20 | how far (meters) a sorter looks for target chests (5 to 60) |
| TransferInterval | 1.0 | seconds between transfer ticks per sorter (0.2 to 10) |
| StacksPerTick | 2 | item stacks moved per tick (1 to 8); raise both for faster sorting |
| ContainsFallback | true | route items to chests that already contain them when no filter matches |

The `[ItemGroups]` section defines the item groups: stone, wood, ores, metals, cooking, meat, seeds, trophies, valuables, meads, ammo, hides. Every group is a comma separated list of item name tokens (wildcards allowed), so you can edit them or add entries for modded items.

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
Only if a chest explicitly claims them via Pin. The contains rule ignores non-stackables on purpose, so your spare gear does not wander around.

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
