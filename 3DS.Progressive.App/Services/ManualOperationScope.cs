using System.Text.Json;
using System.Text.Json.Nodes;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;

internal sealed class ManualOperationScope : IDisposable
{
    private static readonly AsyncLocal<JsonObject?> CurrentSlot=new();
    private readonly JsonObject? _previous=CurrentSlot.Value;
    internal static JsonObject? Current=>CurrentSlot.Value;
    private ManualOperationScope(JsonObject value)=>CurrentSlot.Value=_previous?["Kind"]?.GetValue<string>()=="authored-template"?_previous:value;
    internal static ManualOperationScope Begin(string kind,string key,object payload)=>new(new JsonObject
    { ["Kind"]=kind,["Key"]=key,["Payload"]=JsonSerializer.SerializeToNode(payload) });
    internal static ManualOperationScope Record(IProjectEditor editor,int id,IReadOnlyDictionary<string,int> values,IReadOnlyDictionary<string,int>? original=null)
    {
        original??=EditorValues.Flatten(editor.Read(id));
        var fields=values.Where(p=>!original.TryGetValue(p.Key,out int before)||p.Value!=before).Select(p=>p.Key).ToHashSet();
        if(original.TryGetValue("Count",out int oldCount)&&values.TryGetValue("Count",out int count)&&count>oldCount)
            for(int i=oldCount;i<count;i++)foreach(string key in values.Keys.Where(k=>k.StartsWith("P"+i+".",StringComparison.Ordinal)||k=="Choice"+i))fields.Add(key);
        if(editor is GameEggMoveService&&fields.Any(k=>k.StartsWith("Move",StringComparison.Ordinal)))foreach(string key in values.Keys.Where(k=>k.StartsWith("Move",StringComparison.Ordinal)))fields.Add(key);
        return Begin("record",editor.GetType().Name+":"+editor.Title+":"+id,new{Editor=editor.GetType().Name,Title=editor.Title,Id=id,Values=values,Fields=fields.Order().ToArray()});
    }
    internal static ManualOperationScope Flags(byte[] before,byte[] after,string key)
    {
        var a=new pk3DS.Core.CTR.GARC.MemGARC(before).Files;var b=new pk3DS.Core.CTR.GARC.MemGARC(after).Files;
        if(a.Length!=b.Length)throw new System.IO.InvalidDataException("Cambió la tabla Personal de compatibilidades.");
        var bits=new List<object>();
        for(int id=1;id<a.Length-1;id++)
        {
            if(a[id].Length!=b[id].Length)throw new System.IO.InvalidDataException("Cambió el tamaño de Personal.");
            for(int offset=0;offset<a[id].Length;offset++){int mask=a[id][offset]^b[id][offset];if(mask!=0)bits.Add(new{Id=id,Size=a[id].Length,Offset=offset,Mask=mask,Bits=b[id][offset]&mask});}
        }
        return Begin("personal-flags",key+":"+Guid.NewGuid().ToString("N"),new{Bits=bits});
    }
    public void Dispose()=>CurrentSlot.Value=_previous;
}
