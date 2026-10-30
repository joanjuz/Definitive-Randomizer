using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class FieldItems6Checks
{
    private static byte[] Script(params (int Offset,int Item)[] entries)
    {
        byte[] instructions=new byte[0x1800];foreach(var e in entries)BitConverter.GetBytes((ushort)e.Item).CopyTo(instructions,e.Offset);
        instructions[^4]=0x4A;var code=Gen6ScriptCodec.Encode(instructions);var raw=new byte[32+code.Length+3];
        BitConverter.GetBytes(raw.Length).CopyTo(raw,0);BitConverter.GetBytes(0x0A0AF1E0).CopyTo(raw,4);
        BitConverter.GetBytes(32).CopyTo(raw,12);BitConverter.GetBytes(32).CopyTo(raw,16);BitConverter.GetBytes(32+instructions.Length).CopyTo(raw,20);
        code.CopyTo(raw,32);raw[^3]=0xAA;raw[^2]=0xBB;raw[^1]=0xCC;return raw;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);bool oras=f.Game.ORAS;
            int visible=oras?39:17,start=oras?0xB64:0xB04;
            var files=Enumerable.Range(0,60).Select(_=>new byte[]{0xAA,0xBB}).ToArray();
            files[visible]=Script((start,155),(start+12,156),(start+24,328),(0x16A4,155));
            if(oras)files[57]=Script((2746,656),(2778,657));else files[26]=Script((0xB18,157),(0xB24,158));
            string path=Path.Combine(f.Game.RomFS,oras?"a/0/2/9":"a/0/3/1");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var reference=f.Game.GetGARCReference("personal");f.WriteGarc(path,files,reference.GetRelativeGARC((oras?29:31)-reference.FileNumber,"scripts"));
            string codePath=Path.Combine(f.Game.ExeFS,".code.bin");Directory.CreateDirectory(f.Game.ExeFS);
            byte[] code=new byte[5000];GameFieldItems6Service.HiddenSignature.CopyTo(code,100);
            int hidden=100+GameFieldItems6Service.HiddenSignature.Length;BitConverter.GetBytes((ushort)157).CopyTo(code,hidden+2);BitConverter.GetBytes((ushort)158).CopyTo(code,hidden+16);
            if(oras)File.WriteAllBytes(codePath,code);
            var editor=new GameFieldItems6Service(f.Session);var options=EditorValues.Flatten(editor.RandomOptions);
            check(editor.Entries.Any(e=>e.Name.Contains("Visible"))&&editor.Entries.Any(e=>e.Name.Contains("Hidden"))&&
                (!oras||editor.Entries.Count(e=>e.Name.Contains("Mega"))==2),version+" field items discover original script and code sources");
            int id=editor.Entries.First(e=>e.Name.Contains("Visible")).Id;var values=EditorValues.Flatten(editor.Read(id));
            byte[] before=File.ReadAllBytes(path);editor.Save(id,values);
            check(File.ReadAllBytes(path).SequenceEqual(before)&&f.Session.History.Read().Entries.Count==0,version+" field item no-op preserves whole GARC and history");
            values["Item"]=10000;editor.Save(id,values);var after=new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            check(editor.Read(id).SelectMany(g=>g.Fields).First(f=>f.Key=="Item").Choices!.Any(c=>c.Id==10000),version+" custom item IDs remain selectable instead of being lost in the UI");
            check(editor.ExportCsv().Contains(",10000,")&&editor.ExportCsv().Split(Environment.NewLine).Length==editor.Entries.Count+1,version+" CSV diagnostic exports current values and every source location");
            var old=Gen6ScriptCodec.Decode(files[visible]).Instructions;var updated=Gen6ScriptCodec.Decode(after[visible]).Instructions;
            check(updated.Where((b,i)=>i!=start&&i!=start+1).SequenceEqual(old.Where((b,i)=>i!=start&&i!=start+1))&&
                BitConverter.ToUInt16(updated,start)==10000&&after[visible].AsSpan(^3).SequenceEqual(new byte[]{0xAA,0xBB,0xCC}),version+" field item edits only selected word bytes and preserves script suffix across compression changes");
            check(after.Where((r,i)=>i!=visible).Select((r,i)=>r).Zip(files.Where((r,i)=>i!=visible)).All(p=>p.First.SequenceEqual(p.Second)),version+" unrelated script members unchanged");
            long allocated=GC.GetAllocatedBytesForCurrentThread();_=editor.Read(id);
            check(GC.GetAllocatedBytesForCurrentThread()-allocated<100000,version+"_FieldItems_SelectionUsesSmallSnapshot");
            var preview=editor.Preview(options,editor.Entries.Select(e=>e.Id).ToArray());
            check(EditorValues.Flatten(editor.Read(id))["Item"]==10000&&(!oras||File.ReadAllBytes(codePath).SequenceEqual(code)),version+" mixed script and code preview writes nothing");
            check(preview.Changes.All(c=>EditorValues.Flatten(editor.Read(c.Id))["Item"]!=328)&&
                (!oras||preview.Changes.All(c=>!c.Name.Contains("Mega"))),version+" defaults exclude machines and megapiedra locations");
            if(oras)
            {
                check(preview.Files.Any(file=>file.Path==Path.GetFullPath(codePath))&&preview.Files.Any(file=>file.Path==Path.GetFullPath(path)),version+" hidden and visible changes freeze both file dependencies");
                byte[] changed=(byte[])code.Clone();changed[^1]=1;File.WriteAllBytes(codePath,changed);
                byte[] frozenScripts=File.ReadAllBytes(path);
                reject(()=>editor.Apply(preview),version+" concurrent hidden code edit blocks whole proposal");
                check(File.ReadAllBytes(path).SequenceEqual(frozenScripts),version+" rejected mixed proposal preserves every scripts GARC byte");
                File.WriteAllBytes(codePath,code);editor=new GameFieldItems6Service(f.Session);
                int hiddenId=editor.Entries.First(e=>e.Name.Contains("código")).Id;var hiddenValues=EditorValues.Flatten(editor.Read(hiddenId));hiddenValues["Item"]=155;
                editor.Save(hiddenId,hiddenValues);byte[] codeAfter=File.ReadAllBytes(codePath);
                check(codeAfter.Where((b,i)=>i!=hidden+2&&i!=hidden+3).SequenceEqual(code.Where((b,i)=>i!=hidden+2&&i!=hidden+3)),version+" hidden code save preserves all unrelated instructions");
            }
            options["RandomPool"]=1;editor.Apply(editor.Preview(options,editor.Entries.Select(e=>e.Id).ToArray()));
            check(editor.Entries.Where(e=>e.Name.Contains("Visible")).Select(e=>EditorValues.Flatten(editor.Read(e.Id))["Item"]).Any(item=>item!=10000),version+" Progressive held item pool generates valid field replacements");
            byte[] current=File.ReadAllBytes(path);DateTime time=File.GetLastWriteTimeUtc(path);byte[] foreign=(byte[])current.Clone();foreign[^1]^=1;
            File.WriteAllBytes(path,foreign);File.SetLastWriteTimeUtc(path,time);
            reject(()=>editor.Preview(options,editor.Entries.Select(e=>e.Id).ToArray()),version+"_FieldItems_SameSizeTimestampChangeRejectsPreview");
            reject(()=>editor.Save(id,EditorValues.Flatten(editor.Read(id))),version+"_FieldItems_SameSizeTimestampChangeRejectsSave");
            check(File.ReadAllBytes(path).SequenceEqual(foreign),version+"_FieldItems_ExternalBytesPreserved");
        }
    }
    internal static void RunReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            string[] Inventory()=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order().Select(p=>{var f=new FileInfo(p);return p+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}).ToArray();
            var before=Inventory();var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var editor=new GameFieldItems6Service(session);
            var preview=editor.Preview(EditorValues.Flatten(editor.RandomOptions),editor.Entries.Select(e=>e.Id).ToArray());
            foreach(var file in preview.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
            if(!before.SequenceEqual(Inventory()))throw new IOException("La ROM original cambió.");
            Console.WriteLine($"PASS REAL FIELD: {root}; {editor.Entries.Count} entries; {preview.Changes.Count} preview changes; inventory and proposal source hashes intact; no Apply.");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var f=new Fixture(GameVersion.XY);ModuleChecks.CreateArchives(f);
        var files=Enumerable.Range(0,60).Select(_=>new byte[]{1,2}).ToArray();files[17]=Script((0xB04,155),(0xB10,156));files[26]=Script((0xB18,157),(0xB24,158));
        var reference=f.Game.GetGARCReference("personal");f.WriteGarc(Path.Combine(f.Game.RomFS,"a/0/3/1"),files,reference.GetRelativeGARC(31-reference.FileNumber,"scripts"));
        var main=new ThreeDS.Progressive.MainWindow();main.LoadProject(f.Session);
        var button=(System.Windows.Controls.Button)main.FindName("FieldItemsButton");
        check(button.IsEnabled,"WPF enables Field Items for Gen6 projects");
        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var host=(System.Windows.Controls.ContentControl)main.FindName("SectionHost");
        var view=host.Content as ThreeDS.Progressive.RecordEditorView;
        check(view?.Editor is GameFieldItems6Service&&((System.Windows.Controls.Button)view.FindName("FieldExportButton")).Visibility==System.Windows.Visibility.Visible,
            "WPF Gen6 Field Items opens native dark editor and exposes CSV export");main.Close();
    }
}
