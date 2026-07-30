# W2 — Gather: plan

Written at the start of W2's turn, after verifying every API against `Managed/assembly_valheim.dll`
and `libs/MultiUserChest.dll` (roadmap §9d). Branch `feat/gather` off `dev` (354defd, W1 merged).

## 1. What it does

A **Gather** button beside `m_craftButton` in InventoryGui's crafting panel, on every crafting
station, for both the Craft and Upgrade tabs. Beside each ingredient, "(N in storage)" = how many are
reachable in nearby chests. Pressing Gather pulls the shortfall for the craft amount currently shown
into the player's inventory over the MultiUserChest path.

## 2. API verification — four corrections to the roadmap's §7 W2 notes

The audit's corrections were mostly right, but three of its specifics are wrong and one open question
is answered:

1. **`SetupRequirement` takes SIX parameters, not five.** Roadmap §7 W2 records it as
   "`SetupRequirement(Transform, Piece.Requirement, Player, bool, int) → bool`". The real signature is
   `public static bool SetupRequirement(Transform elementRoot, Piece.Requirement req, Player player,
   bool craft, int quality, int craftMultiplier)`. Harmony injects postfix arguments **by name**, so a
   patch written against the five-parameter form would silently never bind.
2. **`InventoryGui.RecipeDataPair` is `NestedPrivate`.** The audit says to use
   `m_selectedRecipe.Recipe.m_resources`, which is right about the shape but not writable in C#: the
   type cannot be named in source, and the field is private too. It needs `AccessTools` reflection
   against a boxed struct.
   **Consequence — a better design.** Because `SetupRequirement` hands us the `Requirement`, the
   `quality` and the `craftMultiplier` for each ingredient the panel is actually displaying, Gather
   does not need the selected recipe at all. Reading the UI callback is more accurate than
   re-deriving the same values (it is correct on both tabs by construction) and touches no private
   nested type. The one thing it does not carry is `m_requireOnlyOneIngredient`, so that single flag
   is read reflectively, once per panel refresh, behind a guarded static initialiser.
3. **ROADMAP §9 ITEM 5 IS ANSWERED: chest→player works, and no `Puller` helper is needed.** The
   audit worried that "the MUC primitive takes a destination ZDOID and the player inventory has no
   container ZDO". The parameter is not a destination. The real signature is
   `RemoveItemFromChest(Container container, ItemDrop.ItemData item, Inventory destinationInventory,
   Vector2i to, ZDOID sender, int dragAmount, ItemDrop.ItemData switchItem)` — the destination is
   passed as a live `Inventory`, and the ZDOID is the **sender**, i.e. who the RPC response is routed
   back to. `Player.m_localPlayer.GetZDOID()` (via `Character.GetZDOID()`, public) is a perfectly
   valid sender. MUC also ships `HumanoidInventoryOwner` alongside `ContainerInventoryOwner` and
   patches `Humanoid`, so player inventories are a first-class endpoint in its model.
   **`Core/Puller.cs` therefore stays out of W2's TOUCHES.**
4. **`Requirement.GetAmount(int)`** — confirmed, one int (quality). Note `Recipe.GetAmount` is a
   different four-parameter method and is not the one to call.

**Fails safe.** `RemoveItemFromChest` removes nothing locally and applies on the RPC response
(v2 plan §15.7 — this is the asymmetry that makes it *safer* than `AddItemToChest`). So if the
chest→player response path does not behave as read, the failure mode is "nothing arrives", not
"items vanish". Worth confirming in-game, but there is no loss risk in trying.

## 3. Files

| File | Ownership | What |
|---|---|---|
| `Patches/GatherPatch.cs` | new, W2 | The button, the "(N in storage)" annotations, the per-refresh requirement capture. |
| `Core/Gatherer.cs` | new, W2 | Storage counting, shortfall maths, the MUC pull. No Unity UI. |
| `Core/Gather.cs` | W2 (Wave 0 stub) | `[Gather]` config binds. |
| `Core/ContainerTracker.cs` | **footprint extension — flagged** | One append-only accessor, `AccessibleNear(Vector3, float)`. See §5. |

`Plugin.cs` is **not** touched: Wave 0's `Gather.Init(Config)` line already exists.

## 4. Behaviour

- **Counting.** For each displayed requirement, `needed = req.GetAmount(quality) * craftMultiplier`,
  `have = playerInventory.CountItems(name, -1, true)`, `shortfall = max(0, needed - have)`, and
  `inStorage` = the same count summed over accessible chests in `Plugin.SorterRadius`.
- **Annotation.** Appended to the requirement element's `res_amount` `TMP_Text` child (the
  MyLittleUI pattern the audit confirmed), found by name with a `GetComponentsInChildren` fallback.
- **Pulling.** Richest chest first, capped by the player's actual room. Uses the same per-run
  `promised` ledger W1 needed: `Router.Room` reads a local inventory that does not reflect in-flight
  adds, so without debiting at issue time N pulls into a nearly-full inventory all pass the room
  check (v2 plan §16.2.3 — the identical bug, in a new place).
- **`sort: off` is respected.** An `Ignore` chest is not read by Gather either. W1 made "leave this
  chest entirely alone" the contract for that label, and a Gather button that quietly loots a
  personal stash would reintroduce §16.4.5 through the back door. `ManualOnly` chests **are** read:
  Manual means "never auto-*filled*", and Gather is an explicit click, exactly like Pull.
- **`m_requireOnlyOneIngredient`.** Gather pulls for only ONE ingredient on those recipes — the one
  storage can most nearly satisfy — instead of over-pulling every listed option.
- **Wards and access** are checked per chest, through the same `ContainerTracker` filters.

## 5. Footprint extension, flagged

Roadmap §3 lists `ContainerTracker` as reads-only for W2. But every existing query is centred on a
**Container** (`Candidates(Container sorter, …)` measures from `sorter.transform.position` and
excludes `sorter` from the results), and Gather's origin is the **player**. Passing the nearest chest
as a stand-in would centre the radius on the wrong point and then exclude that chest from its own
results.

So `ContainerTracker` gains one append-only accessor, `AccessibleNear(Vector3 point, float radius)`,
and `Candidates` is re-expressed as a call into the same private core so the two can never disagree
about which chests are accessible or how ties break. That refactor is deliberate rather than
duplicating the filter chain: `Candidates` is on the sorter tick path and is load-bearing for W1, and
two copies of that logic drifting apart is a worse outcome than one shared core. `Candidates`'s
observable behaviour is unchanged — same filters in the same order, same `(distance, uid)` comparator.

## 6. Config (`[Gather]`)

- `Enabled` (bool, true) — client-side; it is a UI affordance and changes no shared outcome.
- `ShowStorageCounts` (bool, true) — client-side; the per-ingredient annotation.
- Radius reuses `Plugin.SorterRadius` per the Wave 0 stub's instruction. No second radius knob.

## 7. Testing

The offline suite cannot reach any of this — it is Unity UI plus the MUC seam. What it *can* cover is
the shortfall arithmetic, so `Gatherer`'s maths is written as a pure static over POD and tested:
multiplier handling, quality handling, already-have-enough, partial storage, and the
`requireOnlyOneIngredient` branch. Everything else is in the in-game script at the end of this doc.
