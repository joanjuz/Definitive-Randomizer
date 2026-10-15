namespace ThreeDS.Progressive.Services;

/// <summary>
/// Pure filters used by Gen7 Wild. They never introduce species outside
/// the generation/legendary/event pool chosen by SpeciesRandomizer.
/// </summary>
internal static class WildSelectionPolicy
{
    internal static int[] PreferUnseen(IReadOnlyList<int> pool,IReadOnlySet<int> used)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(used);
        if(pool.Count==0)throw new InvalidOperationException("Pool Wild sin especies.");
        int[] unseen=pool.Where(species=>!used.Contains(species)).ToArray();
        return unseen.Length>0?unseen:pool.ToArray();
    }

    internal static int[] SimilarBstPool(IReadOnlyList<int> pool,
        Func<int,int> bstOf,int originalBST,int tolerancePercent,bool relaxIfEmpty)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(bstOf);
        if(pool.Count==0)return [];
        if(originalBST<=0 || tolerancePercent<0 || tolerancePercent>100)
            throw new ArgumentOutOfRangeException(nameof(tolerancePercent));
        // The UI explicitly labels 0% as unrestricted.
        if(tolerancePercent==0)return pool.ToArray();
        int[] within=pool.Where(id=>
            Math.Abs(bstOf(id)-originalBST)*100L<=originalBST*(long)tolerancePercent).ToArray();
        if(within.Length>0||!relaxIfEmpty)return within;
        int closest=pool.Min(id=>Math.Abs(bstOf(id)-originalBST));
        return pool.Where(id=>Math.Abs(bstOf(id)-originalBST)==closest).ToArray();
    }
}
