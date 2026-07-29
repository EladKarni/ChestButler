# ChestButler 1.1.x — defects found in the shipping code

Found by the 2.0 planning audit (three independent reviews of `src/ChestButler` against the
MultiUserChest source). **Everything here is a bug in 1.1.1 as shipped — none of it depends on
Organize v2.** All of it is client-side behaviour and config, cross-compatible with 1.1.x, so it can
ship as **1.1.2 through the `PATCH_ONLY` server updater without a coordinated release** — unlike 2.0.

Doing this first also de-risks 2.0: items 1, 2, 6, 7, 9 and 10 are load-bearing for the v2 allocator
(see `docs/organize-v2-allocation-plan.md` §16), so fixing them in 1.1.2 shrinks W1.

Ordered by severity. Line references are to the 1.1.1 tree.

## Status after the 1.1.2 patch (branch `fix/1.1.2`)

| # | Item | Status |
|---|---|---|
| 1 | `sort: off` chests enrolled as sources | **DECIDED — fixed in 2.0 (W1), not in 1.1.2.** `sort: off`/`ignore` now means *leave the chest entirely alone* (neither source nor target); Manual and Sorter chests stay sources. Needs the `ChestView` source/target split, so it lands with the allocator rewrite. Original note: The behaviour is deliberate and asserted by planner test [7] ("excluded chests are never targets, still sources"). `Ignore` and `ManualOnly` are treated identically today. The question is whether `sort: off` should mean "never receives" (current) or "leave this chest alone entirely" — a semantic change, not a bug fix, and it belongs in 2.0 with the v2 allocator that makes it matter. |
| 2 | No in-flight guard on `Organizer.Execute` | **FIXED** — single static guard, released in a `finally`; a second press reports "Organize already running". |
| 3 | `IsInUse()` is local-only | **PARTIALLY — documented, not fixed.** There is no networked in-use signal to check. Since `NetworkCompatibility` is `EveryoneMustHaveMod`, 2.0 can add its own synced in-use flag on chest open/close; a patch cannot. The hazard is now spelled out at the call site. |
| 4 | Destination over-commit | **FIXED** — per-run `promised[target]` ledger debited at issue time. |
| 5 | Silent dropped moves / dishonest report | **FIXED** — `skippedMoves`/`skippedItems` counters; the HUD message gains "(N could not move)". |
| 6 | Unstable distance tie-breaks | **FIXED** — `ContainerTracker.Candidates` sorts by `(distance, ZDO uid)`; `Stations.Consider` breaks equidistant ties on the station token. |
| 7 | `FlametalOre` in two groups | **FIXED** — explicit `Groups.GroupOrder` + `GroupsInOrder()`/`FirstGroupFor()`, with a startup check that the order and the group table cannot drift apart. |
| 8 | User-added `[ItemGroups]` ignored | **PARTIALLY** — a `[Stations]` mapping naming a nonexistent group now warns at startup with the valid list. Binding genuinely user-added group keys needs BepInEx orphaned-entry handling; deferred to 2.0. |
| 9 | Sorter tick scans the base per homeless item | **FIXED** — 10 s per-item-type miss cooldown, so the steady state costs ~1 scan per type per 10 s instead of one per tick. |
| 10 | O(chests²) sign resolution on the tick path | **FIXED** — cache TTL 3 s → 30 s, with explicit invalidation on sign edit (`Sign.SetText`), pin/manual change, and chest unload. |
| 11 | `IsSlotBlocked` guards may be dead code | **NOT CHANGED — needs the in-game check** (roadmap §9 item 3). Left in place; removing a guard on an unverified assumption is the wrong risk. |
| 12 | Unbounded static growth | **FIXED** — spec cache pruned on `Container.OnDestroyed`; processor prune amortized to every 64 registrations instead of every one. |
| 13 | Culture-sensitive matching + allocations | **FIXED** — ordinal everywhere, tokens parsed once, `Normalize` memoized. Covered by 16 new offline tests. |
| 14 | `OrganizeMovesPerTick` wrong unit | **PARTIALLY — the rest lands with W1's self-throttling (v2 plan §16.6).** — the per-frame budget is now also capped in real time at the nominal 60 fps rate, so a 144 fps client no longer sends 2.4× the traffic. Renaming the key to an explicit per-second unit is a 2.0 change. |
| 15 | `TransferInterval` 0.2 s floor | **FIXED** — floor raised to 1.0 s (the default was already 1.0). |
| 16 | Everything is `IsAdminOnly` | **DECIDED — done.** The three rate knobs (`TransferInterval`, `StacksPerTick`, `[Organize] MovesPerTick`) are client-side now; result-affecting settings stay admin-only and server-synced. W1 additionally builds self-throttling (v2 plan §16.6), making these a ceiling rather than a tuning dial. Original note: Un-admin-locking the perf knobs lets a non-admin fix their own framerate, but also lets any client change how fast items move for everyone. Worth deciding deliberately rather than in a patch. |
| 17 | `SorterRadius` 128 m vs loaded area | **NOT CHANGED — blocked on measurement** (roadmap §9 items 1–2). `ZoneSystem.c_ZoneSize = 64` is confirmed as a compile-time constant in `assembly_valheim.dll`; `m_activeArea`/`m_activeDistantArea` are Unity-serialized and still need a runtime log. |
| 18 | `StationRange` doc-string misleading | **NOT CHANGED** — cosmetic; folded into the 2.0 config pass. |

## Correctness — item loss, wrong results

1. **`sort: off` / `sort: manual` chests are still enrolled as Organize *sources*.**
   `OrganizePlanner`'s stack-enrollment loop never tests `ExcludedAsTarget`; the flag is only consulted
   in `AnyTargetPins` and `PickBest`. A chest a player deliberately marked ignore gets emptied into the
   base. Milder in v1 than it will be in v2 (v1 only moves items that already have a home elsewhere),
   but it is a real data-loss-of-intent bug today.
   *Fix: `if (c.ExcludedAsTarget) continue;` in enrollment — one line. Decide whether `ManualOnly` is
   source-exempt too; `Ignore` unambiguously is.*

2. **No in-flight guard on `Organizer.Execute`.** It unconditionally starts a coroutine. Pressing
   Organize on a second sorter while the first run is still going gives two coroutines executing
   mutually stale plans over the same chests — and because our `to=(-1,-1)` calls create no
   `InventoryBlock` (see item 11), neither can see the other's in-flight moves.
   *Fix: `static bool _running`, cleared in a `finally`.*

3. **`tgt.IsInUse()` is a local flag, but we act on it globally.** A remote player browsing a chest sets
   `m_inUse` on *their* client; our copy reads false, so we `ClaimOwnership()` the ZDO out from under
   them. `Container.Save()` is owner-gated, so their deposit is silently never written. The code comment
   ("another player is browsing it; don't yank ownership") describes a guarantee the check cannot give.
   *Fix: gate on `tgtNv.GetZDO().GetOwner()`, re-checked immediately before the claim.*

4. **Destination over-commit.** `Router.Room` is re-checked per move but never debited, and MUC applies
   destination adds only on the RPC response — so N moves into the same chest all see the same free
   space and all issue. The source owner has already applied the remove.
   *Fix: a per-run `roomLeft[target]` ledger debited at issue time.*

5. **Organize reports issued amounts, not completed ones, and silently drops moves.** `Run` has five
   `continue` paths that abandon a move and count nothing. A co-op partner idling in one chest means an
   entire item type never moves while the player is told "Organized 1,340 items".
   *Fix: `skipped`/`skippedItems` counters in the message.*

6. **Distance tie-breaks are non-deterministic.** `ContainerTracker.Candidates` iterates a `HashSet`
   (zone-load order), discards distances and uses the unstable `Array.Sort`. `Stations.GroupsForChest`
   has the same defect (strict `<` over `m_allStations` in Awake order). A symmetric storage hall —
   the normal way people build — routes differently between sessions.
   *Fix: sort by `(distance, zdo.m_uid)` with a comparator in both places.*

7. **`FlametalOre` is in two groups in the shipped defaults.** It matches `ores`' `*ore` and `metals`'
   `flametal*`; smelter and forge chests both claim it, and which wins is undefined. Same for
   `flametalorenew`.
   *Fix: an explicit `static readonly string[] GroupOrder` consulted by the matcher — do NOT rely on
   dictionary order, which changes when a group is added.*

8. **User-added `[ItemGroups]` entries are silently ignored.** `Groups.Init` binds only the 13 hardcoded
   default keys, so a custom group in the .cfg is an orphan, and renaming a default resurrects the
   default on next launch while `[Stations]` keeps pointing at it. Signs referencing a custom group name
   become inert. `CustomStations` values are likewise never validated against `[ItemGroups]`.
   *Fix: bind whatever keys exist in the section, and warn on a station mapping to an unknown group and
   on a sign token matching neither a group nor any item in range.*

## Performance — these are the ones players will feel

9. **The sorter tick burns a full base scan per homeless item, every second.** In `SorterBehaviour`,
   `budget--` sits *after* the `target == null` check, so items with no home cost a complete
   `ContainerTracker.Candidates` + `Router.FindTarget` sweep and consume no budget — and a sorter chest
   full of homeless items is the steady state the feature produces. Estimated ~64 ms/tick per sorter
   with 32 homeless items at 400 chests; 20 sorters ≈ 1.28 s of CPU per second. The stagger is only 16
   phase buckets, so it lands as a rhythmic hitch. **Estimated user-visible from ~150 chests.**
   *Fix: move `budget--` above the null check (one line), and memoize "no target for this norm" per tick.*

10. **Sign resolution is O(chests²) and lands on the tick path.** `Filters.GetSpec` → `ParseNearestSign`
    sweeps every sign and calls `NearestTo` (a full container scan) per in-range sign, and
    `CacheTtl = 3f` against a 1 s tick guarantees cold misses. Estimated ~19 M distance ops/s and
    ~480 k string allocations/s at 400 chests once players label chests — i.e. exactly the users who
    adopted the feature.
    *Fix: bind sign→chest once at `Sign.Awake`/text-change; invalidate the cache on sign/pin change
    instead of by TTL.*

11. **The `IsSlotBlocked` guards may be dead code.** MUC's `InventoryBlock.CanBlockSlot` requires
    `slot.x/y >= 0`, and every ChestButler call passes `to = new Vector2i(-1, -1)`, so
    `RemoveItemFromChest` appears never to create a block at all. `AddItemToChest` does block, on the
    source's real grid position — the two paths are not symmetric. Also, `ReleaseSlot` only fires from
    an RPC response and there is no timeout or sweep, so a dropped response blocks a slot permanently.
    *Verify in-game (roadmap §9 item 3), then either remove the misleading guards or fix the call sites.*

12. **Unbounded static growth.** `Filters.Cache` is keyed by `Container` and pruned only in
    `SetPinned`/`SetManual`; zone reloads mint a fresh `Container` per chest, so dead keys accumulate,
    each holding a strong reference to a destroyed MonoBehaviour and pinning its GameObject graph.
    `SignPatches` has an `Awake` patch and **no unregister at all**. `Stations.RegisterProcessor` rescans
    all processors on every registration — O(P²) per zone load.
    *Fix: prune `Filters.Cache` in the `Container.OnDestroyed` prefix that already exists; add a
    `Sign.OnDestroy` unregister; drop the per-registration prune.*

13. **`Names.Matches` uses culture-sensitive `StartsWith`/`EndsWith`** (~4× slower than ordinal, and
    locale-dependent matching for a token matcher), and `token.Trim('*')` allocates on every wildcard
    comparison. `Names.Normalize` is never cached despite ~200 distinct keys.
    *Fix: `StringComparison.Ordinal`, pre-split tokens once per spec, memoize `Normalize`.*

## Config

14. **`OrganizeMovesPerTick` is in the wrong unit.** Per-*frame* means the RPC rate is framerate-
    dependent: 4/tick at 60 fps = 240 RPC/s; 16/tick at 144 fps = 2,304 RPC/s from one client.
    *Fix: `OrganizeMovesPerSecond` (default 25, range 5–100) on a time-accumulated budget, plus
    `OrganizeMaxMovesPerRun` (default 500) with "press again to continue".*

15. **`TransferInterval`'s 0.2 s floor is unsafe** — 20 sorters at 5 ticks/s is 100 full base scans per
    second. Raise the floor to 1.0 s, or scale it by candidate count.

16. **Every config entry is `IsAdminOnly`.** On a dedicated server a non-admin player has no way to turn
    down a mod that is costing them frames. Un-admin-lock the client-side performance knobs (leave the
    behavioural ones synced).

17. **`SorterRadius` defaults to 128 m, which may be 2× the loaded area** — see roadmap §9 items 1–2.
    Unresolved until measured.

18. **`StationRange` is misleading**: `GroupsForChest` scans every station regardless, so the knob
    changes no cost, only the match distance. Worth a doc-string tweak.

## Not fixed here — needs the 2.0 work

- The allocator has no fixed point without a persisted home key (v2 plan §16.1).
- `OrganizePlanner` and `Router` disagree on ranking (the planner has a Station tier, `Router` has
  none), so Organize and the live sorter fight over the same item (v2 plan §15.8). The parity fix is
  cheap and could ride along in 1.1.2 if you want the two loops consistent sooner.
