# W1 — Organize v2: what landed, what I decided, what you need to test

Branch: `feat/organize-v2` off `dev` (9e08a28). No version bump, no changelog, no `pkg/` edits,
no `./build.sh`, nothing pushed. Built with `dotnet build -c Release --no-incremental`.

Offline suite: **103 assertions, 0 failures** (`dotnet run --project tests/OrganizePlannerTests -c Release`).

---

## 1. Measured, not assumed

The DoD asks for `BuildPlan` stopwatched rather than guessed. Two numbers:

**The pure allocator at the 400-chest design target: 4.4 ms**, and it produces 1,147 moves on a
messy synthetic base of 400 chests / 13 buckets. That is test [19], which also asserts the base
is stable on the second and third press. So §14's "planning is cheap" holds for the allocator
itself — it was always the Unity adapter that cost, exactly as §15.2 corrected.

**The adapter is now stopwatched in-game.** Every `BuildPlan` logs
`[organize] BuildPlan took N ms: M move(s), B bucket(s), H item(s) with no room`. That is the
number roadmap §9 item 8 wants, and it is the one thing I cannot produce from here — please
capture it on your biggest base, before and after, and drop it into the pre-flight table.

The adapter should be much cheaper than the 0.6–2 s §16.3 measured: the per-chest
`Stations.GroupsForChest` scan (with its per-chest `GetComponentInParent<ZNetView>()` walks) is
replaced by one spatial pass per run plus an O(hits) lookup per chest, and the planner no longer
calls `AnyTargetPins` per non-stackable stack — classification is one pass over distinct item
types, not per stack.

## 2. The fixed point (§4.1/§16.1) — and a correction to why it matters

`psort_home` is implemented: `Filters.GetHome/SetHome/ClearHome`, written only for chests the
allocator claims itself, read back as `AnchorKind.Home`, released when a bucket no longer needs
the chest, cleared by the Clear button.

**Correction to §16.1's reasoning.** §16.1 says a bucket with no other anchor "relocates 100% of
itself on every press, forever" because step 4's preference test asks for an *empty* chest and a
claimed chest is no longer empty. That is true of the algorithm as §4 describes it, but it is not
true of what I built, and the difference is worth recording so nobody later thinks the marker is
dead weight:

- Capacity here is "slots that will be free once everything movable has moved", not "slots free
  right now". A chest full of items that are themselves being reassigned still reads as empty, so
  the claim order is a pure function of the base's geometry and does not flip between presses.
- Consequently **pressing Organize twice with nothing else changing converges even with the marker
  discarded.** Test [11]'s negative control demonstrates this explicitly.
- Where the marker genuinely earns its keep is when the census *changes*. Buckets are allocated
  largest-demand-first, so a new, bigger category claims the nearest chests — including ones another
  bucket already settled into. The negative control covers that too: settle 420 wood, then mine
  2,000 stone. With markers, wood does not move (0 items). Without them, all 420 wood relocates.

So: still required, still load-bearing, for a different and more realistic reason than the plan gives.

**A convergence trap I hit and fixed, which is not in the plan.** `AnchorKind` ordering is the fill
order, and `Home` must be the *weakest* anchor. A freshly claimed chest has no anchor on run 1 and
carries `Home` on run 2. If `Home` outranked `Station`, a bucket with both a station chest and a
claimed chest would fill station-first on run 1 and claimed-first on run 2, so items would swap back
and forth forever. I originally wrote it the other way round and the acceptance test caught it.

Related: the per-norm choice of *which* of a bucket's chests to fill must not depend on what the
chests currently hold. Ordering by "who already holds the most of this item" reads like churn
minimisation but makes the target layout a function of the current layout, which produced 19
residual moves on the second press of a 400-chest base (settling only on the third). The fill order
is now fixed — anchor strength, then priority, then distance, then ZDO uid — and churn minimisation
lives entirely in the diff step. That is what makes "second press moves zero" provable rather than
lucky, and it made planning ~2× faster as a side effect.

## 3. Files

| File | What |
|---|---|
| `Core/OrganizePlanner.cs` | **Rewritten.** Census → classify → slot demand → slot allocation → target distribution → diff. Pure, no Unity/config/clock. |
| `Core/Organizer.cs` | **Rewritten.** One station pass per run, classification, retry queue, per-second rate, outstanding-request ledger, live re-validation. |
| `Core/Gear.cs` | **New.** `m_itemType` → weapons/armor/tools + a `gear:misc` catch-all that logs what it absorbs. |
| `Core/BucketKeys.cs` | **New.** Bucket-key formats. These are persisted in ZDOs, so the format is a contract; compiled into the test project. |
| `Core/GroupTables.cs` | **New.** `Defaults` + `GroupOrder` split out of `Groups` so §16.4.3's coverage test can actually run offline. Behaviour unchanged. |
| `Core/Throttle.cs` | **New.** §16.6 self-throttling. Measures our own ms/s, hysteresis, bounded, logs every adjustment. |
| `Core/Filters.cs` | `psort_home` key + `Home` on `FilterSpec` (cached with the rest, so a plan costs one ZDO read per chest, not two). |
| `Core/Stations.cs` | Append-only: `HitsAround`, `GroupsNear`, `GroupsForChestCached`. |
| `Core/Router.cs` | Station tier (§15.8), using the cached station pass so it does not land per-candidate-per-item on the tick. |
| `Core/OrganizeConfig.cs` | `MovesPerSecond`, `MaxMovesPerRun`, `IncludeGear`, `MiscPromoteSlots`; calls `Gear.Init`. |
| `Core/Groups.cs` | Points at `GroupTables`. |
| `Core/SorterBehaviour.cs` | **Deviation — see §6.** Throttle hookup only (interval, miss cooldown, one `using`). |
| `Plugin.cs` | One deletion: the `OrganizeMovesPerTick` forwarder, with the key it pointed at. |
| `Patches/GuiPatch.cs` | Scoped: re-plan on confirm, shortfall in the preview, Clear releases the home, empty-chest Manual toggle. |
| `tests/OrganizePlannerTests/**` | Rewritten, 103 assertions. |

## 4. Three open defaults — veto by editing config, no rebuild needed

Written in the §13 "veto if you disagree" spirit. Each is a config string, not a code constant,
precisely so you can overrule me in-game.

1. **Gear buckets are Organize-only, not `[ItemGroups]` groups.** §16.4.4 asks for them to be
   "token-driven like every other group", but the same paragraph notes `[ItemGroups]` is a name-token
   matcher that *cannot* express "all `ItemType.Tool`" — so they cannot literally be groups. I used
   `m_itemType` as the signal plus three override token lists (`[Organize] ToolTokens` /
   `WeaponTokens` / `ArmorTokens`) for the extensibility. **Consequence: the live sorter tick still
   does not route gear** — only Organize does. Making them real groups would fix that asymmetry but
   would start moving gear on the tick, which is a behaviour change beyond what §2 locked. Say the
   word and it is a small change.
2. **Pickaxes file as tools; axes stay weapons.** Verified against the assembly: there is no
   `Pickaxe` item type — pickaxes are weapons by `m_itemType` (Pickaxes weapon skill), exactly the
   misfile §16.4.4 predicted. Default is `ToolTokens = "pickaxe*"`. Axes left as weapons because
   they are a real weapon on their own skill line and `axe*` vs `battleaxe*` is a confusing
   distinction to impose. Add `axe*` to `ToolTokens` if you disagree.
3. **`MiscPromoteSlots = 24`** — an ungrouped item type earns its own chest only above one vanilla
   chest's worth of slots; below that it shares `misc`. §16.4.1 says "exceeds one chest" without
   saying which chest, and modded chest sizes vary, so it is a number you can set.

## 5. §16.2's eight findings

| # | Status |
|---|---|
| 1 in-flight guard | Kept from 1.1.2 (`_running`, released in `finally`). |
| 2 local-only `IsInUse` | **Fixed as specified**: gated on `GetZDO().GetOwner()` being us or nobody, re-checked immediately before the claim. The local `IsInUse` check is kept as a second signal. The *synced* in-use flag is listed unresolved in handoff §7 and is not designed here. |
| 3 destination over-commit | Kept from 1.1.2 (`promised[target]`, debited at issue). |
| 4 source never validated | **Fixed**: both endpoints re-resolved and `IsValid()`-checked immediately before issue. |
| 5 retry queue never terminates | **Fixed**: 2 attempts per move, and the whole queue is abandoned the moment a full drain issues nothing. Plus the NG+ generator is fixed at the root — see below. |
| 6 confirm executes a stale plan | **Fixed**: confirm re-plans. See §7 for the half of this I could not do. |
| 7 wards/access plan-time only | **Fixed**: `PlayerCanAccess` + `PrivateArea.CheckAccess` re-checked per move, both endpoints. |
| 8 multi-sorter claim stamp | **NOT DONE** — see §6. |

**The NG+ generator is fixed at the root, not just bounded.** Stacks of the same item at different
world levels can never merge, so budgeting `ceil(total / maxStack)` for them under-counts slots and
the surplus moves re-queue forever. The census now gives unmergeable stacks distinct identities
(`iron#2`), so the slot maths is exact. Test [10] proves it and contrasts it with the mergeable case:
three unmergeable 20-stacks into one free slot moves 20 items and reports 40 with no room, where one
mergeable 60-pile correctly moves 50.

## 6. Deliberately not done, and one file-ownership deviation

- **`Core/SorterBehaviour.cs` — deviation, flagged.** Roadmap §3 says it is untouched in 2.0. But
  §16.6 (an owner decision, and §16 overrides earlier text per the handoff) names "stretch the sorter
  tick interval" and "lengthen the miss cooldown" as two of the three throttle levers, and both live
  in that file. A throttle that only slows Organize while leaving the sorter tick — which §16.3 calls
  "the path that actually breaks the game" — unthrottled would be building the wrong thing to satisfy
  a file list. The edit is four lines: interval, cooldown, and a `using` for the measurement.
- **§16.2.8 / §15.9 multi-sorter claim stamp: not built.** Two players pressing Organize on
  overlapping radii is a real race, but the stamp needs a claim-expiry policy and a peer-identity
  scheme that is not specified anywhere, and getting it wrong strands chests as permanently claimed.
  It wants its own small design. The in-flight guard covers the single-client case, which is the
  common one.
- **W3's dependency is NOT addressed.** Roadmap §3 says "W1 must treat default-sorter chests as
  claimable/free targets". Sorters are still excluded as targets (`excludedTarget = IsSorter(c) || …`).
  They *are* visible as sources — `BuildPlan` passes `excludeSorters: false` — so the allocator sees
  their contents. Distinguishing "a dedicated Sorter Chest piece" from "a normal chest the player
  toggled" needs `SorterZdo.WasDefaulted`, whose semantics are W3's to settle, and W3's plan is
  explicitly meant to be written at the start of its own turn. Guessing now would bake in a coupling
  to a design that does not exist. **This is the W1→W3 interface item to resolve in W3.**
- **Tidy mode: not built** (deferred to 2.1). No ZDO flag, no tick, no toggle.
- **Radius untouched.** Still 128 m, uncapped, and I wrote no copy about range — handoff §7.
- **`IsSlotBlocked` guards left in place.** Whether our `to = (-1,-1)` calls create a block is
  unresolved pending an in-game check; removing a guard on an unverified assumption is the wrong risk.

## 7. What I could not verify from here — read this before you trust the numbers

**Honest completion reporting is only partly possible, and the plan asserts more than the API
supports.** §16.2.6 says to "report successes" rather than issued amounts. I verified against
`MultiUserChest.dll` what is actually available:

- `ContainerHandler.RemoveItemFromChest` **does** return a handle (`RequestChestRemove` with a
  `RequestID`) — §15.6's premise is correct, and the outstanding-request ledger is built on it. A
  request disappearing from `PackageHandler` is a real acknowledgement signal, reachable through the
  public `PackageHandler.GetPackage<T>` with no patch into MUC. Requests are capped at 8 outstanding
  with a 10 s deadline, and a timeout logs and releases our reservation, since MUC has no sweep of
  its own.
- But `RequestChestRemoveResponse.Success` / `.Amount` are consumed inside
  `InventoryHandler.RPC_RequestItemRemoveResponse` and exposed nowhere. **Per-move success and the
  actual moved amount cannot be read without a Harmony patch on MUC's response handler** — that
  method is `public static`, so it is patchable, but it is new coupling to MUC internals and a new
  patch file outside W1's declared footprint.

So the HUD reports issued items, items that could not move, items with no room, and transfers that
timed out — each labelled as what it is. It no longer silently overstates by counting dropped moves
as successes, but a response that arrives with `Success == false` is still invisible to us. If you
want true completion counts, the MUC response patch is the way, and it should be a deliberate
decision rather than something I slipped in.

Everything else needing a running game is unverified by construction: I have not seen any of this
execute. In particular the UI changes, the throttle's behaviour under real load, and every
multiplayer path.

## 8. In-game test script

Single-player first, then the two-client test — §16.5 is explicit that one 10-minute two-client
session over a shared hall exercises §16.2.1, .2, .3, .4, .7 and .8, and that the offline suite
cannot touch any of them.

**A. The acceptance test (do this first).** Messy base with a forge and a cauldron, several empty
chests, piles of wood/metal/food/misc scattered, plus loose gear. Organize → confirm → then press
Organize again. **The second press must report "Nothing to organize".** If it moves anything, the
fixed point is not holding and that is the headline bug.

**B. Homes stick under change.** After A settles, mine a big pile of stone and drop it in the sorter,
then Organize. The wood should stay where it is; only the stone should find a new home.

**C. `sort: off` is respected.** Put a sign reading `sort: off` on a chest with junk in it, next to
the base. Organize. Nothing should be taken out of it and nothing put in. This is the one that was
"always loots" in v1.

**D. Empty-chest escape hatch.** Open an empty chest → the button should read **Pin**; press it →
"Chest is empty - marked Manual. Organize will not claim or fill it", and the label becomes
**Manual**. Organize should then leave that chest alone. Press Clear to hand it back.

**E. Gear.** Confirm weapons, armor and tools land in three different chests, and that a pickaxe
files with the tools rather than with the swords. Check the log for
`has no gear bucket - filed under misc` lines — anything listed there is a type worth adding to the
token lists.

**F. Stations.** A chest next to the forge should attract metals/ores; next to the cauldron,
cooking/meat/seeds. The cauldron case is the one §16.4.2 was about — one 24-slot kitchen chest
anchoring three buckets must not make the allocator think it has 72 slots.

**G. Under-provisioned.** Remove most chests, then Organize. The preview should say
"(N won't fit - add more chests)" *before* you confirm, and nothing should be lost.

**H. Throttle + cap.** On the biggest base you have, watch for `[throttle] ChestButler is using
N ms/s … rate scale` lines. Also confirm that hitting `MaxMovesPerRun` (500) ends with "Press
Organize again to continue" and that pressing again continues cleanly.

**I. Two clients, dedicated server, one shared hall.** Both players press Organize at overlapping
times; one player sits with the `metals` chest open while the other organizes. Expected: the second
press reports "Organize already running" locally; the chest held open is skipped rather than having
its ownership yanked, and the count reflects that ("N could not move"). Watch for any item appearing
twice or vanishing — that is the failure mode this whole section exists for.

**J. Pre-flight, while you are in there.** Roadmap §9 items 1–2 are still the coin flip that decides
whether 128 m is right or 2× over-reach: log `ZoneSystem.instance.m_zoneSize` / `m_activeArea` /
`m_activeDistantArea` at runtime, and check `ZNetView.m_distant` on `piece_chest`. Item 8 is the
`BuildPlan` number, which the new log line now hands you for free.
