using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed record NdsNpcEdit(int Map, int Index, PlatinumNpcValues Before, PlatinumNpcValues After);
internal sealed record NdsEventDocument(string Format, int Version, string Game, int Revision, string SourcePath, string SourceSha256,
    IReadOnlyList<NdsNpcEdit> Edits, IReadOnlyList<string> Audit);
internal sealed record NdsEventProposal(Guid Owner, int Revision, NdsNpcEdit Edit, bool Undo, IReadOnlyList<Gen5Change> Changes);

/// <summary>DS-specific immutable checkpoints. Every save publishes a NEW JSON file; the source NDS and previous checkpoints stay intact.</summary>
internal sealed class NdsEventSession
{
    internal const string Format = "DefinitiveRandomizer.NdsEvents";
    private readonly Guid _owner = Guid.NewGuid();
    private readonly object _gate = new();
    private readonly NdsRom _rom;
    private readonly PlatinumEvents _original;
    private readonly string _sha256;
    private List<NdsNpcEdit> _edits = [];
    private List<string> _audit = [];
    internal PlatinumEvents Data { get; private set; }
    internal int Revision { get; private set; }
    internal bool HasHistory => _audit.Count > 0;
    internal bool CanUndo => _edits.Count > 0;
    internal IReadOnlyList<string> Audit => _audit.AsReadOnly();
    internal string? LastCheckpoint { get; private set; }
    internal string SourcePath => _rom.Path;

    internal NdsEventSession(NdsRom rom, PlatinumEvents verifiedData)
    {
        _rom = rom; _original = Data = verifiedData;
        CheckPath(rom.Path);
        using var source = new FileStream(rom.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _sha256 = Convert.ToHexString(SHA256.HashData(source));
    }
    internal NdsEventProposal Preview(int map, int index, PlatinumNpcValues values)
    {
        lock (_gate)
        {
            var before = PlatinumEvents.Values(Data.Npc(map, index));
            _ = Data.ReplaceNpc(map, index, before, values);
            var edit = new NdsNpcEdit(map, index, before, values);
            return new(_owner, Revision, edit, false, Differences(edit));
        }
    }
    internal NdsEventProposal PreviewUndo()
    {
        lock (_gate)
        {
            if (_edits.Count == 0) throw new InvalidOperationException("No hay una edición de eventos que restaurar.");
            var last = _edits[^1];
            var reversed = last with { Before = last.After, After = last.Before };
            return new(_owner, Revision, reversed, true, Differences(reversed));
        }
    }
    private static IReadOnlyList<Gen5Change> Differences(NdsNpcEdit edit)
    {
        var changes = new List<Gen5Change>(); string name = $"Mapa {edit.Map} · NPC {edit.Index}";
        if (edit.Before.X != edit.After.X) changes.Add(new(name, "Coordenada X", edit.Before.X, edit.After.X));
        if (edit.Before.Y != edit.After.Y) changes.Add(new(name, "Coordenada Y", edit.Before.Y, edit.After.Y));
        if (edit.Before.Direction != edit.After.Direction) changes.Add(new(name, "Dirección", edit.Before.Direction, edit.After.Direction));
        return changes.AsReadOnly();
    }
    internal void Save(NdsEventProposal proposal, string checkpoint)
    {
        lock (_gate)
        {
            if (proposal.Owner != _owner || proposal.Revision != Revision) throw new InvalidOperationException("Vista previa ajena o desactualizada.");
            if (proposal.Changes.Count == 0) throw new InvalidOperationException("La propuesta no cambia ningún campo.");
            var edits = new List<NdsNpcEdit>(_edits);
            if (proposal.Undo)
            {
                if (edits.Count == 0 || proposal.Edit != (edits[^1] with { Before = edits[^1].After, After = edits[^1].Before }))
                    throw new InvalidDataException("La restauración no corresponde al último cambio.");
                edits.RemoveAt(edits.Count - 1);
            }
            else edits.Add(proposal.Edit);
            var next = Replay(edits);
            var audit = new List<string>(_audit) { (proposal.Undo ? "Restaurar" : "Guardar") + $" mapa {proposal.Edit.Map} NPC {proposal.Edit.Index}: " +
                string.Join("; ", proposal.Changes.Select(c => $"{c.Field} {c.Before} → {c.After}")) };
            var document = new NdsEventDocument(Format, 1, "CPUE", 1, SourcePath, _sha256, edits.AsReadOnly(), audit.AsReadOnly());
            CheckSource();
            WriteNew(checkpoint, JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true }), path =>
            {
                var reread = ReadDocument(path);
                ValidateDocument(reread);
                if (!Replay(reread.Edits).Bytes().SequenceEqual(next.Bytes())) throw new InvalidDataException("La relectura del checkpoint no coincide.");
            });
            // Commit memory only after a complete, verified checkpoint has been published.
            _edits = edits; _audit = audit; Data = next; LastCheckpoint = Path.GetFullPath(checkpoint); Revision++;
        }
    }
    internal void Load(NdsEventDocument document, string checkpoint)
    {
        lock (_gate)
        {
            ValidateDocument(document); CheckSource(); var next = Replay(document.Edits);
            _edits = document.Edits.ToList(); _audit = document.Audit.ToList(); Data = next; LastCheckpoint = Path.GetFullPath(checkpoint); Revision++;
        }
    }
    private void ValidateDocument(NdsEventDocument document)
    {
        if (document.Format != Format || document.Version != 1 || document.Game != "CPUE" || document.Revision != 1 ||
            document.SourceSha256 != _sha256 || !Path.GetFullPath(document.SourcePath).Equals(SourcePath, StringComparison.OrdinalIgnoreCase) ||
            document.Edits is null || document.Audit is null || document.Edits.Count > 10000 || document.Audit.Count > 20000 || document.Audit.Any(s => s is null || s.Length > 2048))
            throw new InvalidDataException("Checkpoint de eventos incompatible, truncado o de otra ROM.");
    }
    private PlatinumEvents Replay(IReadOnlyList<NdsNpcEdit> edits)
    {
        var result = _original;
        foreach (var edit in edits)
        {
            if (edit is null || edit.Before is null || edit.After is null) throw new InvalidDataException("Edición de NPC incompleta.");
            result = result.WithEvents(result.ReplaceNpc(edit.Map, edit.Index, edit.Before, edit.After));
        }
        return result;
    }
    private void CheckSource()
    {
        // NdsRom already holds a read-only handle with FileShare.Read, preventing Windows writers/deleters for the session.
        using var source = new FileStream(SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(source)) != _sha256) throw new IOException("La ROM fuente cambió externamente; se rechaza la operación.");
    }
    internal byte[]? Replacement()
    {
        lock (_gate) { CheckSource(); byte[] bytes = Data.Bytes(); return bytes.SequenceEqual(_original.Bytes()) ? null : bytes; }
    }
    internal void VerifyExport(string path)
    {
        lock (_gate)
        {
            using var rom = NdsRom.Open(path);
            if (!rom.ReadFile(PlatinumEvents.EventPath).SequenceEqual(Data.Bytes())) throw new InvalidDataException("Los eventos no persistieron en la ROM reconstruida.");
        }
    }
    internal void Reset() { lock (_gate) { Data = _original; _edits.Clear(); _audit.Clear(); LastCheckpoint = null; Revision++; } }

    internal static NdsEventDocument ReadDocument(string path)
    {
        CheckPath(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 8 * 1024 * 1024) throw new InvalidDataException("Tamaño de checkpoint de eventos inválido.");
        return JsonSerializer.Deserialize<NdsEventDocument>(input) ?? throw new InvalidDataException("Checkpoint vacío.");
    }
    internal static void WriteNew(string path, byte[] bytes, Action<string> verify)
    {
        string target = Path.GetFullPath(path); CheckPath(Path.GetDirectoryName(target)!);
        if (!target.EndsWith(".ndsevents.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Usa un archivo nuevo .ndsevents.json.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("El checkpoint ya existe; guarda una versión nueva. No se sobrescriben checkpoints.");
        string stage = target + ".partial-" + Guid.NewGuid().ToString("N"); bool owned = false;
        try
        {
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { owned = true; output.Write(bytes); output.Flush(true); }
            verify(stage);
            File.Move(stage, target); owned = false;
        }
        finally { if (owned) File.Delete(stage); }
    }
    private static void CheckPath(string path)
    {
        for (string? at = Path.GetFullPath(path); at is not null; at = Path.GetDirectoryName(at))
            if ((File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se permiten enlaces en la fuente o checkpoints de eventos.");
    }
}
