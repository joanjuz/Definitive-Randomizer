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
        WildPokemon = "WildPokemon";

    public string Game => Kind switch { Gen4Kind.DiamondPearl => "Diamante", Gen4Kind.Platinum => "Platino", _ => "HeartGold" };

    public static IReadOnlyList<Gen4Profile> All { get; } =
    [
        new("ADAE", 5, Gen4Kind.DiamondPearl, new Dictionary<string, (string, uint)>
        {
            [Text] = ("msgdata/msg.narc", 0xCC7250FE),
            [PokemonStats] = ("poketool/personal/personal.narc", 0xF963E181),
            [TrainerData] = ("poketool/trainer/trdata.narc", 0x4A3C0C89),
            [TrainerPokemon] = ("poketool/trainer/trpoke.narc", 0xC4AA9026),
            [WildPokemon] = ("fielddata/encountdata/d_enc_data.narc", 0xE58BE3CA),
        }, 362, 559, 560, 344, 565),
        new("CPUE", 1, Gen4Kind.Platinum, new Dictionary<string, (string, uint)>
        {
            [Text] = ("msgdata/pl_msg.narc", 0x5666AAEC),
            [PokemonStats] = ("poketool/personal/pl_personal.narc", 0xB16A8A01),
            [TrainerData] = ("poketool/trainer/trdata.narc", 0x2EE9B886),
            [TrainerPokemon] = ("poketool/trainer/trpoke.narc", 0x739A02EE),
            [WildPokemon] = ("fielddata/encountdata/pl_enc_data.narc", 0x188084F5),
        }, 412, 618, 619, 392, 624),
        new("IPKE", 0, Gen4Kind.HeartGoldSoulSilver, new Dictionary<string, (string, uint)>
        {
            [Text] = ("a/0/2/7", 0x123DEB0B),
            [PokemonStats] = ("a/0/0/2", 0xDE8C5CAF),
            [TrainerData] = ("a/0/5/5", 0x8B9BD9CE),
            [TrainerPokemon] = ("a/0/5/6", 0xE30BC8BE),
            [WildPokemon] = ("a/0/3/7", 0x9B861BD5),
        }, 237, 729, 730, 222, 735),
    ];
}

/// <summary>
/// One wild species slot: where its species is stored (<paramref name="Width"/> 4 in Diamond/Platinum, 2 in HeartGold) and the
/// level band it is met at. Slots that share <paramref name="Group"/> in one file are one encounter table.
/// </summary>
public sealed record Gen4EncounterSlot(int File, string Group, int Index, int Offset, int Width, int Species, int MinLevel, int MaxLevel);

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
    private readonly Dictionary<string, Narc> _narcs = new();

    public NdsIdentityResult Identity { get; }
    public Gen4Profile Profile { get; }

    private Gen4Data(NdsRom rom, NdsIdentityResult identity, Gen4Profile profile, bool requireOriginalTables)
    {
        _rom = rom;
        _requireOriginalTables = requireOriginalTables;
        Identity = identity;
        Profile = profile;
    }

    /// <summary>
    /// Opens a Diamond, Platinum or HeartGold ROM. Editing requires <paramref name="requireOriginalTables"/>: every table must match
    /// its profile CRC32. Passing false only checks the game code, to re-read ROMs this tool exported; never randomize from such a view.
    /// </summary>
    public static Gen4Data Open(NdsRom rom, bool requireOriginalTables = true)
    {
        var identity = NdsIdentity.Identify(rom);
        var profile = Gen4Profile.All.FirstOrDefault(p => p.GameCode == identity.Profile.GameCode && p.Version == identity.Profile.Version)
            ?? throw new NotSupportedException($"{identity.Profile.Game} no tiene perfil de datos Gen4.");
        var data = new Gen4Data(rom, identity, profile, requireOriginalTables);
        if (requireOriginalTables)
            foreach (string key in profile.Files.Keys)
                data.Narc(key);
        return data;
    }

    private Narc Narc(string key)
    {
        if (_narcs.TryGetValue(key, out var cached))
            return cached;
        var (path, crc) = Profile.Files[key];
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
        if (!_requireOriginalTables)
            throw new InvalidOperationException("Solo se exporta desde una ROM con todas sus tablas originales verificadas.");
        if (result.Overlays.Count != 0)
            throw new NotSupportedException("Gen4 todavía no modifica overlays.");
        _rom.Save(output, result.Files.ToDictionary(p => _rom.File(p.Key).Id, p => p.Value));
    }

    public IReadOnlyList<string> Text(int file) => Gen4Text.Read(Narc(Gen4Profile.Text).Files[file]);

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
            if (data.Length != (hgss ? HgssLength : DpptLength))
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
        for (int set = 0; set < 5; set++, offset += 44)
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
        for (int set = 1; set < 6; set++)
        {
            for (int i = 0; data[set] != 0 && i < HgssAmounts[set]; i++)
            {
                int at = offset + i * 4;
                slots.Add(new Gen4EncounterSlot(file, HgssNonGrassNames[set], i, at + 2, 2, U16(at + 2), data[at], data[at + 1]));
            }
            offset += 4 * HgssAmounts[set];
        }
        string[] extras = ["Enjambre", "Enjambre", "Pesca nocturna", "Enjambre de pesca"];
        for (int i = 0; i < 4; i++)
            if (U16(offset + i * 2) is >= 1 and <= MaxSpecies)
                slots.Add(new Gen4EncounterSlot(file, extras[i], i, offset + i * 2, 2, U16(offset + i * 2), grassTop, grassTop));
    }
}
