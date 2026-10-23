using System.IO;
using System.Buffers.Binary;
using pk3DS.Core.NX;
internal static class SwitchWorldChecks
{
    private static void I32(byte[] b,int at,int n)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at),n);
    private static void I64(byte[] b,int at,long n)=>BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at),n);
    internal static byte[] Pack(params (string Name,byte[] Data)[] members)
    {
        int hashes=40,table=(hashes+members.Length*8+15)&~15,dataStart=(table+members.Length*24+15)&~15;var data=new byte[dataStart+members.Sum(m=>m.Data.Length)];"GFLXPACK"u8.CopyTo(data);I32(data,8,4096);I32(data,16,members.Length);I64(data,24,table);I64(data,32,hashes);int offset=dataStart;
        for(int i=0;i<members.Length;i++){var member=members[i];BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(hashes+i*8),SwitchGfPack.NameHash(member.Name));int at=table+i*24;I32(data,at+4,member.Data.Length);I32(data,at+8,member.Data.Length);I32(data,at+16,offset);member.Data.CopyTo(data,offset);offset+=member.Data.Length;}return data;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var archive=Pack(("encount_symbol_k.bin",SwordShieldGameplayChecks.Encounter()),("encount_k.bin",SwordShieldGameplayChecks.Encounter()),("opaque",new byte[]{17,34,51}));var files=new Dictionary<string,byte[]>{{SwordShieldGameplay.EncountersPath,archive}};var cells=SwordShieldWorldData.Read(files,false,false);var species=cells.Single(c=>c.Region=="encount_k.bin/0/0"&&c.Field=="Especie");var form=cells.Single(c=>c.Region==species.Region&&c.Field=="Forma");var result=SwordShieldWorldData.Edit(files,SwordShieldChecks.Table(),false,false,new[]{new SwordShieldDataEdit(species.Key,2),new(form.Key,0)});var pack=new SwitchGfPack(result.Files()[SwordShieldGameplay.EncountersPath]);
        check(pack.Read(0).SequenceEqual(SwordShieldGameplayChecks.Encounter())&&pack.Read(2).SequenceEqual(new byte[]{17,34,51}),"Switch_Parity_WorldManualEditPreservesOtherMemberAndOpaqueSibling");
        var member=pack.Read(1);check(BinaryPrimitives.ReadInt32LittleEndian(member.AsSpan(184))==2&&member[181]==0&&Enumerable.Range(0,member.Length).All(i=>i==181||i is >=184 and <188||member[i]==SwordShieldGameplayChecks.Encounter()[i]),"Switch_Parity_WorldManualEditChangesOnlyRequestedScalarBytes");
        check(files[SwordShieldGameplay.EncountersPath].SequenceEqual(archive),"Switch_Parity_WorldPreviewDoesNotModifyInputArchive");
        var max=cells.Single(c=>c.Region==species.Region&&c.Field=="Nivel máximo");reject(()=>SwordShieldWorldData.Edit(files,SwordShieldChecks.Table(),false,false,new[]{new SwordShieldDataEdit(max.Key,2)}),"Switch_Parity_WorldRejectsInvertedLevelRange");
        reject(()=>SwordShieldWorldData.Edit(files,SwordShieldChecks.Table(),false,false,new[]{new SwordShieldDataEdit(species.Key,2)}),"Switch_Parity_WorldRejectsUnavailableSpeciesFormPair");
        reject(()=>SwordShieldWorldData.Edit(files,SwordShieldChecks.Table(),false,false,new[]{new SwordShieldDataEdit(form.Key,0),new(form.Key,0)}),"Switch_Parity_WorldRejectsDuplicateScalarEdits");
        var random=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,new(123,VisibleEncounters:true,HiddenEncounters:true){WorldScope=new[]{species.Region}});check(new SwitchGfPack(random.Files()[SwordShieldGameplay.EncountersPath]).Read(0).SequenceEqual(SwordShieldGameplayChecks.Encounter()),"Switch_Parity_WorldRandomizationHonorsSelectedAreaScope");
    }
}
