#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Gen4/Gen5 species, move, learnset, evolution and TM/HM tables seen the same way: where each one lives and the byte layout
/// of the generation (UPR-ZX Gen4Constants/Gen5Constants and the owner's dumps). Built from a game view, draft views included.
/// </summary>
public sealed class NdsDataSource
{
    public int Generation { get; }
    public int MaxSpecies { get; }
    public IReadOnlyList<string> SpeciesNames { get; }
    public IReadOnlyList<string> MoveNames { get; }
    public IReadOnlyList<string> TypeNames { get; }
    public IReadOnlyList<string> AbilityNames { get; }
    public IReadOnlyList<string> ItemNames { get; }
    /// <summary>Move taught by each TM/HM, in the order of the compatibility bits (TMs, then HMs); empty when the ARM9 list is not found.</summary>
    public IReadOnlyList<int> MachineMoves { get; }
    internal Func<string, Narc> Copy { get; }
    internal Func<string, string> PathOf { get; }
    internal string Personal { get; }

    private NdsDataSource(int generation, int maxSpecies, Func<string, Narc> copy, Func<string, string> pathOf, Func<int, IReadOnlyList<string>> text,
        int speciesText, int moveText, int typeText, int abilityText, int itemText, string personal, Func<byte[]?> arm9)
    {
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
        MachineMoves = ReadMachines(arm9);
    }

    public static NdsDataSource Of(Gen5Data data) => new(5, Gen5Randomizer.MaxSpecies, data.CopyNarc, data.PathOf, data.Text, data.Profile.SpeciesNamesText,
        data.Profile.MoveNamesText, data.Profile.TypeNamesText, data.Profile.AbilityNamesText, data.Profile.ItemNamesText, Gen5Profile.PokemonStats, () => Try(data.ReadArm9));

    public static NdsDataSource Of(Gen4Data data) => new(4, Gen4Data.MaxSpecies, data.CopyNarc, data.PathOf, data.Text, data.Profile.SpeciesNamesText,
        data.Profile.MoveNamesText, data.Profile.TypeNamesText, data.Profile.AbilityNamesText, data.Profile.ItemNamesText, Gen4Profile.PokemonStats,
        () => data.Unavailable.ContainsKey(Gen5Feature.Shops) ? null : Try(data.Arm9));

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

    private IReadOnlyList<int> ReadMachines(Func<byte[]?> arm9)
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
        var list = Enumerable.Range(0, total).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(at + i * 2))).ToArray();
        // Gen5 stores TM01-92, HM01-06, TM93-95; the compatibility bits are TM01-95 then HM01-06.
        return G5 ? [.. list[..92], .. list[98..101], .. list[92..98]] : list;
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
}
