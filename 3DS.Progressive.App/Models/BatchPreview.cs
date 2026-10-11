using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive.Models;

public sealed record BatchChange(int Id, string Name, string Field, string Before, string After);

/// <summary>A frozen proposal tied to one service and one exact source file.</summary>
public sealed class BatchPreview
{
    internal Guid Owner { get; }
    internal byte[] Fingerprint { get; }
    private readonly byte[][] _records;
    internal byte[][] CopyRecords() => _records.Select(r => (byte[])r.Clone()).ToArray();
    public string Action { get; }
    public int Seed { get; }
    internal bool BanTraps { get; }
    internal IReadOnlyList<FileChange> AdditionalFiles { get; }
    internal IReadOnlyList<(string Path, byte[] Fingerprint)> Dependencies { get; }
    public int AdditionalFileCount => AdditionalFiles.Count;
    public int ScopeCount { get; }
    public int ChangedCount { get; }
    public IReadOnlyList<BatchChange> Changes { get; }

    internal BatchPreview(Guid owner, byte[] fingerprint, byte[][] records,
        string action, int seed, int scopeCount, IEnumerable<BatchChange> changes, bool banTraps = false,
        IEnumerable<FileChange>? additionalFiles = null, IEnumerable<(string Path, byte[] Fingerprint)>? dependencies = null)
    {
        Owner = owner;
        Fingerprint = (byte[])fingerprint.Clone();
        _records = records.Select(r => (byte[])r.Clone()).ToArray();
        Action = action;
        Seed = seed;
        BanTraps = banTraps;
        AdditionalFiles = Array.AsReadOnly((additionalFiles ?? []).ToArray());
        Dependencies = Array.AsReadOnly((dependencies ?? []).Select(d => (d.Path, (byte[])d.Fingerprint.Clone())).ToArray());
        ScopeCount = scopeCount;
        Changes = Array.AsReadOnly(changes.ToArray());
        ChangedCount = Changes.Where(c => c.Id > 0).Select(c => c.Id).Distinct().Count();
    }
}

public enum BatchAction { PersonalRandomize, PersonalModify, BanTrappingAbilities, MoveRandomize, Metronome, MoveBalance }

public sealed record BatchSettings
{
    public int Seed { get; init; } = 12345;
    public bool Stats { get; init; }
    public bool Shuffle { get; init; }
    // Core order: HP, ATK, DEF, SPE, SPA, SPD.
    public bool[] StatSlots { get; init; } = [true, true, true, true, true, true];
    public decimal Deviation { get; init; } = 25;
    public bool Types { get; init; }
    public decimal SingleTypeChance { get; init; } = 50;
    public bool Abilities { get; init; }
    public bool WonderGuard { get; init; }
    public bool BanTraps { get; init; }
    public bool Items { get; init; }
    public bool Catch { get; init; }
    public bool Eggs { get; init; }
    public decimal SameEggChance { get; init; } = 50;
    public bool Tms { get; init; }
    public bool Hms { get; init; }
    public bool TypeTutors { get; init; }
    public bool SpecialTutors { get; init; }
    public bool PersonalTemplate { get; init; }
    public bool RemoveEvs { get; init; }
    public bool SlowGrowth { get; init; }
    public bool ScaleExperience { get; init; }
    public decimal ExperiencePercent { get; init; } = 100;
    public bool ClearCompatibility { get; init; }
    public bool FullTms { get; init; }
    public bool FullHms { get; init; }
    public bool FullTypeTutors { get; init; }
    public bool FullSpecialTutors { get; init; }
    public bool QuickHatch { get; init; }
    public bool SetCatch { get; init; }
    public int CatchValue { get; init; } = 255;
    public bool SetCall { get; init; }
    public int CallValue { get; init; } = 15;
    public bool MoveTypes { get; init; }
    public bool MoveCategories { get; init; }
    public string? TemplatePath { get; init; }
    public bool BattlePatches { get; init; } = true;
    public bool MoveDescriptions { get; init; } = true;
}
