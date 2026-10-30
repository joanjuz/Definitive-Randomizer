using System.IO;
using System.Buffers.Binary;
using System.Text.Json;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
internal static class SwordShieldGameplayChecks
{
    private static void U16(byte[] data,int at,int value)=>BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),(ushort)value);
    private static void I32(byte[] data,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at),value);
    internal static byte[] Pack(string name,byte[] member)
    {
        // Independent two-member fixture: first literal LZ4 block, second raw opaque bytes.
        var data=new byte[176+member.Length];"GFLXPACK"u8.CopyTo(data);I32(data,8,4096);I32(data,16,2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(24),64);BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(32),40);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40),SwitchGfPack.NameHash(name));BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(48),SwitchGfPack.NameHash("opaque.bin"));
        data[66]=2;I32(data,68,member.Length);I32(data,72,member.Length+2);I32(data,80,128);
        data[128]=0xF0;data[129]=(byte)(member.Length-15);member.CopyTo(data,130);
        int sibling=144+member.Length;I32(data,92,3);I32(data,96,3);I32(data,104,sibling);data[sibling]=77;data[sibling+1]=88;data[sibling+2]=99;return data;
    }
    internal static byte[] Encounter()
    {
        // Hand-built schema offsets, not produced by the reader/writer under test.
        var b=new byte[192];I32(b,0,32);
        U16(b,16,8);U16(b,18,12);U16(b,22,8);I32(b,32,16);I32(b,40,8);I32(b,48,1);I32(b,52,28);
        U16(b,64,8);U16(b,66,16);U16(b,68,8);U16(b,70,12);I32(b,80,16);I32(b,92,4);I32(b,96,1);I32(b,100,28);
        U16(b,112,10);U16(b,114,12);U16(b,116,4);U16(b,118,5);U16(b,120,8);I32(b,128,16);b[132]=4;b[133]=7;I32(b,136,8);I32(b,144,1);I32(b,148,28);
        U16(b,160,10);U16(b,162,12);U16(b,164,4);U16(b,166,8);U16(b,168,5);I32(b,176,16);b[180]=25;b[181]=2;I32(b,184,1);return b;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        check(SwitchGfPack.NameHash("")==0xCBF29CE484222645UL,"Swsh_Game_HashUsesGameSpecificBasis");
        var stats=SwordShieldPersonalRandomizer.Generate(SwordShieldChecks.Table(),SwordShieldChecks.Table(),new(123,false,false,Stats:true));var statsTable=new SwordShieldPersonalTable(stats.ToArray());
        check(stats.Changes.Count>0&&statsTable.Entries().All(e=>e.BST==new SwordShieldPersonalTable(SwordShieldChecks.Table()).Read(e.Index).BST),"Swsh_Game_StatsKeepBst");
        var statsBytes=stats.ToArray();var statsOriginal=SwordShieldChecks.Table();
        check(Enumerable.Range(0,statsBytes.Length).All(i=>i%176<6||statsBytes[i]==statsOriginal[i]),"Swsh_Game_StatsOnlyModifySixBaseValues");
        byte[] literal={0x50,65,66,67,68,69};check(SwitchGfPack.DecodeLz4(literal,5).SequenceEqual("ABCDE"u8.ToArray()),"Swsh_Game_Lz4IndependentLiteral");
        byte[] match={0x10,65,1,0,0x50,66,67,68,69,70};check(SwitchGfPack.DecodeLz4(match,10).SequenceEqual("AAAAABCDEF"u8.ToArray()),"Swsh_Game_Lz4OverlappingMatch");
        reject(()=>SwitchGfPack.DecodeLz4(new byte[]{0,0,0},4),"Swsh_Game_Lz4RejectZeroDistance");reject(()=>SwitchGfPack.DecodeLz4(literal,4),"Swsh_Game_Lz4RejectWrongLength");
        foreach(int size in new[]{0,1,14,15,270,10000}){var b=Enumerable.Range(0,size).Select(i=>(byte)i).ToArray();check(SwitchGfPack.DecodeLz4(SwitchGfPack.EncodeLz4(b),size).SequenceEqual(b),"Swsh_Game_Lz4LiteralBoundary_"+size);}
        var packBytes=Pack("encount_k.bin",Encounter());var pack=new SwitchGfPack(packBytes);var fb=new SwitchFlatBuffer(pack.Read(pack.Find("encount_k.bin")));
        check(fb.Tables(fb.Root,1,true).Length==1&&fb.Data[180]==25,"Swsh_Game_FlatBufferIndependentFixture");
        var edited=(byte[])fb.Data.Clone();I32(edited,184,3);var rewritten=pack.Rewrite(new Dictionary<int,byte[]>{{0,edited}});var reopened=new SwitchGfPack(rewritten);
        check(reopened.Read(0).SequenceEqual(edited)&&reopened.Read(1).SequenceEqual(new byte[]{77,88,99}),"Swsh_Game_ArchivePreservesOpaqueSibling");
        check(rewritten.AsSpan(112,packBytes.Length-112).SequenceEqual(packBytes.AsSpan(112)),"Swsh_Game_ArchivePreservesOriginalPackedBytes");
        var broken=(byte[])fb.Data.Clone();I32(broken,40,int.MaxValue);reject(()=>new SwitchFlatBuffer(broken).Tables(32,1,true),"Swsh_Game_FlatBufferRejectOverflowReference");
        var files=new Dictionary<string,byte[]>();byte[] team=new byte[64],header=new byte[20];header[3]=2;
        for(int i=0;i<2;i++){int at=i*32;U16(team,at+10,12+i);U16(team,at+12,i+1);U16(team,at+14,1);team[at+9]=1;for(int j=16;j<32;j++)team[at+j]=(byte)(j+i);}
        const string path="bin/trainer/trainer_poke/trainer_poke_001.bin";const string hd="bin/trainer/trainer_data/trainer_data_001.bin";files[path]=team;files[hd]=header;
        var options=new SwordShieldGameplayOptions(20261021,Trainers:true,SimilarBst:false);var result=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,options);var output=result.Files()[path];
        check(team[9]==1&&result.EligibleSlots==2,"Swsh_Game_TrainerPreviewDoesNotWriteInput");
        check(Enumerable.Range(0,64).All(i=>output[i]==team[i]||i%32 is 9 or 12 or 13 or 14 or 15),"Swsh_Game_TrainerPreservesLevelsMovesItemsAiIvEv");
        check(result.Files()[path].SequenceEqual(SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,options).Files()[path]),"Swsh_Game_TrainerSeedReproduces");
        header[3]=3;reject(()=>SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,options),"Swsh_Game_RejectTeamCountMismatch");header[3]=2;
        files[SwordShieldGameplay.EncountersPath]=Pack("encount_k.bin",Encounter());var wild=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,new(1,HiddenEncounters:true));var wildBytes=new SwitchGfPack(wild.Files()[SwordShieldGameplay.EncountersPath]).Read(0);
        check(wildBytes[132]==4&&wildBytes[133]==7&&wildBytes[180]==25&&wildBytes[181]==0,"Swsh_Game_EncounterPreservesLevelsWeightsAndResetsForm");
        check(Enumerable.Range(0,192).All(i=>wildBytes[i]==fb.Data[i]||i==181||i>=184&&i<188),"Swsh_Game_EncounterOnlySpeciesAndFormBytes");
        reject(()=>SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),true,new(1,HiddenEncounters:true)),"Swsh_Game_ShieldDoesNotPatchSwordMember");
        check(SwordShieldGameplay.Generate(new Dictionary<string,byte[]>(),SwordShieldChecks.Table(),false,new(1)).Files().Count==0,"Swsh_Game_NoOptionsIsNoOp");
        check(SwordShieldGameplay.IsAllowedItem(4)&&SwordShieldGameplay.IsAllowedItem(17)&&SwordShieldGameplay.IsAllowedItem(149)&&!SwordShieldGameplay.IsAllowedItem(328)&&!SwordShieldGameplay.IsAllowedItem(1278),"Swsh_Game_ItemPoolExcludesTmsAndKeys");
        FieldItems(check,reject);
        byte[] names=new byte[40];U16(names,0,1);U16(names,2,1);I32(names,4,24);I32(names,12,16);I32(names,16,24);I32(names,20,12);U16(names,24,6);
        ushort key=0x7C89;string spanish="Flabé";for(int i=0;i<6;i++){U16(names,28+2*i,(i<5?spanish[i]:0)^key);key=(ushort)((key<<3)|(key>>13));}
        check(SwordShieldText.Read(names).Single()==spanish,"Swsh_Game_LocalizedNamesDecryptSpanishUtf16");
        I32(names,20,100);reject(()=>SwordShieldText.Read(names),"Swsh_Game_LocalizedNamesRejectInvalidOffset");
        string temp=Path.Combine(Path.GetTempPath(),"dr-swsh-game-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string root=SwordShieldChecks.Fixture(temp,"source");foreach(var pair in files.Where(p=>p.Key!=SwordShieldGameplay.EncountersPath)){string file=Path.Combine(root,"romfs",pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,pair.Value);}
            var project=SwordShieldProject.Open(root);var preview=project.PreviewGameplay(options);check(!project.HasChanges&&project.GameplayHistory.Count==0,"Swsh_Game_ProjectPreviewWaitsForApply");
            project.ApplyGameplay(preview);check(project.HasChanges&&project.GameplayHistory.Single().Options.Seed==options.Seed,"Swsh_Game_ProjectJournalsSeedAndChanges");
            reject(()=>project.ApplyGameplay(preview),"Swsh_Game_ProjectRejectsStalePreview");
            var other=SwordShieldProject.Open(root);reject(()=>other.ApplyGameplay(preview),"Swsh_Game_ProjectRejectsForeignPreview");
            string outputFolder=Path.Combine(temp,"mod");project.Export(outputFolder,SwitchModLayout.Ryujinx);
            check(File.ReadAllBytes(Path.Combine(outputFolder,"romfs",path)).SequenceEqual(output)&&!File.Exists(Path.Combine(outputFolder,"romfs",SwordShieldProject.PersonalRelativePath)),"Swsh_Game_ExportOnlyChangedResources");
            using(var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(outputFolder,"definitive-mod.json"))))check(manifest.RootElement.GetProperty("Gameplay").GetArrayLength()==1&&manifest.RootElement.GetProperty("Resources").GetArrayLength()==1,"Swsh_Game_ManifestIncludesResourceHashes");
            project.Apply(project.Preview(1,SwordShieldField.HP,99));project.Export(Path.Combine(temp,"combined"),SwitchModLayout.Ryujinx);
            check(File.Exists(Path.Combine(temp,"combined/romfs",path))&&File.Exists(Path.Combine(temp,"combined/romfs",SwordShieldProject.PersonalRelativePath)),"Swsh_Game_CombinedExportPreservesPersonalAndTrainerChanges");
            var pending=project.PreviewGameplay(options);string originalPath=Path.Combine(root,"romfs",path);File.WriteAllBytes(originalPath,new byte[64]);reject(()=>project.ApplyGameplay(pending),"Swsh_Game_RejectChangedSource");
            File.WriteAllBytes(originalPath,team);
            string added=Path.Combine(root,"romfs/bin/trainer/trainer_poke/trainer_poke_999.bin");File.WriteAllBytes(added,team);reject(()=>project.PreviewGameplay(options),"Swsh_Game_RejectChangedTrainerDirectory");File.Delete(added);
            project.Reset();check(!project.HasChanges&&project.GameplayHistory.Count==0,"Swsh_Game_ResetClearsAllOverlays");
        }
        finally{Directory.Delete(temp,true);}
    }
    private sealed class BufferFixture
    {
        private readonly List<byte> _bytes=new(new byte[4]);
        private int Reserve(int count){int at=_bytes.Count;_bytes.AddRange(new byte[count]);return at;}
        internal void Int(int at,int value){byte[] b=BitConverter.GetBytes(value);for(int i=0;i<4;i++)_bytes[at+i]=b[i];}
        internal void Long(int at,ulong value){byte[] b=BitConverter.GetBytes(value);for(int i=0;i<8;i++)_bytes[at+i]=b[i];}
        internal int Table(int count,params int[] present)
        {
            int vt=Reserve(4+2*count),table=Reserve(4+8*count);var v=BitConverter.GetBytes((ushort)(4+2*count));_bytes[vt]=v[0];_bytes[vt+1]=v[1];v=BitConverter.GetBytes((ushort)(4+8*count));_bytes[vt+2]=v[0];_bytes[vt+3]=v[1];
            foreach(int field in present){v=BitConverter.GetBytes((ushort)(4+8*field));_bytes[vt+4+field*2]=v[0];_bytes[vt+5+field*2]=v[1];}Int(table,table-vt);return table;
        }
        internal int Field(int table,int field)=>table+4+8*field;
        internal void Ref(int at,int target)=>Int(at,target-at);
        internal int Vector(int count,int stride){int at=Reserve(4+count*stride);Int(at,count);return at;}
        internal int String(string value){byte[] bytes=System.Text.Encoding.UTF8.GetBytes(value);int at=Reserve(5+bytes.Length);Int(at,bytes.Length);for(int i=0;i<bytes.Length;i++)_bytes[at+4+i]=bytes[i];return at;}
        internal byte[] Finish(int root){Int(0,root);return _bytes.ToArray();}
    }
    private static void FieldItems(Action<bool,string> check,Action<Action,string> reject)
    {
        var b=new BufferFixture();int root=b.Table(1,0);int zones=b.Vector(1,4);b.Ref(b.Field(root,0),zones);int zone=b.Table(20,6,19);b.Ref(zones+4,zone);
        int visible=b.Vector(1,4);b.Ref(b.Field(zone,6),visible);int holder=b.Table(1,0);b.Ref(visible+4,holder);int item=b.Table(8,2,6,7);b.Ref(b.Field(holder,0),item);
        b.Ref(b.Field(item,2),b.String("bin/field/model/unit_obj/unit_obj_itemred01/unit_obj_itemred01"));
        int hashes=b.Vector(2,8);b.Ref(b.Field(item,6),hashes);b.Long(hashes+4,111);b.Long(hashes+12,333);int quantities=b.Vector(2,4);b.Ref(b.Field(item,7),quantities);b.Int(quantities+4,3);b.Int(quantities+8,7);
        int hidden=b.Vector(1,4);b.Ref(b.Field(zone,19),hidden);int hh=b.Table(1,0);b.Ref(hidden+4,hh);int hi=b.Table(4,2,3);b.Ref(b.Field(hh,0),hi);b.Int(b.Field(hi,3),45);int chances=b.Vector(2,4);b.Ref(b.Field(hi,2),chances);
        var allowed=new HashSet<int>();for(int i=0;i<2;i++){int chance=b.Table(3,0,1,2);b.Ref(chances+4+4*i,chance);int at=b.Field(chance,0);b.Long(at,i==0?111UL:222UL);for(int j=0;j<8;j++)allowed.Add(at+j);b.Int(b.Field(chance,1),i==0?25:75);b.Int(b.Field(chance,2),i+1);}
        for(int j=0;j<8;j++)allowed.Add(hashes+4+j);
        byte[] placement=b.Finish(root),ahtb=new byte[23];"AHTB"u8.CopyTo(ahtb);I32(ahtb,4,1);U16(ahtb,16,5);"test"u8.CopyTo(ahtb.AsSpan(18));
        // Use a standalone archive fixture with four members and a real folder name index.
        var members=new Dictionary<string,byte[]>{{"AreaNameHashTable.tbl",ahtb},{"test.bin",placement}};byte[] archive=MultiPack(members);
        var map=new byte[52];I32(map,0,3);for(int i=0;i<3;i++){BinaryPrimitives.WriteUInt64LittleEndian(map.AsSpan(4+16*i),(ulong)(111*(i+1)));I32(map,12+16*i,i==0?4:i==1?17:328);}
        var files=new Dictionary<string,byte[]>{{SwordShieldGameplay.PlacementPath,archive},{SwordShieldGameplay.ItemHashesPath,map}};
        var result=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,new(123,VisibleItems:true,HiddenItems:true));
        byte[] output=new SwitchGfPack(result.Files()[SwordShieldGameplay.PlacementPath]).Read(1);
        check(result.EligibleSlots==3&&result.SkippedItems==0&&result.Changes.Count>0,"Swsh_Game_ItemsReadVisibleAndWeightedHiddenVariants");
        check(Enumerable.Range(0,placement.Length).All(i=>output[i]==placement[i]||allowed.Contains(i)),"Swsh_Game_ItemsPreserveQuantitiesWeightsRespawnAndEvents");
        check(BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(hashes+12))==333,"Swsh_Game_ItemsPreserveKeyAndTmHashes");
        check(result.Changes.All(c=>c.Before!=c.After),"Swsh_Game_ItemsAlwaysDifferFromOriginal");
        check(SwordShieldGameplay.IsAllowedItem(50)&&SwordShieldGameplay.IsAllowedItem(240)&&SwordShieldGameplay.IsAllowedItem(295)&&SwordShieldGameplay.IsAllowedItem(795),"Swsh_Game_ItemsIncludeRareCandyHeldItemsAndStones");
        check(new SwitchGfPack(result.Files()[SwordShieldGameplay.PlacementPath]).Read(0).SequenceEqual(ahtb),"Swsh_Game_ItemsPreserveAreaNameTable");
        // The remaining Flags entries are opaque event data, not extra item variants.
        var eventFixture=(byte[])placement.Clone();BinaryPrimitives.WriteUInt64LittleEndian(eventFixture.AsSpan(hashes+12),111);files[SwordShieldGameplay.PlacementPath]=MultiPack(new Dictionary<string,byte[]>{{"AreaNameHashTable.tbl",ahtb},{"test.bin",eventFixture}});
        var eventResult=SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,new(123,VisibleItems:true));
        var eventOutput=new SwitchGfPack(eventResult.Files()[SwordShieldGameplay.PlacementPath]).Read(1);
        check(BinaryPrimitives.ReadUInt64LittleEndian(eventOutput.AsSpan(hashes+12))==111&&eventResult.EligibleSlots==1,"Swsh_Game_VisibleItemOnlyPatchesFirstHashPreservingEventFlags");
        var corrupt=(byte[])map.Clone();I32(corrupt,0,4);files[SwordShieldGameplay.ItemHashesPath]=corrupt;reject(()=>SwordShieldGameplay.Generate(files,SwordShieldChecks.Table(),false,new(1,VisibleItems:true)),"Swsh_Game_ItemsRejectMalformedHashTable");
    }
    private static byte[] MultiPack(Dictionary<string,byte[]> members)
    {
        int count=members.Count,hashes=48,folder=hashes+count*8,table=folder+16+count*16,dataAt=table+count*24;
        byte[] output=new byte[dataAt+members.Sum(p=>p.Value.Length)];"GFLXPACK"u8.CopyTo(output);I32(output,8,4096);I32(output,16,count);I32(output,20,1);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(24),table);BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(32),hashes);BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(40),folder);I32(output,folder+8,count);
        int i=0;foreach(var pair in members){ulong hash=SwitchGfPack.NameHash(pair.Key);BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(hashes+8*i),hash^1234UL);BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(folder+16+16*i),hash);I32(output,folder+24+16*i,i);I32(output,table+24*i+4,pair.Value.Length);I32(output,table+24*i+8,pair.Value.Length);I32(output,table+24*i+16,dataAt);pair.Value.CopyTo(output,dataAt);dataAt+=pair.Value.Length;i++;}return output;
    }
    internal static void RunUi(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"dr-swsh-game-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        string root=SwordShieldChecks.Fixture(temp,"source");
        byte[] team=new byte[32],header=new byte[20];header[3]=1;U16(team,10,15);U16(team,12,1);U16(team,14,1);
        foreach(var pair in new[]{("trainer_poke","trainer_poke_001",team),("trainer_data","trainer_data_001",header)}){string path=Path.Combine(root,"romfs/bin/trainer",pair.Item1,pair.Item2+".bin");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,pair.Item3);}
        var project=SwordShieldProject.Open(root);var window=new ThreeDS.Progressive.SwordShieldGameplayWindow(project);
        try
        {
            window.Measure(new System.Windows.Size(700,600));window.Arrange(new System.Windows.Rect(0,0,700,600));window.UpdateLayout();
            var grid=(System.Windows.Controls.DataGrid)window.FindName("SwitchGameplayGrid");var apply=(System.Windows.Controls.Button)window.FindName("SwitchGameplayApply");
            check(grid.IsReadOnly&&grid.FrozenColumnCount==1&&grid.EnableRowVirtualization,"Swsh_Game_UI_ReadOnlyVirtualizedPreview");
            check(!apply.IsEnabled,"Swsh_Game_UI_ApplyDisabledBeforePreview");
            var task=window.PreviewAsync();var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();
            check(!apply.IsEnabled&&grid.Items.Count==0,"Swsh_Game_UI_NoOptionsLeavesApplyDisabled");
            var trainer=(System.Windows.Controls.CheckBox)window.FindName("SwitchGameplayOption0");trainer.IsChecked=true;
            task=window.PreviewAsync();while(!task.IsCompleted){if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);}task.GetAwaiter().GetResult();
            check(apply.IsEnabled&&!project.HasChanges&&grid.Items.Count>0,"Swsh_Game_UI_TrainerPreviewWaitsForApply");
            apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(project.HasChanges&&project.GameplayHistory.Count==1&&!apply.IsEnabled,"Swsh_Game_UI_ApplyCommitsOnlySessionOverlay");
            var seed=(System.Windows.Controls.TextBox)window.FindName("SwitchGameplaySeed");check(System.Windows.Automation.AutomationProperties.GetLabeledBy(seed) is not null,"Swsh_Game_UI_SeedHasAccessibleLabel");
        }
        finally{window.Close();Directory.Delete(temp,true);}
    }
    private static void VerifyRealOverlay(string root,string resource,byte[] output)
    {
        byte[] source=File.ReadAllBytes(Path.Combine(root,"romfs",resource));
        if(resource.StartsWith("bin/trainer/",StringComparison.Ordinal))
        {
            if(source.Length!=output.Length||Enumerable.Range(0,source.Length).Any(i=>source[i]!=output[i]&&i%32 is not (9 or 12 or 13 or 14 or 15)))throw new InvalidDataException("Unexpected trainer field difference.");
            return;
        }
        var before=new SwitchGfPack(source);var after=new SwitchGfPack(output);int table=checked((int)BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(24)));var metadata=new HashSet<int>();
        for(int i=0;i<before.Count;i++){for(int j=4;j<12;j++)metadata.Add(table+i*24+j);for(int j=16;j<20;j++)metadata.Add(table+i*24+j);}
        if(output.Length<source.Length||Enumerable.Range(0,source.Length).Any(i=>source[i]!=output[i]&&!metadata.Contains(i)))throw new InvalidDataException("Opaque archive bytes changed.");
        for(int index=0;index<before.Count;index++)
        {
            byte[] raw=before.Read(index),modified=after.Read(index);if(raw.SequenceEqual(modified))continue;
            if(raw.Length!=modified.Length)throw new InvalidDataException("FlatBuffer size changed.");
            var fb=new SwitchFlatBuffer(raw);var allowed=new HashSet<int>();void Allow(int at,int count){if(at<0)return;for(int j=0;j<count;j++)allowed.Add(at+j);}
            if(resource==SwordShieldGameplay.EncountersPath)
            {
                foreach(int zone in fb.Tables(fb.Root,1,true))foreach(int weather in fb.Tables(zone,1,true))foreach(int slot in fb.Tables(weather,2,true)){Allow(fb.Field(slot,1),4);Allow(fb.Field(slot,2,1),1);}
            }
            else
            {
                foreach(int zone in fb.Tables(fb.Root,0,true))
                {
                    foreach(int holder in fb.Tables(zone,6)){int item=fb.Table(holder,0);if(item<0)continue;foreach(int at in fb.Vector(item,6,8))Allow(at,8);}
                    foreach(int holder in fb.Tables(zone,19)){int item=fb.Table(holder,0);if(item<0)continue;foreach(int chance in fb.Tables(item,2))Allow(fb.Field(chance,0,8),8);}
                }
            }
            if(Enumerable.Range(0,raw.Length).Any(i=>raw[i]!=modified[i]&&!allowed.Contains(i)))throw new InvalidDataException("Unknown scalar changed in archive member "+index);
        }
    }
    internal static void Probe(string root,string output)
    {
        var project=SwordShieldProject.Open(root);Console.WriteLine("Names: "+string.Join("; ",project.Entries.Take(3).Select(e=>e.Label))+"; item 4: "+project.ItemName(4));var options=new SwordShieldGameplayOptions(20261021,true,true,true,true,true);
        var preview=project.PreviewGameplay(options);var repeat=project.PreviewGameplay(options);
        var files=preview.Result.Files();foreach(var pair in files)VerifyRealOverlay(root,pair.Key,pair.Value);if(!files.All(p=>repeat.Result.Files()[p.Key].SequenceEqual(p.Value)))throw new Exception("Nondeterministic gameplay preview.");
        Console.WriteLine($"Real read: {preview.Result.EligibleSlots} eligible slots; {preview.Result.Changes.Count} changes; {preview.Result.SkippedItems} protected/unknown items.");
        foreach(var group in preview.Result.Changes.GroupBy(c=>c.Resource.StartsWith("bin/trainer/")?"Entrenadores":c.Resource))Console.WriteLine(group.Key+": "+group.Count());
        if(output!="-"){project.ApplyGameplay(preview);project.Export(output,SwitchModLayout.Ryujinx);Console.WriteLine("Mod: "+output);}
        Console.WriteLine("PASS deterministic generation and source snapshot verification. Gameplay NOT tested.");
    }
}
