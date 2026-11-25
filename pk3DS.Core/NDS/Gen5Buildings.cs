#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>How a game chooses the building pack of an area (see <see cref="Gen5BuildingProfile"/>).</summary>
public enum Gen5PackRule
{
    /// <summary>Black 2 / White 2: field +0 of the area record (ds-pokemon-hacking, "B2W2 Area Data"; checked on 487 of 491 maps with buildings).</summary>
    AreaTable,
    /// <summary>
    /// Black / White: field +0 of their area record is not a pack. The pack is the area's season group — exterior (area − 2) / 4, interior area − (2 + 4 ×
    /// exterior packs). Deduced by comparison: 334 of 335 maps with buildings of Black and White, the only pack holding every building of 52 areas, and
    /// the rule White 2 stores in its own table for all of its 280 seasonal areas. No table holding it was found in the data or the code.
    /// </summary>
    SeasonGroups,
}

/// <summary>
/// Where a Gen 5 game keeps its buildings, with the fingerprints of the original archives (measured on the owner's dumps). Kept apart from
/// <see cref="Gen5MapProfile"/>: Black and White share their map tables but not their building archives (Black City and White Forest).
/// <list type="bullet">
/// <item>Area table (a/0/1/3, not a NARC): 10 bytes per area, +0 u16 building pack (Black 2 / White 2 only), +2 u16 terrain texture set (a/0/1/4; equal
/// to the area index in every area of the three games), +6 u8 environment (0 interior, 1 exterior: which pack and texture archives are used).</item>
/// <item>Building packs (AB): u16 "AB", u16 entry count, u32 offsets and the u32 end. The first half are descriptors (u16 building id at +0, then
/// animation data), the second half the BMD0 model of each descriptor, in the same order. Models have no textures of their own: pack k is textured by
/// member k of the matching texture archive (every textured material of the 10 695 models binds there).</item>
/// <item>Seasons: areas 2 … 2 + 4 × exterior packs − 1 come in groups of four (spring, summer, autumn, winter); a header names the spring area and the
/// game adds the season (ds-pokemon-hacking documents it for White 2, where the end is 0x11A = 2 + 4 × 70). In 49 of the 52 groups of each game the
/// four texture sets have the same texture names with different pixels.</item>
/// </list>
/// </summary>
public sealed record Gen5BuildingProfile(
    string GameCode, byte Revision, Gen5PackRule Rule,
    string ExteriorPacksPath, string InteriorPacksPath, string ExteriorTexturesPath, string InteriorTexturesPath,
    int AreaCount, int ExteriorPackCount, int InteriorPackCount,
    uint AreasCrc32, uint ExteriorPacksCrc32, uint InteriorPacksCrc32, uint ExteriorTexturesCrc32, uint InteriorTexturesCrc32)
{
    public const string AreasPath = "a/0/1/3";
    public const int AreaSize = 10, FirstSeasonalArea = 2, Seasons = 4;
    /// <summary>First area after the seasonal ones (White 2: 0x11A, as documented).</summary>
    public int SeasonalAreaEnd => FirstSeasonalArea + Seasons * ExteriorPackCount;

    public static Gen5BuildingProfile Black { get; } = new("IRBO", 0, Gen5PackRule.SeasonGroups, "a/2/2/9", "a/2/3/0", "a/1/7/6", "a/1/7/7",
        282, 52, 64, 0x556D0385, 0x38D38A05, 0x67DDB110, 0xBC360449, 0x07ECFE87);
    /// <summary>White IRAS/0: Black's area table and interiors; its own exterior packs and textures.</summary>
    public static Gen5BuildingProfile White { get; } = new("IRAS", 0, Gen5PackRule.SeasonGroups, "a/2/2/9", "a/2/3/0", "a/1/7/6", "a/1/7/7",
        282, 52, 64, 0x556D0385, 0x42EE4F03, 0x67DDB110, 0x23D66E7F, 0x07ECFE87);
    public static Gen5BuildingProfile White2 { get; } = new("IRDO", 0, Gen5PackRule.AreaTable, "a/2/2/5", "a/2/2/6", "a/1/7/4", "a/1/7/5",
        409, 70, 93, 0x149DE4BD, 0x12455102, 0x16498A06, 0x620D5A09, 0x0F7A4CFB);

    public static IReadOnlyList<Gen5BuildingProfile> All { get; } = [Black, White, White2];
    public static Gen5BuildingProfile? For(string gameCode, int revision) => All.FirstOrDefault(p => p.GameCode == gameCode && p.Revision == revision);

    /// <summary>
    /// The pack of an area, and whether it is an interior one; null when the rule gives no existing pack (nothing is drawn then). <paramref name="area"/>
    /// is the area actually loaded (the header's spring area plus the season).
    /// </summary>
    public (int Pack, bool Interior)? Pack(byte[] areas, int area)
    {
        if (area < 0 || area >= AreaCount || areas.Length != AreaCount * AreaSize) return null;
        bool interior = areas[area * AreaSize + 6] == 0;
        int pack = Rule switch
        {
            Gen5PackRule.AreaTable => BinaryPrimitives.ReadUInt16LittleEndian(areas.AsSpan(area * AreaSize)),
            _ => interior ? area - SeasonalAreaEnd : area >= FirstSeasonalArea && area < SeasonalAreaEnd ? (area - FirstSeasonalArea) / Seasons : -1,
        };
        return pack >= 0 && pack < (interior ? InteriorPackCount : ExteriorPackCount) ? (pack, interior) : null;
    }
    /// <summary>The area loaded in <paramref name="season"/> (0 spring … 3 winter): seasonal areas shift by the season, the others never change.</summary>
    public int SeasonalArea(int springArea, int season)
    {
        if (season is < 0 or >= Seasons) throw new ArgumentOutOfRangeException(nameof(season));
        return IsSeasonal(springArea) ? springArea + season : springArea;
    }
    /// <summary>Whether the area is the spring member of a season group (the only kind headers name).</summary>
    public bool IsSeasonal(int area) => area >= FirstSeasonalArea && area < SeasonalAreaEnd && (area - FirstSeasonalArea) % Seasons == 0;
}

/// <summary>One building of a map block: position relative to the block centre in fx32 map units (1.19.12; 16 units per tile), counter-clockwise yaw (0–65535 per turn) and the building id.</summary>
public sealed record Gen5Building(int Index, int RawX, int RawY, int RawZ, int Yaw, int Id)
{
    public const int Size = 16;
    /// <summary>The building table of a block (last section): u32 count, then 16 bytes per building. The id is the only big-endian field.</summary>
    public static IReadOnlyList<Gen5Building> Read(byte[] table)
    {
        if (table.Length < 4) throw new InvalidDataException("Tabla de edificios Gen5 truncada.");
        long count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (table.Length != 4 + count * Size) throw new InvalidDataException("La tabla de edificios Gen5 no tiene el tamaño de sus registros.");
        var list = new Gen5Building[count];
        for (int i = 0; i < count; i++)
        {
            var r = table.AsSpan(4 + i * Size, Size);
            list[i] = new(i, BinaryPrimitives.ReadInt32LittleEndian(r), BinaryPrimitives.ReadInt32LittleEndian(r[4..]), BinaryPrimitives.ReadInt32LittleEndian(r[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(r[12..]), BinaryPrimitives.ReadUInt16BigEndian(r[14..]));
        }
        return list;
    }
}

/// <summary>A building pack (AB): the BMD0 model of each building id. Ids listed twice must have identical descriptors and models, otherwise they are ambiguous and never resolved.</summary>
public sealed class Gen5BuildingPack
{
    private readonly Dictionary<int, byte[]> _models = new();
    private readonly HashSet<int> _ambiguous = new();
    public int Count { get; }
    public IReadOnlyCollection<int> Ids => _models.Keys;

    public Gen5BuildingPack(byte[] pack)
    {
        if (pack.Length < 8 || pack[0] != 'A' || pack[1] != 'B') throw new InvalidDataException("Paquete de edificios Gen5 sin la firma AB.");
        int n = BinaryPrimitives.ReadUInt16LittleEndian(pack.AsSpan(2));
        if (n % 2 != 0 || pack.Length < 4 + 4 * (n + 1)) throw new InvalidDataException("Paquete de edificios Gen5 con un número impar de entradas.");
        var offsets = Enumerable.Range(0, n + 1).Select(i => (int)BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(4 + 4 * i))).ToArray();
        if (offsets[0] != 4 + 4 * (n + 1) || offsets[n] != pack.Length || offsets.Zip(offsets.Skip(1)).Any(p => p.Second < p.First)) throw new InvalidDataException("Entradas del paquete de edificios Gen5 fuera de orden.");
        byte[] Entry(int i) => pack.AsSpan(offsets[i], offsets[i + 1] - offsets[i]).ToArray();
        Count = n / 2; var descriptors = new Dictionary<int, byte[]>();
        for (int i = 0; i < Count; i++)
        {
            byte[] descriptor = Entry(i), model = Entry(Count + i);
            if (descriptor.Length < 2 || model.Length < 4 || model[0] != 'B' || model[1] != 'M' || model[2] != 'D' || model[3] != '0') throw new InvalidDataException($"Entrada {i} del paquete de edificios Gen5 sin descriptor o modelo BMD0.");
            int id = BinaryPrimitives.ReadUInt16LittleEndian(descriptor);
            if (_models.TryGetValue(id, out var known))
            {
                if (!known.AsSpan().SequenceEqual(model) || !descriptors[id].AsSpan().SequenceEqual(descriptor)) _ambiguous.Add(id);
                continue;
            }
            _models[id] = model; descriptors[id] = descriptor;
        }
        foreach (int id in _ambiguous) _models.Remove(id);
    }

    /// <summary>The model bytes of a building id, or null when the pack does not hold it (or holds it ambiguously).</summary>
    public byte[]? Model(int id) => _models.GetValueOrDefault(id);
    public bool IsAmbiguous(int id) => _ambiguous.Contains(id);
}

/// <summary>The building archives of one game, pinned by the CRC32 of its <see cref="Gen5BuildingProfile"/>. Read-only.</summary>
public sealed class Gen5BuildingArchives
{
    public Gen5BuildingProfile Profile { get; }
    public byte[] Areas { get; }
    public Narc ExteriorPacks { get; }
    public Narc InteriorPacks { get; }
    public Narc ExteriorTextures { get; }
    public Narc InteriorTextures { get; }

    public static Gen5BuildingArchives Open(NdsRom rom, Gen5MapProfile map)
    {
        var p = Gen5BuildingProfile.For(map.GameCode, map.Revision) ?? throw new InvalidDataException($"Edificios {map.Key}: sin perfil de edificios.");
        byte[] Pinned(string path, uint crc)
        {
            byte[] bytes = rom.ReadFile(path);
            if (Gen5Data.Crc32(bytes) != crc) throw new InvalidDataException($"Edificios {map.Key}: {path} no coincide con el archivo acreditado.");
            return bytes;
        }
        return new(p, Pinned(Gen5BuildingProfile.AreasPath, p.AreasCrc32), Pinned(p.ExteriorPacksPath, p.ExteriorPacksCrc32), Pinned(p.InteriorPacksPath, p.InteriorPacksCrc32),
            Pinned(p.ExteriorTexturesPath, p.ExteriorTexturesCrc32), Pinned(p.InteriorTexturesPath, p.InteriorTexturesCrc32));
    }

    /// <summary>Structural constructor (fixtures and the real ROM): checks the sizes against the profile, not the fingerprints.</summary>
    public Gen5BuildingArchives(Gen5BuildingProfile profile, byte[] areas, byte[] exteriorPacks, byte[] interiorPacks, byte[] exteriorTextures, byte[] interiorTextures)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (areas.Length != profile.AreaCount * Gen5BuildingProfile.AreaSize) throw new InvalidDataException("Tabla de áreas Gen5 de tamaño inesperado.");
        Areas = (byte[])areas.Clone();
        ExteriorPacks = Narc.Read(exteriorPacks); InteriorPacks = Narc.Read(interiorPacks); ExteriorTextures = Narc.Read(exteriorTextures); InteriorTextures = Narc.Read(interiorTextures);
        if (ExteriorPacks.Files.Count != profile.ExteriorPackCount || ExteriorTextures.Files.Count != profile.ExteriorPackCount
            || InteriorPacks.Files.Count != profile.InteriorPackCount || InteriorTextures.Files.Count != profile.InteriorPackCount)
            throw new InvalidDataException("Los paquetes de edificios Gen5 y sus texturas no tienen el número esperado de miembros.");
    }
}

/// <summary>A least-recently-used cache with a fixed number of entries. Not thread-safe: its owner serializes every use.</summary>
internal sealed class BoundedCache<TKey, TValue>(int limit) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    public int Count => _map.Count;
    public TValue Get(TKey key, Func<TValue> create)
    {
        if (_map.TryGetValue(key, out var node)) { _order.Remove(node); _order.AddFirst(node); return node.Value.Value; }
        var value = create();
        _map[key] = _order.AddFirst((key, value));
        while (_map.Count > limit) { var last = _order.Last!; _order.RemoveLast(); _map.Remove(last.Value.Key); }
        return value;
    }
}
