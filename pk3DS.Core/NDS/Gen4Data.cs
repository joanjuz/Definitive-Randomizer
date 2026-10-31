#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

public enum Gen4Kind { DiamondPearl, Platinum, HeartGoldSoulSilver }

/// <summary>Where each Gen4 table lives in a given game, with the CRC32 its NARC must have (UPR-ZX gen4_offsets.ini, GPLv3).</summary>
public sealed record Gen4Profile(string GameCode, byte Version, Gen4Kind Kind, IReadOnlyDictionary<string, (string Path, uint Crc32)> Files,
    int SpeciesNamesText, int TrainerNamesText, int TrainerClassesText, int ItemNamesText, int TypeNamesText)
{
    public const string Text = "Text", PokemonStats = "PokemonStats", TrainerData = "TrainerData", TrainerPokemon = "TrainerPokemon",
        WildPokemon = "WildPokemon", Scripts = "Scripts", Learnsets = "Learnsets", Evolutions = "Evolutions", Moves = "Moves";

    public string Game => Kind switch { Gen4Kind.DiamondPearl => "Diamante", Gen4Kind.Platinum => "Platino", _ => "HeartGold" };

    public int MoveNamesText { get; init; }
    public int AbilityNamesText { get; init; }
    /// <summary>CRC32 of the decompressed ARM9, which holds the hidden items and the shops.</summary>
    public uint Arm9Crc32 { get; init; }
    /// <summary>Script file listing the item balls; entries in <see cref="ItemBallSkip"/> are not item balls (UPR-ZX ItemBallsSkip).</summary>
    public int ItemBallScript { get; init; }
    public IReadOnlyList<int> ItemBallSkip { get; init; } = [];
    /// <summary>Hidden item table in the ARM9: 8-byte entries starting with the item id.</summary>
    public int HiddenItemTable { get; init; }
    public int HiddenItemCount { get; init; }
    /// <summary>Bytes right before the first shop list in the ARM9; each list ends with 0xFFFF.</summary>
    public IReadOnlyList<byte> ShopPrefix { get; init; } = [];
    public int ShopCount { get; init; }
    /// <summary>TM, Battle Frontier, prize and other special lists that never change (UPR-ZX SkipShops).</summary>
    public IReadOnlyList<int> FixedShops { get; init; } = [];

    /// <summary>Overlay with the starters (Diamond/Platinum, at <see cref="StarterOffset"/>) or their cries (HeartGold).</summary>
    public int StarterOverlay { get; init; }
    public uint StarterOverlayCrc32 { get; init; }
    public int StarterOffset { get; init; }
    /// <summary>Selection-screen text: lines 1-3 (and 4-6 in HeartGold) name each starter.</summary>
    public int StarterScreenText { get; init; }
    /// <summary>Pokédex category names ("Tiny Leaf Pokémon"), used by the Diamond/Platinum selection text.</summary>
    public int DexCategoryText { get; init; }
    /// <summary>Scripts with rival battles that branch on the player's starter, and how many such branches there are.</summary>
    public IReadOnlyList<int> RivalScripts { get; init; } = [];
    public int RivalScriptCount { get; init; }
    /// <summary>Diamond/Platinum tag battles whose partner depends on the player's starter, and how many there are.</summary>
    public IReadOnlyList<int> TagScripts { get; init; } = [];
    public int TagScriptCount { get; init; }

    public static IReadOnlyList<Gen4Profile> All { get; } =
    [
        new("ADAE", 5, Gen4Kind.DiamondPearl, new Dictionary<string, (string, uint)>
        {
            [Text] = ("msgdata/msg.narc", 0xCC7250FE),
            [PokemonStats] = ("poketool/personal/personal.narc", 0xF963E181),
            [TrainerData] = ("poketool/trainer/trdata.narc", 0x4A3C0C89),
            [TrainerPokemon] = ("poketool/trainer/trpoke.narc", 0xC4AA9026),
            [WildPokemon] = ("fielddata/encountdata/d_enc_data.narc", 0xE58BE3CA),
            [Scripts] = ("fielddata/script/scr_seq_release.narc", 0x5FDE722D),
            [Learnsets] = ("poketool/personal/wotbl.narc", 0x2AC7EF62),
            [Evolutions] = ("poketool/personal/evo.narc", 0x0F425176),
            [Moves] = ("poketool/waza/waza_tbl.narc", 0x1D89A95D),
        }, 362, 559, 560, 344, 565)
        {
            MoveNamesText = 588, AbilityNamesText = 552, Arm9Crc32 = 0x08E0337C, ItemBallScript = 370, ItemBallSkip = [40, 196], HiddenItemTable = 0xF2DB4, HiddenItemCount = 229,
            ShopPrefix = Convert.FromHexString("391104027511040285AF0302A5AF0302"), ShopCount = 28, FixedShops = [12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23],
            StarterOverlay = 64, StarterOverlayCrc32 = 0x727963E2, StarterOffset = 0x1B88, StarterScreenText = 320, DexCategoryText = 621,
            RivalScripts = [34, 90, 118, 180, 195, 394], RivalScriptCount = 7, TagScripts = [2, 131, 230], TagScriptCount = 5,
        },
        new("CPUE", 1, Gen4Kind.Platinum, new Dictionary<string, (string, uint)>
        {
            [Text] = ("msgdata/pl_msg.narc", 0x5666AAEC),
            [PokemonStats] = ("poketool/personal/pl_personal.narc", 0xB16A8A01),
            [TrainerData] = ("poketool/trainer/trdata.narc", 0x2EE9B886),
            [TrainerPokemon] = ("poketool/trainer/trpoke.narc", 0x739A02EE),
            [WildPokemon] = ("fielddata/encountdata/pl_enc_data.narc", 0x188084F5),
            [Scripts] = ("fielddata/script/scr_seq.narc", 0x6D482515),
            [Learnsets] = ("poketool/personal/wotbl.narc", 0x977997B9),
            [Evolutions] = ("poketool/personal/evo.narc", 0x6711E4B4),
            [Moves] = ("poketool/waza/pl_waza_tbl.narc", 0xB2D18230),
        }, 412, 618, 619, 392, 624)
        {
            MoveNamesText = 647, AbilityNamesText = 610, Arm9Crc32 = 0x4D104949, ItemBallScript = 404, ItemBallSkip = [25, 238, 321, 325, 326], HiddenItemTable = 0xEA378, HiddenItemCount = 257,
            ShopPrefix = Convert.FromHexString("ED7F0402258004026180040281800402"), ShopCount = 29, FixedShops = [13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24],
            StarterOverlay = 78, StarterOverlayCrc32 = 0x091E8E97, StarterOffset = 0x1BC0, StarterScreenText = 360, DexCategoryText = 711,
            RivalScripts = [31, 36, 112, 123, 186, 427, 429, 1096], RivalScriptCount = 9, TagScripts = [2, 136, 201, 236], TagScriptCount = 6,
        },
        new("IPKE", 0, Gen4Kind.HeartGoldSoulSilver, new Dictionary<string, (string, uint)>
        {
            [Text] = ("a/0/2/7", 0x123DEB0B),
            [PokemonStats] = ("a/0/0/2", 0xDE8C5CAF),
            [TrainerData] = ("a/0/5/5", 0x8B9BD9CE),
            [TrainerPokemon] = ("a/0/5/6", 0xE30BC8BE),
            [WildPokemon] = ("a/0/3/7", 0x9B861BD5),
            [Scripts] = ("a/0/1/2", 0xA7E3F740),
            [Learnsets] = ("a/0/3/3", 0xF0F9B169),
            [Evolutions] = ("a/0/3/4", 0x6711E4B4),
            [Moves] = ("a/0/1/1", 0x284AEE7A),
        }, 237, 729, 730, 222, 735)
        {
            MoveNamesText = 750, AbilityNamesText = 720, Arm9Crc32 = 0x99A30D93, ItemBallScript = 141, ItemBallSkip = [58], HiddenItemTable = 0xFA558, HiddenItemCount = 231,
            ShopPrefix = Convert.FromHexString("298E0402618E0402998E0402B98E0402"), ShopCount = 40, FixedShops = [17, 18, 22, 23, 24, 25, 26, 27, 28, 29, 30, 37, 39],
            StarterOverlay = 61, StarterOverlayCrc32 = 0xEE849CB4, StarterScreenText = 190,
            RivalScripts = [7, 23, 96, 110, 819, 850, 866], RivalScriptCount = 7,
        },
    ];
}

/// <summary>
/// One wild species slot: where its species is stored (<paramref name="Width"/> 4 in Diamond/Platinum, 2 in HeartGold) and the
/// level band it is met at. Slots that share <paramref name="Group"/> in one file are one encounter table.
/// </summary>
public sealed record Gen4EncounterSlot(int File, string Group, int Index, int Offset, int Width, int Species, int MinLevel, int MaxLevel);

/// <summary>A field item: an item ball (offset in the item-ball script) or a hidden item (offset in the ARM9).</summary>
public sealed record Gen4FieldItem(bool Hidden, int Offset, int Item);

/// <summary>One item of a shop list: its offset in the ARM9 and the item id (0 entries are kept as they are).</summary>
public sealed record Gen4ShopItem(int Shop, int Index, int Offset, int Item);

/// <summary>Read-only access to Diamond, Platinum and HeartGold data. Every table is checked against its profile CRC32 before it is parsed.</summary>
public sealed class Gen4Data
{
    public const int MaxSpecies = 493;
    private const int DpptLength = 424, HgssLength = 196;
    // Grass slot whose level each Diamond/Platinum replacement slot uses (UPR-ZX Gen4Constants.dpptAlternateSlots, GPLv3).
    private static readonly int[] DpptAlternateSlots = [0, 1, 2, 3, 2, 3, 4, 5, 10, 11, 8, 9, 8, 9, 8, 9, 8, 9, 8, 9];
    private static readonly string[] DpptAlternateNames = ["Enjambre", "Enjambre", "Día", "Día", "Noche", "Noche", "Radar", "Radar", "Radar", "Radar",
        "Rubí", "Rubí", "Zafiro", "Zafiro", "Esmeralda", "Esmeralda", "Rojo Fuego", "Rojo Fuego", "Verde Hoja", "Verde Hoja"];
    private static readonly string[] DpptWaterNames = ["Surf", "", "Caña Vieja", "Caña Buena", "Supercaña"];
    private static readonly string[] HgssNonGrassNames = ["", "Surf", "Golpe Roca", "Caña Vieja", "Caña Buena", "Supercaña"];
    private static readonly int[] HgssAmounts = [0, 5, 2, 5, 5, 5];

    private readonly NdsRom _rom;
    private readonly bool _requireOriginalTables;
    private readonly bool _romhack;
    private readonly Dictionary<string, Narc> _narcs = new();
    private byte[]? _arm9;
    private readonly Gen4Data? _base;
    private readonly IReadOnlyDictionary<string, byte[]>? _draftFiles;
    private readonly IReadOnlyDictionary<int, byte[]>? _draftOverlays;

    public NdsIdentityResult Identity { get; }
    public Gen4Profile Profile { get; }
    /// <summary>Created by <see cref="WithDraft"/>: reads changed tables first and cannot export.</summary>
    public bool IsDraft => _base is not null;
    /// <summary>Opened with <see cref="OpenRomhack"/>: tables are modified, code binaries are checked one by one.</summary>
    public bool IsRomhack => _romhack;
    /// <summary>Options this ROM cannot use, with the reason (empty for originals).</summary>
    public IReadOnlyDictionary<string, string> Unavailable { get; private set; } = new Dictionary<string, string>();

    private Gen4Data(NdsRom rom, NdsIdentityResult identity, Gen4Profile profile, bool requireOriginalTables, bool romhack = false)
    {
        _rom = rom;
        _requireOriginalTables = requireOriginalTables;
        _romhack = romhack;
        Identity = identity;
        Profile = profile;
    }

    /// <summary>
    /// View of this ROM with some tables already changed: <paramref name="files"/> by ROM path and decompressed overlays by id
    /// and a decompressed ARM9 replace the originals when read. Used to stack edits and randomizations before one export; a draft view never exports.
    /// </summary>
    public Gen4Data WithDraft(IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<int, byte[]> overlays, byte[]? arm9) =>
        new(_base ?? this, files, overlays, arm9);

    private Gen4Data(Gen4Data root, IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<int, byte[]> overlays, byte[]? arm9)
        : this(root._rom, root.Identity, root.Profile, requireOriginalTables: false, romhack: root._romhack)
    {
        _base = root;
        _draftFiles = files;
        _draftOverlays = overlays;
        _arm9 = arm9 is null ? null : (byte[])arm9.Clone();
        Unavailable = root.Unavailable;
    }

    /// <summary>Starter overlay as it is in the ROM, before any draft change (the selection-screen code patch starts from it).</summary>
    internal byte[] OriginalStarterOverlay() => (_base ?? this).StarterOverlay();

    /// <summary>
    /// Opens a romhack of a supported game: tables need not match the profile CRC32, but every table must parse and keep the
    /// structure the randomizer relies on. Options that patch code (overlays, ARM9) stay available only while that binary is
    /// still the original; <see cref="Unavailable"/> says which ones are not and why. Exporting is allowed.
    /// </summary>
    public static Gen4Data OpenRomhack(NdsRom rom)
    {
        var identity = NdsIdentity.Identify(rom);
        var data = new Gen4Data(rom, identity, ProfileFor(identity), requireOriginalTables: false, romhack: true);
        var unavailable = new Dictionary<string, string>();
        foreach (string key in data.Profile.Files.Keys)
            try { data.Narc(key); }
            catch (InvalidDataException ex) when (Gen5Feature.OptionalTables.TryGetValue(key, out string? feature))
            {
                unavailable[feature] = $"{data.PathOf(key)} no se puede leer ({ex.Message})";
            }
        if (data.SpeciesNames().Count <= MaxSpecies || data.BaseStats().Count <= MaxSpecies)
            throw new InvalidDataException("El romhack no tiene los nombres o las estadísticas de las 493 especies donde los espera el perfil.");
        data.Text(data.Profile.ItemNamesText);
        data.Text(data.Profile.MoveNamesText);
        data.Trainers();
        data.Encounters();
        bool arm9 = Matches(() => data._rom.ReadArm9(), data.Profile.Arm9Crc32);
        bool overlay = Matches(() => data._rom.ReadOverlay(data.Profile.StarterOverlay), data.Profile.StarterOverlayCrc32);
        if (!overlay || (!arm9 && data.Profile.Kind == Gen4Kind.HeartGoldSoulSilver))
            unavailable[Gen5Feature.Starters] = overlay ? "el romhack modifica el ARM9, donde están los iniciales" : $"el romhack modifica el overlay {data.Profile.StarterOverlay} de los iniciales";
        if (!arm9)
        {
            unavailable[Gen5Feature.Shops] = "el romhack modifica el ARM9, donde están las tiendas";
            unavailable[Gen5Feature.HiddenItems] = "el romhack modifica el ARM9, donde están los objetos ocultos";
        }
        data.Unavailable = unavailable;
        if (!unavailable.ContainsKey(Gen5Feature.Starters) && Gen4Randomizer.StartersProblem(data) is { } problem)
            unavailable[Gen5Feature.Starters] = "los iniciales no están donde se esperan: " + problem;
        data.FieldItems();
        return data;
    }

    private static bool Matches(Func<byte[]> read, uint crc)
    {
        try { return Gen5Data.Crc32(read()) == crc; }
        catch (InvalidDataException) { return false; }
    }

    private static Gen4Profile ProfileFor(NdsIdentityResult identity) =>
        Gen4Profile.All.FirstOrDefault(p => p.GameCode == identity.Profile.GameCode && p.Version == identity.Profile.Version)
            ?? throw new NotSupportedException($"{identity.Profile.Game} no tiene perfil de datos Gen4.");

    /// <summary>
    /// Opens a Diamond, Platinum or HeartGold ROM. Editing requires <paramref name="requireOriginalTables"/>: every table must match
    /// its profile CRC32. Passing false only checks the game code, to re-read ROMs this tool exported; never randomize from such a view.
    /// </summary>
    public static Gen4Data Open(NdsRom rom, bool requireOriginalTables = true)
    {
        var identity = NdsIdentity.Identify(rom);
        var profile = ProfileFor(identity);
        var data = new Gen4Data(rom, identity, profile, requireOriginalTables);
        if (requireOriginalTables)
        {
            foreach (string key in profile.Files.Keys)
                data.Narc(key);
            data.Arm9();
            data.StarterOverlay();
        }
        return data;
    }

    /// <summary>Copy of the decompressed ARM9, checked against the profile CRC32 when original tables are required.</summary>
    public byte[] Arm9()
    {
        if (_arm9 is null && _base is not null)
            _arm9 = _base.Arm9();
        if (_arm9 is null)
        {
            byte[] arm9 = _rom.ReadArm9();
            uint actual = Gen5Data.Crc32(arm9);
            // Code is never patched unless it is the original, romhacks included.
            if ((_requireOriginalTables || _romhack) && actual != Profile.Arm9Crc32)
                throw new InvalidDataException($"El ARM9 no coincide con el perfil de {Profile.GameCode}: CRC32 {actual:X8}, se esperaba {Profile.Arm9Crc32:X8}.");
            _arm9 = arm9;
        }
        return (byte[])_arm9.Clone();
    }

    /// <summary>Decompressed starter overlay, checked against the profile CRC32 when original tables are required.</summary>
    public byte[] StarterOverlay()
    {
        if (_draftOverlays is not null && _draftOverlays.TryGetValue(Profile.StarterOverlay, out byte[]? draft))
            return (byte[])draft.Clone();
        if (_base is not null)
            return _base.StarterOverlay();
        byte[] overlay = _rom.ReadOverlay(Profile.StarterOverlay);
        uint actual = Gen5Data.Crc32(overlay);
        if ((_requireOriginalTables || _romhack) && actual != Profile.StarterOverlayCrc32)
            throw new InvalidDataException($"El overlay {Profile.StarterOverlay} no coincide con el perfil de {Profile.GameCode}: CRC32 {actual:X8}, se esperaba {Profile.StarterOverlayCrc32:X8}.");
        return overlay;
    }

    // HeartGold keeps its starters in the ARM9, 13 bytes before this code (UPR-ZX hgssStarterCodeSuffix).
    private static readonly byte[] HgssStarterSuffix = [0x03, 0x03, 0x1A, 0x12, 0x01, 0x23, 0x00, 0x00];

    /// <summary>Where the three starters are: ARM9 offset in HeartGold, starter-overlay offset in Diamond/Platinum (16-bit values, 4 bytes apart).</summary>
    public int StarterTable(byte[] arm9) => Profile.Kind == Gen4Kind.HeartGoldSoulSilver ? FindUnique(arm9, HgssStarterSuffix, "los iniciales en el ARM9") - 13 : Profile.StarterOffset;

    /// <summary>The three starters (Grass, Fire, Water).</summary>
    public IReadOnlyList<int> Starters()
    {
        byte[] source = Profile.Kind == Gen4Kind.HeartGoldSoulSilver ? Arm9() : StarterOverlay();
        int at = StarterTable(source);
        return Enumerable.Range(0, 3).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(at + i * 4))).ToArray();
    }

    internal static int FindUnique(byte[] data, IReadOnlyList<byte> pattern, string what)
    {
        byte[] bytes = [.. pattern];
        int found = -1;
        for (int i = 0; i + bytes.Length <= data.Length; i++)
            if (data.AsSpan(i, bytes.Length).SequenceEqual(bytes))
                found = found == -1 ? i : throw new InvalidDataException($"Se encontró más de una vez {what}.");
        return found >= 0 ? found : throw new InvalidDataException($"No se encontró {what}.");
    }

    /// <summary>Item balls from the item-ball script, then the hidden items from the ARM9 table (unless a romhack changed the ARM9).</summary>
    public IReadOnlyList<Gen4FieldItem> FieldItems()
    {
        var scriptFiles = Narc(Gen4Profile.Scripts).Files;
        byte[] script = Profile.ItemBallScript < scriptFiles.Count ? scriptFiles[Profile.ItemBallScript] : throw new InvalidDataException("Falta el script de objetos " + Profile.ItemBallScript + ".");
        int setVar = Profile.Kind == Gen4Kind.HeartGoldSoulSilver ? 0x29 : 0x28;
        var items = new List<Gen4FieldItem>();
        for (int at = 0, index = 0; at + 4 <= script.Length && BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(at)) != 0xFD13; at += 4, index++)
        {
            int target = at + 4 + BinaryPrimitives.ReadInt32LittleEndian(script.AsSpan(at));
            if (target < 0 || target > script.Length)
                break;
            if (Profile.ItemBallSkip.Contains(index) || target + 6 > script.Length)
                continue;
            // setvar 0x8008, item
            if (BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target)) == setVar && BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target + 2)) == 0x8008)
                items.Add(new Gen4FieldItem(false, target + 4, BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(target + 4))));
        }
        if (Unavailable.ContainsKey(Gen5Feature.HiddenItems))
            return items;
        byte[] arm9 = Arm9();
        if (Profile.HiddenItemTable + Profile.HiddenItemCount * 8 > arm9.Length)
            throw new InvalidDataException("La tabla de objetos ocultos sale del ARM9.");
        for (int i = 0; i < Profile.HiddenItemCount; i++)
        {
            int at = Profile.HiddenItemTable + i * 8;
            items.Add(new Gen4FieldItem(true, at, BinaryPrimitives.ReadUInt16LittleEndian(arm9.AsSpan(at))));
        }
        return items;
    }

    /// <summary>Every item of every shop list in the ARM9, found after the profile's unique prefix.</summary>
    public IReadOnlyList<Gen4ShopItem> Shops()
    {
        byte[] arm9 = Arm9();
        int start = -1;
        byte[] prefix = [.. Profile.ShopPrefix];
        for (int i = 0; i + prefix.Length <= arm9.Length; i++)
            if (arm9.AsSpan(i, prefix.Length).SequenceEqual(prefix))
                start = start == -1 ? i + prefix.Length : throw new InvalidDataException("El prefijo de las tiendas aparece más de una vez en el ARM9.");
        if (start < 0)
            throw new InvalidDataException("No se encontraron las tiendas en el ARM9.");
        var items = new List<Gen4ShopItem>();
        int at = start;
        for (int shop = 0; shop < Profile.ShopCount; shop++, at += 2)
            for (int index = 0; ; index++, at += 2)
            {
                if (at + 2 > arm9.Length)
                    throw new InvalidDataException("Las tiendas salen del ARM9.");
                int item = BinaryPrimitives.ReadUInt16LittleEndian(arm9.AsSpan(at));
                if (item == 0xFFFF)
                    break;
                items.Add(new Gen4ShopItem(shop, index, at, item));
            }
        return items;
    }

    private Narc Narc(string key)
    {
        if (_narcs.TryGetValue(key, out var cached))
            return cached;
        var (path, crc) = Profile.Files[key];
        if (_draftFiles is not null && _draftFiles.TryGetValue(path, out byte[]? draft))
            return _narcs[key] = NDS.Narc.Read(draft);
        if (_base is not null)
            return _narcs[key] = _base.Narc(key);
        byte[] bytes = _rom.ReadFile(path);
        uint actual = Gen5Data.Crc32(bytes);
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
        if (IsDraft || !_requireOriginalTables && !_romhack)
            throw new InvalidOperationException("Solo se exporta desde una ROM con todas sus tablas originales verificadas o abierta como romhack.");
        if (result.Overlays.Keys.Any(id => id != Profile.StarterOverlay))
            throw new InvalidOperationException("Gen4 solo modifica el overlay de los iniciales.");
        _rom.Save(output, result.Files.ToDictionary(p => _rom.File(p.Key).Id, p => p.Value), result.Overlays, result.RecompressOverlays, result.Arm9);
    }

    public IReadOnlyList<string> Text(int file)
    {
        var files = Narc(Gen4Profile.Text).Files;
        return file >= 0 && file < files.Count ? Gen4Text.Read(files[file]) : throw new InvalidDataException("Falta el archivo de texto " + file + ".");
    }

    public IReadOnlyList<string> SpeciesNames() => Text(Profile.SpeciesNamesText);

    public IReadOnlyList<Gen5BaseStats> BaseStats()
    {
        var files = Narc(Gen4Profile.PokemonStats).Files;
        return files.Select((p, i) => p.Length < 8
            ? throw new InvalidDataException("Entrada de estadísticas Gen4 truncada: " + i)
            : new Gen5BaseStats(i, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7])).ToArray();
    }

    /// <summary>Bytes per team member: 6, plus the held item and custom moves when flagged, plus 2 in Platinum and HeartGold.</summary>
    public int TeamEntrySize(int flags) => 6 + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0) + (Profile.Kind == Gen4Kind.DiamondPearl ? 0 : 2);

    public IReadOnlyList<Gen5Trainer> Trainers()
    {
        var headers = Narc(Gen4Profile.TrainerData).Files;
        var teams = Narc(Gen4Profile.TrainerPokemon).Files;
        if (headers.Count != teams.Count)
            throw new InvalidDataException("Las tablas de entrenadores y equipos Gen4 no tienen el mismo número de entradas.");
        var names = Text(Profile.TrainerNamesText);
        var classes = Text(Profile.TrainerClassesText);
        var trainers = new List<Gen5Trainer>();
        // Entry 0 is a placeholder; name line N belongs to trainer N.
        for (int id = 1; id < headers.Count; id++)
        {
            byte[] header = headers[id], team = teams[id];
            if (header.Length < 20)
                throw new InvalidDataException("Cabecera de entrenador Gen4 truncada: " + id);
            int flags = header[0], count = header[3], size = TeamEntrySize(flags);
            if (count > 6 || team.Length < count * size)
                throw new InvalidDataException("Equipo de entrenador Gen4 inválido: " + id);
            var pokemon = new List<Gen5TrainerPokemon>(count);
            for (int slot = 0; slot < count; slot++)
            {
                int at = slot * size;
                int item = (flags & 2) != 0 ? BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 6)) : 0;
                int moves = at + 6 + ((flags & 2) != 0 ? 2 : 0);
                int[] moveIds = (flags & 1) != 0 ? Enumerable.Range(0, 4).Select(m => (int)BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(moves + m * 2))).ToArray() : [];
                int value = BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 4));
                pokemon.Add(new Gen5TrainerPokemon(value & 0x3FF, value >> 10, BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at + 2)),
                    team[at], team[at + 1] >> 4, team[at + 1] & 0xF, item, moveIds));
            }
            trainers.Add(new Gen5Trainer(id, flags, header[1], header[16], id < names.Count ? names[id] : "",
                header[1] < classes.Count ? classes[header[1]] : "", pokemon));
        }
        return trainers;
    }

    /// <summary>
    /// Every wild species slot of the main encounter tables (grass and its time, swarm, radar and dual-slot replacements, water,
    /// rods, Rock Smash and the HeartGold radio). Feebas tiles, honey trees, Great Marsh, Trophy Garden and headbutt trees are not read.
    /// </summary>
    public IReadOnlyList<Gen4EncounterSlot> Encounters()
    {
        var files = Narc(Gen4Profile.WildPokemon).Files;
        var slots = new List<Gen4EncounterSlot>();
        bool hgss = Profile.Kind == Gen4Kind.HeartGoldSoulSilver;
        for (int file = 0; file < files.Count; file++)
        {
            byte[] data = files[file];
            // Romhacks may cut tables off the end of a file (Sacred Gold, Renegade Platinum): only the tables that fit are read.
            int expected = hgss ? HgssLength : DpptLength, minimum = hgss ? 100 : 204;
            if (!_requireOriginalTables ? data.Length < minimum || data.Length > expected : data.Length != expected)
                throw new InvalidDataException("Tabla de encuentros Gen4 con tamaño inesperado: " + file);
            if (hgss)
                ReadHgss(file, data, slots);
            else
                ReadDppt(file, data, slots);
        }
        return slots;
    }

    private static void ReadDppt(int file, byte[] data, List<Gen4EncounterSlot> slots)
    {
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
        if (U32(0) != 0)
        {
            var levels = new int[12];
            for (int i = 0; i < 12; i++)
            {
                levels[i] = (int)U32(4 + i * 8);
                slots.Add(new Gen4EncounterSlot(file, "Hierba/cueva", i, 8 + i * 8, 4, (int)U32(8 + i * 8), levels[i], levels[i]));
            }
            for (int i = 0; i < 20; i++)
            {
                int at = 100 + i * 4 + (i >= 10 ? 24 : 0), level = levels[DpptAlternateSlots[i]];
                if (U32(at) is >= 1 and <= MaxSpecies)
                    slots.Add(new Gen4EncounterSlot(file, "Hierba/cueva · " + DpptAlternateNames[i], i, at, 4, (int)U32(at), level, level));
            }
        }
        int offset = 204;
        for (int set = 0; set < 5 && offset + 44 <= data.Length; set++, offset += 44)
        {
            if (U32(offset) == 0 || set == 1)
                continue;
            for (int i = 0; i < 5; i++)
            {
                uint level = U32(offset + 4 + i * 8);
                slots.Add(new Gen4EncounterSlot(file, DpptWaterNames[set], i, offset + 8 + i * 8, 4, (int)U32(offset + 8 + i * 8), (int)(level >> 8) & 0xFF, (int)level & 0xFF));
            }
        }
    }

    private static void ReadHgss(int file, byte[] data, List<Gen4EncounterSlot> slots)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int grassTop = Enumerable.Range(0, 12).Max(i => data[8 + i]);
        if (data[0] != 0)
        {
            string[] times = ["Mañana", "Día", "Noche"];
            for (int time = 0; time < 3; time++)
                for (int i = 0; i < 12; i++)
                    slots.Add(new Gen4EncounterSlot(file, "Hierba/cueva · " + times[time], i, 20 + time * 24 + i * 2, 2, U16(20 + time * 24 + i * 2), data[8 + i], data[8 + i]));
        }
        // Radio and swarm replacements have no level of their own; they appear at grass levels.
        for (int i = 0; i < 4; i++)
            if (U16(92 + i * 2) is >= 1 and <= MaxSpecies)
                slots.Add(new Gen4EncounterSlot(file, i < 2 ? "Radio Hoenn" : "Radio Sinnoh", i % 2, 92 + i * 2, 2, U16(92 + i * 2), grassTop, grassTop));
        int offset = 100;
        for (int set = 1; set < 6 && offset + 4 * HgssAmounts[set] <= data.Length; set++)
        {
            for (int i = 0; data[set] != 0 && i < HgssAmounts[set]; i++)
            {
                int at = offset + i * 4;
                slots.Add(new Gen4EncounterSlot(file, HgssNonGrassNames[set], i, at + 2, 2, U16(at + 2), data[at], data[at + 1]));
            }
            offset += 4 * HgssAmounts[set];
        }
        string[] extras = ["Enjambre", "Enjambre", "Pesca nocturna", "Enjambre de pesca"];
        for (int i = 0; i < 4 && offset + i * 2 + 2 <= data.Length; i++)
            if (U16(offset + i * 2) is >= 1 and <= MaxSpecies)
                slots.Add(new Gen4EncounterSlot(file, extras[i], i, offset + i * 2, 2, U16(offset + i * 2), grassTop, grassTop));
    }
}
