using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

/// <summary>Detached synthetic CRO checks. No native game instructions or installation admission.</summary>
internal static class Gen6MintNativeChecks
{
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static void U(byte[] bytes,int at,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,at);
    internal static byte[] SyntheticBag()
    {
        byte[] bytes=new byte[0x12000];
        bytes.AsSpan(0x180,0xe080).Fill(0xA5);
        bytes.AsSpan(0xf000,0x1000).Fill(0xB6);
        bytes.AsSpan(0x10000,0x1000).Fill(0xC7);
        bytes.AsSpan(0xf100,48).Clear();
        "CRO0"u8.CopyTo(bytes.AsSpan(0x80));U(bytes,0x90,(uint)bytes.Length);
        U(bytes,0xb0,0x180);U(bytes,0xb4,0xee80);U(bytes,0xb8,0x10000);U(bytes,0xbc,0x1000);
        U(bytes,0xc0,0xf000);U(bytes,0xc4,0x1000);U(bytes,0xc8,0xf100);U(bytes,0xcc,4);
        uint[] segments=[0x180,0xe080,0,0xf000,0x1000,1,0x10000,0x1000,2,0,0x80,3];
        for(int i=0;i<segments.Length;i++)U(bytes,0xf100+i*4,segments[i]);
        U(bytes,0x128,0x11000);U(bytes,0x12c,1);
        // One internal relocation: rodata+0x300 -> code+0x1000. Its bytes and semantics must survive expansion.
        U(bytes,0x11000,0x3001);U(bytes,0x11004,2);U(bytes,0x11008,0x1000);
        U(bytes,0x428,0xe51ff004);U(bytes,0x2b5c,0xe5942058);
        return CroIntegrity.Rebuild(bytes);
    }
    private static Gen6MintNativeProfile Profile(byte[] source,bool y)=>new("synthetic-only-"+(y?"Y":"X"),
        y?0x0004000000055E00UL:0x0004000000055D00UL,Hash([(byte)(y?2:1)]),Hash(source));
    private static int Target(byte[] bytes,int at)
    {
        uint word=BitConverter.ToUInt32(bytes,at);int displacement=unchecked((int)(word<<8))>>6;
        return checked(at+8+displacement);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(bool y in new[]{false,true})
        {
            string tag=y?"Y":"X";byte[] original=SyntheticBag(),before=(byte[])original.Clone();var profile=Profile(original,y);
            check(Gen6NatureMintPatch.Inspect(original,profile).State==Gen6MintNativeState.Original,"Gen6 mint synthetic original CRO recognized "+tag);
            byte[] installed=Gen6NatureMintPatch.Prepare(original,profile);var state=Gen6NatureMintPatch.Inspect(installed,profile);
            int start=state.PayloadOffset;
            check(original.SequenceEqual(before)&&!ReferenceEquals(original,installed),"Gen6 mint detached preparation preserves input "+tag);
            check(state.State==Gen6MintNativeState.Installed&&start>=0,"Gen6 mint synthetic installed payload recognized "+tag);
            check(BitConverter.ToUInt32(installed,0x428)>>24==0xea&&BitConverter.ToUInt32(installed,0x2b5c)>>24==0xea&&
                Target(installed,0x428)==start&&Target(installed,0x2b5c)==start+0x100,"Gen6 mint hooks branch to own recover and dispatch entries "+tag);
            var repeat=Gen6NatureMintPatch.Prepare(installed,profile);
            check(repeat.SequenceEqual(installed)&&!ReferenceEquals(repeat,installed),"Gen6 mint preparation idempotent detached clone "+tag);
            check(CroIntegrity.Rebuild(installed).SequenceEqual(installed)&&installed.AsSpan(0x80,4).SequenceEqual("CRO0"u8),"Gen6 mint output CRO integrity rebuilt "+tag);
            check(CroRelocationMap.TryCreate(original,out var oldMap,out _)&&CroRelocationMap.TryCreate(installed,out var newMap,out _)&&
                start>=newMap.CodeStart&&start+Gen6NatureMintPatch.PayloadSize<=newMap.CodeEnd&&newMap.References.Count==1&&
                newMap.References[0].WriteAddress-newMap.RodataStart==oldMap.References[0].WriteAddress-oldMap.RodataStart&&
                newMap.References[0].TargetAddress==oldMap.References[0].TargetAddress,
                "Gen6 mint expansion preserves relocation target and relative write slot "+tag);
            var oldCode=original.AsSpan(0x180,0xe080).ToArray();var newCode=installed.AsSpan(0x180,0xe080).ToArray();
            foreach(int hook in new[]{0x428,0x2b5c})newCode.AsSpan(hook-0x180,4).CopyTo(oldCode.AsSpan(hook-0x180));
            check(oldCode.SequenceEqual(newCode),"Gen6 mint old code differs only at two hooks "+tag);
            CroRelocationMap.TryCreate(installed,out var map,out _);
            check(installed.AsSpan((int)map.RodataStart+0x300,0xd00).SequenceEqual(original.AsSpan(0xf300,0xd00))&&
                installed.AsSpan((int)map.DataStart,0x1000).SequenceEqual(original.AsSpan(0x10000,0x1000)),
                "Gen6 mint expansion preserves opaque rodata and data segments "+tag);
            check(Gen6NatureMintPatch.Inspect(installed,Profile(original,!y)).State==Gen6MintNativeState.Incompatible,
                "Gen6 mint installed title cannot cross independent profile "+tag);
            foreach(string badHash in new[]{"ZZ",new string('A',63),new string('Z',64)})
            {
                check(Gen6NatureMintPatch.Inspect(installed,profile with{BagSha256=badHash}).State==Gen6MintNativeState.Incompatible,
                    "Gen6 mint malformed source profile hash rejected diagnostically "+tag+" "+badHash);
                check(Gen6NatureMintPatch.Inspect(original,profile with{CodeSha256=badHash}).State==Gen6MintNativeState.Incompatible,
                    "Gen6 mint malformed code profile hash rejected diagnostically "+tag+" "+badHash);
            }
            for(int i=0;i<21;i++)
            {
                var item=Gen6NatureMintCatalog.ExperimentalItemMap.Items[i];uint word=BitConverter.ToUInt32(installed,start+0x900+i*4);
                check((word&0xffff)==item.ItemId&&word>>24==item.NatureId,"Gen6 mint experimental map serialized once "+tag+" slot"+i);
            }
            check(BitConverter.ToUInt32(installed,start+0x900+21*4)==0,"Gen6 mint map has zero lookup terminator "+tag);
            foreach(int hook in new[]{0x428,0x2b5c})
            {
                byte[] partial=(byte[])installed.Clone();U(partial,hook,hook==0x428?0xe51ff004u:0xe5942058u);partial=CroIntegrity.Rebuild(partial);
                check(Gen6NatureMintPatch.Inspect(partial,profile).State==Gen6MintNativeState.Partial,"Gen6 mint one original hook classified partial "+tag+" hook"+hook);
                reject(()=>Gen6NatureMintPatch.Prepare(partial,profile),"Gen6 mint partial installation never repaired implicitly "+tag+" hook"+hook);
            }
            foreach(var (offset,name) in new[]{(start+0x10,"payload"),(start+0x900,"catalog"),(start+0xa40,"digest"),(0x1700,"external code")})
            {
                byte[] changed=(byte[])installed.Clone();changed[offset]^=1;changed=CroIntegrity.Rebuild(changed);byte[] unchanged=(byte[])changed.Clone();
                check(Gen6NatureMintPatch.Inspect(changed,profile).State==Gen6MintNativeState.Modified,"Gen6 mint authenticated modification rejected "+tag+" "+name);
                reject(()=>Gen6NatureMintPatch.Prepare(changed,profile),"Gen6 mint modified CRO cannot reinstall "+tag+" "+name);
                check(changed.SequenceEqual(unchanged),"Gen6 mint rejected preparation preserves changed input "+tag+" "+name);
            }
            byte[] duplicate=(byte[])installed.Clone();"XYMINT-E1\0"u8.CopyTo(duplicate.AsSpan(0x1000));duplicate=CroIntegrity.Rebuild(duplicate);
            check(Gen6NatureMintPatch.Inspect(duplicate,profile).State==Gen6MintNativeState.Modified,"Gen6 mint duplicate marker rejected "+tag);
            byte[] foreign=(byte[])original.Clone();foreign[0x1800]^=1;foreign=CroIntegrity.Rebuild(foreign);
            check(Gen6NatureMintPatch.Inspect(foreign,profile).State==Gen6MintNativeState.Incompatible,"Gen6 mint unknown original hash rejected "+tag);
            reject(()=>Gen6NatureMintPatch.Prepare(foreign,profile),"Gen6 mint unknown original cannot allocate code "+tag);
            byte[] occupied=(byte[])original.Clone();U(occupied,0x428,0xe1a00000);occupied=CroIntegrity.Rebuild(occupied);
            check(Gen6NatureMintPatch.Inspect(occupied,profile with{BagSha256=Hash(occupied)}).State==Gen6MintNativeState.Incompatible,
                "Gen6 mint occupied hook rejected even with matching synthetic source hash "+tag);
            byte[] missing=(byte[])installed.Clone();missing.AsSpan(start+0xa00,10).Clear();missing=CroIntegrity.Rebuild(missing);
            check(Gen6NatureMintPatch.Inspect(missing,profile).State==Gen6MintNativeState.Incompatible,"Gen6 mint missing marker does not permit reinstall "+tag);
            foreach(int length in new[]{0,0x80,0x17f,0x2b5f,installed.Length-1})
            {
                byte[] truncated=installed[..length];
                check(Gen6NatureMintPatch.Inspect(truncated,profile).State is Gen6MintNativeState.Incompatible or Gen6MintNativeState.Modified,
                    "Gen6 mint truncated CRO rejected "+tag+" length"+length);
                reject(()=>Gen6NatureMintPatch.Prepare(truncated,profile),"Gen6 mint truncated CRO cannot prepare "+tag+" length"+length);
            }
            byte[] invalid=(byte[])original.Clone();invalid[0x80]^=1;
            check(Gen6NatureMintPatch.Inspect(invalid,profile).State==Gen6MintNativeState.Incompatible,"Gen6 mint invalid magic rejected "+tag);
            invalid=(byte[])original.Clone();invalid[0]^=1;
            check(Gen6NatureMintPatch.Inspect(invalid,profile).State==Gen6MintNativeState.Incompatible,"Gen6 mint bad header hashes rejected "+tag);
            check(Gen6NatureMintPatch.Inspect(original,profile with{TitleId=0}).State==Gen6MintNativeState.Incompatible,"Gen6 mint unsupported title rejected "+tag);
            invalid=(byte[])original.Clone();U(invalid,0xcc,3);invalid=CroIntegrity.Rebuild(invalid);var invalidProfile=profile with{BagSha256=Hash(invalid)};
            reject(()=>Gen6NatureMintPatch.Prepare(invalid,invalidProfile),"Gen6 mint malformed segment table cannot allocate code "+tag);
        }
    }
}
