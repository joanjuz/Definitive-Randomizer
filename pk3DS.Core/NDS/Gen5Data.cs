#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Where each Gen5 table lives in a given game, with the CRC32 its NARC must have (UPR-ZX gen5_offsets.ini and Gen5Constants, GPLv3).
/// Defaults are Black/White's; White 2 overrides every number it does not share.
/// </summary>
public sealed record Gen5Profile(string GameCode, bool Black, IReadOnlyDictionary<string, (string Path, uint Crc32)> Files)
{
    public const string TextStrings = "TextStrings", TextStory = "TextStory", PokemonStats = "PokemonStats", Scripts = "Scripts",
        TrainerData = "TrainerData", TrainerPokemon = "TrainerPokemon", WildPokemon = "WildPokemon",
        PokemonGraphics = "PokemonGraphics", StarterGraphics = "StarterGraphics", ShopItems = "ShopItems";

    /// <summary>Black 2/White 2: shops live in their own NARC and the starter script fix is different.</summary>
    public bool Sequel { get; init; }
    public string Game => Sequel ? (Black ? "Negro 2" : "Blanco 2") : Black ? "Negro" : "Blanco";
    public int SpeciesNamesText { get; init; } = 70;
    public int TrainerNamesText { get; init; } = 190;
    public int TrainerClassesText { get; init; } = 191;
    public int TypeNamesText { get; init; } = 199;
    public int ItemNamesText { get; init; } = 54;

    public IReadOnlyList<(int File, int Offset)>[] StarterOffsets { get; init; } = BlackWhiteStarters;
    public int StarterStoryText { get; init; } = 430;
    /// <summary>Story lines naming each starter's type, in starter order (Grass, Fire, Water).</summary>
    public IReadOnlyList<int> StarterTypeLines { get; init; } = [18, 17, 16];
    public int PokedexGivenScript { get; init; } = 792;
    /// <summary>Instruction replaced by a jump to <see cref="PokedexPatch"/>, which replays it first (UPR-ZX bw1/bw2NewStarterScript).</summary>
    public IReadOnlyList<byte> PokedexSignature { get; init; } = [0x24, 0x00, 0xA7, 0x02, 0xE7, 0x00, 0x00, 0x00];
    public IReadOnlyList<byte> PokedexPatch { get; init; } = [0x24, 0x00, 0xA7, 0x02, 0xE7, 0x00, 0x00, 0x00, 0xDE, 0x00, 0x00, 0x00, 0xF8, 0x01, 0x05, 0x00];
    /// <summary>Script command written over the signature: a call (0x04) in Black/White, a jump (0x1E) in the sequels.</summary>
    public byte PokedexJump { get; init; } = 0x04;
    public int StarterCryOverlay { get; init; } = 223;
    public uint StarterCryOverlayCrc32 { get; init; }
    public IReadOnlyList<byte> StarterCryPrefix { get; init; } = [0x08, 0x0A, 0x07, 0x00, 0x08, 0x00, 0x00];
    /// <summary>Write the cry overlay BLZ-compressed again instead of uncompressed.</summary>
    public bool RecompressStarterCryOverlay { get; init; }

    public int ItemBallScript { get; init; } = 864;
    public int HiddenItemScript { get; init; } = 865;
    /// <summary>Overlay with the shop lists, or -1 when they are files of the <see cref="ShopItems"/> NARC.</summary>
    public int ShopOverlay { get; init; } = 21;
    public uint ShopOverlayCrc32 { get; init; }
    public IReadOnlyList<int> ShopItemOffsets { get; init; } = [];
    /// <summary>Items per shop list; in the shop NARC each file holds exactly one list.</summary>
    public IReadOnlyList<int> ShopItemSizes { get; init; } = [2, 2, 3, 4, 4, 6, 7, 7, 8, 8, 8, 8, 8, 9, 9, 9, 11, 11, 11, 11, 14, 16, 16, 17, 18, 19];
    /// <summary>TM shops and the progressive Poké Mart lists are never changed.</summary>
    public IReadOnlyList<int> FixedShops { get; init; } = [0, 1, 2, 4, 7, 16, 20, 23, 24, 25];

    private static readonly IReadOnlyList<(int, int)>[] BlackWhiteStarters =
    [
        [(782, 639), (782, 644), (782, 0x361), (782, 0x5FD), (304, 0xF9), (304, 0x19C)],
        [(782, 687), (782, 692), (782, 0x356), (782, 0x5F2), (304, 0x11C), (304, 0x1C4)],
        [(782, 716), (782, 721), (782, 0x338), (782, 0x5D4), (304, 0x12C), (304, 0x1D9)],
    ];

    private static Dictionary<string, (string, uint)> BlackFiles() => new()
    {
        [TextStrings] = ("a/0/0/2", 0xC66141BE),
        [PokemonStats] = ("a/0/1/6", 0x3A3FDA3E),
        [Scripts] = ("a/0/5/7", 0x0D4E6B38),
        [TrainerData] = ("a/0/9/2", 0xBF203832),
        [TrainerPokemon] = ("a/0/9/3", 0xA0C7D342),
        [WildPokemon] = ("a/1/2/6", 0x4B3B1CF6),
        [TextStory] = ("a/0/0/3", 0xA1D418F5),
        [PokemonGraphics] = ("a/0/0/4", 0x227031B2),
        [StarterGraphics] = ("a/2/0/5", 0x2AE60524),
    };

    public static IReadOnlyList<Gen5Profile> All { get; } =
    [
        new("IRBO", true, BlackFiles())
        {
            StarterCryOverlayCrc32 = 0x4FFD3D7F, ShopOverlayCrc32 = 0xF2B9DC42,
            ShopItemOffsets = [0x51538, 0x5153C, 0x51546, 0x5154C, 0x51564, 0x51590, 0x515E4, 0x515F2, 0x51600, 0x51610, 0x51620, 0x51630, 0x51640, 0x51650, 0x51662, 0x51674, 0x51686, 0x5169C, 0x516B2, 0x516C8, 0x516F8, 0x51714, 0x51734, 0x51774, 0x51796, 0x517BA],
        },
        // Spanish White: White's encounters plus its own text and scripts; the remaining tables match Black (U).
        new("IRAS", false, new Dictionary<string, (string, uint)>(BlackFiles())
        {
            [TextStrings] = ("a/0/0/2", 0xDCFE58C9),
            [Scripts] = ("a/0/5/7", 0x7638D5C9),
            [WildPokemon] = ("a/1/2/6", 0x01FCA7BB),
            [TextStory] = ("a/0/0/3", 0x074DEAC6),
        })
        {
            StarterCryOverlayCrc32 = 0x2F3ADC12, ShopOverlayCrc32 = 0x3882585C,
            ShopItemOffsets = [0x51534, 0x51538, 0x51542, 0x51548, 0x51560, 0x5158C, 0x515E0, 0x515EE, 0x515FC, 0x5160C, 0x5161C, 0x5162C, 0x5163C, 0x5164C, 0x5165E, 0x51670, 0x51682, 0x51698, 0x516AE, 0x516C4, 0x516F4, 0x51710, 0x51730, 0x51770, 0x51792, 0x517B6],
        },
        // White 2 (U): every table shares Black 2 (U)'s CRC32 except the encounters, as UPR-ZX records; verified on the owner's dump.
        new("IRDO", false, new Dictionary<string, (string, uint)>
        {
            [TextStrings] = ("a/0/0/2", 0x0AAFBD81),
            [TextStory] = ("a/0/0/3", 0x579FA304),
            [PokemonGraphics] = ("a/0/0/4", 0xF7FF84DC),
            [PokemonStats] = ("a/0/1/6", 0x01A12D6A),
            [Scripts] = ("a/0/5/6", 0xEA1E0890),
            [TrainerData] = ("a/0/9/1", 0x0852918C),
            [TrainerPokemon] = ("a/0/9/2", 0x9435BD3C),
            [WildPokemon] = ("a/1/2/7", 0xA35466D9),
            [StarterGraphics] = ("a/2/0/2", 0x83EB4FF0),
            [ShopItems] = ("a/2/8/2", 0x52CABC05),
        })
        {
            Sequel = true, SpeciesNamesText = 90, TrainerNamesText = 382, TrainerClassesText = 383, TypeNamesText = 398, ItemNamesText = 64,
            StarterOffsets = [[(854, 0x58B), (854, 0x590), (854, 0x595)], [(854, 0x5C0), (854, 0x5C5), (854, 0x5CA)], [(854, 0x5E2), (854, 0x5E7), (854, 0x5EC)]],
            StarterStoryText = 169, StarterTypeLines = [37, 36, 35], PokedexGivenScript = 854,
            PokedexSignature = [0x28, 0x00, 0xA1, 0x40, 0x04, 0x00],
            PokedexPatch = [0x28, 0x00, 0xA1, 0x40, 0x04, 0x00, 0xDE, 0x00, 0x00, 0x00, 0xFD, 0x01, 0x05, 0x00],
            PokedexJump = 0x1E,
            StarterCryOverlay = 316, StarterCryOverlayCrc32 = 0xD7EED8D1, StarterCryPrefix = [0x08, 0x0A, 0x07, 0x00, 0x00, 0x08, 0x00, 0x00],
            RecompressStarterCryOverlay = true,
            ItemBallScript = 1240, HiddenItemScript = 1241,
            // 32 shops used of the NARC's 80 files; Poké Mart tiers 0-5 and TM shops 7, 10, 12, 17 and 24 stay as they are.
            ShopOverlay = -1, ShopItemSizes = [2, 11, 14, 17, 18, 19, 10, 2, 8, 10, 4, 11, 4, 11, 11, 8, 8, 3, 9, 8, 8, 4, 9, 16, 2, 16, 6, 7, 9, 10, 7, 10],
            FixedShops = [0, 1, 2, 3, 4, 5, 7, 10, 12, 17, 24],
        },
    ];
}

/// <summary>Names of the options a ROM may not support, used by <c>Unavailable</c> in <see cref="Gen5Data"/> and <see cref="Gen4Data"/>.</summary>
public static class Gen5Feature
{
    public const string Starters = "Iniciales", Shops = "Tiendas", HiddenItems = "Objetos ocultos";
}

public sealed record Gen5BaseStats(int Index, byte HP, byte Attack, byte Defense, byte Speed, byte SpAttack, byte SpDefense, byte Type1, byte Type2)
{
    public int Total => HP + Attack + Defense + Speed + SpAttack + SpDefense;
}

public sealed record Gen5TrainerPokemon(int Species, int Form, int Level, int Difficulty, int AbilitySlot, int GenderFlag, int HeldItem, IReadOnlyList<int> Moves);

public sealed record Gen5Trainer(int Id, int Flags, int TrainerClass, int BattleType, string Name, string ClassName, IReadOnlyList<Gen5TrainerPokemon> Team)
{
    public bool HasItems => (Flags & 2) != 0;
    public bool HasMoves => (Flags & 1) != 0;
}

public sealed record Gen5EncounterSlot(int Species, int Form, int MinLevel, int MaxLevel);

public sealed record Gen5EncounterArea(int File, int Season, int Type, int Rate, IReadOnlyList<Gen5EncounterSlot> Slots)
{
    public static readonly string[] TypeNames = ["Hierba/cueva", "Hierba doble", "Hierba que se mueve", "Surf", "Surf en ondas", "Pesca", "Pesca en ondas"];
    public string TypeName => TypeNames[Type];
}

/// <summary>Read-only access to Black/White and White 2 data. Every table is checked against its profile CRC32 before it is parsed.</summary>
public sealed class Gen5Data
{
    private const int SeasonLength = 232;
    private static readonly int[] SlotsPerType = [12, 12, 12, 5, 5, 5, 5];
    private readonly NdsRom _rom;
    private readonly bool _requireOriginalTables;
    private readonly bool _romhack;
    private readonly Dictionary<string, Narc> _narcs = new();

    public NdsIdentityResult Identity { get; }
    public Gen5Profile Profile { get; }
    /// <summary>Opened with <see cref="OpenRomhack"/>: tables are modified, code binaries are checked one by one.</summary>
    public bool IsRomhack => _romhack;
    /// <summary>Options this ROM cannot use, with the reason (empty for originals).</summary>
    public IReadOnlyDictionary<string, string> Unavailable { get; private set; } = new Dictionary<string, string>();

    private Gen5Data(NdsRom rom, NdsIdentityResult identity, Gen5Profile profile, bool requireOriginalTables, bool romhack = false)
    {
        _rom = rom;
        _requireOriginalTables = requireOriginalTables;
        _romhack = romhack;
        Identity = identity;
        Profile = profile;
    }

    /// <summary>
    /// Opens a romhack of a supported game: tables need not match the profile CRC32, but every table must parse and keep the
    /// structure the randomizer relies on. Options that patch code (overlays, ARM9) stay available only while that binary is
    /// still the original; <see cref="Unavailable"/> says which ones are not and why. Exporting is allowed.
    /// </summary>
    public static Gen5Data OpenRomhack(NdsRom rom)
    {
        var data = new Gen5Data(rom, NdsIdentity.Identify(rom), ProfileFor(rom), requireOriginalTables: false, romhack: true);
        foreach (string key in data.Profile.Files.Keys)
            data.Narc(key);
        if (data.SpeciesNames().Count <= Gen5Randomizer.MaxSpecies || data.BaseStats().Count <= Gen5Randomizer.MaxSpecies)
            throw new InvalidDataException("El romhack no tiene los nombres o las estadísticas de las 649 especies donde los espera el perfil.");
        data.Text(data.Profile.ItemNamesText);
        data.Trainers();
        data.Encounters();
        data.FieldItems();
        var unavailable = new Dictionary<string, string>();
        if (!data.OverlayIsOriginal(data.Profile.StarterCryOverlay, data.Profile.StarterCryOverlayCrc32))
            unavailable[Gen5Feature.Starters] = $"el romhack modifica el overlay {data.Profile.StarterCryOverlay} de los iniciales";
        else
            try { data.Starters(); }
            catch (InvalidDataException ex) { unavailable[Gen5Feature.Starters] = "los scripts de los iniciales no son los esperados: " + ex.Message; }
        if (data.Profile.ShopOverlay >= 0 && !data.OverlayIsOriginal(data.Profile.ShopOverlay, data.Profile.ShopOverlayCrc32))
            unavailable[Gen5Feature.Shops] = $"el romhack modifica el overlay {data.Profile.ShopOverlay} de las tiendas";
        else if (data.Profile.ShopOverlay < 0)
            try { data.Shops(); }
            catch (InvalidDataException ex) { unavailable[Gen5Feature.Shops] = ex.Message; }
        data.Unavailable = unavailable;
        if (!unavailable.ContainsKey(Gen5Feature.Starters) && Gen5Randomizer.StartersProblem(data) is { } problem)
            unavailable[Gen5Feature.Starters] = "los iniciales no están donde se esperan: " + problem;
        return data;
    }

    private bool OverlayIsOriginal(int id, uint crc)
    {
        try { return Crc32(_rom.ReadOverlay(id)) == crc; }
        catch (InvalidDataException) { return false; }
    }

    private static Gen5Profile ProfileFor(NdsRom rom)
    {
        var identity = NdsIdentity.Identify(rom);
        return Gen5Profile.All.FirstOrDefault(p => p.GameCode == identity.Profile.GameCode)
            ?? throw new NotSupportedException($"{identity.Profile.Game} todavía no tiene perfil de datos Gen5.");
    }

    /// <summary>
    /// Opens a Black/White or White 2 ROM. Editing requires <paramref name="requireOriginalTables"/>: every table must match its profile CRC32.
    /// Passing false only checks the game code, to re-read ROMs this tool exported; never randomize from such a view.
    /// </summary>
    public static Gen5Data Open(NdsRom rom, bool requireOriginalTables = true)
    {
        var identity = NdsIdentity.Identify(rom);
        var profile = ProfileFor(rom);
        var data = new Gen5Data(rom, identity, profile, requireOriginalTables);
        // Strict mode verifies every table up front; a verification view only reads what it is asked for.
        if (requireOriginalTables)
        {
            foreach (string key in profile.Files.Keys)
                data.Narc(key);
            data.StarterCryOverlay();
            if (profile.ShopOverlay >= 0)
                data.ShopOverlay();
        }
        return data;
    }

    private Narc Narc(string key)
    {
        if (_narcs.TryGetValue(key, out var cached))
            return cached;
        var (path, crc) = Profile.Files[key];
        byte[] bytes = _rom.ReadFile(path);
        uint actual = Crc32(bytes);
        if (_requireOriginalTables && actual != crc)
            throw new InvalidDataException($"{path} ({key}) no coincide con el perfil de {Profile.GameCode}: CRC32 {actual:X8}, se esperaba {crc:X8}.");
        return _narcs[key] = NDS.Narc.Read(bytes);
    }

    public bool RequiresOriginalTables => _requireOriginalTables;

    public string PathOf(string key) => Profile.Files[key].Path;

    /// <summary>Independent copy of a table, safe to modify without affecting what this instance reads.</summary>
    public Narc CopyNarc(string key) => NDS.Narc.Read(Narc(key).Write());

    /// <summary>Writes a new ROM with the randomized tables. The source ROM is never modified.</summary>
    public void Export(string output, Gen5RandomizerResult result)
    {
        if (!_requireOriginalTables && !_romhack)
            throw new InvalidOperationException("Solo se exporta desde una ROM con todas sus tablas originales verificadas o abierta como romhack.");
        _rom.Save(output, result.Files.ToDictionary(p => _rom.File(p.Key).Id, p => p.Value), result.Overlays, result.RecompressOverlays);
    }

    public IReadOnlyList<string> Text(int file) => Gen5Text.Read(Narc(Gen5Profile.TextStrings).Files[file]);

    /// <summary>Decompressed overlay, checked against the profile CRC32 when original tables are required.</summary>
    public byte[] StarterCryOverlay() => Overlay(Profile.StarterCryOverlay, Profile.StarterCryOverlayCrc32);

    public byte[] ShopOverlay() => Profile.ShopOverlay >= 0 ? Overlay(Profile.ShopOverlay, Profile.ShopOverlayCrc32)
        : throw new NotSupportedException($"Las tiendas de {Profile.Game} están en {PathOf(Gen5Profile.ShopItems)}, no en un overlay.");

    private byte[] Overlay(int id, uint crc)
    {
        byte[] overlay = _rom.ReadOverlay(id);
        uint actual = Crc32(overlay);
        // Code is never patched unless it is the original, romhacks included.
        if ((_requireOriginalTables || _romhack) && actual != crc)
            throw new InvalidDataException($"El overlay {id} no coincide con el perfil de {Profile.GameCode}: CRC32 {actual:X8}, se esperaba {crc:X8}.");
        return overlay;
    }

    /// <summary>Item positions in the item-ball and hidden-item scripts: script file, byte offset of the item id, item id.</summary>
    public IReadOnlyList<(int File, int Offset, int Item)> FieldItems()
    {
        var scripts = Narc(Gen5Profile.Scripts).Files;
        var items = new List<(int, int, int)>();
        foreach (var (file, command, variable) in new[] { (Profile.ItemBallScript, 0x28, 0x800C), (Profile.HiddenItemScript, 0x2A, 0x8000) })
        {
            byte[] script = file < scripts.Count ? scripts[file] : throw new InvalidDataException("Faltan los scripts de objetos " + file + ".");
            for (int at = 0; at + 4 <= script.Length && BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(at)) != 0xFD13; at += 4)
            {
                int target = at + 4 + BinaryPrimitives.ReadInt32LittleEndian(script.AsSpan(at));
                if (target < 0 || target > script.Length)
                    break;
                if (target + 8 <= script.Length && BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target + 2)) == command
                    && BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target + 4)) == variable)
                    items.Add((file, target + 6, BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target + 6))));
            }
        }
        return items;
    }

    /// <summary>Every shop list with its items, from the shop overlay (Black/White) or the shop NARC (sequels).</summary>
    public IReadOnlyList<IReadOnlyList<int>> Shops()
    {
        if (Profile.ShopOverlay < 0)
        {
            var files = Narc(Gen5Profile.ShopItems).Files;
            return Profile.ShopItemSizes.Select((size, shop) => files[shop].Length != size * 2
                ? throw new InvalidDataException($"La tienda {shop} no tiene {size} objetos.")
                : (IReadOnlyList<int>)Enumerable.Range(0, size).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(files[shop].AsSpan(i * 2))).ToArray()).ToArray();
        }
        byte[] overlay = ShopOverlay();
        return Profile.ShopItemOffsets.Select((offset, shop) => (IReadOnlyList<int>)Enumerable.Range(0, Profile.ShopItemSizes[shop])
            .Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(overlay.AsSpan(offset + i * 2))).ToArray()).ToArray();
    }

    public IReadOnlyList<string> SpeciesNames() => Text(Profile.SpeciesNamesText);

    public IReadOnlyList<Gen5BaseStats> BaseStats()
    {
        var files = Narc(Gen5Profile.PokemonStats).Files;
        return files.Select((p, i) => p.Length < 8
            ? throw new InvalidDataException("Entrada de estadísticas Gen5 truncada: " + i)
            : new Gen5BaseStats(i, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7])).ToArray();
    }

    public IReadOnlyList<Gen5Trainer> Trainers()
    {
        var headers = Narc(Gen5Profile.TrainerData).Files;
        var teams = Narc(Gen5Profile.TrainerPokemon).Files;
        if (headers.Count != teams.Count)
            throw new InvalidDataException("Las tablas de entrenadores y equipos Gen5 no tienen el mismo número de entradas.");
        var names = Text(Profile.TrainerNamesText);
        var classes = Text(Profile.TrainerClassesText);
        var trainers = new List<Gen5Trainer>();
        // Entry 0 is a placeholder in Black/White.
        for (int id = 1; id < headers.Count; id++)
        {
            byte[] header = headers[id], team = teams[id];
            if (header.Length < 20)
                throw new InvalidDataException("Cabecera de entrenador Gen5 truncada: " + id);
            int flags = header[0], count = header[3];
            int size = 8 + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0);
            if (count > 6 || team.Length < count * size)
                throw new InvalidDataException("Equipo de entrenador Gen5 inválido: " + id);
            var pokemon = new List<Gen5TrainerPokemon>(count);
            for (int slot = 0; slot < count; slot++)
            {
                int at = slot * size;
                int item = (flags & 2) != 0 ? BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 8)) : 0;
                int moves = at + 8 + ((flags & 2) != 0 ? 2 : 0);
                int[] moveIds = (flags & 1) != 0 ? Enumerable.Range(0, 4).Select(m => (int)BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(moves + m * 2))).ToArray() : [];
                pokemon.Add(new Gen5TrainerPokemon(BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 4)), BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 6)),
                    BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 2)), team[at], team[at + 1] >> 4, team[at + 1] & 0xF, item, moveIds));
            }
            // Name line 0 is blank, so trainer id N uses name line N.
            trainers.Add(new Gen5Trainer(id, flags, header[1], header[2], id < names.Count ? names[id] : "",
                header[1] < classes.Count ? classes[header[1]] : "", pokemon));
        }
        return trainers;
    }

    public IReadOnlyList<Gen5EncounterArea> Encounters()
    {
        var areas = new List<Gen5EncounterArea>();
        var files = Narc(Gen5Profile.WildPokemon).Files;
        for (int file = 0; file < files.Count; file++)
        {
            byte[] data = files[file];
            if (data.Length % SeasonLength != 0)
                throw new InvalidDataException("Tabla de encuentros Gen5 con tamaño inesperado: " + file);
            for (int season = 0; season < data.Length / SeasonLength; season++)
            {
                int start = season * SeasonLength, slot = start + 8;
                for (int type = 0; type < SlotsPerType.Length; type++)
                {
                    int rate = data[start + type];
                    if (rate != 0)
                        areas.Add(new Gen5EncounterArea(file, season, type, rate, Enumerable.Range(0, SlotsPerType[type]).Select(i =>
                        {
                            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(slot + i * 4));
                            return new Gen5EncounterSlot(value & 0x7FF, value >> 11, data[slot + i * 4 + 2], data[slot + i * 4 + 3]);
                        }).ToArray()));
                    slot += SlotsPerType[type] * 4;
                }
            }
        }
        return areas;
    }

    /// <summary>Species at every recorded starter position; all positions of one starter must agree.</summary>
    public IReadOnlyList<int> Starters()
    {
        var scripts = Narc(Gen5Profile.Scripts).Files;
        return Profile.StarterOffsets.Select((positions, n) =>
        {
            var species = positions.Select(p => (int)BinaryPrimitives.ReadUInt16LittleEndian(scripts[p.File].AsSpan(p.Offset))).Distinct().ToArray();
            return species.Length == 1 ? species[0] : throw new InvalidDataException($"Las posiciones del inicial {n + 1} no coinciden: {string.Join(", ", species)}.");
        }).ToArray();
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
