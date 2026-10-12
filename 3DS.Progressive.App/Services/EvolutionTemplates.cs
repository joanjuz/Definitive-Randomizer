using System.IO;
using System.Globalization;
namespace ThreeDS.Progressive.Services;

// CSV schema, method aliases and add/update/cleanup semantics from EvolutionEditor7.
internal static class EvolutionTemplates
{
    internal sealed record Row(int Source,int Target,int Method,int Level,int Argument,int Form);
    internal static IReadOnlyList<Row> Load(string path,string[] species,string[] items,int generation)
    {
        var result=new List<Row>();
        foreach(string[] fields in MoveTemplates.ReadCsv(path))
        {
            string Get(int i)=>i<fields.Length?fields[i].Trim():"";
            string first=Get(0);if(first.Length==0||first.StartsWith('#')||first.Equals("Source",StringComparison.OrdinalIgnoreCase))continue;
            int ID(string token,string[] names)=>int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out int id)?id:Array.FindIndex(names,n=>n.Equals(token,StringComparison.OrdinalIgnoreCase));
            int Number(int column,int fallback=0)=>Get(column)==""?fallback:int.TryParse(Get(column),NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)?value:throw new InvalidDataException("Número inválido en fila de evolución: "+first);
            int source=ID(first,species),target=ID(Get(1),species),method=Method(Get(2)),level=Number(3),argument=Number(4),form=Number(5,-1);
            if(Get(6).Length>0)
            {
                argument=ID(Get(6),items);if(argument<=0&&Get(7).Length>0)argument=ID(Get(7),items);
                if(argument<=0)throw new InvalidDataException("No se encontró el objeto de evolución: "+Get(6));
            }
            if(source<=0||source>=species.Length||target<=0||target>=species.Length||method<0||method>(generation==7?42:33)||level<0||level>100||argument<0||argument>65535||form< -1||form>127)
                throw new InvalidDataException("Fila de evolución fuera de rango: "+first);
            if(method==8&&argument<=0)throw new InvalidDataException("La evolución por objeto necesita un objeto válido: "+first);
            result.Add(new(source,target,method,level,argument,form));
        }
        if(result.Count==0)throw new InvalidDataException("El CSV no contiene evoluciones.");return result;
    }
    private static int Method(string value)=>int.TryParse(value,out int id)?id:value.ToLowerInvariant() switch
    {
        "level" or "levelup" or "l"=>4,"friendship" or "friend" or "f"=>1,
        "malelevel" or "levelmale" or "male" or "m"=>23,"femalelevel" or "levelfemale" or "female" or "h"=>24,
        "useditem" or "item" or "stone" or "u"=>8,_=>4
    };
    internal static void Apply(Dictionary<string,int> values,IEnumerable<Row> rows,int generation)
    {
        var patches=rows.ToArray();int source=patches[0].Source;
        // The original normalizer removes only its two known historical mistakes.
        for(int i=0;i<8;i++)
        {
            string k=$"E{i}.";
            if(source==356&&values[k+"Species"]==477&&values[k+"Method"]==4||source==370&&values[k+"Species"]==594&&values[k+"Method"]==1)
            {values[k+"Species"]=values[k+"Method"]=values[k+"Argument"]=0;if(generation==7){values[k+"Form"]=-1;values[k+"Level"]=0;}}
        }
        foreach(var row in patches)
        {
            int slot=Enumerable.Range(0,8).FirstOrDefault(i=>values[$"E{i}.Species"]==row.Target&&values[$"E{i}.Method"]==row.Method&&(row.Method!=8||values[$"E{i}.Argument"]==row.Argument),-1);
            if(slot<0)slot=Enumerable.Range(0,8).FirstOrDefault(i=>values[$"E{i}.Species"]==0&&values[$"E{i}.Method"]==0,-1);
            if(slot<0)throw new InvalidDataException($"La especie {row.Source} ya tiene sus ocho rutas ocupadas. No se sobrescribió ninguna evolución.");
            string k=$"E{slot}.";values[k+"Species"]=row.Target;values[k+"Method"]=row.Method;
            values[k+"Argument"]=generation==6&&row.Level>0?row.Level:row.Argument;
            if(generation==7){values[k+"Form"]=row.Form;values[k+"Level"]=row.Level;}
        }
    }
}
