using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

internal sealed record NdsNpcEdit(int Map, int Index, PlatinumNpcValues Before, PlatinumNpcValues After);
/// <summary>A new NPC record appended to the NPC block of a map's event file. <c>Index</c> is the position it takes (the NPC count before the addition).</summary>
/// <summary>
/// Behaviour attached to a new NPC. <c>AppendScript</c>: <c>CodeHex</c> is appended as the last local script of the map's script file and the NPC uses
/// it (only the Pokémon Center nurse pattern, re-targeted to the new NPC's own local id). <c>ShareScript</c>: the NPC uses an existing local script
/// of the same file that is a plain message (no local id, flag or variable references). Anything else is refused on replay.
/// </summary>
internal sealed record NdsScriptBinding(string Mode, int ScriptFile, int Number, string? CodeHex)
{
    internal const string Append = "AppendScript", Share = "ShareScript";
}
internal sealed record NdsNpcAddition(int Map, int Index, string RecordHex, NdsScriptBinding? Script = null);
/// <summary>Removal of an NPC that an earlier operation of this history ADDED (never an NPC of the original data). <c>RecordHex</c> must match.</summary>
internal sealed record NdsNpcRemoval(int Map, int Index, string RecordHex);
/// <summary>One recorded operation. Exactly one of the payloads is set, according to <c>Kind</c>.</summary>
internal sealed record NdsEventOperation(string Kind, NdsNpcEdit? Move, NdsNpcAddition? Add, NdsNpcRemoval? Remove = null)
{
    internal const string MoveKind = "MoveNpc", AddKind = "AddNpc", RemoveKind = "RemoveAddedNpc";
    internal static NdsEventOperation OfRemove(NdsNpcRemoval remove) => new(RemoveKind, null, null, remove);
    internal static NdsEventOperation OfMove(NdsNpcEdit edit) => new(MoveKind, edit, null);
    internal static NdsEventOperation OfAdd(NdsNpcAddition add) => new(AddKind, null, add);
    internal int Map => Move?.Map ?? Add?.Map ?? Remove?.Map ?? -1;
}
/// <summary>
/// Version 1: moves only (<c>Edits</c>), exactly as before. Version 2: ordered <c>Operations</c> (moves and NPC additions); <c>Edits</c> is empty.
/// A session that only moved NPC still writes version 1, so those files stay readable by older builds.
/// </summary>
internal sealed record NdsEventDocument(string Format, int Version, string Game, int Revision, string SourcePath, string SourceSha256,
    IReadOnlyList<NdsNpcEdit> Edits, IReadOnlyList<string> Audit, IReadOnlyList<NdsEventOperation>? Operations = null);
/// <summary>A save that has been decided but not installed (see <c>NdsEventSession.Stage</c>).</summary>
internal sealed record NdsEventStage(Guid Owner, int Revision, List<NdsEventOperation> Operations, List<string> Audit, PlatinumEvents Next);
internal sealed record NdsEventProposal(Guid Owner, int Revision, NdsEventOperation Operation, bool Undo, IReadOnlyList<Gen5Change> Changes, IReadOnlyList<string>? Lines = null)
{
    internal bool IsStructural => Operation.Kind is NdsEventOperation.AddKind or NdsEventOperation.RemoveKind;
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
    internal bool HasStructuralOperations => _ops.Any(o => o.Kind != NdsEventOperation.MoveKind);
    /// <summary>NPC of the ORIGINAL data occupy indices below this count; anything at or above it was added by this history.</summary>
    internal int OriginalNpcCount(int map) => _original.NpcCount(map);
    internal bool IsAddedNpc(int map, int index) => index >= _original.NpcCount(map) && index < Data.NpcCount(map);
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
    internal NdsEventProposal PreviewAdd(int map, byte[] record, NdsScriptBinding? script = null)
    {
        lock (_gate)
        {
            int index = Data.NpcCount(map); var addition = new NdsNpcAddition(map, index, Convert.ToHexString(record), script);
            var added = ApplyAddition(Data, addition, record);   // throws with every violated rule
            return new(_owner, Revision, NdsEventOperation.OfAdd(addition), false, AdditionChanges(addition, added.NpcCount(map)), AdditionLines(addition, record));
        }
    }
    /// <summary>Previews removing an NPC that this history added (any position). NPC of the original data are refused.</summary>
    internal NdsEventProposal PreviewRemoveAdded(int map, int index)
    {
        lock (_gate)
        {
            if (!IsAddedNpc(map, index)) throw new InvalidOperationException("Solo se pueden eliminar NPC añadidos por el editor; los del juego original no se eliminan.");
            var npc = Data.Npc(map, index); byte[] record = Convert.FromHexString(npc.Raw);
            _ = Data.RemoveNpc(map, index, record);
            var removal = new NdsNpcRemoval(map, index, npc.Raw); int count = Data.NpcCount(map);
            return new(_owner, Revision, NdsEventOperation.OfRemove(removal), false, [new($"Mapa {map} · NPC añadido {index}", "Cantidad de NPC", count, count - 1)],
                [$"Mapa {map} · eliminar el NPC añadido (índice {index}, ID {npc.Id})", $"  X {npc.X} · Y {npc.Y} · gráfico {npc.Graphics}", index < count - 1 ? $"  Los NPC añadidos posteriores pasan del índice {index + 1}… al {index}…" : "  Es el último NPC del bloque.", $"  Cantidad de NPC: {count} → {count - 1}"]);
        }
    }
    internal NdsEventProposal PreviewUndo()
    {
        lock (_gate)
        {
            if (_ops.Count == 0) throw new InvalidOperationException("No hay una edición de eventos que restaurar.");
            var last = _ops[^1];
            if (last.Kind == NdsEventOperation.RemoveKind)
                return new(_owner, Revision, last, true, [new($"Mapa {last.Remove!.Map} · NPC añadido {last.Remove.Index}", "Cantidad de NPC", Data.NpcCount(last.Remove.Map), Data.NpcCount(last.Remove.Map) + 1)], [$"Mapa {last.Remove.Map} · volver a poner el NPC añadido eliminado (índice {last.Remove.Index})"]);
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
        string behaviour = add.Script switch
        {
            { Mode: NdsScriptBinding.Append } b => $"  Comportamiento: script local {b.Number} AÑADIDO al archivo de scripts {b.ScriptFile} (enfermera: ID local {U16(0)}, script común {PlatinumScriptFile.PokecenterNurseCommonScript})",
            { Mode: NdsScriptBinding.Share } b => $"  Comportamiento: comparte el script local {b.Number} del archivo {b.ScriptFile} (mensaje simple); no se añade nada",
            _ => "  Comportamiento: solo apariencia (script 0)",
        };
        return [head, $"  ID {U16(0)} · gráfico {U16(2)} · movimiento {U16(4)} · dirección {U16(12)}", $"  X {S16(24)} · Y {S16(26)} · script {U16(10)} · flag 0 · tipo 0", behaviour,
            undo ? "  Cantidad de NPC: " + (add.Index + 1) + " → " + add.Index : "  Cantidad de NPC: " + add.Index + " → " + (add.Index + 1)];
    }
    /// <summary>Applies one addition, with its script binding, re-checking every rule (used by preview and by replay).</summary>
    private static PlatinumEvents ApplyAddition(PlatinumEvents from, NdsNpcAddition add, byte[] record)
    {
        if (add.Script is not { } b) return from.AddNpc(add.Map, record);
        var map = from.Map(add.Map); int recordScript = record[10] | record[11] << 8, recordId = record[0] | record[1] << 8;
        if (b.ScriptFile != map.ScriptFile) throw new InvalidOperationException("El script no pertenece al archivo de scripts del mapa.");
        switch (b.Mode)
        {
            case NdsScriptBinding.Append:
                byte[] code = Convert.FromHexString(b.CodeHex ?? "");
                if (!code.AsSpan().SequenceEqual(PlatinumScriptFile.NurseScript(recordId))) throw new InvalidOperationException("Solo se añade el script acreditado de la enfermera, con el ID local del NPC nuevo.");
                var withScript = from.WithAppendedScript(add.Map, code, out int number);
                if (number != b.Number || recordScript != number) throw new InvalidOperationException("El número del script añadido no coincide con el del NPC.");
                return withScript.AddNpc(add.Map, record, number);
            case NdsScriptBinding.Share:
                if (b.CodeHex is not null || recordScript != b.Number || from.ClassifyScript(add.Map, b.Number)?.Pattern != PlatinumScriptPattern.PlainMessage)
                    throw new InvalidOperationException("Solo se comparten scripts locales de mensaje simple del mismo archivo.");
                return from.AddNpc(add.Map, record, b.Number);
            default: throw new InvalidOperationException("Modo de script desconocido: " + b.Mode);
        }
    }
    /// <summary>
    /// Everything a save decides, without writing anything and without changing the session: the resulting operations, the audit and the replayed data.
    /// It is installed with <see cref="Install"/> once it has been made durable (a checkpoint, or the persistent project).
    /// </summary>
    internal NdsEventStage Stage(NdsEventProposal proposal)
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
                bool matches = proposal.Operation.Kind == last.Kind && (last.Kind != NdsEventOperation.MoveKind
                    ? proposal.Operation == last
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
            string what = proposal.Operation.Kind switch { NdsEventOperation.AddKind => $"NPC nuevo índice {proposal.Operation.Add!.Index}", NdsEventOperation.RemoveKind => $"NPC añadido índice {proposal.Operation.Remove!.Index}", _ => $"NPC {proposal.Operation.Move!.Index}" };
            string detail = proposal.Operation.Kind == NdsEventOperation.AddKind ? string.Join("; ", AdditionLines(proposal.Operation.Add!, Convert.FromHexString(proposal.Operation.Add!.RecordHex), proposal.Undo).Skip(1).Select(l => l.Trim()))
                : proposal.Operation.Kind == NdsEventOperation.RemoveKind ? "registro " + proposal.Operation.Remove!.RecordHex
                : string.Join("; ", proposal.Changes.Select(c => $"{c.Field} {c.Before} → {c.After}"));
            string verb = proposal.Undo ? "Restaurar" : proposal.Operation.Kind switch { NdsEventOperation.AddKind => "Añadir", NdsEventOperation.RemoveKind => "Eliminar", _ => "Guardar" };
            var audit = new List<string>(_audit) { verb + $" mapa {proposal.Operation.Map} {what}: " + detail };
            return new(_owner, Revision, ops, audit, next);
        }
    }
    internal void Save(NdsEventProposal proposal, string checkpoint)
    {
        lock (_gate)
        {
            var stage = Stage(proposal); var next = stage.Next;
            var document = Build(stage.Operations, stage.Audit);
            CheckSource();
            WriteNew(checkpoint, JsonSerializer.SerializeToUtf8Bytes(document, Json), path =>
            {
                var reread = ReadDocument(path);
                ValidateDocument(reread);
                if (!Replay(Operations(reread)).Bytes().SequenceEqual(next.Bytes())) throw new InvalidDataException("La relectura del checkpoint no coincide.");
            });
            // Commit memory only after a complete, verified checkpoint has been published.
            Install(stage, checkpoint);
        }
    }
    /// <summary>Makes a staged save the state of the session. The stage must come from this session at its current revision.</summary>
    internal void Install(NdsEventStage stage, string? checkpoint = null)
    {
        lock (_gate)
        {
            if (stage.Owner != _owner || stage.Revision != Revision) throw new InvalidOperationException("El guardado preparado es de otra sesión o quedó desactualizado.");
            _ops = stage.Operations; _audit = stage.Audit; Data = stage.Next; LastCheckpoint = checkpoint is null ? null : Path.GetFullPath(checkpoint); Revision++;
        }
    }
    internal IReadOnlyList<NdsEventOperation> History() { lock (_gate) return _ops.ToList().AsReadOnly(); }
    /// <summary>The event data of a state when it differs from the original (what a persistent project stores), without re-reading the ROM.</summary>
    internal byte[]? EventBytesIfChanged(PlatinumEvents data) { byte[] bytes = data.Bytes(); return bytes.AsSpan().SequenceEqual(_original.Bytes()) ? null : bytes; }
    internal byte[]? ScriptBytesIfChanged(PlatinumEvents data) { byte[] bytes = data.ScriptArchiveBytes(); return bytes.AsSpan().SequenceEqual(_original.ScriptArchiveBytes()) ? null : bytes; }
    /// <summary>
    /// Installs a history read from a persistent project. It is replayed on the original data with every rule, and the result must be, byte for byte, the event
    /// data and the script archive the project stored (null means "the original"); otherwise nothing is installed.
    /// </summary>
    internal void Restore(IReadOnlyList<NdsEventOperation> operations, IReadOnlyList<string> audit, byte[]? eventData, byte[]? scripts)
    {
        lock (_gate)
        {
            if (operations.Count > 10000 || audit.Count > 20000 || audit.Any(s => s is null || s.Length > 2048)) throw new InvalidDataException("El historial de eventos del proyecto supera los límites admitidos.");
            PlatinumEvents next;
            // Whatever stops the replay (a record that is not the expected one, a coordinate out of range) means the same: this history is not of this data.
            try { next = Replay(operations); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { throw new InvalidDataException("El historial de eventos del proyecto no se puede reproducir: " + ex.Message, ex); }
            if (!next.Bytes().AsSpan().SequenceEqual(eventData ?? _original.Bytes()) || !next.ScriptArchiveBytes().AsSpan().SequenceEqual(scripts ?? _original.ScriptArchiveBytes()))
                throw new InvalidDataException("El historial de eventos del proyecto no reproduce los datos guardados; no se abre para no alterar el trabajo.");
            _ops = operations.ToList(); _audit = audit.ToList(); Data = next; LastCheckpoint = null; Revision++;
        }
    }
    private NdsEventDocument Build(List<NdsEventOperation> ops, List<string> audit) =>
        ops.All(o => o.Kind == NdsEventOperation.MoveKind)
            ? new(Format, 1, "CPUE", 1, SourcePath, _sha256, ops.Select(o => o.Move!).ToList().AsReadOnly(), audit.AsReadOnly())
            : new(Format, ops.Any(o => o.Add?.Script is not null) ? 3 : 2, "CPUE", 1, SourcePath, _sha256, Array.Empty<NdsNpcEdit>(), audit.AsReadOnly(), ops.AsReadOnly());
    private static IReadOnlyList<NdsEventOperation> Operations(NdsEventDocument document) =>
        document.Version >= 2 ? document.Operations! : document.Edits.Select(NdsEventOperation.OfMove).ToList();
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
            : document.Version is 2 or 3 && document.Operations is not null && document.Operations.Count > 0 && document.Edits is { Count: 0 }
              && (document.Version == 3 ? document.Operations.Any(o => o?.Add?.Script is not null) : document.Operations.All(o => o?.Add?.Script is null));
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
                    if (edit is null || edit.Before is null || edit.After is null || operation.Add is not null || operation.Remove is not null) throw new InvalidDataException("Edición de NPC incompleta.");
                    result = result.WithEvents(result.ReplaceNpc(edit.Map, edit.Index, edit.Before, edit.After));
                    break;
                case NdsEventOperation.AddKind:
                    var add = operation.Add;
                    if (add is null || operation.Move is not null || operation.Remove is not null || string.IsNullOrEmpty(add.RecordHex) || add.RecordHex.Length != 64) throw new InvalidDataException("Adición de NPC incompleta.");
                    byte[] record;
                    try { record = Convert.FromHexString(add.RecordHex); } catch (FormatException ex) { throw new InvalidDataException("Registro de NPC ilegible en el checkpoint.", ex); }
                    if (add.Map < 0 || add.Map >= result.Maps.Count) throw new InvalidDataException("Mapa inexistente en la adición de NPC.");
                    if (add.Index != result.NpcCount(add.Map)) throw new InvalidDataException("El índice del NPC añadido no coincide con el estado reconstruido.");
                    try { result = ApplyAddition(result, add, record); }
                    catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { throw new InvalidDataException(ex.Message, ex); }
                    break;
                case NdsEventOperation.RemoveKind:
                    var remove = operation.Remove;
                    if (remove is null || operation.Move is not null || operation.Add is not null || string.IsNullOrEmpty(remove.RecordHex) || remove.RecordHex.Length != 64) throw new InvalidDataException("Eliminación de NPC incompleta.");
                    if (remove.Map < 0 || remove.Map >= result.Maps.Count) throw new InvalidDataException("Mapa inexistente en la eliminación de NPC.");
                    if (remove.Index < _original.NpcCount(remove.Map) || remove.Index >= result.NpcCount(remove.Map)) throw new InvalidDataException("Solo se pueden eliminar NPC añadidos por el editor.");
                    byte[] expected;
                    try { expected = Convert.FromHexString(remove.RecordHex); } catch (FormatException ex) { throw new InvalidDataException("Registro de NPC ilegible en el checkpoint.", ex); }
                    try { result = result.RemoveNpc(remove.Map, remove.Index, expected); }
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
    /// <summary>The script archive when a history appended scripts; null otherwise (the original archive is exported as it is).</summary>
    internal byte[]? ScriptReplacement()
    {
        lock (_gate) { CheckSource(); byte[] bytes = Data.ScriptArchiveBytes(); return bytes.SequenceEqual(_original.ScriptArchiveBytes()) ? null : bytes; }
    }
    /// <summary>
    /// Combines the scripts appended by this history with a script archive written by another DS module (randomizer options also edit scr_seq).
    /// A member changed by both is refused: the two edits are never mixed blindly.
    /// </summary>
    internal byte[] MergeScriptArchive(byte[] theirs, PlatinumEvents? state = null)
    {
        lock (_gate)
        {
            var original = Narc.Read(_original.ScriptArchiveBytes()).Files; var mine = Narc.Read((state ?? Data).ScriptArchiveBytes()).Files; var merged = Narc.Read(theirs);
            if (merged.Files.Count != original.Count) throw new InvalidOperationException("El archivo de scripts de otro módulo no tiene el mismo número de miembros.");
            for (int i = 0; i < original.Count; i++)
            {
                if (mine[i].AsSpan().SequenceEqual(original[i])) continue;
                if (!merged.Files[i].AsSpan().SequenceEqual(original[i])) throw new InvalidOperationException($"Otro módulo DS también modificó el archivo de scripts {i}; no se combinan dos ediciones del mismo archivo.");
                merged.Files[i] = mine[i];
            }
            return merged.Write();
        }
    }
    /// <summary>The same rule as <see cref="MergeScriptArchive"/>, for two archives that are not installed yet: no script file changed by both.</summary>
    internal void RequireMergeable(byte[] mineArchive, byte[] theirs)
    {
        var original = Narc.Read(_original.ScriptArchiveBytes()).Files; var mine = Narc.Read(mineArchive).Files; var other = Narc.Read(theirs).Files;
        if (mine.Count != original.Count || other.Count != original.Count) throw new InvalidOperationException("El archivo de scripts no tiene el mismo número de miembros.");
        for (int i = 0; i < original.Count; i++)
            if (!mine[i].AsSpan().SequenceEqual(original[i]) && !other[i].AsSpan().SequenceEqual(original[i])) throw new InvalidOperationException($"Otro módulo DS también modificó el archivo de scripts {i}; no se combinan dos ediciones del mismo archivo.");
    }
    internal void VerifyExport(string path)
    {
        lock (_gate)
        {
            using var rom = NdsRom.Open(path);
            if (!rom.ReadFile(PlatinumEvents.EventPath).SequenceEqual(Data.Bytes())) throw new InvalidDataException("Los eventos no persistieron en la ROM reconstruida.");
            var written = Narc.Read(rom.ReadFile(PlatinumEvents.ScriptPath)).Files; var mine = Narc.Read(Data.ScriptArchiveBytes()).Files; var original = Narc.Read(_original.ScriptArchiveBytes()).Files;
            if (written.Count != mine.Count || Enumerable.Range(0, mine.Count).Any(i => !mine[i].AsSpan().SequenceEqual(original[i]) && !written[i].AsSpan().SequenceEqual(mine[i])))
                throw new InvalidDataException("Los scripts añadidos no persistieron en la ROM reconstruida.");
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
