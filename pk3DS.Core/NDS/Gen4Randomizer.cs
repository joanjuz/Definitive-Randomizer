#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Diamond, Platinum and HeartGold randomizer for trainers, wild Pokémon, field items and shops, with the same modes, seed
/// handling and change log as Gen5. Starters are not supported yet and are rejected instead of ignored.
/// </summary>
public static class Gen4Randomizer
{
    public static Gen5RandomizerResult Generate(Gen4Data data, Gen5RandomizerOptions options)
    {
        if (options.Starters != Gen5StarterMode.Keep)
            throw new ArgumentException("En Gen4 todavía no se pueden cambiar los iniciales.", nameof(options));
        var pick = Gen5Randomizer.SpeciesPicker(data.BaseStats(), Gen4Data.MaxSpecies, options);
        var files = new Dictionary<string, byte[]>();
        var changes = new List<Gen5Change>();
        if (options.Trainers != Gen5SpeciesMode.Keep)
            RandomizeTrainers(data, options, pick, files, changes);
        if (options.Wild != Gen5SpeciesMode.Keep)
            RandomizeWild(data, options, pick, files, changes);
        byte[]? arm9 = null;
        if (options.FieldItems == Gen5ItemMode.Shuffle)
            ShuffleFieldItems(data, options.Seed, files, ref arm9, changes);
        if (options.Shops == Gen5ItemMode.Shuffle)
            ShuffleShops(data, options.Seed, ref arm9, changes);
        return new Gen5RandomizerResult(files, changes, new Dictionary<int, byte[]>()) { Arm9 = arm9 };
    }

    /// <summary>
    /// Only ordinary items move: TMs, HMs, key items, version exclusives and the unused ids (UPR-ZX Gen4 allowed items:
    /// everything from 112 to 134 and from 328 on) stay where they are.
    /// </summary>
    public static bool IsMovableItem(int item) => item is (>= 1 and <= 111) or (>= 135 and <= 327);

    private static void ShuffleFieldItems(Gen4Data data, uint seed, Dictionary<string, byte[]> files, ref byte[]? arm9, List<Gen5Change> changes)
    {
        var slots = data.FieldItems().Where(s => IsMovableItem(s.Item)).ToArray();
        int[] items = Gen5Randomizer.Shuffle(slots.Select(s => s.Item).ToArray(), seed, "field");
        var scripts = data.CopyNarc(Gen4Profile.Scripts);
        byte[] script = scripts.Files[data.Profile.ItemBallScript];
        arm9 ??= data.Arm9();
        for (int i = 0; i < slots.Length; i++)
        {
            var (hidden, offset, before) = slots[i];
            BinaryPrimitives.WriteUInt16LittleEndian((hidden ? arm9 : script).AsSpan(offset), (ushort)items[i]);
            if (items[i] != before)
                changes.Add(new Gen5Change($"{(hidden ? "Objeto oculto" : "Objeto visible")} {i + 1}", "Objeto", before, items[i]));
        }
        files[data.PathOf(Gen4Profile.Scripts)] = scripts.Write();
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
