using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed record NdsNpcEdit(int Map, int Index, PlatinumNpcValues Before, PlatinumNpcValues After);
/// <summary>A new NPC record appended to the NPC block of a map's event file. <c>Index</c> is the position it takes (the NPC count before the addition).</summary>
internal sealed record NdsNpcAddition(int Map, int Index, string RecordHex);
/// <summary>One recorded operation. Exactly one of the payloads is set, according to <c>Kind</c>.</summary>
internal sealed record NdsEventOperation(string Kind, NdsNpcEdit? Move, NdsNpcAddition? Add)
{
    internal const string MoveKind = "MoveNpc", AddKind = "AddNpc";
    internal static NdsEventOperation OfMove(NdsNpcEdit edit) => new(MoveKind, edit, null);
    internal static NdsEventOperation OfAdd(NdsNpcAddition add) => new(AddKind, null, add);
    internal int Map => Move?.Map ?? Add?.Map ?? -1;
}
/// <summary>
/// Version 1: moves only (<c>Edits</c>), exactly as before. Version 2: ordered <c>Operations</c> (moves and NPC additions); <c>Edits</c> is empty.
/// A session that only moved NPC still writes version 1, so those files stay readable by older builds.
/// </summary>
internal sealed record NdsEventDocument(string Format, int Version, string Game, int Revision, string SourcePath, string SourceSha256,
    IReadOnlyList<NdsNpcEdit> Edits, IReadOnlyList<string> Audit, IReadOnlyList<NdsEventOperation>? Operations = null);
internal sealed record NdsEventProposal(Guid Owner, int Revision, NdsEventOperation Operation, bool Undo, IReadOnlyList<Gen5Change> Changes, IReadOnlyList<string>? Lines = null)
{
    internal bool IsStructural => Operation.Kind == NdsEventOperation.AddKind;
}

/// <summary>DS-specific immutable checkpoints. Every save publishes a NEW JSON file; the source NDS and previous checkpoints stay intact.</summary>
internal sealed class NdsEventSession
{
    internal const string Format = "DefinitiveRandomizer.NdsEvents";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly Guid _owner = Guid.NewGuid();
    private readonly object _gate = new();
    private readonly NdsRom _rom;
    private readonly PlatinumEvents _original;
    private readonly string _sha256;
    private List<NdsEventOperation> _ops = [];
    private List<string> _audit = [];
    internal PlatinumEvents Data { get; private set; }
    internal int Revision { get; private set; }
    internal bool HasHistory => _audit.Count > 0;
    internal bool CanUndo => _ops.Count > 0;
    /// <summary>The addition recorded by the last operation, if that is what it was (its exact undo is "Restaurar último cambio").</summary>
    internal NdsNpcAddition? LastAddition => _ops.Count > 0 ? _ops[^1].Add : null;
    internal bool HasStructuralOperations => _ops.Any(o => o.Kind == NdsEventOperation.AddKind);
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
            return new(_owner, Revision, NdsEventOperation.OfMove(edit), false, Differences(edit));
        }
    }
    /// <summary>Previews appending a new NPC. Nothing changes until the proposal is saved into a checkpoint.</summary>
    internal NdsEventProposal PreviewAdd(int map, byte[] record)
    {
        lock (_gate)
        {
            var added = Data.AddNpc(map, record);   // throws with every violated rule
            int index = Data.NpcCount(map); var addition = new NdsNpcAddition(map, index, Convert.ToHexString(record));
            return new(_owner, Revision, NdsEventOperation.OfAdd(addition), false, AdditionChanges(addition, added.NpcCount(map)), AdditionLines(addition, record));
        }
    }
    internal NdsEventProposal PreviewUndo()
    {
        lock (_gate)
        {
            if (_ops.Count == 0) throw new InvalidOperationException("No hay una edición de eventos que restaurar.");
            var last = _ops[^1];
            if (last.Kind == NdsEventOperation.AddKind)
                return new(_owner, Revision, last, true, AdditionChanges(last.Add!, last.Add!.Index, undo: true), AdditionLines(last.Add!, Convert.FromHexString(last.Add!.RecordHex), undo: true));
            var move = last.Move!; var reversed = move with { Before = move.After, After = move.Before };
            return new(_owner, Revision, NdsEventOperation.OfMove(reversed), true, Differences(reversed));
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
    private static IReadOnlyList<Gen5Change> AdditionChanges(NdsNpcAddition add, int countAfter, bool undo = false) =>
        [new($"Mapa {add.Map} · NPC nuevo (índice {add.Index})", "Cantidad de NPC", undo ? add.Index + 1 : add.Index, undo ? add.Index : add.Index + 1)];
    private static IReadOnlyList<string> AdditionLines(NdsNpcAddition add, byte[] record, bool undo = false)
    {
        int U16(int at) => record[at] | record[at + 1] << 8; int S16(int at) => (short)U16(at);
        string head = undo ? $"Mapa {add.Map} · quitar el NPC añadido (índice {add.Index}, ID {U16(0)})" : $"Mapa {add.Map} · NPC nuevo (índice {add.Index})";
        return [head, $"  ID {U16(0)} · gráfico {U16(2)} · movimiento {U16(4)} · dirección {U16(12)}", $"  X {S16(24)} · Y {S16(26)} · script 0 · flag 0 · tipo 0",
            undo ? "  Cantidad de NPC: " + (add.Index + 1) + " → " + add.Index : "  Cantidad de NPC: " + add.Index + " → " + (add.Index + 1)];
    }
    internal void Save(NdsEventProposal proposal, string checkpoint)
    {
        lock (_gate)
        {
            if (proposal.Owner != _owner || proposal.Revision != Revision) throw new InvalidOperationException("Vista previa ajena o desactualizada.");
            if (proposal.Changes.Count == 0) throw new InvalidOperationException("La propuesta no cambia ningún campo.");
            var ops = new List<NdsEventOperation>(_ops);
            if (proposal.Undo)
            {
                if (ops.Count == 0) throw new InvalidDataException("La restauración no corresponde al último cambio.");
                var last = ops[^1];
                bool matches = proposal.Operation.Kind == last.Kind && (last.Kind == NdsEventOperation.AddKind
                    ? proposal.Operation.Add == last.Add
                    : proposal.Operation.Move == (last.Move! with { Before = last.Move!.After, After = last.Move!.Before }));
                if (!matches) throw new InvalidDataException("La restauración no corresponde al último cambio.");
                ops.RemoveAt(ops.Count - 1);
            }
            else
            {
                if (proposal.Operation.Kind == NdsEventOperation.AddKind && proposal.Operation.Add!.Index != Data.NpcCount(proposal.Operation.Add.Map))
                    throw new InvalidDataException("El índice del NPC nuevo ya no corresponde al final del bloque.");
                ops.Add(proposal.Operation);
            }
            var next = Replay(ops);
            string what = proposal.Operation.Kind == NdsEventOperation.AddKind ? $"NPC nuevo índice {proposal.Operation.Add!.Index}" : $"NPC {proposal.Operation.Move!.Index}";
            string detail = proposal.Operation.Kind == NdsEventOperation.AddKind ? string.Join("; ", AdditionLines(proposal.Operation.Add!, Convert.FromHexString(proposal.Operation.Add!.RecordHex), proposal.Undo).Skip(1).Select(l => l.Trim()))
                : string.Join("; ", proposal.Changes.Select(c => $"{c.Field} {c.Before} → {c.After}"));
            var audit = new List<string>(_audit) { (proposal.Undo ? "Restaurar" : proposal.Operation.Kind == NdsEventOperation.AddKind ? "Añadir" : "Guardar") + $" mapa {proposal.Operation.Map} {what}: " + detail };
            var document = Build(ops, audit);
            CheckSource();
            WriteNew(checkpoint, JsonSerializer.SerializeToUtf8Bytes(document, Json), path =>
            {
                var reread = ReadDocument(path);
                ValidateDocument(reread);
                if (!Replay(Operations(reread)).Bytes().SequenceEqual(next.Bytes())) throw new InvalidDataException("La relectura del checkpoint no coincide.");
            });
            // Commit memory only after a complete, verified checkpoint has been published.
            _ops = ops; _audit = audit; Data = next; LastCheckpoint = Path.GetFullPath(checkpoint); Revision++;
        }
    }
    private NdsEventDocument Build(List<NdsEventOperation> ops, List<string> audit) =>
        ops.All(o => o.Kind == NdsEventOperation.MoveKind)
            ? new(Format, 1, "CPUE", 1, SourcePath, _sha256, ops.Select(o => o.Move!).ToList().AsReadOnly(), audit.AsReadOnly())
            : new(Format, 2, "CPUE", 1, SourcePath, _sha256, Array.Empty<NdsNpcEdit>(), audit.AsReadOnly(), ops.AsReadOnly());
    private static IReadOnlyList<NdsEventOperation> Operations(NdsEventDocument document) =>
        document.Version == 2 ? document.Operations! : document.Edits.Select(NdsEventOperation.OfMove).ToList();
    internal void Load(NdsEventDocument document, string checkpoint)
    {
        lock (_gate)
        {
            ValidateDocument(document); CheckSource(); var ops = Operations(document); var next = Replay(ops);
            _ops = ops.ToList(); _audit = document.Audit.ToList(); Data = next; LastCheckpoint = Path.GetFullPath(checkpoint); Revision++;
        }
    }
    private void ValidateDocument(NdsEventDocument document)
    {
        bool shape = document.Version == 1 ? document.Operations is null && document.Edits is not null
            : document.Version == 2 && document.Operations is not null && document.Operations.Count > 0 && document.Edits is { Count: 0 };
        if (document.Format != Format || !shape || document.Game != "CPUE" || document.Revision != 1 ||
            document.SourceSha256 != _sha256 || !Path.GetFullPath(document.SourcePath).Equals(SourcePath, StringComparison.OrdinalIgnoreCase) ||
            document.Audit is null || (document.Edits?.Count ?? 0) > 10000 || (document.Operations?.Count ?? 0) > 10000 || document.Audit.Count > 20000 || document.Audit.Any(s => s is null || s.Length > 2048))
            throw new InvalidDataException("Checkpoint de eventos incompatible, truncado o de otra ROM.");
    }
    private PlatinumEvents Replay(IReadOnlyList<NdsEventOperation> operations)
    {
        var result = _original;
        foreach (var operation in operations)
        {
            if (operation is null) throw new InvalidDataException("Operación de eventos incompleta.");
            switch (operation.Kind)
            {
                case NdsEventOperation.MoveKind:
                    var edit = operation.Move;
                    if (edit is null || edit.Before is null || edit.After is null || operation.Add is not null) throw new InvalidDataException("Edición de NPC incompleta.");
                    result = result.WithEvents(result.ReplaceNpc(edit.Map, edit.Index, edit.Before, edit.After));
                    break;
                case NdsEventOperation.AddKind:
                    var add = operation.Add;
                    if (add is null || operation.Move is not null || string.IsNullOrEmpty(add.RecordHex) || add.RecordHex.Length != 64) throw new InvalidDataException("Adición de NPC incompleta.");
                    byte[] record;
                    try { record = Convert.FromHexString(add.RecordHex); } catch (FormatException ex) { throw new InvalidDataException("Registro de NPC ilegible en el checkpoint.", ex); }
                    if (add.Map < 0 || add.Map >= result.Maps.Count) throw new InvalidDataException("Mapa inexistente en la adición de NPC.");
                    if (add.Index != result.NpcCount(add.Map)) throw new InvalidDataException("El índice del NPC añadido no coincide con el estado reconstruido.");
                    try { result = result.AddNpc(add.Map, record); }
                    catch (InvalidOperationException ex) { throw new InvalidDataException(ex.Message, ex); }
                    break;
                default: throw new InvalidDataException("Operación de eventos desconocida: " + operation.Kind);
            }
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
    internal void Reset() { lock (_gate) { Data = _original; _ops.Clear(); _audit.Clear(); LastCheckpoint = null; Revision++; } }

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
