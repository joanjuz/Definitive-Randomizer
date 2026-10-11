using System.IO;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed partial class GameMoveService
{
    private readonly Guid _batchOwner = Guid.NewGuid();

    public BatchPreview PreviewMoves(BatchAction action, BatchSettings settings, IReadOnlyList<int> scope)
    {
        if (action is not (BatchAction.MoveRandomize or BatchAction.Metronome))
            throw new ArgumentException("Acción de movimientos no reconocida.");
        int[] ids = scope.Distinct().Order().ToArray();
        byte[][] outer = new GARC.MemGARC(GarcTransaction.ReadUnchanged(_path, _sha)).Files;
        if (!_config.XY && outer.Length != 1) throw new InvalidDataException("GARC WD no reconocido.");
        byte[][] records = _config.XY ? outer : Mini.UnpackMini(outer[0], "WD");
        if (records is null || ids.Length == 0 || ids.Any(i => i < 1 || i >= records.Length))
            throw new ArgumentException("El alcance no contiene movimientos válidos.");
        var random = new Random(settings.Seed);
        var changes = new List<BatchChange>();
        foreach (int id in ids)
        {
            if (records[id].Length < (_config.Generation == 7 ? 0x28 : 0x22))
                throw new InvalidDataException($"Movimiento {id} incompleto.");
            Move before = _config.Generation == 7 ? new Move7((byte[])records[id].Clone()) : new Move6((byte[])records[id].Clone());
            Move after = _config.Generation == 7 ? new Move7(records[id]) : new Move6(records[id]);
            BatchAlgorithms.ChangeMove(after, id, action, settings, random, Types.Count);
            changes.AddRange(BatchDifferences.Moves(id,
                Entries.FirstOrDefault(e => e.Id == id)?.Name ?? $"Movimiento {id}", before, after, Types));
        }
        return new BatchPreview(_batchOwner, _sha, records,
            action == BatchAction.Metronome ? "Modo Metrónomo" : "Randomizar movimientos", settings.Seed, ids.Length, changes);
    }

    public void ApplyMovePreview(BatchPreview preview)
    {
        if (preview.Owner != _batchOwner || !preview.Fingerprint.AsSpan().SequenceEqual(_sha))
            throw new InvalidOperationException("La vista previa pertenece a otra sesión o quedó desactualizada.");
        _sha = GarcTransaction.Commit(_path, _config.GetGARCReference("move"), preview.Fingerprint,
            preview.CopyRecords(), !_config.XY);
        _config.InitializeMoves();
    }
}
