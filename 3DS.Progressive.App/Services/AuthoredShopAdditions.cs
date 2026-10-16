using System.IO;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;

internal sealed partial class AuthoredTemplate
{
    internal static int ProgressionStageCount(GameProjectSession p)=>p.Generation==6?9:8;
    internal static string StageLabel(GameProjectSession p,int stage)=>p.Generation==6?$"{stage} "+(stage==1?"medalla":"medallas"):$"{stage} "+(stage==1?"prueba":"pruebas");
    internal static Dictionary<string,int> LegacyShopOptions(GameProjectSession p)=>p.Generation==6?EditorValues.Flatten(new GameMart6Service(p).RandomOptions):new(){["RandomRegular"]=0,["RandomBP"]=0,["Candies"]=0,["Vitamins"]=0,["FreeMega"]=0,["BanCapsule"]=0,["SpecialsOnly"]=0,["ProtectX"]=1};
    internal static bool CanImportProgressive(GameProjectSession p,string kind)
    {
        if(kind=="Shops")return false;
        if(kind=="FieldItems")return true;
        if(kind=="Moves")return p.Generation==6||p.GameConfiguration.USUM;
        if(kind is not ("Stats" or "Evolutions"))return false;
        string prefix=kind=="Stats"?"pokemon_stats":"evolutions";
        string path=Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",$"{prefix}_gen{p.Generation}.csv");
        try
        {
            if(!File.Exists(path))return false;
            return kind=="Stats"?PokemonStatTemplates.Load(path,Names(p)).Length>0:EvolutionTemplates.Load(path,p.GameConfiguration.GetText(pk3DS.Core.TextName.SpeciesNames),p.ItemNames.ToArray(),p.Generation).Count>0;
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){return false;}

    }
    private void ValidateShopMode(GameProjectSession p)
    {
        if(ShopMode=="Replace")return;
        if(ShopMode!="AppendProgression")throw new InvalidDataException("Modo de template de tiendas desconocido.");
        if(Options.Count>0||AllowedItems.Length>0||BannedItems.Length>0)throw new InvalidDataException("El template de incorporaciones solo contiene objetos y su progreso de desbloqueo.");
        if(Rows.Count==0)throw new InvalidDataException("Añadí al menos un objeto al template de tiendas.");
        var seen=new HashSet<(int,int)>();foreach(var row in Rows)
        {
            int stage=Number(row,"Stage",-1),item=Number(row,"Item",-1);
            if(stage<0||stage>=ProgressionStageCount(p)||item<=0||item>=p.ItemNames.Count||!seen.Add((stage,item)))throw new InvalidDataException("Objeto o progreso inválido, o incorporación repetida en la misma etapa.");
        }
    }
    internal void AppendToStages(IReadOnlyList<List<int>> inventories,int count)
    {
        if(inventories.Count<count)throw new InvalidDataException("La tabla no contiene todas las etapas de tiendas esperadas.");
        foreach(var row in Rows)
            for(int stage=Number(row,"Stage");stage<count;stage++)
            {
                int item=Number(row,"Item");if(!inventories[stage].Contains(item))inventories[stage].Add(item);
            }
    }
    private int RunShopAdditions(GameProjectSession p,int seed,bool validateOnly)
    {
        int count=ProgressionStageCount(p);
        if(p.Generation==6)
        {
            var service=new GameMart6Service(p);var inventories=service.ReadAllItems().Select(row=>row.ToList()).ToArray();AppendToStages(inventories,count);
            var options=LegacyShopOptions(p);options["UseChecks"]=1;options["Operation"]=0;
            var proposal=service.PrepareDraft(inventories.Select(row=>row.ToArray()).ToArray(),options,service.Entries.Select(e=>e.Id).ToArray(),seed);
            if(!validateOnly)service.Apply(proposal);return Count(proposal.Changes);
        }
        var marts=new GameMartService(p);var normal=marts.RegularShops.Take(count).ToArray();
        var added=normal.Select(shop=>shop.Rows.Select(row=>row.ItemId).ToList()).ToArray();AppendToStages(added,count);
        if(!marts.SupportsExpansion&&added.Where((row,i)=>row.Count!=normal[i].Rows.Count).Any())throw new InvalidDataException("Sol/Luna todavía no admite ampliar el número de objetos de las tiendas. No se reemplazó ningún objeto ni se guardó ningún cambio.");
        for(int i=0;i<count;i++)foreach(int item in added[i].Skip(normal[i].Rows.Count))normal[i].Rows.Add(new MartSlotRow(normal[i].Rows.Count+1,item));
        var preview=marts.Prepare();if(!validateOnly&&preview.Changes.Count>0)marts.Apply(preview);return Count(preview.Changes);
    }
}
