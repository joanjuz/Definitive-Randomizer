using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Segundo modulo real: lector/escritor de Move GARC, utilizando Mini WD segun el juego.
/// La serializacion replica el esquema de Main.B_Move_Click de pk3DS original.
/// </summary>
public sealed class GameMoveService
{
    private readonly GameConfig _config;
    private readonly string _path;
    private byte[] _sha;

    public GameMoveService(GameProjectSession project)
    {
        _config = project.GameConfiguration;
        _path = Path.Combine(_config.RomFS, _config.GetGARCFileName("move"));
        if (!File.Exists(_path))
            throw new FileNotFoundException("No se encontró el archivo de movimientos.", _path);
        _sha = SHA256.HashData(File.ReadAllBytes(_path));

        var names = _config.GetText(TextName.MoveNames);
        int length = Math.Min(_config.Moves.Length, names.Length);
        if (length <= 1)
            throw new InvalidDataException("No hay registros válidos en la tabla de movimientos.");
        Entries = Enumerable.Range(1, length - 1)
            .Select(id => new MoveListEntry(id,
                string.IsNullOrWhiteSpace(names[id]) ? $"Movimiento {id:000}" : names[id]))
            .ToArray();
        Types = _config.GetText(TextName.Types);
        AllMoveNames = Enumerable.Range(0, names.Length).Select(i =>
            new MoveChoice(i, string.IsNullOrWhiteSpace(names[i]) ? (i == 0 ? "Ninguno" : $"Movimiento {i:000}") :
                names[i] + (i is >= 622 and < 658 ? (i % 2 == 0 ? " (Físico)" : " (Especial)") : "")))
            .ToArray();
    }

    public IReadOnlyList<MoveListEntry> Entries { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<MoveChoice> AllMoveNames { get; }
    public IReadOnlyList<MoveChoice> Qualities { get; } = MoveEditorCatalog.IndexedChoices(MoveEditorCatalog.QualityNames, MoveEditorCatalog.QualityNames.Length);
    public IReadOnlyList<MoveChoice> ZEffects { get; } = MoveEditorCatalog.IndexedChoices(MoveEditorCatalog.ZEffectNames, MoveEditorCatalog.ZEffectNames.Length);
    public IReadOnlyList<MoveChoice> Targets { get; } = MoveEditorCatalog.IndexedChoices(MoveEditorCatalog.TargetNames, MoveEditorCatalog.TargetNames.Length);
    public IReadOnlyList<MoveChoice> Stats { get; } = MoveEditorCatalog.IndexedChoices(MoveEditorCatalog.StatNames, MoveEditorCatalog.StatNames.Length);
    public IReadOnlyList<MoveChoice> Inflictions { get; } = MoveEditorCatalog.InflictionChoices;
    public bool SupportsZMoves => _config.Generation == 7;

    public MoveValues Read(int id)
    {
        if (id < 1 || id >= _config.Moves.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        Move move = _config.Moves[id];
        // Move.Priority esta declarado como int pero respaldado por un byte;
        // la interfaz vanilla lo interpreta correctamente como sbyte.
        return new MoveValues(move.Type, move.Category, move.Power,
            move.Accuracy, move.PP, unchecked((sbyte)move.Priority))
        {
            Quality = move.Quality, HitMin = move.HitMin, HitMax = move.HitMax,
            Inflict = move.Inflict, InflictPercent = move.InflictPercent,
            CritStage = move.CritStage, Flinch = move.Flinch,
            EffectSequence = move.EffectSequence, Target = (int)move.Target,
            ZMove = move is Move7 m7 ? m7.ZMove : 0,
            ZPower = move is Move7 z7 ? z7.ZPower : 0,
            ZEffect = move is Move7 e7 ? e7.ZEffect : 0,
            Flags = move is Move7 f7 ? (uint)f7.Flags : (uint)((Move6)move).Flags,
            InflictCount = (int)move.InflictCount, TurnsMin = move.TurnMin,
            TurnsMax = move.TurnMax, Recoil = unchecked((sbyte)move.Recoil),
            Healing = (int)move.Healing,
            Stat1 = move.Stat1, Stat2 = move.Stat2, Stat3 = move.Stat3,
            Stat1Stage = unchecked((sbyte)move.Stat1Stage),
            Stat2Stage = unchecked((sbyte)move.Stat2Stage),
            Stat3Stage = unchecked((sbyte)move.Stat3Stage),
            Stat1Rate = move.Stat1Percent,
            Stat2Rate = move.Stat2Percent, Stat3Rate = move.Stat3Percent,
            RefreshType = move is Move7 rm ? (int)rm.RefreshAfflictType : 0,
            RefreshRate = move is Move7 rp ? rp.RefreshAfflictPercent : 0,
        };
    }

    public void Save(int id, MoveValues changes)
    {
        if (id < 1 || id >= _config.Moves.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (changes.Type < 0 || changes.Type >= Types.Count ||
            changes.Category is < 0 or > 2 ||
            changes.Power is < 0 or > 255 ||
            changes.Accuracy is < 0 or > 255 ||
            changes.PP is < 0 or > 255 ||
            changes.Priority is < -8 or > 8 ||
            changes.Quality is < 0 or > 255 ||
            changes.HitMin is < 0 or > 15 ||
            changes.HitMax is < 0 or > 15 ||
            changes.Inflict is < 0 or > 65535 ||
            changes.InflictPercent is < 0 or > 255 ||
            changes.CritStage is < 0 or > 255 ||
            changes.Flinch is < 0 or > 255 ||
            changes.EffectSequence is < 0 or > 65535 ||
            changes.Target is < 0 or > 255 ||
            changes.ZMove is < 0 or > 65535 ||
            changes.ZPower is < 0 or > 255 ||
            changes.ZEffect is < 0 or > 255 ||
            changes.InflictCount is < 0 or > 255 ||
            changes.TurnsMin is < 0 or > 255 ||
            changes.TurnsMax is < 0 or > 255 ||
            changes.Recoil is < -128 or > 127 ||
            changes.Healing is < 0 or > 255 ||
            changes.Stat1 is < 0 or > 255 ||
            changes.Stat2 is < 0 or > 255 ||
            changes.Stat3 is < 0 or > 255 ||
            changes.Stat1Stage is < -128 or > 127 ||
            changes.Stat2Stage is < -128 or > 127 ||
            changes.Stat3Stage is < -128 or > 127 ||
            changes.Stat1Rate is < 0 or > 255 ||
            changes.Stat2Rate is < 0 or > 255 ||
            changes.Stat3Rate is < 0 or > 255 ||
            changes.RefreshType is < 0 or > 255 ||
            changes.RefreshRate is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(changes),
                "Revisá tipo, categoría, potencia, precisión (0–255), PP (0–255) y prioridad (-8 a +8).");

        byte[] source = File.ReadAllBytes(_path);
        if (!_sha.AsSpan().SequenceEqual(SHA256.HashData(source)))
            throw new IOException("El GARC de movimientos cambió desde que lo abriste. Reabrí el proyecto.");

        var originalGarc = new GARC.MemGARC(source);
        byte[][] garcFiles = originalGarc.Files;
        bool mini = !_config.XY;
        byte[][] records = mini
            ? Mini.UnpackMini(garcFiles[0], "WD")
            : garcFiles;
        if (records is null) throw new InvalidDataException("Contenedor WD de movimientos no reconocido.");

        if (id >= records.Length)
            throw new InvalidDataException("El movimiento seleccionado no existe en el GARC.");
        int minimum = _config.Generation == 7 ? 0x28 : 0x22;
        if (records[id].Length < minimum)
            throw new InvalidDataException("Registro de movimiento incompleto. No se guardó nada.");

        byte[] edited = (byte[])records[id].Clone();
        // El editor genera campos equivalentes al vanilla; preserva bytes no expuestos.
        edited[0] = (byte)changes.Type;
        edited[2] = (byte)changes.Category;
        edited[3] = (byte)changes.Power;
        edited[4] = (byte)changes.Accuracy;
        edited[5] = (byte)changes.PP;
        edited[6] = unchecked((byte)(sbyte)changes.Priority);
        edited[1] = (byte)changes.Quality;
        edited[7] = (byte)(changes.HitMin | changes.HitMax << 4);
        BitConverter.GetBytes((ushort)changes.Inflict).CopyTo(edited, 8);
        edited[0xA] = (byte)changes.InflictPercent;
        edited[0xE] = (byte)changes.CritStage;
        edited[0xF] = (byte)changes.Flinch;
        BitConverter.GetBytes((ushort)changes.EffectSequence).CopyTo(edited, 0x10);
        edited[0x14] = (byte)changes.Target;
        edited[0x0B] = (byte)changes.InflictCount;
        edited[0x0C] = (byte)changes.TurnsMin;
        edited[0x0D] = (byte)changes.TurnsMax;
        edited[0x12] = unchecked((byte)(sbyte)changes.Recoil);
        edited[0x13] = (byte)changes.Healing;
        edited[0x15] = (byte)changes.Stat1;
        edited[0x16] = (byte)changes.Stat2;
        edited[0x17] = (byte)changes.Stat3;
        edited[0x18] = unchecked((byte)(sbyte)changes.Stat1Stage);
        edited[0x19] = unchecked((byte)(sbyte)changes.Stat2Stage);
        edited[0x1A] = unchecked((byte)(sbyte)changes.Stat3Stage);
        edited[0x1B] = (byte)changes.Stat1Rate;
        edited[0x1C] = (byte)changes.Stat2Rate;
        edited[0x1D] = (byte)changes.Stat3Rate;

        if (mini && _config.Generation == 7)
        {
            var z = new Move7(edited)
            {
                ZMove = changes.ZMove,
                ZPower = changes.ZPower,
                ZEffect = changes.ZEffect,
                Flags = (MoveFlag7)changes.Flags,
                RefreshAfflictType = (RefreshType)changes.RefreshType,
                RefreshAfflictPercent = changes.RefreshRate,
            };
        }
        else
            new Move6(edited).Flags = (MoveFlag6)changes.Flags;
        records[id] = edited;

        if (mini)
        {
            if (garcFiles.Length != 1)
                throw new InvalidDataException("Estructura inesperada del GARC de movimientos.");
            garcFiles[0] = Mini.PackMini(records, "WD");
        }
        else
            garcFiles = records;

        string temporary = _path + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var output = new GARCFile(
                new GARC.MemGARC(source), _config.GetGARCReference("move"), temporary);
            output.Files = garcFiles;
            output.Save();

            byte[] reloaded = File.ReadAllBytes(temporary);
            var verifiedGarc = new GARC.MemGARC(reloaded);
            if (verifiedGarc.FileCount != garcFiles.Length)
                throw new InvalidDataException("El GARC regenerado tiene un número de archivos distinto.");
            byte[][] verified = mini
                ? Mini.UnpackMini(verifiedGarc.GetFile(0), "WD")
                : verifiedGarc.Files;
            if (verified.Length != records.Length)
                throw new InvalidDataException("La tabla WD regenerada tiene un número de movimientos distinto.");
            for (int i = 0; i < records.Length; i++)
                if (!verified[i].AsSpan().SequenceEqual(records[i]))
                    throw new InvalidDataException($"No coincide el movimiento {i} en el archivo verificado.");

            if (!_sha.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(_path))))
                throw new IOException("El GARC cambió mientras se guardaba. No se reemplazó.");

            File.Replace(temporary, _path, null);
            _sha = SHA256.HashData(File.ReadAllBytes(_path));
            _config.InitializeMoves();
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
