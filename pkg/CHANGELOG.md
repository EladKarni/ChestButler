# Changelog

## 2.0.0

The base-management release. Organize is now a whole-base allocator with persistent homes, a **Gather** button pulls recipe ingredients to you at the crafting station, a craftable **Sorter Chest** joins the build menu, and signs can protect whole areas. **Server and all clients must update together.** A 2.0 server refuses 1.1.x clients (and the other way around) with a clear message at connect.

**Organize v2: whole-base, volume-aware**
- Every item type is classified into a bucket (groups, pinned types, gear, misc) and buckets claim chest space by volume, anchored by pins, signs, adjacent processors, and the homes of previous runs. 1.1.x just sent things wherever most of them already sat.
- Homes persist on the chests themselves. A second press moves little or nothing, and established chests keep their role between runs. A category only relocates when it has outgrown its chest and a nearer one fits all of it.
- Weapons, armor and tools are organized into their own homes by default now. Set `[Organize] IncludeGear = false` to restore the 1.1.x leave-gear-alone behavior.
- Small leftovers fold into a shared misc home instead of claiming a whole chest per item type (`MiscPromoteSlots`).
- The completion message counts only transfers the network actually confirmed, never what was merely attempted.
- Dedicated-server correctness: chests that no peer owns are claimed before moving items out of them (those moves previously did nothing, silently, which is why Organize could take 3-4 presses), declined or failed transfers are retried or reported by name in the log, and a big base converges in two presses. Verified on a 5,838-item base: press 2 swept 15 items, press 3 found nothing to do.

**Gather** *(new)*
- A **Gather** button under Craft pulls the selected recipe's missing ingredients from chests in range, scaled to the craft multiplier.
- Green `(N)` counts show what storage holds. Greyed out means you already carry what the recipe needs; it re-arms as crafting consumes materials.

**Sorter Chest** *(new)*
- A craftable chest that is a Sorter out of the box. It sits at the end of the Furniture tab for now (grouping and a distinct icon come in 2.1).
- Rollback caveat: it is a custom prefab, so removing the mod (or rolling back to 1.1.x) deletes placed Sorter Chests from the world. Toggled vanilla chests are unaffected.

**Signs: area-off and priorities**
- `sort: <groups or item names>` on a sign binds the nearest chest, `pN` sets its priority, and `sort: off` excludes it, same as before.
- New: add a number on its own line (or after `off`) and the off applies to every chest within that many meters of the sign, up to 32. One sign protects a whole room.

**Breaking / behavior changes**
- **Station adjacency is now feed-in processors only.** Smelter, blast furnace and fermenter attract their materials; forge, workbench, cauldron and the other crafting stations no longer do. With Gather, materials no longer need to live beside the bench: you fetch exactly what a recipe needs and return leftovers via a Sorter chest. Any station can be re-added in config, for example `CustomStations = $piece_forge=metals,ores`. Upgrading servers lose crafting-station attraction automatically (the stored `[Stations]` lines for removed defaults become inert).
- **Curated chests need protection before your first Organize.** A hand-picked mixed chest (a meals chest, an adventure kit) is reorganized like everything else unless a `sort: off` sign protects it. See the README's "Before your first Organize" section.
- Carts and ships are transport, not storage. The sorter, Organize, Gather and Pull all ignore vehicle inventories (`VehiclesAreStorage` restores the old behavior). The Obliterator is no longer a valid target for anything.
- `[Organize] MovesPerTick` (per-frame) is replaced by `MovesPerSecond` (default 25) and `MaxMovesPerRun` (default 500). An old stored key is ignored.
- Controller users: ChestButler's buttons are now reachable on gamepad.

## 1.1.2
Maintenance release: correctness and performance fixes found by a full source audit. No new features, no config migration needed. Cross-compatible with 1.1.x, so it deploys as a normal patch.

**Fixes that can affect your items**
- Organize could run twice at once (pressing it on a second sorter while the first run was still going). The two runs could not see each other's in-flight moves and could both issue a transfer for the same stack. Only one Organize run at a time now.
- Organize could over-fill a destination chest: it re-checked free space per move but never counted what it had already promised that chest in the same run, and in-flight transfers are not reflected locally. It now tracks its own promises.
- Organize never checked that a *source* chest still existed. A chest destroyed or unloaded mid-run left a window where a transfer was issued against a dying chest. Both endpoints are validated now.
- Organize now reports what it could NOT move ("N could not move") instead of silently dropping those moves and reporting success.

**Performance**
- A sorter chest holding items with no home did a full base-wide scan for every one of them, every tick, forever — the most expensive path in the mod on a large base. Unroutable item types are now remembered for 10 s before being retried.
- The sorter tick no longer runs at all on a dedicated server, where it could never do anything (routing needs a local player for the access and ward checks) but still paid for the scan.
- Filter/sign lookups were cached for only 3 seconds and re-resolved by scanning every sign and every chest. The cache is now invalidated on the events that actually change it (editing a sign, changing pins, unloading a chest) and lives longer.
- Item-name matching is ordinal instead of culture-sensitive, wildcard tokens are parsed once instead of per comparison, and normalized names are memoized.
- Organize's per-frame move budget is now also capped in real time, so a high-refresh-rate client no longer sends proportionally more traffic to the server than a 60 fps one.
- Station tracking no longer rescans everything on each registration, and the per-chest station log moved to Debug (it was one synchronous log write per chest per Organize); the "unmapped station" hint is now logged once per station type instead of once per chest.
- The chest spec cache is dropped when a chest unloads, instead of accumulating an entry per chest per zone reload for the whole session.

**Consistency**
- Chests at exactly the same distance are now ranked by a stable id, so a symmetric storage hall routes the same way every session. Same for a chest sitting equidistant between two crafting stations.
- Group precedence is now explicit and documented in code. Some items match two groups in the default config (FlametalOre matches both `ores` and `metals`), which was previously resolved by dictionary order.
- A `[Stations]` mapping that points at a group name which does not exist in `[ItemGroups]` (a typo, or a renamed group) now logs a warning instead of silently attracting nothing.
- `TransferInterval`'s minimum is now 1 s (was 0.2 s). The default was already 1 s; only settings that were never safe on a large base are removed.

**Known, not fixed here** — see `docs/known-issues-1.1.x.md`: a chest opened by *another player* is invisible to us (`m_inUse` is a local field), so Organize can still take ownership from someone browsing a chest; and the sorter radius default of 128 m may exceed the distance at which chests are actually loaded. Both need changes that are out of scope for a patch.

## 1.1.1
- Dependency pin updated to Jotunn 2.29.2 to match the server (no gameplay changes)
- Default sorter/Organize radius raised to 128 m (max raised to 128). Existing config files keep their saved `Radius`; on a server the admin value is authoritative and syncs to clients, so set the server's `Radius` to 128 to apply it for everyone

## 1.1.0
- New **Organize** button on Sorter chests: one press sweeps every accessible chest within the sorter radius (32 m by default) and consolidates each item type into its best home, in place — you carry nothing
- Previews first ("Organize: move N items across M chests — press again to confirm") and only runs on a second press within 5 seconds; closing the chest, opening another, or the timeout cancels it
- Routing priority per item type: a chest that pins/filters it, then a chest whose adjacent crafting station attracts its group (forge → metals/ores, workbench → wood/hides, stonecutter → stone, cauldron → cooking/meat/seeds, black forge → metals/valuables, galdr table → valuables/meads), then the chest already holding the most of it; otherwise it stays put
- Tools and armor (non-stackables) stay where they are unless a chest explicitly pins them
- Station adjacency also covers processing pieces: smelters and blast furnaces attract ores + fuel (new `fuel` item group with coal), fermenters attract meads; kilns, eitr refineries and cooking stations are detected too (add mappings in config if wanted)
- New server-synced `[Stations]` config section maps station names to item groups (editable; add modded stations via the `CustomStations` entry — the log prints every detected station token); new `[Organize] MovesPerTick` and `StationRange` settings
- CAVEAT: windmills are not detectable (no station identity in game code) — pin a chest for barley/flour instead
- Default sorter/Organize radius raised from 20 m to 32 m (existing config files keep their saved value — edit `Radius` in the cfg to adopt the new default)
- Fixed the `ores` group never matching metal scraps (the in-game tokens are singular: ironscrap, bronzescrap, copperscrap) and wrongly claiming leather scraps via the old `*scraps` wildcard — existing config files keep their saved pattern; delete the `ores` line under `[ItemGroups]` (or paste the new default) to adopt the fix
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
