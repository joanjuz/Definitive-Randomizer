using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

internal static class UsumNatureMintChecks
{
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    private static byte[] Bag()
    {
        byte[] b=new byte[0x12000];
        void U(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(b,at);
        "CRO0"u8.CopyTo(b.AsSpan(0x80));U(0x90,(uint)b.Length);
        U(0xb0,0x200);U(0xb4,0xee00);U(0xb8,0x10000);U(0xbc,0x1000);U(0xc0,0xf000);U(0xc4,0x1000);
        U(0xc8,0xf100);U(0xcc,4);U(0x128,0x11000);
        U(0xf100,0x200);U(0xf104,0xe000);U(0xf108,0);U(0xf10c,0xf000);U(0xf110,0x1000);U(0xf114,1);
        U(0xf118,0x10000);U(0xf11c,0x1000);U(0xf120,2);U(0xf12c,3);
        U(0xc318,0xebffd11e);U(0xddc8,0xebffca72);U(0xc358,0xea00013f);U(0x80dc,0xea0000db);
        return CroIntegrity.Rebuild(b);
    }
    private static byte[] EmptyItem(int id)
    {
        byte[] b=new byte[36];
        if(id is 113 or 114 or 115)b[13]=8;
        else if(id==622){b[8]=0x1f;b[9]=2;b[13]=8;b[15]=255;}
        else if(id>=837){b[8]=0x3f;b[9]=2;b[13]=8;b[15]=255;}
        else if(id!=126)b[13]=2;
        return b;
    }
    internal static UsumNatureMintProfile Setup(Fixture f,bool moon)
    {
        ModuleChecks.CreateArchives(f);
        byte[] code=[1,2,3,(byte)(moon?4:5)];File.WriteAllBytes(Path.Combine(f.Game.ExeFS,"code.bin"),code);
        byte[] header=new byte[0x200];"NCCH"u8.CopyTo(header.AsSpan(0x100));BitConverter.GetBytes(moon?0x00040000001b5100UL:0x00040000001b5000UL).CopyTo(header,0x118);
        File.WriteAllBytes(Path.Combine(f.Root,"HeaderNCCH0.bin"),header);
        byte[] bag=Bag();File.WriteAllBytes(Path.Combine(f.Game.RomFS,"Bag.cro"),bag);
        var items=Enumerable.Range(0,960).Select(_=>new byte[36]).ToArray();
        foreach(var mint in UsumNatureMintCatalog.Mints)items[mint.ItemId]=EmptyItem(mint.ItemId);
        f.WriteGarc(ModuleChecks.PathFor(f,"item"),items,f.Game.GetGARCReference("item"));
        byte[] pocket=new byte[480];foreach(var mint in UsumNatureMintCatalog.Mints)UsumItemPocketTable.Write(pocket,mint.ItemId,mint.ItemId>=622?4:0);
        string pocketPath=Path.Combine(f.Game.RomFS,"a/1/0/3");Directory.CreateDirectory(Path.GetDirectoryName(pocketPath)!);
        f.WriteGarc(pocketPath,[pocket,new byte[]{9,8,7}],f.Game.GetGARCReference("item"));
        var textHashes=new Dictionary<string,string>();
        for(int language=0;language<10;language++){
            var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(language);
            string[][] lines=Enumerable.Range(0,127).Select(_=>new[]{"Preserve"}).ToArray();
            foreach(int bank in new[]{39,40,41,42})lines[bank]=Enumerable.Repeat("???",960).ToArray();
            lines[1]=Enumerable.Range(0,159).Select(i=>"Message"+i).ToArray();
            lines[92]=Enumerable.Range(0,26).Select(i=>"Nature"+i).ToArray();
            foreach(int bank in new[]{39,40,41,42})textHashes[language+":"+bank]=Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\0",UsumNatureMintCatalog.Mints.Select(m=>lines[bank][m.ItemId]))));
            f.WriteGarc(Path.Combine(f.Game.RomFS,reference.Reference),lines.Select(l=>TextFile.GetBytes(f.Game,l)).ToArray(),reference);
        }
        string crr=Path.Combine(f.Game.RomFS,".crr/static.crr");Directory.CreateDirectory(Path.GetDirectoryName(crr)!);
        byte[] c=new byte[0x400];BitConverter.GetBytes(0x380).CopyTo(c,0x350);BitConverter.GetBytes(1).CopyTo(c,0x354);
        File.WriteAllBytes(crr,CroIntegrity.RebuildCrr(c,[bag]));
        return new(moon?"Synthetic-UM":"Synthetic-US",Hash(code),Hash(bag),moon){TextSlotHashes=textHashes};
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var state=new TestState();
        int[] expected=[1,3,4,2,5,8,9,7,15,16,19,17,20,21,23,22,10,11,13,14,12];
        check(UsumNatureMintCatalog.Mints.Select(m=>m.NatureId).SequenceEqual(expected)&&UsumNatureMintCatalog.Mints.Select(m=>m.ItemId).Distinct().Count()==21,"MintCatalogMapping exact 21 nonsequential nature destinations and unique IDs");
        foreach(var mint in UsumNatureMintCatalog.Mints)check(Hash(EmptyItem(mint.ItemId))==UsumNatureMintCatalog.OriginalItemHash(mint.ItemId),"MintReservedSlotShape "+mint.Identity);
        byte[] pockets=Enumerable.Repeat((byte)0xa5,480).ToArray();
        UsumItemPocketTable.Write(pockets,112,1);check(pockets[56]==0x15,"MintPocketPreservation even keeps odd nibble");
        UsumItemPocketTable.Write(pockets,113,3);check(pockets[56]==0x13,"MintPocketPreservation odd keeps even nibble");
        reject(()=>UsumItemPocketTable.Write(pockets,960,1),"MintPocketBounds ID960 rejected");
        reject(()=>UsumItemPocketTable.Write(pockets,113,16),"MintPocketBounds pocket16 rejected");
        reject(()=>UsumNatureMintCatalog.IdentifyCode(new byte[128]),"MintRejectUnknownProfiles unknown executable rejected");
        using(var unrelated=new Fixture(GameVersion.USUM)){
            string[] names=unrelated.Session.ItemNames.ToArray();unrelated.Session.RefreshMintItemNames();
            check(unrelated.Session.ItemNames.SequenceEqual(names),"MintCatalogDoesNotReadUnrelatedMissingTextArchive");
        }
        using(var textFixture=new Fixture(GameVersion.USUM)){
            var original=new TextFile(textFixture.Game){PreserveWhitespace=true,Lines=[" Keep spaces  ","[VAR PKNICK(0000)]","replace"]};
            original.Data[26]=0xca;byte[] beforeText=(byte[])original.Data.Clone();
            var updated=UsumMintTextRows.Patch(textFixture.Game,original.Data,[" Keep spaces  ","[VAR PKNICK(0000)]","Menta nueva","New message"]);
            var decoded=new TextFile(textFixture.Game,updated);
            check(decoded.Lines.SequenceEqual(new[]{" Keep spaces  ","[VAR PKNICK(0000)]","Menta nueva","New message"})&&decoded.LineData[0].SequenceEqual(original.LineData[0])&&decoded.LineData[1].SequenceEqual(original.LineData[1])&&updated[26]==0xca&&original.Data.SequenceEqual(beforeText),"MintTextRowsPreserveWhitespaceCommandsAndOpaqueMetadata");
            reject(()=>UsumMintTextRows.Patch(textFixture.Game,original.Data,["short"]),"MintTextRowsRejectRemoval");
        }
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM}){
            using var other=new Fixture(version);check(new UsumNatureMintService(other.Session).Inspect().State==UsumMintState.Incompatible,"MintRejectOtherGames "+version);
        }
        foreach(bool moon in new[]{false,true}){
            using var f=new Fixture(GameVersion.USUM);var profile=Setup(f,moon);
            Func<byte[],UsumNatureMintProfile> identity=bytes=>Hash(bytes)==profile.CodeSha256?profile:throw new InvalidDataException("Synthetic code mismatch");
            var service=new UsumNatureMintService(f.Session,identity);
            var preview=service.Inspect();if(preview.Proposal is null)throw new Exception("Mint preview: "+preview.Report);
            check(preview.State==UsumMintState.NotInstalled&&preview.Proposal.Files.Count==14,"MintPrepareLifecycle "+profile.Id+" 14 file detached proposal");
            var before=preview.Proposal!.Files.ToDictionary(p=>p.Path,p=>File.ReadAllBytes(p.Path));
            var inventory=Directory.GetFiles(f.Game.RomFS,"*",SearchOption.AllDirectories).Concat(Directory.GetFiles(f.Game.ExeFS,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,p=>Hash(File.ReadAllBytes(p)));
            check(preview.Proposal.Files.All(p=>File.ReadAllBytes(p.Path).SequenceEqual(before[p.Path])),"MintPreviewReadOnly "+profile.Id);
            foreach(var file in preview.Proposal.Files.Where(p=>p.Path.Contains(Path.DirectorySeparatorChar+"a"+Path.DirectorySeparatorChar))){
                var old=new GARC.MemGARC(file.Original()).Files;var after=new GARC.MemGARC(file.Proposed()).Files;
                if(old.Length==127)check(Enumerable.Range(0,old.Length).Where(i=>i is not(1 or 39 or 40 or 41 or 42)).All(i=>old[i].SequenceEqual(after[i])),"MintUnrelatedTextBanksPreserved "+Path.GetFileName(file.Path));
            }
            reject(()=>FileTransaction.Commit(preview.Proposal.Files,i=>{if(i==2)throw new IOException("Injected mint transaction failure");}),"MintTransactionRollback injected failure after partial replacement "+profile.Id);
            check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value))&&f.Session.History.Read().Entries.Count==0,"MintTransactionRollback restores all original bytes "+profile.Id);
            service.Apply(preview);
            check(inventory.Where(p=>!before.ContainsKey(p.Key)).All(p=>Hash(File.ReadAllBytes(p.Key))==p.Value),"MintOnlyDeclaredFilesChanged "+profile.Id);
            check(new UsumNatureMintService(f.Session,identity).Inspect().State==UsumMintState.Installed,"MintInstalledAfterReopen "+profile.Id);
            check(service.Inspect().Proposal is null&&f.Session.History.Read().Entries.Count==1,"MintIdempotence no repeated write or history entry "+profile.Id);
            reject(()=>service.Apply(preview),"MintRepeatedStaleInstall rejected "+profile.Id);
            check(f.Session.History.Read().Entries.Single().Files.Count==14,"MintHistoryAtomicSet "+profile.Id);
            var item=new GameItemService(f.Session);var fields=ThreeDS.Progressive.Models.EditorValues.Flatten(item.Read(113));
            check(fields["Price"]==20000&&fields["UseKeep"]==0&&fields["UseConsume"]==1&&f.Session.ItemNames[113].Contains("Mint"),"MintItemConsumableNamed "+profile.Id);
            var entry=f.Session.History.Read().Entries.Single();RestorePoints.Undo(f.Session.History,f.Root,entry.Id);
            check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value))&&service.Inspect().State==UsumMintState.NotInstalled,"MintUndoRestoresAllFiles "+profile.Id);
            f.Session.RefreshMintItemNames();check(!f.Session.ItemNames[113].Contains("Mint"),"MintUndoRefreshesCatalog "+profile.Id);
            var itemRecords=ModuleChecks.Read(f,"item");itemRecords[113][2]=99;
            f.WriteGarc(ModuleChecks.PathFor(f,"item"),itemRecords,f.Game.GetGARCReference("item"));
            check(service.Inspect().State==UsumMintState.Incompatible&&service.Inspect().Proposal is null,"MintOccupiedItemRejected "+profile.Id);
            File.WriteAllBytes(ModuleChecks.PathFor(f,"item"),before[ModuleChecks.PathFor(f,"item")]);
            var textPath=Path.Combine(f.Game.RomFS,f.Game.GetGARCReference("gametext").GetRelativeGARC(0).Reference);
            var textBefore=File.ReadAllBytes(textPath);var textRecords=new GARC.MemGARC(textBefore).Files;
            var lines=new TextFile(f.Game,textRecords[40]).Lines;lines[622]="Custom reserved object";
            textRecords[40]=TextFile.GetBytes(f.Game,lines);f.WriteGarc(textPath,textRecords,f.Game.GetGARCReference("gametext").GetRelativeGARC(0));
            check(service.Inspect().State==UsumMintState.Incompatible&&service.Inspect().Proposal is null,"MintOccupiedTextRejected "+profile.Id);
            File.WriteAllBytes(textPath,textBefore);
            preview=service.Inspect();string changed=preview.Proposal!.Files[0].Path;
            byte[] external=File.ReadAllBytes(changed);external[^1]^=1;File.WriteAllBytes(changed,external);
            reject(()=>service.Apply(preview),"MintConcurrentEdit rejected before install "+profile.Id);
            check(preview.Proposal.Files.Where(p=>p.Path!=changed).All(p=>File.ReadAllBytes(p.Path).SequenceEqual(p.Original())),"MintConcurrentEditNoPartialWrites "+profile.Id);
            byte[] altered=Bag();altered[0xc318]^=1;var coreProfile=profile with{BagSha256=Hash(altered)};
            reject(()=>UsumNatureMintPatch.Prepare(altered,coreProfile),"MintHookSignatureConflict rejected "+profile.Id);
            var installed=UsumNatureMintPatch.Prepare(Bag(),profile);installed[^1]^=1;
            check(UsumNatureMintPatch.Inspect(installed,profile).State==UsumMintState.Modified,"MintExternalBagMutation detected "+profile.Id);
        }
    }
    internal static void ArmFixtures(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(bool moon in new[]{false,true}){
            var bag=Bag();var p=new UsumNatureMintProfile("ARM",Hash([]),Hash(bag),moon);
            File.WriteAllBytes(Path.Combine(directory,moon?"moon.bin":"sun.bin"),UsumNatureMintPatch.Prepare(bag,p));
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        var app=System.Windows.Application.Current??new ThreeDS.Progressive.App();
        if(app is ThreeDS.Progressive.App progressive)progressive.InitializeComponent();
        using var f=new Fixture(GameVersion.USUM);int opened=0;
        var view=new ThreeDS.Progressive.UsumNatureMintView(f.Session,()=>opened++);
        view.Measure(new System.Windows.Size(1000,720));view.Arrange(new System.Windows.Rect(0,0,1000,720));view.UpdateLayout();
        var nodes=Descendants(view).ToArray();
        check(nodes.OfType<System.Windows.Controls.CheckBox>().Any(c=>System.Windows.Automation.AutomationProperties.GetName(c)==UsumMintItemPolicy.Label&&c.IsChecked==false),"MintUiDistributionOptInOffByDefault");
        check(nodes.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text==UsumNatureMintService.Warning+"."),"MintUiRealNatureWarningVisible");
        var buttons=nodes.OfType<System.Windows.Controls.Button>().ToArray();
        check(buttons.Length==5&&buttons.Where(b=>b.Content.ToString() is "Descargar y preparar iconos (Pok\u00e9API)" or "Instalar iconos preparados").All(b=>!b.IsEnabled)&&buttons.Single(b=>b.Content.ToString()=="Instalar 21 mentas").IsEnabled==false,"MintUiFiveActionsAndNoUnpreparedInstall");
        buttons.Single(b=>b.Content.ToString()=="Abrir Tiendas y BP").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(opened==1&&view.ConfirmLeave(),"MintUiNavigatesExistingMartEditor");
        check(ProjectFeatureAvailability.Supports(f.Session,"UsumNatureMints"),"MintUiUsumRouteAvailable");
        using var other=new Fixture(GameVersion.SM);check(!ProjectFeatureAvailability.Supports(other.Session,"UsumNatureMints"),"MintUiSmRouteUnavailable");
        WpfTestHost.SuppressStartup();ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;
        var main=new ThreeDS.Progressive.MainWindow();
        typeof(ThreeDS.Progressive.MainWindow).GetField("_session",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(main,f.Session);
        var navigate=typeof(ThreeDS.Progressive.MainWindow).GetMethod("NavigateSystem",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var mechanics=new ThreeDS.Progressive.SystemsView(f.Session,"Mechanics",route=>navigate.Invoke(main,[route]),()=>{},()=>{});
        Logical(mechanics).OfType<System.Windows.Controls.Button>().Single(b=>b.Content.ToString()=="Abrir Mentas Gen7").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(((System.Windows.Controls.ContentControl)main.FindName("SectionHost")).Content is ThreeDS.Progressive.UsumNatureMintView,"MintUiMechanicsCardOpensInstaller");main.Close();
        static IEnumerable<System.Windows.DependencyObject> Logical(System.Windows.DependencyObject parent){
            foreach(var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>()){
                yield return child;foreach(var next in Logical(child))yield return next;
            }
        }
        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent){
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++){
                var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;
                foreach(var next in Descendants(child))yield return next;
            }
        }
    }
    internal static void RealCopies(string output,string sun,string moon)
    {
        using var state=new TestState();output=Path.GetFullPath(output);
        string marker=Path.Combine(output,"mint-validation.owner");
        if(Directory.Exists(output)&&(!File.Exists(marker)||File.ReadAllText(marker)!="DefinitiveRandomizer.MintValidation-v1"))throw new IOException("El destino de validación debe ser nuevo o una copia propia interrumpida.");
        foreach(string source in new[]{sun,moon})if(output.StartsWith(Path.GetFullPath(source)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("La copia debe estar separada del original.");
        Directory.CreateDirectory(output);
        File.WriteAllText(marker,"DefinitiveRandomizer.MintValidation-v1");
        foreach((string source,string name) in new[]{(sun,"UltraSol"),(moon,"UltraLuna")}){
            string root=Path.Combine(output,name);
            if(!Directory.Exists(root)){
                Directory.CreateDirectory(root);
                Copy(Path.Combine(source,"ExtractedRomFS"),Path.Combine(root,"RomFS"));
                Copy(Path.Combine(source,"ExtractedExeFS"),Path.Combine(root,"ExeFS"));
                File.Copy(Path.Combine(source,"HeaderNCCH0.bin"),Path.Combine(root,"ncchheader.bin"));
                File.Copy(Path.Combine(source,"DecryptedExHeader.bin"),Path.Combine(root,"exheader.bin"));
            }
            var session=GameProjectSession.Open(root,false);var service=new UsumNatureMintService(session);
            var preview=service.Inspect();if(preview.Proposal is null)throw new IOException(name+": "+preview.Report);
            var before=preview.Proposal.Files.ToDictionary(f=>f.Path,f=>f.Original());
            foreach(var file in before){string relative=Path.GetRelativePath(root,file.Key);string original=Path.Combine(source,relative.Replace("RomFS"+Path.DirectorySeparatorChar,"ExtractedRomFS"+Path.DirectorySeparatorChar));
                if(Hash(File.ReadAllBytes(original))!=Hash(file.Value))throw new IOException("La copia previa difiere de su fuente; no se sobrescribe.");}
            service.Apply(preview);
            if(new UsumNatureMintService(GameProjectSession.Open(root,false)).Inspect().State!=UsumMintState.Installed)throw new IOException("No reconoce la instalación al reabrir.");
            var entry=session.History.Read().Entries.Last();RestorePoints.Undo(session.History,root,entry.Id);
            if(!before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)))throw new IOException("Deshacer no restauró exactamente los 14 archivos.");
            service.Apply(service.Inspect());
            var marts=new GameMartService(session);var shop=marts.RegularShops[0];
            foreach(var mint in UsumNatureMintCatalog.Mints)shop.Rows.Add(new MartSlotRow(shop.Rows.Count+1,mint.ItemId));
            marts.Apply(marts.Prepare());
            var fresh=GameProjectSession.Open(root,false);var reopened=new GameMartService(fresh);
            if(!UsumNatureMintCatalog.Mints.All(m=>reopened.RegularShops[0].Rows.Any(r=>r.ItemId==m.ItemId))||!fresh.ItemNames[114].Contains("Menta")&&!fresh.ItemNames[114].Contains("Mint"))throw new IOException("Tiendas o nombres no persistieron.");
            var items=new GameItemService(fresh);var prices=items.PriceRows().Select(p=>{p.Included=p.Id is 114 or 124 or 840;p.TargetBuy=10;return p;}).ToArray();
            items.Apply(items.PreviewEconomy(prices));
            if(new UsumNatureMintService(fresh).Inspect().State!=UsumMintState.Installed)throw new IOException("Precio o tienda invalidó la instalación.");
            foreach(var file in before){string relative=Path.GetRelativePath(root,file.Key);string original=Path.Combine(source,relative.Replace("RomFS"+Path.DirectorySeparatorChar,"ExtractedRomFS"+Path.DirectorySeparatorChar));
                if(Hash(File.ReadAllBytes(original))!=Hash(file.Value))throw new IOException("Una fuente original cambió.");}
            Console.WriteLine("PASS MintRealCopyLifecycle "+name+": install, reopen, exact undo of 14 files, reinstall, 21 shop entries, editable prices; original inputs unchanged.");
            File.WriteAllText(Path.Combine(output,name+"-validation.txt"),"PASS: 14-file install/reopen/undo; 21 mint shop rows persist; 114/124/840 price=10 for owner manual test. Gameplay NOT tested. Source roots unchanged.\n"+service.Inspect().Report);
        }
        static void Copy(string source,string target){
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se copian enlaces.");Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("No se copian enlaces.");File.Copy(file,Path.Combine(target,Path.GetFileName(file)));}
            foreach(string directory in Directory.GetDirectories(source))Copy(directory,Path.Combine(target,Path.GetFileName(directory)));
        }
    }
}
