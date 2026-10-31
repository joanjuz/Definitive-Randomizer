#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Where a game keeps its egg moves, tutor moves and tutor compatibility (UPR-ZX gen4/gen5_offsets.ini and Gen4/Gen5Constants, GPLv3,
/// checked on the owner's dumps). Parts the game lacks, or that a romhack changed, are left out.
/// </summary>
public sealed record NdsMoveTables
{
    /// <summary>Overlay with the egg move list after the UPR-ZX prefix (Diamond/Platinum), or -1.</summary>
    public int EggOverlay { get; init; } = -1;
    /// <summary>The "EggMoves" NARC: one file per species with a count and the moves (Gen5) or one list for every species (HeartGold).</summary>
    public bool EggNarc { get; init; }
    public bool EggNarcPerSpecies { get; init; }
    public int TutorOverlay { get; init; } = -1;
    public int TutorCount { get; init; }
    public int TutorOffset { get; init; }
    public int TutorStride { get; init; }
    /// <summary>Tutor compatibility: in an overlay (Platinum), in a plain file (HeartGold) or in the species data (Black 2/White 2).</summary>
    public int CompatOverlay { get; init; } = -1;
    public string? CompatFile { get; init; }
    public bool CompatInPersonal { get; init; }
    public int CompatOffset { get; init; }
    public int CompatBytes { get; init; }
}

/// <summary>
/// Gen4/Gen5 species, move, learnset, evolution and TM/HM tables seen the same way: where each one lives and the byte layout
/// of the generation (UPR-ZX Gen4Constants/Gen5Constants and the owner's dumps). Built from a game view, draft views included.
/// </summary>
public sealed class NdsDataSource
{
    public int Generation { get; }
    public string GameCode { get; }
    public int MaxSpecies { get; }
    public IReadOnlyList<string> SpeciesNames { get; }
    public IReadOnlyList<string> MoveNames { get; }
    public IReadOnlyList<string> TypeNames { get; }
    public IReadOnlyList<string> AbilityNames { get; }
    public IReadOnlyList<string> ItemNames { get; }
    /// <summary>Move taught by each TM/HM, in the order of the compatibility bits (TMs, then HMs); empty when the ARM9 list is not found.</summary>
    public IReadOnlyList<int> MachineMoves { get; }
    /// <summary>ARM9 offset of TM01's move when the TM list can be edited (Gen4 with its original ARM9), or -1.</summary>
    public int MachineTable { get; private set; } = -1;
    public NdsMoveTables Tables { get; }
    internal Func<string, Narc> Copy { get; }
    internal Func<string, string> PathOf { get; }
    internal string Personal { get; }
    /// <summary>Decompressed copy of one of the game's overlays, with the draft's changes.</summary>
    internal Func<int, byte[]> Overlay { get; }
    /// <summary>Copy of one of the game's plain files (HeartGold tutor compatibility), with the draft's changes.</summary>
    internal Func<string, byte[]> RawFile { get; }

    private NdsDataSource(string gameCode, int generation, int maxSpecies, Func<string, Narc> copy, Func<string, string> pathOf, Func<int, IReadOnlyList<string>> text,
        int speciesText, int moveText, int typeText, int abilityText, int itemText, string personal, Func<byte[]?> arm9,
        Func<int, byte[]> overlay, Func<string, byte[]> rawFile, NdsMoveTables tables, bool editableMachines)
    {
        Overlay = overlay;
        RawFile = rawFile;
        Tables = tables;
        GameCode = gameCode;
        Generation = generation;
        MaxSpecies = maxSpecies;
        Copy = copy;
        PathOf = pathOf;
        Personal = personal;
        SpeciesNames = text(speciesText);
        MoveNames = text(moveText);
        TypeNames = text(typeText);
        AbilityNames = text(abilityText);
        ItemNames = text(itemText);
        MachineMoves = ReadMachines(arm9, editableMachines);
    }

    public static NdsDataSource Of(Gen5Data data) => new(data.Profile.GameCode, 5, Gen5Randomizer.MaxSpecies, data.CopyNarc, data.PathOf, data.Text, data.Profile.SpeciesNamesText,
        data.Profile.MoveNamesText, data.Profile.TypeNamesText, data.Profile.AbilityNamesText, data.Profile.ItemNamesText, Gen5Profile.PokemonStats, () => Try(data.ReadArm9),
        data.ReadOverlay, path => throw new InvalidOperationException("Gen5 no usa el archivo " + path + "."), new NdsMoveTables
        {
            EggNarc = !data.Unavailable.ContainsKey(Gen5Feature.EggMoves), EggNarcPerSpecies = true,
            TutorOverlay = data.Profile.FieldOverlay, TutorCount = data.Unavailable.ContainsKey(Gen5Feature.Tutors) ? 0 : data.Profile.TutorCount,
            TutorOffset = data.Profile.TutorOffset, TutorStride = data.Profile.TutorStride, CompatInPersonal = true, CompatOffset = 0x3C,
        }, editableMachines: false);

    public static NdsDataSource Of(Gen4Data data)
    {
        var p = data.Profile;
        bool hgss = p.Kind == Gen4Kind.HeartGoldSoulSilver, eggs = !data.Unavailable.ContainsKey(Gen5Feature.EggMoves);
        bool arm9 = !data.Unavailable.ContainsKey(Gen5Feature.Shops);
        return new(p.GameCode, 4, Gen4Data.MaxSpecies, data.CopyNarc, data.PathOf, data.Text, p.SpeciesNamesText, p.MoveNamesText, p.TypeNamesText, p.AbilityNamesText,
            p.ItemNamesText, Gen4Profile.PokemonStats, () => arm9 ? Try(data.Arm9) : null, data.ReadOverlay, data.ReadRawFile, new NdsMoveTables
            {
                EggOverlay = !hgss && eggs ? p.FieldOverlay : -1, EggNarc = hgss && eggs,
                TutorOverlay = p.FieldOverlay, TutorCount = data.Unavailable.ContainsKey(Gen5Feature.Tutors) ? 0 : p.TutorCount, TutorOffset = p.TutorOffset, TutorStride = p.TutorStride,
                CompatOverlay = hgss ? -1 : p.FieldOverlay, CompatFile = p.TutorCompatFile, CompatOffset = p.TutorCompatOffset, CompatBytes = p.TutorCompatBytes,
            }, editableMachines: arm9);
    }

    private static byte[]? Try(Func<byte[]> read)
    {
        try { return read(); }
        catch (InvalidDataException) { return null; }
    }

    private bool G5 => Generation == 5;
    // Species data (personal): offsets per generation.
    internal int CatchRate => 8;
    internal int GenderRatio => G5 ? 18 : 16;
    internal int Growth => G5 ? 21 : 19;
    internal int[] Abilities => G5 ? [24, 25, 26] : [22, 23];
    internal int[] HeldItems => G5 ? [12, 14, 16] : [12, 14];
    internal int MachineBits => G5 ? 40 : 28;
    public int TmCount => G5 ? 95 : 92;
    public int HmCount => G5 ? 6 : 8;
    // Moves.
    internal int MoveType => G5 ? 0 : 4;
    internal int MoveCategory => 2;
    internal int MovePower => 3;
    internal int MoveAccuracy => G5 ? 4 : 5;
    internal int MovePp => G5 ? 5 : 6;
    internal int MovePriority => G5 ? 6 : 10;
    public IReadOnlyList<string> Categories => G5 ? ["Estado", "Físico", "Especial"] : ["Físico", "Especial", "Estado"];
    public int StatusCategory => G5 ? 0 : 2;
    /// <summary>Gen4 has a "???" type (9) that no species or move should get.</summary>
    public IEnumerable<int> UsableTypes => Enumerable.Range(0, TypeNames.Count).Where(t => G5 || t != 9);

    public static readonly IReadOnlyList<string> GrowthNames = ["Medio rápido", "Errático", "Fluctuante", "Medio lento", "Rápido", "Lento"];
    private static readonly string[] Gen4Methods =
    [
        "—", "Amistad", "Amistad de día", "Amistad de noche", "Nivel", "Intercambio", "Intercambio con objeto", "Usar objeto",
        "Nivel (Ataque > Defensa)", "Nivel (Ataque = Defensa)", "Nivel (Ataque < Defensa)", "Nivel (Silcoon)", "Nivel (Cascoon)",
        "Nivel (Ninjask)", "Nivel (Shedinja)", "Belleza", "Usar objeto (macho)", "Usar objeto (hembra)", "Nivel con objeto de día",
        "Nivel con objeto de noche", "Conocer movimiento", "Con Pokémon en el equipo", "Nivel (macho)", "Nivel (hembra)",
        "Nivel en campo magnético", "Nivel junto a Roca Musgo", "Nivel junto a Roca Hielo",
    ];
    /// <summary>Evolution methods by number; Gen5 inserts the Karrablast/Shelmet trade at 7 and shifts the rest.</summary>
    public IReadOnlyList<string> EvolutionMethods => G5 ? [.. Gen4Methods[..7], "Intercambio (Karrablast/Shelmet)", .. Gen4Methods[7..]] : Gen4Methods;
    internal int LevelMethod => 4;
    public bool IsTradeMethod(int method) => method is 5 or 6 || G5 && method == 7;

    private IReadOnlyList<int> ReadMachines(Func<byte[]?> arm9, bool editable)
    {
        byte[]? code = arm9();
        if (code is null)
            return [];
        // HeartGold keeps its tables in a/ paths and has its own TM list prefix (UPR-ZX hgssTMDataPrefix).
        byte[] prefix = Convert.FromHexString(G5 ? "87038803" : PathOf(Personal).StartsWith("a/", StringComparison.Ordinal) ? "1E003200" : "D100D200D300D400");
        int at = -1;
        for (int i = 0; i + prefix.Length <= code.Length; i++)
            if (code.AsSpan(i, prefix.Length).SequenceEqual(prefix))
            {
                if (at >= 0)
                    return [];
                at = i + prefix.Length;
            }
        int total = TmCount + HmCount;
        if (at < 0 || at + total * 2 > code.Length)
            return [];
        if (editable && !G5)
            MachineTable = at;
        var list = Enumerable.Range(0, total).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(at + i * 2))).ToArray();
        // Gen5 stores TM01-92, HM01-06, TM93-95; the compatibility bits are TM01-95 then HM01-06.
        return G5 ? [.. list[..92], .. list[98..101], .. list[92..98]] : list;
    }

    /// <summary>Move taught by each tutor, in the order of the tutor list (empty when the game has no tutors).</summary>
    public IReadOnlyList<int> TutorMoves()
    {
        if (Tables.TutorCount == 0)
            return [];
        byte[] overlay = Overlay(Tables.TutorOverlay);
        return Enumerable.Range(0, Tables.TutorCount).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(overlay.AsSpan(Tables.TutorOffset + i * Tables.TutorStride))).ToArray();
    }

    public string MachineName(int bit) => (bit < TmCount ? "MT" + (bit + 1).ToString("00") : "MO" + (bit - TmCount + 1).ToString("00"))
        + (bit < MachineMoves.Count && MachineMoves[bit] < MoveNames.Count ? " · " + MoveNames[MachineMoves[bit]] : "");
}

// Learnsets, evolutions and moves use the same profile key names in both generations (Gen5Profile and Gen4Profile constants).
public static partial class NdsCells
{
    private static IReadOnlyList<Location> LocateData(NdsDataSource s, NdsSection section)
    {
        var list = new List<Location>();
        string[] stats = ["PS", "Ataque", "Defensa", "Velocidad", "At. esp.", "Def. esp."];
        int typeMax = s.TypeNames.Count - 1, abilityMax = s.AbilityNames.Count - 1, itemMax = s.ItemNames.Count - 1, moveMax = s.MoveNames.Count - 1;
        string Name(int species) => species < s.SpeciesNames.Count ? s.SpeciesNames[species] : "#" + species;
        switch (section)
        {
            case NdsSection.Pokemon:
            {
                var personal = s.Copy(s.Personal).Files;
                for (int species = 1; species <= s.MaxSpecies && species < personal.Count; species++)
                {
                    byte[] p = personal[species];
                    Location L(string group, string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width, IReadOnlyList<string>? options = null) =>
                        new(new NdsCell(species, Name(species), group, field, local, value, min, max, kind) { Options = options }, Target.File, s.Personal, species, -1, offset, width);
                    for (int i = 0; i < 6; i++)
                        list.Add(L("Estadísticas base", stats[i], "Stat" + i, p[i], 1, 255, NdsValueKind.Number, i, 1));
                    list.Add(L("Tipos", "Tipo 1", "Type1", p[6], 0, typeMax, NdsValueKind.Type, 6, 1));
                    list.Add(L("Tipos", "Tipo 2", "Type2", p[7], 0, typeMax, NdsValueKind.Type, 7, 1));
                    for (int i = 0; i < s.Abilities.Length; i++)
                        list.Add(L("Habilidades", i == 2 ? "Habilidad oculta" : "Habilidad " + (i + 1), "Ability" + (i + 1), p[s.Abilities[i]], 0, abilityMax, NdsValueKind.Ability, s.Abilities[i], 1));
                    list.Add(L("Captura y crianza", "Ratio de captura", "CatchRate", p[s.CatchRate], 0, 255, NdsValueKind.Number, s.CatchRate, 1));
                    list.Add(L("Captura y crianza", "Crecimiento", "Growth", Math.Min((int)p[s.Growth], 5), 0, 5, NdsValueKind.Option, s.Growth, 1, NdsDataSource.GrowthNames));
                    list.Add(L("Captura y crianza", "Ratio de género", "Gender", p[s.GenderRatio], 0, 255, NdsValueKind.Number, s.GenderRatio, 1));
                    for (int i = 0; i < s.HeldItems.Length; i++)
                        list.Add(L("Objetos en estado salvaje", i == 2 ? "Objeto (hierba oscura)" : i == 0 ? "Objeto común" : "Objeto raro", "Item" + (i + 1),
                            BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(s.HeldItems[i])), 0, itemMax, NdsValueKind.Item, s.HeldItems[i], 2));
                }
                break;
            }
            case NdsSection.Moves:
            {
                var moves = s.Copy(Gen5Profile.Moves).Files;
                for (int move = 1; move <= moveMax && move < moves.Count; move++)
                {
                    byte[] m = moves[move];
                    Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, IReadOnlyList<string>? options = null) =>
                        new(new NdsCell(move, s.MoveNames[move], "Movimiento", field, local, value, min, max, kind) { Options = options }, Target.File, Gen5Profile.Moves, move, -1, offset, 1);
                    list.Add(L("Tipo", "Type", m[s.MoveType], 0, typeMax, NdsValueKind.Type, s.MoveType));
                    list.Add(L("Categoría", "Category", Math.Min((int)m[s.MoveCategory], 2), 0, 2, NdsValueKind.Option, s.MoveCategory, s.Categories));
                    list.Add(L("Poder", "Power", m[s.MovePower], 0, 255, NdsValueKind.Number, s.MovePower));
                    list.Add(L("Precisión", "Accuracy", m[s.MoveAccuracy], 0, 101, NdsValueKind.Number, s.MoveAccuracy));
                    list.Add(L("PP", "PP", m[s.MovePp], 1, 64, NdsValueKind.Number, s.MovePp));
                    list.Add(L("Prioridad", "Priority", (sbyte)m[s.MovePriority], -7, 7, NdsValueKind.Number, s.MovePriority));
                }
                break;
            }
            case NdsSection.Learnsets:
            {
                var sets = s.Copy(Gen5Profile.Learnsets).Files;
                for (int species = 1; species <= s.MaxSpecies && species < sets.Count; species++)
                {
                    byte[] l = sets[species];
                    for (int i = 0; ; i++)
                    {
                        Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, int width, int shift = 0, int bits = 0) =>
                            new(new NdsCell(species, Name(species), "Movimiento " + (i + 1), field, local, value, min, max, kind), Target.File, Gen5Profile.Learnsets, species, -1, offset, width, -1, shift, bits);
                        if (s.Generation == 5)
                        {
                            if (i * 4 + 4 > l.Length || BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(i * 4)) == 0xFFFF)
                                break;
                            list.Add(L("Movimiento", "M" + i + ".Move", BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(i * 4)), 1, moveMax, NdsValueKind.Move, i * 4, 2));
                            list.Add(L("Nivel", "M" + i + ".Level", BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(i * 4 + 2)), 1, 100, NdsValueKind.Number, i * 4 + 2, 2));
                        }
                        else
                        {
                            if (i * 2 + 2 > l.Length || BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(i * 2)) == 0xFFFF)
                                break;
                            int v = BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(i * 2));
                            list.Add(L("Movimiento", "M" + i + ".Move", v & 0x1FF, 1, Math.Min(moveMax, 0x1FF), NdsValueKind.Move, i * 2, 2, 0, 9));
                            list.Add(L("Nivel", "M" + i + ".Level", v >> 9, 1, 100, NdsValueKind.Number, i * 2, 2, 9, 7));
                        }
                    }
                }
                break;
            }
            case NdsSection.Evolutions:
            {
                var evolutions = s.Copy(Gen5Profile.Evolutions).Files;
                for (int species = 1; species <= s.MaxSpecies && species < evolutions.Count; species++)
                {
                    byte[] e = evolutions[species];
                    for (int i = 0; i < 7 && i * 6 + 6 <= e.Length; i++)
                    {
                        Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset, IReadOnlyList<string>? options = null) =>
                            new(new NdsCell(species, Name(species), "Evolución " + (i + 1), field, local, value, min, max, kind) { Options = options }, Target.File, Gen5Profile.Evolutions, species, -1, offset, 2);
                        list.Add(L("Método", "E" + i + ".Method", Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(i * 6)), s.EvolutionMethods.Count - 1), 0, s.EvolutionMethods.Count - 1, NdsValueKind.Option, i * 6, s.EvolutionMethods));
                        list.Add(L("Parámetro (nivel u objeto)", "E" + i + ".Param", BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(i * 6 + 2)), 0, 0xFFFF, NdsValueKind.Number, i * 6 + 2));
                        list.Add(L("Especie", "E" + i + ".Species", BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(i * 6 + 4)), 0, s.MaxSpecies, NdsValueKind.Species, i * 6 + 4));
                    }
                }
                break;
            }
            case NdsSection.Statics:
            {
                if (!NdsStaticsData.ByGame.TryGetValue(s.GameCode, out var statics))
                    break;
                var scripts = s.Copy(Gen5Profile.Scripts).Files;
                int U16(int file, int offset) => file < scripts.Count && offset + 2 <= scripts[file].Length ? BinaryPrimitives.ReadUInt16LittleEndian(scripts[file].AsSpan(offset)) : -1;
                for (int i = 0; i < statics.Count; i++)
                {
                    var st = statics[i];
                    // Every position must hold the same valid species, or the entry does not belong to this game's scripts.
                    var values = st.Species.Select(p => U16(p.File, p.Offset)).Distinct().ToArray();
                    if (values.Length != 1 || values[0] is < 1 || values[0] > s.MaxSpecies)
                        continue;
                    var first = st.Species[0];
                    list.Add(new(new NdsCell(i, st.Name, "Estático", "Especie", "Species", values[0], 1, s.MaxSpecies, NdsValueKind.Species),
                        Target.File, Gen5Profile.Scripts, first.File, -1, first.Offset, 2) { Copies = st.Species.Skip(1).ToArray() });
                    if (st.Level is { } at && at.File < scripts.Count && at.Offset < scripts[at.File].Length && scripts[at.File][at.Offset] is >= 1 and <= 100)
                        list.Add(new(new NdsCell(i, st.Name, "Estático", "Nivel", "Level", scripts[at.File][at.Offset], 1, 100, NdsValueKind.Number), Target.File, Gen5Profile.Scripts, at.File, -1, at.Offset, 1));
                }
                break;
            }
            case NdsSection.Trades:
            {
                var trades = s.Copy(Gen5Profile.Trades).Files;
                // Given species, requested species and held item (UPR-ZX getIngameTrades): 4-byte values.
                (int Given, int Item, int Requested) at = s.Generation == 5 ? (4, 0x4C, 0x5C) : (0, 0x3C, 0x4C);
                for (int i = 0; i < trades.Count; i++)
                {
                    byte[] t = trades[i];
                    if (at.Requested + 4 > t.Length)
                        continue;
                    int given = (int)BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(at.Given)), requested = (int)BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(at.Requested));
                    if (given is < 1 || given > s.MaxSpecies || requested < 0 || requested > s.MaxSpecies)
                        continue;
                    string name = "Intercambio " + (i + 1) + " · " + Name(given);
                    Location L(string field, string local, int value, int min, int max, NdsValueKind kind, int offset) =>
                        new(new NdsCell(i, name, "Intercambio", field, local, value, min, max, kind), Target.File, Gen5Profile.Trades, i, -1, offset, 4);
                    list.Add(L("Especie entregada", "Given", given, 1, s.MaxSpecies, NdsValueKind.Species, at.Given));
                    list.Add(L("Especie pedida", "Requested", requested, 0, s.MaxSpecies, NdsValueKind.Species, at.Requested));
                    list.Add(L("Objeto", "Item", (int)BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(at.Item)), 0, itemMax, NdsValueKind.Item, at.Item));
                }
                break;
            }
            case NdsSection.EggMoves:
            {
                var t = s.Tables;
                Location Egg(int species, int i, int value, Target target, int file, int overlay, int offset) =>
                    new(new NdsCell(species, Name(species), "Movimientos huevo", "Movimiento huevo " + (i + 1), "E" + i, value, 1, moveMax, NdsValueKind.Move),
                        target, Gen5Profile.EggMoves, file, overlay, offset, 2);
                if (t.EggNarc && t.EggNarcPerSpecies)
                {
                    var eggs = s.Copy(Gen5Profile.EggMoves).Files;
                    for (int species = 1; species <= s.MaxSpecies && species < eggs.Count; species++)
                    {
                        byte[] e = eggs[species];
                        int count = e.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(e) : 0;
                        for (int i = 0; i < count && 4 + i * 2 <= e.Length; i++)
                            list.Add(Egg(species, i, BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(2 + i * 2)), Target.File, species, -1, 2 + i * 2));
                    }
                }
                else if (t.EggNarc || t.EggOverlay >= 0)
                {
                    byte[] data = t.EggNarc ? s.Copy(Gen4Profile.EggMoves).Files[0] : s.Overlay(t.EggOverlay);
                    int start = t.EggNarc ? 0 : Gen4Data.FindUnique(data, EggPrefix, "la lista de movimientos huevo") + EggPrefix.Length;
                    // 20000 + species starts a species, its moves follow, 0xFFFF ends the list (UPR-ZX readEggMoves).
                    for (int at = start, species = 0, i = 0; at + 2 <= data.Length; at += 2)
                    {
                        int value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                        if (value == 0xFFFF)
                            break;
                        if (value > 20000)
                        {
                            (species, i) = (value - 20000, 0);
                            continue;
                        }
                        if (species >= 1 && species <= s.MaxSpecies)
                            list.Add(t.EggNarc ? Egg(species, i, value, Target.File, 0, -1, at) : Egg(species, i, value, Target.Overlay, -1, t.EggOverlay, at));
                        i++;
                    }
                }
                break;
            }
            case NdsSection.Tutors:
            {
                var t = s.Tables;
                if (t.TutorCount == 0)
                    break;
                var moves = s.TutorMoves();
                var personal = t.CompatInPersonal ? s.Copy(s.Personal).Files : null;
                byte[]? compat = t.CompatOverlay >= 0 ? s.Overlay(t.CompatOverlay) : t.CompatFile is { } file ? s.RawFile(file) : null;
                for (int species = 1; species <= s.MaxSpecies; species++)
                    for (int i = 0; i < t.TutorCount; i++)
                    {
                        (int offset, int bit) = personal is not null ? Bw2TutorBit(i) : (t.CompatOffset + (species - 1) * t.CompatBytes + i / 8, i % 8);
                        if (personal is not null)
                            offset += t.CompatOffset;
                        byte[]? data = personal is null ? compat : species < personal.Count ? personal[species] : null;
                        if (data is null || offset >= data.Length)
                            continue;
                        // One group, so the table shows a single row with every tutor as a column, like the TMs.
                        var cell = new NdsCell(species, Name(species), "Tutores", $"Tutor {i + 1:00}" + (moves[i] < s.MoveNames.Count ? " · " + s.MoveNames[moves[i]] : ""),
                            "T" + i, (data[offset] >> bit) & 1, 0, 1, NdsValueKind.Flag);
                        list.Add(personal is not null ? new(cell, Target.File, s.Personal, species, -1, offset, 1, -1, bit, 1)
                            : t.CompatOverlay >= 0 ? new(cell, Target.Overlay, "", -1, t.CompatOverlay, offset, 1, -1, bit, 1)
                            : new(cell, Target.Raw, t.CompatFile!, -1, -1, offset, 1, -1, bit, 1));
                    }
                break;
            }
            case NdsSection.MoveLists:
            {
                // HMs stay: the story needs their field moves.
                for (int i = 0; s.MachineTable >= 0 && i < s.TmCount && i < s.MachineMoves.Count; i++)
                    list.Add(new(new NdsCell(0, "Movimientos de las MT", "MT", $"Movimiento de la MT{i + 1:00}", "TM" + i, s.MachineMoves[i], 1, moveMax, NdsValueKind.Move),
                        Target.Arm9, "", -1, -1, s.MachineTable + i * 2, 2));
                var tutors = s.TutorMoves();
                for (int i = 0; i < tutors.Count; i++)
                    list.Add(new(new NdsCell(1, "Movimientos de los tutores", "Tutores", $"Movimiento del tutor {i + 1:00}", "Tutor" + i, tutors[i], 1, moveMax, NdsValueKind.Move),
                        Target.Overlay, "", -1, s.Tables.TutorOverlay, s.Tables.TutorOffset + i * s.Tables.TutorStride, 2));
                break;
            }
            case NdsSection.Machines:
            {
                var personal = s.Copy(s.Personal).Files;
                int bits = s.TmCount + s.HmCount;
                for (int species = 1; species <= s.MaxSpecies && species < personal.Count; species++)
                {
                    byte[] p = personal[species];
                    for (int bit = 0; bit < bits && s.MachineBits + bit / 8 < p.Length; bit++)
                        // One group, so the table shows a single row with every TM and HM as a column.
                        list.Add(new(new NdsCell(species, Name(species), "MT y MO", s.MachineName(bit), "B" + bit, (p[s.MachineBits + bit / 8] >> (bit % 8)) & 1, 0, 1, NdsValueKind.Flag),
                            Target.File, s.Personal, species, -1, s.MachineBits + bit / 8, 1, -1, bit % 8, 1));
                }
                break;
            }
        }
        return list;
    }

    // Diamond/Platinum egg moves start right after these bytes in the field overlay (UPR-ZX dpptEggMoveTablePrefix).
    private static readonly byte[] EggPrefix = [0x40, 0x01, 0x66, 0x01];

    // Black 2/White 2 keep the tutor flags in four 32-bit blocks whose order is not the tutor list's (UPR-ZX getMoveTutorCompatibility).
    private static (int Offset, int Bit) Bw2TutorBit(int tutor)
    {
        int[] personalCounts = [15, 17, 13, 15], moveCounts = [13, 15, 15, 17], personalToMove = [1, 3, 0, 2];
        for (int block = 0; block < 4; block++)
        {
            int first = moveCounts.Take(personalToMove[block]).Sum();
            if (tutor >= first && tutor < first + personalCounts[block])
                return (block * 4 + (tutor - first) / 8, (tutor - first) % 8);
        }
        throw new ArgumentOutOfRangeException(nameof(tutor));
    }
}
