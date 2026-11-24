using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Adds visible encounter grass to an Ultra Sun / Ultra Moon map block through the project history: a vanilla grass
/// model with a single patch is copied into part 1 of a block that has none, with its vertices moved to the chosen
/// place (see <see cref="Gen7VisibleGrass"/>). Only that member of the map block archive (a/0/8/6) changes; every
/// other member is checked unchanged. The archive (≈77 MB) fits the history's restore points, so the history undoes it.
/// The grass does not create encounters by itself: the catch zone (<see cref="Map3dsGen7CatchZoneService"/>) does.
/// </summary>
internal sealed class Map3dsGen7VisibleGrassService
{
    internal const string Module = "Editor de mapas";
    internal const string ActionId = "map3ds.gen7.visible-grass.add";
    internal const int MapArchive = 86;
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly GARCReference _reference;
    private readonly Guid _owner = Guid.NewGuid();

    internal Map3dsGen7VisibleGrassService(GameProjectSession project)
    {
        var config = project.GameConfiguration;
        if (!config.USUM) throw new NotSupportedException("La hierba visible Gen7 solo está verificada en Ultra Sol / Ultra Luna.");
        _project = project;
        var encdata = config.GetGARCReference("encdata");
        _reference = encdata.GetRelativeGARC(MapArchive - encdata.FileNumber, "map");
        _path = Path.Combine(config.RomFS, _reference.Reference);
    }

    internal string BlockArchive => _path;

    /// <summary>Builds the change without writing anything.</summary>
    internal EditorProposal Preview(Gen7MapBackend backend, int area, (int X, int Y) centre, int templateBlock)
    {
        var (plan, reason) = backend.PlanGrass(area, centre, templateBlock);
        if (plan is null) throw new InvalidOperationException(reason);
        byte[] source = File.ReadAllBytes(_path);
        byte[][] records = new GARC.MemGARC(source).Files;
        if (plan.TargetBlock >= records.Length) throw new InvalidDataException("El bloque no existe en el archivo.");
        // Block members are LZ11-compressed in the archive; the backend reads them decompressed.
        if (!Decode(records[plan.TargetBlock]).AsSpan().SequenceEqual(backend.BlockBytes(plan.TargetBlock)) || !Decode(records[plan.TemplateBlock]).AsSpan().SequenceEqual(backend.BlockBytes(plan.TemplateBlock)))
            throw new InvalidDataException("El archivo de bloques no coincide con lo que muestra el editor. Vuelve a abrir el editor.");
        var original = (byte[][])records.Clone();
        byte[] block = backend.BlockWithGrass(plan);
        records[plan.TargetBlock] = Encode(records[plan.TargetBlock], block);
        byte[] output = GarcTransaction.Build(source, _reference, records, preserveRecordSize: false);
        var after = new GARC.MemGARC(output).Files;
        if (after.Length != original.Length) throw new InvalidDataException("La reconstrucción cambió el número de bloques. No se guardó nada.");
        for (int i = 0; i < after.Length; i++)
            if (i != plan.TargetBlock && !after[i].AsSpan().SequenceEqual(original[i])) throw new InvalidDataException("La reconstrucción cambió otro bloque. No se guardó nada.");
        if (!Decode(after[plan.TargetBlock]).AsSpan().SequenceEqual(block)) throw new InvalidDataException("La relectura no coincide con la propuesta. No se guardó nada.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string sharing = plan.Sharing.Count == 0 ? "" : $" (también se ve desde las áreas {string.Join(", ", plan.Sharing.Select(a => a.ToString("000", inv)))})";
        var changes = new List<BatchChange>
        {
            new(area, $"Bloque {plan.TargetBlock}", "Hierba visible", "sin hierba", $"mancha de la plantilla {plan.TemplateBlock} sobre {plan.Covered.Count} casillas{sharing}", "grass.visible"),
        };
        var parameters = new Dictionary<string, string>
        {
            { "ActionId", ActionId }, { "Game", "USUM" }, { "Area", area.ToString(inv) }, { "Block", plan.TargetBlock.ToString(inv) }, { "Template", plan.TemplateBlock.ToString(inv) },
            { "Centre", $"{centre.X},{centre.Y}" }, { "Offset", $"{plan.Delta.X.ToString("0.##", inv)},{plan.Delta.Y.ToString("0.##", inv)},{plan.Delta.Z.ToString("0.##", inv)}" },
            { "Cells", plan.Covered.Count.ToString(inv) }, { "Gameplay", "not tested" },
        };
        return new EditorProposal(_owner, $"Hierba visible en el bloque {plan.TargetBlock} (área {area:000}, {plan.Covered.Count} casillas)", 0, [new FileChange(_path, source, output)], [.. changes], parameters);
    }

    private static byte[] Decode(byte[] raw)
    {
        if (raw.Length == 0 || raw[0] != 0x11) return raw;
        using var output = new MemoryStream();
        LZSS.Decompress(new MemoryStream(raw), raw.Length, output);
        return output.ToArray();
    }

    private static byte[] Encode(byte[] originalRaw, byte[] edited)
    {
        if (originalRaw.Length == 0 || originalRaw[0] != 0x11) return edited;
        byte[] encoded = GameCatchZoneService.Compress(edited);
        if (!Decode(encoded).AsSpan().SequenceEqual(edited)) throw new InvalidDataException("La recompresión no es reversible. No se guardó nada.");
        return encoded;
    }

    internal void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        foreach (var file in proposal.Files) GarcTransaction.ReadUnchanged(file.Path, file.Fingerprint);
        _project.History.Commit(proposal.Files, Module, proposal.Action, proposal.Changes, proposal.Parameters);
    }
}
