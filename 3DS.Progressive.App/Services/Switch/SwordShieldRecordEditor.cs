using System.Globalization;
using System.Runtime.CompilerServices;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;

/// <summary>Adapts NX storage to the exact editor contract used by CTR. The shared view never writes ROM files.</summary>
internal sealed partial class SwordShieldRecordEditor:IProjectEditor,IOptionTemplateProjectEditor,IOptionTemplatePresentation,ITrainerEditorPresentation,ITableEditorPresentation,IEconomyEditor,ITrainerTableMetadata,IContextualRecordEditor
{
    public string ImportOptionsLabel=>"Cargar template de "+Title.ToLowerInvariant();
    public string ExportOptionsLabel=>"Exportar template de "+Title.ToLowerInvariant();
    private readonly SwordShieldProject _project;
    private readonly SwordShieldOptionsStore _optionStore;
    public void ImportOptions(string path)=>_optionStore.Import(path,RandomOptions);
    public void ExportOptions(string path,IReadOnlyDictionary<string,int> values)=>_optionStore.Export(path,values,RandomOptions);
    public string CaptureOptions(IReadOnlyDictionary<string,int> values)=>_optionStore.Capture(values,RandomOptions);
    public void RestoreOptions(string json)=>_optionStore.Restore(json,RandomOptions);
    private readonly SwordShieldDataSection _section;
    public bool UsesRecordTable=>_section is SwordShieldDataSection.Learnsets or SwordShieldDataSection.Evolutions or SwordShieldDataSection.Machines or SwordShieldDataSection.Shops or SwordShieldDataSection.Items;
    public bool UsesTrainerLayout=>_section==SwordShieldDataSection.TrainerTeams;
    private readonly ConditionalWeakTable<EditorProposal,SwordShieldGameplayPreview> _trainerProposals=new();
    private readonly Guid _owner=Guid.NewGuid();
    private readonly ConditionalWeakTable<EditorProposal,SwordShieldDataPreview> _proposals=new();
    private IReadOnlyList<SwordShieldDataCell> _cells;
    private string _revision;
    internal event Action? Changed;
    public string Title=>UsesTrainerLayout?"Entrenadores":SwitchDataEditorView.TitleFor(_section);
    public IReadOnlyList<RecordEntry> Entries{get;private set;}
    internal SwordShieldRecordEditor(SwordShieldProject project,SwordShieldDataSection section)
    {
        _optionStore=new(section.ToString());_project=project;_section=section;_cells=project.ReadData(section);_revision=project.RevisionHash;Entries=MakeEntries();
    }
    private IReadOnlyList<RecordEntry> MakeEntries()=>_cells.GroupBy(c=>c.Entry).Select(g=>new RecordEntry(g.Key,_section switch{SwordShieldDataSection.Moves=>_project.MoveName(g.Key),SwordShieldDataSection.Items=>_project.ItemName(g.Key),SwordShieldDataSection.TrainerTeams=>_project.TrainerName(g.Key),SwordShieldDataSection.Learnsets or SwordShieldDataSection.Machines or SwordShieldDataSection.Evolutions=>_project.Entries.FirstOrDefault(e=>e.Index==g.Key)?.Name??"Entrada "+g.Key,_=>Title+" "+g.Key})).ToArray();
    private void Refresh(){_cells=_project.ReadData(_section);_revision=_project.RevisionHash;Entries=MakeEntries();}
    private string UiKey(SwordShieldDataCell cell)
    {
        if(!UsesTrainerLayout)return cell.Key;
        string field=cell.Field switch {"Especie"=>"Species","Forma"=>"Form","Nivel"=>"Level","Objeto equipado"=>"Item",_=>cell.Field.StartsWith("Movimiento ")?"Move"+cell.Field.Split(' ').Last():cell.Field.StartsWith("IV ")?"IV"+cell.Mask:cell.Field.StartsWith("EV ")?"EV"+cell.Offset:cell.Field};
        return "P"+cell.Slot+"."+field;
    }
    private static readonly IReadOnlyList<MoveChoice> EvolutionMethods=new string[] {"Ninguna","Amistad","Amistad de día","Amistad de noche","Subir de nivel","Intercambio","Intercambio con objeto","Intercambio Shelmet/Karrablast","Usar objeto","Ataque > Defensa","Ataque = Defensa","Ataque < Defensa","Azar < 5","Azar ≥ 5","Ninjask","Shedinja","Belleza","Objeto · macho","Objeto · hembra","Objeto equipado de día","Objeto equipado de noche","Conocer movimiento","Especie en el equipo","Nivel · macho","Nivel · hembra","Zona eléctrica","Bosque","Zona fría","Consola invertida","Afecto y tipo de movimiento","Tipo Siniestro en el equipo","Lluvia","Nivel de día","Nivel de noche","Hembra · forma 1","Sin uso","Versión del juego","De día · versión","De noche · versión","Monte","Atardecer","Ultraespacio · nivel","Ultraespacio · objeto","Golpes críticos en combate","PS perdidos en combate","Girar","Naturaleza · forma aguda","Naturaleza · forma grave","Torre de las Sombras","Torre de las Aguas"}.Select((name,id)=>new MoveChoice(id,name)).ToArray();
    private IReadOnlyList<MoveChoice>? Choices(SwordShieldDataCell cell)=>cell.Field switch
    {
        "Método"=>EvolutionMethods,
        "Tipo"=>new[]{"Normal","Lucha","Volador","Veneno","Tierra","Roca","Bicho","Fantasma","Acero","Fuego","Agua","Planta","Eléctrico","Psíquico","Hielo","Dragón","Siniestro","Hada"}.Select((n,i)=>new MoveChoice(i,n)).ToArray(),
        "Categoría"=>new[]{new MoveChoice(0,"Estado"),new(1,"Físico"),new(2,"Especial")},
        "Especie"=>_project.Entries.Where(e=>e.Form==0).Select(e=>new MoveChoice(e.Species,e.Name)).ToArray(),
        "Objeto" or "Objeto equipado"=>Enumerable.Range(cell.Min,cell.Max-cell.Min+1).Select(i=>new MoveChoice(i,_project.ItemName(i))).ToArray(),
        _ when cell.Field.StartsWith("Movimiento",StringComparison.Ordinal)=>Enumerable.Range(cell.Min,cell.Max-cell.Min+1).Select(i=>new MoveChoice(i,_project.MoveName(i))).ToArray(),
        _=>null,
    };
    public IReadOnlyList<EditorGroup> Read(int id)=>ReadContext(id,new Dictionary<string,int>());
    public IReadOnlyList<EditorGroup> ReadContext(int id,IReadOnlyDictionary<string,int> values)
    {
        if(_revision!=_project.RevisionHash)Refresh();
        var rows=_cells.Where(c=>c.Entry==id).OrderBy(c=>!UsesTrainerLayout?0:c.Field switch{"Especie"=>0,"Nivel"=>1,"Forma"=>2,"Naturaleza"=>3,"Objeto equipado"=>4,_=>5}).ToArray();if(rows.Length==0)throw new ArgumentException("Entrada desconocida.");
        var groups=rows.GroupBy(c=>c.Slot).Select(g=>new EditorGroup(_section==SwordShieldDataSection.TrainerTeams?"Pokémon "+(g.Key+1):_section==SwordShieldDataSection.Shops?"E"+(g.Key/10000)+" · #"+(g.Key%10000+1):"#"+(g.Key+1),g.Select(c=>new EditorField(UiKey(c),c.Field,values.GetValueOrDefault(UiKey(c),c.Value),c.Min,c.Max,ContextChoices(c,values))).ToArray(),g.Key==rows[0].Slot)).ToArray();
        return groups;
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        if(_revision!=_project.RevisionHash)throw new InvalidOperationException("La sesión cambió. Vuelve a seleccionar la entrada antes de guardar.");
        var rows=_cells.Where(c=>c.Entry==id).ToArray();EditorValues.Validate(Read(id),values);
        var edits=rows.Where(c=>values[UiKey(c)]!=c.Value).Select(c=>new SwordShieldDataEdit(c.Key,values[UiKey(c)])).ToArray();_project.ApplyData(_project.PreviewData(_section,edits));Refresh();Changed?.Invoke();
    }
    private static EditorField Flag(string key,string label,bool initial)=>new(key,label,initial?1:0,0,1);
    public IReadOnlyList<EditorGroup> RandomOptions=>_optionStore.Apply(BaseRandomOptions);
    private IReadOnlyList<EditorGroup> BaseRandomOptions=>UsesTrainerLayout?TrainerOptions():_section switch
    {
        SwordShieldDataSection.Moves=>new[]{new EditorGroup("Movimientos",new[]{Flag("Types","Randomizar tipos",false),Flag("Categories","Randomizar categorías",false)})},
        SwordShieldDataSection.Statics or SwordShieldDataSection.Gifts=>new[]{new EditorGroup(Title,new[]{Flag("Enabled","Randomizar especies",false),Flag("SimilarBST","Mantener BST similar",false)}),new EditorGroup("Iniciales",_section==SwordShieldDataSection.Gifts?new[]{Flag("ThreeStage","Iniciales con tres etapas evolutivas",false)}:Array.Empty<EditorField>())},
        SwordShieldDataSection.Shops=>new[]{new EditorGroup("Tiendas",new[]{Flag("Enabled","Randomizar tiendas",false),Flag("SpecialShopsOnly","Solo randomizar tiendas especiales",true)})},
        SwordShieldDataSection.Machines=>new[]{new EditorGroup("Compatibilidad",new[]{Flag("Enabled","Randomizar compatibilidad",false),new EditorField("Percent","Probabilidad (%)",50,0,100)})},
        SwordShieldDataSection.Evolutions=>new[]{new EditorGroup("Evoluciones",new[]{Flag("Enabled","Randomizar destinos",false),Flag("Trade","Eliminar intercambios · nivel 37",false)})},
        _=>new[]{new EditorGroup(Title,new[]{Flag("Enabled","Randomizar "+Title.ToLowerInvariant(),false)})},
    };
    internal bool HasTradeEvolutionAction=>_section==SwordShieldDataSection.Evolutions;
    internal EditorProposal PreviewTradeRemoval()
    {
        if(!HasTradeEvolutionAction)throw new InvalidOperationException("Se requiere el editor de evoluciones.");
        var options=EditorValues.Flatten(RandomOptions);options["Enabled"]=0;options["Trade"]=1;return Preview(options,Entries.Select(e=>e.Id).ToArray());
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        if(UsesTrainerLayout)return PreviewTrainers(options,scope);
        EditorValues.Validate(RandomOptions,options);uint seed=SwitchSeed.New32()&int.MaxValue;bool enabled=options.GetValueOrDefault("Enabled")==1||options.GetValueOrDefault("Trade")==1||options.GetValueOrDefault("Types")==1||options.GetValueOrDefault("Categories")==1;
        var config=new SwordShieldDataOptions(seed,options.GetValueOrDefault("Trade")==1,options.GetValueOrDefault("Percent",50),options.GetValueOrDefault("Types")==1,options.GetValueOrDefault("Categories")==1,options.GetValueOrDefault("SimilarBST")==1,options.GetValueOrDefault("ThreeStage")==1,options.GetValueOrDefault("SpecialShopsOnly",1)==1);
        var data=enabled?_project.PreviewData(_section,null,config,scope):_project.PreviewData(_section,Array.Empty<SwordShieldDataEdit>());
        var changes=data.Result.Changes.Select(c=>new BatchChange(int.Parse(c.Location.Split('/')[0].Trim(),CultureInfo.InvariantCulture),c.Location,c.Field,c.Before.ToString(CultureInfo.InvariantCulture),c.After.ToString(CultureInfo.InvariantCulture))).ToArray();
        var proposal=new EditorProposal(_owner,"Randomizar "+Title,unchecked((int)seed),Array.Empty<FileChange>(),changes,new Dictionary<string,string>{{"Platform","SWSH"},{"Seed",seed.ToString(CultureInfo.InvariantCulture)}});_proposals.Add(proposal,data);return proposal;
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner==_owner&&_trainerProposals.TryGetValue(proposal,out var trainer)){_project.ApplyGameplay(trainer);_trainerProposals.Remove(proposal);Refresh();Changed?.Invoke();return;}
        if(proposal.Owner!=_owner||!_proposals.TryGetValue(proposal,out var data))throw new InvalidOperationException("Propuesta ajena a este editor.");
        _project.ApplyData(data);_proposals.Remove(proposal);Refresh();Changed?.Invoke();
    }
}
