# ChestButler — Organize v2 ("whole-base allocation") plan

Status: design ready, not built. This REPLACES the Organize planning logic (`OrganizePlanner`)
with a volume-aware allocator. The Unity adapter (`Organizer`) and the execution coroutine are
mostly reused. Read the current files first: `Core/OrganizePlanner.cs`, `Core/Organizer.cs`,
`Core/Groups.cs`, `Core/Stations.cs`, `Core/Filters.cs`, `Core/Router.cs`, `Patches/GuiPatch.cs`.

## 1. The problem with v1

Today `OrganizePlanner.ChooseTarget` moves an item only if some chest already **pins** it, its
**sign** covers it, a nearby **station** attracts its group, or a chest already **holds** some.
Anything scattered with no existing home and no relevant station is left in place. It also
consolidates strictly per exact item type, so it never forms a "wood chest" or claims an empty
chest as a new home.

## 2. What we're building (owner decisions locked)

Press Organize on a Sorter chest → it takes a **census of every accessible chest in range**, works
out how much space each logical bucket needs, **allocates chests to buckets by volume** (claiming
empty chests as needed), and packs everything in. One press tidies the whole base; nothing is left
scattered if there's room for it.

Locked with the owner:
- **Buckets are volume-adjusted, not fixed.** We do NOT assume one chest per item or per category.
  We take stock of the whole base and allocate as many (or few) chests per bucket as the quantity
  needs — a category with 2,000 wood gets several chests; a category with 5 rubies shares one.
- **Categories = the existing `[ItemGroups]`** (wood, metals, ores, stone, cooking, meat, seeds,
  trophies, valuables, meads, ammo, hides, fuel). Items in a category are grouped together, spilling
  across multiple chests when the volume needs it.
- **Ungrouped items → consolidated per exact type** (each ungrouped item type gets its own chest[s]).
- **Gear (non-stackables) → three buckets: weapons, armor, tools.** (This changes the v1 rule that
  left non-stackables put — now they're organized into these three buckets. Pinned gear still obeys
  its pin.)
- **New homes for a bucket:** prefer an empty chest near the bucket's matching station (metals→forge,
  cooking→cauldron, etc.), else the nearest empty chest to the Sorter that was clicked.
- Preview-then-confirm stays (first press previews counts, second press within 5 s runs it).
- Delivery: build, install to the local test profile, commit to **dev**. Do NOT publish or touch the
  live server; the owner tests and releases.

## 3. Mental model: census → demand → allocation → packing → execute

    CENSUS      scan all in-range accessible chests; list every stack; record each chest's
                capacity (total slots) and role (anchor vs free).
    CLASSIFY    map every item to exactly ONE bucket (see §5).
    DEMAND      per bucket, slots needed = Σ over member types of ceil(totalCount / maxStack).
    ALLOCATE    assign whole chests to buckets to satisfy demand, honoring fixed anchors first,
                then claiming free/empty chests (station-preferred) (see §4).
    ASSIGN      produce a target chest for every stack (an item already in its bucket's chest
                stays — minimize churn).
    EXECUTE     multi-pass MUC moves that can never lose items; overflow stays; report.

## 4. Allocation algorithm (deterministic)

Definitions:
- **Anchor chest:** a chest with an explicit/implicit fixed role — it pins/sign-matches an item or
  category, or is station-adjacent to a category. Anchors are pre-assigned to that bucket and are
  never repurposed. `FilterSpec.Ignore`/`ManualOnly`/Sorter chests are excluded as targets entirely.
- **Free chest:** any other accessible chest. Its current contents are fair game to redistribute, so
  its full capacity is available to the allocator.

Steps:
1. Census + classify (§5). Compute each bucket's slot demand.
2. Seed each bucket with its anchor chest(s). Subtract anchor capacity from the bucket's demand.
3. Sort buckets by remaining demand (largest first) for stable, greedy allocation. Deterministic
   tie-break: bucket enum order.
4. For each bucket still needing capacity, claim chests from the free pool until demand is met:
   - preference order for a claimed chest: (a) empty chest within `StationRange` of the bucket's
     station; (b) empty chest nearest the bucket's anchor/primary chest; (c) nearest free chest to
     the origin Sorter. Distance ties broken by ZDO uid (stable).
   - a "free chest" counts as available capacity even if it currently holds other buckets' items —
     those items are themselves being reassigned, so the chest will drain during execution.
5. If the free pool runs out before demand is met, the bucket is under-provisioned: pack what fits,
   leave the rest in place, and count it for the "N items had no room" report. Never error.
6. Produce the final assignment: for every stack, its target chest = the bucket's chest with room,
   filling one chest before spilling to the next (nearest-first within the bucket). A stack already
   sitting in one of its bucket's chests is left where it is (churn minimization).

Capacity: use the real chest size — `inv.GetWidth() * inv.GetHeight()` total slots and
`inv.GetEmptySlots()` for free space; support modded chest sizes. Slot demand treats each distinct
item type as needing its own stacks (`ceil(count / maxStack)`), because a slot holds one item type.

## 5. Classification (one bucket per item)

Resolve each item to a single bucket in this priority:
1. A chest's explicit pin/sign for the item or its category (anchor) → that bucket.
2. Item belongs to a `[ItemGroups]` category (via `Groups.GroupContains`) → that category bucket.
   If an item matches multiple groups, pick the first by a fixed group order (document it).
3. Non-stackable gear → weapon / armor / tool bucket by `item.m_shared.m_itemType`:
   - weapons: OneHandedWeapon, TwoHandedWeapon, TwoHandedWeaponLeft, Bow, Torch, Shield, Ammo?(no—ammo is a group)
   - armor: Helmet, Chest, Legs, Shoulder, Hands, Utility (capes/belts)
   - tools: Tool (hammer, hoe, cultivator, pickaxe is a weapon? verify), plus anything left non-stackable
   VERIFY the `ItemDrop.ItemData.ItemType` enum members against the game assembly and adjust; log any
   unmapped types so the map can be completed.
4. Otherwise (ungrouped stackable) → its own per-type bucket (bucket key = the normalized name).

## 6. Execution (safe, multi-pass) — reuse Organizer.Execute's primitive

Keep the existing execution shape (coroutine on `Plugin.Instance`, `OrganizeMovesPerTick` budget,
per-move `Router.Room` re-check, destination `ClaimOwnership`, `ContainerHandler.RemoveItemFromChest`
— the only sanctioned write path). Changes:
- Order moves to **evict foreign items before filling**: within a pass, first move items OUT of a
  chest that is about to become a bucket's home (to their own targets), then move that bucket's items
  in. This prevents "target full of the wrong stuff" stalls.
- **Multi-pass:** repeat the plan-execute loop until a pass makes zero moves or a max-pass cap
  (e.g., 6) is hit. Chests drain across passes, so late moves that didn't fit earlier now succeed.
- Item safety is inherent: every move is an MUC transfer that either completes or leaves the item
  where it was (the `Room`/`InventoryBlock` re-checks already skip impossible moves). Nothing is ever
  deleted; worst case is an incomplete organize with a clear message.
- Final report: "Organized N items into M chests" +, if any, "K items had no room — add more chests".

## 7. Determinism & re-runnability
The whole plan is a pure function of the census (chest positions/ZDO uids, contents, config). Sort
keys are explicit (bucket order, distance, ZDO uid), so a second Organize on an already-tidy base
produces (near) zero moves. Anchors are stable homes; churn minimization keeps correctly-placed
items still.

## 8. Files to change
- REWRITE `Core/OrganizePlanner.cs`: replace `ChooseTarget`/per-type loop with the allocator (§4).
  Keep it PURE over POD inputs so it's unit-testable offline. New POD needs per-chest capacity
  (total slots), anchor flags per bucket, and each item's bucket key (classification done in the
  adapter and passed in, OR pass the classifier delegate).
- EXTEND `ChestView` (in OrganizePlanner.cs): add `TotalSlots`, `IsAnchorFor(bucketKey)`, and the
  station→bucket info already present via `StationAttracts`. Add each stack's resolved `BucketKey`.
- UPDATE `Core/Organizer.cs`: build the richer views (capacity, classification via Groups/Stations/
  m_itemType), map allocator moves back to Containers, drive the multi-pass execution.
- ADD gear classification helper (m_itemType → weapon/armor/tool) — new `Core/Gear.cs` or inside
  Organizer; keep it data-driven and logged for unmapped types.
- CONFIG (`Plugin.cs`): add `[Organize] IncludeGear` (bool, default true) so the weapon/armor/tool
  sweep can be turned off; keep `MovesPerTick`, `StationRange`. Consider `[Organize] MaxPasses`
  (default 6, hidden/admin).
- `Patches/GuiPatch.cs`: preview text can stay ("move N items across M chests"); optionally add the
  under-provisioned count. Button visibility unchanged (Sorter only).
- CHANGELOG + version bump.

## 9. Config additions
- `[Organize] IncludeGear = true` — sweep tools/armor/weapons into their three buckets.
- `[Organize] MaxPasses = 6` (admin/hidden) — safety cap on the drain-and-refill loop.
- Reuse `StationRange`, `MovesPerTick`, `[Stations]`, `[ItemGroups]`.

## 10. Edge cases / graceful degradation
- Not enough chests/slots for everything → pack what fits, leave the rest, report the shortfall.
- Deadlock risk (everything full, nothing can move) → multi-pass + evict-first ordering resolves the
  common cases; the max-pass cap guarantees termination; leftovers stay put (no loss).
- Mixed anchor chest (pins carrots but also holds junk) → keep carrots, evict the junk to its bucket.
- Item in multiple groups → fixed group order decides; document the order.
- Someone browsing a chest (`IsInUse`) or a ward → skip that chest as a target this run (already handled).
- Tiny bases / nothing to do → "Nothing to organize".
- NG+ world levels: the existing per-move `Room` re-check already clamps; keep it.

## 11. Versioning & delivery

> **SUPERSEDED by `docs/roadmap-2.0.md` §2/§4/§6. Organize v2 now ships inside the coordinated 2.0.0
> release. The W1 agent must NOT bump any version, must NOT edit `pkg/CHANGELOG.md` / `pkg/manifest.json`
> / `pkg/README.md`, and must NOT run `./build.sh` (it installs into a single shared r2modman profile, so
> concurrent agents clobber each other — build with `dotnet build -c Release --no-incremental` instead).
> The integrator owns versioning, the changelog and installation in Wave 3. The rest of this section is
> retained only as the original rationale.**

- This is a client-side behavior change to Organize; the moves are generic MUC and it adds only new
  client/synced config. It is cross-compatible with 1.1.x, so ship as a PATCH (1.1.2): the server
  updater auto-deploys it and no one is locked out. (If you'd rather signal a big feature, 1.2.0 is
  fine but forces a coordinated release — not necessary here.) Recommendation: 1.1.2.
- Bump csproj + Plugin.cs + pkg/manifest.json together (build.sh guard). Add a CHANGELOG entry.
- Build with `./build.sh` (--no-incremental); write .cs via bash heredoc, not the Edit tool (mount
  truncation); verify md5 change + a known new string in the DLL. Install to the test profile.
  Commit to dev; do not publish.

## 12. Testing (agent can't play the game)
Unit-test the PURE allocator offline (throwaway console or tests/ project, no Unity):
- volume sizing: a bucket with 50 wood @ maxStack 50 = 1 slot; 51 wood = 2 slots; multi-type category
  sums per-type ceil.
- allocation: big category claims multiple chests; small buckets share the remainder deterministically.
- anchors: a pinned/station chest is used first and never repurposed; foreign items evicted.
- gear split into weapon/armor/tool; ungrouped consolidated per type.
- under-provisioned base: plan packs to capacity, reports the remainder, loses nothing.
- re-run stability: feeding the post-move census back in yields ~zero moves.
Then in-game (owner, single-player) test script: build a messy base with a forge + cauldron, some
empty chests, piles of wood/metal/food/misc/gear scattered → Organize → confirm → verify each bucket
lands in the right chest(s), big piles span multiple chests, empties get claimed near their stations,
gear splits into three chests, and a second Organize moves nothing.

## 13. Open defaults (veto if you disagree)
- Full re-pack with churn minimization: correctly-placed items stay; only misplaced/homeless items
  move. (Not a full teardown every run.)
- Gear homes have no natural station, so they use the nearest empty chest (weapons/armor/tools each).
- Under-provisioned overflow stays in place with a message rather than cramming randomly.
- Group-overlap resolved by a fixed group order (to be written down in code).

## 14. Scalability (hundreds of chests)

- **Planning is cheap and near-linear.** Census is one O(chests × slots) pass (~10k reads for 300
  chests); allocation is O(buckets × chests) with a small bounded bucket count. Sub-ms to low ms, once.
  **CORRECTION — that is true of the pure allocator only.** The Unity adapter `Organizer.BuildPlan` is
  100–500 ms at 300 chests as currently written; see §15.2 for the five hot spots and the required fixes.
- **Execution is the limit.** Cost is linear in items-in-motion; each move is a MultiUserChest RPC to
  the owner (client→server on a dedicated server). A big base = hundreds–~1.5k moves (tens of seconds);
  a mega storage hall (200–400 chests) = several thousand RPCs (minutes + real MP server load).
  Framerate-safe (per-frame budget) and loss-safe, but not instant.
- **Required refinements for scale (supersedes the multi-pass in §6):**
  - Plan ONCE, then execute with a **retry queue** — moves that don't fit re-queue as chests drain.
    O(moves), not O(passes × census). Drop the re-census-per-pass idea.
  - **Churn minimization** means only the first organize of a messy base is heavy; re-runs move ~nothing.
  - Keep `OrganizeMovesPerTick` conservative to protect the server RPC queue; optional per-run move cap
    with a "press again to continue" so one click can't fire thousands of RPCs at once.
  - Cache station lookups once per run instead of calling `GetCraftingStation` per chest.

## 15. Deferred execution ("keep tidying" mode) — analysis + decision

Question raised after §14: planning is cheap, execution is the cost — so why not persist a plan, smear
the moves over a long period in the background, materialize a chest's contents the moment a player opens
it, and prioritize chests by proximity to players? That implies keeping our own item index.

**Verdict: smear yes — but not the way §14 assumed. No persistent per-item index. No materialization
from unloaded chests (a narrow loaded-only version is accepted instead). Two of the mechanisms first
proposed here were wrong and are corrected below (§15.6, §15.2) — this section was reviewed against the
MultiUserChest source and the ChestButler code, and the review broke things.**

**Scope, decided (see §15.11): the continuous "keep tidying" mode itself is DEFERRED TO 2.1. The
corrections in §15.2, §15.6, §15.8 and §15.10 are in scope for 2.0 regardless — they are defects in the
one-press path, not tidy-mode prerequisites. Read this section for those; treat §15.5/§15.7/§15.9 as the
2.1 design record.**

### 15.1 The engine constraint that reframes it
Valheim only instantiates `Container` objects in zones loaded around a player. `ContainerTracker` is fed
by the `Container.Awake` patch, so **an unloaded chest does not exist to this mod at all.** Consequences:

- There is no "quietly organize the far end of the base while the player stands at the forge."
  Background work is only possible where a player already is.
- **Proximity prioritization is therefore not a feature to build — it is the only mode the engine
  permits.** Distant chests are already invisible; they cannot be deprioritized because they were never
  candidates.

**Open question — must be measured, not assumed.** Zones are 64 m. The guaranteed-loaded radius for an
N-zone block is `(N + 0.5) × 64 − 32` (a player sits up to 32 m off their zone centre):

| block | guaranteed | best case |
|---|---|---|
| 3×3 (`m_activeArea = 1`) | **64 m** | 96 m |
| 5×5 (active + distant = 2) | **128 m** | 160 m |

Creatures are active in the `m_activeArea` block, but *structures* are reported visible out to
`m_activeArea + m_activeDistantArea` — and player-built pieces may be in that class. So the default
`SorterRadius = 128 f` is either exactly 2× over-reach (3×3) or exactly right (5×5). **This is a coin
flip we have not resolved.** Resolve it before capping the radius or writing any Thunderstore copy about
range.

Note the check itself: `m_zoneSize` / `m_activeArea` / `m_activeDistantArea` are Unity-serialized fields
on the ZoneSystem prefab, so **reading the C# initialisers out of `assembly_valheim.dll` is not
authoritative** — log `ZoneSystem.instance` values at runtime, and check `ZNetView.m_distant` on
`piece_chest` / `piece_chest_wood` / `piece_chest_blackmetal`.

### 15.2 Correction: planning is NOT sub-millisecond
§14 costed the *pure allocator*. `Organizer.BuildPlan` is the Unity adapter, and that is where the cost
lives. Measured against the current code, per call at ~300 chests:

- `Stations.GroupsForChest` runs **per chest**, walking `CraftingStation.m_allStations` + every
  registered processor, each with `IsReal()` → `GetComponentInParent<ZNetView>()` — tens of thousands of
  native hierarchy walks.
- It also emits **one `LogInfo` per chest**, synchronously.
- `Filters.GetSpec` has `CacheTtl = 3 f` — the same order as any sane re-plan interval, so the cache
  misses nearly every time; each miss re-parses every nearby `Sign` and calls `ContainerTracker.NearestTo`
  over every container.
- `Names.Matches` allocates a string per token per call (`token.Trim('*')`), on the order of 10⁵–10⁶
  allocations per plan.

Realistic cost: **tens of ms at 100 chests, 100–500 ms at 300** — on the main thread, inside
`FixedUpdate`, plus sustained GC churn. "Re-plan every tick" is not free and throttling does not fix it.

**Required before any continuous mode ships (and worth doing for the one-press path anyway):**
1. One spatial station pass per run, cached by chest (§14 already said this; the first draft of §15 forgot it).
2. Demote the per-chest station log to `LogDebug`.
3. Raise `Filters.CacheTtl` above the re-plan interval, or invalidate on sign/pin change instead of by time.
4. Cache `Names.Matches` token parsing (pre-split patterns once per spec).
5. **Re-measure with a stopwatch before trusting any "planning is cheap" claim in this doc.**

### 15.3 Rejected: a persistent shadow index of item locations
The cost is not memory or CPU — it is **invalidation**. Every cached entry is a claim about a chest this
client does not own, mutable by other players, by MUC transfers in flight, and by any hopper /
craft-from-container mod. `Organizer.Run` already has the correct posture: it re-checks
`sInv.GetAllItems().Contains(item)` and re-runs `Router.Room` before every single move, treating the plan
as advisory. Widening the staleness window makes cached data *less* trustworthy while still requiring the
same live re-validation.

**Rule: never move based on cached state, only on live state.**

*Open, not rejected:* a **read-only census of unloaded chests** (prefab + serialized inventory, straight
off `ZDOMan`'s sector database, no instantiation and no writes) would not decide moves — it would decide
*homes*, fixing the real defect in §15.1: today every plan sees a different arbitrary loaded subset, so
bucket→chest allocation is non-deterministic between runs. That is a genuine problem worth solving. It is
also unverified (the exact ZDO key and the sector query surface need checking) and it is **not** in scope
for 2.0. Log it as a 2.1 investigation.

### 15.4 On-open behaviour: reject the general form, accept the narrow one
**Rejected — materializing from unloaded sources.** To fill a chest on open you need its source stacks
live. If a source is unloaded, `ContainerHandler` cannot be used at all; the only workaround is writing
serialized inventories directly into ZDOs this client may not own. `Container.Save()` is owner-gated, so
a non-owner write is local-only and is overwritten by the owner's next save — **silent item loss**, and
this is confirmed by how every surveyed mod (AzuAutoStore, MyLittleUI) gates ZDO writes, and by our own
`Filters`/`Organizer` ownership claims. Not superstition. Never do it.

It also inverts a safety invariant — `Organizer.Run` deliberately skips `tgt.IsInUse()` so it never yanks
ownership from a browsing player — and it changes the mod's contract from "a butler carries your things
around" to "items appear when observed."

**Accepted (optional, config-gated) — auto-Pull on open, loaded sources only.** `Puller.PullInto` already
implements exactly "fill this chest from nearby storage over the sanctioned MUC path," respecting the
chest's own pins and sign filters. Firing it on open for a chest with explicit filters is a few lines,
introduces no new write path, and carries none of the duplication risk above. Default **off**.

**Also accepted:** on open, **reprioritize** — move queued transfers touching that chest to the front, and
pause transfers involving it while `IsInUse()`. Reuses the check `Organizer.Run` already performs.

### 15.5 Accepted: "keep tidying" mode — with the guards the first draft missed
Store intent, not a plan; re-plan live; reuse the owner-gated tick. The review broke the naive version in
five places, so all of the following are **required**, not optional polish:

- **State:** a bool + generation counter on the sorter's own ZDO (the `psort_sorter` pattern in
  `SorterZdo`). Semantics: "this base wants tidying," not "here is a plan."
- **Execution must be owner-gated, and it currently is not.** `Organizer.Run` has zero `IsOwner` checks —
  its own XML doc says so deliberately, because one-press Organize is a client action. Continuous mode
  needs: (a) a single static in-flight guard so a 3 s tick cannot start a second coroutine over the same
  base, and (b) an abort check each frame if the originating sorter's `nview.IsOwner()` goes false.
  Without both, ZDO ownership migrating to a second player yields two clients executing overlapping plans.
- **Termination cannot be "one pass with zero moves."** That is evaluated over the *loaded subset*: enable
  tidy while standing in the already-clean half of the base and the flag clears in seconds while the messy
  half is never touched. Require **N consecutive zero-move passes over a non-zero, stable census**, and
  **skip the tick entirely when `Player.m_localPlayer` is null** — otherwise a server-owned sorter ZDO
  makes `PlayerCanAccess` reject every candidate, yielding zero moves and silently erasing the flag.
- **The flag can go stale after all** (§15.3's "nothing to invalidate" was too strong): its *meaning*
  lives in the server-synced `[ItemGroups]`/`[Stations]` config, which `Groups.Rebuild`/`Stations.Rebuild`
  swap live on `SettingChanged`. An admin moving `coal` from `fuel` to `ores` would reshuffle every
  tidy-enabled base with no user action. Store a **config hash** beside the flag; on mismatch, clear the
  flag and tell the player. Also clear tidy in `SorterZdo.SetSorter(c, false)`.
- **Preview/confirm survives.** §2 makes preview-then-confirm the contract; enabling a mode that fires
  thousands of unannounced RPCs breaks it. Require a preview + explicit confirm on the **first** pass after
  the flag is set, and keep a per-run move cap.

### 15.6 Correction: `InventoryBlock` is NOT a backpressure signal — do not build flow control on it
The first draft proposed credit-based flow control keyed on `InventoryBlock.IsSlotBlocked`. **That does not
work.** Verified against the MultiUserChest source:

- `InventoryBlock.CanBlockSlot(slot) => slot.x >= 0 && slot.y >= 0`, and both `BlockSlot` and `ReleaseSlot`
  early-return when it is false. ChestButler always passes `to = new Vector2i(-1, -1)` (`Organizer`,
  `Puller`, `SorterBehaviour`). **The Organize and Pull paths therefore never create a block at all** —
  there is nothing to meter. (`AddItemToChest` *does* block, on the source's real `m_gridPos`, which is why
  the `SorterBehaviour` check works. The two paths are not symmetric.)
- `ReleaseSlot` is called only from the two RPC *response* handlers. `ReleaseBlockedSlots()` is dead code,
  and `PackageHandler` has no timeout, TTL or sweep. **A dropped response leaves a permanent block.** A
  credit scheme inferring liveness from blocks would drain to zero and deadlock, silently.

**Replacement:** keep our own ledger. `RemoveItemFromChest`/`AddItemToChest` return a request handle
(non-null = RPC in flight; null = applied synchronously because we own the container). Cap outstanding
handles, attach a **deadline** to each, release on timeout with a log line and re-plan. Debit destination
free slots locally at issue time — `Router.Room(tInv, item)` reads a local inventory that does not reflect
in-flight adds, so K concurrent moves into one chest all pass the room check and over-commit it.
If that ledger is not built, **keep the fixed per-frame budget** — it is honest, and it is what ships today.

### 15.7 The item-loss window continuous mode would widen
`AddItemToChest` removes from the source **locally and immediately, before the RPC**. If the target peer
disconnects or the target object is destroyed by a zone unload, no response ever arrives and the item is
gone. (`RemoveItemFromChest` removes nothing locally, so it degrades to a no-op instead — this asymmetry
matters.) One press exposes a few seconds; continuous mode exposes **every zone-unload boundary, forever**,
unattended, which is exactly when a walking player crosses one.

Mitigations, both required if tidy mode ships: re-resolve the target by ZDO uid on response and abort
(returning the item) if the `NView` is invalid; and refuse to issue moves whose target is more than about
half a zone from the player.

### 15.8 It will fight the live sorter tick (livelock)
`Router.FindTarget` scores pin (3) / group (2) / `ContainsFallback` (1). `OrganizePlanner.ChooseTarget` has
a **Station tier that outranks Holds** — despite the planner's XML doc claiming it mirrors Router exactly.
Concretely: chest A sits by the forge with no pin; chest B far away holds 40 iron. The sorter pushes iron
to B (tier 1; A does not qualify at all); the tidy re-plan sees Station beats Holds and moves it B → A;
more iron arrives in the sorter and goes back to B. **Every pass yields moves forever — the flag never
converges and the §15.2 planning cost is paid indefinitely.**

Fix one of: give `Router` the same station tier (making the planner's mirroring claim true), or suppress
`SorterBehaviour`'s push on any sorter whose tidy flag is set. Do not ship tidy mode without one.

### 15.9 Two sorters, two players, no dedup
`ContainerTracker.Candidates` is an unreserved radius query and `BuildPlan` passes `excludeSorters: false`,
so two sorters with overlapping radii each see the other's contents as a source. `InventoryBlock` is
per-local-`Inventory`-instance, so player A's in-flight state is invisible to player B: both can plan a
move of the same stack, both issue removes, and both `ClaimOwnership()` the destinations — ownership
ping-pongs between clients.

Cheapest fix: stamp a **tidy claim** (owner id + timestamp) on each target chest's ZDO before planning, and
skip chests claimed by another peer within the last N seconds.

### 15.10 Escape hatch gap (pre-existing, blocks this feature)
§15.5 leans on `ManualOnly`/`Ignore` as the way to opt a chest out. But `GuiPatch.Refresh` only shows the
Manual toggle when the chest already has pins, so **an empty chest cannot be marked ignore from the UI** —
and empty chests are exactly what §4's allocator claims as new bucket homes. Show Pin/Manual/Clear on empty
chests as part of W1, independent of tidy mode.

### 15.11 Scope call — DECIDED
The one-press path (§1–§14) ships in 2.0 and is the contract.

- **IN SCOPE FOR 2.0** (all of these stand on their own, independent of tidy mode): the §15.2 planner-cost
  fixes; the §15.6 correction — do **not** build flow control on `InventoryBlock`, keep the fixed per-frame
  budget unless the request-handle ledger is built; the §15.8 `Router` station-tier parity fix; the §15.10
  empty-chest Manual/Ignore gap; and resolving the §15.1 radius question.
- **DEFERRED TO 2.1 — owner decision: "keep tidying" mode.** W1 does not build it in 2.0: no ZDO flag, no
  tick, no UI toggle. When it lands it needs §15.5's five guards, §15.7's unload/item-loss mitigations and
  §15.9's multi-sorter claim stamp. Deferring costs no rework — it reuses the same census, allocator and
  execution primitive.
- **2.1 investigation:** the read-only unloaded-chest census for home stability (§15.3).

**Design target: assume a 400+ chest base until measured.** The real worst case is unvalidated, so build
for it — any O(chests²) pass or per-chest allocation in the hot path is a defect, not a nit.

## 16. Allocator audit — required corrections to §4/§5/§6

Three independent hostile reviews of the allocation math, the execution phase and the 400-chest scale
target. Everything below is traced to code or arithmetic. **§16.1 breaks the design's own acceptance
test and must be fixed before W1 starts; §16.2 is an item-loss set; §16.3 is scale.**

### 16.1 The allocator has no fixed point (BLOCKER — invalidates §7, §13 and the §12 re-run test)

**Nothing persists a claimed home.** §4 step 4 requires an *empty* chest to claim, and run 1 makes its
own claimed chests non-empty. Nothing is written to the claimed chest's ZDO — no pin, no sign, no
station — so on run 2 it is just a non-empty free chest, preference (a)/(b) skip it, and a **fresh**
empty chest is claimed instead; §4 step 6 then relocates the whole spill.

Worked: bucket `wood` = 2,000 Wood + 37 FineWood + 12 RoundLog = 42 slots. Anchor A (24 slots, `sort:
wood` sign) + claimed empty B. Run 2 claims C and moves 849 items B→C. Run 3 claims D. **A bucket with
no anchor at all relocates 100% of itself on every press, forever.** This directly refutes §7 ("a second
Organize produces near-zero moves"), §13 ("correctly-placed items stay") and §12's acceptance test.

**Required fix:** persist the claim. A new `psort_home` ZDO string key on the chest (same pattern as
`psort_items`/`psort_manual` in `Filters`), written when a chest is claimed as a bucket home, read as an
anchor in §4 step 2, cleared by the existing Clear button. **Without this the allocator does not
converge and v2 is worse than v1.** Add it to §8's file list and to the roadmap's W1 ownership.

### 16.2 Item-loss and duplication set (all in scope for 2.0)

1. **No in-flight guard on `Organizer.Execute`** — it unconditionally `StartCoroutine(Run(plan))`. Press
   Organize on sorter A (a 400-chest run is tens of seconds), then on sorter B: B's `BuildPlan` cannot
   see A's in-flight moves (our `to=(-1,-1)` calls create no `InventoryBlock`, §15.6), so both issue a
   remove for the same stack. *Fix: one `static bool _running`, cleared in a `finally`.* This was listed
   as a tidy-mode guard in §15.5 — it is required for the **one-press** path.
2. **`tgt.IsInUse()` is a local flag.** A remote player browsing chest X sets `m_inUse` on *their*
   client; our copy reads false, we `ClaimOwnership()`, and their deposit is never saved (owner-gated
   `Container.Save`). The comment "don't yank ownership" documents a guarantee the check cannot provide.
   *Fix: gate on `tgtNv.GetZDO().GetOwner()` being us or nobody, re-checked immediately before the claim.*
3. **Destination over-commit.** `Router.Room` is re-checked per move but never **debited**, and in-flight
   adds are not reflected locally. Three 50-wood moves into a chest with one free slot all pass. The
   source owner has already applied the remove. *Fix: a per-run `roomLeft[target]` ledger debited at
   issue time — the cheap half of §15.6.*
4. **Source ZDO is never validated.** `Run` checks `tgtNv.IsValid()` but nothing for the source. A chest
   destroyed mid-run spills its contents as ground drops on its owner while we issue a remove against
   the dying ZDO. *Fix: re-resolve both endpoints by uid immediately before issue.*
5. **The §14 retry queue has no termination rule** — and §10's claimed proof cites the `MaxPasses` cap
   that §14 deleted. Three non-terminating generators: NG+ mixed-`worldLevel` stacks that can never
   merge (planner budgets `emptyLeft × maxStack`, `Router.Room` returns 0 forever); a two-chest eviction
   cycle with no spare slots; a target a player leaves open. *Fix: per-move attempt counter (N=2) and
   drop the queue if a full drain yields zero successful issues.*
6. **Confirm executes the stale preview.** `OnOrganizeClick` runs `Organizer.Execute(_pendingPlan)` with
   no re-plan, on a census up to 5.5 s old, then executes it for minutes. `movedItems` counts *issued*
   amounts, not completed ones — both numbers shown to the player can overstate. *Fix: re-plan on
   confirm (100–500 ms, once) and report successes.*
7. **Wards and access are plan-time only.** Neither `PrivateArea.CheckAccess` nor `PlayerCanAccess` is
   re-checked in `Run`, so a ward raised mid-run is bypassed via `ClaimOwnership` + MUC.
8. **§15.9's multi-sorter claim stamp moves into 2.0.** Two players with overlapping 128 m radii each
   pressing Organize is the same race as tidy mode; `BuildPlan` is an unreserved radius query.

### 16.3 Scale corrections (design target: 400+ chests)

**§15.2's estimate was low and profiled the wrong path.** Measured against the code at C=400, S=35:

- `Organizer.BuildPlan` is **0.6–2 s** (2–5 s with a trophy hall), **synchronous in the click handler** —
  plus 14 MB of churn (85 MB gear-heavy) and 1–3 GC pauses. Freeze is user-visible at ~150 chests.
  Missing from §15.2's list: `AnyTargetPins` is O(C·P) **per non-stackable stack** (8 M `Names.Matches`
  for a trophy hall), `HeldOf` is called twice per chest in the Holds tier, and `Names.Matches` uses
  culture-sensitive `StartsWith`/`EndsWith`.
- **The path that actually breaks the game is the sorter tick, which §15.2 does not cost at all.**
  `SorterBehaviour` decrements `budget--` *after* the "no target" check, so every homeless item costs a
  full `ContainerTracker.Candidates` sweep every second — and a sorter chest full of homeless items is
  the steady state. 32 items × 2 ms × 20 sorters ≈ **1.28 s of CPU per second**. Visible at ~150 chests.
- `Filters.GetSpec` → `ParseNearestSign` → `NearestTo` is **O(C²)** and lands on the tick path, where
  `CacheTtl = 3 f` against a 1 s tick guarantees cold misses: ~19 M distance ops/s and ~480 k string
  allocations/s at 400 chests once players use signs.
- **`OrganizeMovesPerTick` is in the wrong unit.** Per-*frame* means the RPC rate is framerate-dependent:
  4/tick at 60 fps = 240 RPC/s; 16/tick at 144 fps = 2,304 RPC/s from one client. *Replace with
  `OrganizeMovesPerSecond` (default 25, range 5–100) plus `OrganizeMaxMovesPerRun` (default 500, "press
  again to continue"), and cap outstanding requests at ~8 with a deadline.*
- **Config surface:** every entry is `IsAdminOnly`, so a non-admin on a dedicated server cannot turn down
  a mod costing them 40 fps — un-admin-lock the client-side perf knobs. `TransferInterval`'s 0.2 s floor
  is the worst value in the file (20 sorters = 100 ticks/s). `StationRange` is misleading: it changes no
  cost, since `GroupsForChest` scans every station regardless.
- **If the Dedicated Sorter Chest (W3) ships, sorter count scales with base size** and total cost becomes
  O(C²). Hoist `Candidates` into one shared per-tick spatial cache before that piece ships.

### 16.4 Classification corrections to §5

1. **Ungrouped stackables get a chest each.** §5.4 gives every ungrouped type its own bucket, and §4
   step 4 claims a chest for any bucket with demand ≥ 1. 40+ vanilla items match none of the 13 default
   groups (Resin, Feathers, Guck, Chain, Crystal, Tar, Eitr, the four bombs, most cooked dishes…), so
   3 Queen Bees + 1 Wisp + 12 Resin claim **three 24-slot chests for 16 items**, and the ungrouped
   buckets eat 40–70 chests before `wood` gets anything. They are also string-keyed, so §4 step 3's
   "bucket enum order" tie-break does not apply to them at all. *Fix: a `misc` catch-all; promote a type
   to its own bucket only when its demand exceeds one chest.*
2. **Anchor capacity is subtracted once per bucket it anchors.** `$piece_cauldron = "cooking, meat,
   seeds"` → one 24-slot kitchen chest is seeded into three buckets and 24 slots are subtracted from
   each, so **nothing claims a free chest** and ~2,400 items are reported homeless while 200 empty chests
   sit two rooms away. `Filters.PinContents` makes it worse: it pins every distinct type present, so a
   chest pinned while holding 20 types anchors 20 buckets = 480 phantom slots. *Fix: allocate **slots**,
   not chests — one `remainingSlots[chestId]` debited once, in step-3 order.*
3. **Group overlap exists in the shipped defaults.** `FlametalOre` matches `ores`' `*ore` **and**
   `metals`' `flametal*`; smelter and forge chests both legitimately claim it. The "fixed group order"
   §5.2/§13 defer to does not exist, and it must **not** be dictionary iteration order (hash-bucket
   order changes when a group is added — that would relocate the entire ores/metals bucket after a mod
   update). *Fix: an explicit `static readonly string[] GroupOrder` literal + a unit test asserting it
   covers every group.*
4. **Gear classification has no existing mechanism and misfiles by its own spec.** No `m_itemType`
   reference exists anywhere in `src/`; `[ItemGroups]` is a name-token matcher and cannot express "all
   `ItemType.Tool`". §5.3 ships an unresolved question ("pickaxe is a weapon? verify") — pickaxes use
   the Pickaxes *weapon* skill, so as written they file with Frostner. And "plus anything left
   non-stackable" makes tools a trash bucket: a **Dragon Egg** files with your hoes, and modded gear is
   silently misfiled rather than logged, because the catch-all means nothing is ever "unmapped". *Fix:
   make the three gear buckets token-driven like every other group, seeded from an `m_itemType` map, and
   give the catch-all its own `gearmisc` bucket that logs every type it absorbs.*
5. **`sort: off` does not protect a chest's contents.** `ExcludedAsTarget` is tested in `AnyTargetPins`
   and `PickBest` but **not in the stack-enrollment loop**, so an Ignore/ManualOnly chest is still
   enrolled as a *source*. §4's wording ("excluded as targets entirely") matches the code and is the
   bug. A personal stash marked `sort: off` is emptied into the communal base on the first press. v1 is
   milder (it only moved items that already had a home); v2 organizes everything, so this escalates from
   "sometimes leaks" to "always loots". *Fix: one line — `if (c.ExcludedAsTarget) continue;` in
   enrollment. Decide deliberately whether `ManualOnly` is source-exempt too; `Ignore` unambiguously is.*
6. **Dead anchors permanently remove chests from the free pool, silently.** A mistyped sign (`sort:
   metal` — not a group, so it becomes an *item token* that nothing matches), a custom group name (the
   `[ItemGroups]` binder only reads the 13 hardcoded keys, so user-added groups are orphans), a
   `CustomStations` value naming a nonexistent group, or a stale pin from `PinContents` — each makes a
   chest an anchor for a bucket with zero demand, never repurposed. *Fix: define an anchor as "matches at
   least one item present in the census"; warn on sign tokens matching neither a group nor any item.*
7. **Determinism is unimplementable as specified.** §4 promises "distance ties broken by ZDO uid
   (stable)", but `ContainerTracker.Candidates` iterates a `HashSet` (zone-load order), discards the
   distances and uses the **unstable** `Array.Sort`, and `ChestView` has no uid field — nor does §8 add
   one. A symmetric hall with chests at ±3.00 m is the normal build, and the winner flips between
   sessions. `Stations.GroupsForChest` has the same defect (strict `<` over `m_allStations` in Awake
   order), so a chest equidistant between a forge and a stonecutter changes bucket across relogs. *Fix:
   add `ZdoUid` to `ChestView`; sort by `(distance, uid)` with a comparator in both places.*
8. **Execution silently drops moves and reports success.** `Run` has five `continue` paths that abandon a
   move and count nothing; §4 step 5's shortfall report is computed at plan time, so it reads 0 for
   exactly the runtime failures. A co-op partner idling in the `metals` chest means 720 Iron never moves
   and the player is told "Organized 1,340 items". *Fix: `skipped`/`skippedItems` counters in the message;
   emit eviction moves before fill moves for the same target regardless of bucket size.*

### 16.5 What §12's tests cannot catch
§12 exercises `OrganizePlanner` over POD `ChestView`s with test-supplied delegates. **Every finding in
§16.2 except the NG+ case passes a green §12 suite unchanged** — they live in the Unity adapter, the MUC
seam, or multiplayer. Worse, §12's "re-run stability" test feeds back a census in which every planned
move succeeded, which is precisely the assumption §16.2.3–5 break: it will certify §7's determinism while
the shipped code re-plans unexecutable moves forever. **Required additions:** (a) a planner case with one
empty slot, `maxStack` 50 and three unmergeable 20-count stacks, asserting no over-budgeted moves; (b) a
two-client dedicated-server session over one messy shared hall — that single 10-minute test exercises
§16.2.1, .2, .3, .4, .7 and .8.
