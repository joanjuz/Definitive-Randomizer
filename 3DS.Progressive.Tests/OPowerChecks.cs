using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class OPowerChecks
{
    internal static (string Path,byte[] Bytes,int Offset) Setup(Fixture f,int padding=3)
    {
        byte[] bytes=Enumerable.Repeat((byte)0xA5,0x402000).ToArray();GameOPowerService.Signature.CopyTo(bytes,0x400100);
        int offset=0x400100+GameOPowerService.Signature.Length;bytes.AsSpan(offset,padding).Fill(255);offset+=padding;
        for(int i=0;i<65;i++)
        {
            int at=offset+22*i;bytes[at]=(byte)(i%3);bytes[at+1]=2;bytes[at+2]=0x31;bytes[at+3]=5;bytes[at+4]=6;bytes[at+5]=0xC1;
            foreach(int n in new[]{6,8,10,12})BitConverter.GetBytes((ushort)(i+n)).CopyTo(bytes,at+n);
            bytes[at+14]=1;bytes[at+15]=4;BitConverter.GetBytes((ushort)7).CopyTo(bytes,at+16);BitConverter.GetBytes((ushort)100).CopyTo(bytes,at+18);
            bytes[at+20]=255;bytes[at+21]=0xAB;
        }
        string path=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(path,bytes);return(path,bytes,offset);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})foreach(int padding in new[]{0,3})
        {
            using var f=new Fixture(version);var data=Setup(f,padding);string tag=$"{version} O-Powers padding {padding} ";var service=new GameOPowerService(f.Session);
            check(service.Entries.Count==64&&service.Entries[0].Id==1&&service.Entries[^1].Id==64,tag+"64 editable entries excluding record zero");
            var values=EditorValues.Flatten(service.Read(64));check(values["Duration"]==255&&values["Efficacy"]==100,tag+"last record and special duration decoded");
            values["PlayerCost"]=9;values["OtherCost"]=10;values["Stage"]=2;values["LevelUp"]=12;values["Efficacy"]=65535;values["Duration"]=80;values["Usability"]=254;
            service.Save(64,values);byte[] expected=(byte[])data.Bytes.Clone();int at=data.Offset+64*22;
            expected[at+3]=9;expected[at+4]=10;expected[at+14]=2;expected[at+15]=12;expected[at+20]=80;expected[at+1]=254;BitConverter.GetBytes(ushort.MaxValue).CopyTo(expected,at+18);
            check(File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"only vanilla editable fields change; quality flags references sort padding and zero preserved");
            check(EditorValues.Flatten(service.Read(64))["Efficacy"]==65535&&f.Session.History.Read().Entries.Last().Module==service.Title,tag+"readback and history verified");
            int count=f.Session.History.Read().Entries.Count;service.Save(64,values);check(f.Session.History.Read().Entries.Count==count&&File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"no-op save preserves bytes and history");
            values["PlayerCost"]=256;reject(()=>service.Save(64,values),tag+"byte overflow rejected");values["PlayerCost"]=9;
            values["Text12"]++;reject(()=>service.Save(64,values),tag+"read-only text reference protected");values["Text12"]--;
            values["Usability"]=1;reject(()=>service.Save(64,values),tag+"unknown new usability rejected");
            reject(()=>service.Read(0),tag+"record zero protected");
            expected[data.Offset+22+1]=33;File.WriteAllBytes(data.Path,expected);service=new GameOPowerService(f.Session);values=EditorValues.Flatten(service.Read(1));values["PlayerCost"]=11;service.Save(1,values);
            expected[data.Offset+22+3]=11;check(File.ReadAllBytes(data.Path).SequenceEqual(expected)&&values["Usability"]==33,tag+"unknown original usability preserved while editing other fields");
            values["Usability"]=0;service.Save(1,values);expected[data.Offset+22+1]=0;check(File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"known usability transition allowed");
            values["PlayerCost"]=12;expected[^1]^=1;File.WriteAllBytes(data.Path,expected);reject(()=>service.Save(1,values),tag+"external change blocks save");check(File.ReadAllBytes(data.Path).SequenceEqual(expected),tag+"external bytes preserved");
            reject(()=>service.Apply(new EditorProposal(Guid.NewGuid(),"foreign",0,[],[],new Dictionary<string,string>())),tag+"foreign proposal rejected");
            byte[] bad=(byte[])data.Bytes.Clone();GameOPowerService.Signature.CopyTo(bad,0x400F00);File.WriteAllBytes(data.Path,bad);reject(()=>new GameOPowerService(f.Session),tag+"duplicate marker rejected");
            bad=(byte[])data.Bytes.Clone();bad.AsSpan(0x400100,GameOPowerService.Signature.Length).Clear();File.WriteAllBytes(data.Path,bad);reject(()=>new GameOPowerService(f.Session),tag+"missing marker rejected");
            bad=(byte[])data.Bytes.Clone();BitConverter.GetBytes(ushort.MaxValue).CopyTo(bad,data.Offset+22+12);File.WriteAllBytes(data.Path,bad);reject(()=>new GameOPowerService(f.Session),tag+"invalid text reference rejected");
            bad=new byte[0x400200];GameOPowerService.Signature.CopyTo(bad,0x400100);File.WriteAllBytes(data.Path,bad);reject(()=>new GameOPowerService(f.Session),tag+"short table rejected");
            bad=Enumerable.Repeat((byte)255,0x400200).ToArray();GameOPowerService.Signature.CopyTo(bad,0x400100);File.WriteAllBytes(data.Path,bad);reject(()=>new GameOPowerService(f.Session),tag+"unterminated FF padding rejected safely");
        }
    }
}
