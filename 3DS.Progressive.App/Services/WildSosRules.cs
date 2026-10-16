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
        Random random,Func<int,int> selectForm,bool diversity=false)
    {
        if(allowed.Count==0)
            throw new InvalidOperationException("No hay especies válidas para rellenar aliados SOS.");
        int added=0;
        for(int group=1;group<=7;group++)
        {
            var used=new HashSet<int>();
            if(diversity)
                for(int existing=0;existing<10;existing++)
                {
                    int id=values[$"S{group}.{existing}.Species"];
                    if(id>0)used.Add(id);
                }
            for(int slot=0;slot<10;slot++)
            {
                string source=$"S0.{slot}.", target=$"S{group}.{slot}.";
                if(values[source+"Species"]==0||values[target+"Species"]!=0)
                    continue;
                // Vanilla randomizes the new ally independently. It is not a
                // clone of the corresponding regular encounter.
                var candidates=diversity?WildSelectionPolicy.PreferUnseen(allowed,used):allowed;
                int species=WildSpeciesSelection.ChooseReplacement(
                    candidates,values[source+"Species"],random);
                if(diversity)used.Add(species);
                values[target+"Species"]=species;
                values[target+"Form"]=selectForm(species);
                added++;
            }
        }
        return added;
    }
}
