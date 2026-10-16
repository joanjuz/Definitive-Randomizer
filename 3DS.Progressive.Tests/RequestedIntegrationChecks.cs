using System.IO;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class RequestedIntegrationChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] matrix=Enumerable.Range(0,324).Select(i=>new byte[]{0,2,4,8}[i%4]).ToArray();var inverted=GameTypeChartService.Inverse(matrix);
        check(inverted.Take(4).SequenceEqual(new byte[]{8,8,4,2})&&matrix.Take(4).SequenceEqual(new byte[]{0,2,4,8}),"Inverse chart maps immunity/resistance to weakness and preserves neutral/source");reject(()=>GameTypeChartService.Inverse(new byte[2]),"Inverse chart rejects malformed matrix");
        foreach(int previous in new[]{5,40,90})foreach(int next in new[]{10,50,80})foreach(int gap in new[]{0,1,5,20})
        {var curve=Enumerable.Range(1,100).Select(ace=>TrainerLevelProgression.Target(ace,20,previous,70,next,gap)).ToArray();check(curve.All(v=>v>=1&&v<next)&&curve.SequenceEqual(curve.Order()),$"Trainer curve {previous}->{next}, gap {gap}: monotone and below next cap");}
        check(TrainerLevelProgression.Target(30,20,90,40,1,0)==1,"Trainer minimum level remains legal when next cap is one");
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);var initialHeaders=ModuleChecks.Read(f,"trdata");var initialTeams=ModuleChecks.Read(f,"trpoke");var headers=Enumerable.Range(0,1000).Select(i=>(byte[])initialHeaders[i==0?0:1].Clone()).ToArray();var teams=Enumerable.Range(0,1000).Select(i=>(byte[])initialTeams[i==0?0:1].Clone()).ToArray();f.WriteGarc(ModuleChecks.PathFor(f,"trdata"),headers,f.Game.GetGARCReference("trdata"));f.WriteGarc(ModuleChecks.PathFor(f,"trpoke"),teams,f.Game.GetGARCReference("trpoke"));int capId=new GameTrainerService(f.Session).GetRuleRows().First(r=>r.Id!=1&&r.Id!=3).Id;
            foreach(int id in new[]{1,capId,3})
            {
                if(f.Game.Generation==6){var data=new TrainerData6(headers[id],teams[id],f.Game.ORAS);data.Team[0].Level=(ushort)(id==capId?35:id==3?23:18);data.Team[1].Level=(ushort)(id==capId?40:id==3?25:20);teams[id]=data.WriteTeam();}
                else{var data=new TrainerData7(headers[id],teams[id]);data.Pokemon[0].Level=id==capId?35:id==3?23:18;data.Pokemon[1].Level=id==capId?40:id==3?25:20;data.Write(out _,out teams[id]);}
            }
            f.WriteGarc(ModuleChecks.PathFor(f,"trpoke"),teams,f.Game.GetGARCReference("trpoke"));var trainer=new GameTrainerService(f.Session);var values=EditorValues.Flatten(trainer.RandomOptions);
            values["CHK_RandomPKM"]=0;values["CHK_LevelCaps"]=1;values["GlobalCaps.ApplyPrevious"]=1;values["GlobalCaps.PreviousGap"]=0;
            foreach(var key in values.Keys.Where(k=>k.StartsWith("CHK_")&&k is not "CHK_LevelCaps"&&!k.StartsWith("CHK_G")).ToArray())values[key]=0;
            values["Rule.1.CapUse"]=0;values["Rule.1.MoveUse"]=0;
            var json=System.Text.Json.Nodes.JsonNode.Parse(trainer.CaptureOptions(values))!;json["LevelCaps"]!["Enabled"]=true;json["LevelCaps"]!["Trainers"]=new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["TrainerID"]=capId,["Use"]=true,["LevelCap"]=30,["CurrentAceLevel"]=40});trainer.RestoreOptions(json.ToJsonString());
            var proposal=trainer.Preview(values,[1,capId,3]);trainer.Apply(proposal);int L(int id)=>EditorValues.Flatten(trainer.Read(id)).Where(p=>p.Key.EndsWith(".Level")).Max(p=>p.Value);
            Console.WriteLine($"ENGINE LEVELS {version}: {L(1)}, {L(capId)}, {L(3)}");check(L(capId)==30&&L(1)<30&&L(3)<30&&L(1)<=L(3),version+" trainer engine scales uncapped teams progressively and keeps them below the explicit cap even with gap zero");
        }
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            var records=f.PersonalRecords;for(int id=1;id<=2;id++){var p=new PersonalTable(records[id],version).Table[0];p.HP=p.ATK=p.DEF=p.SPA=p.SPD=p.SPE=id==1?50:90;records[id]=p.Write();}records[^1]=records.Take(records.Length-1).SelectMany(r=>r).ToArray();f.WriteGarc(f.PersonalPath,records,f.Game.GetGARCReference("personal"));f.Game.InitializePersonal();
            var wild=new GameEncounterService(f.Session);var entry=EditorValues.Flatten(wild.Read(1));entry["S0.Species"]=2;entry["S0.Min"]=entry["S0.Max"]=5;entry["S1.Species"]=1;entry["S1.Min"]=entry["S1.Max"]=65;wild.Save(1,entry);
            var options=EditorValues.Flatten(wild.RandomOptions);options["Progressive"]=1;for(int i=0;i<6;i++){options[$"BST.{i}.MinBST"]=options[$"BST.{i}.MaxBST"]=i==0?300:i==5?540:420;}
            string path=ModuleChecks.PathFor(f,"encdata");byte[] before=File.ReadAllBytes(path);var proposal=wild.Preview(options,[1],19);check(File.ReadAllBytes(path).SequenceEqual(before),version+" BST Gen6 preview preserves source");wild.Apply(proposal);var after=EditorValues.Flatten(wild.Read(1));check(after["S0.Species"]==1&&after["S1.Species"]==2,version+" BST Gen6 uses each slot's own level rather than first slot");
            check(wild.SplitLongestWildRange()&&wild.RandomOptions.SelectMany(g=>g.Fields).Count(f=>f.Key.StartsWith("BST."))==35,version+" Gen6 BST range table can expand");
            SeedMart(f);var marts=new GameMart6Service(f.Session);check(marts.SupportsExpansion,version+" Gen6 mart RO padding is verified");var manual=marts.ReadAllItems();manual[0]=manual[0].Append(50).ToArray();manual[1]=manual[1].SkipLast(1).ToArray();options=EditorValues.Flatten(marts.RandomOptions);options["UseChecks"]=1;
            byte[] codeBefore=File.ReadAllBytes(Path.Combine(f.Game.ExeFS,".code.bin"));var changes=marts.PrepareDraft(manual,options,marts.Entries.Select(e=>e.Id).ToArray(),5);marts.Apply(changes);var reread=new GameMart6Service(f.Session);check(reread.ReadAllItems().Zip(manual).All(pair=>pair.First.SequenceEqual(pair.Second)),version+" Gen6 added/deleted slots roundtrip through count vector and relocated table");
            byte[] written=File.ReadAllBytes(Path.Combine(f.Game.ExeFS,".code.bin"));int header=written.AsSpan().IndexOf("DRM6"u8);var spans=new[]{(header,BitConverter.ToInt32(written,header+12)),(BitConverter.ToInt32(written,header+20),manual.Length),(BitConverter.ToInt32(written,header+24),4)}.OrderBy(p=>p.Item1).ToArray();int cursor=0;bool preserved=true;foreach(var span in spans){preserved&=codeBefore.AsSpan(cursor,span.Item1-cursor).SequenceEqual(written.AsSpan(cursor,span.Item1-cursor));cursor=span.Item1+span.Item2;}preserved&=codeBefore.AsSpan(cursor).SequenceEqual(written.AsSpan(cursor));check(preserved&&written.Length==codeBefore.Length,version+" Gen6 mart expansion preserves code outside owned padding/count/pointer regions and file size");
            options["EVItems"]=1;var ev=reread.PrepareDraft(reread.ReadAllItems(),options,reread.Entries.Select(e=>e.Id).ToArray(),5);int regular=f.Game.ORAS?10:9;reread.Apply(ev);var evRead=new GameMart6Service(f.Session).ReadAllItems();check(evRead.Take(regular).All(row=>Enumerable.Range(565,6).All(row.Contains))&&evRead.Skip(1).Take(regular-1).All(row=>!row.Contains(50)),version+" Gen6 EV preset adds wings and never adds candy");
            var doc=NativeRecipeFactory.Create(f.Session,d=>GlobalEditorTemplateBridge.Capture(d,new GameMart6Service(f.Session),new(options,0),6),["Marts"]);check(doc.Actions.SequenceEqual(new[]{"marts.add-ev-items"})&&NativeRecipeCompletion.TryComplete(f.Session,doc),version+" Gen6 check selection completes native Global/Batch recipe");NativeBatchGen6Bridge.ExecuteSession(f.Session,doc.ToJson(),9);
            written=File.ReadAllBytes(Path.Combine(f.Game.ExeFS,".code.bin"));written[header+64]^=1;File.WriteAllBytes(Path.Combine(f.Game.ExeFS,".code.bin"),written);reject(()=>new GameMart6Service(f.Session),version+" corrupted owned mart payload is rejected");
        }
        using(var rectangular=new System.Drawing.Bitmap(16,8))
        {
            for(int x=0;x<16;x++)for(int y=0;y<8;y++)rectangular.SetPixel(x,y,x<8?System.Drawing.Color.Red:System.Drawing.Color.Blue);
            byte[] original=GameGraphicsCodec.Encode(rectangular,XLIMEncoding.ETC1A4,true,XLIMOrientation.Transpose);
            original[original.Length-32]=0;original[original.Length-31]=1; // preserve a different real-game version
            byte[] encoded=GameGraphicsCodec.EncodeLike(rectangular,original);using var decoded=GameGraphicsCodec.Decode(encoded);
            check(encoded.AsSpan(encoded.Length-40).SequenceEqual(original.AsSpan(original.Length-40))&&decoded.GetPixel(1,1).R>200&&decoded.GetPixel(14,1).B>200,"Title transposed rectangular ETC1A4 preserves version, orientation and pixel placement");
        }
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);using var bitmap=new System.Drawing.Bitmap(8,8);bitmap.SetPixel(0,0,System.Drawing.Color.Red);byte[] image=GameGraphicsCodec.Encode(bitmap,XLIMEncoding.ETC1,true),record=Alyt(image);string path=Path.Combine(f.Game.RomFS,"a/0/9/9");Directory.CreateDirectory(Path.GetDirectoryName(path)!);f.WriteGarc(path,Enumerable.Range(0,27).Select(i=>i>=9?record:new byte[]{1,2,3}).ToArray(),f.Game.GetGARCReference("personal").GetRelativeGARC(99-f.Game.GetGARCReference("personal").FileNumber,"titlescreen"));
            var title=new GameTitleScreenService(f.Session);check(title.Archives.Count==18&&title.Images(9).Count==1,version+" Gen7 exposes 18 ALYT title resources");byte[] replacement=GameGraphicsCodec.Encode(bitmap,XLIMEncoding.RGBA8,true);var alyt=new TitleAlytCodec(record);byte[] replaced=alyt.Replace(1,replacement);check(new TitleAlytCodec(replaced).Read(0).SequenceEqual(new byte[]{1,2,3,4})&&new TitleAlytCodec(replaced).Read(1).SequenceEqual(replacement),version+" growing BFLIM replacement preserves other SARC payloads");byte[] before=File.ReadAllBytes(path);reject(()=>title.Replace(9,1,replacement),version+" Gen7 title rejects unsafe format and size expansion");
            bitmap.SetPixel(0,0,System.Drawing.Color.Blue);replacement=GameGraphicsCodec.EncodeLike(bitmap,image);
            check(replacement.Length==image.Length&&replacement.AsSpan(replacement.Length-40).SequenceEqual(image.AsSpan(image.Length-40)),version+" PNG conversion preserves complete original title texture footer and allocation");title.Replace(9,1,replacement);check(File.ReadAllBytes(path).SequenceEqual(before),version+" Gen7 title proposal is read-only");title.Apply();check(new GameTitleScreenService(f.Session).Read(9,1).SequenceEqual(replacement),version+" Gen7 title save roundtrips a compatible texture");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);SeedMart(f);
            var wild=new ThreeDS.Progressive.RecordEditorView(new GameEncounterService(f.Session));
            ((System.Windows.Controls.Button)wild.FindName("RandomizerButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var fields=(Dictionary<string,System.Windows.FrameworkElement>)typeof(ThreeDS.Progressive.RecordEditorView).GetField("_options",flags)!.GetValue(wild)!;
            var table=(System.Windows.FrameworkElement)typeof(ThreeDS.Progressive.RecordEditorView).GetField("_bstTable",flags)!.GetValue(wild)!;
            ((System.Windows.Controls.CheckBox)fields["Progressive"]).IsChecked=true;check(table.Visibility==System.Windows.Visibility.Visible,version+" WPF shows BST table when checked");
            ((System.Windows.Controls.CheckBox)fields["Progressive"]).IsChecked=false;check(table.Visibility==System.Windows.Visibility.Collapsed,version+" WPF hides BST table when unchecked");
            check(!fields.ContainsKey("AlolanForms")&&!fields.ContainsKey("G7"),version+" WPF hides Gen7-exclusive wild options");
            ((System.Windows.Controls.CheckBox)fields["Progressive"]).IsChecked=true;
            wild.Foreground=(System.Windows.Media.Brush)wild.FindResource("TextMain");
            var row=new TrainerRuleTableRow{Id=1,Group="Boss",Trainer="Test",Ace=15};var rules=new ThreeDS.Progressive.TrainerRulesWindow([row],false);
            var grid=(System.Windows.Controls.DataGrid)typeof(ThreeDS.Progressive.TrainerRulesWindow).GetField("_grid",flags)!.GetValue(rules)!;
            check(!grid.Columns.Any(c=>c.Header?.ToString() is "Mejor estadística" or "Tolerancia" or "Better Movesets"),version+" WPF move-rule table omits redundant global columns");
            var mart=new ThreeDS.Progressive.Gen6MartView(f.Session);var checks=(Dictionary<string,System.Windows.Controls.CheckBox>)typeof(ThreeDS.Progressive.Gen6MartView).GetField("_checks",flags)!.GetValue(mart)!;checks["EVItems"].IsChecked=true;mart.Preview();
            var pending=(EditorProposal)typeof(ThreeDS.Progressive.Gen6MartView).GetField("_pending",flags)!.GetValue(mart)!;
            check(pending is not null&&pending.Changes.Count>0&&mart.RememberOptions().Options["EVItems"]==1,version+" WPF Gen6 shop checks prepare local changes and remember Global options");
            checks["Candies"].IsChecked=true;check(typeof(ThreeDS.Progressive.Gen6MartView).GetField("_pending",flags)!.GetValue(mart) is null,version+" changing shop check invalidates frozen proposal");
            string? target=Environment.GetEnvironmentVariable("PROGRESSIVE_UI_CAPTURE_DIR");if(target is not null)
            {
                Directory.CreateDirectory(target);foreach(var (view,name) in new[]{((System.Windows.FrameworkElement)wild,"wild-bst-"+version),(mart,"marts-"+version),(rules,"rules-"+version),(table,"bst-table-"+version)})
                {view.Measure(new System.Windows.Size(1250,900));view.Arrange(new System.Windows.Rect(0,0,1250,900));view.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1250,900,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(view);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using var output=File.Create(Path.Combine(target,name+".png"));encoder.Save(output);}
            }
        }
    }
    internal static void SeedMart(Fixture f)
    {
        byte[] code=new byte[0x403000],header=new byte[2048];void H(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(header,at);
        H(16,0x100000);H(20,1024);H(24,0x400000);H(32,0x500000);H(36,2);H(40,0x1800);H(48,0x502000);H(52,1);H(56,0x1000);File.WriteAllBytes(Path.Combine(f.Root,"DecryptedExHeader.bin"),header);
        int marker=0x400600,stock=marker+GameMart6Service.Vanilla.Length;GameMart6Service.Vanilla.CopyTo(code,marker);int[] counts=f.Game.ORAS?GameMart6Service.CountsORAS:GameMart6Service.CountsXY;counts.Select(n=>(byte)n).ToArray().CopyTo(code,stock-(f.Game.ORAS?0x236:0x1BA));int cursor=stock;foreach(int count in counts)for(int i=0;i<count;i++){BitConverter.GetBytes((ushort)(4+i%10)).CopyTo(code,cursor);cursor+=2;}BitConverter.GetBytes((uint)(0x100000+stock)).CopyTo(code,0x402100);File.WriteAllBytes(Path.Combine(f.Game.ExeFS,".code.bin"),code);
        var items=Enumerable.Range(0,1000).Select(_=>{byte[] b=new byte[0x30];BitConverter.GetBytes((ushort)100).CopyTo(b,0);b[^1]=0xA5;return b;}).ToArray();f.WriteGarc(ModuleChecks.PathFor(f,"item"),items,f.Game.GetGARCReference("item"));
    }
    internal static byte[] Alyt(byte[] image)
    {
        int sarcSize=((256+image.Length+127)&~127);byte[] result=new byte[128+sarcSize];void W(int at,int value)=>BitConverter.GetBytes(value).CopyTo(result,at);void S(int at,ushort value)=>BitConverter.GetBytes(value).CopyTo(result,at);
        "ALYT"u8.CopyTo(result);W(32,64);W(36,result.Length-64);int p=128;"SARC"u8.CopyTo(result.AsSpan(p));S(p+4,20);S(p+6,0xFEFF);W(p+8,sarcSize);W(p+12,128);W(p+16,0x100);"SFAT"u8.CopyTo(result.AsSpan(p+20));S(p+24,12);S(p+26,2);W(p+28,0x65);
        W(p+36,0x1000000);W(p+40,0);W(p+44,4);W(p+52,0x1000004);W(p+56,128);W(p+60,128+image.Length);"SFNT"u8.CopyTo(result.AsSpan(p+64));S(p+68,8);Encoding.ASCII.GetBytes("layout.bflyt\0").CopyTo(result,p+72);Encoding.ASCII.GetBytes("title.bflim\0").CopyTo(result,p+88);new byte[]{1,2,3,4}.CopyTo(result,p+128);image.CopyTo(result,p+256);return result;
    }
    internal static void RunReal(IEnumerable<string> roots)
    {
        using var state=new TestState();foreach(string root in roots)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
            if(project.Generation==6)
            {
                var editor=new GameMart6Service(project);if(!editor.SupportsExpansion)throw new IOException(editor.ExpansionReason);var rows=editor.ReadAllItems();rows[0]=rows[0].Append(50).ToArray();rows[1]=rows[1].SkipLast(1).ToArray();var options=EditorValues.Flatten(editor.RandomOptions);options["UseChecks"]=1;var before=Directory.GetFiles(project.GameConfiguration.ExeFS).Where(p=>p.EndsWith("code.bin")).ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));var proposal=editor.PrepareDraft(rows,options,editor.Entries.Select(e=>e.Id).ToArray(),11);
                using var f=new Fixture(project.GameConfiguration.Version);string code=Path.Combine(f.Game.ExeFS,".code.bin");File.WriteAllBytes(code,proposal.Files.Single().Proposed());string exheader=new[]{"DecryptedExHeader.bin","ExHeader.bin","exheader.bin"}.Select(n=>Path.Combine(root,n)).First(File.Exists);File.Copy(exheader,Path.Combine(f.Root,"DecryptedExHeader.bin"));var clone=new GameMart6Service(f.Session);if(!clone.ReadAllItems().Zip(rows).All(pair=>pair.First.SequenceEqual(pair.Second)))throw new IOException("Real Gen6 variable mart roundtrip mismatch.");foreach(var p in before)if(!SHA256.HashData(File.ReadAllBytes(p.Key)).SequenceEqual(p.Value))throw new IOException("Original changed.");Console.WriteLine($"PASS REAL MART6: {root}; add/delete roundtrip; {editor.Capacity} total slots; code size and ExHeader unchanged; original SHA preserved.");
            }
            else
            {
                var editor=new GameTitleScreenService(project);string source=editor.ArchivePath;byte[] hash=SHA256.HashData(File.ReadAllBytes(source));int images=0;
                foreach(var archive in editor.Archives)foreach(var entry in editor.Images(archive.Id)){using var bmp=GameGraphicsCodec.Decode(editor.Read(archive.Id,entry.Id));images++;}
                using var f=new Fixture(project.GameConfiguration.Version);string path=Path.Combine(f.Game.RomFS,"a/0/9/9");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(source,path);var clone=new GameTitleScreenService(f.Session);int id=clone.Images(9)[0].Id;using var bitmap=GameGraphicsCodec.Decode(clone.Read(9,id));bitmap.SetPixel(0,0,System.Drawing.Color.Magenta);byte[] replacement=GameGraphicsCodec.Encode(bitmap,XLIMEncoding.RGBA8,true);clone.Replace(9,id,replacement);clone.Apply();if(!new GameTitleScreenService(f.Session).Read(9,id).SequenceEqual(replacement))throw new IOException("Real Gen7 title roundtrip mismatch.");if(!SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(hash))throw new IOException("Original changed.");Console.WriteLine($"PASS REAL TITLE7: {root}; 18 title resources; {images} images decoded; clone replacement saved/reopened; original SHA preserved.");
            }
        }
    }
}
