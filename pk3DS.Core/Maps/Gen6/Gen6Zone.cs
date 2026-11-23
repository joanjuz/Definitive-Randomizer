#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.Maps.Gen6;

/// <summary>
/// One Gen6 zone ("ZO" pack of the zone GARC). Part 0 is the 0x38-byte zone
/// header, part 1 the overworld entities followed by their script, part 2 the
/// zone script. Layouts follow the pk3DS OWSE research and were checked on
/// every zone of X and Omega Ruby copies (see MAPAS_3DS_GEN6.md).
/// </summary>
public sealed class Gen6Zone
{
    public const int HeaderSize = 0x38;
    public Gen6Pack Pack { get; }
    public int Matrix { get; }
    /// <summary>Area data (textures shared by the zone's blocks), u16 at 0x02.</summary>
    public int Area { get; }
    public int ParentMap { get; }
    public Gen6ZoneEntities Entities { get; }

    public Gen6Zone(byte[] data)
    {
        Pack = Gen6Pack.Read(data, "ZO");
        if (Pack.Count < 3) throw new MapFormatException($"Zona con {Pack.Count} partes.");
        var head = Pack.Part(0);
        if (head.Length < HeaderSize) throw new MapFormatException("Cabecera de zona demasiado corta.");
        Area = BinaryPrimitives.ReadUInt16LittleEndian(head[2..]);
        Matrix = BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        ParentMap = BinaryPrimitives.ReadUInt16LittleEndian(head[0x1C..]) & 0x3FF;
        Entities = new Gen6ZoneEntities(Pack.Raw, Pack.Start(1), Pack.Length(1));
    }
}

/// <summary>In-place edits of a decompressed zone file. Only verified fields are written.</summary>
public static class Gen6ZoneEdit
{
    public const int NpcX = 0x28, NpcY = 0x2A;

    /// <summary>
    /// Copy of <paramref name="zone"/> with NPC <paramref name="index"/> on cell (x, y).
    /// Only the two u16 position fields change; the wander area (0x14–0x1A) is relative and is kept.
    /// </summary>
    public static byte[] MoveNpc(byte[] zone, int index, int x, int y)
    {
        if (x is < 0 or > 0xFFFF || y is < 0 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(x), "Casilla fuera del rango de 16 bits.");
        var parsed = new Gen6Zone(zone);
        var record = parsed.Entities.Records.FirstOrDefault(r => r.Kind == MapEventKind.Npc && r.Index == index);
        if (record.Size == 0) throw new ArgumentOutOfRangeException(nameof(index), $"La zona no tiene NPC {index}.");
        var output = (byte[])zone.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(record.Offset + NpcX), (ushort)x);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(record.Offset + NpcY), (ushort)y);
        int changed = 0;
        for (int i = 0; i < zone.Length; i++)
            if (zone[i] != output[i] && (i < record.Offset + NpcX || i >= record.Offset + NpcY + 2)) changed++;
        if (changed != 0) throw new InvalidOperationException("La escritura tocó bytes fuera de la posición del NPC.");
        _ = new Gen6Zone(output); // still a valid zone
        return output;
    }

    public const int NpcFacing = 0x0C, NpcSight = 0x0E;

    /// <summary>
    /// Copy of <paramref name="zone"/> with the facing (u16 0x0C, 0–3) and, when given, the sight range
    /// (u16 0x0E) of NPC <paramref name="index"/>. Nothing else may change.
    /// </summary>
    public static byte[] SetNpcView(byte[] zone, int index, int facing, int? sight)
    {
        if (facing is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(facing), "Solo se escriben las cuatro direcciones verificadas (0–3).");
        if (sight is < 0 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(sight), "Alcance fuera del rango de 16 bits.");
        var parsed = new Gen6Zone(zone);
        var record = parsed.Entities.Records.FirstOrDefault(r => r.Kind == MapEventKind.Npc && r.Index == index);
        if (record.Size == 0) throw new ArgumentOutOfRangeException(nameof(index), $"La zona no tiene NPC {index}.");
        var output = (byte[])zone.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(record.Offset + NpcFacing), (ushort)facing);
        if (sight is int s) BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(record.Offset + NpcSight), (ushort)s);
        for (int i = 0; i < zone.Length; i++)
            if (zone[i] != output[i] && (i < record.Offset + NpcFacing || i >= record.Offset + NpcSight + 2))
                throw new InvalidOperationException("La escritura tocó bytes fuera de la orientación y la visión del NPC.");
        _ = new Gen6Zone(output);
        return output;
    }

    public const int NpcId = 0x00, NpcModel = 0x02, NpcMovement = 0x04, NpcFlag = 0x08, NpcScript = 0x0A;

    /// <summary>
    /// Copy of <paramref name="zone"/> with one more NPC after the last one. The record is
    /// <paramref name="template"/> with its ID set to the new index (ID equals the index in every zone of
    /// X and Alpha Sapphire), its facing and its cell. The NPC count, the declared length of the entity
    /// table and the offsets of the later parts grow by one record; every other byte is kept, the
    /// entity script included (it follows the records and is only moved).
    /// </summary>
    public static byte[] AddNpc(byte[] zone, ReadOnlySpan<byte> template, int x, int y, int facing)
    {
        const int size = Gen6ZoneEntities.NpcSize;
        if (template.Length != size) throw new ArgumentException("La plantilla de NPC debe tener 0x30 bytes.", nameof(template));
        if (x is < 0 or > 0xFFFF || y is < 0 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(x), "Casilla fuera del rango de 16 bits.");
        if (facing is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(facing), "Solo se escriben las cuatro direcciones verificadas (0–3).");
        var parsed = new Gen6Zone(zone);
        var records = parsed.Entities.Records;
        int props = records.Count(r => r.Kind == MapEventKind.Prop), count = records.Count(r => r.Kind == MapEventKind.Npc);
        if (count >= byte.MaxValue) throw new InvalidOperationException("La zona ya tiene el máximo de NPC que admite su recuento.");
        var pack = parsed.Pack;
        int table = pack.Start(1);
        int insert = table + Gen6ZoneEntities.HeaderSize + (props * Gen6ZoneEntities.PropSize) + (count * size);

        var output = new byte[zone.Length + size];
        zone.AsSpan(0, insert).CopyTo(output);
        var record = output.AsSpan(insert, size);
        template.CopyTo(record);
        BinaryPrimitives.WriteUInt16LittleEndian(record[NpcId..], (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(record[NpcFacing..], (ushort)facing);
        BinaryPrimitives.WriteUInt16LittleEndian(record[NpcX..], (ushort)x);
        BinaryPrimitives.WriteUInt16LittleEndian(record[NpcY..], (ushort)y);
        zone.AsSpan(insert).CopyTo(output.AsSpan(insert + size));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(table), BinaryPrimitives.ReadInt32LittleEndian(zone.AsSpan(table)) + size);
        output[table + 5] = (byte)(count + 1);
        for (int i = 2; i <= pack.Count; i++)
        {
            int at = 4 + (i * 4);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at), BinaryPrimitives.ReadInt32LittleEndian(zone.AsSpan(at)) + size);
        }

        // Re-read and compare: the old bytes must be exactly the new ones without the inserted record,
        // apart from the length, the NPC count and the offsets of the parts after the table.
        var reread = new Gen6Zone(output);
        bool Header(int i) => (i >= table && i < table + 4) || i == table + 5 || (i >= 12 && i < 4 + ((pack.Count + 1) * 4));
        for (int i = 0; i < zone.Length; i++)
        {
            int j = i < insert ? i : i + size;
            if (zone[i] != output[j] && !Header(i)) throw new InvalidOperationException("La inserción cambió bytes ajenos al NPC nuevo.");
        }
        if (reread.Pack.Count != pack.Count || reread.Entities.Records.Count != records.Count + 1)
            throw new InvalidOperationException("La relectura de la zona no tiene la estructura esperada.");
        foreach (var r in records)
        {
            var again = reread.Entities.Records.First(n => n.Kind == r.Kind && n.Index == r.Index);
            if (!parsed.Entities.Bytes(r).SequenceEqual(reread.Entities.Bytes(again))) throw new InvalidOperationException($"La relectura cambió {r.Kind} {r.Index}.");
        }
        for (int p = 0; p < pack.Count; p++)
            if (p != 1 && !pack.Part(p).SequenceEqual(reread.Pack.Part(p))) throw new InvalidOperationException($"La inserción cambió la parte {p} de la zona.");
        return output;
    }
}

/// <summary>A Pokémon Center nurse copied from the game itself.</summary>
/// <param name="Zone">Zone the record was copied from.</param>
/// <param name="Index">NPC index in that zone.</param>
/// <param name="Model">Character model of the nurse.</param>
/// <param name="Copies">Nurses with this script found in the game (all with the same model and record shape).</param>
public sealed record Gen6NurseTemplate(int Zone, int Index, int Model, byte[] Record, int Copies);

/// <summary>
/// Finds the Pokémon Center nurse of a Gen6 game. Its talk script is <see cref="Script"/>: the engine
/// sends script IDs 2100–2199 to one common script (X: <c>a/0/3/1</c> #43; Alpha Sapphire: <c>a/0/2/9</c> #63)
/// whose case 2100 is the whole nurse sequence: greeting by time of day, yes/no, healing and farewell.
/// Checked statically on X and Alpha Sapphire (code.bin range table and the common script); 16–17
/// nurses per game use it. Nothing here proves the sequence outside a Pokémon Center.
/// </summary>
public static class Gen6Nurse
{
    public const int Script = 2100;

    /// <summary>
    /// First nurse found, or null when none exists or the nurses disagree on model or on the fields
    /// that are copied unchanged (movement, flag), so a modified game is not guessed.
    /// </summary>
    public static Gen6NurseTemplate? Find(IEnumerable<(int Id, Gen6Zone Zone)> zones)
    {
        Gen6NurseTemplate? first = null;
        int copies = 0;
        foreach (var (id, zone) in zones)
        {
            var e = zone.Entities;
            foreach (var r in e.Records)
            {
                if (r.Kind != MapEventKind.Npc || e.U16(r, Gen6ZoneEdit.NpcScript) != Script) continue;
                int model = e.U16(r, Gen6ZoneEdit.NpcModel);
                if (e.U16(r, Gen6ZoneEdit.NpcMovement) != 0 || e.U16(r, Gen6ZoneEdit.NpcFlag) != 0) return null;
                if (first is null) first = new Gen6NurseTemplate(id, r.Index, model, e.Bytes(r).ToArray(), 0);
                else if (first.Model != model) return null;
                copies++;
            }
        }
        return first is null ? null : first with { Copies = copies };
    }
}

/// <summary>
/// Character models an area loads: X/Y <c>a/1/4/9</c>, OR/AS <c>a/1/3/7</c>, one member per area (the u16 at 0x02 of the
/// zone header). Each entry is 24 bytes with the model in its first u16. In every zone of X and Alpha Sapphire the
/// models of the NPCs are in the list of the zone's area; an NPC whose model is missing there makes the game freeze
/// when the zone loads (seen with a nurse added to Route 2 of X). «Enable Catch Zones» adds the nurse entry the same way.
/// </summary>
public static class Gen6AreaModels
{
    public const int EntrySize = 24;

    public static IReadOnlyList<int> Models(ReadOnlySpan<byte> list)
    {
        if (list.Length % EntrySize != 0) throw new MapFormatException($"Lista de modelos de {list.Length} bytes, no múltiplo de {EntrySize}.");
        var models = new int[list.Length / EntrySize];
        for (int i = 0; i < models.Length; i++) models[i] = BinaryPrimitives.ReadUInt16LittleEndian(list[(i * EntrySize)..]);
        return models;
    }

    /// <summary>The entry of <paramref name="model"/> in the lists, or null when no list has it or two lists disagree.</summary>
    public static byte[]? Entry(IEnumerable<byte[]> lists, int model)
    {
        byte[]? found = null;
        foreach (var list in lists)
        {
            if (list.Length % EntrySize != 0) return null;
            for (int at = 0; at < list.Length; at += EntrySize)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(at)) != model) continue;
                var entry = list.AsSpan(at, EntrySize);
                if (found is null) found = entry.ToArray();
                else if (!entry.SequenceEqual(found)) return null;
            }
        }
        return found;
    }

    /// <summary>Copy of <paramref name="list"/> with <paramref name="entry"/> appended; the existing entries are kept as they are.</summary>
    public static byte[] Add(byte[] list, ReadOnlySpan<byte> entry)
    {
        if (entry.Length != EntrySize) throw new ArgumentException("La entrada de modelo debe tener 24 bytes.", nameof(entry));
        int model = BinaryPrimitives.ReadUInt16LittleEndian(entry);
        if (Models(list).Contains(model)) throw new InvalidOperationException($"El área ya carga el modelo {model}.");
        var output = new byte[list.Length + EntrySize];
        list.CopyTo(output, 0);
        entry.CopyTo(output.AsSpan(list.Length));
        return output;
    }
}

/// <summary>Location of one fixed-size entity record inside the zone file.</summary>
public readonly record struct Gen6EntityRecord(MapEventKind Kind, int Index, int Offset, int Size);

/// <summary>
/// Entity table: u32 length (bytes after this field up to the script),
/// u8 counts of props, NPCs, warps and triggers, three bytes of padding
/// (zero in every checked zone), u32 count of zone triggers, then the
/// fixed-size records. The script that follows is not interpreted.
/// </summary>
public sealed class Gen6ZoneEntities
{
    public const int PropSize = 0x14, NpcSize = 0x30, WarpSize = 0x18, TriggerSize = 0x18;
    public const int HeaderSize = 0x0C;
    private readonly byte[] _raw;
    public IReadOnlyList<Gen6EntityRecord> Records { get; }

    public Gen6ZoneEntities(byte[] raw, int start, int length)
    {
        _raw = raw;
        var span = raw.AsSpan(start, length);
        if (span.Length < HeaderSize) throw new MapFormatException("Tabla de entidades demasiado corta.");
        int declared = BinaryPrimitives.ReadInt32LittleEndian(span);
        int props = span[4], npcs = span[5], warps = span[6], triggers = span[7];
        int zoneTriggers = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        if (zoneTriggers < 0 || zoneTriggers > 255) throw new MapFormatException($"Recuento de activadores de zona {zoneTriggers} no válido.");
        int body = (props * PropSize) + (npcs * NpcSize) + (warps * WarpSize) + ((triggers + zoneTriggers) * TriggerSize);
        if (declared != HeaderSize - 4 + body)
            throw new MapFormatException($"La longitud declarada {declared} no coincide con los recuentos ({HeaderSize - 4 + body}).");
        if (HeaderSize + body > span.Length) throw new MapFormatException("Las entidades no caben en la zona.");
        var list = new List<Gen6EntityRecord>();
        int offset = start + HeaderSize;
        void Add(MapEventKind kind, int count, int size)
        {
            for (int i = 0; i < count; i++, offset += size) list.Add(new Gen6EntityRecord(kind, i, offset, size));
        }
        Add(MapEventKind.Prop, props, PropSize);
        Add(MapEventKind.Npc, npcs, NpcSize);
        Add(MapEventKind.Warp, warps, WarpSize);
        Add(MapEventKind.Trigger, triggers, TriggerSize);
        Add(MapEventKind.ZoneTrigger, zoneTriggers, TriggerSize);
        Records = list;
    }

    public ReadOnlySpan<byte> Bytes(Gen6EntityRecord record) => _raw.AsSpan(record.Offset, record.Size);

    public ushort U16(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(r)[at..]);
    internal short S16(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadInt16LittleEndian(Bytes(r)[at..]);
    internal float F32(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadSingleLittleEndian(Bytes(r)[at..]);
}
