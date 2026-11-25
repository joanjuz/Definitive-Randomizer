#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.NDS;

/// <summary>
/// Where one Gen 5 game keeps its map and event tables, and the fingerprints of the original data. Gen 5 shares nothing binary with
/// <see cref="Gen4EventProfile"/>: headers are 48 bytes in one member of a/0/1/2, matrices are u32 cells, map blocks are WB/GC/NG/RD containers and
/// event files have their own records. Every value was measured on the owner's dump of that exact game and revision.
/// </summary>
public sealed record Gen5MapProfile(
    string GameCode, byte Revision, string GameName,
    string EventPath, string ScriptPath,
    int HeaderCount, int EventFileCount, int MatrixCount, int BlockCount, int TextureSetCount, int ScriptFileCount,
    uint HeadersCrc32, uint MatricesCrc32, uint BlocksCrc32, uint EventsCrc32, uint TexturesCrc32, uint ScriptsCrc32,
    int LocationNamesText, int MaxMatrixSide, int MaxObjectFacing, string EditableEventsSha256) : INdsEventProfile
{
    public const string HeadersPath = "a/0/1/2", MatrixPath = "a/0/0/9", BlocksPath = "a/0/0/8", TexturesPath = "a/0/1/4";
    public string Key => $"{GameCode}/{Revision}";
    public string Label => $"{GameName} {Key}";
    /// <summary>New NPC, nurses and scripts are only accredited in Platinum.</summary>
    public bool SupportsNpcCreation => false;
    /// <summary>The values (0–6) are read and kept, but their meaning in Gen 5 is not accredited: the interaction direction is never changed.</summary>
    public bool EditsObjectFacing => false;
    /// <summary>Sight is read but not accredited in Gen 5 (its representation and behaviour have not been checked in the game), so it is neither drawn nor edited.</summary>
    public int MaxTrainerSight => 0;
    public string? SightRefusal(PlatinumEvent npc) => npc.Kind != PlatinumEventKind.Npc ? "no es un NPC" : $"en {GameName} el alcance de visión no está acreditado; solo se cambia la orientación";

    /// <summary>Pokémon Black USA/EUR rev. 0 (IRBO/0, SHA-1 26ad0b99…).</summary>
    public static Gen5MapProfile Black { get; } = new("IRBO", 0, "Negro", "a/1/2/5", "a/0/5/7",
        427, 428, 255, 649, 282, 899,
        0xEC1CB686, 0x65AC968E, 0x308E1CC6, 0x6B00C87E, 0x0AA75AEC, 0x0D4E6B38,
        89, 29, 6, "314F7AD2F7AF6162163EAFAD1502B6C4C791A6D52877E88F2D8FE3B3C5F1B849");

    /// <summary>
    /// Pokémon White, Spanish edition rev. 0 (IRAS/0, SHA-1 4cc963ca…). Measured on its own dump: headers, matrices, map blocks, zone events, terrain
    /// textures and the area table are byte-identical to Black's (same CRC32 and the same events fingerprint); its scripts (a/0/5/7) and texts are its own
    /// (location names in Spanish, text 89). Its exterior building packs and their textures (a/2/2/9, a/1/7/6) differ, and so does a/2/2/6 (images):
    /// the buildings have their own profile, <see cref="Gen5BuildingProfile.White"/>.
    /// </summary>
    public static Gen5MapProfile White { get; } = new("IRAS", 0, "Blanco", "a/1/2/5", "a/0/5/7",
        427, 428, 255, 649, 282, 899,
        0xEC1CB686, 0x65AC968E, 0x308E1CC6, 0x6B00C87E, 0x0AA75AEC, 0x7638D5C9,
        89, 29, 6, "314F7AD2F7AF6162163EAFAD1502B6C4C791A6D52877E88F2D8FE3B3C5F1B849");

    /// <summary>
    /// Pokémon White 2 USA/EUR rev. 0 (IRDO/0, SHA-1 b5d7490b…). The Black and White formats with their own tables, measured on its dump: 615 headers
    /// (ids up to 614, beyond Platinum's 593), events in a/1/2/6 (616), scripts in a/0/5/6 (1 289), 416 matrices (largest side 29), 1 065 blocks,
    /// 409 texture sets, location names in text 109. +22 is again the only field with which every warp leads to an existing warp (1 084 of 1 089; the other 5
    /// have destination 65535); every grid warp sits at a tile centre; collision shows the same pattern (4 % on NPC tiles against 75 % overall).
    /// </summary>
    public static Gen5MapProfile White2 { get; } = new("IRDO", 0, "Blanco 2", "a/1/2/6", "a/0/5/6",
        615, 616, 416, 1065, 409, 1289,
        0xC6B5EB86, 0xE31AF7F0, 0x0A549232, 0xFE1792B9, 0x35283BC1, 0xEA1E0890,
        109, 29, 6, "8EBA7880BCBE0A4470BAE51415FF2D6964B679E5DDCB75D3E1CDEA164E4B8E92");

    public static IReadOnlyList<Gen5MapProfile> All { get; } = [Black, White, White2];
    public static Gen5MapProfile? For(string gameCode, int revision) => All.FirstOrDefault(p => p.GameCode == gameCode && p.Revision == revision);
    public static Gen5MapProfile? For(NdsHeader header) => For(header.GameCode, header.Version);
    public static Gen5MapProfile Require(NdsRom rom, string area) => For(rom.Header)
        ?? throw new InvalidDataException($"{area}: solo {string.Join(" y ", All.Select(p => p.Label))}; no se aplican perfiles de otros juegos.");
}

/// <summary>
/// Map headers, matrices and zone events of Black, White, Black 2 and White 2, measured on the owner's dumps (no public decompilation of these games
/// covers the field layer; every rule below was checked against the real data and is named where it is an inference):
/// <list type="bullet">
/// <item>Header (48 bytes): +2 texture set (a/0/1/4), +4 matrix, +6 scripts, +22 events, +26 u8 location name. +22 is the only field with which every
/// warp of the game leads to an existing warp, except the special ones whose destination is not a header.</item>
/// <item>Event file: u32 size of the next part, four u8 counts, then records of 20 (background), 36 (NPC), 20 (warp) and 22 (trigger) bytes, then a
/// tail of variable length kept byte for byte.</item>
/// <item>NPC: +0 id, +2 sprite, +4 movement, +6 type, +8 flag, +10 script, +12 direction 0–3, +14 sight, +20/+22 walking range, +24 position type
/// (0 grid, 1 rail), +28 X, +30 Y, +32 height. Background: +0 script, +2 type (2 = hidden item), +4 facing, +6 position type, +8 X, +12 Y.
/// Warp: +0 destination header, +2 destination warp, +6 position type, +8/+12 position in sixteenths of a tile (16·x + 8), +14/+16 extent.
/// Trigger: +0 script, +2 value, +4 variable, +8 position type, +10 X, +12 Y, +14 width, +16 length.</item>
/// <item>Item balls are NPC with a script 7000–7999; hidden items are background events of type 2 with a script 8000+.</item>
/// </list>
/// Records with a rail position (position type 1) are shown but never written. Sight is read but never written: its behaviour in Gen 5 is not accredited.
/// </summary>
public sealed class Gen5ZoneEvents : INdsEventData
{
    INdsEventProfile INdsEventData.Profile => Profile;
    INdsEventData INdsEventData.WithEvents(byte[] events) => WithEvents(events);
    public const int HeaderSize = 48, BackgroundSize = 20, NpcSize = 36, WarpSize = 20, TriggerSize = 22;
    private static readonly int[] Strides = [BackgroundSize, NpcSize, WarpSize, TriggerSize];
    public const int MaxTriggerSide = 32;

    public Gen5MapProfile Profile { get; }
    private readonly byte[] _headers, _headersNarc, _events, _matrices, _scripts;
    private readonly Narc _matrixFiles;
    private readonly string[]? _labels;
    private readonly int[] _starts;
    /// <summary>Per event file: offset of each block inside the member (or -1 for an empty member) and record counts.</summary>
    private readonly (int Offset, int Count)[][] _blocks;
    private readonly PlatinumMap[] _maps;
    public IReadOnlyList<PlatinumMap> Maps => Array.AsReadOnly(_maps);

    /// <summary>Opens the tables of a ROM with a measured profile: every table pinned by CRC32, the event archive by the fingerprint of its original data.</summary>
    public static Gen5ZoneEvents Open(NdsRom rom, IReadOnlyList<string>? locationNames = null)
    {
        var p = Gen5MapProfile.Require(rom, "Eventos"); string key = p.Key;
        byte[] Pinned(string path, uint crc)
        {
            byte[] bytes;
            try { bytes = rom.ReadFile(path); }
            catch (FileNotFoundException ex) { throw new InvalidDataException($"Eventos {key}: falta la tabla {path}.", ex); }
            if (Gen5Data.Crc32(bytes) != crc) throw new InvalidDataException($"Eventos {key}: {path} no coincide con la tabla acreditada.");
            return bytes;
        }
        byte[] headers = Pinned(Gen5MapProfile.HeadersPath, p.HeadersCrc32), matrices = Pinned(Gen5MapProfile.MatrixPath, p.MatricesCrc32), scripts = Pinned(p.ScriptPath, p.ScriptsCrc32);
        var result = Parse(p, headers, rom.ReadFile(p.EventPath), matrices, scripts, locationNames);
        if (result._maps.Length != p.HeaderCount || result._blocks.Length != p.EventFileCount || result._matrixFiles.Files.Count != p.MatrixCount || Narc.Read(scripts).Files.Count != p.ScriptFileCount || !result.IsAccredited())
            throw new InvalidDataException($"Eventos {key}: las tablas o los bytes no editables no coinciden con el perfil.");
        return result;
    }

    /// <summary>Structural parser (fixtures); does not identify or authorize a ROM.</summary>
    public static Gen5ZoneEvents Parse(Gen5MapProfile profile, byte[] headersNarc, byte[] events, byte[] matrices, byte[] scripts, IReadOnlyList<string>? labels = null) =>
        new(profile, headersNarc, events, matrices, scripts, labels);

    private Gen5ZoneEvents(Gen5MapProfile profile, byte[] headersNarc, byte[] events, byte[] matrices, byte[] scripts, IReadOnlyList<string>? labels)
    {
        _scripts = (byte[])scripts.Clone(); _ = Narc.Read(_scripts);
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        var headerArchive = Narc.Read(headersNarc);
        if (headerArchive.Files.Count != 1 || headerArchive.Files[0].Length == 0 || headerArchive.Files[0].Length % HeaderSize != 0) throw new InvalidDataException("Tabla de cabeceras Gen5 desconocida.");
        _headers = (byte[])headerArchive.Files[0].Clone(); _headersNarc = (byte[])headersNarc.Clone(); _events = (byte[])events.Clone(); _matrices = (byte[])matrices.Clone(); _labels = labels?.ToArray();
        var archive = Narc.Read(_events); _matrixFiles = Narc.Read(_matrices);
        _starts = MemberStarts(_events, archive);
        _blocks = new (int, int)[archive.Files.Count][];
        for (int file = 0; file < archive.Files.Count; file++)
        {
            var b = archive.Files[file]; var blocks = new (int, int)[4];
            if (b.Length < 8) { if (b.Length != 4 || U32(b, 0) != 0) throw new InvalidDataException("Archivo de eventos Gen5 truncado."); _blocks[file] = [(8, 0), (8, 0), (8, 0), (8, 0)]; continue; }
            int at = 8; uint size = U32(b, 0);
            for (int kind = 0; kind < 4; kind++) { int count = b[4 + kind]; blocks[kind] = (at, count); at += count * Strides[kind]; }
            if (at != size + 4 || at > b.Length) throw new InvalidDataException("Recuento de eventos Gen5 incoherente con su tamaño.");
            _blocks[file] = blocks;
        }
        int n = _headers.Length / HeaderSize; _maps = new PlatinumMap[n];
        for (int id = 0; id < n; id++)
        {
            int at = id * HeaderSize, matrix = U16(_headers, at + 4), script = U16(_headers, at + 6), ev = U16(_headers, at + 22), label = _headers[at + 26];
            if (matrix >= _matrixFiles.Files.Count || ev >= _blocks.Length) throw new InvalidDataException("Referencia de cabecera Gen5 fuera de sus tablas.");
            var (w, h, _) = MatrixShape(_matrixFiles.Files[matrix]);
            string internalName = $"Z{id:D3}";
            string name = labels is not null && label < labels.Count && !string.IsNullOrWhiteSpace(labels[label]) ? labels[label] : internalName;
            _maps[id] = new(id, name, internalName, ev, script, matrix, w * 32, h * 32);
        }
    }

    /// <summary>Gen 5 matrix: u32 has-headers, u16 width, u16 height, then width×height u32 block ids and, if present, as many u32 header ids.</summary>
    public static (int Width, int Height, bool HasHeaders) MatrixShape(byte[] m)
    {
        if (m.Length < 8) throw new InvalidDataException("Matriz Gen5 truncada.");
        uint has = U32(m, 0); int w = U16(m, 4), h = U16(m, 6);
        if (has > 1 || w < 1 || h < 1 || m.Length != 8 + w * h * 4 * (has == 1 ? 2 : 1)) throw new InvalidDataException("Matriz Gen5 con secciones desconocidas.");
        return (w, h, has == 1);
    }

    /// <summary>
    /// The matrix of a map in the shared, read-only presentation layout: the same cells (block ids; 0xFFFFFFFF becomes the empty id 0xFFFF; stored headers
    /// when the matrix has them) written as a Gen 4 matrix and read with <see cref="PlatinumMapLayout"/>. Nothing of it is ever written back.
    /// </summary>
    public PlatinumMapLayout Layout(int mapId)
    {
        var map = Map(mapId); var m = _matrixFiles.Files[map.Matrix]; var (w, h, has) = MatrixShape(m);
        if (w > Profile.MaxMatrixSide || h > Profile.MaxMatrixSide) throw new InvalidDataException("Matriz Gen5 mayor que el máximo medido.");
        int count = w * h; var bytes = new byte[5 + count * (2 + (has ? 2 : 0))];
        bytes[0] = (byte)w; bytes[1] = (byte)h; bytes[2] = (byte)(has ? 1 : 0);
        int at = 5;
        if (has) for (int i = 0; i < count; i++, at += 2) Put16(bytes, at, Low16(U32(m, 8 + count * 4 + i * 4)));
        for (int i = 0; i < count; i++, at += 2) Put16(bytes, at, Low16(U32(m, 8 + i * 4)));
        return PlatinumMapLayout.ParseMatrix(bytes, map.Id, map.Matrix, Profile.MaxMatrixSide, _maps.Length);
        static int Low16(uint v) => v == 0xFFFFFFFF ? 0xFFFF : v < 0xFFFF ? (int)v : throw new InvalidDataException("Identificador de matriz Gen5 fuera de rango.");
    }

    /// <summary>Texture set (a/0/1/4) a header names for its terrain.</summary>
    public int TextureSet(int mapId) => U16(_headers, Map(mapId).Id * HeaderSize + 2);
    public PlatinumMap Map(int mapId) => mapId >= 0 && mapId < _maps.Length ? _maps[mapId] : throw new ArgumentOutOfRangeException(nameof(mapId));

    public IReadOnlyList<PlatinumEvent> Events(int mapId)
    {
        int file = Map(mapId).EventFile; var result = new List<PlatinumEvent>();
        for (int kind = 0; kind < 4; kind++)
        {
            var (offset, count) = _blocks[file][kind];
            for (int i = 0; i < count; i++)
            {
                int at = _starts[file] + offset + i * Strides[kind]; byte[] r = _events.AsSpan(at, Strides[kind]).ToArray(); string raw = Convert.ToHexString(r);
                var e = kind switch
                {
                    0 => new(PlatinumEventKind.Object, i, i, U16(r, 8), U16(r, 12), U16(r, 16), U16(r, 0), U16(r, 2), U16(r, 4), 0, 0, 0, 0, 0, 0, -1, -1, raw),
                    1 => new PlatinumEvent(PlatinumEventKind.Npc, i, U16(r, 0), U16(r, 28), U16(r, 30), I32(r, 32), U16(r, 10), U16(r, 6), U16(r, 12), U16(r, 2), U16(r, 4), U16(r, 8), U16(r, 14), U16(r, 20), U16(r, 22), -1, -1, raw)
                    { ScriptedGroundItem = U16(r, 10) is >= 7000 and < 8000 },
                    2 => new(PlatinumEventKind.Warp, i, i, U16(r, 8) >> 4, U16(r, 12) >> 4, (short)U16(r, 10), 0, 0, 0, 0, 0, 0, 0, U16(r, 14), U16(r, 16), U16(r, 0), U16(r, 2), raw),
                    _ => new(PlatinumEventKind.Trigger, i, i, U16(r, 10), U16(r, 12), U16(r, 18), U16(r, 0), 0, 0, 0, 0, U16(r, 4), 0, U16(r, 14), U16(r, 16), -1, -1, raw),
                };
                result.Add(e with { EditLock = Lock(e) });
            }
        }
        return result.AsReadOnly();
    }
    public PlatinumEvent Npc(int mapId, int index) => Events(mapId).FirstOrDefault(e => e.Kind == PlatinumEventKind.Npc && e.Index == index) ?? throw new ArgumentOutOfRangeException(nameof(index), "NPC inexistente.");
    public PlatinumEvent Event(int mapId, PlatinumEventKind kind, int index) => Events(mapId).FirstOrDefault(e => e.Kind == kind && e.Index == index) ?? throw new ArgumentOutOfRangeException(nameof(index), "Evento inexistente.");
    public int NpcCount(int mapId) => _blocks[Map(mapId).EventFile][1].Count;

    /// <summary>Position type of a record: 0 grid, 1 rail (not editable). Every kind keeps it at a different offset.</summary>
    public static int PositionType(PlatinumEvent e)
    {
        byte[] r = Convert.FromHexString(e.Raw);
        return e.Kind switch { PlatinumEventKind.Npc => U16(r, 24), PlatinumEventKind.Object or PlatinumEventKind.Warp => U16(r, 6), _ => U16(r, 8) };
    }
    /// <summary>Why the editor does not write this record, or null when its accredited fields can be edited.</summary>
    public string? EditRefusal(PlatinumEvent e) => e.Kind == PlatinumEventKind.Npc && e.IsGroundItem ? "es un objeto de suelo (Poké Ball)" : Lock(e);
    /// <summary>Records shown but never written: rail positions, and warps whose destination is not a warp of a header.</summary>
    private string? Lock(PlatinumEvent e) =>
        PositionType(e) != 0 ? "usa una posición de raíl (no de casilla); su formato no está acreditado"
        : e.Kind == PlatinumEventKind.Warp && !WarpTargetExists(e.Destination, e.Anchor) ? "su destino es especial (no es un warp de un mapa)"
        : null;

    public (int Width, int Height) Bounds(int mapId)
    {
        int file = Map(mapId).EventFile; var users = _maps.Where(m => m.EventFile == file);
        return (users.Min(m => m.Width), users.Min(m => m.Height));
    }

    /// <summary>
    /// Writes X, Y and direction of an existing grid NPC (not an item ball) and, for an NPC whose movement is a fixed look (14–17), the fixed look of the new
    /// direction. Sight (+14) is never written. Every other byte stays as it is.
    /// </summary>
    public byte[] ReplaceNpc(int mapId, int index, PlatinumNpcValues expected, PlatinumNpcValues values)
    {
        var npc = Npc(mapId, index); var bounds = Bounds(mapId);
        if (npc.X != expected.X || npc.Y != expected.Y || npc.Direction != expected.Direction || expected.Movement is int em && em != npc.Movement) throw new InvalidOperationException("El NPC cambió desde la vista previa.");
        if (EditRefusal(npc) is { } why) throw new InvalidOperationException($"Este NPC no se edita: {why}.");
        if (expected.Sight is not null || values.Sight is not null) throw new InvalidOperationException($"En {Profile.Label} el alcance de visión no se edita: su comportamiento no está acreditado.");
        if (values.X < 0 || values.X >= bounds.Width || values.Y < 0 || values.Y >= bounds.Height || values.Direction is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(values), "Coordenadas fuera de la matriz o dirección fuera de 0–3.");
        if (expected.Movement.HasValue != values.Movement.HasValue) throw new ArgumentException("El estado anterior y el propuesto deben incluir los mismos campos.", nameof(values));
        if (values.Movement is int movement && (!PlatinumTrainerVision.IsFixedLook(npc.Movement) || movement != PlatinumTrainerVision.FixedLookFor(values.Direction)))
            throw new ArgumentOutOfRangeException(nameof(values), "El tipo de movimiento solo cambia entre «mirar fijo» (14–17) y debe coincidir con la dirección propuesta.");
        byte[] output = (byte[])_events.Clone(); int file = Map(mapId).EventFile; int at = _starts[file] + _blocks[file][1].Offset + index * NpcSize;
        Put16(output, at + 28, values.X); Put16(output, at + 30, values.Y); Put16(output, at + 12, values.Direction);
        if (values.Movement is int m) Put16(output, at + 4, m);
        return output;
    }

    /// <summary>
    /// Writes the editable fields of an existing grid background object (position, facing up to the measured maximum), warp (position; destination and
    /// warp index, only to an existing warp) or trigger (position and size). Scripts, types, flags, variables, heights, warp extents and every other byte stay.
    /// </summary>
    public byte[] ReplaceEvent(int mapId, PlatinumEventKind kind, int index, PlatinumEventValues expected, PlatinumEventValues values)
    {
        if (kind is PlatinumEventKind.Npc) throw new ArgumentException("Los NPC se editan con ReplaceNpc.", nameof(kind));
        var e = Event(mapId, kind, index); var bounds = Bounds(mapId);
        if (expected != PlatinumEvents.EventValues(e)) throw new InvalidOperationException("El evento cambió desde la vista previa.");
        if (EditRefusal(e) is { } why) throw new InvalidOperationException($"Este evento no se edita: {why}.");
        if (values.Destination.HasValue != expected.Destination.HasValue || values.Anchor.HasValue != expected.Anchor.HasValue || values.Width.HasValue != expected.Width.HasValue
            || values.Length.HasValue != expected.Length.HasValue || values.Facing.HasValue != expected.Facing.HasValue)
            throw new ArgumentException("El estado anterior y el propuesto deben incluir los mismos campos.", nameof(values));
        int width = kind == PlatinumEventKind.Trigger ? values.Width ?? 1 : kind == PlatinumEventKind.Warp ? Math.Max(1, e.RangeX) : 1;
        int length = kind == PlatinumEventKind.Trigger ? values.Length ?? 1 : kind == PlatinumEventKind.Warp ? Math.Max(1, e.RangeY) : 1;
        if (values.X < 0 || values.Y < 0 || values.X + width > bounds.Width || values.Y + length > bounds.Height)
            throw new ArgumentOutOfRangeException(nameof(values), "Posición o área fuera de los límites que acepta el editor.");
        byte[] output = (byte[])_events.Clone(); int file = Map(mapId).EventFile; int at = _starts[file] + _blocks[file][(int)kind].Offset + index * Strides[(int)kind];
        switch (kind)
        {
            case PlatinumEventKind.Object:
                if (values.Facing != expected.Facing) throw new ArgumentOutOfRangeException(nameof(values), $"En {Profile.Label} la dirección de interacción no se cambia: su significado no está acreditado.");
                Put16(output, at + 8, values.X); Put16(output, at + 12, values.Y);
                break;
            case PlatinumEventKind.Warp:
                if ((values.Destination != expected.Destination || values.Anchor != expected.Anchor) && !WarpTargetExists(values.Destination!.Value, values.Anchor!.Value))
                    throw new ArgumentOutOfRangeException(nameof(values), "El destino debe ser un warp existente de un mapa existente.");
                Put16(output, at + 8, values.X * 16 + 8); Put16(output, at + 12, values.Y * 16 + 8); Put16(output, at, values.Destination!.Value); Put16(output, at + 2, values.Anchor!.Value);
                break;
            case PlatinumEventKind.Trigger:
                if (width is < 1 or > MaxTriggerSide || length is < 1 or > MaxTriggerSide) throw new ArgumentOutOfRangeException(nameof(values), $"El activador mide de 1 a {MaxTriggerSide} casillas por lado.");
                Put16(output, at + 10, values.X); Put16(output, at + 12, values.Y); Put16(output, at + 14, width); Put16(output, at + 16, length);
                break;
        }
        return output;
    }

    /// <summary>Whether a destination header and warp index name an existing warp.</summary>
    public bool WarpTargetExists(int header, int anchor) => header >= 0 && header < _maps.Length && anchor >= 0 && anchor < _blocks[_maps[header].EventFile][2].Count;
    public string WarpStatus(PlatinumEvent warp)
    {
        if (warp.Kind != PlatinumEventKind.Warp) throw new ArgumentException("No es un warp.");
        if (warp.Destination < 0 || warp.Destination >= _maps.Length) return "Destino especial; no navegable ni editable.";
        return WarpTargetExists(warp.Destination, warp.Anchor)
            ? $"{Map(warp.Destination).Display} · warp {warp.Anchor}. Referencia existente; no acredita jugabilidad."
            : "El mapa existe, pero el índice de warp no corresponde a un registro; solo consulta.";
    }

    public byte[] Bytes() => (byte[])_events.Clone();
    /// <summary>The script archive, pinned and never written: the editor does not add scripts in Gen 5.</summary>
    public byte[] ScriptArchiveBytes() => (byte[])_scripts.Clone();
    /// <summary>Script references are shown as numbers only: the Gen 5 script format is not interpreted here.</summary>
    public PlatinumScriptLink Script(int mapId, int reference) =>
        new(reference, Map(mapId).ScriptFile, null, null, reference == 0 ? "Sin script asignado." : "Referencia de script Gen5: solo se muestra el número; no se interpreta ni se escribe.", "");
    public PlatinumScriptInfo? ClassifyScript(int mapId, int number) => null;
    /// <summary>A new instance with edited event bytes; refused when anything beyond the editable fields changed.</summary>
    public Gen5ZoneEvents WithEvents(byte[] events)
    {
        var next = new Gen5ZoneEvents(Profile, HeadersNarc(), events, _matrices, _scripts, _labels);
        if (next.EditableHash() != EditableHash()) throw new InvalidDataException("La edición altera bytes ajenos a los campos editables de los eventos.");
        return next;
    }
    private byte[] HeadersNarc() => (byte[])_headersNarc.Clone();

    /// <summary>
    /// SHA-256 of the event archive with the fields the editor may write cleared, on grid records only: NPC X, Y, direction and a fixed-look movement;
    /// background X and Y; warp position, destination and warp index; trigger position and size. Everything else (sight and facing included) stays protected.
    /// </summary>
    public string EditableHash()
    {
        byte[] bytes = Bytes();
        for (int file = 0; file < _blocks.Length; file++)
        {
            int Rec(int kind, int i) => _starts[file] + _blocks[file][kind].Offset + i * Strides[kind];
            for (int i = 0; i < _blocks[file][1].Count; i++)
            {
                int at = Rec(1, i); if (U16(bytes, at + 24) != 0) continue;
                bytes.AsSpan(at + 28, 4).Clear(); bytes.AsSpan(at + 12, 2).Clear();
                if (PlatinumTrainerVision.IsFixedLook(U16(bytes, at + 4))) bytes.AsSpan(at + 4, 2).Clear();
            }
            for (int i = 0; i < _blocks[file][0].Count; i++) { int at = Rec(0, i); if (U16(bytes, at + 6) != 0) continue; bytes.AsSpan(at + 8, 2).Clear(); bytes.AsSpan(at + 12, 2).Clear(); }
            for (int i = 0; i < _blocks[file][2].Count; i++) { int at = Rec(2, i); if (U16(bytes, at + 6) != 0) continue; bytes.AsSpan(at, 4).Clear(); bytes.AsSpan(at + 8, 2).Clear(); bytes.AsSpan(at + 12, 2).Clear(); }
            for (int i = 0; i < _blocks[file][3].Count; i++) { int at = Rec(3, i); if (U16(bytes, at + 8) != 0) continue; bytes.AsSpan(at + 10, 8).Clear(); }
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    /// <summary>The original data, or an edit of it: the fingerprint of the profile, every grid warp at a tile centre and leading to an existing warp.</summary>
    private bool IsAccredited() =>
        EditableHash() == Profile.EditableEventsSha256 && _maps.Select(m => m.EventFile).Distinct().All(file =>
            Events(_maps.First(m => m.EventFile == file).Id).Where(e => e.Kind == PlatinumEventKind.Warp && PositionType(e) == 0)
                .All(w => { byte[] r = Convert.FromHexString(w.Raw); return (U16(r, 8) & 15) == 8 && (U16(r, 12) & 15) == 8; }));

    private static int[] MemberStarts(byte[] raw, Narc archive)
    {
        if (raw.Length < 28 || U32(raw, 8) != raw.Length || U16(raw, 14) != 3) throw new InvalidDataException("Cabecera NARC inconsistente.");
        int fat = U16(raw, 12); int names = checked(fat + (int)U32(raw, fat + 4)); int image = checked(names + (int)U32(raw, names + 4));
        if ((long)image + U32(raw, image + 4) != raw.Length) throw new InvalidDataException("NARC con datos externos a GMIF.");
        int[] starts = new int[archive.Files.Count]; int previousEnd = 0;
        for (int i = 0; i < starts.Length; i++)
        {
            int start = checked((int)U32(raw, fat + 12 + i * 8)), end = checked((int)U32(raw, fat + 16 + i * 8));
            if (start < previousEnd || end - start != archive.Files[i].Length) throw new InvalidDataException("Miembros NARC solapados.");
            starts[i] = image + 8 + start; previousEnd = end;
        }
        return starts;
    }
    private static int U16(byte[] b, int at) => at >= 0 && at + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : throw new InvalidDataException("Tabla truncada.");
    private static int I32(byte[] b, int at) => at >= 0 && at + 4 <= b.Length ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at)) : throw new InvalidDataException("Tabla truncada.");
    private static uint U32(byte[] b, int at) => at >= 0 && at + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : throw new InvalidDataException("Tabla truncada.");
    private static void Put16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), checked((ushort)v));
}
