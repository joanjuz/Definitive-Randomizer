# Definitive Randomizer (pk3DS Progressive)

**Definitive Randomizer**, formerly **pk3DS Progressive**, is a free and open-source evolution of [pk3DS](https://github.com/kwsch/pk3DS). It focuses on deeper progression systems, configurable randomization, balancing, automation, quality-of-life features, and advanced customization for Pokémon games.

It edits and randomizes three platforms from one Windows application:

- **Nintendo 3DS:** X/Y, Omega Ruby/Alpha Sapphire, Sun/Moon and Ultra Sun/Ultra Moon.
- **Nintendo DS:** Diamond, Platinum, HeartGold, Black, White (Spanish) and White 2, for original ROMs with an exact profile. A romhack mode is available in beta.
- **Nintendo Switch:** Pokémon Sword and Shield, version 1.3.2. The application exports a mod (Ryujinx format) instead of rebuilding a ROM.

How much each game has been checked differs, and "implemented" does not mean "played in-game". See the [compatibility matrix](docs/3ds-progressive/COMPATIBILIDAD_PUBLICA.md) (Spanish) for what is automated, tested on real files or confirmed in-game, and the [stated limitations](docs/3ds-progressive/LIMITACIONES_ESTABLE.md).

> **Free software, always.** The latest stable version is available for free. There are no paid builds, premium editions, memberships, or paywalled features.

## Project status

| Line | Version | Branch | Notes |
|---|---|---|---|
| Stable (WinForms) | v4.17.1 | `Eris-Ultranova` | Current stable release |
| Definitive Randomizer WPF (3DS + DS + Sword/Shield) | Prereleases (latest published: v0.15.0-preview.2) | `main` | Prereleases for testers; Sword/Shield exports mods instead of rebuilding ROMs. `v0.15.0-preview.2` was published from a release branch; its functional fix is also integrated in `main`. A first stable WPF version is being prepared and is **not yet published** |

- **Platform:** Windows x64
- **License:** GNU GPL v3
- **Primary progressive feature target:** Pokémon Ultra Sun / Ultra Moon

The two version numbers are independent: v4.x is the original WinForms line, and 0.x numbers the new WPF application ("Definitive Randomizer") until it replaces it.

## Definitive Randomizer WPF preview

Windows x64 prereleases of the new WPF editor are published in this repository's [Releases](../../releases).

Download the Windows ZIP, extract the **complete** folder into a new directory, and run `DefinitiveRandomizer.exe`. The package includes .NET and the Batch backend. Personal templates and game data are not included. The executables are **not digitally signed**, so Windows or SmartScreen may show a warning; check the SHA-256 listed in the release.

New users: start with [Primeros pasos](docs/3ds-progressive/PRIMEROS_PASOS.md) (installation, project setup, backups, editing, rebuilding and checking the result) and read [Respaldo y recuperación](docs/3ds-progressive/RESPALDO_Y_RECUPERACION.md) before editing. These guides are in Spanish. The tester notes of each release are in [docs/3ds-progressive](docs/3ds-progressive/INDICE.md).

## Sword/Shield (preview)

The Switch integration adds editors and randomization for Pokémon, moves, learnsets, evolutions, TM/TR and tutors, trainers, encounters, field items, shops, statics, gifts and trades. It also includes history, a Switch-specific Global Template, Batch mod generation and an integrated runtime (PokeFeur) for encounters, level caps and EXP options. Instead of rebuilding a ROM, it exports a mod in Ryujinx format.

Current limits:

- Targets game version 1.3.2 only. Other versions and updates are rejected.
- Sword and Shield have in-game confirmation from the owner's testing in the Ryujinx emulator (Shield: the options of 0.9.0-preview.1, tested before release). A full playthrough of every event, real console hardware, Atmosphère and DLC are not claimed.
- Random double battles only start after the first gym: trainers whose team is no stronger than the first gym leader (Milo) stay single, as do the mandatory early Hop battles. There is still no runtime guard for a player with a single usable Pokémon later on.
- Pokédex animations are temporarily disabled.
- Console/Atmosphère and DLC are not validated.

See [SWITCH_ESCUDO_VALIDACION_Y_COMBATES_DOBLES.md](docs/3ds-progressive/SWITCH_ESCUDO_VALIDACION_Y_COMBATES_DOBLES.md) (Spanish).

## Nintendo DS

Pokémon Diamond (USA), Platinum (USA), HeartGold (USA), Black, White (Spanish) and White 2 (USA/EUR) are supported through one exact profile per game, which fixes the code and the tables it expects. Features include trainers, wild encounters, starters, items and shops, game data, evolutions, statics and trades, egg moves and tutors, game tweaks, Global Template and Batch. "Export ROM" writes a **new** ROM; the original is never modified.

Current limits:

- Other games and revisions (for example Black 2, SoulSilver or Pearl) are rejected.
- The romhack mode is beta and only patches game code (overlays, ARM9) when it is the original. Black, White and White 2 are DSi-enhanced and their ARM9 is not modified.
- The owner confirmed phases 0–14 in-game (2026-10-31) and trainer editing (one Pokémon, four moves and an item) in Platinum in combat; that confirmation is not extended to other controls or games.
- The DS map and event editor is not part of the stable scope.

See [COMPATIBILIDAD_PUBLICA.md](docs/3ds-progressive/COMPATIBILIDAD_PUBLICA.md) (Spanish) and the `NDS_FASE*` documents in the [index](docs/3ds-progressive/INDICE.md).

## Current development highlights

The features below describe the 3DS feature set, consolidated in v4.17.1 Stable and carried into the WPF application. Some newer or experimental features may still require additional runtime testing.

### Progressive randomization

- Progressive BST balancing
- Progressive wild encounter scaling
- Story-aware trainer scaling
- Trainer Level Caps
- Player Level Caps
- Totem Level Caps and Totem improvements
- Static, gift, and trade progression controls

### Trainer customization

- Better Movesets with optional TM support
- Smart Held Items with configurable quality
- Strong Stat move-selection logic
- Normal / Important / Boss trainer categories
- Maximum trainer team-size rules
- Random Double Battles with story-aware restrictions
- Double Battle AI support
- Configurable Trainer AI

### Templates and automation

- Global Randomization Template
- Batch ROM Builder
- Reusable trainer, wild, level-cap, and gameplay settings
- Cross-feature execution ordering so dependent systems can work together consistently

### Economy and item QoL

- Editable Fix Economy system
- Expanded Marts
- Optional Rare Candies and six vitamins in marts (+10 EV, up to 252 per stat, for 200 Pokédollars each in USUM)
- Persistent battle consumables
- Additional item and shop quality-of-life options

### USUM gameplay QoL

- Mega Evolution availability options
- All Pokémon Can Call Allies
- Move Relearner patches
- Trade quality-of-life improvements
- Static / gift / trade filters
- Additional configurable gameplay patches

Progressive-specific functionality is currently most developed for **Ultra Sun / Ultra Moon**, including the optional Gen 7 Mints (21 functional Mints, Ultra Sun/Ultra Moon only; confirmed in-game on Ultra Sun, not yet on Ultra Moon). The original pk3DS functionality for other supported Nintendo 3DS Pokémon titles remains part of the project, but not every Progressive feature is available for every game.

## Downloads

Stable releases are distributed **for free** through this repository's [Releases](../../releases).

The latest published stable release is **v4.17.1**, built from `Eris-Ultranova`.

New stable versions will be released after their features have been completed and validated.

pk3DS Progressive does **not** distribute ROMs, decrypted game files, copyrighted Pokémon assets, or Nintendo content.

## Requirements

For prebuilt releases:

- Windows x64
- A legally obtained copy of the supported game
- Properly dumped and extracted game files

For building from source:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A compiler supporting C# 14

Build from the repository root:

```powershell
# WPF application (Definitive Randomizer); also builds pk3DS.Core and the LegacyBatch backend
dotnet build .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release

# Legacy solution (pk3DS.Core + pk3DS.WinForms only)
dotnet build .\pk3DS.slnx -c Release
```

`3DS.Progressive.Tests` is a standalone executable with its own runner, not an MSTest/xUnit project, so `dotnet test` does not run it. See [RUNNER_PRUEBAS.md](docs/3ds-progressive/RUNNER_PRUEBAS.md) for the available options. The CI workflow in `.github/workflows/3ds-progressive-ci.yml` shows the full build, test and preview packaging sequence.

To publish the legacy WinForms build:


```powershell
dotnet publish .\pk3DS.WinForms\pk3DS.WinForms.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
```

## Usage

1. Dump your own legally obtained game. The application does not decrypt commercial games: 3DS projects need an already decrypted extraction.
2. Keep your own clean copy of the original files, and work on a copy.
3. Open the copy in Definitive Randomizer (3DS extraction, DS ROM or Sword/Shield extraction).
4. Configure the edits and randomization you want, and review the preview and the History.
5. Save, then rebuild (3DS), export the ROM (DS) or export the mod (Switch).
6. Test the result in an emulator with a new game or a copy of your save, not your main save.

3DS projects keep restore points (the last 8 saved changes) that the History can undo, but they are not a substitute for your own backups: undo does not revert saves or already generated ROMs. See [Primeros pasos](docs/3ds-progressive/PRIMEROS_PASOS.md) and [Respaldo y recuperación](docs/3ds-progressive/RESPALDO_Y_RECUPERACION.md) (Spanish).

## Support, donations, and commissions

pk3DS Progressive itself is free.

Optional donations through Ko-fi help support continued development, but donating is never required to access stable builds or project features.

Paid **custom development commissions** are also available for requested features, tools, patches, integrations, workflows, or other project-specific programming work.

Commission fees pay for development time and programming work. They do not include the sale or distribution of ROMs, copyrighted game files, Pokémon assets, or Nintendo content.

## License

pk3DS Progressive is distributed under the **GNU General Public License v3.0**.

See [LICENSE.md](LICENSE.md) for the complete license text.

This project is based on the original [pk3DS](https://github.com/kwsch/pk3DS) project and the work of its contributors. pk3DS Progressive contains modifications and additional functionality built on that foundation.

## Credits

- [kwsch/pk3DS](https://github.com/kwsch/pk3DS) — original pk3DS project
- Original pk3DS contributors and the wider Project Pokémon community
- Contributors, testers, and users who help validate pk3DS Progressive

The original pk3DS discussion and community resources are available on [Project Pokémon](https://projectpokemon.org/home/forums/topic/34377-pk3ds-pok%C3%A9mon-3ds-rom-editor-and-randomizer/).

## Disclaimer

pk3DS Progressive is an independent community project.

It is not affiliated with, endorsed by, sponsored by, or associated with Nintendo, The Pokémon Company, Game Freak, or Creatures Inc.

Pokémon and related names and assets are trademarks and copyrights of their respective owners.
