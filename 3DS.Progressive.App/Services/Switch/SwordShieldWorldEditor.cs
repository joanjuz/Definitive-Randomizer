using System.Globalization;
using System.Runtime.CompilerServices;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed class SwordShieldWorldEditor:IProjectEditor,IOptionTemplateProjectEditor,IOptionTemplatePresentation,IWildEditorPresentation
{
    public string ImportOptionsLabel=>"Cargar template de "+Title.ToLowerInvariant();
    public string ExportOptionsLabel=>"Exportar template de "+Title.ToLowerInvariant();
    private readonly SwordShieldProject _project;
    private readonly SwordShieldOptionsStore _optionStore;
    public void ImportOptions(string path)=>_optionStore.Import(path,RandomOptions);
    public void ExportOptions(string path,IReadOnlyDictionary<string,int> values)=>_optionStore.Export(path,values,RandomOptions);
    public string CaptureOptions(IReadOnlyDictionary<string,int> values)=>_optionStore.Capture(values,RandomOptions);
    public void RestoreOptions(string json)=>_optionStore.Restore(json,RandomOptions);
    private readonly bool _items;
    private IReadOnlyList<SwordShieldWorldCell> _cells;
    private string _revision;
    private IReadOnlyList<MoveChoice>? _itemChoices;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly ConditionalWeakTable<EditorProposal,SwordShieldGameplayPreview> _proposals=new();
    private Dictionary<int,string> _regions=new();
    private readonly Dictionary<string,int> _regionIds=new(StringComparer.Ordinal);
    internal event Action? Changed;
    public string Title=>_items?"Field Items":"Encuentros";
    public bool UsesWildLayout=>!_items;
    public IReadOnlyList<RecordEntry> Entries{get;private set;}=Array.Empty<RecordEntry>();
    internal SwordShieldWorldEditor(SwordShieldProject project,bool items){_optionStore=new(items?"FieldItems":"Encounters");_project=project;_items=items;_cells=Array.Empty<SwordShieldWorldCell>();_revision="";Refresh();}
    private void Refresh(){_cells=_project.ReadWorld(_items);_revision=_project.RevisionHash;foreach(string region in _cells.Select(c=>c.Region).Distinct())if(!_regionIds.ContainsKey(region))_regionIds[region]=_regionIds.Count+1;_regions=_cells.Select(c=>c.Region).Distinct().ToDictionary(name=>_regionIds[name],name=>name);Entries=_regions.Select(p=>new RecordEntry(p.Key,RegionLabel(p.Value))).ToArray();}
    private string RegionLabel(string region)
    {
        if(_items)return "Área "+region;var parts=region.Split('/');return parts.Length==3?(parts[0].StartsWith("encount_symbol_",StringComparison.Ordinal)?"Visibles":"Ocultos")+" · zona "+int.Parse(parts[1]).ToString("000")+" · clima "+parts[2]:region;
    }
    private IReadOnlyList<MoveChoice>? Choices(SwordShieldWorldCell c)=>c.Field=="Especie"?_project.Entries.Where(e=>e.Form==0).Select(e=>new MoveChoice(e.Species,e.Name)).ToArray():c.Field=="Objeto"?(_itemChoices??=_project.WorldItemIds().Select(i=>new MoveChoice(i,_project.ItemName(i))).ToArray()):null;
    private string UiKey(SwordShieldWorldCell cell)=>_items?cell.Key:cell.Group.StartsWith("Pokémon ",StringComparison.Ordinal)?"S"+cell.Group.Split(' ').Last()+"."+(cell.Field=="Especie"?"Species":cell.Field=="Forma"?"Form":"Rate"):cell.Key;
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if(_revision!=_project.RevisionHash)Refresh();if(!_regions.TryGetValue(id,out var region))throw new ArgumentException("Área desconocida.");
        return _cells.Where(c=>c.Region==region).GroupBy(c=>_items?c.Group:c.Group=="Niveles"?"Niveles":"Encuentros").Select(g=>new EditorGroup(g.Key,g.Select(c=>new EditorField(UiKey(c),!_items&&c.Group!="Niveles"?c.Group+" · "+c.Field:c.Field,c.Value,c.Min,c.Max,Choices(c))).ToArray())).ToArray();
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        if(_revision!=_project.RevisionHash)throw new InvalidOperationException("La sesión cambió. Vuelve a seleccionar el área antes de guardar.");EditorValues.Validate(Read(id),values);var edits=_cells.Where(c=>c.Region==_regions[id]&&values[UiKey(c)]!=c.Value).Select(c=>new SwordShieldDataEdit(c.Key,values[UiKey(c)])).ToArray();_project.ApplyGameplay(_project.PreviewWorld(_items,edits));Refresh();Changed?.Invoke();
    }
    public IReadOnlyList<EditorGroup> RandomOptions=>_optionStore.Apply(BaseRandomOptions);
    private IReadOnlyList<EditorGroup> BaseRandomOptions
    {
        get
        {
            var groups=new List<EditorGroup>{new(Title,new[]{new EditorField("Visible","Randomizar visibles",1,0,1),new EditorField("Hidden","Randomizar ocultos",1,0,1)})};
            if(_items)return groups;
            groups.Add(new("Especies",new[]{new EditorField("SimilarBST","Mantener BST similar",0,0,1),new EditorField("Progressive","BST progresivo por nivel",0,0,1),new EditorField("FillEmpty","Llenar ranuras vacías existentes",0,0,1),new EditorField("EmptyMin","Tablas sin nivel · nivel mínimo",1,1,100),new EditorField("EmptyMax","Tablas sin nivel · nivel máximo",5,1,100)}));
            for(int i=0;i<SwordShieldBstRange.Defaults.Count;i++){var r=SwordShieldBstRange.Defaults[i];string key="BST."+i+".";groups.Add(new("Rango "+(i+1),new[]{new EditorField(key+"MinLevel","Nivel mínimo",r.MinLevel,1,100),new EditorField(key+"MaxLevel","Nivel máximo",r.MaxLevel,1,100),new EditorField(key+"MinBST","BST mínimo",r.MinBst,0,1530),new EditorField(key+"MaxBST","BST máximo",r.MaxBst,0,1530),new EditorField(key+"FullRandom","Full Random",0,0,1)}));}return groups;
        }
    }
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions.Select(g=>g with{Fields=g.Fields.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray()}),options);if(scope.Count==0||scope.Any(id=>!_regions.ContainsKey(id)))throw new ArgumentException("Alcance de áreas inválido.");
        int V(string key)=>options.GetValueOrDefault(key);var ranges=options.Where(p=>p.Key.StartsWith("BST.",StringComparison.Ordinal)).GroupBy(p=>p.Key.Split('.')[1]).Select(g=>{string key="BST."+g.Key+".";return new SwordShieldBstRange(V(key+"MinLevel"),V(key+"MaxLevel"),V(key+"MinBST"),V(key+"MaxBST"),V(key+"FullRandom")==1);}).ToArray();uint seed=SwitchSeed.New32()&int.MaxValue;
        var config=new SwordShieldGameplayOptions(seed,VisibleEncounters:!_items&&V("Visible")==1,HiddenEncounters:!_items&&V("Hidden")==1,VisibleItems:_items&&V("Visible")==1,HiddenItems:_items&&V("Hidden")==1,SimilarBst:V("SimilarBST")==1){FillEmptyEncounters=V("FillEmpty")==1,EmptyMinLevel=options.GetValueOrDefault("EmptyMin",1),EmptyMaxLevel=options.GetValueOrDefault("EmptyMax",5),ProgressiveBst=V("Progressive")==1,BstRanges=ranges,WorldScope=V("FillEmpty")==1&&scope.Count==Entries.Count?null:scope.Select(id=>_regions[id]).ToArray()};var data=_project.PreviewGameplay(config);var proposal=new EditorProposal(_owner,"Randomizar "+Title,(int)seed,Array.Empty<FileChange>(),data.Result.Changes.Select(c=>new BatchChange(0,c.Location,c.Field,c.Before.ToString(CultureInfo.InvariantCulture),c.After.ToString(CultureInfo.InvariantCulture))),new Dictionary<string,string>{{"Platform","SWSH"}});_proposals.Add(proposal,data);return proposal;
    }
    public void Apply(EditorProposal proposal){if(proposal.Owner!=_owner||!_proposals.TryGetValue(proposal,out var preview))throw new InvalidOperationException("Propuesta ajena a este editor.");_project.ApplyGameplay(preview);_proposals.Remove(proposal);Refresh();Changed?.Invoke();}
}
