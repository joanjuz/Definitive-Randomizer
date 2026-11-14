# Definitive Randomizer

**Definitive Randomizer** is a free and open-source Windows application to edit and randomize Pokémon games from your own copies: **Nintendo 3DS**, **Nintendo DS** and **Nintendo Switch (Sword/Shield)**. It is an independent application based on [pk3DS](https://github.com/kwsch/pk3DS) (formerly *pk3DS Progressive*) and adds progression-aware randomization, balancing tools, reusable templates, batch generation and a safety-first editing workflow.

- **Download:** [Definitive Randomizer 1.0.0](../../releases/latest), the first stable version of the WPF application (free, recommended) · [all releases](../../releases)
- **Platform:** Windows x64 (tested on Windows 11). No installer and no .NET required.
- **License:** GNU GPL v3. Free to use, study, modify and share.
- **Guides:** [Getting started](docs/3ds-progressive/PRIMEROS_PASOS.md) · [Backups and recovery](docs/3ds-progressive/RESPALDO_Y_RECUPERACION.md) · [Compatibility](docs/3ds-progressive/COMPATIBILIDAD_PUBLICA.md) · [Limitations](docs/3ds-progressive/LIMITACIONES_ESTABLE.md) (Spanish)

## Which version should I use?

| | **Stable 1.0** (recommended) | **Early access 1.1 preview** |
|---|---|---|
| For | Everyone | People who want to try new features first and report problems |
| Contents | Mature features with in-game or real-file evidence | Everything in 1.0, plus newer features with less in-game testing |
| Updates | Bug fixes only (1.0.1, 1.0.2…) | New features (1.1.0-preview.1, -preview.2…) |
| Availability | Free download from Releases | Builds are delivered first to Ko-fi supporters (page coming soon) and can later be published here; the source code is public and GPLv3 applies in full |

Features in the preview move to a free stable release once they have enough evidence. Details: [version lines](docs/3ds-progressive/LINEAS_DE_VERSION.md) (Spanish).

## Supported games

"Supported" means the game has a profile and its editors and randomizers are implemented. How much each one has been checked (automated tests, real files, in-game) is in the [compatibility matrix](docs/3ds-progressive/COMPATIBILIDAD_PUBLICA.md); "implemented" does not mean "played through".

| Platform | Games | What you provide | Output |
|---|---|---|---|
| Nintendo 3DS | X / Y, Omega Ruby / Alpha Sapphire, Sun / Moon, Ultra Sun / Ultra Moon | An already **decrypted** extraction (the app does not decrypt games) | Rebuilt game files |
| Nintendo DS | Diamond (USA), Platinum (USA), HeartGold (USA), Black, White (Spain), White 2 (USA/EUR) — exact original ROMs | The `.nds` ROM | A **new** ROM; the original is never modified |
| Nintendo Switch | Sword and Shield, game version **1.3.2** only | An extraction of the game | A mod in Ryujinx format |

Other games, regions and revisions (for example Black 2, SoulSilver or Pearl) are rejected instead of being edited blindly. A DS romhack mode is available in beta.

## Main features

- **Editors and randomizers:** Pokémon (stats, types, abilities), moves and learnsets, evolutions, trainers, wild encounters, starters, statics, gifts and trades, items and shops, TMs and tutors.
- **Progressive randomization:** Progressive BST, encounter and trainer scaling that follows the story, trainer and player level caps, smart movesets and held items, random double battles with story restrictions. Most developed for Ultra Sun / Ultra Moon.
- **Ultra Sun / Ultra Moon extras:** Gen 7 Mints (21 Mints), Mega Evolution availability, Move Relearner and other gameplay patches, Definitive Game Data export for Progressive Tracker.
- **DS:** persistent projects (`.drproj`) with backups, a Platinum event and map editor (move, add and copy NPCs), trainer editing shared with 3DS.
- **Templates and automation:** Global Template to record and replay a whole randomization, Batch to generate several outputs, CSV/JSON import and export, a template editor for your own move and stat balance.
- **Safety:** one "Open project" for every platform, transactional saves with recovery, history with Undo (3DS), and work on copies by design.

No balance is applied by default: move and stat changes only come from a CSV you choose. The balance of the *Eris-Ultranova* romhack ships as an optional package in `plantillas-opcionales/eris-ultranova` for those who want it.

## Install

1. Download `DefinitiveRandomizer-<version>-win-x64.zip` and `SHA256SUMS.txt` from [Releases](../../releases).
2. Check the hash: `Get-FileHash .\DefinitiveRandomizer-<version>-win-x64.zip` must match `SHA256SUMS.txt`.
3. Extract the **whole** ZIP into a new folder (do not run it from inside the ZIP or copy it over an older version).
4. Run `DefinitiveRandomizer.exe`.

The executables are **not digitally signed**, so Windows SmartScreen may warn you; the SHA-256 and `VERSION.txt` identify the exact build. Stable and preview can live side by side in different folders.

## Before you edit

- Use your own legally obtained games. This project does **not** distribute ROMs, game files, saves or Nintendo assets.
- Keep a clean copy of your original files and work on a copy.
- Test the result with a new game or a copy of your save.

## Known limitations

The full list is in [LIMITACIONES_ESTABLE.md](docs/3ds-progressive/LIMITACIONES_ESTABLE.md). The most important ones:

- Undo exists only for 3DS projects, covers the last 8 saved changes and does not revert saves or already generated ROMs.
- Mints: Ultra Sun / Ultra Moon only; Ultra Moon has no in-game Mint confirmation.
- DS: only the exact original ROMs listed above; romhack mode is beta; the event editor is Platinum (USA) only.
- Switch: Sword/Shield 1.3.2 only; no real console, Atmosphère or DLC validation.
- Online battles between differently randomized games are not guaranteed to be stable.
- Builds are not digitally signed.

## Reporting a problem

Open an [issue](../../issues) with:

- The version and commit from `VERSION.txt` (stable or preview).
- Game, region and revision, and the platform (3DS, DS, Switch).
- What you did, what you expected and what happened; the seed and any error message.

**Never attach ROMs, game extractions or save files.**

## Support the project

Definitive Randomizer is free software and the stable version is free for everyone. If you want to support development, a Ko-fi page is coming soon. Supporters will get preview builds first and can take part in testing; they support the work, they do not buy exclusive rights. Every build is GPLv3: whoever receives it may share it, and the corresponding source code is in this repository.

Paid custom development (features, tools, patches) is also possible on request. It never includes ROMs, game files or Nintendo content.

## Building from source

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release
```

`3DS.Progressive.Tests` is a standalone executable with its own runner (`dotnet test` does not run it); see [RUNNER_PRUEBAS.md](docs/3ds-progressive/RUNNER_PRUEBAS.md). The CI workflow `.github/workflows/3ds-progressive-ci.yml` shows the full build and test sequence. Release packages are made with `tools/Publish-Preview.ps1`.

The previous WinForms application (v4.x, branch `Eris-Ultranova`) remains available in Releases; it is a separate, older line.

## License and credits

Definitive Randomizer is distributed under the **GNU General Public License v3.0** ([LICENSE.md](LICENSE.md)). Third-party licenses are in [docs/third-party](docs/third-party) and in the `third-party` folder of every package.

- [kwsch/pk3DS](https://github.com/kwsch/pk3DS) and its contributors — the original project this application is based on.
- The projects whose code or data is used under their licenses, listed in `third-party`: pkNX, Universal Pokémon Randomizer ZX, PokeFeur, [adhfult/pk3DS-but-mostly-for-USUM](https://github.com/adhfult/pk3DS-but-mostly-for-USUM) (Nature Mints route, building on ARM/CRO research by ABZB), FFmpeg (through Mobipeg) and the .NET runtime.
- The Project Pokémon community, and the testers and users who report problems.

## Disclaimer

Definitive Randomizer is an independent community project. It is not affiliated with, endorsed by or sponsored by Nintendo, The Pokémon Company, Game Freak or Creatures Inc. Pokémon and related names are trademarks of their respective owners.
