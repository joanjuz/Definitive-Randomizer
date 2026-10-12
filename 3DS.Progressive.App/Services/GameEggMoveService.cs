using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Original EggMoves6/7 serialization and original EggMoveRandomizer
/// with preview-first, scoped GARC changes and history.</summary>
public sealed class GameEggMoveService : ArchiveProjectEditor
{
    private const int MaxSlots=24;
    public GameEggMoveService(GameProjectSession project):base(project,"eggmove")
    {
        var archive=Records()["eggmove"];
        Entries=Enumerable.Range(1,archive.Length-1).Select(i=>
            new RecordEntry(i,Project.Entries.FirstOrDefault(x=>x.Index==i)?.Name
                ?? $"Especie / forma #{i:000}")).ToArray();
    }
    public override string Title=>"Egg Moves · Movimientos huevo";
    public override IReadOnlyList<EditorGroup> RandomOptions=>[
        new("Opciones originales",[
            Field("Expand","Ampliar lista de movimientos",1,0,1),
            Field("Count","Movimientos por especie",18,1,MaxSlots),
            Field("STAB","Priorizar movimientos STAB",1,0,1),
            Field("STABPercent","Porcentaje STAB",33,0,100)])
    ];
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> archives)
    {
        var egg=Make(archives["eggmove"][id]);
        var fields=new List<EditorField>();
        if(Config.Generation==7)fields.Add(Field("FormIndex","Índice de tabla para otras formas",
            egg.FormTableIndex,0,archives["eggmove"].Length-1));
        for(int i=0;i<MaxSlots;i++)fields.Add(Field($"Move{i}",
            $"Movimiento huevo {i+1:00} · 0 = vacío",i<egg.Moves.Length?egg.Moves[i]:0,
            0,Project.MoveNames.Count-1,Choices(TextName.MoveNames)));
        return [new("Movimientos huevo · slots",fields)];
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> values,
        Dictionary<string,byte[][]> archives)
    {
        var egg=Make(archives["eggmove"][id]);
        int[] ids=Enumerable.Range(0,MaxSlots).Select(i=>values[$"Move{i}"])
            .Where(x=>x>0).ToArray();
        if(ids.Distinct().Count()!=ids.Length)
            throw new InvalidDataException("Un movimiento huevo no puede repetirse en la misma especie.");
        egg.Moves=ids;
        if(Config.Generation==7)egg.FormTableIndex=values["FormIndex"];
        archives["eggmove"][id]=egg.Write();
    }
    private EggMoves Make(byte[] data)=>Config.Generation==7?new EggMoves7(data):new EggMoves6(data);
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> archives,Random random)=>
        throw new NotSupportedException("La randomización de Egg Moves usa el motor global original, no un generador alternativo.");

    public override EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        var ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!Entries.Any(e=>e.Id==id)))
            throw new InvalidDataException("El alcance contiene especies de huevo inválidas.");
        int seed=RandomNumberGenerator.GetInt32(int.MaxValue);
        var archives=Records();var all=archives["eggmove"];
        var original=all.Select(x=>x.ToArray()).ToArray();
        EggMoves[] sets=all.Select(Make).ToArray();
        var engine=new EggMoveRandomizer(Config,sets)
        {
            Expand=options["Expand"]==1,
            ExpandTo=options["Count"],
            STAB=options["STAB"]==1,
            STABPercent=options["STABPercent"],
            BannedMoves=[165,621,464,..Legal.Z_Moves]
        };
        Util.ReseedRand(seed);
        engine.Execute();
        var changes=new List<BatchChange>();
        foreach(var id in ids)
        {
            var before=ReadRecord(id,archives);
            all[id]=sets[id].Write();
            // Original randomizer can generate up to 24 slots; reject otherwise.
            if(sets[id].Moves.Length>MaxSlots)
                throw new InvalidDataException("El motor generó más slots de los admitidos por la interfaz.");
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,
                before,ReadRecord(id,archives)));
        }
        var props=options.ToDictionary(p=>p.Key,p=>p.Value.ToString());
        props["Scope"]=string.Join(",",ids);props["Seed"]=seed.ToString();
        props["ActionId"]="egg-moves.randomize";
        return new EditorProposal(ProposalOwner,"Randomizar Egg Moves",seed,
            Build(archives),changes,props);
    }
}
