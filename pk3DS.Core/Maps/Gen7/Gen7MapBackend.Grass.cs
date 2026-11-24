#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using pk3DS.Core.Maps.Ctr;

namespace pk3DS.Core.Maps.Gen7;

/// <summary>A vanilla grass patch that can be copied: block, textures and size.</summary>
public sealed record Gen7GrassTemplate(int Block, IReadOnlyList<string> Textures, Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;
}

/// <summary>What placing a grass template would write: the target block, the offset and the cells the grass covers.</summary>
/// <param name="Covered">Cells whose centre lies inside the moved patch's bounding box (seen from above).</param>
/// <param name="Sharing">Other areas that load the target block at the same position: they show the grass too.</param>
public sealed record Gen7GrassPlan(int Area, int Col, int Row, int TargetBlock, int TemplateBlock, Vector3 Delta, IReadOnlyList<(int X, int Y)> Covered, IReadOnlyList<int> Sharing, float GroundSpread);

/// <summary>Visible encounter grass (see <see cref="Gen7VisibleGrass"/>).</summary>
public sealed partial class Gen7MapBackend
{
    /// <summary>Largest height difference of the ground under a new patch (units): grass is a flat model.</summary>
    public const float MaxGrassGroundSpread = 30f;

    public byte[] BlockBytes(int id) => id >= 0 && id < _chunkCount ? (byte[])_chunk(id).Clone() : throw new ArgumentOutOfRangeException(nameof(id));

    private IReadOnlyList<Gen7GrassTemplate>? _grassTemplates;

    /// <summary>Vanilla grass models with a single patch (23 in Ultra Moon), the only templates of this version.</summary>
    public IReadOnlyList<Gen7GrassTemplate> GrassTemplates
    {
        get
        {
            lock (_catchGate)
            {
                if (_grassTemplates is not null) return _grassTemplates;
                var list = new List<Gen7GrassTemplate>();
                for (int id = 0; id < _chunkCount; id++)
                {
                    try
                    {
                        byte[] data = _chunk(id);
                        if (data.Length < 8 || data[0] != 'B' || data[1] != 'G') continue;
                        var parts = Gen7VisibleGrass.BlockParts(data);
                        var grass = parts[Gen7VisibleGrass.GrassPart];
                        if (!Gen7VisibleGrass.IsGrassModel(grass) || Gen7VisibleGrass.MeshCount(grass) != 1) continue;
                        var (min, max) = Gen7VisibleGrass.Bounds(grass);
                        list.Add(new Gen7GrassTemplate(id, Gen7VisibleGrass.TextureNames(grass), min, max));
                    }
                    catch (MapFormatException) { }
                }
                return _grassTemplates = list;
            }
        }
    }

    /// <summary>Whether the area has every texture of the template (the grass draws with the area's textures).</summary>
    public bool HasTextures(int area, Gen7GrassTemplate template)
    {
        var textures = Textures(area);
        return template.Textures.All(textures.ContainsKey);
    }

    /// <summary>Templates whose textures the area has.</summary>
    public IReadOnlyList<Gen7GrassTemplate> GrassTemplatesFor(int area) => GrassTemplates.Where(t => HasTextures(area, t)).ToArray();

    /// <summary>
    /// Places the template centred on a cell: the target is the block of that cell, which must have no grass model; the
    /// moved patch must stay inside it, over ground whose height varies by at most <see cref="MaxGrassGroundSpread"/>.
    /// </summary>
    public (Gen7GrassPlan? Plan, string Reason) PlanGrass(int area, (int X, int Y) centre, int templateBlock)
    {
        if (!_worlds.TryGetValue(area, out var w)) return (null, "Área no disponible.");
        var template = GrassTemplates.FirstOrDefault(t => t.Block == templateBlock);
        if (template is null) return (null, "Esa plantilla no es una mancha de hierba original de una sola malla.");
        if (!HasTextures(area, template)) return (null, $"El área no tiene las texturas de esa hierba ({string.Join(", ", template.Textures)}); esta versión no añade texturas.");
        int col = (int)Math.Floor(centre.X / 40.0), row = (int)Math.Floor(centre.Y / 40.0);
        if (col < 0 || row < 0 || col >= w.Width || row >= w.Height || w.Blocks[(row * w.Width) + col] is not (>= 0 and var target))
            return (null, "La casilla no está en un bloque del área.");
        byte[][] parts;
        try { parts = Gen7VisibleGrass.BlockParts(BlockBytes(target)); }
        catch (MapFormatException ex) { return (null, $"El bloque {target} no tiene la estructura verificada: {ex.Message}"); }
        if (parts[Gen7VisibleGrass.GrassPart].Length != 0) return (null, $"El bloque {target} ya tiene hierba; esta versión solo la añade en bloques sin hierba.");

        var (cx, cz) = CellCentre(centre.X, centre.Y);
        var size = template.Size;
        float x0 = cx - (size.X / 2), z0 = cz - (size.Z / 2);
        float bx0 = col * (float)w.BlockSize, bz0 = row * (float)w.BlockSize;
        if (x0 < bx0 || z0 < bz0 || x0 + size.X > bx0 + w.BlockSize || z0 + size.Z > bz0 + w.BlockSize)
            return (null, $"La mancha ({size.X:0} × {size.Z:0} unidades) se saldría del bloque {target}; elige un centro más alejado de su borde.");

        // Cells of the patch: centre inside the moved bounding box. The mesh is a set of tufts, not a carpet; vanilla
        // regions are boxes over the whole patch (see Gen7EncounterRegion.Contains), so the zone covers the box.
        var dxz = new Vector2(x0 - template.Min.X, z0 - template.Min.Z);
        var covered = new HashSet<(int, int)>();
        for (int x = (int)Math.Floor(x0 / UnitsPerCell); x <= (int)Math.Floor((x0 + size.X) / UnitsPerCell); x++)
            for (int y = (int)Math.Floor(z0 / UnitsPerCell); y <= (int)Math.Floor((z0 + size.Z) / UnitsPerCell); y++)
            {
                float px = (x + 0.5f) * UnitsPerCell, pz = (y + 0.5f) * UnitsPerCell;
                if (px >= x0 && px <= x0 + size.X && pz >= z0 && pz <= z0 + size.Z) covered.Add((x, y));
            }
        if (covered.Count == 0) return (null, "La mancha no cubre ninguna casilla.");
        var heights = new List<float>();
        foreach (var (x, y) in covered)
        {
            var (px, pz) = CellCentre(x, y);
            if (GroundHeight(area, px, pz, template.Min.Y) is not float h) return (null, $"La casilla {x}, {y} bajo la hierba no tiene suelo.");
            heights.Add(h);
        }
        float spread = heights.Max() - heights.Min();
        if (spread > MaxGrassGroundSpread) return (null, $"El suelo bajo la mancha tiene {spread:0} unidades de desnivel (máximo {MaxGrassGroundSpread:0}); la hierba es un modelo plano.");
        // The vanilla grass base is the lowest vertex of the patch; it goes on the average ground height.
        var delta = new Vector3(dxz.X, heights.Average() - template.Min.Y, dxz.Y);
        var sharing = _worlds.Where(o => o.Key != area && col < o.Value.Width && row < o.Value.Height && o.Value.Blocks[(row * o.Value.Width) + col] == target).Select(o => o.Key).ToArray();
        return (new Gen7GrassPlan(area, col, row, target, templateBlock, delta, covered.OrderBy(c => c.Item2).ThenBy(c => c.Item1).ToArray(), sharing, spread), "");
    }

    /// <summary>The target block with the planned grass (checked as in <see cref="Gen7VisibleGrass.AddGrass"/>).</summary>
    public byte[] BlockWithGrass(Gen7GrassPlan plan) => Gen7VisibleGrass.AddGrass(BlockBytes(plan.TargetBlock), BlockBytes(plan.TemplateBlock), plan.Delta);

    private byte[][] BlockParts(int id) => Gen7VisibleGrass.BlockParts(BlockBytes(id));
}
