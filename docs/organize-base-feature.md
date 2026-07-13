# Feature idea: "Organize Base" (one-click base consolidation, station-aware)

Status: planned (target 1.1.0). Not yet built.

## Problem

Setting ChestButler up on a *fresh* base is easy, but bootstrapping an *established*
messy base is a pain: chests are scattered with unrelated items, and the current
options all require handling items by hand (carry everything into a sorter, or set
up destination chests one by one). We want a one-press "clean up my whole base."

## The feature

A chest that has **Sorter** enabled gains an **Organize base** button. One press
sweeps every chest within range and routes each item to its best home, in place —
the player carries nothing.

### Routing priority (per item)

1. Explicit pin/sign filter (a chest that claims the item)
2. **Station adjacency** (new): a chest next to a crafting station attracts that
   station's materials
3. Chest that already holds the most of that item (consolidation)
4. Otherwise leave it where it is

Consolidation logic confirmed with the user: with no filters set, each item type
gravitates to whichever chest already holds the most of it.

### Station awareness (the differentiator)

For each chest, detect the nearby crafting station and bias routing so station
materials pool next to the station that uses them. One press and the forge gets its
metals, the cauldron its cooking ingredients, etc.

Starter station -> item-group map (curated, editable in config like `[ItemGroups]`):

| Station | Attracts (groups) |
|---|---|
| Forge / Blast furnace | metals, ores |
| Workbench | wood, hides |
| Stonecutter | stone |
| Cauldron / Food prep table | cooking, meat, seeds |
| Fermenter | meads |
| Black forge / Galdr table | metals, valuables |

(Modded stations need a config entry; "near" uses the station's build range.)

## Feasibility (verified against game assemblies, 0.221.12)

The game exposes exactly what's needed on `CraftingStation`:

- `static CraftingStation GetCraftingStation(Vector3 point)` -> the station in range
  of a point (use the chest position)
- `static CraftingStation FindClosestStationInRange(string name, Vector3 point, float range)`
- `public string m_name` -> station type key (e.g. `$piece_forge`, `$piece_cauldron`,
  `$piece_workbench`, `$piece_stonecutter`)
- private static `m_allStations` list of all stations (reflectable if needed)

So per chest: `CraftingStation.GetCraftingStation(chest.transform.position)` -> read
`m_name` -> look up the station->groups map -> treat as an implicit high-priority
group filter for that chest during Organize.

## Implementation sketch

- New `Core/Stations.cs`: station-name -> item-group map (config-bound, server-synced,
  same pattern as `Groups.cs`).
- Extend the router / add an `Organizer.cs` that iterates all tracked chests, builds
  per-chest affinity (pins > station groups > most-held), then routes every item via
  the existing MUC transfer path.
- New button in `GuiPatch.cs`, shown only when the chest is a Sorter.
- Reuse `ContainerTracker`, `Router`, and `Filters`; the only genuinely new pieces are
  station detection and the "process all chests" loop.

## Caveats to handle

- **Batch across ticks.** A base-wide pass can move hundreds of items; process in
  budgeted batches (like the sorter's StacksPerTick) so the game doesn't hitch.
  Print a summary ("moved 340 items across 12 chests").
- **No undo.** Warn before running.
- **Stackables consolidate; tools/armor stay put** unless explicitly claimed (same
  rule as the sorter today).
- **Curated station map** covers vanilla stations; document how to add modded ones.
- Ships as a feature release (1.1.0) -> coordinated server + client update, not an
  auto-applied patch.

## Open questions

- Final station -> group mappings (user to confirm/tweak).
- Organize radius: reuse the sorter radius, or a separate larger "base" radius?
- Trigger: button on sorter chests (chosen). Optional hotkey later.
