#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

public static partial class NdsTrainerTools
{
    private static List<Member> ReadTeam(Layout layout, byte[] raw, int flags, int count)
    {
        int size = layout.BaseSize + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0) + layout.Trailing;
        if (raw.Length < size * count)
            throw new InvalidDataException("Equipo de entrenador incompleto.");
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
        return team;
    }

    private static byte[] WriteTeam(Layout layout, IReadOnlyList<Member> team, int flags)
    {
        int size = layout.BaseSize + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0) + layout.Trailing;
        var output = new byte[size * team.Count];
        for (int slot = 0; slot < team.Count; slot++)
        {
            var m = team[slot];
            int at = slot * size;
            m.Base.CopyTo(output, at); at += layout.BaseSize;
            if ((flags & 2) != 0) { BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at), (ushort)m.Item); at += 2; }
            if ((flags & 1) != 0)
            {
                for (int k = 0; k < 4; k++) BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + k * 2), (ushort)m.Moves[k]);
                at += 8;
            }
            m.Trailing.CopyTo(output, at);
        }
        return output;
    }

    public static IReadOnlyList<NdsCell> ReadManual(Gen5Data data) => ManualCells(data.Trainers(), 5,
        data.Text(data.Profile.TrainerClassesText), data.Text(data.Profile.ItemNamesText).Count - 1, data.Text(data.Profile.MoveNamesText).Count - 1,
        data.CopyNarc(Gen5Profile.TrainerData));
    public static IReadOnlyList<NdsCell> ReadManual(Gen4Data data) => ManualCells(data.Trainers(), 4,
        data.Text(data.Profile.TrainerClassesText), data.Text(data.Profile.ItemNamesText).Count - 1, data.Text(data.Profile.MoveNamesText).Count - 1,
        data.CopyNarc(Gen4Profile.TrainerData));

    private static IReadOnlyList<NdsCell> ManualCells(IReadOnlyList<Gen5Trainer> trainers, int generation, IReadOnlyList<string> classes, int items, int moves, Narc headers)
    {
        var cells = new List<NdsCell>();
        foreach (var t in trainers)
        {
            string name = $"{t.ClassName} {t.Name}".Trim(), group = "Datos del entrenador";
            void Add(string key, string label, int value, int min, int max, NdsValueKind kind = NdsValueKind.Number, IReadOnlyList<string>? options = null) =>
                cells.Add(new NdsCell(t.Id, name, group, label, key, value, min, max, kind) { Options = options });
            Add("Class", "Clase", t.TrainerClass, 0, Math.Min(255, classes.Count - 1), NdsValueKind.Option, classes);
            Add("Count", "Pokémon en el equipo", t.Team.Count, t.Team.Count == 0 ? 0 : 1, 6);
            Add("HasMoves", "Movimientos personalizados · todo el equipo", t.HasMoves ? 1 : 0, 0, 1);
            Add("HasItems", "Objetos equipados explícitos · todo el equipo", t.HasItems ? 1 : 0, 0, 1);
            // Preserve battle type: changing it manually would bypass the existing doubles patches/guards.
            for (int i = 0; i < 4; i++) Add("Item" + i, $"Objeto de combate {i + 1}", BinaryPrimitives.ReadUInt16LittleEndian(headers.Files[t.Id].AsSpan(4 + i * 2)), 0, items, NdsValueKind.Item);
            for (int slot = 0; slot < 6; slot++)
            {
                group = "Pokémon " + (slot + 1);
                var p = slot < t.Team.Count ? t.Team[slot] : new Gen5TrainerPokemon(1, 0, 1, 0, 0, 0, 0, new int[4]);
                string k = $"P{slot}.";
                Add(k + "Species", "Especie", p.Species, 1, generation == 4 ? Gen4Data.MaxSpecies : Gen5Randomizer.MaxSpecies, NdsValueKind.Species);
                Add(k + "Form", "Forma (ID del juego)", p.Form, 0, generation == 4 ? 63 : 65535);
                Add(k + "Level", "Nivel", p.Level, 1, 100);
                Add(k + "IVs", "Dificultad / IV combinado (0–255)", p.Difficulty, 0, 255);
                Add(k + "Ability", "Slot de habilidad (valor del juego)", p.AbilitySlot, 0, 15);
                Add(k + "Gender", "Género (valor del juego)", p.GenderFlag, 0, 15);
                Add(k + "Item", "Objeto equipado · requiere formato explícito", p.HeldItem, 0, items, NdsValueKind.Item);
                for (int m = 0; m < 4; m++) Add(k + "Move" + m, $"Movimiento {m + 1} · requiere formato personalizado", t.HasMoves && slot < t.Team.Count ? p.Moves[m] : 0, 0, moves, NdsValueKind.Move);
            }
        }
        return cells;
    }

    public static Gen5RandomizerResult EditManual(Gen5Data data, IReadOnlyDictionary<string, int> edits) => EditManual(
        new Layout(true, 8, 0, 2), ReadManual(data), data.CopyNarc(Gen5Profile.TrainerData), data.CopyNarc(Gen5Profile.TrainerPokemon),
        data.PathOf(Gen5Profile.TrainerData), data.PathOf(Gen5Profile.TrainerPokemon), edits);
    public static Gen5RandomizerResult EditManual(Gen4Data data, IReadOnlyDictionary<string, int> edits) => EditManual(
        new Layout(false, 6, data.Profile.Kind == Gen4Kind.DiamondPearl ? 0 : 2, 16), ReadManual(data), data.CopyNarc(Gen4Profile.TrainerData), data.CopyNarc(Gen4Profile.TrainerPokemon),
        data.PathOf(Gen4Profile.TrainerData), data.PathOf(Gen4Profile.TrainerPokemon), edits);

    // New manual operations use zero-based move keys. Historical NdsCells/Edit recipes remain one-based, unchanged.
    private static Gen5RandomizerResult EditManual(Layout layout, IReadOnlyList<NdsCell> cells, Narc headers, Narc teams, string headerPath, string teamPath, IReadOnlyDictionary<string, int> edits)
    {
        var byKey = cells.ToDictionary(c => c.Key);
        foreach (var (key, value) in edits)
            if (!byKey.TryGetValue(key, out var cell) || value < cell.Min || value > cell.Max)
                throw new ArgumentException("Campo de entrenador desconocido o fuera de rango: " + key);
        var changes = new List<Gen5Change>();
        foreach (var entry in edits.GroupBy(e => byKey[e.Key].Entry))
        {
            int id = entry.Key;
            var original = cells.Where(c => c.Entry == id).ToDictionary(c => c.Local, c => c.Value);
            var values = new Dictionary<string, int>(original);
            foreach (var (key, value) in entry) values[byKey[key].Local] = value;
            int V(string key) => values[key];
            byte[] header = headers.Files[id], raw = teams.Files[id];
            int flags = header[0], count = header[3], newFlags = (flags & ~3) | V("HasMoves") | V("HasItems") << 1;
            if (header[layout.BattleOffset] != 0 && V("Count") < count)
                throw new ArgumentException("No se puede reducir manualmente un equipo doble o de tipo especial.");
            // Turning a format off is explicit; it removes those fields for every member.
            foreach (var (key, value) in entry)
            {
                var c = byKey[key];
                if (!c.Local.StartsWith("P", StringComparison.Ordinal) || c.Value == value) continue;
                if (c.Local[1] - '0' >= V("Count"))
                    throw new ArgumentException("El Pokémon editado está fuera del equipo.");
                if (c.Local.EndsWith(".Item", StringComparison.Ordinal) && V("HasItems") == 0 || c.Kind == NdsValueKind.Move && V("HasMoves") == 0)
                    throw new ArgumentException("Activa primero el formato explícito de objetos o movimientos del equipo.");
            }
            var team = ReadTeam(layout, raw, flags, count);
            // Keep opaque data after the team's known records, including hacks' extra tail bytes.
            int oldSize = layout.BaseSize + ((flags & 2) != 0 ? 2 : 0) + ((flags & 1) != 0 ? 8 : 0) + layout.Trailing;
            byte[] suffix = raw.AsSpan(oldSize * count).ToArray();
            while (team.Count < V("Count")) team.Add(new Member { Base = new byte[layout.BaseSize], Gen5 = layout.Gen5, Trailing = new byte[layout.Trailing] });
            team = team.Take(V("Count")).ToList();
            for (int slot = 0; slot < team.Count; slot++)
            {
                var m = team[slot]; string k = $"P{slot}.";
                if (V(k + "Species") != original[k + "Species"] && !edits.ContainsKey($"{id}:" + k + "Form"))
                {
                    values[k + "Form"] = 0;
                    if (original[k + "Form"] != 0) changes.Add(new Gen5Change($"Entrenador {id} · {byKey[$"{id}:" + k + "Species"].EntryName} · Pokémon {slot + 1}", "Forma", original[k + "Form"], 0));
                }
                m.Base[0] = (byte)V(k + "IVs"); m.Base[1] = (byte)(V(k + "Ability") << 4 | V(k + "Gender"));
                m.Level = V(k + "Level");
                BinaryPrimitives.WriteUInt16LittleEndian(m.Base.AsSpan(4), (ushort)(V(k + "Species") | (layout.Gen5 ? 0 : V(k + "Form") << 10)));
                if (layout.Gen5) BinaryPrimitives.WriteUInt16LittleEndian(m.Base.AsSpan(6), (ushort)V(k + "Form"));
                m.Item = V(k + "Item"); m.Moves = Enumerable.Range(0, 4).Select(i => V(k + "Move" + i)).ToArray();
            }
            header[0] = (byte)newFlags; header[1] = (byte)V("Class"); header[3] = (byte)team.Count;
            for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4 + i * 2), (ushort)V("Item" + i));
            var output = WriteTeam(layout, team, newFlags).Concat(suffix).ToArray();
            teams.Files[id] = output;
            foreach (var (key, value) in entry)
            {
                var c = byKey[key];
                if (value != c.Value) changes.Add(new Gen5Change($"Entrenador {id} · {c.EntryName} · {c.Group}", c.Field, c.Value, value));
            }
        }
        return new Gen5RandomizerResult(new Dictionary<string, byte[]> { [headerPath] = headers.Write(), [teamPath] = teams.Write() }, changes, new Dictionary<int, byte[]>());
    }
}
