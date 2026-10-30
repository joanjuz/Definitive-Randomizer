# Definitive Randomizer (pk3DS Progressive)

**Definitive Randomizer**, formerly **pk3DS Progressive**, is a free and open-source evolution of [pk3DS](https://github.com/kwsch/pk3DS). It focuses on deeper progression systems, configurable randomization, balancing, automation, quality-of-life features, and advanced customization for Pokémon games.

It supports the Nintendo 3DS titles X/Y, Omega Ruby/Alpha Sapphire, Sun/Moon and Ultra Sun/Ultra Moon. Support for **Pokémon Sword/Shield** (Nintendo Switch) is in development on a separate branch.

> **Free software, always.** The latest stable version is available for free. There are no paid builds, premium editions, memberships, or paywalled features.

## Project status

| Line | Version | Branch | Notes |
|---|---|---|---|
| Stable (WinForms) | v4.17.1 | `Eris-Ultranova` | Current stable release |
| WPF preview (3DS) | 0.8.0-preview.4 | `feature/3ds-progressive-ui` | Prerelease for testers; not merged into `main` |
| Sword/Shield (Switch) | in development | `feature/switch-swsh-prototype` | Exports mods instead of rebuilding ROMs; not released |

- **Platform:** Windows x64
- **License:** GNU GPL v3
- **Primary progressive feature target:** Pokémon Ultra Sun / Ultra Moon

The two version numbers are independent: v4.x is the original WinForms line, and 0.8.x numbers the new WPF application ("Definitive Randomizer") until it replaces it.

## Definitive Randomizer WPF preview

Windows x64 prereleases of the new WPF editor are published in this repository's [Releases](../../releases). They are built from `feature/3ds-progressive-ui`.

Download the Windows ZIP, extract the complete folder, and run `DefinitiveRandomizer.exe`. The preview includes .NET and the Batch backend. See the [latest tester guide](docs/3ds-progressive/RELEASE_0.8.0_PREVIEW_4.md) for test areas, limitations, and packaging instructions. Personal templates and game data are not included.

## Sword/Shield (in development)

The Switch integration adds editors and randomization for Pokémon, moves, learnsets, evolutions, TM/TR and tutors, trainers, encounters, field items, shops, statics, gifts and trades. It also includes history, a Switch-specific Global Template, Batch mod generation and an integrated runtime (PokeFeur) for encounters, level caps and EXP options. Instead of rebuilding a ROM, it exports a mod in Ryujinx format.

Current limits:

- Targets game version 1.3.2 only. Other versions and updates are rejected.
- Sword has partial in-game confirmation from testing. Shield is validated by automated checks only.
- Random double battles still need full in-game validation.
- Pokédex animations are temporarily disabled.
- Console/Atmosphère and DLC are not validated.

See [HOJA_RUTA_SWITCH.md](docs/3ds-progressive/HOJA_RUTA_SWITCH.md) and [SWITCH_ESCUDO_VALIDACION_Y_COMBATES_DOBLES.md](docs/3ds-progressive/SWITCH_ESCUDO_VALIDACION_Y_COMBATES_DOBLES.md) (Spanish).

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

Progressive-specific functionality is currently most developed for **Ultra Sun / Ultra Moon**. The original pk3DS functionality for other supported Nintendo 3DS Pokémon titles remains part of the project, but not every Progressive feature is available for every game.

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

1. Dump your own legally obtained Pokémon 3DS game.
2. Extract the required RomFS / ExeFS content using your preferred 3DS tooling.
3. Keep your own clean copy of the original extracted files. Automatic persistent backups are disabled by default.
4. Open the extracted game in pk3DS Progressive.
5. Configure the randomization and gameplay options you want.
6. Save the modified files and rebuild or deploy them using your normal 3DS modding workflow.

Because several features modify game files, keep a clean original extraction outside the working project. pk3DS Progressive does not create persistent backups automatically by default.

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
