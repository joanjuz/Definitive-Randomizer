#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

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
    public int ParentMap { get; }
    public Gen6ZoneEntities Entities { get; }

    public Gen6Zone(byte[] data)
    {
        Pack = Gen6Pack.Read(data, "ZO");
        if (Pack.Count < 3) throw new MapFormatException($"Zona con {Pack.Count} partes.");
        var head = Pack.Part(0);
        if (head.Length < HeaderSize) throw new MapFormatException("Cabecera de zona demasiado corta.");
        Matrix = BinaryPrimitives.ReadUInt16LittleEndian(head[4..]);
        ParentMap = BinaryPrimitives.ReadUInt16LittleEndian(head[0x1C..]) & 0x3FF;
        Entities = new Gen6ZoneEntities(Pack.Raw, Pack.Start(1), Pack.Length(1));
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

    internal ushort U16(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(r)[at..]);
    internal short S16(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadInt16LittleEndian(Bytes(r)[at..]);
    internal float F32(Gen6EntityRecord r, int at) => BinaryPrimitives.ReadSingleLittleEndian(Bytes(r)[at..]);
}
