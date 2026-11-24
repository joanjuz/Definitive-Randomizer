#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>
/// Visible encounter grass of Ultra Sun / Ultra Moon. The tall grass drawn on the field is not tied to the encounter
/// regions: it is a dedicated model in part 1 of the map block pack (a/0/8/6), always one GFL2 model named
/// "Map07_encgrass01" (143 of 852 blocks in Ultra Moon) with one material ("Kusa" shader) and one mesh per patch, whose
/// textures come from part 0 of the area's BG pack (slot 1 of the field archive). Its vertices are already in world
/// coordinates (float32 positions), so a vanilla patch can be placed elsewhere by moving its vertices.
/// <para>
/// This first version only copies a vanilla grass model with a single mesh into a block that has no grass model yet,
/// moving every vertex, the bounding box and the reference point by the same offset; every other byte is the vanilla
/// one. Block packs ("BG", 8 parts, data from 0x80, parts aligned to 0x80) are rebuilt byte for byte or not written.
/// </para>
/// </summary>
public static class Gen7VisibleGrass
{
    public const int GrassPart = 1, PartCount = 8, DataStart = 0x80, Alignment = 0x80;
    public const string ModelName = "Map07_encgrass01";

    /// <summary>Sections of a model blob: u32 magic, u32 count, 8 bytes, then {name[8], u32 length, u32, data} each.</summary>
    public static IReadOnlyList<(string Name, int Offset, int Length)> Sections(ReadOnlySpan<byte> model)
    {
        if (model.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(model) != Gfl2File.ModelMagic) throw new MapFormatException("No es un modelo GFL2.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(model[4..]);
        if (count is 0 or > 512) throw new MapFormatException($"Recuento de secciones GFL2 {count} no válido.");
        var list = new List<(string, int, int)>();
        int p = 16;
        for (int i = 0; i < count; i++)
        {
            if (p + 16 > model.Length) throw new MapFormatException("Sección GFL2 truncada.");
            string name = Encoding.ASCII.GetString(model.Slice(p, 8)).TrimEnd('\0');
            int length = BinaryPrimitives.ReadInt32LittleEndian(model[(p + 8)..]);
            if (length < 0 || p + 16 + length > model.Length) throw new MapFormatException("Sección GFL2 fuera del modelo.");
            list.Add((name, p, length));
            p += 16 + length;
        }
        if (model[p..].IndexOfAnyExcept((byte)0) >= 0) throw new MapFormatException("El modelo tiene datos después de sus secciones.");
        return list;
    }

    /// <summary>Whether a block part is the encounter-grass model.</summary>
    public static bool IsGrassModel(ReadOnlySpan<byte> part)
    {
        if (part.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(part) != Gfl2File.ModelMagic) return false;
        try
        {
            var s = Sections(part);
            return s.Count > 0 && s[0].Name == "gfmodel" && part.Slice(s[0].Offset, s[0].Length + 16).IndexOf(Encoding.ASCII.GetBytes(ModelName)) >= 0;
        }
        catch (MapFormatException) { return false; }
    }

    public static int MeshCount(ReadOnlySpan<byte> model) => Sections(model).Count(s => s.Name == "mesh");

    /// <summary>Texture names a model uses (from its materials).</summary>
    public static IReadOnlyList<string> TextureNames(ReadOnlySpan<byte> model) =>
        Gfl2File.ReadModel(model).Materials.SelectMany(m => m.TextureNames).Where(t => t.Length > 0)
            .Select(t => t.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? t[..^4] : t).Distinct().ToArray();

    /// <summary>World bounding box of the vertices of a model.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(ReadOnlySpan<byte> model)
    {
        var pos = Gfl2File.ReadModel(model).Meshes.SelectMany(m => m.Read(PicaAttribute.Position) ?? []).ToArray();
        if (pos.Length == 0) throw new MapFormatException("El modelo no tiene vértices.");
        return (new Vector3(pos.Min(v => v.X), pos.Min(v => v.Y), pos.Min(v => v.Z)), new Vector3(pos.Max(v => v.X), pos.Max(v => v.Y), pos.Max(v => v.Z)));
    }

    /// <summary>
    /// The model with every vertex position, the bounding box and the reference point moved by <paramref name="delta"/>.
    /// Positions must be the first attribute and float32 × 3 with scale 1 (every grass mesh of Ultra Moon); the result is
    /// read back and every other byte is checked unchanged.
    /// </summary>
    public static byte[] Translate(byte[] model, Vector3 delta)
    {
        if (!float.IsFinite(delta.X + delta.Y + delta.Z)) throw new ArgumentOutOfRangeException(nameof(delta));
        var layout = Gfl2File.ReadLayout(model);
        if (layout.BoundingBox < 0 || layout.Transform < 0 || layout.Buffers.Count == 0) throw new MapFormatException("No se encontró la estructura del modelo.");
        var output = (byte[])model.Clone();
        var touched = new bool[model.Length];
        void Add(int at, float d)
        {
            if (d == 0) return; // -0.0 + 0 would become +0.0: an axis that does not move keeps its bytes.
            float v = BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(at));
            BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(at), v + d);
            for (int k = 0; k < 4; k++) touched[at + k] = true;
        }
        foreach (var b in layout.Buffers)
        {
            if (b.Attributes.Count == 0 || b.Attributes[0] is not { Name: PicaAttribute.Position, Format: 3, Elements: 3 } a || a.Scale != 1f)
                throw new MapFormatException("Las posiciones de la malla no son float32 × 3 al principio del vértice; no se mueve.");
            // The buffer is padded after its last vertex (the reader also counts Length / Stride vertices); the padding is zeros.
            if (b.Stride < 12 || b.Length < b.Stride || model.AsSpan(b.Offset + (b.Length / b.Stride * b.Stride), b.Length % b.Stride).IndexOfAnyExcept((byte)0) >= 0)
                throw new MapFormatException("Búfer de vértices no válido.");
            for (int v = 0; v < b.Length / b.Stride; v++)
            {
                int at = b.Offset + (v * b.Stride);
                Add(at, delta.X); Add(at + 4, delta.Y); Add(at + 8, delta.Z);
            }
        }
        // Bounding box: min (x, y, z, w) then max (x, y, z, w).
        Add(layout.BoundingBox, delta.X); Add(layout.BoundingBox + 4, delta.Y); Add(layout.BoundingBox + 8, delta.Z);
        Add(layout.BoundingBox + 16, delta.X); Add(layout.BoundingBox + 20, delta.Y); Add(layout.BoundingBox + 24, delta.Z);
        // Reference point: the translation row of the row-major transform (elements 12, 13, 14).
        Add(layout.Transform + 48, delta.X); Add(layout.Transform + 52, delta.Y); Add(layout.Transform + 56, delta.Z);

        for (int i = 0; i < model.Length; i++)
            if (!touched[i] && output[i] != model[i]) throw new InvalidOperationException("Se modificó un byte fuera de las posiciones.");
        var before = Gfl2File.ReadModel(model); var after = Gfl2File.ReadModel(output);
        if (before.Meshes.Count != after.Meshes.Count) throw new InvalidOperationException("La relectura cambió el número de mallas.");
        for (int m = 0; m < before.Meshes.Count; m++)
        {
            var p0 = before.Meshes[m].Read(PicaAttribute.Position)!; var p1 = after.Meshes[m].Read(PicaAttribute.Position)!;
            for (int v = 0; v < p0.Length; v++)
                if (Vector3.Distance(new Vector3(p1[v].X, p1[v].Y, p1[v].Z), new Vector3(p0[v].X, p0[v].Y, p0[v].Z) + delta) > 0.01f)
                    throw new InvalidOperationException("La relectura de las posiciones no coincide.");
        }
        return output;
    }

    /// <summary>Parts of a block pack, checked to rebuild byte for byte with <see cref="PackBlock"/>.</summary>
    public static byte[][] BlockParts(byte[] block)
    {
        var parts = Gen7CatchZones.Parts(block, "BG");
        if (parts.Length != PartCount) throw new MapFormatException($"El bloque tiene {parts.Length} partes; se esperaban {PartCount}.");
        if (!PackBlock(parts).AsSpan().SequenceEqual(block)) throw new MapFormatException("El bloque no se reconstruye byte a byte; no se escribe.");
        return parts;
    }

    /// <summary>A block pack: "BG", u16 count, offsets, data from 0x80, every part a multiple of 0x80.</summary>
    public static byte[] PackBlock(IReadOnlyList<byte[]> parts)
    {
        if (parts.Any(p => p.Length % Alignment != 0)) throw new ArgumentException("Cada parte del bloque debe medir un múltiplo de 0x80.", nameof(parts));
        return Gen7CatchZones.Pack("BG", parts, DataStart);
    }

    /// <summary>
    /// The target block with the template's grass model moved by <paramref name="delta"/> in its part 1. The target must
    /// have no grass model and the template exactly one mesh.
    /// </summary>
    public static byte[] AddGrass(byte[] target, byte[] templateBlock, Vector3 delta)
    {
        var parts = BlockParts(target);
        if (parts[GrassPart].Length != 0) throw new InvalidOperationException("El bloque ya tiene un modelo de hierba; esta versión solo escribe en bloques sin hierba.");
        var template = BlockParts(templateBlock)[GrassPart];
        if (!IsGrassModel(template)) throw new InvalidOperationException("La plantilla no tiene modelo de hierba.");
        if (MeshCount(template) != 1) throw new InvalidOperationException("La plantilla debe tener una sola mancha de hierba.");
        var moved = Translate(template, delta);
        var output = (byte[][])parts.Clone();
        output[GrassPart] = moved;
        byte[] block = PackBlock(output);
        var check = BlockParts(block);
        for (int i = 0; i < PartCount; i++)
            if (!check[i].AsSpan().SequenceEqual(output[i])) throw new InvalidOperationException("La relectura del bloque no coincide.");
        return block;
    }
}
