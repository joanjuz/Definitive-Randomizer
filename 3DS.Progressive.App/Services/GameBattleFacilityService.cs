using System.IO;
using System.Text;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class GameBattleFacilityService : ArchiveProjectEditor
{
    private readonly string _trainer,_pokemon;
    private readonly int _trainerCount,_pokemonCount;
    private readonly bool _alternate;
    private readonly IReadOnlyList<MoveChoice> _pokemonChoices;
    public GameBattleFacilityService(GameProjectSession project,bool alternate):base(project,"maisontr"+(alternate?"S":"N"),"maisonpk"+(alternate?"S":"N"))
    {
        _alternate=alternate;_trainer="maisontr"+(alternate?"S":"N");_pokemon="maisonpk"+(alternate?"S":"N");
        var records=Records();_trainerCount=records[_trainer].Length;_pokemonCount=records[_pokemon].Length;
        if(_pokemonCount==0||_pokemonCount>ushort.MaxValue)throw new InvalidDataException("Catálogo de instalación incompleto.");
        _pokemonChoices=Enumerable.Range(0,_pokemonCount).Select(i=>new MoveChoice(i,$"Pokémon {i:000}")).ToArray();
        var names=Config.GetText(Config.Generation==6?(alternate?TextName.SuperTrainerNames:TextName.MaisonTrainerNames):(alternate?TextName.BattleRoyalNames:TextName.BattleTreeNames));
        Entries=Enumerable.Range(0,_trainerCount).Select(i=>new RecordEntry(i,$"Entrenador {i:000} · "+(i<names.Length?names[i]:"Sin nombre")))
            .Concat(Enumerable.Range(0,_pokemonCount).Select(i=>new RecordEntry(_trainerCount+i,$"Pokémon {i:000}"))).ToArray();
        foreach(byte[] data in records[_trainer])if(data.Length<4||4+2*BitConverter.ToUInt16(data,2)>data.Length)throw new InvalidDataException("Entrenador de instalación truncado.");
        if(records[_pokemon].Any(p=>p.Length<16))throw new InvalidDataException("Pokémon de instalación truncado.");
    }
    public override string Title=>Config.Generation==6?(_alternate?"Super Maison · Gen6":"Battle Maison · Gen6"):(_alternate?"Battle Royal · Gen7":"Battle Tree · Gen7");
    public override IReadOnlyList<EditorGroup> RandomOptions=>[];
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,Dictionary<string,byte[][]> records,Random random)=>throw new NotSupportedException("El editor original de instalaciones no tiene randomizador.");
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        if(id<0||id>=_trainerCount+_pokemonCount)throw new ArgumentOutOfRangeException(nameof(id));
        if(id<_trainerCount)
        {
            byte[] data=records[_trainer][id];if(data.Length<4)throw new InvalidDataException("Entrenador de instalación truncado.");
            int count=BitConverter.ToUInt16(data,2);if(4+count*2>data.Length)throw new InvalidDataException("Lista de Pokémon truncada.");
            int capacity=Math.Min(ushort.MaxValue,Math.Max(count,_pokemonCount));
            var fields=new List<EditorField>{Field("Class","Clase",BitConverter.ToUInt16(data,0),0,ushort.MaxValue,Choices(TextName.TrainerClasses)),Field("Count","Cantidad de opciones Pokémon",count,0,capacity)};
            for(int i=0;i<capacity;i++)fields.Add(Field("Choice"+i,$"Opción {i+1:000}",i<count?BitConverter.ToUInt16(data,4+2*i):0,0,ushort.MaxValue,_pokemonChoices));
            return [new("Entrenador · solo las primeras Cantidad opciones están activas",fields)];
        }
        byte[] p=records[_pokemon][id-_trainerCount];if(p.Length<16)throw new InvalidDataException("Pokémon de instalación truncado.");
        var values=new List<EditorField>{Field("Species","Especie",BitConverter.ToUInt16(p,0),0,ushort.MaxValue,SpeciesChoices),Field("Nature","Naturaleza",p[11],0,255,Choices(TextName.Natures)),Field("Item","Objeto",BitConverter.ToUInt16(p,12),0,ushort.MaxValue,Choices(TextName.ItemNames))};
        if(Config.Generation==7)values.Add(Field("Form","Forma",BitConverter.ToUInt16(p,14),0,ushort.MaxValue));
        for(int i=0;i<4;i++)values.Add(Field("Move"+i,$"Movimiento {i+1}",BitConverter.ToUInt16(p,2+2*i),0,ushort.MaxValue,Choices(TextName.MoveNames)));
        string[] stats=["PS","Ataque","Defensa","Velocidad","At. Especial","Def. Especial"];
        for(int i=0;i<6;i++)values.Add(Field("EV"+i,"EV · "+stats[i],(p[10]>>i)&1,0,1));
        return [new("Pokémon de instalación",values)];
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> values,Dictionary<string,byte[][]> records)
    {
        if(id<_trainerCount)
        {
            byte[] old=records[_trainer][id];int oldCount=BitConverter.ToUInt16(old,2),count=values["Count"];
            ushort[] choices=Enumerable.Range(0,count).Select(i=>(ushort)values["Choice"+i]).ToArray();
            for(int i=0;i<count;i++)if(choices[i]>=_pokemonCount&&(i>=oldCount||choices[i]!=BitConverter.ToUInt16(old,4+2*i)))throw new InvalidDataException("La opción apunta fuera del catálogo Pokémon.");
            int tail=4+2*oldCount;byte[] output=new byte[4+2*count+old.Length-tail];old.AsSpan(tail).CopyTo(output.AsSpan(4+2*count));
            BitConverter.GetBytes((ushort)values["Class"]).CopyTo(output,0);BitConverter.GetBytes((ushort)count).CopyTo(output,2);
            for(int i=0;i<count;i++)BitConverter.GetBytes(choices[i]).CopyTo(output,4+2*i);
            records[_trainer][id]=output;return;
        }
        byte[] p=records[_pokemon][id-_trainerCount];
        foreach(var (name,at) in new[]{("Species",0),("Item",12),("Move0",2),("Move1",4),("Move2",6),("Move3",8)})BitConverter.GetBytes((ushort)values[name]).CopyTo(p,at);
        p[11]=(byte)values["Nature"];p[10]=(byte)((p[10]&0xC0)|Enumerable.Range(0,6).Sum(i=>values["EV"+i]<<i));
        if(Config.Generation==7)BitConverter.GetBytes((ushort)values["Form"]).CopyTo(p,14);
    }
    public string ExportText()
    {
        var records=Records();var result=new StringBuilder(Title+Environment.NewLine);
        foreach(var entry in Entries){result.AppendLine(entry.Name);foreach(var field in ReadRecord(entry.Id,records).SelectMany(g=>g.Fields).Where(f=>!f.Key.StartsWith("Choice")||int.Parse(f.Key[6..])<BitConverter.ToUInt16(records[_trainer][entry.Id],2)))result.AppendLine(field.Label+": "+field.Value);result.AppendLine();}
        return result.ToString();
    }
}
