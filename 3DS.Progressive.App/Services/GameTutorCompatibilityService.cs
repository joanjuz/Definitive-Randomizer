using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Frozen, read-only proposal for the USUM Tutor Sanity / Follow Evolutions operations.</summary>
public sealed record TutorCompatibilityPreview(
    byte[] PersonalOriginal, byte[] PersonalOutput,
    byte[] LearnsetHash, byte[] EvolutionHash, byte[] ShopHash,
    bool Sanity, bool FollowEvolutions, int AddedSanityFlags,
    int AddedInheritedFlags, IReadOnlyList<BatchChange> Changes);

/// <summary>
/// WPF port of TutorEditor7's two compatibility algorithms.
/// Builds Personal GARC detached from disk; never modifies GameConfig's cached
/// Personal entries and never writes Shop.cro or code.bin.
/// </summary>
public sealed class GameTutorCompatibilityService
{
    private static readonly ushort[] TypeTutorMoves = [520,519,518,338,307,308,434,620];
    private readonly GameProjectSession _project;
    private readonly string _personalPath, _learnsetPath, _evolutionPath, _shopPath;
    private readonly byte[] _personalOriginal, _learnsetOriginal, _evolutionOriginal, _shopOriginal;
    private readonly ushort[] _tutorMoves;
    private readonly GARCReference _personalRef;

    public GameTutorCompatibilityService(GameProjectSession project)
    {
        _project=project;
        var config=project.GameConfiguration;
        if(!config.USUM)
            throw new NotSupportedException("Tutor Sanity y Follow Evolutions requieren Ultra Sol o Ultra Luna.");
        _personalRef=config.GetGARCReference("personal");
        _personalPath=Path.Combine(config.RomFS,_personalRef.Reference);
        _learnsetPath=Path.Combine(config.RomFS,config.GetGARCFileName("levelup"));
        _evolutionPath=Path.Combine(config.RomFS,config.GetGARCFileName("evolution"));
        _shopPath=Path.Combine(config.RomFS,"Shop.cro");
        _personalOriginal=File.ReadAllBytes(_personalPath);
        _learnsetOriginal=File.ReadAllBytes(_learnsetPath);
        _evolutionOriginal=File.ReadAllBytes(_evolutionPath);
        _shopOriginal=File.ReadAllBytes(_shopPath);
        if(!pk3DS.Core.Modding.Research.Gen7ShopCroLayout.TryRead(
            _shopOriginal,out var layout,out string error))
            throw new InvalidDataException("No se encontró la tabla estructural de tutores: "+error);
        if(layout.BPTutors.TotalEntries!=67)
            throw new InvalidDataException("La lista activa debe contener exactamente 67 movimientos de tutor.");
        _tutorMoves=Enumerable.Range(0,67).Select(i=>
            BitConverter.ToUInt16(_shopOriginal,checked((int)layout.BPTutors.DataStart)+i*4)).ToArray();
        if(_tutorMoves.Any(id=>id<=0||id>=_project.MoveNames.Count))
            throw new InvalidDataException("Shop.cro contiene movimientos de tutor fuera del catálogo actual.");
    }

    public TutorCompatibilityPreview Preview(bool sanity, bool followEvolutions)
    {
        if(!sanity&&!followEvolutions)
            throw new ArgumentException("Seleccioná Tutor Sanity, Follow Evolutions o ambas opciones.");
        byte[][] records=new GARC.MemGARC(_personalOriginal).Files;
        byte[][] learnBytes=new GARC.MemGARC(_learnsetOriginal).Files;
        byte[][] evoBytes=new GARC.MemGARC(_evolutionOriginal).Files;
        if(records.Length<3||records[^1].Length!=(records.Length-1)*PersonalInfoSM.SIZE)
            throw new InvalidDataException("El GARC Personal no tiene una tabla maestra compatible.");
        int count=records.Length-1;
        var table=new PersonalInfoSM[count];
        for(int i=0;i<count;i++)
        {
            if(records[i].Length!=PersonalInfoSM.SIZE)
                throw new InvalidDataException($"Registro Personal #{i} de tamaño desconocido.");
            table[i]=new PersonalInfoSM((byte[])records[i].Clone());
            if(table[i].TypeTutors.Length!=32||table[i].SpecialTutors.Length<1||
                table[i].SpecialTutors[0].Length<67)
                throw new InvalidDataException($"Compatibilidad de tutores desconocida en #{i}.");
        }
        int sanityCount=0,followCount=0;
        if(sanity)
        {
            var sets=learnBytes.Select(bytes=>new Learnset6(bytes)).ToArray();
            sanityCount=ApplySanity(table,sets,_tutorMoves);
        }
        if(followEvolutions)
        {
            var sets=evoBytes.Select(bytes=>new EvolutionSet7(bytes)).ToArray();
            followCount=ApplyFollowEvolutions(table,sets);
        }
        var changes=new List<BatchChange>();
        for(int i=0;i<count;i++)
        {
            byte[] modified=table[i].Write();
            // Type tutor flags are bytes 0x38..0x3B; beach tutors 0x3C..0x45.
            // All other personal values, including 0x46..0x53, must be unchanged.
            if(modified.Length!=records[i].Length ||
               modified.Where((value,at)=>value!=records[i][at] &&
                   (at<0x38||at>=0x46)).Any())
                throw new InvalidDataException("Sanity/Evoluciones intentó modificar campos ajenos a tutores.");
            if(modified.AsSpan().SequenceEqual(records[i]))continue;
            int before=CountTutorFlags(new PersonalInfoSM((byte[])records[i].Clone()));
            int after=CountTutorFlags(table[i]);
            if(after<before)
                throw new InvalidDataException("Una compatibilidad de tutor fue eliminada inesperadamente.");
            string name=_project.Entries.FirstOrDefault(z=>z.Index==i)?.Name??$"Pokémon #{i}";
            changes.Add(new BatchChange(i,name,"Compatibilidad tutores",before.ToString(),after.ToString()));
            records[i]=modified;
        }
        byte[] result;
        if(changes.Count==0)result=(byte[])_personalOriginal.Clone();
        else
        {
            GarcTransaction.RebuildPersonalMaster(records);
            result=GarcTransaction.Build(_personalOriginal,_personalRef,records);
            var verified=new GARC.MemGARC(result).Files;
            if(verified.Length!=records.Length ||
               verified.Where((bytes,i)=>!bytes.AsSpan().SequenceEqual(records[i])).Any())
                throw new InvalidDataException("La relectura del GARC Personal no coincide con la vista previa.");
        }
        return new TutorCompatibilityPreview((byte[])_personalOriginal.Clone(),result,
            SHA256.HashData(_learnsetOriginal),SHA256.HashData(_evolutionOriginal),
            SHA256.HashData(_shopOriginal),sanity,followEvolutions,
            sanityCount,followCount,changes);
    }

    public void Apply(TutorCompatibilityPreview preview)
    {
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:ManualOperationScope.Flags(_personalOriginal,preview.PersonalOutput,"GameTutorCompatibilityService");
        ArgumentNullException.ThrowIfNull(preview);
        if(preview.Changes.Count==0)
            throw new InvalidOperationException("No se encontraron compatibilidades nuevas que guardar.");
        TutorCompatibilityPreview fresh=Preview(preview.Sanity,preview.FollowEvolutions);
        if(!fresh.PersonalOutput.AsSpan().SequenceEqual(preview.PersonalOutput) ||
           fresh.Changes.Count!=preview.Changes.Count)
            throw new InvalidDataException("La propuesta cambió desde la vista previa.");
        byte[] original=GarcTransaction.ReadUnchanged(_personalPath,SHA256.HashData(_personalOriginal));
        GarcTransaction.ReadUnchanged(_learnsetPath,preview.LearnsetHash);
        GarcTransaction.ReadUnchanged(_evolutionPath,preview.EvolutionHash);
        GarcTransaction.ReadUnchanged(_shopPath,preview.ShopHash);
        _project.History.Commit([new FileChange(_personalPath,original,preview.PersonalOutput)],
            "MT y tutores",preview.Sanity&&preview.FollowEvolutions
                ?"Tutor Sanity y Follow Evolutions"
                :preview.Sanity?"Tutor Sanity":"Tutor Follow Evolutions",
            preview.Changes,new Dictionary<string,string>
            {
                ["TutorSanity"]=preview.Sanity.ToString(),
                ["FollowEvolutions"]=preview.FollowEvolutions.ToString(),
                ["AddedSanity"]=preview.AddedSanityFlags.ToString(),
                ["AddedFollow"]=preview.AddedInheritedFlags.ToString(),
                ["Dependencies"]="Shop.cro, levelup GARC, evolution GARC"
            });
        _project.RefreshPersonalAfterExternalEdit();
    }

    private static int CountTutorFlags(PersonalInfoSM row)=>
        row.TypeTutors.Count(flag=>flag)+row.SpecialTutors.Sum(group=>group.Count(flag=>flag));

    /// <summary>Sets tutor compatibility for every move already learned by level-up.</summary>
    internal static int ApplySanity(PersonalInfoSM[] table,Learnset6[] sets,IReadOnlyList<ushort> beach)
    {
        int added=0;
        for(int i=1;i<Math.Min(table.Length,sets.Length);i++)
        {
            var personal=table[i];
            var levelup=sets[i].Moves.ToHashSet();
            for(int tutor=0;tutor<Math.Min(TypeTutorMoves.Length,personal.TypeTutors.Length);tutor++)
                if(!personal.TypeTutors[tutor]&&levelup.Contains(TypeTutorMoves[tutor]))
                { personal.TypeTutors[tutor]=true;added++; }
            var special=personal.SpecialTutors[0];
            for(int tutor=0;tutor<Math.Min(beach.Count,special.Length);tutor++)
                if(!special[tutor]&&levelup.Contains(beach[tutor]))
                { special[tutor]=true;added++; }
        }
        return added;
    }

    /// <summary>Fixed-point inherited tutor compatibility, supports chains and branches.</summary>
    internal static int ApplyFollowEvolutions(PersonalInfoSM[] table,EvolutionSet7[] evolutions)
    {
        var edges=new HashSet<(int Source,int Target)>();
        for(int source=1;source<Math.Min(table.Length,evolutions.Length);source++)
        {
            foreach(var evo in evolutions[source].PossibleEvolutions)
            {
                if(evo.Method<=0||evo.Species<=0||evo.Species>=table.Length)continue;
                int target=table[evo.Species].FormeIndex(evo.Species,evo.Form);
                if(target<=0||target>=table.Length||target==source)continue;
                edges.Add((source,target));
            }
        }
        int added=0;
        bool changed;
        do
        {
            changed=false;
            foreach(var (source,target) in edges)
            {
                var from=table[source];
                var to=table[target];
                int type=Math.Min(from.TypeTutors.Length,to.TypeTutors.Length);
                for(int j=0;j<type;j++)
                    if(from.TypeTutors[j]&&!to.TypeTutors[j])
                    {to.TypeTutors[j]=true;added++;changed=true;}
                for(int group=0;group<Math.Min(from.SpecialTutors.Length,to.SpecialTutors.Length);group++)
                {
                    var fromBits=from.SpecialTutors[group];var toBits=to.SpecialTutors[group];
                    for(int j=0;j<Math.Min(fromBits.Length,toBits.Length);j++)
                        if(fromBits[j]&&!toBits[j])
                        {toBits[j]=true;added++;changed=true;}
                }
            }
        }while(changed);
        return added;
    }
}
