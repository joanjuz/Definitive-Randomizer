#nullable enable
using System;
using System.Collections.Generic;

namespace pk3DS.Core.Maps;

/// <summary>
/// Presentation contract shared by the map editors of every family. It only
/// describes what the user sees and which operations an adapter allows; each
/// family keeps its own binary readers and writers behind it, and no binary
/// layout is shared through these types.
/// </summary>
public interface IMapEditorBackend
{
    /// <summary>Stable identifier of the adapter, e.g. "gen6-oras".</summary>
    string Family { get; }
    /// <summary>Game title shown to the user.</summary>
    string Title { get; }
    IReadOnlyList<MapZoneEntry> Zones { get; }
    IMapResourceProvider Resources { get; }
    IMapEventProvider Events { get; }
    /// <summary>Null while the adapter is read-only.</summary>
    IMapMutationValidator? Mutations { get; }
    MapCapabilities Capabilities { get; }
}

/// <summary>Map geometry and images of one zone.</summary>
public interface IMapResourceProvider
{
    MapLayout Layout(int zone);
    /// <summary>Collision/permission cells of one block, or null when the block has none.</summary>
    MapCellGrid? Cells(int block);
    /// <summary>
    /// Image of one block drawn from its game models, <paramref name="size"/> pixels a side,
    /// or null when the adapter cannot draw it. Zone-dependent because textures may come from the zone's area.
    /// </summary>
    MapBlockImage? Graphic(int zone, int block, int size);
    /// <summary>
    /// Optional diagnostic image of a block (for example collision surfaces), clearly not the
    /// game's graphics. Null when the adapter has none.
    /// </summary>
    MapBlockImage? Diagnostic(int zone, int block, int size) => null;
}

/// <summary>BGRA pixels (first row at the top) of a rendered block.</summary>
public sealed record MapBlockImage(int Size, byte[] Pixels, int Triangles, int Untextured);

public interface IMapEventProvider
{
    IReadOnlyList<MapEventView> ZoneEvents(int zone);
}

/// <summary>Checks and applies edits to fields whose format is verified.</summary>
public interface IMapMutationValidator
{
    MapMutationCheck Validate(int zone, MapMutation mutation);
    /// <summary>Whether an event may be placed on the given cell of its zone.</summary>
    MapMutationCheck ValidateMove(int zone, MapEventKind kind, int index, int x, int y);
}

/// <summary>What an adapter can do, with the reason when it cannot.</summary>
/// <param name="GraphicsByDefault">Show the model layer when the editor opens (for adapters without a cell grid).</param>
public sealed record MapCapabilities(bool Graphics, bool Events, bool Editing, string Note, bool GraphicsByDefault = false, string? DiagnosticLayer = null);

public sealed record MapZoneEntry(int Id, string Name, int Layout, string Detail);

/// <summary>
/// Blocks of a zone in cells. <see cref="Blocks"/> is row-major; -1 means no block.
/// </summary>
public sealed record MapLayout(int Width, int Height, int BlockCells, IReadOnlyList<int> Blocks)
{
    public int BlockAt(int column, int row) =>
        column < 0 || row < 0 || column >= Width || row >= Height ? -1 : Blocks[(row * Width) + column];
}

public sealed record MapCellGrid(int Width, int Height, IReadOnlyList<uint> Cells);

public enum MapEventKind { Npc, Prop, Warp, Trigger, ZoneTrigger }

/// <summary>
/// One event as seen on the map. Coordinates are in cells of the zone; Width
/// and Height are 1 for point events.
/// </summary>
public sealed record MapEventView(
    MapEventKind Kind,
    int Index,
    double X,
    double Y,
    double Width,
    double Height,
    int? Facing,
    string Label,
    IReadOnlyList<MapEventProperty> Properties,
    string? PositionNote = null)
{
    /// <summary>False when the adapter cannot place the event on the map; <see cref="PositionNote"/> says why.</summary>
    public bool Placed => PositionNote is null;
}

public sealed record MapEventProperty(string Name, string Value, bool Writable = false, string? Note = null);

public sealed record MapMutation(MapEventKind Kind, int Index, string Property, int Value);

/// <summary>
/// Facing and sight of a character as its adapter reads them. <see cref="Facing"/> is a compass angle on
/// the map: 0 up, 90 right, 180 down, 270 left. <see cref="Sight"/> is in cells and null when the format
/// keeps it somewhere the adapter has not located. <see cref="FreeAngle"/> false means only the four
/// cardinal directions exist.
/// </summary>
public sealed record MapTrainerView(double Facing, int? Sight, bool FacingWritable, bool SightWritable, bool FreeAngle, int MaxSight, string Note);

/// <summary>Optional part of an adapter that reads, and may write, the facing and sight of characters.</summary>
public interface IMapTrainerViews
{
    /// <summary>Null when the event is not a character whose facing the adapter knows.</summary>
    MapTrainerView? View(int zone, MapEventKind kind, int index);
    MapMutationCheck ValidateView(int zone, MapEventKind kind, int index, double facing, int? sight);
    /// <summary>Cells covered by a sight of <paramref name="sight"/> cells facing <paramref name="facing"/>; empty when the sight shape is not known.</summary>
    IReadOnlyList<(int X, int Y)> SightCells(int zone, MapEventKind kind, int index, double facing, int sight);
}

public sealed record MapMutationCheck(bool Allowed, string Reason);

/// <summary>Optional part of an adapter that can turn plain-ground cells into cells with wild encounters.</summary>
public interface IMapCatchZones
{
    /// <summary>Whether the zone already has an encounter table.</summary>
    bool HasEncounterTable(int zone);
    /// <summary>Whether a source table must be chosen (Gen6: zones without a table; USUM: always, a region names its table).</summary>
    bool RequiresTableSource(int zone);
    /// <summary>Zones with an encounter table that can be copied into a zone without one (id and name).</summary>
    IReadOnlyList<MapZoneEntry> EncounterSources { get; }
    /// <summary>Cells of the zone that already produce encounters (encounter value, owned by the zone).</summary>
    IReadOnlyList<(int X, int Y)> EncounterCells(int zone);
    /// <summary>Cells of the zone where a catch zone can be created (the surface the adapter writes, owned by the zone, without encounters yet).</summary>
    IReadOnlyList<(int X, int Y)> PaintableCells(int zone);
    /// <summary>Checks the cells and, for a zone without a table, the zone whose table is copied. Nothing is written.</summary>
    MapMutationCheck ValidateCatchCells(int zone, IReadOnlyCollection<(int X, int Y)> cells, int? sourceZone);
}

/// <summary>Optional part of an adapter that can add a Pokémon Center nurse copied from the game itself.</summary>
public interface IMapNurseCreation
{
    /// <summary>Where the copied nurse comes from, or why the game has none to copy.</summary>
    string NurseSummary { get; }
    /// <summary>Checks a new nurse on cell (x, y) looking towards <paramref name="facing"/> (compass degrees). Nothing is written.</summary>
    MapMutationCheck ValidateAddNurse(int zone, int x, int y, double facing);
}

/// <summary>Optional X/Y service copied only after its executable and common script are accredited.</summary>
public interface IMapReminderCreation
{
    bool CanAddReminder { get; }
    string ReminderSummary { get; }
    MapMutationCheck ValidateAddReminder(int zone, int x, int y, double facing);
}

/// <summary>Raised when a file does not have the layout an adapter verified.</summary>
public sealed class MapFormatException(string message) : Exception(message);
