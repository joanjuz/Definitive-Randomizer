using System.IO;
using System.Text;
using pk3DS.Core.NX;

namespace ThreeDS.Progressive.Services.Switch;
internal sealed record SwordShieldGameplayOperation(string Algorithm,SwordShieldGameplayOptions Options,IReadOnlyList<SwordShieldGameplayChange> Changes,int EligibleSlots,int SkippedItems,string InputSha256,string ResultSha256,IReadOnlyList<string>? Warnings=null);
internal sealed class SwordShieldGameplayPreview
{
    internal Guid Owner { get; }
    internal string Revision { get; }
    internal SwordShieldGameplayOptions Options { get; }
    internal SwordShieldGameplayResult Result { get; }
    internal SwordShieldGameplayPreview(Guid owner,string revision,SwordShieldGameplayOptions options,SwordShieldGameplayResult result)
    {Owner=owner;Revision=revision;Options=options;Result=result;}
}
internal sealed partial class SwordShieldProject
{
    private readonly Dictionary<string,IReadOnlyList<string>> _catalogs=new(StringComparer.Ordinal);
    internal string SpeciesName(int id)=>CatalogName("monsname",id);
    internal string AbilityName(int id)=>CatalogName("tokusei",id);
    internal string MoveName(int id)=>CatalogName("wazaname",id);
    internal string ItemName(int id)=>CatalogName("itemname",id);
    internal string TrainerName(int id)=>CatalogName("trname",id);
    private string CatalogName(string bank,int id)
    {
        if(!_catalogs.TryGetValue(bank,out var names))
        {
            names=Array.Empty<string>();
            foreach(string language in new[]{"Spanish","English"})
            {
                string path=Path.Combine(Root,"romfs/bin/message",language,"common",bank+".dat");
                if(!File.Exists(path))continue;CheckNoLinks(path);names=SwordShieldText.Read(File.ReadAllBytes(path));break;
            }
            _catalogs.Add(bank,names);
        }
        return id>=0&&id<names.Count?names[id]:"";
    }
    private readonly Dictionary<string,byte[]> _gameplayOriginal=new(StringComparer.Ordinal);
    private readonly Dictionary<string,byte[]> _gameplayDraft=new(StringComparer.Ordinal);
    private string[]? _trainerPaths;
    private readonly List<SwordShieldGameplayOperation> _gameplayHistory=new();
    internal IReadOnlyList<SwordShieldGameplayOperation> GameplayHistory=>_gameplayHistory.AsReadOnly();
    internal string RevisionHash=>_gameplayDraft.Count==0 && _surpriseDraft.Count==0 && _pokeDraft.Count==0 ? DraftHash : Hash(Encoding.UTF8.GetBytes(DraftHash+string.Join("",_gameplayDraft.Concat(_surpriseDraft).Concat(_pokeDraft).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+Hash(p.Value)))));
    private byte[] LoadGameplay(string relative)
    {
        if(!_gameplayOriginal.TryGetValue(relative,out var data))
        {
            string path=Path.Combine(Root,"romfs",relative);CheckNoLinks(path);data=File.ReadAllBytes(path);
            _gameplayOriginal.Add(relative,data);_dependencies.Add("romfs/"+relative,Hash(data));
        }
        return _gameplayDraft.TryGetValue(relative,out var draft)?draft:data;
    }
    internal SwordShieldGameplayPreview PreviewGameplay(SwordShieldGameplayOptions options)
    {
        RequireEditable();VerifySource();
        SwordShieldBattlePolicy.Validate(options);
        options=options with{WorldScope=options.WorldScope is null?null:Array.AsReadOnly(options.WorldScope.ToArray()),BstRanges=Array.AsReadOnly(options.BstRanges.ToArray()),TrainerScope=options.TrainerScope is null?null:Array.AsReadOnly(options.TrainerScope.ToArray()),TrainerRules=options.TrainerRules with{ItemModes=Array.AsReadOnly(options.TrainerRules.ItemModes.ToArray()),BetterScope=options.TrainerRules.BetterScope is {} better?Array.AsReadOnly(better.ToArray()):null,StrongScope=options.TrainerRules.StrongScope is {} strong?Array.AsReadOnly(strong.ToArray()):null,PowerScope=options.TrainerRules.PowerScope is {} power?Array.AsReadOnly(power.ToArray()):null,SmartItemScope=options.TrainerRules.SmartItemScope is {} smart?Array.AsReadOnly(smart.ToArray()):null,Levels=Array.AsReadOnly(options.TrainerRules.Levels.ToArray()),BannedMoves=Array.AsReadOnly(options.TrainerRules.BannedMoves.ToArray()),MoveRules=Array.AsReadOnly(options.TrainerRules.MoveRules.ToArray())}};
        SwordShieldBattlePolicy.Validate(options);var input=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        void Load(string path)=>input[path]=LoadGameplay(path);
        if(options.Trainers)
        {
            string folder=Path.Combine(Root,"romfs/bin/trainer/trainer_poke");CheckNoLinks(folder);
            var trainerPaths=Directory.EnumerateFiles(folder,"*.bin").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(_trainerPaths is not null&&!trainerPaths.SequenceEqual(_trainerPaths))throw new IOException("Cambió el listado de entrenadores. Vuelve a abrir el proyecto.");
            _trainerPaths=trainerPaths;
            foreach(string file in trainerPaths)
            {
                string path="bin/trainer/trainer_poke/"+Path.GetFileName(file);Load(path);
                Load(path.Replace("/trainer_poke/","/trainer_data/",StringComparison.Ordinal).Replace("trainer_poke_","trainer_data_",StringComparison.Ordinal));
            }
        }
        if(options.Trainers&&(options.TrainerRules.NeedsMoves||options.TrainerRules.SmartItems))
        {
            foreach(var pair in LoadData(SwordShieldDataSection.Moves))input[pair.Key]=pair.Value;
            if(options.TrainerRules.NeedsMoves)foreach(var pair in LoadData(SwordShieldDataSection.Learnsets))input[pair.Key]=pair.Value;
            if(options.TrainerRules.SmartItems)foreach(var pair in LoadData(SwordShieldDataSection.Items))input[pair.Key]=pair.Value;
        }
        if(options.VisibleEncounters||options.HiddenEncounters)Load(SwordShieldGameplay.EncountersPath);
        if(options.VisibleItems||options.HiddenItems){Load(SwordShieldGameplay.PlacementPath);Load(SwordShieldGameplay.ItemHashesPath);}
        var result=SwordShieldGameplay.Generate(input,_draft,Identity.TitleId=="01008DB008C2C000",options);VerifySource();
        return new(_id,RevisionHash,options,result);
    }
    internal void ApplyGameplay(SwordShieldGameplayPreview preview)
    {
        RequireEditable();VerifySource();if(preview.Owner!=_id||preview.Revision!=RevisionHash)throw new InvalidOperationException("Vista previa desactualizada o de otro proyecto.");
        if(preview.Result.Changes.Count==0)return;
        var recipe=CaptureGameplayAction(preview);string before=RevisionHash;
        foreach(var pair in preview.Result.Files())
        {if(pair.Value.AsSpan().SequenceEqual(_gameplayOriginal[pair.Key]))_gameplayDraft.Remove(pair.Key);else _gameplayDraft[pair.Key]=pair.Value;}
        _gameplayHistory.Add(new(SwordShieldGameplay.Algorithm,preview.Options,preview.Result.Changes,preview.Result.EligibleSlots,preview.Result.SkippedItems,before,RevisionHash,preview.Result.Warnings));AppendRecipe(recipe);
    }
    private IReadOnlyList<SwordShieldGameplayChange> GameplayNetChanges()=>_gameplayHistory.SelectMany(h=>h.Changes).GroupBy(c=>(c.Resource,c.Location,c.Field)).Select(g=>g.First() with {After=g.Last().After}).Where(c=>c.Before!=c.After).ToArray();
    private IEnumerable<object> ExportResources()
    {
        if(!_draft.AsSpan().SequenceEqual(_original))yield return new{Resource="romfs/"+PersonalRelativePath,SourceSha256=SourceHash,ResultSha256=DraftHash};
        foreach(var p in _surpriseDraft.OrderBy(p=>p.Key,StringComparer.Ordinal))yield return new{Resource=p.Key,SourceSha256=p.Key=="exefs/main"?_dependencies["exefs/main"]:null,ResultSha256=Hash(p.Value)};
        foreach(var p in _pokeDraft.OrderBy(p=>p.Key,StringComparer.Ordinal))yield return new{Resource=p.Key,SourceSha256=p.Key=="exefs/main.npdm"?_dependencies["exefs/main.npdm"]:p.Key=="romfs/"+ScriptRegistryRelative?Hash(_gameplayOriginal[ScriptRegistryRelative]):null,ResultSha256=Hash(p.Value)};
        foreach(var p in _gameplayDraft.OrderBy(p=>p.Key,StringComparer.Ordinal))yield return new{Resource="romfs/"+p.Key,SourceSha256=Hash(_gameplayOriginal[p.Key]),ResultSha256=Hash(p.Value)};
    }
    private void ExportGameplay(string stage,SwitchModLayout layout)
    {
        foreach(var pair in _gameplayDraft)
        {
            string root=layout==SwitchModLayout.Ryujinx?stage:Path.Combine(stage,"atmosphere/contents",Identity.TitleId);
            string path=Path.Combine(root,"romfs",pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,pair.Value);
            if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(pair.Value))throw new IOException("Falló la reapertura de "+pair.Key);
        }
    }
}
