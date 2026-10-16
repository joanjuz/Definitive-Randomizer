using System.IO;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;
public sealed partial class GameMartService
{
    internal static readonly byte[] SmRegularSignature=Convert.FromHexString("2D0000003B0000002F0000003D0000000000000000000000FFFFFFFF00000000100000000E0000000000000000000100");
    internal static readonly byte[] SmBpSignature=[9,11,13,15,17,19,20,21,9,4,8,12,5,4,11,3,10,6,10,6,4,5,7,1];
    internal static readonly int[] SmRegularCounts=[9,11,13,15,17,19,20,21,9,4,8,12,5,4,11,3,10,6,10,6,4,5,7,1];
    internal static readonly int[] SmBpCounts=[8,7,18,12,21,16];
    private readonly int _smRegularOffset,_smBpOffset;
    public bool SupportsExpansion=>_project.GameConfiguration.USUM;
    private static int FindSmTable(byte[] source,byte[] signature,int size)
    {
        int found=-1;
        for(int i=0x5000;i<=source.Length-signature.Length;i++)
            if(source.AsSpan(i,signature.Length).SequenceEqual(signature))
            {
                if(found>=0)throw new InvalidDataException("La firma de tiendas SM es ambigua.");found=i+signature.Length;
            }
        if(found<0||(long)found+size>source.Length)throw new InvalidDataException("No se reconoce la tabla de tiendas SM.");return found;
    }
    private MartShopPreview PrepareSm()
    {
        byte[] output=(byte[])_original.Clone();var changes=new List<BatchChange>();
        foreach(bool bp in new[]{false,true})
        {
            int cursor=bp?_smBpOffset:_smRegularOffset;
            foreach(var shop in bp?BpShops:RegularShops)
            {
                int expected=bp?_bpBefore[shop.Index].Length:_regularBefore[shop.Index].Length;
                if(shop.Rows.Count!=expected)throw new InvalidDataException("SM requiere conservar el número original de ranuras: "+shop.Name);
                for(int i=0;i<expected;i++)
                {
                    var row=shop.Rows[i];int old=BitConverter.ToUInt16(output,cursor);
                    if(old!=row.ItemId)changes.Add(new(shop.Index,shop.Name,$"Objeto {i+1}",ItemLabel(old),ItemLabel(row.ItemId)));
                    BitConverter.GetBytes((ushort)row.ItemId).CopyTo(output,cursor);
                    if(bp)
                    {
                        int price=BitConverter.ToUInt16(output,cursor+2);
                        if(price!=row.Price)changes.Add(new(shop.Index,shop.Name,$"BP {i+1}",price.ToString(),row.Price.ToString()));
                        BitConverter.GetBytes((ushort)row.Price).CopyTo(output,cursor+2);
                    }
                    cursor+=bp?4:2;
                }
            }
        }
        if(changes.Count>0)output=CroIntegrity.Rebuild(output);
        var items=PrepareItemPatches();changes.AddRange(items.Changes);int slots=SmRegularCounts.Sum();
        var report=new Gen7ExpandedMartBuildReport(changes.Count==0,_original.Length,output.Length,slots,slots,(uint)_smRegularOffset,(uint)_smRegularOffset,0,-1,0,0,true);
        return new((byte[])_original.Clone(),output,changes,report,items.Original,items.Output);
    }
    private int AddSmTrainingItems()
    {
        int[] wings=Wings.Select(RequireItem).ToArray();
        int heart=RequireItem("Heart Scale","Escama Corazón","Escama Corazon"),ring=RequireItem("Mega Ring","Megaaro","Mega Aro","Mega-Aro");
        string[][] healing=[["Potion","Poción","Pocion"],["Antidote","Antídoto","Antidoto"],["Paralyze Heal","Antiparalizador"],["Awakening","Despertar"],["Burn Heal","Antiquemar"],["Ice Heal","Antihielo"]];
        int[] healIds=healing.Select(a=>TryItemId(a)??-1).ToArray();
        int super=TryItemId("Super Potion","Superpoción","Superpocion")??-1,revive=TryItemId("Revive","Revivir")??-1;
        int[] ringTargets=[ring,696,TryItemId("Hyper Potion","Hiperpoción","Hiperpocion")??-1,TryItemId("Full Heal","Cura Total")??-1,TryItemId("Max Potion","Poción Máxima","Pocion Maxima")??-1,TryItemId("Full Restore","Restaurar Todo")??-1];
        var proposed=RegularShops.Take(8).Select(s=>s.Rows.Select(r=>r.ItemId).ToArray()).ToArray();
        for(int mart=0;mart<8;mart++)
        {
            int[] baseline=_regularBefore[mart].Select(id=>(int)id).ToArray();
            int ringSlot=-1;
            if(mart>=5)
            {
                foreach(int id in ringTargets.Where(id=>id>0)){ringSlot=Enumerable.Range(0,baseline.Length).FirstOrDefault(slot=>baseline[slot]==id&&(!_setCandyPrice||slot!=baseline.Length-1),-1);if(ringSlot>=0)break;}
                if(ringSlot<0)throw new InvalidDataException("No hay una ranura de curación para Megaaro en "+RegularShops[mart].Name);
            }
            for(int slot=0;slot<proposed[mart].Length;slot++)
            {
                // Presets target the original stock positions even after randomization.
                // The independent candy preset owns the final slot of these eight shops.
                if(_setCandyPrice&&slot==proposed[mart].Length-1)continue;
                int old=baseline[slot],index=Array.IndexOf(healIds,old),replacement=proposed[mart][slot];
                if(slot==ringSlot)replacement=ring;
                else if(old is >=517 and <=522)replacement=wings[old-517];
                else if(old==111)replacement=mart>=3?heart:super>0?super:old;
                else if(index>=0)replacement=wings[index];
                else if(old==super&&mart>=3)replacement=heart;
                proposed[mart][slot]=replacement;
            }
        }
        int changed=0;
        for(int mart=0;mart<8;mart++)for(int slot=0;slot<proposed[mart].Length;slot++)
            if(RegularShops[mart].Rows[slot].ItemId!=proposed[mart][slot]){RegularShops[mart].Rows[slot].ItemId=proposed[mart][slot];changed++;}
        _setKeyPrice=true;_presets.Add("marts.add-ev-items:SM-fixed-slots");return changed;
    }
}
