# Hand-off prompt — build ChestButler 1.1.0 "Organize Base"

> Paste everything below the line into a fresh agent working in the ChestButler repo.
> It is self-contained; the agent also has the repo and can read the files it references.

---

You are extending a finished, already-shipped Valheim BepInEx mod called **ChestButler** by
adding one new feature, autonomously and overnight. The owner is ASLEEP and cannot answer
questions. Make reasonable decisions consistent with the existing code, do not stop to ask, and
leave a clean, testable result on the `dev` branch.

## Goal
Implement the **"Organize Base"** feature. The full spec, algorithm, file list, and acceptance
criteria are in `docs/organize-base-plan.md` — READ IT FIRST, in full. Also read
`docs/organize-base-feature.md` for the original design rationale. This prompt is the summary;
the plan is the source of truth.

## What the feature does (one paragraph)
A chest that has **Sorter** enabled gets an **Organize** button. One press sweeps every accessible
chest within the sorter radius (20 m) and consolidates each item type into its best home, in place:
first a chest that pins the item, else a chest whose adjacent crafting station attracts the item's
group (forge→metals/ores, cauldron→cooking, etc.), else the chest already holding the most of it.
Tools/armor stay put unless pinned. It previews first ("move N items across M chests — press again
to confirm") and only executes on a second press.

## Locked decisions — do not change
- Radius = reuse `Plugin.SorterRadius` (20 m). No new radius config.
- Safety = preview-then-confirm (first press previews + counts; second press within ~5 s executes).
- Routing = station-adjacency + most-held, curated server-synced `[Stations]` map (the FULL feature).
- Delivery = build, install the DLL into the local test profile, commit to **dev**.
  DO NOT push prod, DO NOT publish to Thunderstore, DO NOT touch the live server or its cron updater.

## Read these before writing code (all small, ~880 lines total)
`src/ChestButler/Plugin.cs`, `Core/Groups.cs`, `Core/Router.cs`, `Core/Filters.cs`,
`Core/SorterBehaviour.cs`, `Core/Puller.cs`, `Core/ContainerTracker.cs`, `Core/SorterZdo.cs`,
`Core/Names.cs`, `Patches/GuiPatch.cs`, `Patches/ContainerPatches.cs`.
THE ONE INVIOLABLE RULE: never write to a chest inventory you don't own. Every move goes through
MultiUserChest's `ContainerHandler`, which routes to the owner. Copy `Puller`'s transfer call
verbatim — Organize is a client-triggered action like Pull, NOT owner-gated like the sorter tick.

## Environment & build
- Repo: host `C:\Users\Light\projects\Valheim mod`; sandbox mount path is in your Shell-access
  section (`/sessions/<your-id>/mnt/Valheim mod/`, differs per session).
- Build offline: `./build.sh` (dotnet 8, net472; references resolve from `Managed/` and `libs/`,
  which are present but git-ignored). It already installs the DLL into the test profile.
- MOUNT GOTCHAS — these cost hours last time, obey them:
  - Write/edit `.cs` files with a bash heredoc (`cat > f <<'EOF' … EOF`), NOT the Edit/Write
    tools. The synced mount truncated files mid-edit twice. After every write: `wc -l f && tail -1 f`.
  - build.sh passes `--no-incremental` because the mount defeats incremental builds (stale DLLs).
    Verify every build by md5-diffing `dist/ChestButler.dll` and `strings -el dist/ChestButler.dll | grep Organize`.
  - Install goes to the manager folder `…/profiles/Default/BepInEx/plugins/EK_Solutions-ChestButler/`.
    Never leave a loose second DLL in `plugins/` (duplicate-GUID conflict).

## Tasks, in order
1. Read `docs/organize-base-plan.md` and the source files listed above.
2. Confirm station `m_name` keys (not fully verified): the plan lists a starting map; add a brief
   temporary log of detected `CraftingStation.m_name` near chests to confirm keys, or reason from
   the game's prefab tokens, then finalize the map with a "verify in-game" comment.
3. Create `Core/Stations.cs` — config-synced station→groups map, mirroring `Groups.cs`.
4. Create `Core/Organizer.cs` — a PURE planner over POD inputs (deterministic; one winning target
   per item type; tiers = pin > station > most-held; capacity-aware; no self-moves), a thin Unity
   adapter, and a batched execution coroutine on `Plugin.Instance` using `ContainerHandler.RemoveItemFromChest`
   exactly like `Puller`. Algorithm is in plan §3.
5. Wire `Plugin.cs` — add `static Plugin Instance`, `Stations.Init(Config)`, and an
   `OrganizeMovesPerTick` config (default 4).
6. Add the **Organize** button to `GuiPatch.cs` — visible only when `SorterZdo.IsSorter`;
   preview-then-confirm handler (static pending-plan + timestamp; clear on Hide / different chest).
7. Bump version to `1.1.0` in `ChestButler.csproj`, `Plugin.cs`, and `pkg/manifest.json` (all three,
   or build.sh's guard fails). Add a `## 1.1.0` CHANGELOG entry (mention the smelter caveat).
8. Write a pure-logic unit test for the planner (throwaway console in `/tmp` or a `tests/` project;
   must NOT need Unity). Cover the cases in plan §8. Make it pass; capture the output.
9. `./build.sh` — clean build; confirm md5 changed and "Organize" is in the DLL; confirm the DLL
   landed in the manager folder.
10. Commit to **dev** with a clear message. Leave the working tree clean. Put the passing test
    output and a short single-player TEST SCRIPT (plan §11) in the commit body.

## Definition of done
Everything in plan §11. In short: compiles clean, planner unit-tested and passing, DLL installed to
the test profile, committed to dev only, nothing published, and a crisp single-player test script
left for the owner. Where something is genuinely ambiguous, choose the option most consistent with
the existing `SorterBehaviour`/`Puller` code and note it in the commit body — do not block.

## Do NOT
- push prod, publish to Thunderstore, or touch the live server / cron updater.
- write directly to chest inventories — use `ContainerHandler` (copy Puller).
- use the Edit/Write tools on `.cs` files (mount truncates) — use bash heredoc + verify.
- change `Router.cs`'s existing `FindTarget` (the live sorter path must keep working).
- expect to test a 1.1.0 client against the live 1.0.2 server — the version handshake refuses it;
  test in single-player.
