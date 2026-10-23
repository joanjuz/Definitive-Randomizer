using System.IO;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record PokeFeurCapSource(int Trainer,int Level,string Resource,string Hash);
internal sealed record PokeFeurCapEvidence(int Badge,int Trainer,int Level,string Resource,string Hash,IReadOnlyList<PokeFeurCapSource>? Sources=null);
internal sealed record PokeFeurParameterChange(string Parameter,string Before,string After);
internal sealed record PokeFeurOperation(bool Enabled,PokeFeurOptions Options,IReadOnlyList<PokeFeurParameterChange> Changes,IReadOnlyList<PokeFeurCapEvidence> CapEvidence,string Commit,string InputRevision,string ResultRevision);
internal sealed class PokeFeurPreview
{
    internal Guid Owner {get;}
    internal string Revision {get;}
    internal PokeFeurOptions Options {get;}
    internal IReadOnlyList<PokeFeurCapEvidence> Caps {get;}
    private readonly Dictionary<string,byte[]> _files;
    internal IReadOnlyDictionary<string,string> Hashes=>_files.ToDictionary(p=>p.Key,p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value)),StringComparer.Ordinal);
    internal Dictionary<string,byte[]> Files()=>_files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone(),StringComparer.Ordinal);
    internal PokeFeurPreview(Guid owner,string revision,PokeFeurOptions options,IReadOnlyList<PokeFeurCapEvidence> caps,Dictionary<string,byte[]> files){Owner=owner;Revision=revision;Options=options;Caps=caps;_files=files;}
}
internal sealed partial class SwordShieldProject
{
    internal const string PokeConfigRelative="sdcard/config/swsh-mods-exl/config.toml";
    internal const string ScriptRegistryRelative="bin/script/param/script_id/script_id_record.bin";
    private readonly Dictionary<string,byte[]> _pokeDraft=new(StringComparer.Ordinal);
    private readonly List<PokeFeurOperation> _pokeHistory=new();
    private bool _pokeProfileChecked;
    internal bool PokeFeurEnabled=>_pokeDraft.Count>0;
    internal IReadOnlyList<PokeFeurOperation> PokeFeurHistory=>_pokeHistory.AsReadOnly();
    internal PokeFeurOptions? PokeFeurCurrent {get;private set;}
    private IReadOnlyList<PokeFeurCapEvidence> _pokeCaps=Array.Empty<PokeFeurCapEvidence>();
    internal int[] PokeFeurPresentPool()=>Entries.Where(e=>e.Species>0&&e.Form==0).Select(e=>e.Species).Distinct().Order().ToArray();
    private void CheckPokeProfile()
    {
        RequireEditable();VerifySource();
        VerifyNoSubsdk9();if(SurpriseEnabled)throw new IOException("El prototipo nativo anterior está añadido a esta sesión. Descártalo antes de usar PokeFeur.");
        if(!_pokeProfileChecked&&Identity.TitleId=="01008DB008C2C000"){byte[] text=SwordSurpriseMod.ReadSegment(File.ReadAllBytes(Path.Combine(Root,"exefs/main")),0);if(Hash(text)!="88F4650C736FB61E1D0C624BFDFF1718C87A286F2686C7C3CBB9378301F8EB28")throw new InvalidDataException("El código de Escudo no coincide con el perfil 1.3.2 comprobado.");_pokeProfileChecked=true;}
        if(!_pokeProfileChecked){var audit=SwordSurprisePreflight.Audit(File.ReadAllBytes(Path.Combine(Root,"exefs/main")));if(!audit.SignaturesMatch||audit.TextSha256!=SwordSurpriseMod.OriginalTextHash)throw new InvalidDataException("El código de Espada no coincide con el perfil original validado.");_pokeProfileChecked=true;}
    }
    internal IReadOnlyList<PokeFeurCapEvidence> PokeFeurTrainerCaps()
    {
        RequireEditable();VerifySource();bool shield=Identity.TitleId=="01008DB008C2C000";int[][] groups={new[]{32},new[]{36},new[]{37},new[]{shield?78:77},new[]{108},new[]{shield?136:135},new[]{107},new[]{144},new[]{149,189,190}};var evidence=new List<PokeFeurCapEvidence>();
        for(int i=0;i<groups.Length;i++)
        {
            var sources=new List<PokeFeurCapSource>();foreach(int id in groups[i])
            {
                string name=$"trainer_poke_{id:000}.bin",path="bin/trainer/trainer_poke/"+name;byte[] party=LoadGameplay(path);byte[] trainer=LoadGameplay("bin/trainer/trainer_data/"+name.Replace("trainer_poke_","trainer_data_",StringComparison.Ordinal));
                if(party.Length==0||party.Length%32!=0||trainer.Length<4||trainer[3]<1||trainer[3]!=party.Length/32)throw new InvalidDataException("Equipo de jefe inválido: "+path);
                int level=0;for(int j=0;j<trainer[3];j++){int value=BinaryPrimitives.ReadUInt16LittleEndian(party.AsSpan(j*32+10));if(value<1||value>100)throw new InvalidDataException("Nivel de jefe fuera de rango.");level=Math.Max(level,value);}sources.Add(new(id,level,"romfs/"+path,Hash(party)));
            }
            evidence.Add(new(i,groups[i][0],sources.Max(s=>s.Level),sources[0].Resource,Hash(Encoding.UTF8.GetBytes(string.Join("|",sources.Select(s=>s.Resource+":"+s.Hash)))),sources.AsReadOnly()));
        }
        VerifySource();return evidence.AsReadOnly();
    }
    internal PokeFeurPreview PreviewPokeFeur(PokeFeurPackage? package,PokeFeurOptions options)
    {
        CheckPokeProfile();IReadOnlyList<PokeFeurCapEvidence> caps=options.Caps&&options.CapMode==PokeFeurCapMode.StoryGymLeague&&options.CapsFromTrainers?PokeFeurTrainerCaps():Array.Empty<PokeFeurCapEvidence>();
        if(caps.Count>0)options=options with{StoryCaps=caps.Select(c=>c.Level).ToArray()};options=options.Normalize(PokeFeurPresentPool());var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        if(options.Enabled)
        {
            package??=PokeFeurPackage.BuiltIn(Identity.TitleId);if(package.TitleId!=Identity.TitleId)throw new InvalidDataException("El paquete de runtime corresponde a otro juego.");foreach(string file in new[]{"exefs/subsdk9","exefs/main.npdm","LICENSE-PokeFeur.txt"})files[file]=package.File(file);files[PokeConfigRelative]=Encoding.UTF8.GetBytes(options.Config());
            var scripted=PokeFeurOptions.Catalog.Where(f=>f.Script is not null&&options.Features.Contains(f.Feature)).ToArray();
            if(scripted.Length>0)
            {
                if(_gameplayDraft.ContainsKey(ScriptRegistryRelative))throw new IOException("El registro de scripts ya tiene otro cambio pendiente.");byte[] original=LoadGameplay(ScriptRegistryRelative);var entries=PokeFeurScriptRegistry.Read(original);
                foreach(var feature in scripted)files["romfs/"+feature.Script]=package.File("romfs/"+feature.Script);
                bool pc=options.Features.Contains(PokeFeurFeature.PcMenu),bike=options.Features.Contains(PokeFeurFeature.EarlyBike),dex=options.Features.Contains(PokeFeurFeature.DexAnimations);
                if(pc){var entry=entries.SingleOrDefault(e=>e.Id==13435777772824609090UL);if(entry is null||entry.Amx!="bin/script/amx/pokemoncenter.amx"||entry.Text!="script/pokemoncenter.dat")throw new InvalidDataException("El script del Centro Pokémon no es el original esperado; no se sustituye.");}
                byte[] output=PokeFeurScriptRegistry.Patch(original,pc,bike,dex);var result=PokeFeurScriptRegistry.Read(output).ToDictionary(e=>e.Id);
                foreach(var e in entries)if(!(pc&&e.Id==13435777772824609090UL)&&(!result.TryGetValue(e.Id,out var after)||e!=after))throw new InvalidDataException("Se alteraría un registro de script ajeno a las opciones.");files["romfs/"+ScriptRegistryRelative]=output;
            }
        }
        VerifySource();return new(_id,RevisionHash,options,caps,files);
    }
    internal void ApplyPokeFeur(PokeFeurPreview preview)
    {
        CheckPokeProfile();if(preview.Owner!=_id||preview.Revision!=RevisionHash)throw new InvalidOperationException("Vista previa de otro proyecto o desactualizada.");var files=preview.Files();if(files.Count==0&&_pokeDraft.Count==0)return;
        if(files.Count==_pokeDraft.Count&&files.All(p=>_pokeDraft.TryGetValue(p.Key,out var value)&&value.AsSpan().SequenceEqual(p.Value))&&PokeFeurCurrent?.RecipeJson()==preview.Options.RecipeJson())return;
        string before=RevisionHash;var prior=(PokeFeurCurrent??new PokeFeurOptions()).Normalize(PokeFeurPresentPool());var old=ConfigParameters(prior.Config());var next=ConfigParameters(preview.Options.Config());var changes=next.Where(p=>!old.TryGetValue(p.Key,out var value)||value!=p.Value).Select(p=>new PokeFeurParameterChange(p.Key,old.GetValueOrDefault(p.Key,""),p.Value)).ToList();
        if(prior.CapsFromTrainers!=preview.Options.CapsFromTrainers)changes.Add(new("caps.source",prior.CapsFromTrainers?"jefes":"manual",preview.Options.CapsFromTrainers?"jefes":"manual"));
        _pokeDraft.Clear();foreach(var pair in files)_pokeDraft.Add(pair.Key,pair.Value);PokeFeurCurrent=preview.Options.Enabled?preview.Options:null;_pokeCaps=preview.Caps;_pokeHistory.Add(new(preview.Options.Enabled,preview.Options,changes.AsReadOnly(),preview.Caps,PokeFeurPackage.Commit,before,RevisionHash));AppendRecipe(RecipeAction("runtime.options",preview.Options));
    }
    private static Dictionary<string,string> ConfigParameters(string text)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);string section="";foreach(string raw in text.Split('\n')){string line=raw.Trim();if(line.StartsWith('['))section=line;else{int at=line.IndexOf('=');if(at>0&&!line.StartsWith('#'))result[section+"."+line[..at].Trim()]=line[(at+1)..].Trim();}}return result;
    }
    internal void RemovePokeFeur()
    {RequireEditable();VerifySource();if(!PokeFeurEnabled)return;string before=RevisionHash;var options=(new PokeFeurOptions()).Normalize(PokeFeurPresentPool());_pokeDraft.Clear();PokeFeurCurrent=null;_pokeCaps=Array.Empty<PokeFeurCapEvidence>();_pokeHistory.Add(new(false,options,Array.AsReadOnly(new[]{new PokeFeurParameterChange("runtime.enabled","true","false")}),Array.Empty<PokeFeurCapEvidence>(),PokeFeurPackage.Commit,before,RevisionHash));AppendRecipe(RecipeAction("runtime.remove",true));}
    private void ResetPokeFeur(){_pokeDraft.Clear();_pokeHistory.Clear();PokeFeurCurrent=null;_pokeCaps=Array.Empty<PokeFeurCapEvidence>();}
    private void ValidatePokeExport()
    {if(!PokeFeurEnabled)return;CheckPokeProfile();if(_pokeCaps.Count>0&&!PokeFeurTrainerCaps().Select(c=>c.Level).SequenceEqual(_pokeCaps.Select(c=>c.Level)))throw new InvalidOperationException("Los niveles de los jefes cambiaron. Actualiza los level caps antes de exportar.");}
    internal PokeFeurSdPlan PreviewPokeSd(string sdRoot){ValidatePokeExport();if(!PokeFeurEnabled)throw new InvalidOperationException("Añade la configuración al mod antes de instalarla.");return PokeFeurSdInstall.Preview(sdRoot,Encoding.UTF8.GetString(_pokeDraft[PokeConfigRelative]),Root);}
    internal void InstallPokeSd(PokeFeurSdPlan plan,bool replace){ValidatePokeExport();if(!PokeFeurEnabled||plan.NewHash!=Hash(_pokeDraft[PokeConfigRelative]))throw new InvalidOperationException("La configuración de la sesión cambió.");PokeFeurSdInstall.Apply(plan,replace);}
    private void ExportPokeFeur(string stage,SwitchModLayout layout)
    {
        if(!PokeFeurEnabled)return;if(layout!=SwitchModLayout.Ryujinx)throw new NotSupportedException("Este paquete del runtime es para Ryujinx; Atmosphère requiere otra variante validada.");ValidatePokeExport();
        foreach(var pair in _pokeDraft){string path=Path.Combine(stage,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,pair.Value);if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(pair.Value))throw new IOException("No se pudo reabrir "+pair.Key);}
        File.WriteAllText(Path.Combine(stage,"PokeFeur-procedencia.json"),JsonSerializer.Serialize(new{Provider="PokeFeur",PokeFeurPackage.Repository,PokeFeurPackage.Commit,TitleId=Identity.TitleId,ModuleHash=Hash(_pokeDraft["exefs/subsdk9"]),NpdmHash=Hash(_pokeDraft["exefs/main.npdm"]),UserValidated=Identity.TitleId=="01008DB008C2C000"?"Escudo 1.3.2: comprobaciones automáticas; prueba jugable pendiente.":"Combate sorpresa, captura y menús: Espada 1.3.2 / Ryujinx. Otras funciones pendientes de prueba jugable.",Options=PokeFeurCurrent,Caps=_pokeCaps},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(stage,"INSTALAR-CONFIGURACION-SD.txt"),"Este mod usa el runtime externo PokeFeur (GPL-3.0); ver licencia y procedencia.\nCopia sdcard/config/swsh-mods-exl/config.toml a la SD de Ryujinx conservando la configuración previa, o instálala explícitamente desde la ventana del runtime.\nActiva solo un runtime ExeFS para este título. La configuración SD es compartida entre mods PokeFeur.\nLos modos de descubrimiento guardan progreso en SD. Level caps, EXP, scripts y otras opciones requieren validación jugable. No se distribuye el main del juego.\n");
    }
}
