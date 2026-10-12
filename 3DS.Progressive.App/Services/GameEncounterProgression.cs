using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// ProgressiveWildRandomizer.GetTableProgressionLevel + GetAreaProgressionLevel,
/// without a live WinForms Area7 object. Freeze fallback before modifying
/// encounter levels across a batch of day/night tables.
/// </summary>
public sealed partial class GameEncounterService
{
    private readonly Dictionary<int,int[]> _zoneIndicesByFile = [];
    private Dictionary<int,int>? _previewAreaFallback;

    internal static int ResolveProgressionLevel(int minimum,int maximum,
        IReadOnlyList<int> species,int areaFallback)
    {
        if(minimum<=0&&maximum<=0 || species.Count==0 ||
           species.All(id=>id==0)||species.All(id=>id==731))
            return Math.Clamp(areaFallback,1,100);
        return Math.Clamp(Math.Max(minimum,maximum),1,100);
    }

    private bool AccessibleForProgression(Location entry)
    {
        if(!_zoneIndicesByFile.TryGetValue(entry.File,out var zones)||zones.Length==0)
            return true;
        var ignored=Config.USUM?Gen7SlotDumper.InaccessibleUnused_USUM:
            Gen7SlotDumper.InaccessibleUnused_SM;
        int number=entry.Table+1;
        return zones.Any(index=>!ignored.TryGetValue(index,out var inaccessible)||
                                !inaccessible.Contains(number));
    }

    private int AreaFallback(int file,Dictionary<string,byte[][]> records)
    {
        var entries=_locations.Values.Where(l=>l.File==file).ToArray();
        int best=0,any=0;
        var tables=Mini.UnpackMini(records["encdata"][file],"EA");
        foreach(var l in entries)
        {
            byte[] bytes=tables[l.Table];
            int offset=l.Offset;
            int lo=bytes[offset],hi=bytes[offset+1];
            int level=Math.Max(lo,hi);
            any=Math.Max(any,level);
            bool usable=level>0&&AccessibleForProgression(l);
            bool occupied=false,allPikipek=true;
            for(int i=0;i<10;i++)
            {
                int species=(int)(BitConverter.ToUInt32(bytes,offset+12+i*4)&0x7FF);
                if(species!=0)occupied=true;
                if(species!=731)allPikipek=false;
            }
            if(usable&&occupied&&!allPikipek)
                best=Math.Max(best,level);
        }
        return Math.Clamp(best>0?best:any>0?any:1,1,100);
    }

    private int ProgressionLevel(int id,Dictionary<string,byte[][]> records,
        IReadOnlyDictionary<string,int> values)
    {
        var l=_locations[id];
        int fallback=_previewAreaFallback is not null&&
            _previewAreaFallback.TryGetValue(l.File,out var saved)
            ?saved:AreaFallback(l.File,records);
        int[] species=Enumerable.Range(0,10)
            .Select(i=>values["S0."+i+".Species"]).ToArray();
        return ResolveProgressionLevel(values["Min"],values["Max"],species,fallback);
    }

    public override EditorProposal Preview(IReadOnlyDictionary<string,int> options,
        IReadOnlyList<int> scope)
    {
        if(Config.Generation!=7 || !options.TryGetValue("Progressive",out int progressive)
            ||progressive!=1)return base.Preview(options,scope);
        var original=Records();
        _previewAreaFallback=_locations.Values.Select(x=>x.File).Distinct()
            .ToDictionary(file=>file,file=>AreaFallback(file,original));
        try { return base.Preview(options,scope); }
        finally { _previewAreaFallback=null; }
    }
}
