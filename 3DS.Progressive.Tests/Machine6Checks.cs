using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class Machine6Checks
{
    internal static (string Code,string Text,byte[] CodeBytes,byte[][] TextRecords,int Items,int Moves) Setup(Fixture f)
    {
        byte[] code=Enumerable.Repeat((byte)0xA5,0x401000).ToArray();GameMachine6Service.Signature.CopyTo(code,0x400100);int at=0x400108;
        for(int i=0;i<(f.Game.ORAS?107:105);i++)BitConverter.GetBytes((ushort)(i+1)).CopyTo(code,at+2*i);
        BitConverter.GetBytes((ushort)148).CopyTo(code,at);
        (f.Game.ORAS?Gen6HmRestriction.ORAS:Gen6HmRestriction.XY).CopyTo(code,0x2000);
        string cp=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(cp,code);
        int item=f.Game.GameText.First(t=>t.Name==TextName.ItemFlavor).Index,move=f.Game.GameText.First(t=>t.Name==TextName.MoveFlavor).Index;
        byte[][] records=Enumerable.Range(0,f.Game.GameText.Max(t=>t.Index)+1).Select(i=>new byte[]{0xAB,(byte)i,0xCD}).ToArray();
        records[item]=TextFile.GetBytes(f.Game,Enumerable.Range(0,1000).Select(i=>"Item "+i).ToArray(),f.Game.RemapCharacters);
        records[move]=TextFile.GetBytes(f.Game,Enumerable.Range(0,1000).Select(i=>"Move "+i+"\\nInfo").ToArray(),f.Game.RemapCharacters);
        string tp=Path.Combine(f.Game.RomFS,f.Game.GetGARCFileName("gametext"));var reference=f.Game.GetGARCReference("gametext");
        f.WriteGarc(tp,records,reference.LanguageVariant?reference.GetRelativeGARC(f.Game.Language):reference);
        return(cp,tp,code,records,item,move);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using(var f=new Fixture(GameVersion.ORAS))
        {
            var setup=Setup(f);var records=setup.TextRecords;
            var items=new TextFile(f.Game,records[setup.Items],f.Game.RemapCharacters).Lines;
            var moves=new TextFile(f.Game,records[setup.Moves],f.Game.RemapCharacters).Lines;
            items[0]="  Descripción ajena con espacios  ";moves[321]="  Descripción MT con espacios  ";
            records[setup.Items]=new TextFile(f.Game,remapChars:f.Game.RemapCharacters){PreserveWhitespace=true,Lines=items}.Data;
            records[setup.Moves]=new TextFile(f.Game,remapChars:f.Game.RemapCharacters){PreserveWhitespace=true,Lines=moves}.Data;
            var reference=f.Game.GetGARCReference("gametext");f.WriteGarc(setup.Text,records,reference.LanguageVariant?reference.GetRelativeGARC(f.Game.Language):reference);
            var editor=new GameMachine6Service(f.Session);var values=EditorValues.Flatten(editor.Read(1));values["TM093"]=321;editor.Save(1,values);
            var after=new GARC.MemGARC(File.ReadAllBytes(setup.Text)).Files;
            var descriptions=new TextFile(f.Game,after[setup.Items],f.Game.RemapCharacters).Lines;
            check(descriptions[0]==items[0]&&descriptions[618]==moves[321],"ORAS TM descriptions preserve unrelated and copied leading/trailing whitespace");
        }
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);if(f.Game.ORAS)f.Game.Language=6;string tag=version+" MT/MO Gen6 ";var fixture=Setup(f);
            var service=new GameMachine6Service(f.Session);var values=EditorValues.Flatten(service.Read(1));
            check(values.Count==(f.Game.ORAS?108:106)&&values["TM093"]==(f.Game.ORAS?99:98)&&values["HM01"]==93,tag+"interleaved vanilla table decoded");
            check(values[f.Game.ORAS?"HM07":"HM05"]==(f.Game.ORAS?107:97),tag+"last HM index decoded");
            var options=EditorValues.Flatten(service.RandomOptions);byte[] text=File.ReadAllBytes(fixture.Text);
            var preview=service.Preview(options,[1]);
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(fixture.CodeBytes)&&File.ReadAllBytes(fixture.Text).SequenceEqual(text),tag+"preview writes neither code nor text");
            var proposed=preview.Files.First(x=>x.Path==fixture.Code).Proposed();
            check(BitConverter.ToUInt16(proposed,0x400108)==148&&proposed.AsSpan(0x400108+92*2,10).SequenceEqual(fixture.CodeBytes.AsSpan(0x400108+92*2,10)),tag+"default randomizer preserves field MT and MO");
            reject(()=>new GameMachine6Service(f.Session).Apply(preview),tag+"foreign proposal rejected");
            values["TM093"]=321;values[f.Game.ORAS?"HM07":"HM05"]=322;service.Save(1,values);
            byte[] expected=(byte[])fixture.CodeBytes.Clone();BitConverter.GetBytes((ushort)321).CopyTo(expected,0x400108+(f.Game.ORAS?98:97)*2);
            BitConverter.GetBytes((ushort)322).CopyTo(expected,0x400108+(f.Game.ORAS?106:96)*2);
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"only requested TM/HM bytes change");
            var records=new GARC.MemGARC(File.ReadAllBytes(fixture.Text)).Files;
            check(records.Where((r,i)=>i!=fixture.Items).Select((r,i)=>true).Count()==records.Length-1&&
                Enumerable.Range(0,records.Length).Where(i=>i!=fixture.Items).All(i=>records[i].SequenceEqual(fixture.TextRecords[i])),tag+"all other text records byte-identical");
            string[] item=new TextFile(f.Game,records[fixture.Items],f.Game.RemapCharacters).Lines;
            check(item[618]=="Move 321\\nInfo"&&item[f.Game.ORAS?737:424]=="Move 322\\nInfo"&&item[500]=="Item 500",tag+"localized item descriptions synchronized and unrelated lines preserved");
            check(f.Game.GetText(TextName.ItemFlavor)[618]==item[618]&&f.Session.TmMoveIds.Count==100&&f.Session.TmMoveIds[92]==321,tag+"text cache and inspector refresh after commit");
            service.Save(1,EditorValues.Flatten(service.Read(1)));
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected)&&new GARC.MemGARC(File.ReadAllBytes(fixture.Text)).Files[fixture.Items].SequenceEqual(records[fixture.Items]),tag+"repeat save is byte-idempotent");
            var history=f.Session.History.Read().Entries.Last();check(history.Files.Count==2&&history.Module==service.Title,tag+"code and text share one history entry");
            reject(()=>service.Apply(preview),tag+"stale proposal rejected");
            values=EditorValues.Flatten(service.Read(1));values["ForgetHms"]=1;service.Save(1,values);
            byte[] patched=(byte[])expected.Clone();Convert.FromHexString("0000A0E31EFF2FE1").CopyTo(patched,0x2000);
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(patched)&&EditorValues.Flatten(service.Read(1))["ForgetHms"]==1,tag+"forgettable HM patch changes only first eight bytes");
            values["ForgetHms"]=0;service.Save(1,values);check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"HM restriction restores exact original function");
            values=EditorValues.Flatten(service.Read(1));values["TM001"]=100000;reject(()=>service.Save(1,values),tag+"invalid move rejected");
            options["Field"]=1;options["Hms"]=1;preview=service.Preview(options,[1]);proposed=preview.Files.First(x=>x.Path==fixture.Code).Proposed();
            var ids=Enumerable.Range(0,f.Game.ORAS?107:105).Select(i=>(int)BitConverter.ToUInt16(proposed,0x400108+i*2)).ToArray();
            check(ids.Distinct().Count()==ids.Length&&ids.All(id=>id>0&&id<1000&&id!=165&&id!=621),tag+"full randomization uses unique vanilla pool excluding banned moves");
            var hmOptions=EditorValues.Flatten(service.RandomOptions);hmOptions["Tms"]=0;hmOptions["HmRestriction"]=1;
            var hmPreview=service.Preview(hmOptions,[1]);var hmBytes=hmPreview.Files.First(x=>x.Path==fixture.Code).Proposed();
            check(Gen6HmRestriction.Locate(hmBytes,f.Game.ORAS).Patched&&File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"forgettable HMs have read-only preview without randomizing machines");
            service.Apply(hmPreview);
            hmOptions["HmRestriction"]=0;var preserve=service.Preview(hmOptions,[1]);
            check(Gen6HmRestriction.Locate(preserve.Files.First(x=>x.Path==fixture.Code).Proposed(),f.Game.ORAS).Patched,tag+"default preview preserves existing forgettable HMs");
            var global=GlobalTemplateDocument.Empty(version.ToString(),6);GlobalEditorTemplateBridge.Capture(global,service,new(hmOptions,0),6);
            check(GlobalEditorTemplateBridge.Restore(global,service,6)!.Options["HmRestriction"]==0,tag+"Global preserves HM patch policy");
            hmOptions["HmRestriction"]=2;service.Apply(service.Preview(hmOptions,[1]));
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected),tag+"preview restores original HM restriction without changing machines");
            preview=service.Preview(options,[1]);
            byte[] external=File.ReadAllBytes(fixture.Text);external[^1]^=1;File.WriteAllBytes(fixture.Text,external);
            reject(()=>service.Apply(preview),tag+"external text blocks both writes");check(File.ReadAllBytes(fixture.Code).SequenceEqual(expected)&&File.ReadAllBytes(fixture.Text).SequenceEqual(external),tag+"both files preserved after stale text rejection");
            File.WriteAllBytes(fixture.Text,text);service=new GameMachine6Service(f.Session);preview=service.Preview(options,[1]);expected[^1]^=1;File.WriteAllBytes(fixture.Code,expected);
            reject(()=>service.Apply(preview),tag+"external code blocks text write");check(File.ReadAllBytes(fixture.Text).SequenceEqual(text),tag+"text preserved after stale code rejection");
            byte[] missing=(byte[])fixture.CodeBytes.Clone();missing.AsSpan(0x400100,GameMachine6Service.Signature.Length).Clear();File.WriteAllBytes(fixture.Code,missing);
            reject(()=>new GameMachine6Service(f.Session),tag+"missing table signature rejected");
            byte[] unknown=(byte[])fixture.CodeBytes.Clone();unknown[0x2000]^=1;File.WriteAllBytes(fixture.Code,unknown);service=new GameMachine6Service(f.Session);
            values=EditorValues.Flatten(service.Read(1));values["ForgetHms"]=1;reject(()=>service.Save(1,values),tag+"unknown HM function cannot be patched");
            check(File.ReadAllBytes(fixture.Code).SequenceEqual(unknown)&&File.ReadAllBytes(fixture.Text).SequenceEqual(text),tag+"unknown function rejection leaves both files intact");
            byte[] repeated=(byte[])fixture.CodeBytes.Clone();(f.Game.ORAS?Gen6HmRestriction.ORAS:Gen6HmRestriction.XY).CopyTo(repeated,0x2200);File.WriteAllBytes(fixture.Code,repeated);
            reject(()=>new GameMachine6Service(f.Session),tag+"duplicate HM function rejected");
            byte[] duplicate=(byte[])fixture.CodeBytes.Clone();GameMachine6Service.Signature.CopyTo(duplicate,0x400300);File.WriteAllBytes(fixture.Code,duplicate);
            reject(()=>new GameMachine6Service(f.Session),tag+"duplicate signature rejected");
            File.WriteAllBytes(fixture.Code,new byte[512]);reject(()=>new GameMachine6Service(f.Session),tag+"truncated code rejected");
        }
    }
}
