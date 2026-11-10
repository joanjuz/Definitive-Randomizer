#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.NDS;

public enum PlatinumEventKind { Object, Npc, Warp, Trigger }
public sealed record PlatinumMap(int Id, string Name, string InternalName, int EventFile, int ScriptFile, int Matrix, int Width, int Height)
{
    public string Display => $"{Id:D3} · {Name} · {InternalName}";
}
public sealed record PlatinumEvent(PlatinumEventKind Kind, int Index, int Id, int X, int Y, int Z, int Script, int Type, int Direction,
    int Graphics, int Movement, int Flag, int Sight, int RangeX, int RangeY, int Destination, int Anchor, string Raw)
{
    public string Description => Kind switch
    {
        PlatinumEventKind.Npc when Type == 3 => "NPC · objeto de suelo (referencia de script)",
        PlatinumEventKind.Npc => "NPC existente",
        PlatinumEventKind.Object when Type == 2 => "Evento de fondo · objeto oculto",
        PlatinumEventKind.Object => "Evento de fondo / cartel",
        PlatinumEventKind.Warp => "Teletransporte (solo consulta)",
        _ => "Activador de script (solo consulta)",
    };
}
public sealed record PlatinumNpcValues(int X, int Y, int Direction);
public sealed record PlatinumScriptLink(int Reference, int Archive, int? Entry, int? Offset, string Status, string Bytes);

/// <summary>
/// CPUE/1 only. Binary facts researched against DSPRE, independently implemented and checked against the owner's dump.
/// No DSPRE code/assets included. NdsRom/Narc own container parsing; fixed-size event edits patch the original NARC bytes.
/// </summary>
public sealed class PlatinumEvents
{
    public const string EventPath = "fielddata/eventdata/zone_event.narc";
    public const string NamesPath = "fielddata/maptable/mapname.bin";
    public const string MatrixPath = "fielddata/mapmatrix/map_matrix.narc";
    public const string ScriptPath = "fielddata/script/scr_seq.narc";
    public const int HeaderOffset = 0xE601C, HeaderCount = 593;
    public const uint EventsCrc32 = 0xD8BB2D84, NamesCrc32 = 0x530174B6, MatricesCrc32 = 0x90D98DF7, HeadersCrc32 = 0xBB8DDE9D;
    public const string NormalizedEventsSha256 = "24D1E66E53C5D87DE91CAA26D6D19690C8AC04BAFDE1EAD6BFD7C25F2E1AA7DE";
    private readonly byte[] _events;
    private readonly Narc _scripts, _matrixFiles;
    private readonly int[] _starts;
    private readonly List<(int Offset, int Count)[]> _blocks;
    private readonly PlatinumMap[] _maps;
    private readonly byte[] _headers, _names, _matrices, _scriptBytes;
    private readonly string[]? _labels;
    public IReadOnlyList<PlatinumMap> Maps => Array.AsReadOnly(_maps);
    private static readonly int[] Strides = [20, 32, 12, 16];

    public static PlatinumEvents Open(NdsRom rom, IReadOnlyList<string>? locationNames = null)
    {
        if (rom.Header.GameCode != "CPUE" || rom.Header.Version != 1 || rom.Header.IsDsiEnhanced)
            throw new InvalidDataException("Eventos: se requiere Platino CPUE/1; no se aplican perfiles de otros juegos.");
        byte[] arm9 = rom.ReadArm9(), names = rom.ReadFile(NamesPath), matrices = rom.ReadFile(MatrixPath), scripts = rom.ReadFile(ScriptPath);
        if (Gen5Data.Crc32(arm9) != 0x4D104949 || Gen5Data.Crc32(names) != NamesCrc32 || Gen5Data.Crc32(matrices) != MatricesCrc32 || Gen5Data.Crc32(scripts) != 0x6D482515)
            throw new InvalidDataException("Eventos CPUE/1: ARM9, nombres, matrices o scripts no coinciden con las tablas acreditadas.");
        var result = Parse(arm9.AsSpan(HeaderOffset, HeaderCount * 24).ToArray(), names, rom.ReadFile(EventPath), matrices, scripts, locationNames);
        if (Gen5Data.Crc32(arm9.AsSpan(HeaderOffset, HeaderCount * 24).ToArray()) != HeadersCrc32 || result._maps.Length != HeaderCount ||
            result._blocks.Count != 534 || result._scripts.Files.Count != 1124 || result.NormalizedHash() != NormalizedEventsSha256)
            throw new InvalidDataException("Eventos CPUE/1: las tablas o los bytes no editables no coinciden con el perfil.");
        return result;
    }

    /// <summary>Structural parser for binary fixtures; does not identify or authorize a ROM. Open enforces the CPUE/1 profile.</summary>
    public static PlatinumEvents Parse(byte[] headers, byte[] names, byte[] events, byte[] matrices, byte[] scripts, IReadOnlyList<string>? labels = null) =>
        new(headers, names, events, matrices, scripts, labels);

    private PlatinumEvents(byte[] headers, byte[] names, byte[] events, byte[] matricesBytes, byte[] scripts, IReadOnlyList<string>? labels)
    {
        if (headers.Length == 0 || headers.Length % 24 != 0 || names.Length != headers.Length / 24 * 16)
            throw new InvalidDataException("Cabeceras o nombres de mapa truncados.");
        _headers = (byte[])headers.Clone(); _names = (byte[])names.Clone(); _matrices = (byte[])matricesBytes.Clone();
        _scriptBytes = (byte[])scripts.Clone(); _labels = labels?.ToArray(); _events = (byte[])events.Clone();
        var archive = Narc.Read(_events); var matrices = _matrixFiles = Narc.Read(matricesBytes); _scripts = Narc.Read(scripts);
        _starts = MemberStarts(_events, archive); _blocks = new();
        for (int file = 0; file < archive.Files.Count; file++)
        {
            var bytes = archive.Files[file]; int at = 0; var blocks = new (int, int)[4];
            for (int kind = 0; kind < 4; kind++)
            {
                Need(bytes, at, 4); uint count = U32(bytes, at); at += 4;
                if (count > 4096 || (long)at + count * Strides[kind] > bytes.Length) throw new InvalidDataException("Conteo de eventos fuera de límites.");
                blocks[kind] = (at, (int)count); at += (int)count * Strides[kind];
            }
            if (at != bytes.Length) throw new InvalidDataException("El archivo de eventos tiene bytes finales desconocidos.");
            _blocks.Add(blocks);
        }
        _maps = new PlatinumMap[headers.Length / 24];
        for (int id = 0; id < _maps.Length; id++)
        {
            int at = id * 24, matrix = U16(headers, at + 2), script = U16(headers, at + 4), ev = U16(headers, at + 16), label = headers[at + 18];
            if (matrix >= matrices.Files.Count || script >= _scripts.Files.Count || ev >= archive.Files.Count)
                throw new InvalidDataException("Referencia de mapa fuera de sus tablas.");
            var m = matrices.Files[matrix]; Need(m, 0, 5);
            if (m[0] == 0 || m[1] == 0 || m[2] > 1 || m[3] > 1 || 5 + m[4] + m[0] * m[1] * (2 + m[2] * 2 + m[3]) != m.Length)
                throw new InvalidDataException("Dimensiones o secciones de matriz desconocidas.");
            string internalName = Encoding.ASCII.GetString(names, id * 16, 16).TrimEnd('\0');
            if (internalName.Length == 0 || internalName.Any(c => c < 32 || c > 126)) throw new InvalidDataException("Nombre interno de mapa inválido.");
            string name = labels is not null && label < labels.Count ? labels[label] : internalName;
            _maps[id] = new(id, name, internalName, ev, script, matrix, m[0] * 32, m[1] * 32);
        }
    }

    // Only locates members after Narc.Read has parsed the same BTAF/BTNF/GMIF. Extra validation forbids aliases/overlaps.
    private static int[] MemberStarts(byte[] raw, Narc archive)
    {
        if (raw.Length < 28 || U32(raw, 8) != raw.Length || U16(raw, 14) != 3) throw new InvalidDataException("Cabecera NARC inconsistente.");
        int fat = U16(raw, 12); Need(raw, fat, 12);
        int names = checked(fat + (int)U32(raw, fat + 4)); Need(raw, names, 8);
        int image = checked(names + (int)U32(raw, names + 4)); Need(raw, image, 8);
        if ((long)image + U32(raw, image + 4) != raw.Length) throw new InvalidDataException("NARC con datos externos a GMIF.");
        int[] starts = new int[archive.Files.Count]; int previousEnd = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int start = checked((int)U32(raw, fat + 12 + i * 8)), end = checked((int)U32(raw, fat + 16 + i * 8));
            if (start < previousEnd || start % 4 != 0 || end - start != archive.Files[i].Length) throw new InvalidDataException("Miembros NARC solapados o desalineados.");
            starts[i] = image + 8 + start; previousEnd = end;
        }
        return starts;
    }

    public IReadOnlyList<PlatinumEvent> Events(int mapId)
    {
        var map = Map(mapId); var result = new List<PlatinumEvent>();
        for (int kind = 0; kind < 4; kind++)
        {
            var block = _blocks[map.EventFile][kind];
            for (int i = 0; i < block.Count; i++)
            {
                int at = _starts[map.EventFile] + block.Offset + i * Strides[kind]; byte[] r = _events.AsSpan(at, Strides[kind]).ToArray();
                result.Add(kind switch
                {
                    1 => new((PlatinumEventKind)kind, i, U16(r, 0), I16(r, 24), I16(r, 26), I32(r, 28), U16(r, 10), U16(r, 6), U16(r, 12), U16(r, 2), U16(r, 4), U16(r, 8), U16(r, 14), U16(r, 20), U16(r, 22), -1, -1, Convert.ToHexString(r)),
                    2 => new((PlatinumEventKind)kind, i, i, I16(r, 0), I16(r, 2), I32(r, 8), 0, 0, 0, 0, 0, 0, 0, 0, 0, U16(r, 4), U16(r, 6), Convert.ToHexString(r)),
                    0 => new((PlatinumEventKind)kind, i, i, I16(r, 4), I16(r, 8), I32(r, 12), U16(r, 0), U16(r, 2), U16(r, 16), 0, 0, 0, 0, 0, 0, -1, -1, Convert.ToHexString(r)),
                    _ => new((PlatinumEventKind)kind, i, i, I16(r, 2), I16(r, 4), U16(r, 10), U16(r, 0), 0, 0, 0, 0, U16(r, 14), 0, U16(r, 6), U16(r, 8), -1, -1, Convert.ToHexString(r)),
                });
            }
        }
        return result.AsReadOnly();
    }

    /// <summary>Read-only matrix layout of a map's own matrix (no writes, no events).</summary>
    public PlatinumMapLayout Layout(int mapId) { var map = Map(mapId); return PlatinumMapLayout.ParseMatrix(_matrixFiles.Files[map.Matrix], map.Id, map.Matrix); }
    public PlatinumMap Map(int mapId) => mapId >= 0 && mapId < _maps.Length ? _maps[mapId] : throw new ArgumentOutOfRangeException(nameof(mapId));
    public PlatinumEvent Npc(int mapId, int index) => Events(mapId).FirstOrDefault(e => e.Kind == PlatinumEventKind.Npc && e.Index == index)
        ?? throw new ArgumentOutOfRangeException(nameof(index), "NPC inexistente.");
    public static PlatinumNpcValues Values(PlatinumEvent npc) => new(npc.X, npc.Y, npc.Direction);
    public (int Width, int Height) Bounds(int mapId)
    {
        int file = Map(mapId).EventFile; var users = _maps.Where(m => m.EventFile == file);
        return (users.Min(m => m.Width), users.Min(m => m.Height));
    }
    public byte[] ReplaceNpc(int mapId, int index, PlatinumNpcValues expected, PlatinumNpcValues values)
    {
        var npc = Npc(mapId, index); var bounds = Bounds(mapId);
        if (Values(npc) != expected) throw new InvalidOperationException("El NPC cambió desde la vista previa.");
        if (values.X < 0 || values.X >= bounds.Width || values.Y < 0 || values.Y >= bounds.Height || values.Direction is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(values), "Coordenadas fuera de la matriz o dirección fuera de 0–3.");
        byte[] output = (byte[])_events.Clone(); int file = Map(mapId).EventFile;
        int at = _starts[file] + _blocks[file][1].Offset + index * 32;
        Put16(output, at + 24, values.X); Put16(output, at + 26, values.Y); Put16(output, at + 12, values.Direction);
        return output;
    }
    public byte[] Bytes() => (byte[])_events.Clone();
    public PlatinumEvents WithEvents(byte[] events)
    {
        var next = new PlatinumEvents(_headers, _names, events, _matrices, _scriptBytes, _labels);
        if (next.NormalizedHash() != NormalizedHash()) throw new InvalidDataException("La edición altera bytes ajenos a X/Y/dirección de NPC.");
        return next;
    }
    public string NormalizedHash()
    {
        byte[] bytes = Bytes();
        for (int file = 0; file < _blocks.Count; file++) for (int i = 0; i < _blocks[file][1].Count; i++)
        {
            int at = _starts[file] + _blocks[file][1].Offset + i * 32;
            bytes.AsSpan(at + 12, 2).Clear(); bytes.AsSpan(at + 24, 4).Clear();
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    public string WarpStatus(PlatinumEvent warp)
    {
        if (warp.Kind != PlatinumEventKind.Warp) throw new ArgumentException("No es un warp.");
        if (warp.Destination < 0 || warp.Destination >= _maps.Length) return "Destino especial/dinámico o fuera de rango; no navegable ni editable.";
        return Events(warp.Destination).Any(e => e.Kind == PlatinumEventKind.Warp && e.Index == warp.Anchor)
            ? $"{Map(warp.Destination).Display} · warp {warp.Anchor}. Referencia existente; no acredita jugabilidad."
            : "El mapa existe, pero el índice de warp no corresponde a un registro; solo consulta.";
    }
    public PlatinumScriptLink Script(int mapId, int reference)
    {
        int archive = Map(mapId).ScriptFile, entry = reference;
        // Only the two item ranges already identified in this profile. Other common scripts stay unresolved.
        if (reference is >= 7000 and < 8000) { archive = 404; entry = reference - 7000 + 1; }
        else if (reference is >= 8000 and < 8800) { archive = 408; entry = reference - 8000 + 1; }
        if (reference == 0) return new(reference, archive, null, null, "Sin script asignado.", "");
        if (entry is <= 0 or >= 2000 || archive >= _scripts.Files.Count)
            return new(reference, archive, null, null, "Referencia común/externa sin resolver; no se interpreta como índice local.", "");
        byte[] bytes = _scripts.Files[archive]; int at = 0; var offsets = new List<int>(); bool ended = false;
        while (at + 2 <= bytes.Length && offsets.Count < 2000)
        {
            if (U16(bytes, at) == 0xFD13) { at += 2; ended = true; break; }
            if (at + 4 > bytes.Length) break;
            long target = (long)at + 4 + U32(bytes, at);
            if (target > int.MaxValue) break;
            offsets.Add((int)target); at += 4;
        }
        if (!ended || offsets.Any(o => o < at || o >= bytes.Length) || entry > offsets.Count)
            return new(reference, archive, null, null, "Tabla de entradas no acreditada o referencia local inexistente; solo ID.", "");
        int offset = offsets[entry - 1];
        return new(reference, archive, entry, offset, "Entrada localizada; bytes de consulta, sin parser de instrucciones ni escritura.", Convert.ToHexString(bytes.AsSpan(offset, Math.Min(32, bytes.Length - offset))));
    }
    private static void Need(byte[] bytes, int at, int length) { if (at < 0 || length < 0 || (long)at + length > bytes.Length) throw new InvalidDataException("Tabla truncada."); }
    private static int U16(byte[] bytes, int at) { Need(bytes, at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at)); }
    private static int I16(byte[] bytes, int at) { Need(bytes, at, 2); return BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at)); }
    private static int I32(byte[] bytes, int at) { Need(bytes, at, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)); }
    private static uint U32(byte[] bytes, int at) { Need(bytes, at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)); }
    private static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)value));
}
