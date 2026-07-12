<!-- PROJECT SHIELDS -->
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]



<!-- PROJECT LOGO -->
<br />
<p align="center">
  <a href="https://github.com/EladKarni/ChestButler">
    <img src="pkg/icon.png" alt="Logo" width="80" height="80">
  </a>

  <h3 align="center">ChestButler</h3>

  <p align="center">
    Your loot, put away for you. A Valheim mod that turns any chest into a sorter - dump everything in, close the lid, and items fly to the right chests around your base.
    <br />
    <br />
    <a href="https://github.com/EladKarni/ChestButler/issues">Report Bug</a>
    ·
    <a href="https://github.com/EladKarni/ChestButler/issues">Request Feature</a>
  </p>
</p>



<!-- TABLE OF CONTENTS -->
<details open="open">
  <summary><h2 style="display: inline-block">Table of Contents</h2></summary>
  <ol>
    <li><a href="#about-the-project">About The Project</a>
      <ul><li><a href="#built-with">Built With</a></li></ul>
    </li>
    <li><a href="#getting-started-players">Getting Started (Players)</a></li>
    <li><a href="#getting-started-developers">Getting Started (Developers)</a></li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgements">Acknowledgements</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project

Base-keeping in Valheim means hauling loot to a dozen chests by hand. ChestButler fixes that with one idea: **designated sorter chests**. Dump your entire inventory into one, close it, and every item routes itself to the right storage chest nearby - stone to the stone chest, carrots to the cooking chest, overflow to wherever fits.

It also works in reverse: chests with saved filters get a **Pull** button that fetches a stack of each wanted item from surrounding storage on demand.

Design principles:

* **Zero-loss by construction.** Items that nothing claims stay in the sorter. Every transfer runs through MultiUserChest's owner-routed networking - no dupes, no vanishing stacks, safe with several players online.
* **Zero-config to start.** With no setup at all, items route to chests that already contain them (fullest chest first). Filters, groups, signs and priorities refine from there.
* **Server-authoritative config.** All settings (radius, speed, item groups) sync from the server; everyone plays by the same rules.

### Built With

* [BepInEx](https://github.com/BepInEx/BepInEx) + [HarmonyX](https://github.com/BepInEx/HarmonyX)
* [Jötunn (JVL)](https://valheim-modding.github.io/Jotunn/)
* [MultiUserChest](https://github.com/MSchmoecker/No-Chest-Block) - networking layer for all item moves
* C# / .NET Framework 4.7.2



<!-- GETTING STARTED: PLAYERS -->
## Getting Started (Players)

### Prerequisites

* Valheim on **Steam** (PC). Xbox / Game Pass cannot run client mods.
* A mod manager: [r2modman or Thunderstore Mod Manager](https://valheim.thunderstore.io) → “Get Manager”.

### Installation

1. In your mod manager, search for **ChestButler** in the Online tab and click Install - BepInEx, Jötunn and MultiUserChest install automatically as dependencies.
2. Launch via **Start modded**.

Manual install: drop `ChestButler.dll` (plus the dependencies) into `BepInEx/plugins/`.

**Multiplayer:** the mod must be installed on the **server and every client**, same version - mismatches are refused at connect with a clear message. Crossplay must be disabled (Steam networking only). On a managed host (e.g. CubeCoders AMP): enable the BepInEx option, then upload `ChestButler.dll`, `Jotunn.dll` and `MultiUserChest.dll` to `BepInEx/plugins/`.



<!-- GETTING STARTED: DEVELOPERS -->
## Getting Started (Developers)

The build is fully offline by design - no NuGet restore; every reference is a local DLL.

### Prerequisites

* .NET SDK 8.0+
* A local Valheim install (for the game assemblies)

### Build setup

1. Clone the repo
   ```sh
   git clone https://github.com/EladKarni/ChestButler.git
   ```
2. Copy the game's `valheim_Data/Managed/` folder into the repo root as `Managed/` (never committed).
3. Drop the modding-stack DLLs into `libs/` - see [`libs/README.md`](libs/README.md) for the exact four files and where to get them.
4. Build:
   ```sh
   ./build.sh          # or: dotnet build src/ChestButler/ChestButler.csproj -c Release
   ```
   Output lands in `dist/ChestButler.dll`. Framework references resolve from `Managed/` via `FrameworkPathOverride`; `NuGet.config` deliberately has zero package sources.

### Architecture map

| Area | Files |
|---|---|
| Plugin entry, config | `src/ChestButler/Plugin.cs` |
| Sorting engine (owner-gated tick) | `Core/SorterBehaviour.cs` |
| Routing rules (tiers, priority, consolidation) | `Core/Router.cs` |
| Per-chest filters (ZDO pins + sign parsing) | `Core/Filters.cs`, `Core/SorterZdo.cs` |
| Pull/restock | `Core/Puller.cs` |
| Item groups (synced config) | `Core/Groups.cs`, `Core/Names.cs` |
| Chest-UI toolbar | `Patches/GuiPatch.cs` |

Key invariant to preserve in any PR: **inventories are only ever mutated by their ZDO owner** - all cross-chest moves go through MultiUserChest's request/response API. Direct `Inventory.AddItem` on a remote chest is how the mod this replaces destroyed items.



<!-- USAGE -->
## Usage

Buttons appear at the bottom of every chest UI:

| Button | Shown on | Does |
|---|---|---|
| `Sorter: ON/OFF` | any chest | makes it a dump chest - contents distribute when closed |
| `Pin` → `Auto (n)`/`Manual (n)` | non-sorter chests | saves current contents as filters; then toggles auto-fill vs pull-only |
| `Clear` | chests with filters | erases saved filters |
| `Pull` | chests with filters | fetches one stack of each saved type from nearby chests |

**Sign labels** (optional): place a sign on/next to a chest - `sort: cooking`, `sort: finewood, trophy*, p5`, `sort: off`. Tokens: group names, item names (`*` wildcards), `pN` priority, `off` to exclude.

**Routing order:** explicit item filter → group filter → “already contains it”. Ties: higher priority, then most-of-that-item held, then nearest. Partial fills top off a chest and re-route the remainder.

**Config:** `BepInEx/config/light.chestbutler.cfg` - radius (default 20 m), tick rate, stacks per tick, contains-fallback, and all item groups under `[ItemGroups]`. Server values win and sync to clients.



<!-- ROADMAP -->
## Roadmap

* [ ] Transfer VFX/SFX on chests
* [ ] Gamepad support for the chest-UI toolbar
* [ ] Filter-editor panel (view/remove individual pinned items, group checkboxes)
* [ ] Craftable dedicated Sorter Chest piece
* [ ] Localization
* [ ] Valheim 1.0 (“Deep North”, Sept 2026) compatibility pass

See [open issues](https://github.com/EladKarni/ChestButler/issues) for the full list.



<!-- CONTRIBUTING -->
## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request **against `dev`**

Branch model: `dev` (active work) → `staging` (group playtesting) → `prod` (what the live server runs) → `main` (latest published release).

House rules: never commit game assemblies (`Managed/`) or third-party DLLs (`libs/`); keep the owner-only mutation invariant; test multiplayer-sensitive changes with two clients before PR.



<!-- LICENSE -->
## License

Distributed under the MIT License. See `LICENSE` for more information.



<!-- CONTACT -->
## Contact

Elad Karni - elad.karni@gmail.com

Project Link: [https://github.com/EladKarni/ChestButler](https://github.com/EladKarni/ChestButler)



<!-- ACKNOWLEDGEMENTS -->
## Acknowledgements

* [MultiUserChest](https://github.com/MSchmoecker/No-Chest-Block) and [ItemHopper](https://github.com/MSchmoecker/ValheimHopper) by MSchmoecker - the proof that safe networked item moves are possible, and the library this mod stands on
* [Jötunn](https://valheim-modding.github.io/Jotunn/) - mod compatibility & synced config
* Smarter Containers by Flueno - the original concept (rebuilt here from scratch with a safe transfer engine)
* [Best-README-Template](https://github.com/EladKarni/Best-README-Template)



<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/EladKarni/ChestButler.svg?style=for-the-badge
[contributors-url]: https://github.com/EladKarni/ChestButler/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/EladKarni/ChestButler.svg?style=for-the-badge
[forks-url]: https://github.com/EladKarni/ChestButler/network/members
[stars-shield]: https://img.shields.io/github/stars/EladKarni/ChestButler.svg?style=for-the-badge
[stars-url]: https://github.com/EladKarni/ChestButler/stargazers
[issues-shield]: https://img.shields.io/github/issues/EladKarni/ChestButler.svg?style=for-the-badge
[issues-url]: https://github.com/EladKarni/ChestButler/issues
[license-shield]: https://img.shields.io/github/license/EladKarni/ChestButler.svg?style=for-the-badge
[license-url]: https://github.com/EladKarni/ChestButler/blob/main/LICENSE
