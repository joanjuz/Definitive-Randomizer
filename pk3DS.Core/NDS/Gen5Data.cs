#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>Where each Gen5 table lives in a given game, with the CRC32 its NARC must have (UPR-ZX gen5_offsets.ini, GPLv3).</summary>
public sealed record Gen5Profile(string GameCode, bool Black, IReadOnlyDictionary<string, (string Path, uint Crc32)> Files,
    int SpeciesNamesText, int TrainerNamesText, int TrainerClassesText, IReadOnlyList<(int File, int Offset)>[] StarterOffsets)
{
    public const string TextStrings = "TextStrings", PokemonStats = "PokemonStats", Scripts = "Scripts",
        TrainerData = "TrainerData", TrainerPokemon = "TrainerPokemon", WildPokemon = "WildPokemon";

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
    };

    public static IReadOnlyList<Gen5Profile> All { get; } =
    [
        new("IRBO", true, BlackFiles(), 70, 190, 191, BlackWhiteStarters),
        // Spanish White: White's encounters plus its own text and scripts; the remaining tables match Black (U).
        new("IRAS", false, new Dictionary<string, (string, uint)>(BlackFiles())
        {
            [TextStrings] = ("a/0/0/2", 0xDCFE58C9),
            [Scripts] = ("a/0/5/7", 0x7638D5C9),
            [WildPokemon] = ("a/1/2/6", 0x01FCA7BB),
        }, 70, 190, 191, BlackWhiteStarters),
    ];
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

/// <summary>Read-only access to Black/White data. Every table is checked against its profile CRC32 before it is parsed.</summary>
public sealed class Gen5Data
{
    private const int SeasonLength = 232;
    private static readonly int[] SlotsPerType = [12, 12, 12, 5, 5, 5, 5];
    private readonly NdsRom _rom;
    private readonly Dictionary<string, Narc> _narcs = new();

    public NdsIdentityResult Identity { get; }
    public Gen5Profile Profile { get; }

    private Gen5Data(NdsRom rom, NdsIdentityResult identity, Gen5Profile profile)
    {
        _rom = rom;
        Identity = identity;
        Profile = profile;
    }

    public static Gen5Data Open(NdsRom rom)
    {
        var identity = NdsIdentity.Identify(rom);
        var profile = Gen5Profile.All.FirstOrDefault(p => p.GameCode == identity.Profile.GameCode)
            ?? throw new NotSupportedException($"{identity.Profile.Game} todavía no tiene perfil de datos Gen5.");
        var data = new Gen5Data(rom, identity, profile);
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
        uint actual = Crc32(bytes);
        if (actual != crc)
            throw new InvalidDataException($"{path} ({key}) no coincide con el perfil de {Profile.GameCode}: CRC32 {actual:X8}, se esperaba {crc:X8}.");
        return _narcs[key] = NDS.Narc.Read(bytes);
    }

    public IReadOnlyList<string> Text(int file) => Gen5Text.Read(Narc(Gen5Profile.TextStrings).Files[file]);

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
