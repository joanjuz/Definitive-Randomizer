using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

// The audited V2 installer inserts complete code pages before the read-only/data segments.
// Code addresses remain fixed; stock descriptor/registry addresses move with those segments.
internal static class LegacyCroLayout
{
    internal const int StockSize=0x13C000;
    internal static bool Compatible(byte[] data,int expectedSize=StockSize)=>data.Length==expectedSize||
        (data.Length>expectedSize&&(data.Length-expectedSize)%0x1000==0&&
         Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(data)&&CroRelocationMap.TryCreate(data,out _,out _));
    internal static int StockOffset(byte[] data,int offset)
    {
        if(data.Length==StockSize)return offset;
        if(!Compatible(data))return offset;
        int delta=data.Length-StockSize;
        int segments=checked((int)BitConverter.ToUInt32(data,0xC8));
        int rodata=checked((int)BitConverter.ToUInt32(data,segments+12));
        int originalRodata=checked(rodata-delta);
        if(originalRodata<0xF0000||originalRodata>0x110000)
            throw new System.IO.InvalidDataException("La expansión de Battle.cro no conserva el layout auditado.");
        return offset>=originalRodata&&offset<StockSize?checked(offset+delta):offset;
    }
}
