using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

public sealed record TextMapReference(int Zone, string Location);
internal static class TextMapResolver
{
    // Gen6 ZoneData.Size, TextFile and ParentMap match the original OWSE reader.
    internal static Dictionary<int, TextMapReference[]> Read(GameConfig config,int bankCount)
    {
        if(config.Generation==6)return ReadGen6(config,bankCount);
        if(config.Generation!=7)return [];
        string path=Path.Combine(config.RomFS,config.GetGARCFileName("zonedata"));
        if(!File.Exists(path))return [];
        return ParseGen7(config.GetlzGARCData("zonedata")[0],config.GetText(TextName.metlist_000000),bankCount);
    }
    // Text-bank field at 0x0C cross-checked against unique Bulbapedia dialogue
    // matches and English map names in both SM and USUM; parent name is 0x1C.
    internal static Dictionary<int, TextMapReference[]> ParseGen7(byte[] zones,string[] locations,int bankCount)
    {
        if(zones.Length==0||zones.Length%0x54!=0)return [];
        var result=new Dictionary<int,List<TextMapReference>>();
        for(int offset=0;offset<zones.Length;offset+=0x54)
        {
            int text=BitConverter.ToInt32(zones,offset+0x0C),place=BitConverter.ToInt32(zones,offset+0x1C);
            if(text<0||text>=bankCount||place<0||place>=locations.Length||string.IsNullOrWhiteSpace(locations[place]))continue;
            if(!result.TryGetValue(text,out var list))result[text]=list=[];
            list.Add(new(offset/0x54,locations[place]));
        }
        return result.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
    }
    internal static Dictionary<int, TextMapReference[]> ReadGen6(GameConfig config, int bankCount)
    {
        if(config.Generation != 6) return [];
        string path=Path.Combine(config.RomFS,config.GetGARCFileName("encdata"));
        if(!File.Exists(path)) return [];
        var archive=config.GetlzGARCData("encdata");
        var locations=config.GetText(TextName.metlist_000000);
        var result=new Dictionary<int,List<TextMapReference>>();
        for(int file=0;file<archive.FileCount;file++)
        {
            byte[] record=archive[file];
            if(record.Length<4 || record[0]!='Z' || record[1]!='O')continue;
            int count=BitConverter.ToUInt16(record,2);
            if(count is not (4 or 5) || record.Length<8+4*count)continue;
            int start=BitConverter.ToInt32(record,4),end=BitConverter.ToInt32(record,8);
            if(start<8+4*count || end<start || end>record.Length || end-start!=0x38)continue;
            foreach(var pair in ParseGen6(record[start..end],locations,bankCount))
            {
                if(!result.TryGetValue(pair.Key,out var list))result[pair.Key]=list=[];
                list.AddRange(pair.Value.Select(r=>r with{Zone=file}));
            }
        }
        return result.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
    }
    internal static Dictionary<int, TextMapReference[]> ParseGen6(byte[] zones, string[] locations, int bankCount)
    {
        if(zones.Length==0 || zones.Length%0x38!=0) return [];
        var result=new Dictionary<int,List<TextMapReference>>();
        for(int offset=0;offset<zones.Length;offset+=0x38)
        {
            int text=BitConverter.ToUInt16(zones,offset+6);
            int place=BitConverter.ToUInt16(zones,offset+0x1C)&0x3FF;
            if(text>=bankCount || place>=locations.Length || string.IsNullOrWhiteSpace(locations[place])) continue;
            if(!result.TryGetValue(text,out var references))result[text]=references=[];
            references.Add(new(offset/0x38,locations[place]));
        }
        return result.ToDictionary(p=>p.Key,p=>p.Value.ToArray());
    }
}
