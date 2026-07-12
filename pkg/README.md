# ChestButler

Sorts your storage for you. Mark a chest as a Sorter, dump everything into it, and when you close it the items get moved to the right chests around your base. Stone goes where your stone already is, carrots end up in the kitchen, and anything the mod can't place stays put so nothing is lost.

## What it does

* Toggle any chest into a sorter with one button. Contents distribute when you close it.
* Chests attract items they already hold, fullest chest first, so it works with zero setup.
* Pin filters: put sample items in a chest, hit Pin, and the chest claims those item types even when empty. An Auto/Manual toggle decides whether the sorter fills it on its own or only when you ask.
* Pull: chests with filters get a button that fetches one stack of each wanted item from nearby storage. Good for a cooking chest that restocks itself.
* Sign labels as an alternative: a sign reading `sort: cooking` on a chest makes it the cooking chest. Also takes item names, `*` wildcards, `pN` for priority and `off` to exclude a chest.
* Item groups (stone, wood, ores, metals, cooking, meat, seeds, trophies, valuables, meads, ammo, hides) are editable in the config and sync from the server.

Every transfer runs through MultiUserChest's networking, so only the actual owner of a chest modifies it. No dupes and no lost items, including with several people online.

## Buttons

| Button | On | Does |
|---|---|---|
| `Sorter: ON/OFF` | any chest | dump chest toggle |
| `Pin` / `Auto (n)` / `Manual (n)` | target chests | save contents as filters, toggle auto-fill |
| `Clear` | filtered chests | remove saved filters |
| `Pull` | filtered chests | fetch a stack of each saved item from nearby |

## Setup notes

Config: `BepInEx/config/light.chestbutler.cfg` (radius, speed, fallback behavior, item groups). Server values sync to all clients.

Multiplayer needs the mod on the server and every client at the same version. Mismatches get a clear message at connect. Crossplay must be off.

## Credits

Item transfer networking is built on [MultiUserChest](https://thunderstore.io/c/valheim/p/MSchmoecker/MultiUserChest/) by MSchmoecker.
