# ChestButler 1.1.0 — "Organize Base" implementation plan

Status: ready to build. Read `docs/organize-base-feature.md` too (original design rationale).
This plan is written for a fresh agent to execute autonomously. Every decision below is
already made — do not re-open them.

## 1. What we're building

A chest that has **Sorter** enabled gains an **Organize** button in its chest UI. One press
sweeps every accessible chest within the sorter radius and consolidates each item type into
its best home — in place. The player carries nothing.

Decisions locked with the owner (do NOT re-litigate):
- **Radius:** reuse the existing sorter radius (`Plugin.SorterRadius`, 20 m default). No new setting.
- **Safety:** preview-then-confirm. First press builds the plan and shows a summary
  ("Organize: move N items across M chests — press again to confirm"). A second press within
  ~5 s executes. Any other action, opening a different chest, or the timeout cancels it.
- **Routing:** station-adjacency + most-held, using a curated, server-synced station→group map
  (editable in config). This is the full feature, not the most-held-only fallback.
- **Delivery:** build it, install the DLL into the local Thunderstore test profile, and commit
  to the **dev** branch. DO NOT push prod, publish to Thunderstore, or touch the live server.

## 2. Behavior spec

Trigger: an `Organize` button, visible only when `SorterZdo.IsSorter(chest)` is true (i.e. add
it in the `isSorter` branch of `GuiPatch.Refresh()`, the same gate the Sorter toggle uses).

Per press:
1. Build a plan over all accessible player-built chests within `Plugin.SorterRadius` of the
   origin chest, using `ContainerTracker.Candidates(origin, radius, excludeSorters:false)`
   (already filters by distance, ward access, container access, player-built) plus the origin.
2. Show a preview summary via `Player.m_localPlayer.Message(MessageHud.MessageType.Center, ...)`
   and stash {plan, time, originContainer}.
3. On a second press on the same chest within the confirm window, execute the plan in budgeted
   batches across frames; print a final summary ("Organized N items into M chests").

Per-item-type routing priority (highest wins):
1. A chest that **pins / sign-filters** the item (`FilterSpec.MatchesItem` / `MatchesGroup`).
2. A chest whose **adjacent crafting station** attracts the item's group (station adjacency).
3. The chest that already **holds the most** of that item type (consolidation).
4. None qualify → leave the item where it is.
Ties broken by distance (nearest wins; `Candidates` already returns nearest-first).

Rules:
- **Stackables consolidate; non-stackables (m_maxStackSize <= 1: tools, armor) stay put** unless
  a chest explicitly pins them — identical to the sorter's existing rule.
- A chest with `FilterSpec.Ignore` or `FilterSpec.ManualOnly` is never a target (matches sorter).
- Never exceed a target's capacity — use `Router.Room`; overflow stays in its source chest.
- A chest that is itself a **Sorter** is never a target (would just re-sort out); it can be a source.
- Skip items already in flight — `InventoryBlock.Get(inv).IsSlotBlocked(item.m_gridPos)`.
- Every move goes through MultiUserChest — NEVER write to a chest inventory directly.

## 3. The planning algorithm (deterministic — no ping-pong)

Do NOT reuse the per-item sorter loop (it re-evaluates each tick and could bounce items).
Compute ONE deterministic plan: choose a single winning destination per item type, then move
all other instances of that type into it.

    BuildPlan(origin, radius):
      chests = Candidates(origin, radius, excludeSorters:false) + origin
      byType = {}                       # normType -> list of (chest, ItemData)
      for chest in chests:
        for item in chest.Inventory.GetAllItems():
          if blocked-in-flight: continue
          norm = Names.Normalize(item.m_shared.m_name)
          if item.m_shared.m_maxStackSize <= 1 and not anyChestPins(norm): continue  # leave gear
          byType[norm].add(chest, item)
      plan = []; remainingRoom = {}      # target -> running free room for this type
      for norm, holders in byType:
        target = ChooseTarget(norm, holders, chests)   # tiers below; null => leave
        if target == null: continue
        room = Router.Room(target.Inventory, sampleItemOf(norm))
        for (chest, item) in holders:
          if chest == target: continue
          amount = min(item.m_stack, room)
          if amount <= 0: break
          plan.add(Move(source=chest, item, amount, target)); room -= amount
      return plan (+ counts: total items, distinct targets, source chests) for the preview

    ChooseTarget(norm, holders, chests):
      targets = [c in chests where !IsSorter(c) and !spec(c).Ignore and !spec(c).ManualOnly]
      # tier 4 — explicit pin/filter
      t = targets where spec(c).MatchesItem(norm) or spec(c).MatchesGroup(norm)
      if t: return most-held-of-norm, then nearest
      # tier 3 — station adjacency
      t = targets where Stations.GroupsForChest(c) has a group G with Groups.GroupContains(G, norm)
      if t: return most-held-of-norm, then nearest
      # tier 2 — consolidation
      t = targets that already hold norm (Inventory.CountItems > 0)
      if t: return the one holding the most (then nearest)
      return null   # nothing wants it -> leave in place

`anyChestPins(norm)`: true if some in-range chest's FilterSpec matches norm (so pinned gear is
allowed to move to that chest, but un-pinned gear never wanders).

## 4. Station detection

`CraftingStation.GetCraftingStation(Vector3 point)` is present in assembly_valheim (verified).
It returns the crafting station within build range of a point, or null.
Per chest: `var st = CraftingStation.GetCraftingStation(chest.transform.position);`
`string key = st != null ? st.m_name : null;`

Curated default map — server-synced config section `[Stations]`, same pattern as `[ItemGroups]`.
Keys are the station's `m_name` token. VERIFY the exact strings at runtime (log detected m_name
values while testing and correct the map). Starting map:

| Station (m_name — verify) | Groups            |
|---------------------------|-------------------|
| $piece_forge              | metals, ores      |
| $piece_workbench          | wood, hides       |
| $piece_stonecutter        | stone             |
| $piece_cauldron           | cooking, meat, seeds |
| $piece_fermenter          | meads             |
| $piece_blackforge         | metals, valuables |
| $piece_magetable          | valuables, meads  |

CAVEAT: smelters, kilns, blast furnaces, and windmills are `Smelter`/`Windmill`, NOT
`CraftingStation`, so `GetCraftingStation` won't detect them. Station adjacency covers only true
crafting stations. Say so in a code comment and the changelog; don't promise furnace detection.

## 5. Files to create / modify

CREATE `src/ChestButler/Core/Stations.cs`
- Mirror `Groups.cs`: `Init(ConfigFile)` binds `[Stations]` entries (IsAdminOnly=true so Jötunn
  server-syncs them), `config.SettingChanged += Rebuild`, parse CSV → group-name lists.
- `List<string> GroupsForStationName(string mName)`.
- `List<string> GroupsForChest(Container c)` → GetCraftingStation(c.transform.position), read m_name, look up.
- Values are comma-separated **group names** that must exist in `Groups`.

CREATE `src/ChestButler/Core/Organizer.cs`
- PURE planner separated from Unity for testing (§8): operate over a small POD so it can be
  unit-tested without the game, e.g.:
    struct ChestView { int id; List<string> stationGroups; FilterView filter;
                       List<StackView> stacks; }
    struct StackView { string norm; int count; bool stackable; }
  `OrganizePlanner.Plan(IReadOnlyList<ChestView>) -> List<Move{srcId,norm,amount,tgtId}> + Summary`
  implementing §3 exactly (tiers, most-held, capacity, no self-move, exclusions).
- Unity adapter `Organizer.BuildPlan(Container origin, float radius)`: gather live containers into
  ChestViews (norm via Names.Normalize, stationGroups via Stations.GroupsForChest, filter via
  Filters.GetSpec, room via Router.Room), call the pure planner, map moves back to
  (Container source, ItemData, amount, Container target). Return plan + summary.
- `Organizer.Execute(Plan plan)`: **model this on `Puller`, not SorterBehaviour.** Organize is a
  client-triggered action (like Pull), so it is NOT owner-gated; MUC routes each move to the owner.
  For each move call exactly Puller's primitive:
    ContainerHandler.RemoveItemFromChest(source, item, targetInv, new Vector2i(-1,-1),
                                         targetNv.GetZDO().m_uid, amount, null);
  Drive it as a coroutine on `Plugin.Instance` that issues `Plugin.OrganizeMovesPerTick` moves per
  frame (yield return null), re-checking InventoryBlock each move, then prints the summary.

MODIFY `src/ChestButler/Plugin.cs`
- Add `internal static Plugin Instance;` set to `this` in Awake (needed for StartCoroutine).
- Call `Stations.Init(Config);` right after `Groups.Init(Config);`.
- Add config `OrganizeMovesPerTick` (int, default 4, range 1–16, IsAdminOnly, section "Organize").

MODIFY `src/ChestButler/Patches/GuiPatch.cs`
- Add a 5th button `psort_organize` to the bar via the existing `MakeButton` path (same vanilla
  styling). Show it ONLY when `SorterZdo.IsSorter(_current)` — put its SetActive in the `isSorter`
  branch of `Refresh()`; hide the pin/clear/pull group when it's a sorter (they already hide).
- Preview-then-confirm using static `_pendingPlan`, `_pendingChest`, `_pendingAt`:
  first click → `Organizer.BuildPlan`; if empty → "Nothing to organize"; else stash + show
  "Organize: move N items across M chests — press again to confirm". Second click on the same
  chest within 5 s → `Organizer.Execute` then clear pending. Clear pending in `Hide()` and when a
  different container opens.
- WARNING: this file was truncated twice by mid-file edits on the synced mount. Edit surgically OR
  rewrite the whole file with a bash heredoc, then verify `wc -l` and that it still closes with `}`.

MODIFY `pkg/CHANGELOG.md` — add a `## 1.1.0` entry describing Organize + station adjacency + the
smelter caveat.

BUMP version to `1.1.0` in ALL THREE: `src/ChestButler/ChestButler.csproj` (<Version>),
`src/ChestButler/Plugin.cs` (ModVersion), `pkg/manifest.json` (version_number). build.sh's guard
fails the build if they drift.

Do NOT modify `Router.cs`'s `FindTarget` — the live sorter path must keep working. Reusing
`Router.Room` is fine.

## 6. Config summary
- `[Stations]` — one entry per station, server-synced (IsAdminOnly). Default map §4.
- `[Organize] MovesPerTick = 4`.
- Radius: none (reuse `Plugin.SorterRadius`).

## 7. Reuse these existing pieces (don't reinvent)
- `ContainerTracker.Candidates(origin, radius, excludeSorters:false)` — in-range accessible chests, nearest-first.
- `Router.Room(inv, item)` — capacity (partial stacks + empty slots).
- `Filters.GetSpec(chest)` — pins + sign filters (Ignore / ManualOnly / Priority / MatchesItem / MatchesGroup).
- `Groups.GroupContains(group, norm)` / `Groups.IsGroup` — group membership.
- `Names.Normalize` / `Names.Matches` — name tokens.
- `SorterZdo.IsSorter / HasValidNView / NView / PlayerCanAccess`.
- `ContainerHandler.RemoveItemFromChest(...)` — the MUC move (copy Puller's call exactly).
- `InventoryBlock.Get(inv).IsSlotBlocked(item.m_gridPos)` — skip in-flight items.

## 8. Testing (the agent cannot play the game)
- Put the ranking/consolidation logic in the PURE `OrganizePlanner` over POD inputs and write a
  test that runs WITHOUT Unity: a throwaway `dotnet` console under `/tmp` or a `tests/` xUnit
  project referencing only the planner (copy the file or use InternalsVisibleTo). Cover:
  most-held wins ties; pins beat station beats most-held; non-stackables excluded unless pinned;
  capacity/overflow respected; no self-moves; Ignore/ManualOnly/Sorter chests excluded as targets;
  empty input → empty plan.
- The Unity glue (Container/Inventory/CraftingStation/MUC) can't be unit-tested offline; keep it
  thin and obviously mirror SorterBehaviour/Puller, which already work in-game.
- Build clean with `./build.sh` (already --no-incremental). Confirm the DLL md5 changed and
  `strings -el dist/ChestButler.dll | grep -c Organize` > 0.

## 9. Build & install + MOUNT GOTCHAS (these have bitten us — heed them)
- Repo: host `C:\Users\Light\projects\Valheim mod` ↔ your session's sandbox mount (see your Shell
  access section for the exact `/sessions/<id>/mnt/Valheim mod/` path — it differs per session).
- WRITE .cs FILES VIA `cat > file <<'EOF' … EOF` IN BASH, not the Edit/Write tools. The synced
  mount truncated files mid-edit twice. After every write: `wc -l file && tail -1 file`.
- Build offline: `./build.sh`. It passes `--no-incremental` because the mount's timestamps make
  incremental builds skip recompilation → stale DLLs. Always md5-diff the DLL to confirm a rebuild.
- Install target for testing = the profile MANAGER folder (build.sh already copies here):
  `<mount>/profiles/Default/BepInEx/plugins/EK_Solutions-ChestButler/ChestButler.dll`.
  Never leave a second loose DLL in `plugins/` — duplicate GUID conflict.
- The Thunderstore Mod Manager's version LABEL is cosmetic (its own state, not the DLL). Verify by
  md5/strings, not the label.
- Overnight the game is closed, so the DLL won't be locked. If a copy fails with EPERM, the game
  or manager is holding it — stop and note it.

## 10. Versioning & release (READ — handshake implications)
- Version = 1.1.0. This is a MINOR bump; `[NetworkCompatibility(..., VersionStrictness.Minor)]`
  means a 1.0.x client and a 1.1.0 server refuse each other. So this is a COORDINATED release.
- The server auto-updater is PATCH_ONLY and will NOT auto-deploy a minor bump — by design. Leave it.
- Because of the handshake, TEST IN SINGLE-PLAYER / a local world (no server). Do not try a 1.1.0
  client against the live 1.0.2 server; it will be refused at connect.
- Commit to **dev** only. Do not push prod, do not publish Thunderstore, do not run the server updater.

## 11. Definition of done (what the owner wakes up to)
- [ ] Stations.cs + Organizer.cs created; Plugin.cs, GuiPatch.cs, CHANGELOG, and the 3 version
      declarations updated to 1.1.0.
- [ ] `./build.sh` clean (0 errors), DLL md5 changed, "Organize" present in DLL strings.
- [ ] Pure planner unit test written and passing (paste the test output into the commit body).
- [ ] DLL installed into the Default test profile's manager folder.
- [ ] Committed to dev (NOT prod). Working tree clean. Nothing published to Thunderstore.
- [ ] A short single-player TEST SCRIPT appended to this file (below) and to the commit body:
      new local world → build 3–4 chests near a forge and a cauldron, drop mixed loot into them →
      mark one chest a Sorter → press Organize → read the preview → press again → verify metals/ores
      pool by the forge, cooking/meat/seeds by the cauldron, everything else consolidates by
      most-held, tools/armor stay put, and NOTHING is lost or duplicated.

## 12. Risks / edge cases to handle
- Empty base / nothing to move → "Nothing to organize", no-op (don't enter confirm state).
- Item with no home and no station match → left in place (not an error).
- Tie on most-held → nearest wins (deterministic).
- Target fills mid-execution (another player) → the MUC move returns short / InventoryBlock guards;
  overflow stays put. Re-check room/block each move in the coroutine.
- Non-stackables only move when pinned; never auto-consolidate gear.
- Large base → the per-frame `OrganizeMovesPerTick` budget prevents frame hitches.
- Confirm window: if the second press lands on a different chest or after timeout, treat it as a
  fresh first press (rebuild preview), don't execute a stale plan.

---

## 13. Single-player test script (v1.1.0) — run this before shipping

Because a 1.1.0 client is refused by the live 1.0.2 server (minor version handshake), test in a
**local/single-player world**, not against the server.

1. **New local world.** Launch Valheim through the Thunderstore "Default" profile (the one this DLL
   is installed into) → Start Game → **Start** a fresh local world (or any dev world). Enter it.
2. **Enable devcommands** (fastest way to get loot/stations): press F5, type `devcommands`, then
   `god`, `debugmode`. Fly (`Z`) and `spawn` what you need.
3. **Build stations + chests.** Place a **Forge** and a **Cauldron** a few metres apart. Next to the
   forge place 2 chests; next to the cauldron place 2 chests. (`spawn Chest 1`, or build from the
   hammer.) All chests must be inside the wards/your build area so you own them.
4. **Drop mixed loot into the chests, deliberately scrambled** so nothing is already sorted:
   - metals/ores near the **cauldron** (e.g. `spawn Bronze 20`, `spawn Iron 15`, `spawn CopperOre 30`)
   - cooking/meat near the **forge** (e.g. `spawn Carrot 20`, `spawn Mushroom 15`, `spawn NeckTail 10`)
   - a pile of `Wood` split unevenly across two chests (say 40 in one, 12 in another)
   - one or two **tools/armor** pieces in a chest (e.g. `spawn BronzeSword 1`, `spawn ArmorBronzeChest 1`)
5. **Make ONE chest a Sorter.** Open any chest → click **Sorter: OFF** so it reads **Sorter: ON**.
   The pin/clear/pull buttons disappear and a single **Organize** button appears.
6. **First press = preview.** Click **Organize**. A centre message reads
   *"Organize: move N items across M chests — press again to confirm"*. Nothing has moved yet.
7. **Second press = execute** (within 5 s, same chest still open). Click **Organize** again. A centre
   message reads *"Organized N items into M chests"*.
8. **Verify the result** (open the chests):
   - **Metals + ores** pooled into a chest **next to the forge** (not where they started).
   - **Cooking + meat + seeds** pooled into a chest **next to the cauldron**.
   - **Wood** consolidated into the chest that already held the most of it (the 40-pile chest).
   - **Tools/armor stayed put** (BronzeSword / armor did NOT move) — unless you pinned them first.
   - **Item totals are unchanged** — nothing lost, nothing duplicated. (Count before/after.)
9. **VERIFY STATION TOKENS.** Open `BepInEx/LogOutput.log` (in the profile folder) and look for
   `[organize] chest near station '<token>' -> ...` lines. Confirm the tokens match the `[Stations]`
   config keys ($piece_forge, $piece_cauldron, $piece_workbench, $piece_stonecutter, $piece_blackforge,
   $piece_magetable). If any differ, edit `eksolutions.chestbutler.cfg` → `[Stations]` to match and
   re-test. (Smelters/kilns/blast furnaces/windmills/fermenters are NOT crafting stations and will
   not appear — this is expected; route their materials with pins if desired.)
10. **Edge checks:** press Organize in an already-tidy base → *"Nothing to organize"* (no confirm
    state). Press once, wait 6 s, press again → it re-previews instead of executing (stale plan
    expired). Close the chest between presses → the pending plan is cancelled.
