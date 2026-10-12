namespace ThreeDS.Progressive.Services;

/// <summary>
/// Faithful slot behavior from EncounterTable.CopySlotsToSOS and
/// Wild7Randomizer/ProgressiveWildRandomizer.FillEmptySOSSlots.
/// Does not touch the six weather encounter positions (S8).
/// </summary>
internal static class WildSosRules
{
    internal static void CopyRegularIntoSos(IDictionary<string,int> values)
    {
        for(int group=1;group<=7;group++)
            for(int slot=0;slot<10;slot++)
            {
                string source=$"S0.{slot}.", target=$"S{group}.{slot}.";
                // Vanilla copies even into formerly empty SOS slots.
                values[target+"Species"]=values[source+"Species"];
                values[target+"Form"]=values[source+"Form"];
            }
    }

    internal static int FillMissingAllies(
        IDictionary<string,int> values,IReadOnlyList<int> allowed,
        Random random,Func<int,int> selectForm)
    {
        if(allowed.Count==0)
            throw new InvalidOperationException("No hay especies válidas para rellenar aliados SOS.");
        int added=0;
        for(int group=1;group<=7;group++)
            for(int slot=0;slot<10;slot++)
            {
                string source=$"S0.{slot}.", target=$"S{group}.{slot}.";
                if(values[source+"Species"]==0||values[target+"Species"]!=0)
                    continue;
                // Vanilla randomizes the new ally independently. It is not a
                // clone of the corresponding regular encounter.
                int species=allowed[random.Next(allowed.Count)];
                values[target+"Species"]=species;
                values[target+"Form"]=selectForm(species);
                added++;
            }
        return added;
    }
}
