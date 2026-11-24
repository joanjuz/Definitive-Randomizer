#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>A nurse to copy: her NPC record and its two shape lists (talk range, body), as stored in a Pokémon Center.</summary>
/// <param name="Talk">Talk-range shape list (record +0x70), stored relative to the nurse: positions minus hers.</param>
/// <param name="Body">Body shape list (record +0x74), relative as <paramref name="Talk"/>.</param>
/// <param name="Character">The "CP" pack of her model in the area character pack ("AC", slot 3).</param>
public sealed record Gen7NurseTemplate(byte[] Record, byte[] Talk, byte[] Body, byte[] Character, uint Model, uint Script, int Count);

/// <summary>
/// Pokémon Center nurse of Ultra Sun / Ultra Moon. Every Center has one NPC (ED part 1, 120 bytes) with model 66 and
/// script 5030; the script number is a case of a shared script (a/0/9/2 member 3, cases −1 and 5030–5035) that the
/// game reaches from any area, so the copy needs no script of its own. The model is loaded by the area: its character
/// pack ("AC", slot 3) holds one "CP" pack per character model, and in every area of Ultra Moon and Ultra Sun it holds
/// the model of every NPC and trainer (300/300). A nurse in another area therefore needs the "CP" of model 66 (the same
/// in the 15 Centers and both games) in that pack.
/// <para>
/// Zone files of ED part 1 are a u32 count, the 120-byte records and then each record's shape lists, in record order
/// and back to back (307/307 files). A new nurse goes after the last record; the shapes of the other NPCs only move by
/// one record.
/// </para>
/// </summary>
public static class Gen7Nurse
{
    public const uint Model = 66, Script = 5030;
    public const int Position = 0x04, Rotation = 0x10, EventId = 0x2C, ModelField = 0x34, ScriptField = 0x38;
    /// <summary>Largest character pack and most character models of an area in the game (both versions).</summary>
    public const int MaxCharacterBytes = 7_344_640, MaxCharacters = 37;

    /// <summary>Name of a "CP" pack: the ASCII model number at the start of its first part.</summary>
    public static string? CharacterName(byte[] cp)
    {
        try
        {
            var parts = Gen7CatchZones.Parts(cp, "CP");
            if (parts.Length == 0) return null;
            int end = Array.IndexOf(parts[0], (byte)0);
            var s = Encoding.ASCII.GetString(parts[0], 0, end < 0 ? Math.Min(16, parts[0].Length) : end);
            return s.Length > 0 && s.All(char.IsDigit) ? s : null;
        }
        catch (MapFormatException) { return null; }
    }

    /// <summary>Model names of an area character pack, checked to rebuild byte for byte.</summary>
    public static IReadOnlyList<string?> Characters(byte[] ac)
    {
        if (!Gen7CatchZones.RoundTrips(ac, "AC")) throw new MapFormatException("El paquete de personajes del área no se reconstruye byte a byte.");
        return Gen7CatchZones.Parts(ac, "AC").Select(CharacterName).ToArray();
    }

    /// <summary>The character pack with <paramref name="cp"/> appended (same data start), or the same bytes when the model is already there.</summary>
    public static byte[] AddCharacter(byte[] ac, byte[] cp)
    {
        string name = CharacterName(cp) ?? throw new ArgumentException("El paquete del modelo no tiene nombre.", nameof(cp));
        var names = Characters(ac);
        if (names.Contains(name)) return ac;
        if (cp.Length % 0x80 != 0) throw new ArgumentException("El paquete del modelo no mide un múltiplo de 0x80.", nameof(cp));
        var parts = Gen7CatchZones.Parts(ac, "AC").Append(cp).ToArray();
        if (parts.Length > MaxCharacters) throw new InvalidOperationException($"El área tendría {parts.Length} modelos de personaje; el juego no pasa de {MaxCharacters}.");
        // The data start is the header rounded up to 0x80 (311/311 areas of both games: 0x80 up to 30 models, 0x100 above).
        if (Gen7CatchZones.DataStart(ac) != DataStart(parts.Length - 1)) throw new InvalidOperationException("El inicio de datos del paquete de personajes no sigue la regla del juego.");
        byte[] output = Gen7CatchZones.Pack("AC", parts, DataStart(parts.Length));
        if (output.Length > MaxCharacterBytes) throw new InvalidOperationException($"Los modelos de personaje del área ocuparían {output.Length} bytes; el juego no pasa de {MaxCharacterBytes}.");
        var reread = Gen7CatchZones.Parts(output, "AC");
        var old = Gen7CatchZones.Parts(ac, "AC");
        if (reread.Length != old.Length + 1 || !old.Select((p, i) => p.AsSpan().SequenceEqual(reread[i])).All(x => x) || !reread[^1].AsSpan().SequenceEqual(cp))
            throw new InvalidOperationException("La relectura del paquete de personajes no coincide.");
        return output;
    }

    /// <summary>Data start of a character pack with <paramref name="count"/> models: its header rounded up to 0x80.</summary>
    public static int DataStart(int count) => (4 + ((count + 1) * 4) + 0x7F) / 0x80 * 0x80;

    /// <summary>Shape lists of a record as bytes: count, types and shapes (sizes as <see cref="Gen7ZoneEdit.ShapePoints"/>).</summary>
    public static byte[] ShapeList(byte[] file, int offset)
    {
        var points = Gen7ZoneEdit.ShapePoints(file, offset);
        if (points.Count == 0) throw new MapFormatException("El NPC no tiene formas.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(offset));
        int end = offset + 4 + (count * 4);
        for (int i = 0; i < count; i++) end += ShapeSize(BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(offset + 4 + (i * 4))));
        return file.AsSpan(offset, end - offset).ToArray();
    }

    private static int ShapeSize(int type) => type switch { 0 => 20, 1 => 40, 2 => 28, 3 => 36, _ => throw new MapFormatException($"Tipo de forma {type} desconocido.") };

    /// <summary>Shape list moved by <paramref name="delta"/> (every point), checked with <see cref="Gen7ZoneEdit.ShapePoints"/>.</summary>
    public static byte[] MoveShapes(byte[] shapes, Vector3 delta)
    {
        var output = (byte[])shapes.Clone();
        foreach (var (at, bytes) in Points(shapes))
            for (int k = 0; k < bytes; k += 4)
            {
                float d = (k / 4 % 3) switch { 0 => delta.X, 1 => delta.Y, _ => delta.Z };
                if (d == 0) continue;
                BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(at + k), BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(at + k)) + d);
            }
        return output;
    }

    /// <summary>Point ranges of a standalone shape list (offset 0), as <see cref="Gen7ZoneEdit.ShapePoints"/> reads them in a file.</summary>
    private static List<(int At, int Bytes)> Points(byte[] shapes)
    {
        // ShapePoints treats offset 0 as "no shape"; read the list behind a 4-byte pad instead.
        var padded = new byte[shapes.Length + 4];
        shapes.CopyTo(padded, 4);
        return Gen7ZoneEdit.ShapePoints(padded, 4).Select(p => (p.At - 4, p.Bytes)).ToList();
    }

    /// <summary>First point of a shape list (its centre for a cylinder).</summary>
    public static Vector3 FirstPoint(byte[] shapes)
    {
        var (at, _) = Points(shapes)[0];
        return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(shapes.AsSpan(at)), BinaryPrimitives.ReadSingleLittleEndian(shapes.AsSpan(at + 4)), BinaryPrimitives.ReadSingleLittleEndian(shapes.AsSpan(at + 8)));
    }

    /// <summary>Map compass angle (0 up, 90 right, 180 down, 270 left) → yaw quaternion about the vertical axis.</summary>
    public static Quaternion Facing(double compass)
    {
        double half = Gen7ZoneEdit.YawOfCompass(compass) * Math.PI / 360;
        return new Quaternion(0, (float)Math.Sin(half), 0, (float)Math.Cos(half));
    }

    /// <summary>
    /// The nurse record for <paramref name="position"/> and <paramref name="compass"/>, with its shapes: the body shape on
    /// the nurse and the talk shape turned with her (the template's talk shape is in front of a nurse without rotation).
    /// </summary>
    public static (byte[] Record, byte[] Talk, byte[] Body) Place(Gen7NurseTemplate t, Vector3 position, double compass, uint eventId)
    {
        var record = (byte[])t.Record.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Position), position.X);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Position + 4), position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Position + 8), position.Z);
        var q = Facing(compass);
        float[] rot = [q.X, q.Y, q.Z, q.W];
        for (int k = 0; k < 4; k++) BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(Rotation + (k * 4)), rot[k]);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(EventId), eventId);
        // Relative talk offset (template nurse without rotation) turned by the yaw about the vertical axis.
        var rel = FirstPoint(t.Talk);
        var turned = Vector3.Transform(rel, q);
        var talk = MoveShapes(t.Talk, position + turned - rel);
        var body = MoveShapes(t.Body, position);
        return (record, talk, body);
    }

    /// <summary>
    /// Copy of the area event pack with one more NPC in zone file <paramref name="zoneFile"/> of part 1: the record goes
    /// after the existing ones, whose shape offsets move by one record, and its talk and body shapes at the end of the file.
    /// Every other part, zone file and record is checked to keep its bytes.
    /// </summary>
    public static byte[] AddNpc(byte[] ed, int zoneFile, byte[] record, byte[] talk, byte[] body)
    {
        if (record.Length != Gen7ZoneEdit.NpcSize || BinaryPrimitives.ReadUInt32LittleEndian(record) != Gen7ZoneEdit.NpcType) throw new ArgumentException("El registro no es un NPC.", nameof(record));
        if (!Gen7CatchZones.RoundTrips(ed, "ED")) throw new InvalidOperationException("El paquete de eventos no tiene la estructura verificada.");
        var parts = Gen7CatchZones.Parts(ed, "ED");
        if (parts.Length <= Gen7Events.NpcPack) throw new InvalidOperationException("El paquete de eventos no tiene parte de NPC.");
        var inner = parts[Gen7Events.NpcPack];
        string magic = Encoding.ASCII.GetString(inner, 0, 2);
        if (!Gen7CatchZones.RoundTrips(inner, magic)) throw new InvalidOperationException("La parte de NPC no tiene la estructura verificada.");
        var files = Gen7CatchZones.Parts(inner, magic);
        if (zoneFile < 0 || zoneFile >= files.Length) throw new ArgumentOutOfRangeException(nameof(zoneFile));
        var old = files[zoneFile];
        int n = old.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(old) : 0;
        int records = 4 + (n * Gen7ZoneEdit.NpcSize);
        if (n < 0 || records > old.Length) throw new InvalidOperationException("El archivo de NPC no tiene la estructura verificada.");
        // The shapes follow the records, in record order and back to back, and nothing else follows them.
        int p = records;
        for (int i = 0; i < n; i++)
            foreach (int at in new[] { Gen7ZoneEdit.NpcTalkShape, Gen7ZoneEdit.NpcBodyShape })
            {
                int o = BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(4 + (i * Gen7ZoneEdit.NpcSize) + at));
                if (o != p) throw new InvalidOperationException("Las formas de los NPC no siguen a los registros en orden; no se escribe.");
                p += ShapeList(old, o).Length;
            }
        if (p != old.Length) throw new InvalidOperationException("El archivo de NPC tiene datos después de las formas; no se escribe.");

        var output = new byte[old.Length + Gen7ZoneEdit.NpcSize + talk.Length + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(output, n + 1);
        for (int i = 0; i < n; i++)
        {
            int at = 4 + (i * Gen7ZoneEdit.NpcSize);
            old.AsSpan(at, Gen7ZoneEdit.NpcSize).CopyTo(output.AsSpan(at));
            foreach (int f in new[] { Gen7ZoneEdit.NpcTalkShape, Gen7ZoneEdit.NpcBodyShape })
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + f), BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(at + f)) + Gen7ZoneEdit.NpcSize);
        }
        int rec = records, shapes = records + Gen7ZoneEdit.NpcSize;
        var newRecord = (byte[])record.Clone();
        int talkAt = shapes + (old.Length - records), bodyAt = talkAt + talk.Length;
        BinaryPrimitives.WriteInt32LittleEndian(newRecord.AsSpan(Gen7ZoneEdit.NpcTalkShape), talkAt);
        BinaryPrimitives.WriteInt32LittleEndian(newRecord.AsSpan(Gen7ZoneEdit.NpcBodyShape), bodyAt);
        newRecord.CopyTo(output, rec);
        old.AsSpan(records).CopyTo(output.AsSpan(shapes));
        talk.CopyTo(output, talkAt); body.CopyTo(output, bodyAt);

        files[zoneFile] = output;
        parts[Gen7Events.NpcPack] = Gen7CatchZones.Pack(magic, files, Gen7CatchZones.DataStart(inner));
        byte[] result = Gen7CatchZones.Pack("ED", parts, Gen7CatchZones.DataStart(ed));

        // Re-read: the old NPCs (records without their offsets, and shapes) and every other part are kept.
        var oldParts = Gen7CatchZones.Parts(ed, "ED"); var newParts = Gen7CatchZones.Parts(result, "ED");
        for (int i = 0; i < oldParts.Length; i++)
            if (i != Gen7Events.NpcPack && !oldParts[i].AsSpan().SequenceEqual(newParts[i])) throw new InvalidOperationException("La escritura cambió otra parte del paquete de eventos.");
        var oldFiles = Gen7CatchZones.Parts(inner, magic); var newFiles = Gen7CatchZones.Parts(newParts[Gen7Events.NpcPack], magic);
        for (int i = 0; i < oldFiles.Length; i++)
            if (i != zoneFile && !oldFiles[i].AsSpan().SequenceEqual(newFiles[i])) throw new InvalidOperationException("La escritura cambió otro archivo de NPC.");
        var readBack = Gen7Events.Records(output, Gen7ZoneEdit.NpcSize, Gen7ZoneEdit.NpcType).ToList();
        if (readBack.Count != n + 1) throw new InvalidOperationException("La relectura no tiene un NPC más.");
        for (int i = 0; i < n; i++)
        {
            int oa = 4 + (i * Gen7ZoneEdit.NpcSize);
            if (!old.AsSpan(oa, Gen7ZoneEdit.NpcTalkShape).SequenceEqual(output.AsSpan(oa, Gen7ZoneEdit.NpcTalkShape))
                || !old.AsSpan(oa + Gen7ZoneEdit.NpcBodyShape + 4, Gen7ZoneEdit.NpcSize - Gen7ZoneEdit.NpcBodyShape - 4).SequenceEqual(output.AsSpan(oa + Gen7ZoneEdit.NpcBodyShape + 4, Gen7ZoneEdit.NpcSize - Gen7ZoneEdit.NpcBodyShape - 4))
                || !ShapeList(old, BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(oa + Gen7ZoneEdit.NpcTalkShape))).SequenceEqual(ShapeList(output, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(oa + Gen7ZoneEdit.NpcTalkShape))))
                || !ShapeList(old, BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(oa + Gen7ZoneEdit.NpcBodyShape))).SequenceEqual(ShapeList(output, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(oa + Gen7ZoneEdit.NpcBodyShape)))))
                throw new InvalidOperationException("La escritura cambió un NPC existente.");
        }
        if (!output.AsSpan(rec, Gen7ZoneEdit.NpcTalkShape).SequenceEqual(record.AsSpan(0, Gen7ZoneEdit.NpcTalkShape))
            || !ShapeList(output, talkAt).SequenceEqual(talk) || !ShapeList(output, bodyAt).SequenceEqual(body))
            throw new InvalidOperationException("El NPC nuevo no se relee igual.");
        return result;
    }
}
