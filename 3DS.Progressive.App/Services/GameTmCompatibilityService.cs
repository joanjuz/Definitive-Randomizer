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
/// Operates only on 100 TMHM bits (Personal 0x28..0x37).
/// Reads real MT assignments from code.bin; never touches Shop.cro.
/// </summary>
public sealed class GameTmCompatibilityService
{
    private readonly GameProjectSession _project;
    private readonly string _personalPath, _codePath, _learnsetPath, _evolutionPath;
    private readonly byte[] _personalOriginal, _codeHash, _learnsetOriginal, _evolutionOriginal;
    private readonly ushort[] _tmMoves;
    private readonly GARCReference _personalRef;

    public GameTmCompatibilityService(GameProjectSession project)
    {
        _project=project;
        var config=project.GameConfiguration;
        if(config.Generation!=7)
            throw new NotSupportedException("TM Sanity / Follow Evolutions solo se admiten para Gen7.");
        _personalRef=config.GetGARCReference("personal");
        _personalPath=Path.Combine(config.RomFS,config.GetGARCFileName("personal"));
        _learnsetPath=Path.Combine(config.RomFS,config.GetGARCFileName("levelup"));
        _evolutionPath=Path.Combine(config.RomFS,config.GetGARCFileName("evolution"));
        _codePath=Directory.EnumerateFiles(config.ExeFS)
            .FirstOrDefault(p=>Path.GetFileName(p).Contains("code",StringComparison.OrdinalIgnoreCase))
            ??throw new FileNotFoundException("Falta code.bin para identificar las MT actuales.");
        _personalOriginal=File.ReadAllBytes(_personalPath);
        _learnsetOriginal=File.ReadAllBytes(_learnsetPath);
        _evolutionOriginal=File.ReadAllBytes(_evolutionPath);
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));
        // Use the same already-validated signature + offsets as the MT editor.
        _tmMoves=new GameMachineTutorService(project).Machines
            .Select(row=>checked((ushort)row.MoveId)).ToArray();
        if(_tmMoves.Length!=100 || _tmMoves.Any(x=>x<=0||x>=project.MoveNames.Count))
            throw new InvalidDataException("code.bin no contiene 100 MT válidas para el catálogo del proyecto.");
    }

    public TmCompatibilityPreview Preview(bool sanity,bool follow)
    {
        if(!sanity&&!follow)
            throw new ArgumentException("Elegí TM Sanity y/o TM Follow Evolutions.");
        byte[][] records=new GARC.MemGARC(_personalOriginal).Files;
        byte[][] learnBytes=new GARC.MemGARC(_learnsetOriginal).Files;
        byte[][] evolutionBytes=new GARC.MemGARC(_evolutionOriginal).Files;
        if(records.Length<3||records[^1].Length!=(records.Length-1)*PersonalInfoSM.SIZE)
            throw new InvalidDataException("El GARC Personal no contiene una tabla maestra Gen7 coherente.");
        int count=records.Length-1;
        var table=new PersonalInfoSM[count];
        for(int i=0;i<count;i++)
        {
            if(records[i].Length!=PersonalInfoSM.SIZE)
                throw new InvalidDataException($"Personal #{i} no tiene longitud 0x54.");
            table[i]=new PersonalInfoSM((byte[])records[i].Clone());
            if(table[i].TMHM.Length<100)
                throw new InvalidDataException($"Personal #{i} no contiene los 100 bits de MT.");
        }
        var learnsets=learnBytes.Select(b=>new Learnset6(b)).ToArray();
        var evolutions=evolutionBytes.Select(b=>new EvolutionSet7(b)).ToArray();
        int sanityAdded=sanity?ApplySanity(table,learnsets,_tmMoves):0;
        int followAdded=follow?ApplyFollowEvolutions(table,evolutions,
            _project.GameConfiguration.MaxSpeciesID):0;
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
            int before=new PersonalInfoSM((byte[])records[i].Clone()).TMHM.Count(x=>x);
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
    internal static int ApplySanity(PersonalInfoSM[] personal,Learnset6[] learnsets,
        IReadOnlyList<ushort> tmMoves)
    {
        int added=0;
        for(int species=1;species<Math.Min(personal.Length,learnsets.Length);species++)
        {
            var learned=learnsets[species].Moves.ToHashSet();
            var flags=personal[species].TMHM;
            for(int tm=0;tm<Math.Min(tmMoves.Count,flags.Length);tm++)
                if(tmMoves[tm]>0&&!flags[tm]&&learned.Contains(tmMoves[tm]))
                {flags[tm]=true;added++;}
        }
        return added;
    }

    /// <summary>Exact Gen7 TMEditor7 form-aware propagation over chains and branches.</summary>
    internal static int ApplyFollowEvolutions(PersonalInfoSM[] personal,
        EvolutionSet7[] evolutions,int maxSpecies)
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
                var from=personal[source].TMHM;var to=personal[target].TMHM;
                for(int tm=0;tm<Math.Min(100,Math.Min(from.Length,to.Length));tm++)
                    if(from[tm]&&!to[tm]){to[tm]=true;added++;changed=true;}
            }
        }while(changed);
        return added;
    }
}
