using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;
// Original code stays at its addresses. Read-only data follows segment 1, not total file size.
// Giro Vil also moves the complete move registry; registry entries follow relocation #755.
internal static class LegacyCroLayout
{
 internal const int StockSize=0x13C000;
 private const int StockRodata=0xFD000,StockMoveTable=0x105DF0,StockMoveBytes=343*8;
 internal static bool Compatible(byte[] data,int expectedSize=StockSize)=>data.Length==expectedSize||
   (Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(data)&&CroRelocationMap.TryCreate(data,out var map,out _)&&
    map.RodataStart>=StockRodata&&(map.RodataStart-StockRodata)%0x1000==0);
 internal static int StockOffset(byte[] data,int offset)
 {
  if(!Gen7BrutalSwingSelfConfusionPatcher.IsInstalled(data))return offset;
  if(!CroRelocationMap.TryCreate(data,out var map,out _))throw new System.IO.InvalidDataException("No se puede resolver el layout CRO.");
  if(offset>=StockMoveTable&&offset<StockMoveTable+StockMoveBytes)
   return checked((int)map.References[755].TargetAddress+offset-StockMoveTable);
  int delta=checked((int)map.RodataStart-StockRodata);
  if(delta<0||delta%0x1000!=0)throw new System.IO.InvalidDataException("El segmento CRO no conserva el layout auditado.");
  return offset>=StockRodata&&offset<StockSize?checked(offset+delta):offset;
 }
}
