#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
namespace pk3DS.Core.NX;
/// <summary>Materializes omitted zero scalar fields in the known SWSH encounter schema.
/// Original bytes and unknown siblings survive; only selected subtable references are redirected.
/// No unknown or pointer-bearing slot fields are reserialized.</summary>
public static class SwordShieldEncounterExpansion
{
    public static byte[] Materialize(byte[] source,string member,IReadOnlyList<string>? scope)
    {
        var fb=new SwitchFlatBuffer(source);var output=new List<byte>(source);int zoneIndex=0;bool changed=false;
        if(scope is not null)
        {
            var references=new Dictionary<int,bool>();int zi=0;
            foreach(int zone in fb.Tables(fb.Root,1,true)){int wi=0;foreach(int pointer in fb.Vector(zone,1,4,true)){bool selected=scope.Contains($"{member}/{zi}/{wi++}");if(references.TryGetValue(pointer,out bool previous)&&previous!=selected)throw new InvalidDataException("El alcance separa referencias de subtablas compartidas. Selecciona todas sus regiones antes de expandir.");references[pointer]=selected;}zi++;}
        }
        void Known(int table){int vt=table-fb.I32(table),size=fb.U16(vt);for(int at=10;at<size;at+=2)if(fb.U16(vt+at)!=0)throw new InvalidDataException("La subtabla de encuentros contiene campos desconocidos; no se expandirá.");}
        int Read(int table,int field,int width){int at=fb.Field(table,field,width);return at<0?0:width==4?fb.I32(at):fb.Data[at];}
        void Align(){while(output.Count%4!=0)output.Add(0);}
        void U16(int at,int value)=>BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(output).Slice(at,2),(ushort)value);
        void I32(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(output).Slice(at,4),value);
        int ScalarTable(){Align();int vt=output.Count;output.AddRange(new byte[12]);int table=output.Count;output.AddRange(new byte[12]);U16(vt,10);U16(vt+2,12);U16(vt+4,4);U16(vt+6,5);U16(vt+8,8);I32(table,table-vt);return table;}
        foreach(int zone in fb.Tables(fb.Root,1,true))
        {
            int weather=0;foreach(int pointer in fb.Vector(zone,1,4,true))
            {
                int sub=fb.Reference(pointer);string region=$"{member}/{zoneIndex}/{weather++}";if(scope is not null&&!scope.Contains(region))continue;
                var slots=fb.Tables(sub,2,true);if(slots.Length==0||slots.All(s=>Read(s,1,4)>0))continue;if(slots.Length>100)throw new InvalidDataException("Demasiadas ranuras para expandir probabilidades.");
                if(fb.Field(sub,0,1)>=0&&fb.Field(sub,1,1)>=0&&slots.All(s=>Read(s,1,4)>0||(fb.Field(s,0,1)>=0&&fb.Field(s,1,4)>=0)))continue;
                Known(sub);foreach(int slot in slots)Known(slot);
                int replacement=ScalarTable();output[replacement+4]=(byte)Read(sub,0,1);output[replacement+5]=(byte)Read(sub,1,1);I32(pointer,replacement-pointer);
                Align();int vector=output.Count;output.AddRange(new byte[4+4*slots.Length]);I32(vector,slots.Length);I32(replacement+8,vector-(replacement+8));
                for(int i=0;i<slots.Length;i++)
                {
                    // Slot layout differs from subtable: probability byte, species int, form byte.
                    int slot=ScalarTable(),vt=slot-12;U16(vt+6,8);U16(vt+8,5);output[slot+4]=(byte)Read(slots[i],0,1);output[slot+5]=(byte)Read(slots[i],2,1);I32(slot+8,Read(slots[i],1,4));int reference=vector+4+4*i;I32(reference,slot-reference);
                }
                changed=true;
            }
            zoneIndex++;
        }
        if(!changed)return (byte[])source.Clone();var result=output.ToArray();var reopened=new SwitchFlatBuffer(result);zoneIndex=0;
        foreach(int zone in reopened.Tables(reopened.Root,1,true)){int weather=0;foreach(int sub in reopened.Tables(zone,1,true)){string region=$"{member}/{zoneIndex}/{weather++}";foreach(int slot in reopened.Tables(sub,2,true)){_ = reopened.Field(slot,1,4);}if(scope is not null&&!scope.Contains(region))continue;}zoneIndex++;}
        return result;
    }
}
