using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Original USUM MartEditor7UU presets, staged until preview and atomic save.</summary>
public sealed partial class GameMartService
{
    private static readonly int[] ItemPool = [68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,99,100,101,102,103,104,105,106,107,108,109,110,111,112,116,117,118,119,135,136,137,213,214,215,217,218,219,220,221,222,223,224,225,226,227,228,229,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255,256,257,258,259,260,261,262,263,264,265,266,267,268,269,270,271,272,273,274,275,276,277,278,279,280,281,282,283,284,285,286,287,288,289,290,291,292,293,294,295,296,297,298,299,300,301,302,303,304,305,306,307,308,309,310,311,312,313,314,315,316,317,318,319,320,321,322,323,324,325,326,327,499,534,535,537,538,539,540,541,542,543,544,545,546,547,548,549,550,551,552,553,554,555,556,557,558,559,560,561,562,563,564,571,572,573,580,581,582,583,584,585,586,587,588,589,590,639,640,644,645,646,647,648,649,650,656,657,658,659,660,661,662,663,664,665,666,667,668,669,670,671,672,673,674,675,676,677,678,679,680,681,682,683,684,685,699,704,710,711,715,752,753,754,755,756,757,758,759,760,761,762,763,764,767,768,769,770,795,796,844,849,853,854,855,856,879,880,881,882,883,884,904,905,906,907,908,909,910,911,912,913,914,915,916,917,918,919,920,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,492,493,494,495,496,497,498,576,851,55,56,57,58,59,60,61,62,63,64,577,846,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,65,66,67,134,504,565,566,567,568,569,570,591,708,709,852,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,492,493,494,495,496,497,498,499,576];
    private static readonly HashSet<int> ProtectedItems = [328,329,330,331,332,333,334,335,336,337,338,339,340,341,342,343,344,345,346,347,348,349,350,351,352,353,354,355,356,357,358,359,360,361,362,363,364,365,366,367,368,369,370,371,372,373,374,375,376,377,378,379,380,381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,401,402,403,404,405,406,407,408,409,410,411,412,413,414,415,416,417,418,419,420,421,422,423,424,425,426,427,618,619,620,690,691,692,693,694,701,737];
    private static readonly HashSet<int> SpeedrunXItems = [55,56,57,58,59,60,61,62];
    private static readonly string[][] Vitamins =
    [
        ["HP Up","Más PS","Mas PS"],["Protein","Proteína","Proteina"],
        ["Iron","Hierro"],["Calcium","Calcio"],["Zinc"],["Carbos","Carburante"]
    ];
    private static readonly string[][] Wings =
    [
        ["Health Wing","Pluma Vigor"],["Muscle Wing","Pluma Músculo","Pluma Musculo"],
        ["Resist Wing","Pluma Aguante"],["Genius Wing","Pluma Intelecto"],
        ["Clever Wing","Pluma Mente"],["Swift Wing","Pluma Ímpetu","Pluma Impetu"]
    ];
    internal const int AbilityCapsuleUSUM = 645;
    private bool _setCandyPrice, _setKeyPrice, _enableVitamin252, _banCapsule;
    private int[] _vitaminIds = [];
    private readonly List<string> _presets = [];
    private bool HasItemPatches => _setCandyPrice || _setKeyPrice || _enableVitamin252;

    private static string NormalizeName(string? value) =>
        new string((value??"").Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            .Select(char.ToLowerInvariant).ToArray());
    private int? TryItemId(params string[] aliases)
    {
        var keys=aliases.Select(NormalizeName).ToHashSet(StringComparer.Ordinal);
        for(int i=1;i<_project.ItemNames.Count;i++)
            if(keys.Contains(NormalizeName(_project.ItemNames[i])))return i;
        return null;
    }
    private int RequireItem(params string[] aliases)=>TryItemId(aliases)??
        throw new InvalidDataException("No se encuentra el objeto "+aliases[0]+
        " en el idioma actual. Cambiá el idioma de datos a inglés o español y recargá el proyecto.");

    public int RandomizeShops(bool bp,bool specialsOnly=false,bool protectX=true)
    {
        int[] pool=ItemPool.Where(id=>id>0&&id<_project.ItemNames.Count&&
            (!_banCapsule||id!=AbilityCapsuleUSUM)).ToArray();
        if(pool.Length==0)throw new InvalidDataException("No hay objetos válidos para randomizar.");
        int count=RandomizeSlots(RegularShops,BpShops,pool,
            new Random(RandomNumberGenerator.GetInt32(int.MaxValue)),bp,specialsOnly,protectX);
        _presets.Add(bp?"marts.randomize-bp":specialsOnly?"marts.randomize:special-only":"marts.randomize");
        return count;
    }

    // Separated from GameProjectSession so the exact Mount Lanakila/X-item
    // scenario can be regression-tested without a real Shop.cro.
    internal static int RandomizeSlots(IReadOnlyList<MartShop> regular,IReadOnlyList<MartShop> bpShops,
        IReadOnlyList<int> allowed,Random rng,bool bp,bool specialsOnly,bool protectX)
    {
        int[] pool=allowed.ToArray();
        if(pool.Length==0)throw new InvalidDataException("El pool de objetos está vacío.");
        void Shuffle()
        {
            for(int i=pool.Length-1;i>0;i--)
            {int j=rng.Next(i+1);(pool[i],pool[j])=(pool[j],pool[i]);}
        }
        Shuffle();int cursor=0,count=0;
        IEnumerable<MartShop> shops=bp?bpShops:regular.Skip(specialsOnly?8:0);
        foreach(var shop in shops)
            foreach(var row in shop.Rows)
            {
                int before=row.ItemId;
                if(!bp&&(ProtectedItems.Contains(before)||(protectX&&SpeedrunXItems.Contains(before))))continue;
                if(cursor==pool.Length){Shuffle();cursor=0;}
                int next=pool[cursor++];
                // Disabling protection must actually change a store made solely
                // of X items. Never replace an X item with another X item.
                if(next==before||(!protectX&&SpeedrunXItems.Contains(before)&&SpeedrunXItems.Contains(next)))
                {
                    int search=0;
                    while(search++<pool.Length &&
                        (next==before||(!protectX&&SpeedrunXItems.Contains(before)&&SpeedrunXItems.Contains(next))))
                    {
                        if(cursor==pool.Length){Shuffle();cursor=0;}
                        next=pool[cursor++];
                    }
                    if(next==before||(!protectX&&SpeedrunXItems.Contains(before)&&SpeedrunXItems.Contains(next)))
                        throw new InvalidDataException("No se encontró reemplazo válido para objeto X.");
                }
                row.ItemId=next;count++;
            }
        return count;
    }
    public int AddRareCandies()
    {
        int candy=RequireItem("Rare Candy","Caramelo Raro");
        var missing=RegularShops.Take(8).Where(s=>s.Rows.All(r=>r.ItemId!=candy)).ToArray();
        if(missing.Any(s=>s.Rows.Count>=Gen7ExpandedMartTable.MaximumSlotsPerMart))
            throw new InvalidOperationException("Alguna tienda de progreso ya alcanzó el máximo de ranuras.");
        foreach(var shop in missing)shop.Rows.Add(new MartSlotRow(shop.Rows.Count+1,candy));
        _setCandyPrice=true;_presets.Add("marts.add-rare-candies:expanded");
        return missing.Length;
    }
    public int AddVitamins252()
    {
        int[] vitamins=Vitamins.Select(RequireItem).ToArray();
        if(vitamins.Distinct().Count()!=6)throw new InvalidDataException("Las seis vitaminas deben ser distintas.");
        int[] wings=Wings.Select(a=>TryItemId(a)??0).ToArray();
        int candy=RequireItem("Rare Candy","Caramelo Raro");
        int heart=RequireItem("Heart Scale","Escama Corazón","Escama Corazon");
        int key=RequireItem("Key Stone","Piedra Activadora","Piedra Llave");
        // Compute all inventories and validate records before altering any UI data.
        var proposed=new List<int[]>();
        for(int mart=0;mart<8;mart++)
        {
            var items=RegularShops[mart].Rows.Select(r=>r.ItemId).ToList();
            for(int v=0;v<6;v++)
            {
                if(wings[v]==0||wings[v]==vitamins[v])continue;
                while(items.Contains(wings[v]))
                {
                    int at=items.IndexOf(wings[v]);
                    if(items.Contains(vitamins[v]))items.RemoveAt(at); else items[at]=vitamins[v];
                }
            }
            var required=vitamins.Concat([candy])
                .Concat(mart>=3?[heart]:Array.Empty<int>())
                .Concat(mart>=5?[key]:Array.Empty<int>()).Distinct();
            foreach(int id in required)if(!items.Contains(id))items.Add(id);
            if(items.Count>Gen7ExpandedMartTable.MaximumSlotsPerMart)
                throw new InvalidDataException("Ranuras insuficientes en "+RegularShops[mart].Name);
            proposed.Add(items.ToArray());
        }
        ValidateVitamins(vitamins);
        int changes=0;
        for(int mart=0;mart<8;mart++)
        {
            var shop=RegularShops[mart];
            if(shop.Rows.Select(r=>r.ItemId).SequenceEqual(proposed[mart]))continue;
            changes+=Math.Max(shop.Rows.Count,proposed[mart].Length);
            shop.Rows.Clear();
            for(int slot=0;slot<proposed[mart].Length;slot++)
                shop.Rows.Add(new MartSlotRow(slot+1,proposed[mart][slot]));
        }
        _vitaminIds=vitamins;_enableVitamin252=true;_setCandyPrice=true;_setKeyPrice=true;
        _presets.Add("marts.add-ev-items:vitamins-252");
        return changes;
    }
    public int BanAbilityCapsule()
    {
        int capsule=AbilityCapsuleUSUM;
        if(capsule>=_project.ItemNames.Count)
            throw new InvalidDataException("La tabla de objetos USUM no contiene el ID 645.");
        int[] pool=ItemPool.Where(id=>id>0&&id<_project.ItemNames.Count&&id!=capsule&&!ProtectedItems.Contains(id)).Distinct().ToArray();
        if(pool.Length==0)throw new InvalidOperationException("No hay reemplazos válidos para Cápsula Habilidad.");
        var rng=new Random(RandomNumberGenerator.GetInt32(int.MaxValue));int changed=0;
        foreach(var shop in RegularShops.Concat(BpShops))
            foreach(var row in shop.Rows)
                if(row.ItemId==capsule){row.ItemId=pool[rng.Next(pool.Length)];changed++;}
        _banCapsule=true;_presets.Add("marts.ban-ability-capsule");
        return changed;
    }
    private void ValidateVitamins(IReadOnlyList<int> ids,byte[][]? records=null)
    {
        var config=_project.GameConfiguration;
        var reference=config.GetGARCReference("item");
        records??=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS,reference.Reference))).Files;
        if(ids.Count!=6||ids.Distinct().Count()!=6)
            throw new InvalidDataException("Deben existir seis vitaminas únicas.");
        for(int i=0;i<6;i++)
        {
            int id=ids[i];
            if(id<=0||id>=records.Length||records[id].Length<0x24)
                throw new InvalidDataException("Registro incompatible para "+Vitamins[i][0]);
            var item=new Item(records[id]);
            int gain=i switch {0=>item.EVHP,1=>item.EVATK,2=>item.EVDEF,
                3=>item.EVSPA,4=>item.EVSPD,5=>item.EVSPE,_=>0};
            if(gain<=0)throw new InvalidDataException("La vitamina no incrementa su EV correspondiente: "+Vitamins[i][0]);
        }
    }
    private (byte[]? Original,byte[]? Output,IReadOnlyList<BatchChange> Changes) PrepareItemPatches()
    {
        if(!HasItemPatches)return(null,null,[]);
        var config=_project.GameConfiguration;var reference=config.GetGARCReference("item");
        string path=Path.Combine(config.RomFS,reference.Reference);
        byte[] source=File.ReadAllBytes(path);
        byte[][] records=new GARC.MemGARC(source).Files;
        var changes=new List<BatchChange>();
        void Price(int id,int target,string name)
        {
            if(id<=0||id>=records.Length)throw new InvalidDataException("Objeto inválido: "+name);
            var item=new Item(records[id]);int old=item.BuyPrice;
            if(old==target)return;
            item.BuyPrice=target;records[id]=item.Write();
            changes.Add(new BatchChange(id,name,"Precio de compra",old.ToString(),target.ToString()));
        }
        if(_enableVitamin252)
        {
            ValidateVitamins(_vitaminIds,records);
            for(int i=0;i<6;i++)
            {
                int id=_vitaminIds[i];var item=new Item(records[id]);
                if((item.FunctionFlags1&ItemFlags2.AddEVAbove100)==0)
                {
                    item.FunctionFlags1|=ItemFlags2.AddEVAbove100;
                    records[id]=item.Write();
                    changes.Add(new BatchChange(id,Vitamins[i][0],"Límite EV","100","252"));
                }
                Price(id,200,Vitamins[i][0]);
            }
        }
        if(_setCandyPrice)Price(RequireItem("Rare Candy","Caramelo Raro"),10,"Caramelo Raro");
        if(_setKeyPrice)Price(RequireItem("Key Stone","Piedra Activadora","Piedra Llave"),10,"Piedra Activadora");
        if(changes.Count==0)return(null,null,[]);
        byte[] output=GarcTransaction.Build(source,reference,records);
        return(source,output,changes);
    }
}
