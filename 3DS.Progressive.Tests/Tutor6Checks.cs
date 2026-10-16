using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;
internal static class Tutor6Checks
{
    internal static void Setup(Fixture f)
    {
        byte[] code=new byte[0x403000];code.AsSpan().Fill(0xA5);int list=0x402000+Gen6TutorLayout.ListHeader.Length,shop=0x401000+Gen6TutorLayout.ShopHeader.Length;
        Gen6TutorLayout.ListHeader.CopyTo(code,0x402000);Gen6TutorLayout.ShopHeader.CopyTo(code,0x401000);
        for(int i=0;i<67;i++)BitConverter.GetBytes((ushort)(new[]{15,33,50,66}.Contains(i)?622:100+i)).CopyTo(code,list+2*i);BitConverter.GetBytes(ushort.MaxValue).CopyTo(code,list+134);
        for(int i=0;i<60;i++){BitConverter.GetBytes((ushort)(100+Gen6TutorLayout.ListSlots[i])).CopyTo(code,shop+i*8);BitConverter.GetBytes((uint)(4+i)).CopyTo(code,shop+i*8+4);}
        File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.ORAS);Setup(f);string path=Path.Combine(f.Game.ExeFS,"code.bin");byte[] source=File.ReadAllBytes(path);var layout=Gen6TutorLayout.Read(source,f.Game.Moves.Length);var editor=new GameTutor6Service(f.Session);
        check(editor.Entries.Count==60&&EditorValues.Flatten(editor.Read(0))["Move"]==134,"ORAS tutor editor exposes 60 actual shop rows in shop order");
        var values=EditorValues.Flatten(editor.Read(0));values["Move"]=350;values["Price"]=77;editor.Save(0,values);byte[] after=File.ReadAllBytes(path);
        check(BitConverter.ToUInt16(after,layout.Shop)==350&&BitConverter.ToUInt16(after,layout.List+68)==350&&BitConverter.ToUInt32(after,layout.Shop+4)==77,"ORAS tutor edit synchronizes independently expected list slot 34 shop row zero and BP price");
        var allowed=new[]{layout.Shop,layout.Shop+1,layout.Shop+4,layout.Shop+5,layout.Shop+6,layout.Shop+7,layout.List+68,layout.List+69}.ToHashSet();
        check(source.Where((_,offset)=>!allowed.Contains(offset)).SequenceEqual(after.Where((_,offset)=>!allowed.Contains(offset))),"ORAS tutor edit preserves all unrelated code bytes including unused list slots markers and row reserved bytes");
        var options=EditorValues.Flatten(editor.RandomOptions);options["Prices"]=1;var scope=editor.Entries.Select(row=>row.Id).ToArray();var proposal=editor.Preview(options,scope,1837);
        check(File.ReadAllBytes(path).SequenceEqual(after)&&proposal.Files[0].Proposed().SequenceEqual(editor.Preview(options,scope,1837).Files[0].Proposed()),"ORAS tutor preview is frozen deterministic and read-only");
        editor.Apply(proposal);var fresh=new GameTutor6Service(f.Session);var moves=fresh.Entries.Select(row=>EditorValues.Flatten(fresh.Read(row.Id))["Move"]).ToArray();
        check(moves.Distinct().Count()==60&&!moves.Any(id=>new[]{15,19,57,70,127,249,291,165,621,622}.Contains(id))&&fresh.Entries.All(row=>EditorValues.Flatten(fresh.Read(row.Id))["Price"]==0),"ORAS tutor randomizer uses unique legal moves and can make every active tutor free");
        reject(()=>editor.Apply(proposal),"ORAS tutor stale proposal rejected");reject(()=>fresh.Apply(proposal),"ORAS tutor foreign proposal rejected");
        var bad=EditorValues.Flatten(fresh.Read(1));bad["Move"]=622;reject(()=>fresh.Save(1,bad),"ORAS tutor sentinel move rejected");
        reject(()=>fresh.Preview(options,[0,0],1),"ORAS tutor duplicate scope rejected");reject(()=>fresh.Preview(options,[60],1),"ORAS tutor foreign scope rejected");
        File.WriteAllBytes(path,source);var document=NativeRecipeFactory.Create(f.Session,modules:["Tutors"]);var snapshot=(JsonObject)document.Section("ProgressiveApp")!;snapshot["PartialSnapshot"]=false;document.SetSection("ProgressiveApp",snapshot);NativeBatchGen6Bridge.ValidateSession(f.Session,document);
        var manual=new GameTutor6Service(f.Session);byte[] expected=manual.Preview(EditorValues.Flatten(manual.RandomOptions),scope,1234+1009).Files[0].Proposed();NativeBatchGen6Bridge.ExecuteSession(f.Session,document.ToJson(),1234);
        check(File.ReadAllBytes(path).SequenceEqual(expected),"ORAS native Batch replays tutor snapshot with exact manual seeded bytes");
        var bridge=new GameTutor6Service(f.Session);var restored=GlobalEditorTemplateBridge.Restore(document,bridge,6);
        check(restored?.Scope==0&&restored.Options["Moves"]==1,"ORAS tutor Global snapshot captures full scope");
        byte[] mismatch=(byte[])source.Clone();mismatch[layout.Shop]^=1;File.WriteAllBytes(path,mismatch);reject(()=>new GameTutor6Service(f.Session),"ORAS tutor mismatched tables rejected before any write");check(File.ReadAllBytes(path).SequenceEqual(mismatch),"ORAS tutor mismatch preserves current code");
        byte[] duplicate=(byte[])source.Clone();Gen6TutorLayout.ShopHeader.CopyTo(duplicate,0x400100);File.WriteAllBytes(path,duplicate);reject(()=>new GameTutor6Service(f.Session),"ORAS tutor ambiguous shop signature rejected");
        using var xy=new Fixture(GameVersion.XY);reject(()=>new GameTutor6Service(xy.Session),"Tutor ORAS editor unavailable for XY");using var usum=new Fixture(GameVersion.USUM);reject(()=>new GameTutor6Service(usum.Session),"Tutor ORAS editor unavailable for Gen7");
    }
}
