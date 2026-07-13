# Changelog

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
