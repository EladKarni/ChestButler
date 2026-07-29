# ChestButler 2.0 — agent handoff

You are taking over development of **ChestButler**, a Valheim BepInEx/Jotunn mod that sorts chests.
1.1.2 is released and live. Your job is to build **2.0**, one workstream at a time.

Read this file first, then the two plan documents it points at. Everything below is the result of a
full source audit and three rounds of adversarial review — where it says "verified" or "the audit
found", that is a fact traced to the code, not an assumption. Where it says "unresolved", do not guess.

---

## 1. Where things stand

| | |
|---|---|
| Repo | `C:\Users\Light\projects\Valheim mod` (a **synced folder**, see §4) |
| Branch to build from | `dev` |
| Released | **1.1.2** — `prod`/`staging`/`main` all at the 1.1.2 commit, published to Thunderstore |
| Landed on `dev` since | the 1.1.2 fixes, **Wave 0** (2.0 groundwork), the corrected plan docs |
| 2.0 feature code written | **none yet — you are starting W1** |

The mod ships and works. Do not treat the existing code as a draft: `Router`, `Puller`,
`SorterBehaviour` and the MultiUserChest transfer path are load-bearing and battle-tested. The audit
found real defects in them, all of which are either fixed in 1.1.2 or written up as open items.

## 2. What you are building, in order

Serial, one at a time, each on its own branch off the current `dev`, merged before the next starts.

1. **W1 — Organize v2** (`feat/organize-v2`). The big one. Whole-base, volume-aware allocation.
   Full spec: `docs/organize-v2-allocation-plan.md`. **Start here.**
2. **W2 — Gather** (`feat/gather`). A button in the crafting panel that pulls a recipe's missing
   ingredients from nearby chests. Spec sketch: `docs/roadmap-2.0.md` §7 W2.
3. **W3 — Dedicated Sorter Chest** (`feat/sorter-chest`). A craftable chest that is a Sorter by
   default. Depends on W1. Spec sketch: roadmap §7 W3.
4. **W4 — Gamepad support** (`feat/gamepad`). Last, because it wires up buttons W1 and W2 create.

Then release-prep: one 2.0.0 version bump, consolidated changelog, coordinated release.

**Only W1 is specified to build-ready depth.** W2/W3/W4 are one-paragraph specs plus verified API
corrections. Write the detailed plan for each at the *start* of its turn, not now — and verify its APIs
against the reference assemblies first (§5).

## 3. Read these before writing code

- `docs/roadmap-2.0.md` — the release plan. §3 file-ownership matrix, §4 collision rules, §5 wave
  order, §6 the rules you must follow, §7 per-feature specs, §8 risks, §9 unanswered questions.
- `docs/organize-v2-allocation-plan.md` — the W1 spec. §1–§13 is the design; **§4.1, §15 and §16 are
  the corrections and they override anything earlier that contradicts them.**
- `docs/known-issues-1.1.x.md` — 18 audited defects with per-item status. Six were load-bearing for
  the allocator and are fixed; the rest are marked deferred or decided.

**The three things most likely to waste your time if you skip them:**

1. **§16.1 / §4.1 — the allocator must persist the chests it claims** (a `psort_home` ZDO marker).
   Without it, Organize relocates entire categories on every press and never converges. This is
   already written into the algorithm; just build it.
2. **§16.6 — build the self-throttling**, and note §16.3: `Organizer.BuildPlan` is 0.6–2 s at 400
   chests, synchronous in the click handler. The planner-cost fixes are in scope for 2.0.
3. **§16.2 — eight item-loss/duplication findings**, several already fixed in 1.1.2. Read them before
   touching the execution coroutine so you do not reintroduce one.

## 4. Environment — read this or you will lose hours

The project folder is a **synced mount**, not an ordinary filesystem. Two consequences:

**Git cannot remove its own lock files there.** Every `git` command leaves an `index.lock`,
`HEAD.lock` or `packed-refs.lock` behind, and the *next* git command fails with "another git process
seems to be running". Move them aside before each git invocation:

```bash
for f in .git/index.lock .git/HEAD.lock .git/packed-refs.lock; do
  [ -e "$f" ] && mv "$f" "_to_delete/git-locks/$(basename $f).$RANDOM"
done
```

`_to_delete/git-locks/` must exist — recreate it if the owner has cleaned it up. Related limits:

- **`git checkout` between branches does not work** — git cannot rewrite the working tree, and you get
  a half-switched repo (HEAD moved, files not). Recovering from that needs `git reset --mixed`.
- **Merge by moving the pointer**: `git branch -f <target> <source>` for a fast-forward, never
  checkout-and-merge.
- **`git worktree` is unusable** for the same reason. This is why the workstreams are serial.
- **No network** in the shell that reaches the folder — you cannot `git push`. Stage the commits and
  ask the owner to push.

**Build in a scratch copy, not in the mount.** The working pattern that has been used all along:

1. Copy the source into a scratch dir outside the mount (e.g. `/tmp/cb`).
2. Stage `Managed/` and `libs/` (the game + modding DLLs) into the scratch dir once. They are ~31 MB;
   tar them into a single file inside the mount first, transfer that, and untar — transferring 120
   individual files is slower and the file-transfer cache can serve stale copies.
3. Edit and build there: `dotnet build src/ChestButler/ChestButler.csproj -c Release --no-incremental`.
   .NET 8 SDK installs via `apt-get install -y dotnet-sdk-8.0` after `apt-get update`. NuGet is
   blocked, but the project needs no packages — `NuGet.config` already clears all sources.
4. Run the offline tests: `dotnet run --project tests/OrganizePlannerTests -c Release`.
5. Write finished files back to the mount, then commit there.

**Beware the file-transfer cache:** re-fetching a path you fetched earlier can return the *old*
content even when the tool reports the new size. If a file looks unchanged, copy it to a fresh
filename inside the mount and transfer that instead. (This cost an hour once: a build came out
byte-identical to the previous one because the source never actually updated.)

**Do NOT run `./build.sh`.** It installs into a shared mod-manager profile and its cache-sync loop
overwrites *every* cached version, not just the one the test profile uses. The owner's Test profile
pins cache slot **1.0.2** (yes, really — the label is cosmetic); installing means writing the DLL to
both the profile's plugin folder and that cache slot.

**Verifying a build:** md5 the DLL and grep it for a known new string. .NET string literals are UTF-16
— use `strings -el` for literals and plain `strings` for symbol names.

## 5. Rules — non-negotiable

1. **Never write to a chest inventory directly.** Every transfer goes through MultiUserChest's
   `ContainerHandler` (copy `Puller`/`Organizer`). Never write serialized inventories into a ZDO you
   do not own: `Container.Save()` is owner-gated, so the write is silently discarded. This is the
   single rule that keeps the mod from eating items in multiplayer.
2. **Verify every game/Jotunn/MUC API against the reference assemblies before using it.** Member names
   can be read straight out of `Managed/assembly_valheim.dll` and `libs/*.dll` — a small
   `System.Reflection.Metadata` program does it offline. The audit found three wrong plan-level claims,
   and one static initializer that would have taken down an entire Harmony patch class on a mistyped
   field name.
3. **Do not bump the version or edit `pkg/CHANGELOG.md` / `pkg/manifest.json` / `pkg/README.md`.**
   The integrator owns 2.0.0. Put a "what changed" summary in your commit body.
   (Note: §11 of the v2 plan tells you to ship as 1.1.2 — that section is superseded and says so.)
4. **Stay inside your workstream's declared files** (roadmap §3). W1 legitimately touches six shared
   files; the matrix says which and in what way ("append-only", "scoped region").
5. **Do not push `prod`, publish to Thunderstore, or touch the live server.** The owner releases.
6. **Unit-test pure logic offline.** `tests/OrganizePlannerTests` runs with no game and no packages;
   it is W1's to maintain, and the rewrite will break it until updated.

## 6. Decisions already made — do not reopen

- **Tidy mode (continuous background organizing) is deferred to 2.1.** Do not build it. The analysis is
  v2 plan §15; it needs five separate guards to be safe and is not a prerequisite for anything.
- **`sort: off` / `ignore` means "leave this chest entirely alone"** — not a source and not a target.
  The **Manual** toggle keeps its existing meaning (never auto-filled, but Organize may take from it).
  Sorter chests stay sources. Implementation: split `ChestView.ExcludedAsTarget` into
  `ExcludedAsTarget` + `ExcludedAsSource`. See the exclusion table in §4 of the v2 plan.
- **The three rate knobs are client-side, not admin-only** (already landed). Result-affecting settings
  stay admin-only and server-synced. **W1 also builds self-throttling** — v2 plan §16.6.
- **Design target is a 400+ chest base** until measured. Any O(chests²) pass or per-chest allocation in
  a hot path is a defect, not a nit.

## 7. Still unresolved — do not invent answers

- **Roadmap §9 pre-flight table, 8 questions**, none answered. The owner checks these in-game.
  The one that matters: `SorterRadius` defaults to **128 m**, but chests may only load within **64 m**
  (guaranteed radius is `(N+0.5)×64 − 32` for an N-zone block; `ZoneSystem.c_ZoneSize = 64` is
  confirmed, `m_activeArea` is not). It is either exactly right or exactly 2× over-reach. Do not cap
  the radius or write store copy about range until it is measured — and measure at *runtime*, since
  those are Unity-serialized prefab fields, not C# constants.
- **Does a `to = (-1,-1)` MultiUserChest call create an `InventoryBlock` at all?** Reading the MUC
  source says no — `CanBlockSlot` requires non-negative coordinates — which would make the existing
  `IsSlotBlocked` guards dead code on our paths. Confirm in-game before removing or relying on them.
- **A chest opened by another player is invisible to us** (`Container.m_inUse` is a local field), so
  claiming ownership can strand their edits. 2.0 *can* fix this — every peer runs the mod, so a synced
  in-use flag is possible. Not yet designed.

## 8. Definition of done for W1

- A second Organize on an already-organized base moves **zero** items. This is the acceptance test and
  it is unmeetable without §4.1.
- A chest labelled `sort: off` is neither read nor written.
- `BuildPlan` is stopwatched before and after; the §16.2 cost fixes are in and measured, not assumed.
- The offline test suite passes, including new cases for: the `Ignore` source exclusion, NG+
  mixed-`worldLevel` stacks that cannot merge (which otherwise re-queue forever), and re-run stability.
- Nothing in §16.2 is reintroduced.
- Built, installed to the Test profile, and handed to the owner with a specific list of what to try
  in-game — you cannot test in the game yourself, so say plainly what is unverified.

## 9. How to work with the owner

They know this codebase well and will catch you being vague, so be concrete: name files, symbols and
line-level facts. When something cannot be verified from here — anything needing a running game — say
so explicitly rather than hedging. When a design decision is theirs, ask with the trade-off spelled
out and a recommendation, not an open-ended question. They will tell you when they want depth over
speed; the default so far has been depth.
