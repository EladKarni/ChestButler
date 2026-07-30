# W3 — Dedicated Sorter Chest: plan

Written at the start of W3's turn, after verifying the Jötunn and game APIs against `libs/Jotunn.dll`
and `Managed/assembly_valheim.dll` (roadmap §9d). Branch `feat/sorter-chest` off `dev` (0008732).

## 1. What it is

A craftable chest that is a Sorter out of the box: a Jötunn `CustomPiece` cloned from the vanilla
**reinforced** chest (`piece_chest` — `piece_chest_wood` is the *basic* one), buildable at a workbench
for 10 Wood + 5 BronzeNails, filed under Furniture in the hammer menu.

Nothing else in the mod needs to know it exists. It is still a `Container` with a `Piece`, so
`ContainerPatches` registers it with `ContainerTracker` and attaches a `SorterBehaviour`
automatically, and the existing chest toolbar works on it unchanged.

## 2. API verification

All confirmed present with these exact shapes:

- `new CustomPiece(string name, string baseName, PieceConfig)` — the clone-by-name constructor.
- `PieceManager.Instance.AddPiece(CustomPiece) → bool`.
- `PrefabManager.OnVanillaPrefabsAvailable` add/remove — vanilla prefabs are not loaded during
  `Plugin.Awake`, so the clone waits on this and unsubscribes immediately (the event can fire again on
  a world change, and adding the same piece name twice makes Jötunn log an error and drop the second).
- `PieceConfig` — settable `Name`, `Description`, `PieceTable`, `Category`, `CraftingStation`, `Icon`,
  plus `AddRequirement(RequirementConfig)`; `RequirementConfig(string item, int amount,
  int amountPerLevel, bool recover)`.
- `Piece.PieceCategory.Furniture` exists (= 4); `PieceConfig.Category` is a *string*, so "Furniture".
- `ZDO.GetPrefab() → int` for identifying the piece at runtime.

**One csproj change was needed and is not in the roadmap's list.** `CustomPiece` has
`AssetBundle`-based constructor overloads, so the compiler needs `UnityEngine.AssetBundleModule`
referenced to resolve the overload we actually call — otherwise `error CS0012`. Reference only; no
asset bundle ships. Roadmap §4 already anticipated W3 touching the csproj (for an icon resource), so
this stays inside the declared footprint, just for a different reason.

## 3. The two-key spawn default

`SorterZdo.IsSorter` reads a bool that defaults to false, so it cannot tell "never set" from "the
player switched it off". Writing the flag on `Awake` would therefore re-enable Sorter on **every zone
reload** and quietly overrule the player. Wave 0 already stubbed the right shape:
`SetSorterDefault` guards on a separate `WasDefaulted` marker and fires at most once per chest. W3
just calls it — no edit to `SorterZdo.cs` was needed at all.

**Additionally gated on ZDO ownership.** The client that places a piece owns its fresh ZDO, so
`nv.IsOwner()` is true exactly where the decision belongs. Without that gate, a peer's chest loading
into our zone before its flag had synced would have us claim their ZDO just to write a default —
ownership churn for nothing. The patch lives in `SorterChestPiece.cs` as a nested `[HarmonyPatch]`
class, so `ContainerPatches.cs` stays reads-only for W3 as §3 requires (two patch classes can both
postfix `Container.Awake`).

## 4. The W1 → W3 dependency: RESOLVED BY DECISION, not by code

Roadmap §3 says "W1 must treat default-sorter chests as claimable/free targets", because `Organizer`
excludes sorters as Organize targets and a base built out of Sorter Chests would be invisible to the
allocator. Having now built both halves, **the right answer is to leave the exclusion exactly as it
is**, and this is the reasoning rather than an oversight:

- A Sorter chest's entire job is to *push its contents out* on the sorter tick. Making it an Organize
  *target* means Organize fills it and the tick immediately empties it again — the same class of
  livelock as v2 plan §15.8, which W1 spent a fix on eliminating. Building it in deliberately would be
  a regression.
- Sorter chests are already visible as **sources**: `Organizer.BuildPlan` calls
  `ContainerTracker.CandidatesWithDistance(..., excludeSorters: false)`, so their contents are
  censused and redistributed. Draining them is the feature.
- The failure the roadmap feared — a base of *nothing but* Sorter Chests — is a base with nowhere for
  anything to go, and W1 already reports exactly that: "N items had no room - add more chests", in the
  preview, before the player confirms.

So: no code change, and the interface item is closed. Worth testing jointly anyway (§7 test D).

## 5. The icon — deliberately inherited, and why that is fine

Roadmap §8 flags the icon as "the only non-code artifact; flag if asset tooling is unavailable". It is
unavailable here, and it turns out not to matter: cloning `piece_chest` copies its `Piece` component,
`m_icon` included, so the piece shows the vanilla reinforced-chest icon in the build menu and is
perfectly legible. `PieceConfig.Icon` is left unset for that reason.

The cost is that Sorter Chest and reinforced chest look identical in the hammer menu — they are
distinguishable by name and by the recipe, but not at a glance. That is a polish item for release-prep
if someone wants to draw one, not a blocker, and the `<EmbeddedResource Include="Assets/**" />` glob
Wave 0 pre-added is already in place to receive it.

## 6. Deployment consequences (unchanged, restating for release-prep)

- A custom prefab must exist on the **server and every client**. This piece is the single reason 2.0
  cannot be a patch release.
- The `PATCH_ONLY` server updater will skip 2.0.0; deploy by hand, then set `.chestbutler_deployed`
  to `v2.0.0` so 2.0.x patches auto-flow again.
- Removing the mod after players have built these orphans the placed pieces. Jötunn softens it, but it
  needs saying in the Thunderstore copy.

## 7. In-game test script

**A. It registers.** Load a world and check the log for `[sorterchest] registered
'ChestButler_SorterChest' (cloned from piece_chest)`. If Jötunn refused it, the warning says so and
nothing else in the mod is affected.

**B. It builds and is a Sorter immediately.** Build one at a workbench (10 Wood, 5 BronzeNails).
Open it — the toolbar should already read **Sorter: ON** with no interaction, and the log should show
`applied the Sorter default`. Confirm it is a reinforced chest's size, not the small chest's.

**C. The default does not fight the player — the important one.** Switch Sorter **OFF** on a placed
Sorter Chest. Walk far enough away to unload the zone, come back. It must still be **OFF**. This is
the failure the `WasDefaulted` marker exists to prevent, and it only shows up across a zone reload.

**D. Joint test with W1.** Put a Sorter Chest and several plain chests in one base with loose loot.
Confirm the Sorter Chest's contents distribute on the tick, and that pressing Organize on it censuses
the base and never routes items *into* a sorter chest (see §4).

**E. Multiplayer parity.** With a second client, confirm the piece renders and is usable for both,
and that a chest placed by one player is not re-defaulted on the other's client (the ownership gate).

**F. Cleanup behaviour.** Destroy one with a hammer and confirm the contents drop as normal and no
errors appear — it is a vanilla chest underneath, so this should be uneventful.
