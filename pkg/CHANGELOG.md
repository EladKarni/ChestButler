# Changelog

## 1.1.0
- New **Organize** button on Sorter chests: one press sweeps every accessible chest within the sorter radius (20 m) and consolidates each item type into its best home, in place — you carry nothing
- Previews first ("Organize: move N items across M chests — press again to confirm") and only runs on a second press within 5 seconds; closing the chest, opening another, or the timeout cancels it
- Routing priority per item type: a chest that pins/filters it, then a chest whose adjacent crafting station attracts its group (forge → metals/ores, workbench → wood/hides, stonecutter → stone, cauldron → cooking/meat/seeds, black forge → metals/valuables, galdr table → valuables/meads), then the chest already holding the most of it; otherwise it stays put
- Tools and armor (non-stackables) stay where they are unless a chest explicitly pins them
- Station adjacency also covers processing pieces: smelters and blast furnaces attract ores + fuel (new `fuel` item group with coal), fermenters attract meads; kilns, eitr refineries and cooking stations are detected too (add mappings in config if wanted)
- New server-synced `[Stations]` config section maps station names to item groups (editable; add modded stations via the `CustomStations` entry — the log prints every detected station token); new `[Organize] MovesPerTick` and `StationRange` settings
- CAVEAT: windmills are not detectable (no station identity in game code) — pin a chest for barley/flour instead
- Default sorter/Organize radius raised from 20 m to 32 m (existing config files keep their saved value — edit `Radius` in the cfg to adopt the new default)
- This is a minor release: server and all clients must update together (a 1.1.0 client is refused by a 1.0.x server, and vice-versa)


## 1.0.2
- Rewrote the Thunderstore page: quick start, clearer install/multiplayer instructions, config table, compatibility notes, FAQ
- Chest UI buttons now match the vanilla Take all / Place stacks style and anchor to the panel's bottom-left corner
- Docs no longer mention sign labels (that path stays in the code but is undocumented for now)


## 1.0.1
- Mod ID changed to eksolutions.chestbutler (config file is now BepInEx/config/eksolutions.chestbutler.cfg; settings reset to defaults once)
- Server and all players must update together, older versions are refused at connect

## 1.0.0
- First public release
- Sorter chest toggle with tiered routing: pinned filters, then groups, then chests that already hold the item
- Consolidation (fullest chest wins ties) and partial fills
- Pin / Auto / Manual / Clear / Pull buttons in the chest UI
- Sign labels (`sort: ...`) with groups, wildcards, priority and off
- Server-synced config including editable item groups
- Same-version check for server and clients at connect
