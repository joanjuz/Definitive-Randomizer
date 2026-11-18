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

public sealed record MapMutationCheck(bool Allowed, string Reason);

/// <summary>Raised when a file does not have the layout an adapter verified.</summary>
public sealed class MapFormatException(string message) : Exception(message);
