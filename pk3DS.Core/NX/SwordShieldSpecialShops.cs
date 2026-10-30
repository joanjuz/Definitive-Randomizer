using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace pk3DS.Core.NX;
public static partial class SwordShieldShopClassification
{
    // Multi inventories are progression shops. Fixed inventories containing only basic supplies
    // remain normal; the other fixed inventories form the explicit special-shop scope.
    public static IReadOnlyList<int> SpecialShopIds(byte[] data)
    {
        var fb=new SwitchFlatBuffer(data);var result=new List<int>();var fixedItems=new Dictionary<int,int[]>();var protectedOffsets=new HashSet<int>();int id=0;
        foreach(int shop in fb.Tables(fb.Root,0,true)){int inv=fb.Table(shop,1);if(inv<0)throw new InvalidDataException("Inventario ausente.");var offsets=fb.Vector(inv,0,4,true);var items=offsets.Select(fb.I32).Where(i=>i>0).ToArray();if(items.Any(i=>i>44&&i is not (76 or 77 or 78 or 79))){result.Add(id);fixedItems[id]=offsets;}else foreach(int offset in offsets)protectedOffsets.Add(offset);id++;}
        foreach(int shop in fb.Tables(fb.Root,1,true))foreach(int inv in fb.Tables(shop,1,true))foreach(int offset in fb.Vector(inv,0,4,true))protectedOffsets.Add(offset);
        return result.Where(i=>!fixedItems[i].Any(protectedOffsets.Contains)).ToArray();
    }
}
