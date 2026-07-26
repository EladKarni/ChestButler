# ChestButler 2.0 — roadmap for parallel development

Goal of this doc: split 2.0 into workstreams that touch **disjoint files**, so several agents can
build them at the same time and merge with near-zero conflict. The feature list is the easy part;
the file-ownership matrix (§3) and wave plan (§5) are the point.

## 1. What 2.0 is
Four features, shipped together as one coordinated major release:
- **Organize v2** — whole-base, volume-aware allocation: census the base, size each bucket by
  quantity, claim empty chests as new homes. Detailed plan: `docs/organize-v2-allocation-plan.md`.
- **Gather** — a crafting-station "Gather" button that pulls a recipe's ingredients (Craft **and**
  Upgrade tabs) from nearby storage into your inventory, showing a per-ingredient "(N in storage)"
  count. Spec in §7.
- **Dedicated Sorter Chest** — a craftable chest piece that is a Sorter by default (Jötunn
  CustomPiece cloned from the vanilla chest). Spec in §7.
- **Gamepad support** — controller navigation + button hints across all ChestButler UI (chest
  toolbar + the Gather button). Spec in §7.

## 2. Release nature — READ FIRST
- **2.0.0 is a MAJOR bump.** Under `VersionStrictness.Minor`, a 1.1.x peer and a 2.0.0 peer refuse
  each other. This is a **coordinated release**: the server and every client update together.
- The **Dedicated Sorter Chest is a custom prefab** — it must exist on the server AND every client.
  A player without the mod can't render/use it, and removing the mod later orphans placed pieces
  (Jötunn softens this, but note it). This alone forces server/client parity.
- The `PATCH_ONLY` server updater will **skip** 2.0.0 (it's a major jump). Deploy to the server by
  hand and coordinate the group; afterwards, set `.chestbutler_deployed` to `v2.0.0` so 2.0.x
  patches auto-flow again (same bookkeeping fix as the 1.0.2→1.1.0 crossover).

## 3. Workstreams & file ownership (the parallelization key)

| # | Workstream | OWNS (new / rewritten) | TOUCHES (shared) | reads-only | depends on |
|---|---|---|---|---|---|
| W1 | Organize v2 | `Core/OrganizePlanner.cs` (rewrite), `Core/Organizer.cs` (rewrite), `Core/Gear.cs` (new), `tests/OrganizePlannerTests/**` | `Plugin.cs` (`[Organize]` config block + `OrganizeConfig.Init`), `Core/Groups.cs`, `Core/Stations.cs`, `Core/ContainerTracker.cs` (append-only accessors), `Core/Router.cs` (station-tier parity — v2 §15.8), `Patches/GuiPatch.cs` (**scoped**: `OnOrganizeClick` + `ClearPending` + preview string + empty-chest Manual/Ignore toggle) | Filters, Names, SorterZdo | — |
| W2 | Gather | `Patches/GatherPatch.cs` (new), `Core/Gatherer.cs` (new) | `Plugin.cs` (one-line `Gather.Init`), `Core/Puller.cs` (only if a shared chest→player helper is needed) | ContainerTracker, Names, Puller/MUC pattern | — |
| W3 | Sorter Chest | `Core/SorterChestPiece.cs` (new) + icon asset | `Plugin.cs` (one-line `SorterChestPiece.Register`), `Core/SorterZdo.cs` (default-flag helpers), `src/ChestButler/ChestButler.csproj` (icon resource, if not glob-compiled) | ContainerPatches, Jötunn PieceManager | **W1** (allocator must treat default-sorter chests as claimable targets) |
| W4 | Gamepad | `Patches/GuiPatch.cs` (edit), `Patches/GatherPatch.cs` (edit) | — | — | **W1 + W2** (buttons must exist) |

Key facts this table encodes (**revised after a source audit — the first draft understated W1's
footprint by five files**):

- W2 and W3 own **mostly new, self-contained files**. **W1 does not** — it rewrites the two files the
  chest UI is compiled against, so it has the widest blast radius and should be treated as the
  integration risk, not as one of three equal peers.
- W2's UI lives in the **crafting panel** (a new `GatherPatch` cloning `m_craftButton`), NOT the chest
  toolbar `GuiPatch` (which clones `m_takeAllButton`). Two independent Harmony patch classes can both
  postfix `InventoryGui.Show/Update`, so W2 and W1 genuinely don't share UI code. *This was the one
  contentious call in the first draft and it holds.*
- **`Patches/GuiPatch.cs` is compile-coupled to W1**, which the first draft missed. It binds
  `OrganizePlan`, `OrganizePlan.Summary/IsEmpty`, `OrganizeSummary.TotalItems/TargetChests/SourceChests`,
  `Organizer.BuildPlan(Container,float)` and `Organizer.Execute(OrganizePlan)` — all inside the two files
  W1 rewrites. W1 therefore **touches `GuiPatch.cs` in a scoped region** and must treat the
  `OrganizePlan`/`OrganizeSummary` public shape as **append-only**. W4 rebases on it in Wave 2.
- **`Core/SorterBehaviour.cs` is untouched in 2.0** — tidy mode is deferred to 2.1 (owner decision, §2).
  When it lands, it goes inside that owner-gated tick and the file becomes W1's; nobody edits it now.
- **`Core/Router.cs` moves from reads-only into W1's TOUCHES.** `OrganizePlanner`'s XML doc claims it
  mirrors `Router`'s ranking "exactly so Organize and the live sorter never disagree" — it does not:
  the planner has a Station tier that outranks Holds and `Router.FindTarget` has no station tier at all.
  With a sorter running, Organize parks iron by the forge and the sorter tick sends the next batch
  somewhere else. Fix the parity in 2.0 (it is also the precondition for tidy mode in 2.1).
- **`Core/Groups.cs`, `Core/Stations.cs` and `Core/ContainerTracker.cs` are NOT reads-only for W1.**
  `Groups` exposes no ordered enumeration, but v2 §4/§5 require a documented fixed group order for
  tie-breaks; `Stations` exposes no station *position* and no group→station inversion, which v2 §4 step 4a
  needs ("empty chest within `StationRange` of the bucket's station") and §15.2 needs for the one-pass
  station cache; `ContainerTracker.Candidates` discards distances and sorts with the unstable
  `Array.Sort`, defeating v2 §7's "distance ties broken by ZDO uid (stable)". All three take **append-only
  accessors** (`GroupsInOrder()`, `StationsInRange(pos,range)`, `CandidatesWithDistance(...)`).
- **`tests/OrganizePlannerTests/` belongs to W1.** It binds `OrganizePlanner.Plan` and `ChestView`
  directly, so W1's rewrite breaks the build until W1 updates it. It was owned by nobody.
- **W3 depends on W1 behaviourally**, despite touching no shared logic: `Organizer` excludes sorters as
  targets (`bool excluded = SorterZdo.IsSorter(c) || …`) and `ContainerTracker.Candidates` skips them
  entirely, so a base built out of dedicated Sorter Chests would be **invisible to the v2 allocator**.
  W1 must treat default-sorter chests as claimable/free targets; test jointly in Wave 2.
- W4 (Gamepad) edits `GuiPatch.cs` and `GatherPatch.cs` → still a Wave-2 integration step.

## 4. Collision map & rules
- **`Plugin.cs`** — W1's edit is **not** one line. `SorterRadius` / `OrganizeMovesPerTick` / `StationRange`
  are declared as `internal static ConfigEntry<>` fields on `Plugin` and bound in `Awake`, and they are read
  from `Organizer` and `SorterBehaviour`; v2 needs new `[Organize]` entries on top. Either Wave 0 relocates
  the whole `[Organize]` block into `OrganizeConfig` (leaving forwarding properties on `Plugin` so existing
  readers still compile), or §3 must record W1's edit as "field block + Awake block". W2/W3 stay one-line.
- **`Core/SorterZdo.cs`** — in 2.0, **W3 only** (tidy mode's accessors move to 2.1 with it). W3 needs **two**
  hash constants, not one: its default flag *and* a "was defaulted" marker (see §7 W3). Rule: append only,
  at the end of the class; do not reorder or reformat existing members.
- **`Patches/GuiPatch.cs`** — W1 (scoped region), W4 (Wave 2). W1 goes first; W4 rebases.
- **`src/ChestButler/ChestButler.csproj`** — verify it globs `**/*.cs`. If it lists files explicitly, every
  Wave-1 agent adding a new file touches it; and W3's icon needs an `<EmbeddedResource>` entry regardless.
  Pre-add W3's resource line in Wave 0 if so.
- **`pkg/CHANGELOG.md`, version declarations, `pkg/manifest.json`, `pkg/README.md`** → the **integrator
  owns these**. Agents do NOT bump the version or edit the changelog top — otherwise they collide and
  trip `build.sh`'s version-consistency guard. Each agent instead writes a short "what I changed" note
  in its commit body; the integrator consolidates into one 2.0.0 entry at the end.
  **Note:** §11 of `docs/organize-v2-allocation-plan.md` still instructs W1 to ship as 1.1.2 and edit the
  changelog. That section is superseded and now carries a banner saying so — but if you hand an agent that
  doc, say it out loud in the prompt too.
- **`build.sh` is not safe for concurrent agents.** It installs `dist/ChestButler.dll` into a single
  hardcoded r2modman profile and mod-manager cache, outside the worktree and identical for every branch —
  and all branches share the plugin GUID, so three agents building at once overwrite each other's install
  and the md5-verify step in §6 becomes a race. Wave-1 agents build with
  `dotnet build -c Release --no-incremental` only. The **integrator** runs `./build.sh` and does the
  install/test pass in Wave 3.
- Everyone follows the shared build rules in §6.

## 5. Waves, branches, merge order
Base branch: **dev** (currently the 1.1.1 tip). Every workstream = a feature branch off `dev`; agents
should work in **isolated git worktrees** so they never share a working tree.

- **Wave 0 — Foundation (small, land first, ~30–60 min):** introduce a **modular-init** pattern so
  each feature registers itself. Add empty stubs `OrganizeConfig.Init(ConfigFile)`, `Gather.Init(...)`,
  `SorterChestPiece.Register()`, and have `Plugin.Awake` call them (alongside the existing
  `Groups.Init`/`Stations.Init`). Commit to `dev`. Now each Wave-1 agent only fills in its own stub —
  no two agents edit the same `Plugin.cs` region. **Wave 0 is now recommended, not optional** — the audit
  found five more shared-file hotspots, and Wave 0 is where they get pre-partitioned. It should land:
  - the `Init`/`Register` stubs, **and** relocation of the `[Organize]` config block into `OrganizeConfig`
    with forwarding properties on `Plugin` (see §4);
  - two `SorterZdo` stubs for W3: `SetSorterDefault` + `WasDefaulted` (tidy mode's stubs land in 2.1);
  - three append-only accessors: `Groups.GroupsInOrder()`, `Stations.StationsInRange(pos, range)`,
    `ContainerTracker.CandidatesWithDistance(...)` (see §3);
  - the csproj check, plus W3's `<EmbeddedResource>` line if the csproj does not glob.
- **Wave 1 — Parallel (3 agents, disjoint files):** `feat/organize-v2` (W1), `feat/gather` (W2),
  `feat/sorter-chest` (W3), each branched off `dev` after Wave 0. They run fully concurrently.
- **Wave 2 — Integration (serial):** merge W1, W2, W3 into `dev` (any order — trivial conflicts), then
  run `feat/gamepad` (W4) off the updated `dev` so the Organize + Gather buttons exist to wire up.
- **Wave 3 — Release-prep (integrator):** single 2.0.0 version bump across csproj/Plugin/manifest,
  consolidated CHANGELOG + README/Thunderstore copy, build, single-player test pass, then the
  coordinated 2.0 release + manual server deploy.

Merge order into `dev`: **W1 → W2 → W3 → W4 → release-prep** (W1–W3 order doesn't matter).

## 6. Shared rules baked into every agent prompt
- Work ONLY on your branch, and edit ONLY your OWNED files (+ your one-line `Plugin.cs` stub).
- Do NOT bump the version or edit `pkg/CHANGELOG.md`/`pkg/manifest.json`/`pkg/README.md` — the
  integrator owns 2.0.0. Put a "what changed" summary in your commit body.
- Write/edit `.cs` files with a **bash heredoc**, never the Edit/Write tools (the synced mount
  truncates files mid-edit). After each write: `wc -l file && tail -1 file`.
- Build offline with `dotnet build src/ChestButler/ChestButler.csproj -c Release --no-incremental`.
  **Do NOT run `./build.sh`** — it installs into a single shared r2modman profile and cache outside your
  worktree, so concurrent agents clobber each other (see §4). The integrator builds and installs in Wave 3.
- Verify your build by md5-diff of the produced DLL + grepping it for a known new string.
- Never write to a chest inventory directly — every transfer goes through MultiUserChest's
  `ContainerHandler` (copy `Puller`/`Organizer`). This is the one inviolable rule.
- Unit-test any pure logic offline (no Unity); leave a single-player test script in your commit body.
- Commit to your branch; do NOT push prod, publish Thunderstore, or touch the live server.

## 7. Feature specs

### W1 — Organize v2
Full spec: `docs/organize-v2-allocation-plan.md` (census → per-bucket slot demand → chest allocation
respecting pins/signs/stations → claim empty chests → multi-pass safe execution). Owner-locked
decisions: volume-adjusted buckets (not one-per-type), misc consolidated per type, gear split into
weapon/armor/tool, new homes near the matching station. Scale note in §14 of that doc: plan once +
retry queue, churn-minimization, conservative move budget.

**Execution model — read §15 of that doc before writing `Organizer.Execute`.** §15 was audited against
the MultiUserChest source and two of its first-draft mechanisms were refuted; the settled position:

- **`InventoryBlock` is NOT a backpressure signal on our paths.** `CanBlockSlot` requires `slot.x/y >= 0`
  and we always pass `to = (-1,-1)`, so `RemoveItemFromChest` **never creates a block at all**; and
  `ReleaseSlot` only ever fires from an RPC response, with no timeout or sweep, so a dropped response
  blocks a slot permanently. Do not build flow control on it. Either keep the **fixed per-frame budget**
  (what ships today, and acceptable) or build our own ledger off the returned request handle with a
  per-move deadline. §15.6.
- **Planner cost is 100–500 ms at 300 chests, not sub-ms** — `Stations.GroupsForChest` per chest (plus a
  per-chest `LogInfo`), `Filters.CacheTtl = 3 f` thrash, and per-token allocations in `Names.Matches`.
  §15.2 lists five required fixes; they are **in scope for 2.0 regardless of tidy mode**, and W1 should
  stopwatch a plan before and after.
- **No persistent plan and no shadow item index.** Store intent, re-plan live; keep `Organizer.Run`'s
  live re-validation before every move. §15.3.
- **Never write chest inventories via ZDO surgery** to reach unloaded chests — `Container.Save()` is
  owner-gated, so a non-owner write is silently overwritten. §15.4.
- **Fix the empty-chest escape hatch** (§15.10): `GuiPatch.Refresh` only shows the Manual/Ignore toggle on
  chests that already have pins, so the empty chests the allocator claims as new homes cannot be opted out.
- **"Keep tidying" mode is DEFERRED TO 2.1 — owner decision. W1 does not build it.** Do not add the ZDO
  flag, the tick, or a UI toggle. What W1 *does* carry forward from that analysis: the §15.2 planner-cost
  fixes, the §15.6 correction, the §15.8 `Router` station-tier parity fix and the §15.10 empty-chest
  toggle — all of which stand on their own. The 2.1 prerequisites are listed in v2 plan §15.5/§15.7/§15.9.
- **Design target: assume a 400+ chest base until measured.** We cannot validate the real worst case yet,
  so build for it — every O(chests²) or per-chest-allocating path in the plan is a defect, not a nit.

### W2 — Gather (settled design)
A **Gather** button beside `m_craftButton` in InventoryGui's crafting panel, on all crafting stations,
for both the **Craft and Upgrade** tabs. Read the selected recipe (`m_selectedRecipe`) → its resources
(`Recipe.m_resources`: each `Piece.Requirement.m_resItem` + `m_amount`; use `GetAmount(quality)` on the
Upgrade tab). Beside each ingredient, show **"(N in storage)"** = count available across accessible
chests in range (reuse `ContainerTracker` + access/ward checks). Recompute on recipe-select (hook the
requirement setup, e.g. `InventoryGui.SetupRequirement`/`UpdateRecipe`). Click Gather → pull the
**shortfall for the craft amount shown** (read the live x-multiplier; default 1× if awkward) from
storage into the **player inventory** via the Puller MUC path (`ContainerHandler.RemoveItemFromChest`
with the player inventory as destination). New files: `Patches/GatherPatch.cs`, `Core/Gatherer.cs`.
Config via `Gather.Init` (reuse `SorterRadius`; optional enable toggle).

**API corrections from the audit — apply these or the agent will burn a cycle:**
- `InventoryGui.m_selectedRecipe` is **not** a `Recipe`. It is a pair with `.Recipe`, `.ItemData`,
  `.CanCraft` → use `m_selectedRecipe.Recipe.m_resources`. `.ItemData != null` is exactly the Upgrade-tab
  discriminator, and supplies the quality for `Requirement.GetAmount(quality)`.
- `Recipe.m_requireOnlyOneIngredient` exists; Gather would over-pull on those recipes unless handled.
- **Gather is not a drop-in copy of `Puller`.** The MUC primitive takes a destination **ZDOID**
  (`destNv.GetZDO().m_uid` from a `Container`); the player inventory has no container ZDO. Verify MUC's
  chest→player path against `MultiUserChest.dll` **before** writing `Gatherer`; if a shared helper is
  needed, `Core/Puller.cs` moves into W2's TOUCHES.
- Confirmed present and usable: `InventoryGui.SetupRequirement(Transform, Piece.Requirement, Player, bool,
  int) → bool`, `UpdateRecipe`, `m_craftButton`, `m_recipeRequirementList`. Appending a count into the
  `res_amount` `TMP_Text` child of the requirement element is a known-working pattern (MyLittleUI does it).

### W3 — Dedicated Sorter Chest (feasibility + wrinkle)
Jötunn **CustomPiece** cloning the vanilla reinforced chest prefab; on spawn, set the `psort_sorter`
ZDO flag so it's a Sorter by default (add `SorterZdo.SetSorterDefault`/spawn hook; the existing
`SorterBehaviour`/`ContainerTracker` then apply automatically since it's still a `Container`). Register
via Jötunn `PieceManager` after vanilla prefabs load (`PrefabManager.OnVanillaPrefabsAvailable`), with
a build recipe (e.g. wood + bronze nails) and an **icon asset** (the one non-code wrinkle — start from
the vanilla chest icon or render one). Server + client parity is mandatory (custom prefab). New file:
`Core/SorterChestPiece.cs` + the icon. Verify the exact vanilla chest prefab name and Jötunn
CustomPiece clone API against the referenced Jotunn.dll before building.

**Audit corrections:**
- The reinforced chest prefab is **`piece_chest`** (counter-intuitively, `piece_chest_wood` is the *basic*
  chest). `new CustomPiece(prefabName, newPrefabName, PieceConfig)` + `PieceManager.Instance.AddPiece(...)`
  under `PrefabManager.OnVanillaPrefabsAvailable` (unsubscribe after) is confirmed correct.
- **The spawn-default needs two ZDO keys, not one.** `SorterZdo.IsSorter` reads
  `GetZDO().GetBool(SorterHash, false)`, which cannot distinguish "never set" from "player switched it
  off" — so a naive default that writes `SorterHash` on `Awake` would **re-enable Sorter every time the
  zone reloads**, overriding the player. Add a separate `WasDefaulted` marker and only apply the default
  when it is absent.
- **Behavioural dependency on W1 (see §3):** sorters are excluded as Organize targets today
  (`Organizer`: `excluded = SorterZdo.IsSorter(c) || …`; `ContainerTracker.Candidates` skips them), so a
  base of dedicated Sorter Chests would be invisible to the v2 allocator.
- Perf note: `ContainerPatches` attaches a `SorterBehaviour` to every container, and each *enabled* sorter
  runs a full `Router.FindTarget` → `Candidates` sweep every `TransferInterval`. A piece that is a sorter
  **by default** makes that O(n²) as soon as players build a wall of them. Consider a global cap or a
  shared per-frame candidate cache before shipping.

### W4 — Gamepad support
The current `MakeButton` in `GuiPatch.cs` strips `UIGamePad` from cloned buttons. Re-enable controller
support: register the toolbar buttons (and the Gather button) with InventoryGui's gamepad focus/nav so
they're reachable on a controller, and add key hints. Touches `GuiPatch.cs` + `GatherPatch.cs`; must
run after W1/W2 land. Verify Valheim's current gamepad/`UIGamePad`/`ZInput` API before wiring.

## 8. Risks / open items
- **Zone-loading vs `SorterRadius` = 128 m — UNRESOLVED, and it is a coin flip.** Chests only exist to the
  mod while instantiated. Guaranteed radius for an N-zone block is `(N + 0.5) × 64 − 32`: **3×3 → 64 m**,
  **5×5 → 128 m**. Creatures are active in the `m_activeArea` block, but *structures* are reported visible
  out to `m_activeArea + m_activeDistantArea`, and it is unverified offline whether player-built chests are
  in that class. So today's default is either exactly 2× over-reach or exactly right. **Measure at runtime**
  (log `ZoneSystem.instance.m_zoneSize` / `m_activeArea` / `m_activeDistantArea`, and check
  `ZNetView.m_distant` on the chest prefabs) — reading the DLL's C# field initialisers is **not**
  authoritative, since these are Unity-serialized prefab fields. Decide before the 2.0 Thunderstore copy is
  written; it changes what we can claim about range.
- **MUC `InventoryBlock` does not do what the code appears to assume** (see §7 W1): our `to = (-1,-1)`
  calls never create a block, and blocks have no timeout. Any design that infers "transfer in flight" from
  it on the Organize/Pull path is wrong. Worth a targeted in-game check early — it also affects whether the
  existing `IsSlotBlocked` guards in `Organizer`/`Puller` are doing anything at all.
- **Item-loss window on the `AddItemToChest` path.** It removes from the source locally *before* the RPC,
  so if the target peer disconnects or the target zone unloads, no response arrives and the item is gone.
  (`RemoveItemFromChest` degrades to a no-op instead.) One press exposes seconds; any continuous mode
  exposes every zone-unload boundary. Mitigate before shipping tidy mode (v2 plan §15.7).
- **Sorter Chest icon asset** is the only non-code artifact; flag if asset tooling is unavailable.
- **Gamepad** strictly depends on the buttons existing → Wave 2, not parallel.
- **2.0 = coordinated release** (major bump + custom prefab); plan the group + manual server deploy.
- Removing the mod after players build Sorter Chests orphans those pieces — document for users.
- **"Keep tidying" mode: DEFERRED TO 2.1** (owner decision). It is the only feature that would move items
  with no player input, it needs five separate guards to be safe (v2 plan §15.5/§15.7/§15.9), and it is
  client-side only (`PlayerCanAccess` → `Player.m_localPlayer`, null on a dedicated server) — there is no
  headless server-side tidying, ever. Nothing about deferring it costs rework: it reuses the same census,
  allocator and execution primitive.
- **Scale is a stated assumption, not a measurement.** Design target is 400+ chests per base. If the
  pre-flight (§9) shows real bases are an order of magnitude smaller, revisit — several §15.2 fixes and the
  whole per-run move-cap discussion get much less urgent.

## 9. Pre-flight checklist (owner verifies manually — fill in the answers here)
These are Unity-serialized or third-party runtime facts. **Reading the C# field initialisers out of a DLL
is not authoritative for items 1–2** — those fields are set on the prefab in the Unity scene. Items 3–6 are
decompiler questions and can be answered from `Managed/assembly_valheim.dll` + `libs/` in dnSpy/ILSpy.

| # | Question | How to check | For | Answer |
|---|---|---|---|---|
| 1 | `ZoneSystem.instance.m_zoneSize`, `m_activeArea`, `m_activeDistantArea` | log at runtime in a world | radius | |
| 2 | `ZNetView.m_distant` on `piece_chest`, `piece_chest_wood`, `piece_chest_blackmetal` | inspect prefab at runtime | radius | |
| 3 | Does a `to = (-1,-1)` call create an `InventoryBlock`? | read `MultiUserChest.dll` `InventoryBlock.CanBlockSlot`; confirm in-game with a log | W1 §7 | |
| 4 | `InventoryGui.m_selectedRecipe` — exact type + members (`.Recipe`/`.ItemData`/`.CanCraft`) | decompile `assembly_valheim.dll` | W2 | |
| 5 | Does MUC accept a non-container ZDOID as a destination (chest→player)? | read `ContainerHandler`/`InventoryHandler` | W2 | |
| 6 | `UIGamePad` field layout (hint text, key binding) | decompile | W4 | |
| 7 | Prefab names: `piece_chest_blackmetal`, `piece_chest_private` (only `piece_chest` is confirmed) | prefab list at runtime | W3 | |
| 8 | Stopwatch `Organizer.BuildPlan` on the biggest available base + chest count | temporary log line in 1.1.1 | W1 §15.2 | |

**#1 and #2 are the only ones that change user-facing behaviour and Thunderstore copy** — the radius is
either 64 m or 128 m and we are currently shipping 128. Do those first. #8 is worth doing even if the
answer is "we don't have a base that big yet": record the number of chests, so the 400+ design target
stays an explicit assumption rather than a forgotten one.

## 10. Next actions
1. **Land Wave 0 on `dev` — now recommended, not optional** (§5): init stubs, `[Organize]` config
   relocation, two `SorterZdo` stubs, three append-only accessors, csproj check.
2. Fill in the §9 pre-flight table (owner, manual).
3. Generate the three Wave-1 handoff prompts (W1 has a plan; write W2 Gather + W3 Sorter-Chest plans),
   plus the W4 Gamepad prompt — each self-contained, per §6. **W1's prompt must state explicitly** that
   §11 of the v2 allocation plan is superseded (no version bump, no changelog, no `./build.sh`).
4. Launch W1/W2/W3 agents on their branches; integrate per §5; then W4; then release-prep to 2.0.0.
