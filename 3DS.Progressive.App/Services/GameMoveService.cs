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
    }

    public IReadOnlyList<MoveListEntry> Entries { get; }
    public IReadOnlyList<string> Types { get; }

    public MoveValues Read(int id)
    {
        if (id < 1 || id >= _config.Moves.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        Move move = _config.Moves[id];
        // Move.Priority esta declarado como int pero respaldado por un byte;
        // la interfaz vanilla lo interpreta correctamente como sbyte.
        return new MoveValues(move.Type, move.Category, move.Power,
            move.Accuracy, move.PP, unchecked((sbyte)move.Priority));
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
            changes.Priority is < -8 or > 8)
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

        if (id >= records.Length)
            throw new InvalidDataException("El movimiento seleccionado no existe en el GARC.");
        int minimum = _config.Generation == 7 ? 0x28 : 0x22;
        if (records[id].Length < minimum)
            throw new InvalidDataException("Registro de movimiento incompleto. No se guardó nada.");

        byte[] edited = (byte[])records[id].Clone();
        // Solo tocar los seis campos admitidos; preserva flags, Z-Move,
        // efectos, objetivos, multigolpe y demás bytes.
        edited[0] = (byte)changes.Type;
        edited[2] = (byte)changes.Category;
        edited[3] = (byte)changes.Power;
        edited[4] = (byte)changes.Accuracy;
        edited[5] = (byte)changes.PP;
        edited[6] = unchecked((byte)(sbyte)changes.Priority);
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
