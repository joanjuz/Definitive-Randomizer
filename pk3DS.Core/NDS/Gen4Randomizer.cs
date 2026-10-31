#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Diamond, Platinum and HeartGold randomizer for trainers, wild Pokémon, starters, field items and shops, with the same modes,
/// seed handling and change log as Gen5.
/// </summary>
public static class Gen4Randomizer
{
    public static Gen5RandomizerResult Generate(Gen4Data data, Gen5RandomizerOptions options)
    {
        Gen5Randomizer.RejectUnavailable(data.Unavailable, options);
        var pick = Gen5Randomizer.SpeciesPicker(data.BaseStats(), Gen4Data.MaxSpecies, options);
        var files = new Dictionary<string, byte[]>();
        var changes = new List<Gen5Change>();
        if (options.Trainers != Gen5SpeciesMode.Keep)
            RandomizeTrainers(data, options, pick, files, changes);
        if (options.Wild != Gen5SpeciesMode.Keep)
            RandomizeWild(data, options, pick, files, changes);
        byte[]? arm9 = null;
        var overlays = new Dictionary<int, byte[]>();
        var scripts = new Lazy<Narc>(() => data.CopyNarc(Gen4Profile.Scripts));
        if (options.Starters != Gen5StarterMode.Keep)
            SetStarters(data, options, scripts.Value, ref arm9, overlays, files, changes);
        if (options.FieldItems == Gen5ItemMode.Shuffle)
            ShuffleFieldItems(data, options.Seed, scripts.Value, ref arm9, changes);
        if (options.Shops == Gen5ItemMode.Shuffle)
            ShuffleShops(data, options.Seed, ref arm9, changes);
        // Starters and item balls share the scripts table: one working copy, written once.
        if (scripts.IsValueCreated)
            files[data.PathOf(Gen4Profile.Scripts)] = scripts.Value.Write();
        return new Gen5RandomizerResult(files, changes, overlays)
        {
            Arm9 = arm9,
            // HeartGold's cry overlay is stored compressed and does not fit uncompressed; Diamond/Platinum's is not compressed.
            RecompressOverlays = overlays.Keys.ToHashSet(),
        };
    }

    // Rival battle choice in Diamond/Platinum scripts: the two compared species follow at +0x08 and +0x15 (UPR-ZX dpptRivalScriptMagic).
    private static readonly byte[] DpptRivalMagic = [0xDE, 0x00, 0x0C, 0x80, 0x11, 0x00, 0x0C, 0x80, 0x83, 0x01, 0x1C, 0x00, 0x01];
    private static readonly byte[] DpptTagMagic = [0xDE, 0x00, 0x0C, 0x80, 0x28, 0x00, 0x04, 0x80];
    private static readonly byte[] DpptTagMagic2 = [0x11, 0x00, 0x0C, 0x80, 0x86, 0x01, 0x1C, 0x00, 0x01];
    private static readonly byte[] HgssRivalMagic = [0xCE, 0x00, 0x0C, 0x80, 0x11, 0x00, 0x0C, 0x80, 152, 0, 0x1C, 0x00, 0x05];
    private static readonly byte[] HgssCriesPrefix = Convert.FromHexString("0004000C10BD0000000000000000000000E000000000000000E0000000000200");
    private static readonly byte[] DpptGraphicsPrefix = Convert.FromHexString("000222402104120C"), DpptGraphicsInner = Convert.FromHexString("0290039002200002");

    /// <summary>
    /// Starters everywhere the game relies on them (UPR-ZX Gen4RomHandler.setStarters, GPLv3): the starter table, the
    /// Diamond/Platinum selection-screen graphics routine, rival and tag-battle scripts that branch on the starter, HeartGold's
    /// cries, and the selection text. Every pattern must be found exactly as expected, or nothing is returned.
    /// </summary>
    private static void SetStarters(Gen4Data data, Gen5RandomizerOptions options, Narc scripts, ref byte[]? arm9,
        Dictionary<int, byte[]> overlays, Dictionary<string, byte[]> files, List<Gen5Change> changes)
    {
        var profile = data.Profile;
        int[] chosen = options.Starters == Gen5StarterMode.Custom
            ? (options.CustomStarters ?? throw new ArgumentException("Faltan los iniciales personalizados.", nameof(options))).ToArray()
            : PickStarters(options.Seed);
        if (chosen.Length != 3 || chosen.Distinct().Count() != 3 || chosen.Any(s => s is < 1 or > Gen4Data.MaxSpecies))
            throw new ArgumentException("Se necesitan tres iniciales distintos entre 1 y 493.", nameof(options));
        var original = data.Starters();
        bool hgss = profile.Kind == Gen4Kind.HeartGoldSoulSilver;

        byte[] overlay = data.StarterOverlay();
        byte[] table = hgss ? (arm9 ??= data.Arm9()) : overlay;
        int at = data.StarterTable(table);
        for (int i = 0; i < 3; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(at + i * 4), (ushort)chosen[i]);

        if (hgss)
        {
            int cries = Gen4Data.FindUnique(overlay, HgssCriesPrefix, "la tabla de gritos de los iniciales") + HgssCriesPrefix.Length;
            for (int i = 0; i < 3; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(cries + i * 4)) != original[i])
                    throw new InvalidDataException("La tabla de gritos de los iniciales no contiene los iniciales originales.");
                BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(cries + i * 4), (uint)chosen[i]);
            }
        }
        else
            PatchSelectionGraphics(overlay, chosen);
        overlays[profile.StarterOverlay] = overlay;

        int rivals = 0, tags = 0;
        foreach (int file in profile.RivalScripts)
            rivals += hgss ? PatchHgssRival(scripts.Files[file], chosen, original) : PatchDpptRival(scripts.Files[file], chosen, original, profile.Kind);
        foreach (int file in profile.TagScripts)
            tags += PatchTagBattle(scripts.Files[file], chosen, original);
        // Every branch found compares the original starters; a romhack may add branches (Renegade Platinum has one more rival battle).
        bool expected = data.IsRomhack ? rivals >= profile.RivalScriptCount && tags >= profile.TagScriptCount
            : rivals == profile.RivalScriptCount && tags == profile.TagScriptCount;
        if (!expected)
            throw new InvalidDataException($"Los scripts del rival no son los esperados ({rivals} de {profile.RivalScriptCount} combates, {tags} de {profile.TagScriptCount} combates dobles).");

        SetSelectionText(data, chosen, original, files);
        for (int i = 0; i < 3; i++)
            if (chosen[i] != original[i])
                changes.Add(new Gen5Change($"Inicial {i + 1}", "Especie", original[i], chosen[i]));
    }

    // The selection screen computes each species from a pointer offset; this frees room for two add/sub instructions per starter
    // so any species can be shown (UPR-ZX, StarterPokemonGraphicsPrefix). Thumb code, 16-bit words.
    private static void PatchSelectionGraphics(byte[] data, int[] chosen)
    {
        ushort R(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        void W(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), (ushort)value);
        int offset = Gen4Data.FindUnique(data, DpptGraphicsPrefix, "la rutina gráfica de la elección de inicial") + DpptGraphicsPrefix.Length;
        int inner = Gen4Data.FindUnique(data, DpptGraphicsInner, "la rutina interna de la elección de inicial") + DpptGraphicsInner.Length;
        W(offset + 0xC, R(offset + 0xA));
        if (offset % 4 == 0)
            data[offset + 0xC]--;
        W(offset + 0xA, R(offset + 0x8));
        data[offset + 0xA]--;
        W(offset + 0x8, R(offset + 0x6));
        W(offset + 0x6, R(offset + 0x4));
        W(offset + 0x4, R(offset + 0x2));
        W(offset + 0x2, 0x6828);
        W(offset, 0x182D);
        offset += 0x16;
        W(offset, 0x6828);
        offset += 0xA;
        for (int i = 0; i < 3; i++)
        {
            int difference = chosen[i] - 4 * (i + 1), first = 0x3200, second = 0x3200;
            if (difference < 0)
            {
                first |= 0x800;
                difference = -difference;
            }
            else if (difference > 255)
            {
                second |= 0xFF;
                difference -= 255;
            }
            first |= difference & 0xFF;
            data[offset] = (byte)(4 * (i + 1));
            W(offset + 2, R(offset + 4));
            W(offset + 4, first);
            W(offset + 8, second);
            offset += 0xE;
        }
        data[offset] = 1;
        data[inner + 1] = 0x68;
    }

    private static int PatchDpptRival(byte[] file, int[] chosen, IReadOnlyList<int> original, Gen4Kind kind)
    {
        int count = 0;
        foreach (int at in Occurrences(file, DpptRivalMagic))
        {
            int jump = at + DpptRivalMagic.Length, target = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(jump)) + jump + 4;
            if (target < 0 || target + 2 > file.Length)
                continue;
            // The branch leads to a trainer battle, the Hall of Fame check (Fight/Survival Area) or, in Platinum, the starter battle.
            int command = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(target));
            if (command != 0xE5 && command != 0x28F && (command != 0x125 || kind != Gen4Kind.Platinum))
                continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0x8)) != original[0] || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0x15)) != original[1])
                throw new InvalidDataException("Un script del rival no compara los iniciales originales.");
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 0x8), (ushort)chosen[0]);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 0x15), (ushort)chosen[1]);
            count++;
        }
        return count;
    }

    private static int PatchTagBattle(byte[] file, int[] chosen, IReadOnlyList<int> original)
    {
        int count = 0;
        foreach (int at in Occurrences(file, DpptTagMagic))
        {
            int second = at + DpptTagMagic.Length + 2;
            if (second + DpptTagMagic2.Length + 4 > file.Length || !file.AsSpan(second, DpptTagMagic2.Length).SequenceEqual(DpptTagMagic2))
                continue;
            int jump = second + DpptTagMagic2.Length, target = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(jump)) + jump + 4;
            if (target < 0 || target + 2 > file.Length || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(target)) != 0x1B)
                continue;
            int partner = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0x21));
            if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at + 0xE)) != original[1] || (partner != original[0] && partner != original[2]))
                throw new InvalidDataException("Un combate doble no compara los iniciales originales.");
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 0x21), (ushort)(partner == original[0] ? chosen[0] : chosen[2]));
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 0xE), (ushort)chosen[1]);
            count++;
        }
        return count;
    }

    private static int PatchHgssRival(byte[] file, int[] chosen, IReadOnlyList<int> original)
    {
        var found = Occurrences(file, HgssRivalMagic).ToArray();
        if (found.Length != 1)
            return 0;
        int at = found[0], second = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 13)) + at + 17;
        if (second < 0 || second + 6 > file.Length || file[second] != 0x11 || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(second + 4)) != original[1])
            throw new InvalidDataException("Un script del rival de HeartGold no compara los iniciales originales.");
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 8), (ushort)chosen[0]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(second + 4), (ushort)chosen[1]);
        return 1;
    }

    // Names, Pokédex categories and types are replaced inside the existing lines, keeping colours and line breaks.
    private static void SetSelectionText(Gen4Data data, int[] chosen, IReadOnlyList<int> original, Dictionary<string, byte[]> files)
    {
        var profile = data.Profile;
        var names = data.SpeciesNames();
        var text = data.CopyNarc(Gen4Profile.Text);
        byte[] screen = text.Files[profile.StarterScreenText];
        var lines = Gen4Text.Read(screen);
        var edits = new Dictionary<int, string>();
        void Swap(int line, string before, string after)
        {
            string current = edits.TryGetValue(line, out var edited) ? edited : lines[line];
            int first = current.IndexOf(before, StringComparison.Ordinal);
            if (first < 0 || current.IndexOf(before, first + before.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException($"La línea {line} del texto de elección no contiene «{before}» una sola vez.");
            edits[line] = current.Replace(before, after, StringComparison.Ordinal);
        }
        if (profile.Kind == Gen4Kind.HeartGoldSoulSilver)
        {
            var stats = data.BaseStats();
            var types = data.Text(profile.TypeNamesText);
            string Type(int species) => TitleCase(types[stats[species].Type1]) + "-type";
            for (int i = 0; i < 3; i++)
                foreach (int line in new[] { i + 1, i + 4 })
                {
                    Swap(line, names[original[i]], names[chosen[i]]);
                    Swap(line, Type(original[i]), Type(chosen[i]));
                }
        }
        else
        {
            var categories = data.Text(profile.DexCategoryText);
            for (int i = 0; i < 3; i++)
            {
                Swap(i + 1, categories[original[i]], categories[chosen[i]]);
                Swap(i + 1, names[original[i]], names[chosen[i]]);
            }
        }
        text.Files[profile.StarterScreenText] = Gen4Text.Replace(screen, edits);
        files[data.PathOf(Gen4Profile.Text)] = text.Write();
    }

    private static string TitleCase(string upper) => upper.Length == 0 ? upper : upper[0] + upper[1..].ToLowerInvariant();

    private static IEnumerable<int> Occurrences(byte[] data, byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= data.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                yield return i;
    }

    private static int[] PickStarters(uint seed)
    {
        var picked = new List<int>();
        for (int attempt = 0; picked.Count < 3; attempt++)
        {
            int species = Gen5Randomizer.Index(seed, $"starter/{attempt}", Gen4Data.MaxSpecies) + 1;
            if (!picked.Contains(species))
                picked.Add(species);
        }
        return [.. picked];
    }

    /// <summary>Dry run of the starter changes with the original starters, to know before randomizing whether a romhack supports them.</summary>
    internal static string? StartersProblem(Gen4Data data)
    {
        try
        {
            Generate(data, new Gen5RandomizerOptions(0) { Starters = Gen5StarterMode.Custom, CustomStarters = [.. data.Starters()] });
            return null;
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Only ordinary items move: TMs, HMs, key items, version exclusives and the unused ids (UPR-ZX Gen4 allowed items:
    /// everything from 112 to 134 and from 328 on) stay where they are.
    /// </summary>
    public static bool IsMovableItem(int item) => item is (>= 1 and <= 111) or (>= 135 and <= 327);

    private static void ShuffleFieldItems(Gen4Data data, uint seed, Narc scripts, ref byte[]? arm9, List<Gen5Change> changes)
    {
        var slots = data.FieldItems().Where(s => IsMovableItem(s.Item)).ToArray();
        int[] items = Gen5Randomizer.Shuffle(slots.Select(s => s.Item).ToArray(), seed, "field");
        byte[] script = scripts.Files[data.Profile.ItemBallScript];
        // Romhacks with a modified ARM9 have no hidden items to write.
        if (slots.Any(s => s.Hidden))
            arm9 ??= data.Arm9();
        for (int i = 0; i < slots.Length; i++)
        {
            var (hidden, offset, before) = slots[i];
            BinaryPrimitives.WriteUInt16LittleEndian((hidden ? arm9! : script).AsSpan(offset), (ushort)items[i]);
            if (items[i] != before)
                changes.Add(new Gen5Change($"{(hidden ? "Objeto oculto" : "Objeto visible")} {i + 1}", "Objeto", before, items[i]));
        }
    }

    private static void ShuffleShops(Gen4Data data, uint seed, ref byte[]? arm9, List<Gen5Change> changes)
    {
        var slots = data.Shops().Where(s => !data.Profile.FixedShops.Contains(s.Shop) && IsMovableItem(s.Item)).ToArray();
        int[] items = Gen5Randomizer.Shuffle(slots.Select(s => s.Item).ToArray(), seed, "shop");
        arm9 ??= data.Arm9();
        for (int i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(arm9.AsSpan(slots[i].Offset), (ushort)items[i]);
            if (items[i] != slots[i].Item)
                changes.Add(new Gen5Change($"Tienda {slots[i].Shop} · producto {slots[i].Index + 1}", "Objeto", slots[i].Item, items[i]));
        }
    }

    private static void RandomizeTrainers(Gen4Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Dictionary<string, byte[]> files, List<Gen5Change> changes)
    {
        var headers = data.CopyNarc(Gen4Profile.TrainerData);
        var teams = data.CopyNarc(Gen4Profile.TrainerPokemon);
        foreach (var trainer in data.Trainers())
        {
            string location = $"Entrenador {trainer.Id} · {trainer.ClassName} {trainer.Name}".TrimEnd();
            int oldSize = data.TeamEntrySize(trainer.Flags);
            // New species cannot keep moves picked for the old one: drop custom moves so the game uses level-up moves.
            int flags = trainer.Flags & ~1, newSize = data.TeamEntrySize(flags);
            byte[] original = teams.Files[trainer.Id];
            var team = new List<byte>();
            for (int slot = 0; slot < trainer.Team.Count; slot++)
            {
                var p = trainer.Team[slot];
                int species = pick(options.Trainers, $"trainer/{trainer.Id}/{slot}", p.Species, p.Level);
                if (species != p.Species)
                    changes.Add(new Gen5Change($"{location} · Pokémon {slot + 1} · nivel {p.Level}", "Especie", p.Species, species));
                byte[] entry = new byte[newSize];
                int at = slot * oldSize;
                // Difficulty, ability/gender and level as they were; forms belong to the old species, so form 0.
                original.AsSpan(at, 4).CopyTo(entry);
                BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), (ushort)species);
                if (trainer.HasItems)
                    original.AsSpan(at + 6, 2).CopyTo(entry.AsSpan(6));
                // Platinum and HeartGold end each member with two more bytes (ball seal); keep them.
                if (data.Profile.Kind != Gen4Kind.DiamondPearl)
                    original.AsSpan(at + oldSize - 2, 2).CopyTo(entry.AsSpan(newSize - 2));
                team.AddRange(entry);
            }
            if (flags != trainer.Flags)
            {
                changes.Add(new Gen5Change(location, "Movimientos personalizados", 1, 0));
                headers.Files[trainer.Id] = (byte[])headers.Files[trainer.Id].Clone();
                headers.Files[trainer.Id][0] = (byte)flags;
            }
            // Keep any bytes after the team (padding in some entries) as they were.
            teams.Files[trainer.Id] = team.Concat(original.Skip(trainer.Team.Count * oldSize)).ToArray();
        }
        files[data.PathOf(Gen4Profile.TrainerData)] = headers.Write();
        files[data.PathOf(Gen4Profile.TrainerPokemon)] = teams.Write();
    }

    private static void RandomizeWild(Gen4Data data, Gen5RandomizerOptions options, Func<Gen5SpeciesMode, string, int, int, int> pick,
        Dictionary<string, byte[]> files, List<Gen5Change> changes)
    {
        var wild = data.CopyNarc(Gen4Profile.WildPokemon);
        foreach (var slot in data.Encounters())
        {
            if (slot.Species is < 1 or > Gen4Data.MaxSpecies)
                continue;
            // Same original species in one table maps to the same new species, keeping the table's rates.
            int replacement = pick(options.Wild, $"wild/{slot.File}/{slot.Group}/{slot.Species}", slot.Species, slot.MaxLevel);
            var span = wild.Files[slot.File].AsSpan(slot.Offset);
            if (slot.Width == 4)
                BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)replacement);
            else
                BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)replacement);
            if (replacement != slot.Species)
                changes.Add(new Gen5Change($"Encuentros {slot.File} · {slot.Group} · hueco {slot.Index + 1}", "Especie", slot.Species, replacement));
        }
        files[data.PathOf(Gen4Profile.WildPokemon)] = wild.Write();
    }
}
