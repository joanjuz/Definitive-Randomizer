using System.Text;

namespace ThreeDS.Progressive.Services;

/// <summary>Read-only equivalent of SMWE's Dump Tables operation.</summary>
public sealed partial class GameEncounterService
{
    internal string ExportAllEncounterTables()
    {
        var data=Records();
        var sb=new StringBuilder(1024*64);
        sb.AppendLine("3DS Progressive · reporte de encuentros");
        sb.AppendLine("Juego: "+(Config.USUM?"Ultra Sol / Ultra Luna":
            Config.Generation==7?"Sol / Luna":Config.ORAS?"Omega Ruby / Alpha Sapphire":"X / Y"));
        sb.AppendLine("Tablas: "+Entries.Count);
        foreach(var entry in Entries)
        {
            var groups=ReadRecord(entry.Id,data);
            var fields=groups.SelectMany(g=>g.Fields)
                .ToDictionary(f=>f.Key,f=>f.Value);
            sb.AppendLine();
            sb.AppendLine("==================================================");
            sb.AppendLine(entry.Display);
            sb.AppendLine("==================================================");
            if(Config.Generation==7)
            {
                sb.AppendLine($"Niveles: {fields["Min"]}–{fields["Max"]}");
                sb.AppendLine("Probabilidades: "+string.Join(", ",
                    Enumerable.Range(0,10).Select(i=>
                        $"#{i+1}:{fields["Rate"+i]}%"))+
                    $" · TOTAL {Enumerable.Range(0,10).Sum(i=>fields["Rate"+i])}%");
                for(int group=0;group<=8;group++)
                {
                    sb.AppendLine(group==0?"ENCUENTROS NORMALES":
                        group==8?"SOS · CLIMA":$"ALIADOS SOS · FILA {group}");
                    int count=group==8?6:10;
                    for(int slot=0;slot<count;slot++)
                    {
                        string prefix=$"S{group}.{slot}.";
                        int species=fields[prefix+"Species"];
                        int form=fields[prefix+"Form"];
                        string name=species==0?"(vacío)":species<Config.MaxSpeciesID+1
                            ?Config.GetText(pk3DS.Core.TextName.SpeciesNames)[species]
                            :$"ID {species}";
                        sb.AppendLine($"  #{slot+1:00} · {name} [ID {species}, forma {form}]");
                    }
                }
            }
            else
            {
                foreach(var group in groups)
                {
                    sb.AppendLine(group.Name.ToUpperInvariant());
                    foreach(var species in group.Fields.Where(f=>f.Key.EndsWith(".Species",
                        StringComparison.Ordinal)))
                    {
                        string prefix=species.Key[..^7];
                        string name=species.Choices?.FirstOrDefault(c=>c.Id==species.Value)
                            ?.Display??$"ID {species.Value}";
                        sb.AppendLine($"  {name} · forma {fields[prefix+"Form"]} " +
                            $"· niveles {fields[prefix+"Min"]}–{fields[prefix+"Max"]}");
                    }
                }
            }
        }
        return sb.ToString();
    }
}
