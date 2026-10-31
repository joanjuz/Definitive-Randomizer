#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

public enum NdsTrainerMoveMode { Keep, LevelUp, Better, Random, Metronome }
public enum NdsTrainerItemMode { Keep, Random, Smart, None }

/// <summary>Trainer team rules of the 3DS randomizer and UPR-ZX, applied on top of the species randomizer.</summary>
public sealed record NdsTrainerOptions(uint Seed)
{
    /// <summary>Teams smaller than this get new Pokémon of similar strength (0 = keep).</summary>
    public int MinTeamSize { get; init; }
    public NdsTrainerMoveMode Moves { get; init; }
    public NdsTrainerItemMode Items { get; init; }
    /// <summary>Every trainer Pokémon gets the highest difficulty value (all IVs 31).</summary>
    public bool MaxIvs { get; init; }
    /// <summary>The basic, attack-evaluation and expert AI flags are turned on.</summary>
    public bool MaxAi { get; init; }
    /// <summary>Percentage of single battles turned into double battles (UPR-ZX double battle mode at 100).</summary>
    public int DoubleChance { get; init; }
    public IReadOnlySet<int>? Scope { get; init; }
    internal bool InScope(int id) => Scope is null || Scope.Contains(id);
    public bool ChangesAnything => MinTeamSize > 0 || Moves != NdsTrainerMoveMode.Keep || Items != NdsTrainerItemMode.Keep || MaxIvs || MaxAi || DoubleChance > 0;
}

/// <summary>
/// Rewrites trainer headers and teams (as UPR-ZX setTrainers does): team size, custom moves and held items (adding the
/// "has moves/items" formats when needed), difficulty, AI and double battles. Double battles also patch code as UPR-ZX does:
/// the Gen4 ARM9 and battle sub-sequence, or the Gen5 field overlay and trainer text boxes; only on the original binaries.
/// Gen5 cannot get UPR-ZX's ARM9 text-box fix (DSi ARM9), so a doubles trainer that spots the player may show a blank box.
/// </summary>
public static class NdsTrainerTools
{
    // Gen4/Gen5 held items, the same ids in both generations (checked against the names of the five supported dumps).
    private const int SitrusBerry = 158, LumBerry = 157, Leftovers = 234, LifeOrb = 270, FocusSash = 275, ExpertBelt = 268, ChoiceBand = 220,
        ChoiceScarf = 287, ChoiceSpecs = 297, MuscleBand = 266, WiseGlasses = 267, BlackSludge = 281;
    public static IReadOnlyList<int> HeldItemIds => HeldItems;
    private static readonly int[] HeldItems = [149, 155, 157, 158, 213, 217, 220, 221, 222, 232, 233, 234, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 253, 266, 267, 268, 270, 275, 281, 287, 297];
    // Type-boosting items in the Gen5 type order (Normal … Dark); Gen4 has "???" at 9, so its later types shift by one.
    private static readonly int[] TypeBoosters = [251, 241, 244, 245, 237, 238, 222, 247, 233, 249, 243, 239, 242, 248, 246, 250, 240];
    private const int Struggle = 165, Metronome = 118, PoisonType = 3;
    // Diamond/Pearl and Platinum partner trainers: as doubles they turn double wild battles into broken trainer battles (UPR-ZX).
    private static readonly HashSet<int> Gen4PartnerTrainers = [608, 609, 610, 611, 612];
    // Trainers whose strongest Pokémon is below this level are early scripted battles (UPR-ZX skips the first rival battles).
    private const int DoublesMinLevel = 8;

    public static string? DoublesProblem(Gen5Data data) => data.Unavailable.TryGetValue(Gen5Feature.Doubles, out string? reason) ? reason : null;
    public static string? DoublesProblem(Gen4Data data) => data.Unavailable.TryGetValue(Gen5Feature.Doubles, out string? reason) ? reason
        : data.Unavailable.ContainsKey(Gen5Feature.Shops) ? "el romhack modifica el ARM9, donde se parchean los combates dobles" : null;

    private sealed record Layout(bool Gen5, int BaseSize, int Trailing, int BattleOffset);

    private sealed class Member
    {
        public required byte[] Base;
        public int Item;
        public int[] Moves = [0, 0, 0, 0];
        public byte[] Trailing = [];
        public bool Gen5;
        public int Level { get => BinaryPrimitives.ReadUInt16LittleEndian(Base.AsSpan(2)); }
        public int Species
        {
            get => Gen5 ? BinaryPrimitives.ReadUInt16LittleEndian(Base.AsSpan(4)) : BinaryPrimitives.ReadUInt16LittleEndian(Base.AsSpan(4)) & 0x3FF;
            set
            {
                BinaryPrimitives.WriteUInt16LittleEndian(Base.AsSpan(4), (ushort)value);
                if (Gen5)
                    BinaryPrimitives.WriteUInt16LittleEndian(Base.AsSpan(6), 0);
            }
        }
        public Member Copy() => new() { Base = (byte[])Base.Clone(), Item = Item, Moves = (int[])Moves.Clone(), Trailing = (byte[])Trailing.Clone(), Gen5 = Gen5 };
    }

    public static Gen5RandomizerResult Generate(Gen5Data data, NdsTrainerOptions options)
    {
        if (options.DoubleChance > 0 && DoublesProblem(data) is { } problem)
            throw new ArgumentException("Combates dobles no disponibles: " + problem + ".", nameof(options));
        var headers = data.CopyNarc(Gen5Profile.TrainerData);
        var teams = data.CopyNarc(Gen5Profile.TrainerPokemon);
        var changes = new List<Gen5Change>();
        var converted = Run(new Layout(true, 8, 0, 2), data.Trainers(), headers, teams, Tables.Of(NdsDataSource.Of(data), s => NdsCells.Read(data, s)), options, gen4Partners: false, changes);
        var files = new Dictionary<string, byte[]> { [data.PathOf(Gen5Profile.TrainerData)] = headers.Write(), [data.PathOf(Gen5Profile.TrainerPokemon)] = teams.Write() };
        var overlays = new Dictionary<int, byte[]>();
        if (converted > 0 && PatchGen5Doubles(data, files, overlays))
            changes.Add(new Gen5Change("Combates dobles", "Código del overlay de campo y cajas de texto", 0, 1));
        return new Gen5RandomizerResult(files, changes, overlays) { RecompressOverlays = overlays.Keys.ToHashSet() };
    }

    public static Gen5RandomizerResult Generate(Gen4Data data, NdsTrainerOptions options)
    {
        if (options.DoubleChance > 0 && DoublesProblem(data) is { } problem)
            throw new ArgumentException("Combates dobles no disponibles: " + problem + ".", nameof(options));
        var headers = data.CopyNarc(Gen4Profile.TrainerData);
        var teams = data.CopyNarc(Gen4Profile.TrainerPokemon);
        var changes = new List<Gen5Change>();
        bool dp = data.Profile.Kind == Gen4Kind.DiamondPearl;
        var converted = Run(new Layout(false, 6, dp ? 0 : 2, 16), data.Trainers(), headers, teams, Tables.Of(NdsDataSource.Of(data), s => NdsCells.Read(data, s)), options,
            gen4Partners: data.Profile.Kind != Gen4Kind.HeartGoldSoulSilver, changes);
        var files = new Dictionary<string, byte[]> { [data.PathOf(Gen4Profile.TrainerData)] = headers.Write(), [data.PathOf(Gen4Profile.TrainerPokemon)] = teams.Write() };
        byte[]? arm9 = null;
        if (converted > 0 && PatchGen4Doubles(data, files, out arm9))
            changes.Add(new Gen5Change("Combates dobles", "Código del ARM9 y secuencia de fin de combate", 0, 1));
        return new Gen5RandomizerResult(files, changes, new Dictionary<int, byte[]>()) { Arm9 = arm9 };
    }

    private sealed record Tables(NdsDataSource Source, IReadOnlyDictionary<int, Dictionary<string, int>> Pokemon, IReadOnlyDictionary<int, Dictionary<string, int>> Learnsets,
        IReadOnlyDictionary<int, Dictionary<string, int>> Moves, IReadOnlyDictionary<int, Dictionary<string, int>> Machines)
    {
        public static Tables Of(NdsDataSource source, Func<NdsSection, IReadOnlyList<NdsCell>> read)
        {
            IReadOnlyDictionary<int, Dictionary<string, int>> T(NdsSection s) => read(s).GroupBy(c => c.Entry).ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Local, c => c.Value));
            return new(source, T(NdsSection.Pokemon), T(NdsSection.Learnsets), T(NdsSection.Moves), T(NdsSection.Machines));
        }
        public int Bst(int species) => Pokemon.TryGetValue(species, out var p) ? Enumerable.Range(0, 6).Sum(i => p["Stat" + i]) : 0;
        public int[] Types(int species) => Pokemon.TryGetValue(species, out var p) ? [p["Type1"], p["Type2"]] : [];
        // Gen5 type order; Gen4's "???" (9) is skipped.
        public int Canonical(int type) => Source.Generation == 4 && type >= 10 ? type - 1 : type;
        public bool IsStatus(int move) => Moves.TryGetValue(move, out var m) && Source.Categories[m["Category"]] == "Estado";
    }

    private static int Run(Layout layout, IReadOnlyList<Gen5Trainer> trainers, Narc headers, Narc teams, Tables tables, NdsTrainerOptions options, bool gen4Partners, List<Gen5Change> changes)
    {
        int Pick(string key, int count) => Gen5Randomizer.Index(options.Seed, key, count);
        int converted = 0;
        var all = tables.Pokemon.Keys.Where(s => s >= 1 && s <= tables.Source.MaxSpecies).Order().ToArray();
        foreach (var trainer in trainers)
        {
            if (!options.InScope(trainer.Id))
                continue;
            byte[] header = headers.Files[trainer.Id];
            string name = $"{trainer.ClassName} {trainer.Name}".Trim();
            int flags = header[0], count = header[3];
            if (count == 0)
                continue;
            int size = layout.BaseSize + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0) + layout.Trailing;
            byte[] raw = teams.Files[trainer.Id];
            var team = new List<Member>();
            for (int slot = 0; slot < count; slot++)
            {
                int at = slot * size, rest = at + layout.BaseSize;
                var member = new Member { Base = raw.AsSpan(at, layout.BaseSize).ToArray(), Gen5 = layout.Gen5 };
                if ((flags & 2) != 0) { member.Item = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(rest)); rest += 2; }
                if ((flags & 1) != 0) { member.Moves = Enumerable.Range(0, 4).Select(m => (int)BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(rest + m * 2))).ToArray(); rest += 8; }
                member.Trailing = raw.AsSpan(rest, layout.Trailing).ToArray();
                team.Add(member);
            }
            int newFlags = flags;
            void Note(string field, int before, int after) { if (before != after) changes.Add(new Gen5Change(name, field, before, after)); }

            // Team size: new members copy a random teammate's level with a species of similar strength.
            while (team.Count < Math.Min(6, options.MinTeamSize))
            {
                var model = team[Pick($"tsize/{trainer.Id}/{team.Count}", team.Count)];
                var added = model.Copy();
                int bst = tables.Bst(model.Species);
                var similar = all.Where(s => Math.Abs(tables.Bst(s) - bst) <= Math.Max(40, bst / 10)).ToArray();
                added.Species = similar.Length > 0 ? similar[Pick($"tsize/{trainer.Id}/{team.Count}/species", similar.Length)] : model.Species;
                // Kept moves were the teammate's: the new species gets its own level-up moves.
                if ((flags & 1) != 0)
                    added.Moves = LevelUpMoves(tables, added.Species, added.Level);
                team.Add(added);
                changes.Add(new Gen5Change(name + " · Pokémon " + team.Count, "Especie", 0, added.Species));
            }

            // Double battles (UPR-ZX doubleBattleMode): single battles only, at least two Pokémon.
            int battle = header[layout.BattleOffset];
            if (options.DoubleChance > 0 && battle == 0 && !(gen4Partners && Gen4PartnerTrainers.Contains(trainer.Id))
                && team.Max(m => m.Level) >= DoublesMinLevel && Pick($"double/{trainer.Id}", 100) < options.DoubleChance)
            {
                if (team.Count == 1)
                {
                    team.Add(team[0].Copy());
                    changes.Add(new Gen5Change(name + " · Pokémon 2", "Especie", 0, team[1].Species));
                }
                if (layout.Gen5)
                {
                    header[2] = 1;
                    header[12] |= 0x80; // the trainer does not attack its own Pokémon
                }
                else
                    header[16] |= 3;
                Note("Combate doble", 0, 1);
                converted++;
            }

            if (options.Moves != NdsTrainerMoveMode.Keep)
            {
                newFlags |= 1;
                for (int i = 0; i < team.Count; i++)
                {
                    var m = team[i];
                    int[] before = m.Moves;
                    m.Moves = options.Moves switch
                    {
                        NdsTrainerMoveMode.LevelUp => LevelUpMoves(tables, m.Species, m.Level),
                        NdsTrainerMoveMode.Better => BetterMoves(tables, m.Species, m.Level),
                        NdsTrainerMoveMode.Metronome => [Metronome, 0, 0, 0],
                        _ => RandomMoves(tables, key => Pick($"tmove/{trainer.Id}/{i}/{key}", tables.Source.MoveNames.Count)),
                    };
                    for (int k = 0; k < 4; k++)
                        Note($"Pokémon {i + 1} · Movimiento {k + 1}", (flags & 1) != 0 && i < count ? before[k] : 0, m.Moves[k]);
                }
            }
            if (options.Items == NdsTrainerItemMode.None)
            {
                newFlags &= ~2;
                for (int i = 0; i < team.Count; i++)
                    Note($"Pokémon {i + 1} · Objeto equipado", team[i].Item, 0);
            }
            else if (options.Items != NdsTrainerItemMode.Keep)
            {
                newFlags |= 2;
                for (int i = 0; i < team.Count; i++)
                {
                    int before = (flags & 2) != 0 && i < count ? team[i].Item : 0;
                    team[i].Item = options.Items == NdsTrainerItemMode.Random
                        ? HeldItems[Pick($"titem/{trainer.Id}/{i}", HeldItems.Length)]
                        : SmartItem(tables, team[i].Species, key => Pick($"titem/{trainer.Id}/{i}/{key}", 1000));
                    Note($"Pokémon {i + 1} · Objeto equipado", before, team[i].Item);
                }
            }
            if (options.MaxIvs)
                for (int i = 0; i < team.Count; i++)
                {
                    Note($"Pokémon {i + 1} · Dificultad (IV)", team[i].Base[0], 255);
                    team[i].Base[0] = 255;
                }
            if (options.MaxAi)
            {
                Note("IA", header[12] & 7, 7);
                header[12] |= 7;
            }

            header[0] = (byte)newFlags;
            Note("Pokémon en el equipo", count, team.Count);
            header[3] = (byte)team.Count;
            int newSize = layout.BaseSize + ((newFlags & 2) != 0 ? 2 : 0) + ((newFlags & 1) != 0 ? 8 : 0) + layout.Trailing;
            var output = new byte[newSize * team.Count];
            for (int slot = 0; slot < team.Count; slot++)
            {
                var m = team[slot];
                int at = slot * newSize;
                m.Base.CopyTo(output, at);
                at += layout.BaseSize;
                if ((newFlags & 2) != 0) { BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at), (ushort)m.Item); at += 2; }
                if ((newFlags & 1) != 0)
                {
                    for (int k = 0; k < 4; k++)
                        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + k * 2), (ushort)m.Moves[k]);
                    at += 8;
                }
                m.Trailing.CopyTo(output, at);
            }
            teams.Files[trainer.Id] = output;
        }
        return converted;
    }

    // UPR-ZX getMovesAtLevel: the last four different moves learned up to the level.
    private static int[] LevelUpMoves(Tables tables, int species, int level)
    {
        var learned = new List<int>();
        if (tables.Learnsets.TryGetValue(species, out var row))
            for (int i = 0; row.TryGetValue("M" + i + ".Move", out int move); i++)
                if (row["M" + i + ".Level"] <= level && move > 0)
                {
                    learned.Remove(move);
                    learned.Add(move);
                }
        var last = learned.TakeLast(4).ToList();
        while (last.Count < 4)
            last.Add(0);
        return [.. last];
    }

    // Strongest damaging moves the Pokémon can know at its level (level-up and TM/HM), at most two of a type, plus a status move to fill.
    private static int[] BetterMoves(Tables tables, int species, int level)
    {
        var pool = new HashSet<int>();
        if (tables.Machines.TryGetValue(species, out var bits))
            for (int bit = 0; bit < tables.Source.MachineMoves.Count; bit++)
                if (bits.TryGetValue("B" + bit, out int on) && on == 1)
                    pool.Add(tables.Source.MachineMoves[bit]);
        AllLearnable(tables, species, level, pool);
        var types = tables.Types(species);
        bool physical = tables.Pokemon.TryGetValue(species, out var p) && p["Stat1"] >= p["Stat4"];
        double Score(int move)
        {
            var m = tables.Moves[move];
            bool movePhysical = tables.Source.Categories[m["Category"]] == "Físico";
            double accuracy = m["Accuracy"] is 0 or > 100 ? 100 : m["Accuracy"];
            return m["Power"] * accuracy / 100 * (types.Contains(m["Type"]) ? 1.5 : 1) * (movePhysical == physical ? 1.2 : 1);
        }
        var damaging = pool.Where(m => m != Struggle && tables.Moves.ContainsKey(m) && !tables.IsStatus(m) && tables.Moves[m]["Power"] > 1).OrderByDescending(Score).ThenBy(m => m).ToList();
        var chosen = new List<int>();
        foreach (int move in damaging)
            if (chosen.Count < 3 && chosen.Count(c => tables.Moves[c]["Type"] == tables.Moves[move]["Type"]) < 2)
                chosen.Add(move);
        // The last slot: the latest status move learned, otherwise the next damaging move.
        int status = LevelUpMoves(tables, species, level).LastOrDefault(m => m > 0 && tables.IsStatus(m) && !chosen.Contains(m));
        if (status > 0)
            chosen.Add(status);
        foreach (int move in damaging.Concat(LevelUpMoves(tables, species, level)).Where(m => m > 0 && !chosen.Contains(m)))
            if (chosen.Count < 4)
                chosen.Add(move);
        while (chosen.Count < 4)
            chosen.Add(0);
        return [.. chosen.Take(4)];
    }

    private static void AllLearnable(Tables tables, int species, int level, HashSet<int> pool)
    {
        if (!tables.Learnsets.TryGetValue(species, out var row))
            return;
        for (int i = 0; row.TryGetValue("M" + i + ".Move", out int move); i++)
            if (row["M" + i + ".Level"] <= level && move > 0)
                pool.Add(move);
    }

    private static int[] RandomMoves(Tables tables, Func<int, int> pick)
    {
        var chosen = new List<int>();
        for (int tries = 0; chosen.Count < 4 && tries < 64; tries++)
        {
            int move = pick(tries);
            if (move is > 0 and not Struggle && tables.Moves.ContainsKey(move) && !chosen.Contains(move))
                chosen.Add(move);
        }
        while (chosen.Count < 4)
            chosen.Add(0);
        return [.. chosen];
    }

    // 3DS "smart items": what suits the Pokémon (its type booster, Black Sludge for Poison, choice items by its best attacking stat) or a general item.
    private static int SmartItem(Tables tables, int species, Func<string, int> roll)
    {
        var types = tables.Types(species);
        if (types.Length == 0)
            return Leftovers;
        if (types.Contains(PoisonType) && roll("sludge") < 500)
            return BlackSludge;
        if (roll("booster") < 350)
        {
            int type = tables.Canonical(types[roll("type") % 2 == 0 || types[0] == types[1] ? 0 : 1]);
            if (type >= 0 && type < TypeBoosters.Length)
                return TypeBoosters[type];
        }
        bool physical = tables.Pokemon[species]["Stat1"] >= tables.Pokemon[species]["Stat4"];
        int[] general = [Leftovers, SitrusBerry, LumBerry, LifeOrb, FocusSash, ExpertBelt, ChoiceScarf, physical ? ChoiceBand : ChoiceSpecs, physical ? MuscleBand : WiseGlasses];
        return general[roll("general") % general.Length];
    }

    // ---- Double battle code patches (UPR-ZX setTrainers, GPLv3) ----

    // Gen4: patched once; a later doubles operation finds the ARM9 already patched against the original and leaves it.
    private static bool PatchGen4Doubles(Gen4Data data, Dictionary<string, byte[]> files, out byte[]? arm9)
    {
        arm9 = null;
        var kind = data.Profile.Kind;
        string fix = kind switch { Gen4Kind.DiamondPearl => "022912D90221214201", Gen4Kind.Platinum => "022919D90221214205", _ => "2C2815D00221214201" };
        string[] prefixes = kind switch
        {
            Gen4Kind.DiamondPearl => ["08B5092131F020FB", "16B00020F8BD1498", "36FB16B00120F8BD", "F7F71AFEF7F734FE"],
            Gen4Kind.Platinum => ["08B5092139F0C6FF", "16B00020F8BD1498", "C4FE16B00120F8BD", "F6F792FCF6F7ACFC"],
            _ => ["08B5092132F0B4FF", "16B00020F8BD1498", "9CFE16B00120F8BD", "F6F794FEF6F7AEFE"],
        };
        byte[] code = data.Arm9();
        int At(string hex) => Find(code, hex) is var i and >= 0 ? i + hex.Length / 2 : -1;
        int fixAt = At(fix);
        if (fixAt < 0)
        {
            if (Find(data.OriginalArm9(), fix) >= 0)
                return false; // already patched by an earlier operation
            throw new InvalidDataException("No se encontró en el ARM9 el código de combates dobles de UPR-ZX.");
        }
        int[] at = prefixes.Select(At).ToArray();
        if (at.Any(i => i < 0))
            throw new InvalidDataException("No se encontró en el ARM9 el código de combates dobles de UPR-ZX.");
        code[fixAt] = 0xE0;
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(at[0]), 0xBD08);
        code[at[1]] = 2;
        code[at[1] + 3] = 0xD0;
        code[at[2]] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(at[3]), 0x46C0);
        BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(at[3] + 2), 0x2802);
        code[at[3] + 5] = 0xD0;
        arm9 = code;
        // The end-of-battle text box checks "flag == 2" instead of "flag & 2", so single trainers in doubles (flag 3) end as singles.
        var subSeq = data.CopyNarc(Gen4Profile.BattleSkillSubSeq);
        if (subSeq.Files[4][0xD8] == 4)
            subSeq.Files[4][0xD8] = 0;
        files[data.PathOf(Gen4Profile.BattleSkillSubSeq)] = subSeq.Write();
        return true;
    }

    private static bool PatchGen5Doubles(Gen5Data data, Dictionary<string, byte[]> files, Dictionary<int, byte[]> overlays)
    {
        string limit = data.Profile.Sequel ? "0224E3E7012817D1" : "321C26E0012E17D1";
        const string textBox = "0004000C03D10320", pointer = "0A9906980904090C";
        int beq = data.Profile.Sequel ? 0xD0C7 : 0xD0D1;
        byte[] overlay = data.FieldOverlay();
        int box = Find(overlay, textBox);
        if (box < 0)
        {
            if (Find(data.OriginalFieldOverlay(), textBox) >= 0)
                return false; // already patched by an earlier operation
            throw new InvalidDataException("No se encontró en el overlay de campo el código de combates dobles de UPR-ZX.");
        }
        int limitAt = Find(overlay, limit), pointerAt = Find(overlay, pointer);
        if (limitAt < 0 || pointerAt < 0)
            throw new InvalidDataException("No se encontró en el overlay de campo el código de combates dobles de UPR-ZX.");
        // Text boxes of the first trainer of a pair use the single-trainer values.
        int o = box + 8;
        overlay[o - 2] = 0;
        overlay[o] = 2;
        overlay[o + 2] = 0x18;
        // No two-Pokémon limit for doubles trainers spotting the player.
        o = limitAt + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o), 0x46C0);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 2), 0x46C0);
        // A null partner pointer goes to the single-trainer case.
        o = pointerAt + 8;
        ushort U(int at) => BinaryPrimitives.ReadUInt16LittleEndian(overlay.AsSpan(at));
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 0x10), U(o + 0xE));
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 0xE), U(o + 0xC));
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 0xC), U(o + 0xA));
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 0xA), (ushort)beq);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(o + 8), 0x2800);
        overlays[data.Profile.FieldOverlay] = overlay;
        // Original doubles trainers keep single text boxes (3 → 0, 5 → 2, 6 → 0x18).
        var originalDoubles = data.OriginalTrainers().Where(t => t.BattleType == 1).Select(t => t.Id).ToHashSet();
        var boxes = data.CopyNarc(Gen5Profile.TrainerTextBoxes);
        byte[] table = boxes.Files[0];
        for (int i = 0; i + 4 <= table.Length; i += 4)
        {
            if (!originalDoubles.Contains(BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(i))))
                continue;
            int value = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(i + 2));
            int replaced = value switch { 3 => 0, 5 => 2, 6 => 0x18, _ => value };
            BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(i + 2), (ushort)replaced);
        }
        files[data.PathOf(Gen5Profile.TrainerTextBoxes)] = boxes.Write();
        return true;
    }

    private static int Find(byte[] data, string hex)
    {
        byte[] needle = Convert.FromHexString(hex);
        int found = -1;
        for (int i = data.AsSpan().IndexOf(needle); i >= 0; )
        {
            if (found >= 0)
                return -1; // not unique
            found = i;
            int next = data.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return found;
    }
}
