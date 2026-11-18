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
        PlatinumEventKind.Warp => "Teletransporte (warp)",
        _ => "Activador de script",
    };
}
/// <summary>
/// The editable values of an existing NPC. <c>Movement</c> and <c>Sight</c> are optional: null means "not part of this edit" (every checkpoint written before
/// they existed has them null and is replayed exactly as before). When set, before and after must both carry them.
/// </summary>
public sealed record PlatinumNpcValues(int X, int Y, int Direction, int? Movement = null, int? Sight = null)
{
    public bool HasVisionFields => Movement is not null || Sight is not null;
}

/// <summary>
/// Facing and trainer sight in Platinum, as the engine uses them (pret/pokeplatinum, public decompilation: <c>constants/map_object.h</c>,
/// <c>struct_defs/events.h</c>, <c>trainer_encounter.c</c>, <c>res/field/scripts/battles.s</c>; checked against the owner's CPUE/1 dump):
/// <list type="bullet">
/// <item>+12 is the facing direction: 0 north (up), 1 south (down), 2 west (left), 3 east (right).</item>
/// <item>+6 is the trainer type. 1 checks sight in the facing direction; 2 in the four directions; 4–8 turn by themselves and are checked like 1 with the
/// direction they face at that moment; 0 is not a trainer; 3 is a ground item; 9–11 are not identified.</item>
/// <item>+14 is <c>data[0]</c>: the sight range of the trainer types above. For other NPC it holds unrelated values (up to 117 in the original data), so
/// it is never presented or edited as sight there.</item>
/// <item>Movement types 14–17 (LOOK_NORTH, LOOK_SOUTH, LOOK_WEST, LOOK_EAST) keep the NPC facing one direction; the game itself turns trainers for good by
/// changing that movement type. In the original data every such trainer has the direction that matches its movement type.</item>
/// </list>
/// </summary>
public static class PlatinumTrainerVision
{
    /// <summary>Largest sight range of a trainer in the original CPUE/1 data. The engine has no explicit cap; larger values are not accredited.</summary>
    public const int MaxAccreditedSight = 6;
    public const int LookNorthMovement = 14, LookEastMovement = 17;
    public static bool ChecksSight(int trainerType) => trainerType is 1 or 2 or (>= 4 and <= 8);
    public static bool SeesAllDirections(int trainerType) => trainerType == 2;
    public static bool TurnsByItself(int trainerType) => trainerType is >= 4 and <= 8;
    public static bool IsFixedLook(int movement) => movement is >= LookNorthMovement and <= LookEastMovement;
    /// <summary>The LOOK_* movement type that keeps an NPC facing <paramref name="direction"/> (14 + direction).</summary>
    public static int FixedLookFor(int direction) => direction is >= 0 and <= 3 ? LookNorthMovement + direction : throw new ArgumentOutOfRangeException(nameof(direction));
    /// <summary>Movement types whose facing does not change by itself: none (0) and the fixed looks. Any other one turns or walks, so a stored direction is only the initial one.</summary>
    public static bool KeepsFacing(int movement) => movement == 0 || IsFixedLook(movement);
    public static string DirectionName(int direction) => direction switch { 0 => "Arriba", 1 => "Abajo", 2 => "Izquierda", 3 => "Derecha", _ => $"desconocida ({direction})" };
    public static (int Dx, int Dy) Step(int direction) => direction switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), 3 => (1, 0), _ => throw new ArgumentOutOfRangeException(nameof(direction)) };
    /// <summary>Battle script ranges of the engine (Script_GetTrainerID): 3000+ single battles, 5000+ double battles. Returns the trainer id or null.</summary>
    public static int? TrainerId(int script) => script switch { >= 5000 and < 6000 => script - 5000 + 1, >= 3000 and < 5000 => script - 3000 + 1, _ => null };
    /// <summary>Why <paramref name="npc"/> cannot have its sight edited, or null when it can.</summary>
    public static string? SightRefusal(PlatinumEvent npc) =>
        npc.Kind != PlatinumEventKind.Npc ? "no es un NPC"
        : !ChecksSight(npc.Type) ? $"el juego no comprueba la visión de este NPC (tipo {npc.Type}: {(npc.Type == 0 ? "no es entrenador" : npc.Type == 3 ? "objeto de suelo" : "tipo no identificado")}); su campo +14 no es un alcance"
        : npc.Sight > MaxAccreditedSight ? $"su alcance guardado ({npc.Sight}) supera el máximo acreditado ({MaxAccreditedSight})" : null;
}
/// <summary>Editable values of a background object (X, Y, Facing), warp (X, Y, Destination, Anchor) or trigger (X, Y, Width, Length). Fields the kind lacks are null.</summary>
public sealed record PlatinumEventValues(int X, int Y, int? Destination = null, int? Anchor = null, int? Width = null, int? Length = null, int? Facing = null);

/// <summary>
/// Event facts used by the editor (pret/pokeplatinum: struct_defs/events.h, location.h, overlay005/field_control.c; generated/bg_event_dirs.txt, checked
/// against the owner's dump: 1 207 warps with an existing target and 6 dynamic ones; triggers up to 8×6).
/// </summary>
public static class PlatinumEventFields
{
    /// <summary>WARP_ID_DYNAMIC: the destination is the saved special location, not the warp's fields.</summary>
    public const int DynamicAnchor = 256, MaxFacing = 6, MaxTriggerSide = 32;
    /// <summary>Direction the player must face to use a background event (BG_EVENT_DIR_*: 0 north, 1 east, 2 west, 3 south, 4 any, 5 west/east, 6 north/south).</summary>
    public static string FacingName(int facing) => facing switch
    {
        0 => "Mirando arriba", 1 => "Mirando a la derecha", 2 => "Mirando a la izquierda", 3 => "Mirando abajo", 4 => "Desde cualquier lado", 5 => "Izquierda o derecha", 6 => "Arriba o abajo", _ => $"desconocida ({facing})",
    };
    public static string ObjectTypeName(int type) => type switch { 0 => "evento al mirar", 1 => "cartel de pared", 2 => "objeto oculto", _ => $"tipo {type}" };
}
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
    /// <summary>Largest NPC block found in the original CPUE/1 event archive (verified on the owner's dump). New NPC are never allowed beyond it: the real engine limit is not known.</summary>
    public const int MaxVanillaNpcCount = 51;
    public const string NormalizedEventsSha256 = "24D1E66E53C5D87DE91CAA26D6D19690C8AC04BAFDE1EAD6BFD7C25F2E1AA7DE";
    /// <summary><see cref="EditableHash"/> of the original CPUE/1 event archive (computed on the owner's dump, whose <see cref="NormalizedHash"/> is the constant above).</summary>
    public const string EditableEventsSha256 = "EAB5C0E3F69E80479F257484E576E70425C45E082A21917A0C0AA716029033B3";
    /// <summary><see cref="EventsHash"/> of the original CPUE/1 event archive (computed on the owner's dump; levels 0 and 1 reproduce the two constants above).</summary>
    public const string EventsEditableSha256 = "8A66B39F643BE98BDBC09CBCA46D5066A5C330F7A5F9514C10EAB2613D2ED16C";
    private readonly byte[] _events;
    private readonly Narc _scripts, _matrixFiles;
    private readonly int[] _starts;
    private readonly List<(int Offset, int Count)[]> _blocks;
    private readonly PlatinumMap[] _maps;
    private readonly byte[] _headers, _names, _matrices, _scriptBytes;
    private readonly string[]? _labels;
    public IReadOnlyList<PlatinumMap> Maps => Array.AsReadOnly(_maps);
    private static readonly int[] Strides = [20, 32, 12, 16];
    private readonly SortedSet<int> _graphics = new(), _movements = new();
    /// <summary>Appearance and movement ids that occur in NPC records of the game's own data (type 0). Only these are offered for new NPC.</summary>
    public IReadOnlyCollection<int> AccreditedGraphics => _graphics;
    public IReadOnlyCollection<int> AccreditedMovements => _movements;

    public static PlatinumEvents Open(NdsRom rom, IReadOnlyList<string>? locationNames = null)
    {
        if (rom.Header.GameCode != "CPUE" || rom.Header.Version != 1 || rom.Header.IsDsiEnhanced)
            throw new InvalidDataException("Eventos: se requiere Platino CPUE/1; no se aplican perfiles de otros juegos.");
        byte[] arm9 = rom.ReadArm9(), names = rom.ReadFile(NamesPath), matrices = rom.ReadFile(MatrixPath), scripts = rom.ReadFile(ScriptPath);
        if (Gen5Data.Crc32(arm9) != 0x4D104949 || Gen5Data.Crc32(names) != NamesCrc32 || Gen5Data.Crc32(matrices) != MatricesCrc32 || Gen5Data.Crc32(scripts) != 0x6D482515)
            throw new InvalidDataException("Eventos CPUE/1: ARM9, nombres, matrices o scripts no coinciden con las tablas acreditadas.");
        var result = Parse(arm9.AsSpan(HeaderOffset, HeaderCount * 24).ToArray(), names, rom.ReadFile(EventPath), matrices, scripts, locationNames);
        if (Gen5Data.Crc32(arm9.AsSpan(HeaderOffset, HeaderCount * 24).ToArray()) != HeadersCrc32 || result._maps.Length != HeaderCount ||
            result._blocks.Count != 534 || result._scripts.Files.Count != 1124 || (result.NormalizedHash() != NormalizedEventsSha256 && !result.IsAccreditedVisionEdit() && !result.IsAccreditedEventEdit()))
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
        for (int file = 0; file < _blocks.Count; file++)
            for (int i = 0; i < _blocks[file][1].Count; i++)
            {
                int at = _starts[file] + _blocks[file][1].Offset + i * 32;
                if (U16(_events, at + 6) != 0) continue;
                _graphics.Add(U16(_events, at + 2)); _movements.Add(U16(_events, at + 4));
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
    /// <summary>
    /// Writes X, Y and direction of an existing NPC and, when the edit carries them, its fixed-look movement type (+4) and trainer sight (+14).
    /// Every other byte stays as it is. The extended fields are only accepted where they mean what the editor says (see <see cref="PlatinumTrainerVision"/>).
    /// </summary>
    public byte[] ReplaceNpc(int mapId, int index, PlatinumNpcValues expected, PlatinumNpcValues values)
    {
        var npc = Npc(mapId, index); var bounds = Bounds(mapId);
        if (!Matches(npc, expected)) throw new InvalidOperationException("El NPC cambió desde la vista previa.");
        if (values.X < 0 || values.X >= bounds.Width || values.Y < 0 || values.Y >= bounds.Height || values.Direction is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(values), "Coordenadas fuera de la matriz o dirección fuera de 0–3.");
        if (expected.Movement.HasValue != values.Movement.HasValue || expected.Sight.HasValue != values.Sight.HasValue)
            throw new ArgumentException("El estado anterior y el propuesto deben incluir los mismos campos.", nameof(values));
        if (values.Movement is int movement && (!PlatinumTrainerVision.IsFixedLook(npc.Movement) || movement != PlatinumTrainerVision.FixedLookFor(values.Direction)))
            throw new ArgumentOutOfRangeException(nameof(values), "El tipo de movimiento solo cambia entre «mirar fijo» (14–17) y debe coincidir con la dirección propuesta.");
        if (values.Sight is int sight)
        {
            string? refusal = PlatinumTrainerVision.SightRefusal(npc) ?? (sight is < 0 or > PlatinumTrainerVision.MaxAccreditedSight ? $"debe estar entre 0 y {PlatinumTrainerVision.MaxAccreditedSight}" : null);
            if (refusal is not null) throw new ArgumentOutOfRangeException(nameof(values), $"Alcance de visión no admitido: {refusal}.");
        }
        byte[] output = (byte[])_events.Clone(); int file = Map(mapId).EventFile;
        int at = _starts[file] + _blocks[file][1].Offset + index * 32;
        Put16(output, at + 24, values.X); Put16(output, at + 26, values.Y); Put16(output, at + 12, values.Direction);
        if (values.Movement is int m) Put16(output, at + 4, m);
        if (values.Sight is int s) Put16(output, at + 14, s);
        return output;
    }
    private static bool Matches(PlatinumEvent npc, PlatinumNpcValues expected) =>
        npc.X == expected.X && npc.Y == expected.Y && npc.Direction == expected.Direction &&
        (expected.Movement is null || expected.Movement == npc.Movement) && (expected.Sight is null || expected.Sight == npc.Sight);
    public int NpcCount(int mapId) => _blocks[Map(mapId).EventFile][1].Count;

    /// <summary>The current editable values of a background object, warp or trigger (only the fields its kind has).</summary>
    public static PlatinumEventValues EventValues(PlatinumEvent e) => e.Kind switch
    {
        PlatinumEventKind.Object => new(e.X, e.Y, Facing: e.Direction),
        PlatinumEventKind.Warp => new(e.X, e.Y, Destination: e.Destination, Anchor: e.Anchor),
        PlatinumEventKind.Trigger => new(e.X, e.Y, Width: e.RangeX, Length: e.RangeY),
        _ => throw new ArgumentException("Solo objetos de fondo, warps y activadores.", nameof(e)),
    };
    public PlatinumEvent Event(int mapId, PlatinumEventKind kind, int index) => Events(mapId).FirstOrDefault(e => e.Kind == kind && e.Index == index)
        ?? throw new ArgumentOutOfRangeException(nameof(index), "Evento inexistente.");
    /// <summary>
    /// Writes the editable fields of an existing background object (position, interaction direction), warp (position; destination and warp index,
    /// except for dynamic warps, and only to an existing warp) or trigger (position and size). Script, type, flags, variables, values and every other
    /// byte stay as they are. Positions and areas must lie within the editor bounds (the most conservative when several headers share the file).
    /// </summary>
    public byte[] ReplaceEvent(int mapId, PlatinumEventKind kind, int index, PlatinumEventValues expected, PlatinumEventValues values)
    {
        if (kind is PlatinumEventKind.Npc) throw new ArgumentException("Los NPC se editan con ReplaceNpc.", nameof(kind));
        var e = Event(mapId, kind, index); var bounds = Bounds(mapId); var current = EventValues(e);
        if (expected != current) throw new InvalidOperationException("El evento cambió desde la vista previa.");
        if (values.Destination.HasValue != expected.Destination.HasValue || values.Anchor.HasValue != expected.Anchor.HasValue || values.Width.HasValue != expected.Width.HasValue
            || values.Length.HasValue != expected.Length.HasValue || values.Facing.HasValue != expected.Facing.HasValue)
            throw new ArgumentException("El estado anterior y el propuesto deben incluir los mismos campos.", nameof(values));
        int width = values.Width ?? 1, length = values.Length ?? 1;
        if (values.X < 0 || values.Y < 0 || values.X + width > bounds.Width || values.Y + length > bounds.Height)
            throw new ArgumentOutOfRangeException(nameof(values), "Posición o área fuera de los límites que acepta el editor.");
        byte[] output = (byte[])_events.Clone(); int file = Map(mapId).EventFile;
        int at = _starts[file] + _blocks[file][(int)kind].Offset + index * Strides[(int)kind];
        switch (kind)
        {
            case PlatinumEventKind.Object:
                if (values.Facing is not (>= 0 and <= PlatinumEventFields.MaxFacing)) throw new ArgumentOutOfRangeException(nameof(values), "Dirección de interacción fuera de 0–6.");
                BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 4), values.X); BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 8), values.Y);
                Put16(output, at + 16, values.Facing.Value);
                break;
            case PlatinumEventKind.Warp:
                if ((values.Destination != expected.Destination || values.Anchor != expected.Anchor) && (expected.Anchor == PlatinumEventFields.DynamicAnchor || !WarpTargetExists(values.Destination!.Value, values.Anchor!.Value)))
                    throw new ArgumentOutOfRangeException(nameof(values), expected.Anchor == PlatinumEventFields.DynamicAnchor ? "El destino de un warp dinámico lo decide la partida; no se edita." : "El destino debe ser un warp existente de un mapa existente.");
                Put16(output, at, values.X); Put16(output, at + 2, values.Y); Put16(output, at + 4, values.Destination!.Value); Put16(output, at + 6, values.Anchor!.Value);
                break;
            case PlatinumEventKind.Trigger:
                if (width is < 1 or > PlatinumEventFields.MaxTriggerSide || length is < 1 or > PlatinumEventFields.MaxTriggerSide) throw new ArgumentOutOfRangeException(nameof(values), $"El activador mide de 1 a {PlatinumEventFields.MaxTriggerSide} casillas por lado.");
                Put16(output, at + 2, values.X); Put16(output, at + 4, values.Y); Put16(output, at + 6, width); Put16(output, at + 8, length);
                break;
        }
        return output;
    }

    /// <summary>
    /// Builds a 32-byte NPC record from an existing, accredited NPC (never a ground object). Unknown bytes, sight, ranges and height are copied from
    /// the template; script, flag and type are set to 0 ("no script", "no flag", normal NPC) because a script or flag copied from another NPC would
    /// refer to that NPC's local id.
    /// </summary>
    public static byte[] NewNpcRecord(PlatinumEvent template, int id, int graphics, int movement, int direction, int x, int y, int? rawHeight = null)
    {
        if (template.Kind != PlatinumEventKind.Npc || template.Type != 0) throw new ArgumentException("La plantilla debe ser un NPC normal existente.", nameof(template));
        byte[] record = Convert.FromHexString(template.Raw);
        if (record.Length != 32) throw new InvalidDataException("Registro de NPC de plantilla inválido.");
        Put16(record, 0, id); Put16(record, 2, graphics); Put16(record, 4, movement); Put16(record, 6, 0); Put16(record, 8, 0); Put16(record, 10, 0);
        Put16(record, 12, direction); Put16(record, 24, x); Put16(record, 26, y);
        if (rawHeight is int z) BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(28), z);
        return record;
    }

    /// <summary>Why a new NPC record cannot be added to a map; empty when every structural rule holds. Spatial rules (terrain, occupancy) are the caller's.</summary>
    public IReadOnlyList<string> ValidateNewNpc(int mapId, byte[] record, int? allowedScript = null)
    {
        var problems = new List<string>(); var map = Map(mapId);
        if (record.Length != 32) { problems.Add("el registro de NPC debe medir 32 bytes"); return problems; }
        int id = U16(record, 0), gfx = U16(record, 2), movement = U16(record, 4), type = U16(record, 6), flag = U16(record, 8), script = U16(record, 10), direction = U16(record, 12), x = I16(record, 24), y = I16(record, 26);
        var existing = Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc).ToList();
        if (existing.Count >= MaxVanillaNpcCount) problems.Add($"el mapa ya tiene {existing.Count} NPC; no se supera el máximo observado en el juego original ({MaxVanillaNpcCount})");
        if (id == 0xFFFF) problems.Add("el ID 65535 está reservado");
        else if (existing.Any(e => e.Id == id)) problems.Add($"el ID {id} ya lo usa otro NPC del archivo de eventos: los scripts podrían referirse a ambos");
        if (!_graphics.Contains(gfx)) problems.Add($"el gráfico {gfx} no aparece en los NPC del juego original");
        if (!_movements.Contains(movement)) problems.Add($"el tipo de movimiento {movement} no aparece en los NPC del juego original");
        if (type != 0) problems.Add("solo se crean NPC normales (tipo 0)");
        if (flag != 0) problems.Add("un NPC nuevo no lleva flag: no se copian referencias de otro NPC");
        if (script != 0 && script != allowedScript) problems.Add("un NPC nuevo solo lleva el script que el editor acredita (copiado o compartido); no se copian referencias de otro NPC");
        if (direction > 3) problems.Add("la dirección debe estar entre 0 y 3");
        var bounds = Bounds(mapId);
        if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height) problems.Add("la posición está fuera de los límites que acepta el editor");
        return problems;
    }

    /// <summary>
    /// Returns a NEW instance whose event archive has one more NPC record appended to the NPC block of this map's event file (shared files affect every
    /// header that uses them). The archive is rebuilt through Narc; every other member must stay byte-identical and the result is re-parsed and checked.
    /// This instance is not modified. The accredited-source hash does not apply to derived data: callers verify by replaying their operation list.
    /// </summary>
    public PlatinumEvents AddNpc(int mapId, byte[] record, int? allowedScript = null)
    {
        var problems = ValidateNewNpc(mapId, record, allowedScript);
        if (problems.Count > 0) throw new InvalidOperationException("No se puede añadir el NPC: " + string.Join("; ", problems) + ".");
        int file = Map(mapId).EventFile; var archive = Narc.Read(_events); byte[] member = archive.Files[file];
        var (offset, count) = _blocks[file][1]; int end = offset + count * 32;
        byte[] grown = new byte[member.Length + 32];
        member.AsSpan(0, end).CopyTo(grown); record.CopyTo(grown, end); member.AsSpan(end).CopyTo(grown.AsSpan(end + 32));
        Put32(grown, offset - 4, (uint)(count + 1));
        archive.Files[file] = grown;
        var next = new PlatinumEvents(_headers, _names, archive.Write(), _matrices, _scriptBytes, _labels);
        var before = Narc.Read(_events).Files; var after = Narc.Read(next._events).Files;
        bool others = after.Count == before.Count && Enumerable.Range(0, before.Count).All(i => i == file || before[i].AsSpan().SequenceEqual(after[i]));
        var added = next.Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc).ToList();
        if (!others || after[file].Length != member.Length + 32 || added.Count != count + 1 || !string.Equals(added[^1].Raw, Convert.ToHexString(record), StringComparison.Ordinal))
            throw new InvalidDataException("La reconstrucción del archivo de eventos no cumple las invariantes; se descarta.");
        return next;
    }

    /// <summary>
    /// Returns a NEW instance without the NPC record at <paramref name="index"/> (later NPC of the same file move up one index). Only the caller
    /// knows whether the record may be removed (the session restricts it to records it added itself); this method enforces the structure:
    /// the record must match <paramref name="expectedRecord"/>, every other member stays byte-identical, and the result is re-parsed.
    /// </summary>
    public PlatinumEvents RemoveNpc(int mapId, int index, byte[] expectedRecord)
    {
        int file = Map(mapId).EventFile; var (offset, count) = _blocks[file][1];
        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index), "NPC inexistente.");
        var archive = Narc.Read(_events); byte[] member = archive.Files[file]; int at = offset + index * 32;
        if (expectedRecord.Length != 32 || !member.AsSpan(at, 32).SequenceEqual(expectedRecord)) throw new InvalidOperationException("El NPC ya no coincide con el registro esperado.");
        byte[] shrunk = new byte[member.Length - 32];
        member.AsSpan(0, at).CopyTo(shrunk); member.AsSpan(at + 32).CopyTo(shrunk.AsSpan(at));
        Put32(shrunk, offset - 4, (uint)(count - 1));
        archive.Files[file] = shrunk;
        var next = new PlatinumEvents(_headers, _names, archive.Write(), _matrices, _scriptBytes, _labels);
        var before = Narc.Read(_events).Files; var after = Narc.Read(next._events).Files;
        bool others = after.Count == before.Count && Enumerable.Range(0, before.Count).All(i => i == file || before[i].AsSpan().SequenceEqual(after[i]));
        var remaining = next.Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc).Select(e => e.Raw).ToList();
        var expected = Events(mapId).Where(e => e.Kind == PlatinumEventKind.Npc && e.Index != index).Select(e => e.Raw).ToList();
        if (!others || after[file].Length != member.Length - 32 || !remaining.SequenceEqual(expected))
            throw new InvalidDataException("La reconstrucción del archivo de eventos no cumple las invariantes; se descarta.");
        return next;
    }

    public byte[] Bytes() => (byte[])_events.Clone();
    /// <summary>The script archive this instance carries (the original one, or one with entries appended by <see cref="WithAppendedScript"/>).</summary>
    public byte[] ScriptArchiveBytes() => (byte[])_scriptBytes.Clone();
    public int ScriptEntryCount(int mapId) => PlatinumScriptFile.EntryOffsets(_scripts.Files[Map(mapId).ScriptFile]).Count;
    public bool HasReadableScriptFile(int mapId) => PlatinumScriptFile.TryEntryCount(_scripts.Files[Map(mapId).ScriptFile], out _);
    /// <summary>Recognises a LOCAL script (1…N) of the map's script file. Common and battle scripts are not local and return null.</summary>
    public PlatinumScriptInfo? ClassifyScript(int mapId, int number)
    {
        var member = _scripts.Files[Map(mapId).ScriptFile];
        // About half of scr_seq are level-script tables, not entry tables; a header pointing at one simply has no recognisable local scripts.
        if (!PlatinumScriptFile.TryEntryCount(member, out int count)) return new(number, PlatinumScriptPattern.Unknown, null, null, null, "");
        return number >= 1 && number <= count ? PlatinumScriptFile.Classify(member, number) : null;
    }

    /// <summary>
    /// Returns a NEW instance whose script archive has <paramref name="code"/> appended as the last entry of the map's script file. Existing
    /// script numbers keep their meaning; every other member of the archive stays byte-identical; the event archive is not touched.
    /// </summary>
    public PlatinumEvents WithAppendedScript(int mapId, byte[] code, out int number)
    {
        int file = Map(mapId).ScriptFile; var archive = Narc.Read(_scriptBytes); var before = archive.Files.Select(f => (byte[])f.Clone()).ToList();
        archive.Files[file] = PlatinumScriptFile.AppendEntry(archive.Files[file], code, out number);
        var next = new PlatinumEvents(_headers, _names, _events, _matrices, archive.Write(), _labels);
        var after = Narc.Read(next._scriptBytes).Files;
        if (after.Count != before.Count || !Enumerable.Range(0, before.Count).All(i => i == file || before[i].AsSpan().SequenceEqual(after[i])) || !next._events.AsSpan().SequenceEqual(_events))
            throw new InvalidDataException("La ampliación del archivo de scripts alteró otros miembros; se descarta.");
        return next;
    }
    public PlatinumEvents WithEvents(byte[] events)
    {
        var next = new PlatinumEvents(_headers, _names, events, _matrices, _scriptBytes, _labels);
        if (next.EventsHash() != EventsHash()) throw new InvalidDataException("La edición altera bytes ajenos a los campos editables de los eventos.");
        return next;
    }
    /// <summary>The accredited fingerprint used since phase 1: the whole NARC with direction and X/Y of every NPC cleared.</summary>
    public string NormalizedHash() => Fingerprint(0);
    /// <summary>
    /// <see cref="EditableHash"/> plus the fields <see cref="ReplaceEvent"/> may write: position and interaction direction of background objects (+4..+11,
    /// +16), position of warps (+0..+3) and their destination (+4..+7) unless the warp is dynamic (anchor 256), and position and size of triggers (+2..+9).
    /// Script, type, flags, variables, values and every other byte stay protected.
    /// </summary>
    public string EventsHash() => Fingerprint(2);
    /// <summary>
    /// Like <see cref="NormalizedHash"/>, and also clearing the sight (+14) of the trainer types that check it and the movement type (+4) of NPC whose
    /// movement is a fixed look (14–17). Which records are cleared depends only on their type and on whether their movement is a fixed look, which an
    /// accepted edit never changes: a movement turned into anything else, or a sight written on another kind of NPC, changes this fingerprint.
    /// </summary>
    public string EditableHash() => Fingerprint(1);
    private string Fingerprint(int level)
    {
        byte[] bytes = Bytes();
        for (int file = 0; file < _blocks.Count; file++)
        {
            for (int i = 0; i < _blocks[file][1].Count; i++)
            {
                int at = _starts[file] + _blocks[file][1].Offset + i * 32;
                bytes.AsSpan(at + 12, 2).Clear(); bytes.AsSpan(at + 24, 4).Clear();
                if (level < 1) continue;
                if (PlatinumTrainerVision.ChecksSight(U16(bytes, at + 6))) bytes.AsSpan(at + 14, 2).Clear();
                if (PlatinumTrainerVision.IsFixedLook(U16(bytes, at + 4))) bytes.AsSpan(at + 4, 2).Clear();
            }
            if (level < 2) continue;
            for (int i = 0; i < _blocks[file][0].Count; i++) { int at = _starts[file] + _blocks[file][0].Offset + i * 20; bytes.AsSpan(at + 4, 8).Clear(); bytes.AsSpan(at + 16, 2).Clear(); }
            for (int i = 0; i < _blocks[file][2].Count; i++)
            {
                int at = _starts[file] + _blocks[file][2].Offset + i * 12; bytes.AsSpan(at, 4).Clear();
                if (U16(bytes, at + 6) != PlatinumEventFields.DynamicAnchor) bytes.AsSpan(at + 4, 4).Clear();
            }
            for (int i = 0; i < _blocks[file][3].Count; i++) { int at = _starts[file] + _blocks[file][3].Offset + i * 16; bytes.AsSpan(at + 2, 8).Clear(); }
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    /// <summary>A ROM rebuilt with edited objects, warps or triggers: the events fingerprint of the profile, the trainer sight range and every warp destination valid.</summary>
    private bool IsAccreditedEventEdit() =>
        EventsHash() == EventsEditableSha256 &&
        _maps.Select(m => m.Id).All(id => Events(id).All(e =>
            e.Kind == PlatinumEventKind.Npc ? !PlatinumTrainerVision.ChecksSight(e.Type) || e.Sight <= PlatinumTrainerVision.MaxAccreditedSight
            : e.Kind != PlatinumEventKind.Warp || e.Anchor == PlatinumEventFields.DynamicAnchor || WarpTargetExists(e.Destination, e.Anchor)));
    /// <summary>Whether a destination header and warp index name an existing warp (what the engine places the player on).</summary>
    public bool WarpTargetExists(int header, int anchor) =>
        header >= 0 && header < _maps.Length && anchor >= 0 && anchor != PlatinumEventFields.DynamicAnchor && anchor < _blocks[_maps[header].EventFile][2].Count;
    /// <summary>A ROM rebuilt with turned trainers or edited sight: the editable fingerprint of the profile, and every trainer sight within the accredited range.</summary>
    private bool IsAccreditedVisionEdit() =>
        EditableHash() == EditableEventsSha256 &&
        _maps.Select(m => m.Id).All(id => Events(id).All(e => e.Kind != PlatinumEventKind.Npc || !PlatinumTrainerVision.ChecksSight(e.Type) || e.Sight <= PlatinumTrainerVision.MaxAccreditedSight));
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
    private static void Put32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    private static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)value));
}
