#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pk3DS.Core.Maps.Gen6;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>
/// In-place edits of a decompressed Gen7 area event pack ("ED"). Nothing changes size: a record
/// is rewritten where it is, and the shapes it points to are translated where they are. Every
/// edit checks afterwards that only the expected bytes changed and that the pack still parses.
/// </summary>
public static class Gen7ZoneEdit
{
    public const int NpcSize = 120, NpcType = 1, NpcTalkShape = 0x70, NpcBodyShape = 0x74;
    private static readonly int[] ShapeSizes = [20, 40, 28, 36];
    private static readonly int[] ShapePointBytes = [12, 12, 24, 36];

    /// <summary>Absolute offset in <paramref name="ed"/> of the file of local zone <paramref name="zone"/> in ED part <paramref name="part"/>.</summary>
    public static (int Start, int Length) ZoneFile(byte[] ed, int part, int zone)
    {
        var pack = Gen6Pack.Read(ed, "ED");
        if (part >= pack.Count) throw new MapFormatException($"El paquete ED no tiene la parte {part}.");
        var inner = Gen7Events.Container(pack.Part(part).ToArray()) ?? throw new MapFormatException($"La parte {part} del ED no es un paquete.");
        if (zone < 0 || zone >= inner.Count) throw new MapFormatException($"La parte {part} no tiene la zona local {zone}.");
        return (pack.Start(part) + inner.Start(zone), inner.Length(zone));
    }

    /// <summary>Shape point ranges (absolute offsets in the zone file) of the shape list at <paramref name="offset"/>.</summary>
    public static List<(int At, int Bytes)> ShapePoints(ReadOnlySpan<byte> file, int offset)
    {
        var list = new List<(int, int)>();
        if (offset == 0) return list;
        if (offset < 4 || offset + 4 > file.Length) throw new MapFormatException("Forma fuera del archivo de zona.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(file[offset..]);
        if (count < 0 || count > 64 || offset + 4 + (count * 4) > file.Length) throw new MapFormatException("Lista de formas no válida.");
        int p = offset + 4 + (count * 4);
        for (int i = 0; i < count; i++)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(file[(offset + 4 + (i * 4))..]);
            if (type is < 0 or > 3) throw new MapFormatException($"Tipo de forma {type} desconocido.");
            if (p + ShapeSizes[type] > file.Length) throw new MapFormatException("Forma truncada.");
            list.Add((p, ShapePointBytes[type]));
            p += ShapeSizes[type];
        }
        return list;
    }

    /// <summary>
    /// Copy of the ED pack with NPC <paramref name="row"/> of local zone <paramref name="zone"/>
    /// moved to <paramref name="position"/>; its talk-range and body shapes move by the same delta.
    /// Refused when a shape is shared with another record of the file.
    /// </summary>
    public static byte[] MoveNpc(byte[] ed, int zone, int row, Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new ArgumentException("Posición no finita.");
        var (start, length) = ZoneFile(ed, Gen7Events.NpcPack, zone);
        var file = ed.AsSpan(start, length);
        int count = BinaryPrimitives.ReadInt32LittleEndian(file);
        if (row < 0 || row >= count || 4 + (count * NpcSize) > length) throw new ArgumentOutOfRangeException(nameof(row), $"La zona no tiene el NPC {row}.");
        int rec = 4 + (row * NpcSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(file[rec..]) != NpcType) throw new MapFormatException("El registro no es de tipo NPC.");
        var old = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(file[(rec + 4)..]), BinaryPrimitives.ReadSingleLittleEndian(file[(rec + 8)..]), BinaryPrimitives.ReadSingleLittleEndian(file[(rec + 12)..]));
        var delta = position - old;
        var shapeList = new List<int>();
        foreach (int at in new[] { NpcTalkShape, NpcBodyShape })
        {
            int o = (int)BinaryPrimitives.ReadUInt32LittleEndian(file[(rec + at)..]);
            if (o != 0 && !shapeList.Contains(o)) shapeList.Add(o);
        }
        var shapes = shapeList.ToArray();
        for (int other = 0; other < count; other++)
        {
            if (other == row) continue;
            int o = 4 + (other * NpcSize);
            foreach (int at in new[] { NpcTalkShape, NpcBodyShape })
                if (shapes.Contains((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(o + at)..]))) throw new InvalidOperationException("Una forma del NPC la comparte otro registro; no se mueve para no desplazar ambos.");
        }
        var output = (byte[])ed.Clone();
        var outFile = output.AsSpan(start, length);
        BinaryPrimitives.WriteSingleLittleEndian(outFile[(rec + 4)..], position.X);
        BinaryPrimitives.WriteSingleLittleEndian(outFile[(rec + 8)..], position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(outFile[(rec + 12)..], position.Z);
        var allowed = new List<(int From, int To)> { (start + rec + 4, start + rec + 16) };
        foreach (int s in shapes)
            foreach (var (at, bytes) in ShapePoints(file, s))
            {
                for (int k = 0; k < bytes; k += 4)
                {
                    float v = BinaryPrimitives.ReadSingleLittleEndian(file[(at + k)..]);
                    float d = (k / 4 % 3) switch { 0 => delta.X, 1 => delta.Y, _ => delta.Z };
                    BinaryPrimitives.WriteSingleLittleEndian(outFile[(at + k)..], v + d);
                }
                allowed.Add((start + at, start + at + bytes));
            }
        for (int i = 0; i < ed.Length; i++)
            if (ed[i] != output[i] && !allowed.Any(r => i >= r.From && i < r.To)) throw new InvalidOperationException("La escritura tocó bytes fuera del NPC y sus formas.");
        _ = ZoneFile(output, Gen7Events.NpcPack, zone);
        _ = Gen7Events.Records(output.AsSpan(start, length).ToArray(), NpcSize, NpcType).ToList();
        return output;
    }

    /// <summary>
    /// Trainer record (ED part 7, 84 bytes): rotation quaternion x, y, z, w at +0x10; offsets into the
    /// zone file for the movement block (+0x3C: u32 movement, u32 motion, f32 frame), talk-range and
    /// body shapes (+0x40, +0x44), motion signals (+0x48), patrol route (+0x4C) and route actions (+0x50).
    /// </summary>
    public const int TrainerSize = 84, TrainerType = 7, TrainerRotation = 0x10, TrainerMovement = 0x3C,
        TrainerTalkShape = 0x40, TrainerBodyShape = 0x44, TrainerSignals = 0x48, TrainerRoute = 0x4C, TrainerActions = 0x50;

    /// <summary>Movement value of trainers that stand still (every trainer without a route in Ultra Moon).</summary>
    public const uint StandingMovement = 4;

    /// <summary>
    /// Map compass angle (0 up, 90 right, 180 down, 270 left) of a yaw about the vertical axis. A trainer
    /// with no rotation faces +Z, which is down on the map: forward = (sin yaw, cos yaw) on X/Z.
    /// </summary>
    public static double CompassOfYaw(double yawDegrees) => (((180 - yawDegrees) % 360) + 360) % 360;
    public static double YawOfCompass(double compass) => 180 - compass;

    /// <summary>Yaw in degrees of a quaternion that only turns about the vertical axis; null otherwise.</summary>
    public static double? Yaw(Gen7Record trainer)
    {
        float x = trainer.F(TrainerRotation), y = trainer.F(TrainerRotation + 4), z = trainer.F(TrainerRotation + 8), w = trainer.F(TrainerRotation + 12);
        float norm = (x * x) + (y * y) + (z * z) + (w * w);
        if (!float.IsFinite(norm) || MathF.Abs(norm - 1) > 1e-3f || x != 0 || z != 0) return null;
        return 2 * Math.Atan2(y, w) * 180 / Math.PI;
    }

    /// <summary>Why a trainer cannot be turned by rewriting its rotation alone, or null when it can.</summary>
    public static string? TurnRestriction(ReadOnlySpan<byte> file, Gen7Record trainer)
    {
        if (trainer.U32(0) != TrainerType) return "El registro no es de entrenador.";
        if (Yaw(trainer) is null) return "La rotación no es un giro sobre el eje vertical; no se modifica.";
        uint movement = trainer.U32(TrainerMovement);
        if (movement != 0 && (movement + 8 > file.Length || BinaryPrimitives.ReadUInt32LittleEndian(file[(int)movement..]) != StandingMovement))
            return "El entrenador no está quieto (tipo de movimiento distinto de 4); su dirección la controla el movimiento.";
        if (trainer.U32(TrainerRoute) != 0 || trainer.U32(TrainerActions) != 0) return "El entrenador recorre una ruta; su dirección la controla la ruta.";
        var p = trainer.Position;
        foreach (int at in new[] { TrainerTalkShape, TrainerBodyShape })
        {
            int o = (int)trainer.U32(at);
            if (o == 0) continue;
            int count = BinaryPrimitives.ReadInt32LittleEndian(file[o..]);
            for (int i = 0; i < count; i++)
                if (BinaryPrimitives.ReadInt32LittleEndian(file[(o + 4 + (i * 4))..]) != 0) return "Una forma del entrenador no es un cilindro; habría que girarla también.";
            foreach (var (point, _) in ShapePoints(file, o))
            {
                float sx = BinaryPrimitives.ReadSingleLittleEndian(file[point..]), sz = BinaryPrimitives.ReadSingleLittleEndian(file[(point + 8)..]);
                if (MathF.Abs(sx - p.X) > 0.5f || MathF.Abs(sz - p.Z) > 0.5f) return "Una forma del entrenador no está centrada en él; habría que girarla también.";
            }
        }
        return null;
    }

    /// <summary>
    /// Copy of the ED pack with trainer <paramref name="row"/> of local zone <paramref name="zone"/> turned to
    /// <paramref name="compass"/>. Only the y and w of its rotation change (8 bytes); its cylinders are centred
    /// on it, so they stay valid. Refused for trainers whose route, movement or shapes depend on the direction.
    /// </summary>
    public static byte[] TurnTrainer(byte[] ed, int zone, int row, double compass)
    {
        if (!double.IsFinite(compass)) throw new ArgumentException("Ángulo no finito.");
        var (start, length) = ZoneFile(ed, Gen7Events.TrainerPack, zone);
        var file = ed.AsSpan(start, length);
        var rows = Gen7Events.Records(file.ToArray(), TrainerSize, TrainerType).ToList();
        if (row < 0 || row >= rows.Count) throw new ArgumentOutOfRangeException(nameof(row), $"La zona no tiene el entrenador {row}.");
        if (TurnRestriction(file, rows[row]) is { } why) throw new InvalidOperationException(why);
        double half = YawOfCompass(compass) * Math.PI / 360;
        var output = (byte[])ed.Clone();
        int rec = start + 4 + (row * TrainerSize);
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(rec + TrainerRotation + 4), (float)Math.Sin(half));
        BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(rec + TrainerRotation + 12), (float)Math.Cos(half));
        for (int i = 0; i < ed.Length; i++)
            if (ed[i] != output[i] && !(i >= rec + TrainerRotation + 4 && i < rec + TrainerRotation + 8) && !(i >= rec + TrainerRotation + 12 && i < rec + TrainerRotation + 16))
                throw new InvalidOperationException("La escritura tocó bytes fuera de la rotación del entrenador.");
        _ = ZoneFile(output, Gen7Events.TrainerPack, zone);
        _ = Gen7Events.Records(output.AsSpan(start, length).ToArray(), TrainerSize, TrainerType).ToList();
        return output;
    }
}
