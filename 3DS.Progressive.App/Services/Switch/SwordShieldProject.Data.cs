using System.IO;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record SwordShieldDataOperation(SwordShieldDataSection Section,uint? Seed,IReadOnlyList<SwordShieldDataChange> Changes,string InputRevision,string ResultRevision,SwordShieldDataOptions? Options=null,IReadOnlyList<int>? Scope=null);
internal sealed record SwordShieldDataPreview(Guid Owner,string Revision,SwordShieldDataSection Section,uint? Seed,SwordShieldDataResult Result,SwordShieldDataOptions? Options=null,IReadOnlyList<int>? Scope=null);
internal sealed partial class SwordShieldProject
{
    private readonly List<SwordShieldDataOperation> _dataHistory=new();
    private readonly Dictionary<string,string[]> _dataListings=new(StringComparer.Ordinal);
    internal IReadOnlyList<SwordShieldDataOperation> DataHistory=>_dataHistory.AsReadOnly();
    private Dictionary<string,byte[]> LoadData(SwordShieldDataSection section)
    {
        RequireEditable();VerifySource();var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        void Load(string path)=>files[path]=LoadGameplay(path);
        void Folder(string relative,string pattern){string path=Path.Combine(Root,"romfs",relative);CheckNoLinks(path);var listing=Directory.GetFiles(path,pattern).OrderBy(p=>p,StringComparer.Ordinal).ToArray();if(listing.Length==0||listing.Length>2000)throw new InvalidDataException("Listado de datos incompatible: "+relative);if(_dataListings.TryGetValue(relative,out var old)&&!listing.SequenceEqual(old))throw new IOException("Cambió el listado de datos: "+relative);_dataListings[relative]=listing;foreach(string file in listing)Load(relative+"/"+Path.GetFileName(file));}
        switch(section){case SwordShieldDataSection.TrainerTeams:Folder("bin/trainer/trainer_poke","*.bin");Folder("bin/trainer/trainer_data","*.bin");break;case SwordShieldDataSection.Moves:Folder("bin/pml/waza","*.wazabin");break;case SwordShieldDataSection.Learnsets:Load(SwordShieldDataEditor.LearnPath);Folder("bin/pml/waza","*.wazabin");break;case SwordShieldDataSection.Evolutions:Folder("bin/pml/evolution","evo_*.bin");break;case SwordShieldDataSection.Machines:break;case SwordShieldDataSection.Items:Load(SwordShieldDataEditor.ItemPath);break;case SwordShieldDataSection.Shops:Load(SwordShieldDataEditor.ShopPath);break;default:Load(SwordShieldDataEditor.EventPath(section));break;}
        return files;
    }
    internal IReadOnlyList<SwordShieldDataCell> ReadData(SwordShieldDataSection section,int? entry=null)
    {var files=LoadData(section);var result=SwordShieldDataEditor.Read(section,files,_draft,entry);VerifySource();return result;}
    internal SwordShieldDataPreview PreviewData(SwordShieldDataSection section,IReadOnlyList<SwordShieldDataEdit>? edits,SwordShieldDataOptions? options=null,IReadOnlyList<int>? scope=null)
    {var files=LoadData(section);if(options?.SpecialShopsOnly==true&&section==SwordShieldDataSection.Shops)options=options with{SpecialShopEntries=Array.AsReadOnly(SwordShieldShopClassification.SpecialShopIds(_gameplayOriginal[SwordShieldDataEditor.ShopPath]).ToArray())};if(options?.ThreeStageStarters==true){foreach(var pair in LoadData(SwordShieldDataSection.Evolutions))files[pair.Key]=pair.Value;if(section==SwordShieldDataSection.Gifts){string giftPath=SwordShieldDataEditor.EventPath(section);var original=new Dictionary<string,byte[]>{{giftPath,_gameplayOriginal[giftPath]}};var entries=SwordShieldDataEditor.Read(section,original,_original).Where(c=>c.Field=="Especie"&&c.Value is 810 or 813 or 816).Select(c=>c.Entry).ToArray();if(entries.Length==0)throw new InvalidDataException("No se reconocen los registros de los iniciales en la extracción original. No se aplicará una suposición a regalos distintos.");options=options with{StarterEntries=Array.AsReadOnly(entries)};}}var result=options is null?SwordShieldDataEditor.Edit(section,files,_draft,edits??Array.Empty<SwordShieldDataEdit>()):SwordShieldDataEditor.Randomize(section,files,_draft,options);if(scope is not null)
        {
            var rows=SwordShieldDataEditor.Read(section,files,_draft);var ids=rows.Select(c=>c.Entry).ToHashSet();if(scope.Count==0||scope.Any(id=>!ids.Contains(id)))throw new ArgumentException("Alcance inválido.");
            var combined=files.ToDictionary(p=>p.Key,p=>p.Value);foreach(var pair in result.Files())combined[pair.Key]=pair.Value;
            var changed=SwordShieldDataEditor.Read(section,combined,combined.TryGetValue(PersonalRelativePath,out var p)?p:_draft).ToDictionary(c=>c.Key);
            var scoped=rows.Where(c=>scope.Contains(c.Entry)&&changed[c.Key].Value!=c.Value).Select(c=>new SwordShieldDataEdit(c.Key,changed[c.Key].Value)).ToArray();result=SwordShieldDataEditor.Edit(section,files,_draft,scoped);
        }
        VerifySource();return new(_id,RevisionHash,section,options?.Seed,result,options,scope?.ToArray());}
    internal void ApplyData(SwordShieldDataPreview preview)
    {
        RequireEditable();VerifySource();if(preview.Owner!=_id||preview.Revision!=RevisionHash)throw new InvalidOperationException("Vista previa desactualizada o ajena a este proyecto.");if(preview.Result.Changes.Count==0)return;
        var recipe=CaptureDataAction(preview);string before=RevisionHash;foreach(var p in preview.Result.Files()){if(p.Key==PersonalRelativePath)_draft=p.Value;else if(p.Value.AsSpan().SequenceEqual(_gameplayOriginal[p.Key]))_gameplayDraft.Remove(p.Key);else _gameplayDraft[p.Key]=p.Value;}
        _dataHistory.Add(new(preview.Section,preview.Seed,preview.Result.Changes,before,RevisionHash,preview.Options,preview.Scope));AppendRecipe(recipe);
    }
    private IReadOnlyList<SwordShieldDataChange> DataNetChanges()=>_dataHistory.SelectMany(h=>h.Changes).GroupBy(c=>(c.Resource,c.Location,c.Field)).Select(g=>g.First() with{After=g.Last().After}).Where(c=>c.Before!=c.After).ToArray();
    private void VerifyDataListings(){foreach(var p in _dataListings){string path=Path.Combine(Root,"romfs",p.Key);CheckNoLinks(path);string pattern=p.Key=="bin/pml/waza"?"*.wazabin":p.Key=="bin/pml/evolution"?"evo_*.bin":"*.bin";if(!Directory.GetFiles(path,pattern).OrderBy(f=>f,StringComparer.Ordinal).SequenceEqual(p.Value))throw new IOException("Cambió el listado de datos: "+p.Key);}}
}
