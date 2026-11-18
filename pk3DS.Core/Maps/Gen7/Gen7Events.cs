#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using pk3DS.Core.Maps.Gen6;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>A static object placed in the area (ED part 4).</summary>
public sealed record Gen7StaticPlacement(int Zone, int Index, Vector3 Position, Quaternion Rotation, int Model);

/// <summary>One fixed-size event record: zone, row and its bytes.</summary>
public sealed class Gen7Record(int index, byte[] bytes)
{
    public int Index { get; } = index;
    public byte[] Bytes { get; } = bytes;
    public uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(at));
    public ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(at));
    public float F(int at) => BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(at));
    public Vector3 Position => new(F(4), F(8), F(12));
}

/// <summary>
/// Gen7 area event pack ("ED", slot 0 of the area). Each part is a pack with one file per
/// zone of the area; each file is a u32 count, fixed-size records whose first u32 is the
/// record type, then a data tail (shapes) addressed by offsets from the file start.
/// Layout cross-checked against USUM-Studio (Aqua-0, GPL-3.0) and real Ultra Moon data.
/// </summary>
public static class Gen7Events
{
    public const int TriggerPack = 0, NpcPack = 1, WarpPack = 2, InteractionPack = 3, StaticPack = 4, TrainerPack = 7, PickupPack = 10;
    public const int StaticSize = 56, StaticType = 4;

    /// <summary>Record layout of one event kind: ED part, record size, type value and optional field offsets.</summary>
    public sealed record Layout(MapEventKind Kind, string Name, int Pack, int Size, int Type, int Model, int Script);

    /// <summary>Kinds shown on the map. Event id is the u32 at 0x2C for all of them except triggers.</summary>
    public static readonly IReadOnlyList<Layout> Layouts =
    [
        new(MapEventKind.Npc, "NPC", NpcPack, 120, 1, 52, 56),
        new(MapEventKind.Npc, "Entrenador", TrainerPack, 84, 7, 48, 52),
        new(MapEventKind.Warp, "Warp", WarpPack, 88, 3, -1, -1),
        new(MapEventKind.Prop, "Interacción", InteractionPack, 60, 2, -1, 48),
        new(MapEventKind.Prop, "Objeto recogible", PickupPack, 64, 10, -1, -1),
        new(MapEventKind.Trigger, "Activador", TriggerPack, 60, 0, -1, 44),
    ];

    /// <summary>Any two-letter BinLinker container.</summary>
    public static Gen6Pack? Container(byte[] data)
    {
        if (data.Length < 8 || data[0] < 'A' || data[0] > 'Z' || data[1] < 'A' || data[1] > 'Z') return null;
        return Gen6Pack.Read(data, Encoding.ASCII.GetString(data, 0, 2));
    }

    public static IEnumerable<Gen7Record> Records(byte[] zone, int size, int type)
    {
        if (zone.Length < 4) yield break;
        int count = BinaryPrimitives.ReadInt32LittleEndian(zone);
        if (count < 0 || count > 4096 || 4 + ((long)count * size) > zone.Length) throw new MapFormatException($"Recuento de eventos {count} no válido.");
        for (int i = 0; i < count; i++)
        {
            var r = new Gen7Record(i, zone.AsSpan(4 + (i * size), size).ToArray());
            if (r.U32(0) != (uint)type) throw new MapFormatException($"Registro de tipo {r.U32(0)} donde se esperaba {type}.");
            yield return r;
        }
    }
}
