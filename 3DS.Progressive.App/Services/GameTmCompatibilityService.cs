using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed record TmCompatibilityPreview(
    byte[] PersonalOriginal, byte[] PersonalOutput,
    byte[] CodeHash, byte[] LearnsetHash, byte[] EvolutionHash,
    bool Sanity, bool FollowEvolutions, int AddedSanityFlags,
    int AddedInheritedFlags, IReadOnlyList<BatchChange> Changes);

/// <summary>
/// WPF port of TMEditor7.ApplyTMSanity / ApplyTMFollowEvolutions.
/// Operates on active TM/HM bits (Personal 0x28..0x37), preserving padding bits.
/// Reads real MT assignments from code.bin; never touches Shop.cro.
/// </summary>
public sealed class GameTmCompatibilityService
{
    private readonly GameProjectSession _project;
    private readonly string _personalPath, _codePath, _learnsetPath, _evolutionPath;
    private readonly byte[] _personalOriginal, _codeHash, _learnsetOriginal, _evolutionOriginal;
    private readonly ushort[] _tmMoves;
    private readonly int _recordSize;
    private readonly GARCReference _personalRef;

    public GameTmCompatibilityService(GameProjectSession project)
    {
        _project=project;
        var config=project.GameConfiguration;
        if(config.Generation is not (6 or 7))throw new NotSupportedException("Compatibilidad MT/MO requiere Gen6 o Gen7.");
        _recordSize=config.Generation==7?PersonalInfoSM.SIZE:config.ORAS?PersonalInfoORAS.SIZE:PersonalInfoXY.SIZE;
        _personalRef=config.GetGARCReference("personal");
        _personalPath=Path.Combine(config.RomFS,config.GetGARCFileName("personal"));
        _learnsetPath=Path.Combine(config.RomFS,config.GetGARCFileName("levelup"));
        _evolutionPath=Path.Combine(config.RomFS,config.GetGARCFileName("evolution"));
        _codePath=new[]{".code.bin","code.bin"}.Select(name=>Path.Combine(config.ExeFS,name)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin para identificar las MT actuales.");
        _personalOriginal=File.ReadAllBytes(_personalPath);
        _learnsetOriginal=File.ReadAllBytes(_learnsetPath);
        _evolutionOriginal=File.ReadAllBytes(_evolutionPath);
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));
        // Use the same already-validated signature + offsets as the MT editor.
        _tmMoves=config.Generation==7?new GameMachineTutorService(project).Machines.Select(row=>checked((ushort)row.MoveId)).ToArray():
            EditorValues.Flatten(new GameMachine6Service(project).Read(1)).Where(row=>row.Key.StartsWith("TM")||row.Key.StartsWith("HM")).OrderBy(row=>row.Key.StartsWith("HM")?1:0).ThenBy(row=>row.Key,StringComparer.Ordinal).Select(row=>checked((ushort)row.Value)).ToArray();
        int expected=config.Generation==7?100:config.ORAS?107:105;
        if(_tmMoves.Length!=expected || _tmMoves.Any(x=>x<=0||x>=project.MoveNames.Count))
            throw new InvalidDataException("code.bin no contiene una tabla MT/MO válida para el catálogo del proyecto.");
    }

    private PersonalInfo DecodePersonal(byte[] record)=>_project.Generation==7?new PersonalInfoSM((byte[])record.Clone()):
        _project.GameConfiguration.ORAS?new PersonalInfoORAS((byte[])record.Clone()):new PersonalInfoXY((byte[])record.Clone());

    public TmCompatibilityPreview Preview(bool sanity,bool follow)
    {
        if(!sanity&&!follow)
            throw new ArgumentException("Elegí TM Sanity y/o TM Follow Evolutions.");
        byte[][] records=new GARC.MemGARC(_personalOriginal).Files;
        byte[][] learnBytes=new GARC.MemGARC(_learnsetOriginal).Files;
        byte[][] evolutionBytes=new GARC.MemGARC(_evolutionOriginal).Files;
        if(records.Length<3||records[^1].Length!=(records.Length-1)*_recordSize)
            throw new InvalidDataException("El GARC Personal no contiene una tabla maestra coherente.");
        int count=records.Length-1;
        var table=new PersonalInfo[count];
        for(int i=0;i<count;i++)
        {
            if(records[i].Length!=_recordSize)
                throw new InvalidDataException($"Personal #{i} tiene longitud incompatible.");
            table[i]=DecodePersonal(records[i]);
            if(table[i].TMHM.Length<_tmMoves.Length)
                throw new InvalidDataException($"Personal #{i} no contiene los 100 bits de MT.");
        }
        var learnsets=learnBytes.Select(b=>new Learnset6(b)).ToArray();
        var evolutions=evolutionBytes.Select(b=>_project.Generation==6?(EvolutionSet)new EvolutionSet6(b):new EvolutionSet7(b)).ToArray();
        int sanityAdded=sanity?ApplySanity(table,learnsets,_tmMoves):0;
        int followAdded=follow?ApplyFollowEvolutions(table,evolutions,
            _project.GameConfiguration.MaxSpeciesID,_tmMoves.Length):0;
        var changes=new List<BatchChange>();
        for(int i=0;i<count;i++)
        {
            byte[] updated=table[i].Write();
            // TM compatibility only: 0x28..0x37. Tutors, species stats and
            // Z fields remain byte identical.
            if(updated.Length!=records[i].Length||
                updated.Where((value,offset)=>value!=records[i][offset]&&
                    (offset<0x28||offset>=0x38)).Any())
                throw new InvalidDataException("Las MT intentaron modificar datos ajenos a TMHM.");
            if(updated.AsSpan().SequenceEqual(records[i]))continue;
            int before=DecodePersonal(records[i]).TMHM.Count(x=>x);
            int after=table[i].TMHM.Count(x=>x);
            if(after<before)throw new InvalidDataException("Se intentó eliminar compatibilidad de MT.");
            string name=_project.Entries.FirstOrDefault(x=>x.Index==i)?.Name??$"Pokémon #{i}";
            changes.Add(new BatchChange(i,name,"Compatibilidad MT",before.ToString(),after.ToString()));
            records[i]=updated;
        }
        byte[] output;
        if(changes.Count==0)output=(byte[])_personalOriginal.Clone();
        else
        {
            GarcTransaction.RebuildPersonalMaster(records);
            output=GarcTransaction.Build(_personalOriginal,_personalRef,records);
            byte[][] check=new GARC.MemGARC(output).Files;
            if(check.Length!=records.Length||
               check.Where((bytes,i)=>!bytes.AsSpan().SequenceEqual(records[i])).Any())
                throw new InvalidDataException("El GARC Personal reconstruido no coincide con su vista previa.");
        }
        return new TmCompatibilityPreview((byte[])_personalOriginal.Clone(),output,
            (byte[])_codeHash.Clone(),SHA256.HashData(_learnsetOriginal),
            SHA256.HashData(_evolutionOriginal),sanity,follow,sanityAdded,followAdded,changes);
    }

    public void Apply(TmCompatibilityPreview preview)
    {
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:ManualOperationScope.Flags(_personalOriginal,preview.PersonalOutput,"GameTmCompatibilityService");
        ArgumentNullException.ThrowIfNull(preview);
        if(preview.Changes.Count==0)
            throw new InvalidOperationException("No existen nuevas compatibilidades para guardar.");
        var fresh=Preview(preview.Sanity,preview.FollowEvolutions);
        if(!fresh.PersonalOutput.AsSpan().SequenceEqual(preview.PersonalOutput)||
           fresh.Changes.Count!=preview.Changes.Count)
            throw new InvalidDataException("La vista previa de MT dejó de ser válida.");
        var current=GarcTransaction.ReadUnchanged(_personalPath,SHA256.HashData(_personalOriginal));
        GarcTransaction.ReadUnchanged(_codePath,preview.CodeHash);
        GarcTransaction.ReadUnchanged(_learnsetPath,preview.LearnsetHash);
        GarcTransaction.ReadUnchanged(_evolutionPath,preview.EvolutionHash);
        _project.History.Commit([new FileChange(_personalPath,current,preview.PersonalOutput)],
            "MT y tutores",
            preview.Sanity&&preview.FollowEvolutions?"TM Sanity y TM Follow Evolutions":
            preview.Sanity?"TM Sanity":"TM Follow Evolutions",
            preview.Changes,new Dictionary<string,string>
            {
                ["TMSanity"]=preview.Sanity.ToString(),
                ["TMFollowEvolutions"]=preview.FollowEvolutions.ToString(),
                ["AddedSanity"]=preview.AddedSanityFlags.ToString(),
                ["AddedFollow"]=preview.AddedInheritedFlags.ToString(),
                ["Dependencies"]="code.bin, levelup GARC, evolution GARC"
            });
        _project.RefreshPersonalAfterExternalEdit();
    }

    /// <summary>Gen7 MT flags for already known moves, using the current code.bin map.</summary>
    internal static int ApplySanity(PersonalInfo[] personal,Learnset6[] learnsets,
        IReadOnlyList<ushort> tmMoves,Func<PersonalInfo,bool[]>? selectFlags=null)
    {
        int added=0;
        for(int species=1;species<Math.Min(personal.Length,learnsets.Length);species++)
        {
            var learned=learnsets[species].Moves.ToHashSet();
            var flags=selectFlags?.Invoke(personal[species])??personal[species].TMHM;
            for(int tm=0;tm<Math.Min(tmMoves.Count,flags.Length);tm++)
                if(tmMoves[tm]>0&&!flags[tm]&&learned.Contains(tmMoves[tm]))
                {flags[tm]=true;added++;}
        }
        return added;
    }

    /// <summary>Exact Gen7 TMEditor7 form-aware propagation over chains and branches.</summary>
    internal static int ApplyFollowEvolutions(PersonalInfo[] personal,
        EvolutionSet[] evolutions,int maxSpecies,int machineCount=100,Func<PersonalInfo,bool[]>? selectFlags=null)
    {
        var edges=new HashSet<(int Source,int Target)>();
        for(int species=1;species<=Math.Min(maxSpecies,personal.Length-1);species++)
        {
            AddEvolutionEdges(species,species);
            int formCount=personal[species].FormeCount;
            for(int form=1;form<formCount;form++)
            {
                int sourceIndex=personal[species].FormeIndex(species,form);
                if(sourceIndex==species||sourceIndex<=0||sourceIndex>=personal.Length)continue;
                AddEvolutionEdges(sourceIndex,species);
            }
        }
        void AddEvolutionEdges(int source,int fallbackSpecies)
        {
            int index=source<evolutions.Length?source:fallbackSpecies;
            if(index<0||index>=evolutions.Length)return;
            foreach(var evo in evolutions[index].PossibleEvolutions)
            {
                if(evo.Method<=0||evo.Species<=0||evo.Species>=personal.Length)continue;
                int target=personal[evo.Species].FormeIndex(evo.Species,evo.Form);
                if(target<=0||target>=personal.Length||target==source)continue;
                edges.Add((source,target));
            }
        }
        int added=0;bool changed;
        do
        {
            changed=false;
            foreach(var (source,target) in edges)
            {
                var from=selectFlags?.Invoke(personal[source])??personal[source].TMHM;var to=selectFlags?.Invoke(personal[target])??personal[target].TMHM;
                for(int tm=0;tm<Math.Min(machineCount,Math.Min(from.Length,to.Length));tm++)
                    if(from[tm]&&!to[tm]){to[tm]=true;added++;changed=true;}
            }
        }while(changed);
        return added;
    }
}
