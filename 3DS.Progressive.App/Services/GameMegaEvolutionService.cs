using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Preserve original two-record Mega table, including undocumented u6 bytes.</summary>
public sealed class GameMegaEvolutionService : ArchiveProjectEditor
{
    public GameMegaEvolutionService(GameProjectSession project):base(project,"megaevo")
    {
        var all=Records()["megaevo"];
        Entries=Enumerable.Range(1,Math.Min(all.Length-1,Config.MaxSpeciesID))
            .Select(i=>new RecordEntry(i,Project.Entries.FirstOrDefault(x=>x.Index==i)?.Name
                ?? $"Pokémon #{i:000}")).ToArray();
    }
    internal bool GameIsUsum=>Config.USUM;
    internal GameProjectSession Session=>Project;
    public override string Title=>"Mega Evolutions";
    public override IReadOnlyList<EditorGroup> RandomOptions=>[];
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> data)
    {
        var mega=new MegaEvolutions(data["megaevo"][id]);
        if(mega.Method is null||mega.Method.Length!=2)
            throw new InvalidDataException("La entrada Mega Evolution no tiene los dos slots originales.");
        return Enumerable.Range(0,2).Select(i=>(EditorGroup)new EditorGroup($"Mega Evolución {i+1}",[
            Field($"Slot{i}.Enabled","Habilitada",mega.Method[i]==1?1:0,0,1),
            Field($"Slot{i}.Item","Piedra activadora",mega.Argument[i],0,Project.ItemNames.Count-1,
                Choices(TextName.ItemNames)),
            Field($"Slot{i}.Form","Forma Mega resultante",mega.Form[i],0,Math.Max(0,Config.Personal[id].FormeCount-1))
        ],true)).ToArray();
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> fields,
        Dictionary<string,byte[][]> data)
    {
        var original=data["megaevo"][id];var mega=new MegaEvolutions(original);
        if(mega.Method is null||mega.Method.Length!=2)
            throw new InvalidDataException("Tabla Mega no compatible.");
        for(int i=0;i<2;i++)
        {
            bool enabled=fields[$"Slot{i}.Enabled"]==1;
            if(mega.Method[i]>1)throw new InvalidDataException("Método Mega no admitido por esta interfaz.");
            mega.Method[i]=(ushort)(enabled?1:0);
            mega.Argument[i]=(ushort)(enabled?fields[$"Slot{i}.Item"]:0);
            mega.Form[i]=(ushort)(enabled?fields[$"Slot{i}.Form"]:0);
        }
        byte[] output=mega.Write();
        if(id==384&&!output.AsSpan().SequenceEqual(original))
            throw new InvalidDataException("Rayquaza utiliza Ascenso Draco; no se permite cambiar su regla especial.");
        data["megaevo"][id]=output;
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,
        Dictionary<string,byte[][]> records,Random random)=>
        throw new NotSupportedException("El editor Mega original no ofrece randomización masiva.");
}
