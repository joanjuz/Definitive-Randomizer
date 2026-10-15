using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class CroIntegrityChecks
{
    internal static byte[] Cro(int marker)
    {
        byte[] bytes=Enumerable.Range(0,0x380).Select(i=>(byte)(i*7+marker)).ToArray();
        "CRO0"u8.CopyTo(bytes.AsSpan(0x80));
        BitConverter.GetBytes(0x180).CopyTo(bytes,0xB0);BitConverter.GetBytes(0x100).CopyTo(bytes,0xB4);
        BitConverter.GetBytes(0x300).CopyTo(bytes,0xB8);BitConverter.GetBytes(0x80).CopyTo(bytes,0xBC);
        BitConverter.GetBytes(0x280).CopyTo(bytes,0xC0);return bytes;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] original=Cro(1),legacy=(byte[])original.Clone();
        object[] args=[legacy];typeof(CRO).GetMethod("HashCRO",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
        byte[] rebuilt=CroIntegrity.Rebuild(original);
        check(rebuilt.SequenceEqual((byte[])args[0]),"CRO integrity byte-for-byte vanilla HashCRO parity");
        check(rebuilt.AsSpan(0x80).SequenceEqual(original.AsSpan(0x80)),"CRO integrity preserves every non-hash byte");
        check(CroIntegrity.Rebuild(rebuilt).SequenceEqual(rebuilt),"CRO integrity idempotent rehash");
        byte[] invalid=(byte[])original.Clone();BitConverter.GetBytes(int.MaxValue).CopyTo(invalid,0xB4);
        reject(()=>CroIntegrity.Rebuild(invalid),"CRO integrity out-of-range segment rejected");
        invalid=(byte[])original.Clone();BitConverter.GetBytes(0x100).CopyTo(invalid,0xB0);
        reject(()=>CroIntegrity.Rebuild(invalid),"CRO integrity overlapping hash segment rejected");
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);string tag=version+" CRO/CRR ";
            string a=Path.Combine(f.Game.RomFS,"A.cro"),b=Path.Combine(f.Game.RomFS,"B.cro");
            byte[] sourceA=Cro(1),sourceB=Cro(2);File.WriteAllBytes(a,sourceA);File.WriteAllBytes(b,sourceB);
            string crrPath=Path.Combine(f.Game.RomFS,".crr","static.crr");Directory.CreateDirectory(Path.GetDirectoryName(crrPath)!);
            byte[] crr=Enumerable.Repeat((byte)0xA5,0x400).ToArray();
            BitConverter.GetBytes(0x380).CopyTo(crr,0x350);BitConverter.GetBytes(2).CopyTo(crr,0x354);File.WriteAllBytes(crrPath,crr);
            var service=new GameCroIntegrityService(f.Session);var preview=service.Preview(false);
            check(preview.Changes.Count==2&&preview.Files.Count==2&&File.ReadAllBytes(a).SequenceEqual(sourceA)&&File.ReadAllBytes(b).SequenceEqual(sourceB),tag+"default preview writes nothing and excludes CRR");
            reject(()=>new GameCroIntegrityService(f.Session).Apply(preview),tag+"foreign proposal rejected");
            service.Apply(preview);var expectedA=CroIntegrity.Rebuild(sourceA);var expectedB=CroIntegrity.Rebuild(sourceB);
            check(File.ReadAllBytes(a).SequenceEqual(expectedA)&&File.ReadAllBytes(b).SequenceEqual(expectedB)&&File.ReadAllBytes(crrPath).SequenceEqual(crr),tag+"default apply changes only CRO hashes");
            reject(()=>service.Apply(preview),tag+"stale proposal rejected");
            check(f.Session.History.Read().Entries.Last().Files.Count==2,tag+"joint CRO journal");
            preview=service.Preview(true);var expectedCrr=CroIntegrity.RebuildCrr(crr,[expectedA,expectedB]);
            check(preview.Changes.Count==1&&File.ReadAllBytes(crrPath).SequenceEqual(crr),tag+"optional CRR preview is read-only");
            service.Apply(preview);
            check(File.ReadAllBytes(crrPath).SequenceEqual(expectedCrr)&&expectedCrr.AsSpan(0,0x380).SequenceEqual(crr.AsSpan(0,0x380)),tag+"CRR hashes sorted with surrounding bytes preserved");
            check(service.Preview(true).Changes.Count==0,tag+"repeated CRO/CRR operation is no-op");
            var pending=service.Preview(true);byte[] edited=(byte[])expectedA.Clone();edited[^1]^=1;File.WriteAllBytes(a,edited);
            reject(()=>service.Apply(pending),tag+"even unchanged CRO dependency validated before CRR apply");
            check(File.ReadAllBytes(a).SequenceEqual(edited)&&File.ReadAllBytes(crrPath).SequenceEqual(expectedCrr),tag+"external edit and CRR preserved after rejection");
            File.WriteAllBytes(a,expectedA);pending=service.Preview(false);
            string extra=Path.Combine(f.Game.RomFS,"Extra.cro");File.WriteAllBytes(extra,Cro(3));
            reject(()=>service.Apply(pending),tag+"changed CRO inventory rejected");
            reject(()=>service.Preview(true),tag+"CRR count mismatch rejected before writes");
            check(File.ReadAllBytes(a).SequenceEqual(expectedA)&&File.ReadAllBytes(crrPath).SequenceEqual(expectedCrr),tag+"inventory failures write nothing");
        }
    }
}
