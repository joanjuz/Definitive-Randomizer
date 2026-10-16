using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
internal sealed record Tutor6CompatibilityPreview(Guid Owner,FileChange File,IReadOnlyList<BatchChange> Changes,bool Sanity,bool Follow,int AddedSanity,int AddedInherited);
internal sealed class GameTutor6CompatibilityService
{
    private readonly GameProjectSession _project;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly string _personalPath,_learnPath,_evoPath,_codePath;
    private readonly byte[] _personal,_learn,_evo,_code;
    private readonly ushort[] _moves;
    internal GameTutor6CompatibilityService(GameProjectSession project)
    {
        if(!project.GameConfiguration.ORAS)throw new ArgumentException("La compatibilidad de tutores BP requiere ORAS.");
        _project=project;var game=project.GameConfiguration;
        _personalPath=Path.Combine(game.RomFS,game.GetGARCFileName("personal"));_learnPath=Path.Combine(game.RomFS,game.GetGARCFileName("levelup"));_evoPath=Path.Combine(game.RomFS,game.GetGARCFileName("evolution"));
        _codePath=new[]{".code.bin","code.bin"}.Select(name=>Path.Combine(game.ExeFS,name)).FirstOrDefault(File.Exists)??throw new FileNotFoundException("Falta code.bin.");
        _personal=File.ReadAllBytes(_personalPath);_learn=File.ReadAllBytes(_learnPath);_evo=File.ReadAllBytes(_evoPath);_code=File.ReadAllBytes(_codePath);
        var layout=Gen6TutorLayout.Read(_code,Math.Min(game.Moves.Length,project.MoveNames.Count));_moves=Enumerable.Range(0,60).Select(slot=>BitConverter.ToUInt16(_code,layout.Shop+8*slot)).ToArray();
    }
    internal Tutor6CompatibilityPreview Preview(bool sanity,bool follow)
    {
        if(!sanity&&!follow)throw new ArgumentException("Elegí Sanity y/o Follow Evolutions.");
        var records=new GARC.MemGARC(_personal).Files;if(records.Length<3||records[^1].Length!=(records.Length-1)*PersonalInfoORAS.SIZE)throw new InvalidDataException("Personal ORAS no contiene una tabla maestra coherente.");
        PersonalInfo[] table=records[..^1].Select(data=>data.Length==PersonalInfoORAS.SIZE?(PersonalInfo)new PersonalInfoORAS((byte[])data.Clone()):throw new InvalidDataException("Tamaño Personal ORAS incompatible.")).ToArray();
        var flags=table.ToDictionary(personal=>personal,personal=>Enumerable.Range(0,60).Select(slot=>{var (group,flag)=Gen6TutorLayout.Compatibility(slot);return personal.SpecialTutors[group][flag];}).ToArray());
        int addedSanity=sanity?GameTmCompatibilityService.ApplySanity(table,new GARC.MemGARC(_learn).Files.Select(bytes=>new Learnset6(bytes)).ToArray(),_moves,personal=>flags[personal]):0;
        int addedFollow=follow?GameTmCompatibilityService.ApplyFollowEvolutions(table,new GARC.MemGARC(_evo).Files.Select(bytes=>(EvolutionSet)new EvolutionSet6(bytes)).ToArray(),_project.GameConfiguration.MaxSpeciesID,60,personal=>flags[personal]):0;
        var changes=new List<BatchChange>();
        for(int id=0;id<table.Length;id++)
        {
            for(int slot=0;slot<60;slot++){var (group,flag)=Gen6TutorLayout.Compatibility(slot);table[id].SpecialTutors[group][flag]=flags[table[id]][slot];}
            byte[] next=table[id].Write();if(next.Where((value,offset)=>value!=records[id][offset]&&(offset<0x40||offset>=0x50)).Any())throw new InvalidDataException("La compatibilidad intentó modificar campos ajenos a tutores ORAS.");
            if(next.SequenceEqual(records[id]))continue;
            int changedBits=next.Select((value,offset)=>System.Numerics.BitOperations.PopCount((uint)(value^records[id][offset]))).Sum();
            changes.Add(new(id,_project.Entries.FirstOrDefault(row=>row.Index==id)?.Name??"Pokémon "+id,"Compatibilidad tutor ORAS","Conservar flags originales","+"+changedBits));records[id]=next;
        }
        byte[] output=_personal;if(changes.Count>0){GarcTransaction.RebuildPersonalMaster(records);output=GarcTransaction.Build(_personal,_project.GameConfiguration.GetGARCReference("personal"),records);}
        return new(_owner,new FileChange(_personalPath,_personal,output),changes.AsReadOnly(),sanity,follow,addedSanity,addedFollow);
    }
    internal void Apply(Tutor6CompatibilityPreview preview)
    {
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:ManualOperationScope.Flags(preview.File.Original(),preview.File.Proposed(),"GameTutor6CompatibilityService");
        if(preview.Owner!=_owner||preview.Changes.Count==0)throw new InvalidOperationException("La propuesta no pertenece a este servicio o no tiene cambios.");
        foreach(var (path,bytes) in new[]{(_personalPath,_personal),(_learnPath,_learn),(_evoPath,_evo),(_codePath,_code)})GarcTransaction.ReadUnchanged(path,SHA256.HashData(bytes));
        _project.History.Commit([preview.File],"Tutores ORAS","Sanity / Follow Evolutions",preview.Changes,new Dictionary<string,string>{{"Scope","All"},{"TutorSanity",preview.Sanity.ToString()},{"TutorFollowEvolutions",preview.Follow.ToString()},{"Dependencies","code.bin, levelup, evolution"}});
        _project.RefreshPersonalAfterExternalEdit();
    }
}
