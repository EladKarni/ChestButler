# ChestButler

Your loot, put away for you. Flag any chest as a **Sorter**, dump everything into it, close the lid - ChestButler distributes the items to the right chests around your base. Ask politely and it fetches things back, too.

## Features

- **Sorter chests** - toggle any chest into a dump chest with one button. Items route out the moment you close it.
- **Smart routing** - explicit item filters beat group filters beat "chest already contains it". Ties broken by priority, then by which chest holds the most (consolidation), then distance.
- **Pin filters** - put sample items in a chest, click **Pin**: the chest now claims those item types, even when empty. Toggle **Auto/Manual** to decide whether the sorter fills it automatically or only on demand. **Clear** wipes the filters.
- **Pull** - on any chest with filters, fetch one stack of each saved item type from nearby storage. Perfect for a cooking chest next to the cauldron.
- **Sign labels** - put a sign on a chest: `sort: cooking` or `sort: finewood, trophy*, p5`. Tokens: group names, item names ('*' wildcards), `pN` priority, `off` to exclude a chest.
- **Item groups** - stone, wood, ores, metals, cooking, meat, seeds, trophies, valuables, meads, ammo, hides - all editable in config (`[ItemGroups]`), server-synced.
- **Zero-loss by design** - items nothing claims stay in the sorter. All transfers run through MultiUserChest's owner-routed networking: no dupes, no vanishing items, safe with multiple players.

## Buttons (bottom of the chest UI)

| Button | On | Does |
|---|---|---|
| `Sorter: ON/OFF` | any chest | makes it a dump chest |
| `Pin` / `Auto (n)` / `Manual (n)` | target chests | save contents as filters / toggle auto-fill |
| `Clear` | filtered chests | remove saved filters |
| `Pull` | filtered chests | fetch a stack of each saved type from nearby |

## Config

`BepInEx/config/light.chestbutler.cfg` - radius (default 20m), transfer speed, contains-fallback, item groups. All admin-locked and synced from the server.

## Requirements & multiplayer

Dependencies install automatically (BepInEx, Jötunn, MultiUserChest). **Server and every player need the mod** - version mismatches are refused at connect with a clear message. Crossplay must be off (Steam only).

## Credits

Item-transfer networking built on [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/) (MIT) by MSchmoecker.
