#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NX;

public sealed record SwordShieldRandomOptions(uint Seed, bool Types, bool Abilities,
    bool IncludeForms = true, bool PreserveTypeCount = true, bool DistinctNormalAbilities = true,
    bool AllowWonderGuard = false, int? OnlyEntry = null, bool Stats = false)
{
    public bool RedistributeStats {get;init;}
    public bool BanTrappingAbilities {get;init;}
    public bool CatchRate {get;init;}
    public int MinimumCatchRate {get;init;}=45;
    public bool HeldItems {get;init;}
    public IReadOnlyList<int> HeldItemPool {get;init;}=Array.Empty<int>();
    public bool EggGroups {get;init;}
    public bool Growth {get;init;}
}
public sealed record SwordShieldRandomChange(int Entry, SwordShieldField Field, int Before, int After);
public sealed class SwordShieldRandomResult
{
    private readonly byte[] _data;
    public byte[] ToArray() => (byte[])_data.Clone();
    public IReadOnlyList<SwordShieldRandomChange> Changes { get; }
    public IReadOnlyList<int> ProtectedSpecies { get; }
    public int EligibleEntries { get; }
    internal SwordShieldRandomResult(byte[] data, List<SwordShieldRandomChange> changes, List<int> protectedSpecies, int eligible)
    { _data = data; Changes = changes.AsReadOnly(); ProtectedSpecies = protectedSpecies.AsReadOnly(); EligibleEntries = eligible; }
}

/// <summary>Versioned pure randomization. Independent streams for each entry and feature; no global RNG.</summary>
public static class SwordShieldPersonalRandomizer
{
    public const string Algorithm = "swsh-personal-splitmix64-v2";
    // Form-dependent ability IDs; reference PKHeX Ability enum. Conservative policy pending gameplay tests.
    private static readonly HashSet<int> FormAbilities = new()
    { 59, 121, 122, 161, 176, 197, 208, 209, 210, 211, 225, 241, 248, 258, 266, 267 };
    public static bool IsProtectedAbility(int id) => FormAbilities.Contains(id);
    public static IReadOnlyList<int> AbilityPool(ReadOnlySpan<byte> baseline, bool allowWonderGuard)
    {
        var table = new SwordShieldPersonalTable(baseline);
        var ids = new SortedSet<int>();
        foreach (var e in table.Entries().Where(e => e.Species != 0))
            for (int slot = 9; slot < 12; slot++)
            {
                int ability = e.Values[slot];
                if (ability < 0 || ability > 267) throw new InvalidDataException("La tabla contiene un ID de habilidad fuera del perfil de Gen8.");
                if (ability != 0 && !FormAbilities.Contains(ability) && (allowWonderGuard || ability != 25)) ids.Add(ability);
            }
        return Array.AsReadOnly(ids.ToArray());
    }
    public static SwordShieldRandomResult Generate(ReadOnlySpan<byte> draft, ReadOnlySpan<byte> baseline, SwordShieldRandomOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var current = new SwordShieldPersonalTable(draft); var original = new SwordShieldPersonalTable(baseline);
        byte[] output = current.ToArray(); var changes = new List<SwordShieldRandomChange>();
        if(options.CatchRate&&options.MinimumCatchRate is <1 or >255)throw new InvalidDataException("Ratio mínimo fuera de rango.");
        if(options.HeldItems&&(options.HeldItemPool is null||options.HeldItemPool.Count==0||options.HeldItemPool.Any(id=>id!=0&&!SwordShieldGameplay.IsAllowedItem(id))))throw new InvalidDataException("Pool de objetos equipados inválido.");
        if (!options.Types && !options.Abilities && !options.Stats&&!options.CatchRate&&!options.HeldItems&&!options.EggGroups&&!options.Growth) return new(output, changes, new(), 0);
        var entries = current.Entries().Where(e => e.Species != 0 && (options.IncludeForms || e.Form == 0)).ToArray();
        if (options.OnlyEntry is int selected)
        {
            if (!entries.Any(e => e.Index == selected)) throw new InvalidDataException("La entrada seleccionada no está presente, no se resolvió o es una forma excluida.");
            entries = entries.Where(e => e.Index == selected).ToArray();
        }
        var protectedIds = original.Entries().Where(e => Enumerable.Range(9, 3).Any(s => FormAbilities.Contains(e.Values[s])))
            .Select(e => e.Species).Where(s => s != 0).ToHashSet();
        var pool = options.Abilities ? AbilityPool(baseline, options.AllowWonderGuard).Where(id=>!options.BanTrappingAbilities||id is not (23 or 71)).ToArray() : Array.Empty<int>();
        int required = options.DistinctNormalAbilities ? 2 : 1;
        var targets = entries.Where(e => !protectedIds.Contains(e.Species)).ToArray();
        if (options.Abilities && targets.Length != 0 && pool.Length < required)
            throw new InvalidDataException($"El proyecto no tiene suficientes habilidades candidatas ({required} necesarias).");
        foreach (var e in targets)
        {
            void Set(SwordShieldField field, int value)
            {
                int before = e.Values[(int)field];
                if (before == value) return;
                SwordShieldPersonalTable.WriteValue(output, e.Index, field, value);
                changes.Add(new(e.Index, field, before, value));
            }
            if(options.Stats)
            {
                var rng=new StableRandom(options.Seed,e.Index,0x53544154);
                int start=e.Values[0]==1?1:0; // Preserve Shedinja-style 1 HP invariant.
                var stats=e.Values.Take(6).ToArray();
                if(options.RedistributeStats)for(int step=0;step<96;step++){int from=start+rng.Next(6-start),to=start+rng.Next(6-start);if(from==to)continue;int amount=Math.Min(1+rng.Next(32),Math.Min(stats[from]-1,255-stats[to]));stats[from]-=amount;stats[to]+=amount;}
                else for(int i=5;i>start;i--){int j=start+rng.Next(i-start+1);(stats[i],stats[j])=(stats[j],stats[i]);}
                for(int i=start;i<6;i++)Set((SwordShieldField)i,stats[i]);
            }
            if(options.CatchRate){var rng=new StableRandom(options.Seed,e.Index,0x43415443);Set(SwordShieldField.CatchRate,options.MinimumCatchRate+rng.Next(256-options.MinimumCatchRate));}
            if(options.HeldItems){var rng=new StableRandom(options.Seed,e.Index,0x4954454D);for(int slot=0;slot<3;slot++)Set((SwordShieldField)(12+slot),options.HeldItemPool[rng.Next(options.HeldItemPool.Count)]);}
            if(options.EggGroups){var rng=new StableRandom(options.Seed,e.Index,0x45474753);Set(SwordShieldField.EggGroup1,1+rng.Next(15));Set(SwordShieldField.EggGroup2,1+rng.Next(15));}
            if(options.Growth){var rng=new StableRandom(options.Seed,e.Index,0x47524F57);Set(SwordShieldField.Growth,rng.Next(6));}
            if (options.Types)
            {
                var rng = new StableRandom(options.Seed, e.Index, 0x54595045);
                int first = rng.Next(18);
                bool mono = options.PreserveTypeCount ? e.Values[6] == e.Values[7] : rng.Next(2) == 0;
                int second = first;
                if (!mono) { second = rng.Next(17); if (second >= first) second++; }
                Set(SwordShieldField.Type1, first); Set(SwordShieldField.Type2, second);
            }
            if (options.Abilities)
            {
                var rng = new StableRandom(options.Seed, e.Index, 0x4142494C);
                int a = rng.Next(pool.Length), b;
                if (options.DistinctNormalAbilities) { b = rng.Next(pool.Length - 1); if (b >= a) b++; }
                else b = rng.Next(pool.Length);
                Set(SwordShieldField.Ability1, pool[a]); Set(SwordShieldField.Ability2, pool[b]);
                Set(SwordShieldField.AbilityHidden, pool[rng.Next(pool.Length)]);
            }
        }
        _ = new SwordShieldPersonalTable(output);
        return new(output, changes, entries.Where(e => protectedIds.Contains(e.Species)).Select(e => e.Species).Distinct().Order().ToList(), targets.Length);
    }
    private sealed class StableRandom
    {
        private ulong _state;
        internal StableRandom(uint seed, int entry, uint domain) => _state = unchecked(((ulong)seed << 32) ^ ((ulong)(uint)entry * 0xD1B54A32D192ED03UL) ^ domain);
        private uint NextUInt()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL; ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (uint)((z ^ (z >> 31)) >> 32);
            }
        }
        internal int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            uint bound = (uint)exclusiveMax, threshold = unchecked(0u - bound) % bound, value;
            do value = NextUInt(); while (value < threshold);
            return (int)(value % bound);
        }
    }
}
