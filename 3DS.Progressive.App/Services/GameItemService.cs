using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Original pk3DS Item format, preserving undocumented bytes and using transactional history.</summary>
public sealed class GameItemService : ArchiveProjectEditor
{
    public GameItemService(GameProjectSession project):base(project,"item")
    {
        var records=Records()["item"];
        var names=Config.GetText(TextName.ItemNames);
        Entries=Enumerable.Range(1,Math.Min(records.Length,names.Length)-1)
            .Select(id=>new RecordEntry(id,string.IsNullOrWhiteSpace(names[id])?$"Objeto #{id:000}":names[id])).ToArray();
    }
    public override string Title=>"Objetos";
    public override IReadOnlyList<EditorGroup> RandomOptions=>[
        new("Economía",[
            Field("PriceScale","Multiplicador de precios (%)",100,10,500),
            Field("OnlyPaid","Modificar solo objetos con precio mayor que cero",1,0,1)
        ])
    ];
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        var item=new Item(records["item"][id]);
        return [
            new("Economía y bolsas",[
                Field("Price","Precio de compra",item.BuyPrice,0,655350),
                Field("Pocket","Bolsillo / pocket",item.PocketField,0,15),
                Field("PocketBattle","Bolsillo de batalla",(int)item.PocketBattle,0,31),
                Field("Sort","Orden",item.SortIndex,0,255)
            ]),
            new("Efectos en combate",[
                Field("HeldEffect","Efecto equipado",item.HeldEffect,0,255),
                Field("HeldArg","Argumento efecto",item.HeldArgument,0,255),
                Field("Fling","Potencia Lanzamiento",item.FlingPower,0,255),
                Field("GiftPower","Potencia Don Natural",item.NaturalGiftPower,0,255),
                Field("EffectBattle","Efecto de uso en combate",item.EffectBattle,0,255),
                Field("EffectField","Efecto de uso fuera de combate",item.EffectField,0,255),
                Field("UseConsume","Se consume al usar",item.UseConsume?1:0,0,1),
                Field("UseKeep","Conservar tras uso",item.UseKeep?1:0,0,1)
            ],false),
            new("EV y otros atributos",[
                Field("EVHP","EV PS",item.EVHP,-128,127),
                Field("EVATK","EV Ataque",item.EVATK,-128,127),
                Field("EVDEF","EV Defensa",item.EVDEF,-128,127),
                Field("EVSPA","EV At. Especial",item.EVSPA,-128,127),
                Field("EVSPD","EV Def. Especial",item.EVSPD,-128,127),
                Field("EVSPE","EV Velocidad",item.EVSPE,-128,127),
                Field("BoostATK","Bonus ataque",item.BoostATK,0,15),
                Field("BoostDEF","Bonus defensa",item.BoostDEF,0,15),
                Field("BoostSPA","Bonus At. Esp.",item.BoostSPA,0,15),
                Field("BoostSPD","Bonus Def. Esp.",item.BoostSPD,0,15),
                Field("BoostSPE","Bonus velocidad",item.BoostSPE,0,15),
                Field("LevelUp","Subir nivel",item.LevelUp?1:0,0,1),
                Field("EvoStone","Piedra evolutiva",item.EvoStone?1:0,0,1)
            ],false)
        ];
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> values,Dictionary<string,byte[][]> records)
    {
        byte[] original=records["item"][id];
        var item=new Item(original);
        if(values["Price"]%10!=0)throw new ArgumentException("Los precios de objetos deben ser múltiplos de 10.");
        // Set fields only when changed. Some vanilla setters do not preserve unused bit flags.
        if(item.BuyPrice!=values["Price"])item.BuyPrice=values["Price"];
        if(item.PocketField!=values["Pocket"])item.PocketField=values["Pocket"];
        if((int)item.PocketBattle!=values["PocketBattle"])item.PocketBattle=(BattlePocket)values["PocketBattle"];
        if(item.SortIndex!=values["Sort"])item.SortIndex=(byte)values["Sort"];
        if(item.HeldEffect!=values["HeldEffect"])item.HeldEffect=(byte)values["HeldEffect"];
        if(item.HeldArgument!=values["HeldArg"])item.HeldArgument=(byte)values["HeldArg"];
        if(item.FlingPower!=values["Fling"])item.FlingPower=(byte)values["Fling"];
        if(item.NaturalGiftPower!=values["GiftPower"])item.NaturalGiftPower=(byte)values["GiftPower"];
        if(item.EffectBattle!=values["EffectBattle"])item.EffectBattle=(byte)values["EffectBattle"];
        if(item.EffectField!=values["EffectField"])item.EffectField=(byte)values["EffectField"];
        if(item.UseConsume!=(values["UseConsume"]==1))item.UseConsume=values["UseConsume"]==1;
        if(item.UseKeep!=(values["UseKeep"]==1))item.UseKeep=values["UseKeep"]==1;
        if(item.EVHP!=values["EVHP"])item.EVHP=(sbyte)values["EVHP"];
        if(item.EVATK!=values["EVATK"])item.EVATK=(sbyte)values["EVATK"];
        if(item.EVDEF!=values["EVDEF"])item.EVDEF=(sbyte)values["EVDEF"];
        if(item.EVSPA!=values["EVSPA"])item.EVSPA=(sbyte)values["EVSPA"];
        if(item.EVSPD!=values["EVSPD"])item.EVSPD=(sbyte)values["EVSPD"];
        if(item.EVSPE!=values["EVSPE"])item.EVSPE=(sbyte)values["EVSPE"];
        if(item.BoostATK!=values["BoostATK"])item.BoostATK=values["BoostATK"];
        if(item.BoostDEF!=values["BoostDEF"])item.BoostDEF=values["BoostDEF"];
        if(item.BoostSPA!=values["BoostSPA"])item.BoostSPA=values["BoostSPA"];
        if(item.BoostSPD!=values["BoostSPD"])item.BoostSPD=values["BoostSPD"];
        if(item.BoostSPE!=values["BoostSPE"])item.BoostSPE=values["BoostSPE"];
        if(item.LevelUp!=(values["LevelUp"]==1))item.LevelUp=values["LevelUp"]==1;
        if(item.EvoStone!=(values["EvoStone"]==1))item.EvoStone=values["EvoStone"]==1;
        byte[] written=item.Write();
        if(written.Length!=original.Length)throw new InvalidDataException("La longitud del objeto no coincide. No se guardó.");
        records["item"][id]=written;
    }
    public IReadOnlyList<ItemPriceRow> PriceRows()
    {
        var records=Records();
        return Entries.Select(entry=>
        {
            int current=new Item(records["item"][entry.Id]).BuyPrice;
            int target=Config.Generation==7?GameEconomyDefaults.TargetPrice(entry.Id)??current:current;
            return new ItemPriceRow
            {
                Id=entry.Id,Name=entry.Name,CurrentBuy=current,TargetBuy=target,
                Included=Config.Generation==7&&GameEconomyDefaults.TargetPrice(entry.Id).HasValue,
                Source=Config.Generation==7&&GameEconomyDefaults.TargetPrice(entry.Id).HasValue
                    ?GameEconomyDefaults.Source(entry.Id):"Actual"
            };
        }).ToArray();
    }
    public EditorProposal PreviewEconomy(IReadOnlyList<ItemPriceRow> rows)
    {
        var records=Records();var changes=new List<BatchChange>();
        if(rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)
            throw new ArgumentException("La tabla contiene elementos duplicados.");
        foreach(var row in rows.Where(r=>r.Included))
        {
            if(!Entries.Any(e=>e.Id==row.Id))
                throw new ArgumentException($"Objeto #{row.Id} desconocido.");
            if(row.TargetBuy<0||row.TargetBuy>655350||row.TargetBuy%10!=0)
                throw new ArgumentException($"Precio inválido de {row.Name}. Debe ser múltiplo de 10.");
            var before=ReadRecord(row.Id,records);
            var values=EditorValues.Flatten(before);
            values["Price"]=row.TargetBuy;
            EditorValues.Validate(before,values);
            WriteRecord(row.Id,values,records);
            changes.AddRange(EditorValues.Diff(row.Id,row.Name,before,ReadRecord(row.Id,records)));
        }
        return new EditorProposal(ProposalOwner,"Aplicar economía",0,Build(records),changes,
            new Dictionary<string,string>{{"Scope",string.Join(",",rows.Where(r=>r.Included).Select(r=>r.Id))},
                {"Preset","UPR-ZX Gen7 + Progressive"}});
    }

    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random)
    {
        var values=EditorValues.Flatten(ReadRecord(id,records));
        if(values["Price"]>0||options["OnlyPaid"]==0)
            values["Price"]=Math.Clamp((int)Math.Round(values["Price"]*options["PriceScale"]/100.0/10)*10,0,655350);
        return values;
    }
}

public sealed class ItemPriceRow : System.ComponentModel.INotifyPropertyChanged
{
    public int Id {get;init;}
    public string Name {get;init;}="";
    public int CurrentBuy {get;init;}
    public string Source {get;init;}="";
    private int _targetBuy;
    public int TargetBuy{get=>_targetBuy;set{_targetBuy=value;PropertyChanged?.Invoke(this,new(nameof(TargetBuy)));PropertyChanged?.Invoke(this,new(nameof(TargetSell)));}}
    public int TargetSell=>TargetBuy/2;
    private bool _included;
    public bool Included{get=>_included;set{_included=value;PropertyChanged?.Invoke(this,new(nameof(Included)));}}
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
