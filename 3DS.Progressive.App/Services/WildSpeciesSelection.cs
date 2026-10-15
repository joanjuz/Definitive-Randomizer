namespace ThreeDS.Progressive.Services;

/// <summary>
/// Reproduce the vanilla/Progressive preference for a species different from
/// the one being replaced, without retries, bias or an unbounded loop.
/// The only valid no-op is a pool containing no alternative species.
/// </summary>
internal static class WildSpeciesSelection
{
    internal static int ChooseReplacement(IReadOnlyList<int> pool,int original,Random random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        if(pool.Count==0)
            throw new InvalidOperationException("No hay especies permitidas para reemplazar el encuentro.");

        int alternatives=0;
        foreach(int candidate in pool)
            if(candidate!=original)alternatives++;

        if(alternatives==0)
            return pool[random.Next(pool.Count)];

        // Draw uniformly from all valid alternatives (duplicates retain their
        // source-pool weights), using exactly one seeded RNG draw.
        int index=random.Next(alternatives);
        foreach(int candidate in pool)
            if(candidate!=original && index--==0)return candidate;

        throw new InvalidOperationException("No se pudo seleccionar la especie alternativa.");
    }
}
