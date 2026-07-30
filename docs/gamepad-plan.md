# W4 — Gamepad support: plan

Written at the start of W4's turn, after verifying the input and UI APIs against
`Managed/assembly_valheim.dll`, `assembly_guiutils.dll` and `assembly_utils.dll` (roadmap §9d).
Branch `feat/gamepad` off `dev` (c272f5a, W1+W2+W3 merged).

## 1. The roadmap's approach would have made things worse

Roadmap §7 W4 says: "The current `MakeButton` in `GuiPatch.cs` strips `UIGamePad` from cloned buttons.
Re-enable controller support: register the toolbar buttons (and the Gather button) with InventoryGui's
gamepad focus/nav so they're reachable on a controller, and add key hints."

Reading the actual types says the first half of that is a trap and the second half is unnecessary:

**`UIGamePad` is not a navigation component.** Its fields are `m_keyCode`, `m_zinputKey` (string),
`m_hint` (GameObject) and `m_blockingElements`, with a private `m_button`/`m_toggle`/`m_group` resolved
in `Start()` and an `Update()` that fires the button when the key is pressed. It binds **one gamepad
button directly to one UI Button** — it is a shortcut, not focus handling.

That is exactly why `MakeButton` strips it, and the strip is correct: a clone that keeps its
`UIGamePad` responds to the *same* gamepad button as the vanilla button it was cloned from. Our five
toolbar buttons are all cloned from Take All, so "re-enabling `UIGamePad`" as literally described would
give five buttons plus Take All the same binding and fire several at once.

Assigning *fresh* keys instead requires knowing which gamepad buttons are still free while each panel
is open. Those bindings are Unity-serialized on the prefabs, so they cannot be read offline — this is
**roadmap §9 item 6**, still unanswered. Building on a guess there would ship a real regression for
controller users, who today simply cannot reach our buttons rather than having a vanilla shortcut
double-fire.

**There is a better route, and the game already uses it.** `UIGroupHandler` (in `assembly_guiutils`,
not `assembly_valheim`) drives focus with plain Unity `Selectable` navigation: it has
`m_defaultElement`, a private `FindSelectable(GameObject root)`, `ResetActiveElement()`,
`HaveSelectedObject()` and a static priority-ordered `m_groups` list. Our buttons are `Button`s,
therefore already `Selectable`s. So they can be linked into D-pad navigation directly.

That resolves the blocked pre-flight item rather than waiting on it: **no free-button hunt, no conflict
with any vanilla shortcut, and no key hints needed** — Unity's own selection highlight already shows
which button has focus, which is what a hint would otherwise have to duplicate. `UIGamePad` stays
stripped, `ZInput` is not touched, and §9 item 6 stops being a blocker for W4 (it stays open only if
someone later wants true button shortcuts as well).

## 2. Explicit links, not `Navigation.Mode.Automatic`

Automatic navigation picks neighbours geometrically. This toolbar **moves**: `PositionBar` re-anchors
it under the item grid every time a differently-sized chest opens, and W1 added a case where it drops
below the panel entirely when the rows reach the corner. Geometric guessing would silently change which
button the D-pad reaches depending on the chest. So every link is explicit.

`Core/GamepadNav.cs` (new, W4-owned) provides:

- `LinkRow(IList<Selectable>)` — chains a row left↔right, **skipping inactive buttons**. Explicit
  navigation into a hidden Selectable dead-ends, and this toolbar hides buttons depending on whether
  the chest is a sorter, has pins, has filters to Pull. So the chain is rebuilt at the end of
  `Refresh()`, every time visibility settles — not once at construction.
- `LinkVertical(above, below, onlyIfVanillaEmpty)` — the `onlyIfVanillaEmpty` guard exists so we never
  *steal* an existing vanilla downward link. Breaking working controller navigation to add ours would
  be a bad trade; if the game already routes down from Take All, we leave it and our buttons stay
  reachable upward only, with a debug line saying so.
- `AttachRowToAnchor(anchor, row)` — links the first live button in the row to a vanilla anchor above.

Wiring: the chest toolbar attaches under **Take All**; the Gather button attaches above **Craft**
(it is positioned directly above it, so that is the natural route).

## 3. Config, and why `Plugin.cs` is untouched

One entry, `[Gamepad] Enabled` (default true, client-side). Wave 0 never stubbed an `Init` for W4, and
roadmap §3 lists W4 as touching only `GuiPatch.cs` and `GatherPatch.cs` — so rather than adding a line
to `Plugin.Awake`, `GamepadNav` binds lazily off `Plugin.Instance.Config` on first access. That keeps
the declared footprint honest and adds no shared-file edit.

## 4. Files

| File | Ownership | What |
|---|---|---|
| `Core/GamepadNav.cs` | new, W4 | The navigation helper + lazy config. |
| `Patches/GuiPatch.cs` | W4 (edit) | `LinkGamepadNav()` called at the end of `Refresh()`. |
| `Patches/GatherPatch.cs` | W4 (edit) | Gather ↔ Craft vertical link in `RefreshButton()`. |

No `Plugin.cs`, no `SorterZdo.cs`, no csproj, no version, no changelog.

## 5. What is unverified — read before trusting this

This is the workstream least verifiable offline, and I want to be blunt about it: **I have not seen a
controller touch this.** What is verified is the API shape (the field layouts above, read out of the
assemblies) and that it compiles. What is not:

- **Whether the container panel's `UIGroupHandler` considers our buttons at all.** Our bar is parented
  to Take All's parent, so it should fall inside the same group hierarchy, but `FindSelectable` is
  private and its exact traversal is unread. If the group never selects anything in our bar, the links
  are inert and nothing happens — a no-op, not a break.
- **Whether Take All already navigates downward.** If it does, the `onlyIfVanillaEmpty` guard leaves it
  alone and our toolbar is reachable only by pressing *up* from... nothing, which in practice may mean
  not reachable. The debug line reports which branch was taken; if it says "left it alone", the fix is
  to pick a different anchor, and that is a one-line change once someone can see it.
- **Whether the Gather button's downward link to Craft is disruptive.** That one is written
  unconditionally (`onlyIfVanillaEmpty: false`) because Gather sits directly above Craft and the
  vanilla panel is unlikely to route up from Craft to anything useful — but "unlikely" is doing work
  in that sentence, and if it turns out Craft already navigated up somewhere, this takes it.

Failure mode throughout is "the buttons still are not reachable on a controller", i.e. exactly today's
behaviour, not a crash and not a change for keyboard users.

## 6. In-game test script (needs a controller)

**A. Baseline.** With a controller connected, open a chest. Confirm the vanilla panel still navigates
exactly as before — that nothing about keyboard/mouse or existing controller behaviour changed.

**B. Reachability.** From Take All, press D-pad **down**. Focus should land on the first ChestButler
toolbar button. Left/right should walk the row, skipping buttons that are hidden for that chest (open a
sorter chest and a plain chest to see two different rows).

**C. Activation.** With a toolbar button focused, the submit button should fire it — Sorter toggles,
Pin pins, Organize previews then confirms.

**D. Gather.** At a crafting station with a recipe selected, press **up** from the Craft button; focus
should land on Gather. Confirm activating it gathers, and that Craft itself still works normally.

**E. The log.** Check for `[gamepad] '<name>' already navigates down to '<x>'; left it alone`. If that
line appears for Take All, test B will fail and the anchor needs changing — that line is the diagnostic
for exactly this.

**F. Turn it off.** Set `[Gamepad] Enabled = false` and confirm everything reverts to the current
behaviour, as an escape hatch if any of the above misbehaves.
