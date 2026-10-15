using System.IO;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
public sealed partial class GameMart6Service
{
    private readonly Gen6MartTable? _structure;
    internal string? ExpansionReason {get;}
    internal bool SupportsExpansion=>_structure is not null;
    internal int Capacity=>_structure?.Capacity??_counts.Sum();
    internal static readonly string[] PresetKeys=["Randomize","Candies","EVItems","BanCapsule"];
    internal int[][] ReadAllItems()=>Entries.Select(e=>Read(e.Id).SelectMany(g=>g.Fields).Select(f=>f.Value).ToArray()).ToArray();
    internal EditorProposal PrepareDraft(IReadOnlyList<int[]> inventories,IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);byte[] source=Source();var before=ReadAllItems();var rows=inventories.Select(r=>r.ToList()).ToArray();
        if(rows.Length!=Entries.Count||rows.Any(r=>r.Count is <1 or >64||r.Any(i=>i<0||i>=_items.Count)))throw new InvalidDataException("Inventario Gen6 inválido.");
        int[] ids=scope.Distinct().Order().ToArray();if(ids.Length==0||ids.Any(i=>i<1||i>rows.Length))throw new ArgumentException("Alcance de tiendas inválido.");
        var rng=new Random(seed);int[] pool=ProgressiveLegacy.Randomizer.GetRandomItemList(_project.GameConfiguration).Where(i=>i>0&&i<_items.Count&&(options["BanCapsule"]==0||i!=645)).Distinct().ToArray();
        if((options["Randomize"]==1||options["BanCapsule"]==1)&&pool.Length==0)throw new InvalidDataException("No hay reemplazos legales.");
        if(options["Randomize"]==1)foreach(int id in ids.Where(id=>options["SpecialOnly"]==0||id>=11))for(int n=0;n<rows[id-1].Count;n++)
            if(!Protected.Contains(rows[id-1][n])&&!(options["KeepX"]==1&&rows[id-1][n] is >=55 and <=62))rows[id-1][n]=pool[rng.Next(pool.Length)];
        int regular=_project.GameConfiguration.ORAS?10:9;var prices=new HashSet<int>();
        if(options["Candies"]==1)foreach(int id in ids.Where(id=>id<=regular)){if(!rows[id-1].Contains(50))rows[id-1].Add(50);prices.Add(50);}
        // Wings have the native Gen6 EV effect up to 252; no USUM-only vitamin flag is installed.
        if(options["EVItems"]==1)foreach(int id in ids.Where(id=>id<=regular))foreach(int wing in Enumerable.Range(565,6))
        {if(wing>=_items.Count)throw new InvalidDataException("Faltan las seis plumas EV Gen6.");if(!rows[id-1].Contains(wing))rows[id-1].Add(wing);prices.Add(wing);}
        if(options["BanCapsule"]==1)foreach(int id in ids)for(int n=0;n<rows[id-1].Count;n++)if(rows[id-1][n]==645)rows[id-1][n]=pool[rng.Next(pool.Length)];
        byte[] output;
        if(_structure is not null)output=_structure.Build(source,rows.Select(r=>r.ToArray()).ToArray());
        else
        {
            if(rows.Where((r,i)=>r.Count!=_counts[i]).Any())throw new InvalidDataException(ExpansionReason??"No se puede ampliar este code.bin.");
            output=(byte[])source.Clone();for(int i=0;i<rows.Length;i++)for(int n=0;n<rows[i].Count;n++)BitConverter.GetBytes((ushort)rows[i][n]).CopyTo(output,_offsets[i]+n*2);
        }
        var changes=new List<BatchChange>();
        for(int i=0;i<rows.Length;i++)for(int n=0;n<Math.Max(before[i].Length,rows[i].Count);n++)
        {string old=n<before[i].Length?before[i][n].ToString():"—",next=n<rows[i].Count?rows[i][n].ToString():"—";if(old!=next)changes.Add(new(i+1,Entries[i].Name,$"Ranura {n+1}",old,next));}
        var files=new List<FileChange>{new(_codePath,source,output)};
        if(prices.Count>0)
        {
            if(_itemHash is null)throw new InvalidDataException("Falta el GARC de objetos Gen6.");byte[] raw=GarcTransaction.ReadUnchanged(_itemPath,_itemHash);var records=new GARC.MemGARC(raw).Files;
            foreach(int item in prices)
            {if(item>=records.Length||records[item].Length<2)throw new InvalidDataException("Registro de precio incompleto.");int old=BitConverter.ToUInt16(records[item],0)*10;records[item]=(byte[])records[item].Clone();BitConverter.GetBytes((ushort)1).CopyTo(records[item],0);if(old!=10)changes.Add(new(item,_items[item].Name,"Precio",old.ToString(),"10"));}
            files.Add(new(_itemPath,raw,GarcTransaction.Build(raw,_itemRef,records)));
        }
        return new(_owner,"Tiendas Gen6 y presets",seed,files,changes,options.ToDictionary(p=>p.Key,p=>p.Value.ToString()));
    }
}
