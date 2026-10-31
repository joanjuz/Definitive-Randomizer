#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>Editable sections of a DS game, in the order the workspace shows them.</summary>
public enum NdsSection { Starters, Trainers, Encounters, FieldItems, Shops, Pokemon, Moves, Learnsets, Evolutions, Machines, Statics, Trades }

/// <summary>What a cell's value names, so the editor can offer a list instead of a number.</summary>
public enum NdsValueKind { Number, Species, Item, Move, Type, Ability, Option, Flag }

/// <summary>
/// One editable value: the entry it belongs to (a trainer, an area, a shop…), its group and field, and its range.
/// <see cref="Local"/> identifies the value inside its entry; <see cref="Key"/> is unique in the section.
/// </summary>
public sealed record NdsCell(int Entry, string EntryName, string Group, string Field, string Local, int Value, int Min, int Max, NdsValueKind Kind)
{
    /// <summary>Names of the values of an <see cref="NdsValueKind.Option"/> cell, by value.</summary>
    public IReadOnlyList<string>? Options { get; init; }
    public string Key => Entry + ":" + Local;
}

/// <summary>
/// Manual edits for Gen4 and Gen5: every section is read as cells and written back from a key → value map into new tables,
/// returned like a randomizer result so edits and randomizations stack the same way. Starters go through the randomizer's
/// custom-starter path, which updates every place the game uses them.
/// </summary>
public static partial class NdsCells
{
    private enum Target { File, Overlay, Arm9 }

    // Where a cell lives: a file of a NARC (by profile key), an overlay or the ARM9; Width is 1, 2 or 4 bytes.
    // Shift/Bits select a bit field inside the value (packed learnset entries, TM/HM flags); 0 bits means the whole value.
    private sealed record Location(NdsCell Cell, Target Target, string NarcKey, int File, int Overlay, int Offset, int Width, int FormOffset = -1, int Shift = 0, int Bits = 0)
    {
        /// <summary>Other positions of the same NARC that hold the same value and change with it (static encounters' script copies).</summary>
        public IReadOnlyList<(int File, int Offset)> Copies { get; init; } = [];
    }

    public static IReadOnlyList<NdsCell> Read(Gen5Data data, NdsSection section) =>
        section == NdsSection.Starters ? StarterCells(data.Starters(), data.SpeciesNames(), Gen5Randomizer.MaxSpecies) : Locate(data, section).Select(l => l.Cell).ToArray();

    private static IReadOnlyList<Location> Locate(Gen5Data data, NdsSection section) => section >= NdsSection.Pokemon ? LocateData(NdsDataSource.Of(data), section) : LocateGame(data, section);
    private static IReadOnlyList<Location> Locate(Gen4Data data, NdsSection section) => section >= NdsSection.Pokemon ? LocateData(NdsDataSource.Of(data), section) : LocateGame(data, section);

    public static IReadOnlyList<NdsCell> Read(Gen4Data data, NdsSection section) =>
        section == NdsSection.Starters ? StarterCells(data.Starters(), data.SpeciesNames(), Gen4Data.MaxSpecies) : Locate(data, section).Select(l => l.Cell).ToArray();

    public static Gen5RandomizerResult Write(Gen5Data data, NdsSection section, IReadOnlyDictionary<string, int> edits)
    {
        if (section == NdsSection.Starters)
            return Gen5Randomizer.Generate(data, new Gen5RandomizerOptions(0) { Starters = Gen5StarterMode.Custom, CustomStarters = StarterValues(data.Starters(), edits, Gen5Randomizer.MaxSpecies) });
        var overlays = new Dictionary<int, byte[]>();
        var result = Apply(Locate(data, section), edits, data.CopyNarc, data.PathOf, id => overlays.TryGetValue(id, out var o) ? o : overlays[id] = OverlayOf(data, id), null);
        return new Gen5RandomizerResult(result.Files, result.Changes, overlays)
        {
            RecompressOverlays = overlays.Keys.Where(id => id == data.Profile.ShopOverlay).ToHashSet(),
        };
    }

    public static Gen5RandomizerResult Write(Gen4Data data, NdsSection section, IReadOnlyDictionary<string, int> edits)
    {
        if (section == NdsSection.Starters)
            return Gen4Randomizer.Generate(data, new Gen5RandomizerOptions(0) { Starters = Gen5StarterMode.Custom, CustomStarters = StarterValues(data.Starters(), edits, Gen4Data.MaxSpecies) });
        byte[]? arm9 = null;
        var result = Apply(Locate(data, section), edits, data.CopyNarc, data.PathOf, _ => throw new InvalidOperationException(), () => arm9 ??= data.Arm9());
        return new Gen5RandomizerResult(result.Files, result.Changes, new Dictionary<int, byte[]>()) { Arm9 = arm9 };
    }

    private static byte[] OverlayOf(Gen5Data data, int id) => id == data.Profile.ShopOverlay ? data.ShopOverlay()
        : throw new InvalidOperationException("Overlay inesperado en una edición: " + id);

    private static IReadOnlyList<NdsCell> StarterCells(IReadOnlyList<int> starters, IReadOnlyList<string> names, int max)
    {
        string[] slots = ["Planta", "Fuego", "Agua"];
        return starters.Select((s, i) => new NdsCell(0, "Iniciales", "Iniciales", slots[i], "Starter" + (i + 1), s, 1, max, NdsValueKind.Species)).ToArray();
    }

    private static int[] StarterValues(IReadOnlyList<int> current, IReadOnlyDictionary<string, int> edits, int max)
    {
        var values = current.Select((s, i) => edits.TryGetValue("0:Starter" + (i + 1), out int v) ? v : s).ToArray();
        if (values.Any(v => v < 1 || v > max) || values.Distinct().Count() != 3)
            throw new ArgumentException($"Se necesitan tres iniciales distintos entre 1 y {max}.");
        return values;
    }

    private static (IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<Gen5Change> Changes) Apply(IReadOnlyList<Location> locations,
        IReadOnlyDictionary<string, int> edits, Func<string, Narc> copy, Func<string, string> pathOf, Func<int, byte[]> overlay, Func<byte[]>? arm9)
    {
        var byKey = locations.ToDictionary(l => l.Cell.Key);
        var narcs = new Dictionary<string, Narc>();
        var changes = new List<Gen5Change>();
        foreach (var (key, value) in edits)
        {
            if (!byKey.TryGetValue(key, out var at))
                throw new ArgumentException("Valor desconocido en esta sección: " + key);
            var cell = at.Cell;
            if (value < cell.Min || value > cell.Max)
                throw new ArgumentException($"{cell.EntryName} · {cell.Field}: debe estar entre {cell.Min} y {cell.Max}.");
            if (value == cell.Value)
                continue;
            byte[] target = at.Target switch
            {
                Target.File => (narcs.TryGetValue(at.NarcKey, out var n) ? n : narcs[at.NarcKey] = copy(at.NarcKey)).Files[at.File],
                Target.Overlay => overlay(at.Overlay),
                _ => (arm9 ?? throw new InvalidOperationException("Esta sección no usa el ARM9."))(),
            };
            if (at.Bits > 0)
            {
                int mask = ((1 << at.Bits) - 1) << at.Shift;
                Write(target, at.Offset, at.Width, (ReadValue(target, at.Offset, at.Width) & ~mask) | ((value << at.Shift) & mask));
            }
            else
                Write(target, at.Offset, at.Width, value);
            foreach (var (file, offset) in at.Copies)
                Write(narcs[at.NarcKey].Files[file], offset, at.Width, value);
            // A new species cannot keep the old one's form; form 0 always exists.
            if (at.FormOffset >= 0)
                Write(target, at.FormOffset, 2, 0);
            changes.Add(new Gen5Change($"{cell.EntryName} · {cell.Group}", cell.Field, cell.Value, value) );
        }
        return (narcs.ToDictionary(p => pathOf(p.Key), p => p.Value.Write()), changes);
    }

    private static int ReadValue(byte[] data, int offset, int width) => width switch
    {
        1 => data[offset],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
        _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)),
    };

    private static void Write(byte[] data, int offset, int width, int value)
    {
        if (offset < 0 || offset + width > data.Length)
            throw new InvalidDataException("Un valor editado queda fuera de su tabla.");
        switch (width)
        {
            case 1: data[offset] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)value); break;
            default: BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), (uint)value); break;
        }
    }

    private static int MaxId(IReadOnlyList<string> names) => Math.Max(0, names.Count - 1);

    // ---- Gen5 ----

    private static IReadOnlyList<Location> LocateGame(Gen5Data data, NdsSection section)
    {
        var list = new List<Location>();
        int items = MaxId(data.Text(data.Profile.ItemNamesText));
        switch (section)
        {
            case NdsSection.Trainers:
            {
                int moves = MaxId(data.Text(data.Profile.MoveNamesText));
                foreach (var t in data.Trainers())
                {
                    string name = $"{t.ClassName} {t.Name}".Trim();
                    int size = 8 + (t.HasItems ? 2 : 0) + (t.HasMoves ? 8 : 0);
                    for (int slot = 0; slot < t.Team.Count; slot++)
                    {
                        var p = t.Team[slot];
                        int at = slot * size;
                        string group = "Pokémon " + (slot + 1), prefix = "P" + slot + ".";
                        Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width, int form = -1) =>
                            new(new NdsCell(t.Id, name, group, field, prefix + local, value, min, max, kind), Target.File, Gen5Profile.TrainerPokemon, t.Id, -1, offset, width, form);
                        list.Add(L("Especie", "Species", p.Species, 1, Gen5Randomizer.MaxSpecies, NdsValueKind.Species, at + 4, 2, at + 6));
                        list.Add(L("Nivel", "Level", p.Level, 1, 100, NdsValueKind.Number, at + 2, 2));
                        if (t.HasItems)
                            list.Add(L("Objeto equipado", "Item", p.HeldItem, 0, items, NdsValueKind.Item, at + 8, 2));
                        for (int m = 0; t.HasMoves && m < 4; m++)
                            list.Add(L("Movimiento " + (m + 1), "Move" + (m + 1), p.Moves[m], 0, moves, NdsValueKind.Move, at + 8 + (t.HasItems ? 2 : 0) + m * 2, 2));
                    }
                }
                break;
            }
            case NdsSection.Encounters:
                foreach (var area in data.Encounters())
                {
                    int entry = area.File * 4 + area.Season, start = area.Season * 232 + 8 + Enumerable.Range(0, area.Type).Sum(t => SlotsPerType[t] * 4);
                    string name = $"Zona {area.File:000} · estación {area.Season + 1}", group = area.TypeName;
                    for (int i = 0; i < area.Slots.Count; i++)
                    {
                        var s = area.Slots[i];
                        int at = start + i * 4;
                        string prefix = "T" + area.Type + ".S" + i + ".";
                        Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width) =>
                            new(new NdsCell(entry, name, group, field, prefix + local, value, min, max, kind), Target.File, Gen5Profile.WildPokemon, area.File, -1, offset, width);
                        // Species and form share one 16-bit value: a new species is written with form 0.
                        list.Add(L("Hueco " + (i + 1) + " · especie", "Species", s.Species, 0, Gen5Randomizer.MaxSpecies, NdsValueKind.Species, at, 2));
                        list.Add(L("Hueco " + (i + 1) + " · nivel mínimo", "MinLevel", s.MinLevel, 1, 100, NdsValueKind.Number, at + 2, 1));
                        list.Add(L("Hueco " + (i + 1) + " · nivel máximo", "MaxLevel", s.MaxLevel, 1, 100, NdsValueKind.Number, at + 3, 1));
                    }
                }
                break;
            case NdsSection.FieldItems:
            {
                var field = data.FieldItems();
                for (int i = 0; i < field.Count; i++)
                {
                    var (file, offset, item) = field[i];
                    string name = (file == data.Profile.ItemBallScript ? "Objeto visible " : "Objeto oculto ") + (i + 1);
                    list.Add(new(new NdsCell(i, name, "Objeto", "Objeto", "Item", item, 1, items, NdsValueKind.Item), Target.File, Gen5Profile.Scripts, file, -1, offset, 2));
                }
                break;
            }
            case NdsSection.Shops:
            {
                if (data.Unavailable.ContainsKey(Gen5Feature.Shops))
                    break;
                var shops = data.Shops();
                for (int shop = 0; shop < shops.Count; shop++)
                    for (int i = 0; i < shops[shop].Count; i++)
                    {
                        var cell = new NdsCell(shop, "Tienda " + shop + (data.Profile.FixedShops.Contains(shop) ? " · fija" : ""), "Productos", "Producto " + (i + 1), "Item" + i, shops[shop][i], 0, items, NdsValueKind.Item);
                        list.Add(data.Profile.ShopOverlay >= 0
                            ? new Location(cell, Target.Overlay, "", -1, data.Profile.ShopOverlay, data.Profile.ShopItemOffsets[shop] + i * 2, 2)
                            : new Location(cell, Target.File, Gen5Profile.ShopItems, shop, -1, i * 2, 2));
                    }
                break;
            }
        }
        return list;
    }

    private static readonly int[] SlotsPerType = [12, 12, 12, 5, 5, 5, 5];

    // ---- Gen4 ----

    private static IReadOnlyList<Location> LocateGame(Gen4Data data, NdsSection section)
    {
        var list = new List<Location>();
        int items = MaxId(data.Text(data.Profile.ItemNamesText));
        bool hgss = data.Profile.Kind == Gen4Kind.HeartGoldSoulSilver;
        switch (section)
        {
            case NdsSection.Trainers:
            {
                int moves = MaxId(data.Text(data.Profile.MoveNamesText));
                foreach (var t in data.Trainers())
                {
                    string name = $"{t.ClassName} {t.Name}".Trim();
                    int size = data.TeamEntrySize(t.Flags);
                    for (int slot = 0; slot < t.Team.Count; slot++)
                    {
                        var p = t.Team[slot];
                        int at = slot * size;
                        string group = "Pokémon " + (slot + 1), prefix = "P" + slot + ".";
                        Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width) =>
                            new(new NdsCell(t.Id, name, group, field, prefix + local, value, min, max, kind), Target.File, Gen4Profile.TrainerPokemon, t.Id, -1, offset, width);
                        // Species and form share one 16-bit value: a new species is written with form 0.
                        list.Add(L("Especie", "Species", p.Species, 1, Gen4Data.MaxSpecies, NdsValueKind.Species, at + 4, 2));
                        list.Add(L("Nivel", "Level", p.Level, 1, 100, NdsValueKind.Number, at + 2, 2));
                        if (t.HasItems)
                            list.Add(L("Objeto equipado", "Item", p.HeldItem, 0, items, NdsValueKind.Item, at + 6, 2));
                        for (int m = 0; t.HasMoves && m < 4; m++)
                            list.Add(L("Movimiento " + (m + 1), "Move" + (m + 1), p.Moves[m], 0, moves, NdsValueKind.Move, at + 6 + (t.HasItems ? 2 : 0) + m * 2, 2));
                    }
                }
                break;
            }
            case NdsSection.Encounters:
            {
                var groups = data.Encounters().GroupBy(s => s.File);
                foreach (var file in groups)
                {
                    string name = $"Zona {file.Key:000}";
                    int groupIndex = 0;
                    foreach (var group in file.GroupBy(s => s.Group))
                    {
                        string prefix = "G" + groupIndex++ + ".";
                        bool grass = group.Key == "Hierba/cueva";
                        bool water = !group.Key.StartsWith("Hierba", StringComparison.Ordinal) && !group.Key.StartsWith("Radio", StringComparison.Ordinal)
                            && !group.Key.StartsWith("Enjambre", StringComparison.Ordinal) && group.Key != "Pesca nocturna";
                        foreach (var s in group)
                        {
                            string slot = "Hueco " + (s.Index + 1);
                            Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width) =>
                                new(new NdsCell(file.Key, name, group.Key, slot + " · " + field, prefix + "S" + s.Index + "." + local, value, min, max, kind), Target.File, Gen4Profile.WildPokemon, file.Key, -1, offset, width);
                            list.Add(L("especie", "Species", s.Species, 1, Gen4Data.MaxSpecies, NdsValueKind.Species, s.Offset, s.Width));
                            // Levels: Diamond/Platinum grass has one per slot, water a minimum and a maximum (bytes 1 and 0 of the
                            // level word); HeartGold water stores them before the species. HeartGold grass levels are shared below.
                            if (!hgss && grass)
                                list.Add(L("nivel", "Level", s.MinLevel, 1, 100, NdsValueKind.Number, s.Offset - 4, 4));
                            else if (water)
                            {
                                list.Add(L("nivel mínimo", "MinLevel", s.MinLevel, 1, 100, NdsValueKind.Number, hgss ? s.Offset - 2 : s.Offset - 3, 1));
                                list.Add(L("nivel máximo", "MaxLevel", s.MaxLevel, 1, 100, NdsValueKind.Number, hgss ? s.Offset - 1 : s.Offset - 4, 1));
                            }
                        }
                    }
                    if (hgss && file.Any(s => s.Group.StartsWith("Hierba/cueva · ", StringComparison.Ordinal)))
                    {
                        byte[] raw = data.CopyNarc(Gen4Profile.WildPokemon).Files[file.Key];
                        for (int i = 0; i < 12; i++)
                            list.Add(new(new NdsCell(file.Key, name, "Niveles de hierba (mañana, día y noche)", "Hueco " + (i + 1) + " · nivel", "Grass.L" + i, raw[8 + i], 1, 100, NdsValueKind.Number),
                                Target.File, Gen4Profile.WildPokemon, file.Key, -1, 8 + i, 1));
                    }
                }
                break;
            }
            case NdsSection.FieldItems:
            {
                var field = data.FieldItems();
                for (int i = 0; i < field.Count; i++)
                {
                    var (hidden, offset, item) = field[i];
                    var cell = new NdsCell(i, (hidden ? "Objeto oculto " : "Objeto visible ") + (i + 1), "Objeto", "Objeto", "Item", item, 1, items, NdsValueKind.Item);
                    list.Add(hidden ? new Location(cell, Target.Arm9, "", -1, -1, offset, 2) : new Location(cell, Target.File, Gen4Profile.Scripts, data.Profile.ItemBallScript, -1, offset, 2));
                }
                break;
            }
            case NdsSection.Shops:
                if (data.Unavailable.ContainsKey(Gen5Feature.Shops))
                    break;
                foreach (var s in data.Shops())
                    list.Add(new(new NdsCell(s.Shop, "Tienda " + s.Shop + (data.Profile.FixedShops.Contains(s.Shop) ? " · fija" : ""), "Productos", "Producto " + (s.Index + 1), "Item" + s.Index, s.Item, 0, items, NdsValueKind.Item),
                        Target.Arm9, "", -1, -1, s.Offset, 2));
                break;
        }
        return list;
    }
}
