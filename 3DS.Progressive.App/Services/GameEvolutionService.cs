using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class GameEvolutionService : ArchiveProjectEditor
{
    public GameEvolutionService(GameProjectSession project):base(project,"evolution")
    {
        var records=Records()["evolution"];var species=Config.GetText(TextName.SpeciesNames);
        Entries=Enumerable.Range(1,records.Length-1).Select(i=>new RecordEntry(i,
            Project.Entries.FirstOrDefault(p=>p.Index==i)?.Name??(i<species.Length?species[i]:$"Entrada {i}"))).ToArray();
    }
    public override string Title=>"Evoluciones";
    public override IReadOnlyList<EditorGroup> RandomOptions=>[new("Evoluciones y acceso",[
        Field("Species","Randomizar destinos de evolución",1,0,1),
        Field("BST","Tolerancia BST del destino (%) · 0 = libre",30,0,100),
        Field("Trade","Cambiar evoluciones por intercambio a nivel",0,0,1),
        Field("TradeLevel","Nivel para evoluciones por intercambio",37,1,100)])];
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        byte[] record=records["evolution"][id];int size=Config.Generation==7?8:6;
        if(record.Length!=size*8)throw new InvalidDataException("La tabla de evoluciones no tiene ocho entradas válidas.");
        var groups=new List<EditorGroup>();
        for(int i=0;i<8;i++)
        {
            int at=i*size;string k=$"E{i}.";
            var fields=new List<EditorField>{Field(k+"Method","Método",BitConverter.ToUInt16(record,at),0,65535,Methods),
                Field(k+"Argument","Parámetro del método · nivel / objeto / movimiento",BitConverter.ToUInt16(record,at+2),0,65535),
                Field(k+"Species","Especie resultante",BitConverter.ToUInt16(record,at+4),0,Config.MaxSpeciesID,SpeciesChoices)};
            if(Config.Generation==7){fields.Add(Field(k+"Form","Forma resultante · -1 = conservar",unchecked((sbyte)record[at+6]),-1,127));fields.Add(Field(k+"Level","Nivel requerido",record[at+7],0,100));}
            groups.Add(new($"Evolución {i+1}",fields,i==0||record[at]!=0));
        }
        return groups;
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> values,Dictionary<string,byte[][]> records)
    {
        byte[] record=records["evolution"][id];int size=Config.Generation==7?8:6;
        for(int i=0;i<8;i++)
        {
            int at=i*size;string k=$"E{i}.";
            foreach(var pair in new[]{("Method",0),("Argument",2),("Species",4)})BitConverter.GetBytes((ushort)values[k+pair.Item1]).CopyTo(record,at+pair.Item2);
            if(Config.Generation==7){record[at+6]=unchecked((byte)(sbyte)values[k+"Form"]);record[at+7]=(byte)values[k+"Level"];}
        }
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,Dictionary<string,byte[][]> records,Random random)
    {
        var values=EditorValues.Flatten(ReadRecord(id,records));
        for(int i=0;i<8;i++)
        {
            string k=$"E{i}.";if(values[k+"Method"]==0)continue;
            if(options["Species"]==1&&values[k+"Species"]>0){values[k+"Species"]=PickSpecies(values[k+"Species"],random,options["BST"]);if(Config.Generation==7)values[k+"Form"]=0;}
            if(options["Trade"]==1&&values[k+"Method"] is 5 or 6 or 7)
            {values[k+"Method"]=4;values[k+"Argument"]=Config.Generation==6?options["TradeLevel"]:0;if(Config.Generation==7)values[k+"Level"]=options["TradeLevel"];}
        }
        return values;
    }
    private static readonly string[] MethodNames=["Ninguna","Amistad","Amistad de día","Amistad de noche","Subir de nivel","Intercambio","Intercambio con objeto","Intercambio Shelmet/Karrablast","Usar objeto","Ataque > Defensa","Ataque = Defensa","Ataque < Defensa","Azar < 5","Azar ≥ 5","Ninjask","Shedinja","Belleza","Objeto · macho","Objeto · hembra","Objeto equipado de día","Objeto equipado de noche","Conocer movimiento","Especie en el equipo","Nivel · macho","Nivel · hembra","Zona eléctrica","Bosque","Zona fría","Consola invertida","Afecto y tipo de movimiento","Tipo Siniestro en el equipo","Lluvia","Nivel de día","Nivel de noche","Hembra · forma 1","Sin uso","Versión del juego","De día · versión","De noche · versión","Monte","Atardecer","Ultraespacio · nivel","Ultraespacio · objeto"];
    private IReadOnlyList<MoveChoice> Methods=>MethodNames.Take(Config.Generation==6?34:Config.USUM?43:40).Select((n,i)=>new MoveChoice(i,n)).ToArray();
}
