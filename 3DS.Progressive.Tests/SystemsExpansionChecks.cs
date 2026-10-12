using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class SystemsExpansionChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        // Pure native ARM USUM lookup fixture: LDR r2,[pc,#0x70] points to
        // the 67-slot table; no actual ROM data or fixed US/UM offsets needed.
        byte[] tutorCode = new byte[0x1200];
        BitConverter.GetBytes(0xE59F2070u).CopyTo(tutorCode,0x128);
        BitConverter.GetBytes(0xE92D4070u).CopyTo(tutorCode,0x12C);
        BitConverter.GetBytes(0xE3500043u).CopyTo(tutorCode,0x158);
        BitConverter.GetBytes(0xE3A00029u).CopyTo(tutorCode,0x180);
        BitConverter.GetBytes(0xE354002Bu).CopyTo(tutorCode,0x18C);
        BitConverter.GetBytes(0x00100800u).CopyTo(tutorCode,0x1A0);
        for(int i=0;i<67;i++)
            BitConverter.GetBytes((ushort)(i+10)).CopyTo(tutorCode,0x800+i*2);
        ushort[] tutorMoves = Enumerable.Range(0,67)
            .Select(i=>(ushort)(i+100)).ToArray();
        var patchedTutorCode=UsumTutorCodePatch.Prepare(tutorCode,tutorMoves);
        check(patchedTutorCode.TableOffset==0x800&&patchedTutorCode.ChangedSlots==67
            &&tutorMoves.SequenceEqual(Enumerable.Range(0,67)
                .Select(i=>BitConverter.ToUInt16(patchedTutorCode.Output,0x800+i*2)))
            &&tutorCode.AsSpan(0,0x800).SequenceEqual(patchedTutorCode.Output.AsSpan(0,0x800))
            &&tutorCode.AsSpan(0x886).SequenceEqual(patchedTutorCode.Output.AsSpan(0x886)),
            "USUM tutors patch only 67 x 2 native ARM code.bin bytes");
        check(UsumTutorCodePatch.Prepare(patchedTutorCode.Output,tutorMoves).ChangedSlots==0,
            "USUM tutors code.bin synchronization is idempotent");
        var unknownTutorCode=(byte[])tutorCode.Clone();
        unknownTutorCode[0x180]=0;
        reject(()=>UsumTutorCodePatch.Prepare(unknownTutorCode,tutorMoves),
            "USUM tutors refuse missing native compatibility signature");
        reject(()=>UsumTutorCodePatch.Prepare(tutorCode,tutorMoves.Take(66).ToArray()),
            "USUM tutors reject incorrect 67-slot move count");

        // Regression: deleting an ORIGINAL middle slot must work, reindex the
        // remaining entries and refuse the final slot or BP (fixed-size) rows.
        var regular=new MartShop(0,false,"Prueba regular",
            [new MartSlotRow(1,10),new MartSlotRow(2,20),new MartSlotRow(3,30)]);
        check(GameMartService.TryRemoveRegularSlot(regular,regular.Rows[1]) &&
            regular.Rows.Select(r=>r.ItemId).SequenceEqual([10,30]) &&
            regular.Rows.Select(r=>r.Number).SequenceEqual([1,2]),
            "USUM mart deletes an existing middle slot and renumbers it");
        check(GameMartService.TryRemoveRegularSlot(regular,regular.Rows[0]) &&
            !GameMartService.TryRemoveRegularSlot(regular,regular.Rows[0]) &&
            regular.Rows.Count==1, "USUM mart cannot remove the last normal slot");
        var bp=new MartShop(0,true,"Prueba BP",[new MartSlotRow(1,1),new MartSlotRow(2,2)]);
        check(!GameMartService.TryRemoveRegularSlot(bp,bp.Rows[0]) && bp.Rows.Count==2,
            "USUM BP shops retain fixed row counts");
        using var f=new Fixture(GameVersion.USUM);
        var config=f.Game;
        // Shops must never fall back to hard-coded offsets or create a bogus
        // Shop.cro when a project lacks the expected relocation metadata.
        string shopCro=Path.Combine(config.RomFS,"Shop.cro");
        reject(()=>new GameMartService(f.Session),
            "USUM Mart editor refuses missing Shop.cro instead of guessing tables");
        byte[] unknownShop=new byte[0x9000];
        File.WriteAllBytes(shopCro,unknownShop);
        reject(()=>new GameMartService(f.Session),
            "USUM Mart editor rejects unrecognized CRO before any edit");
        check(File.ReadAllBytes(shopCro).SequenceEqual(unknownShop),
            "Unknown Shop.cro remains byte-identical after rejected editor load");
        File.Delete(shopCro);
        string EggPath()=>Path.Combine(config.RomFS,config.GetGARCFileName("eggmove"));
        string MegaPath()=>Path.Combine(config.RomFS,config.GetGARCFileName("megaevo"));
        byte[][] egg=Enumerable.Range(0,840).Select(i=>
        {
            var row=new EggMoves7([0,0,0,0]){Moves=i==0?[]:[1,2,3],FormTableIndex=0};
            return row.Write();
        }).ToArray();
        f.WriteGarc(EggPath(),egg,config.GetGARCReference("eggmove"));
        var editor=new GameEggMoveService(f.Session);
        var originalEgg=File.ReadAllBytes(EggPath());
        var values=EditorValues.Flatten(editor.Read(1));
        check(values["Move0"]==1&&values["Move1"]==2&&values["Move2"]==3,
            "USUM egg move serialization reads original per-species moves");
        values["Move1"]=15;
        editor.Save(1,values);
        check(new EggMoves7(new GARC.MemGARC(File.ReadAllBytes(EggPath())).Files[1]).Moves.SequenceEqual([1,15,3]),
            "Egg Move save changes one species while preserving order and GARC");
        var eggOptions=EditorValues.Flatten(editor.RandomOptions);
        var beforePreview=File.ReadAllBytes(EggPath());
        var candidate=editor.Preview(eggOptions,[1]);
        check(File.ReadAllBytes(EggPath()).SequenceEqual(beforePreview)&&candidate.Seed>=0,
            "Egg Move original randomizer proposal is frozen and never writes to disk");
        reject(()=>new GameEggMoveService(f.Session).Apply(candidate),
            "Egg Moves reject a proposal from another editor session");

        byte[][] mega=Enumerable.Range(0,840).Select(_=>new byte[16]).ToArray();
        // First Mega slot, original undocumented u6 field preserved.
        BitConverter.GetBytes((ushort)0).CopyTo(mega[1],0);
        BitConverter.GetBytes((ushort)1).CopyTo(mega[1],2);
        BitConverter.GetBytes((ushort)150).CopyTo(mega[1],4);
        BitConverter.GetBytes((ushort)0xCAFE).CopyTo(mega[1],6);
        f.WriteGarc(MegaPath(),mega,config.GetGARCReference("megaevo"));
        var megaEditor=new GameMegaEvolutionService(f.Session);
        var fields=EditorValues.Flatten(megaEditor.Read(1));
        check(fields["Slot0.Enabled"]==1&&fields["Slot0.Item"]==150,
            "Mega Evolution editor reads original two-slot method/item/form format");
        fields["Slot0.Item"]=151;
        megaEditor.Save(1,fields);
        var raw=new GARC.MemGARC(File.ReadAllBytes(MegaPath())).Files[1];
        check(BitConverter.ToUInt16(raw,4)==151&&BitConverter.ToUInt16(raw,6)==0xCAFE,
            "Mega editor only edits activation item and preserves undocumented bytes");

        // The optional USUM Mega unlock must preserve CRO structure and its
        // original story-gate instruction, with a frozen preview.
        string battlePath=Path.Combine(config.RomFS,"Battle.cro");
        byte[] battle=new byte[0x9D300];
        System.Text.Encoding.ASCII.GetBytes("CRO0").CopyTo(battle,0x80);
        BitConverter.GetBytes(0x400).CopyTo(battle,0xB0);
        BitConverter.GetBytes(0x9D000-0x400).CopyTo(battle,0xB4);
        BitConverter.GetBytes(0x300).CopyTo(battle,0xB8);
        BitConverter.GetBytes(0x10).CopyTo(battle,0xBC);
        BitConverter.GetBytes(0x280).CopyTo(battle,0xC0);
        new byte[]{0x01,0x00,0x00,0x0A}.CopyTo(battle,0x9CE24);
        File.WriteAllBytes(battlePath,battle);
        var unlock=new GameMegaUnlockService(f.Session);
        var patch=unlock.Preview();
        check(patch.Changes.Count==1&&File.ReadAllBytes(battlePath).SequenceEqual(battle),
            "Mega from Start only stages the verified USUM CRO instruction without writing");
        unlock.Apply(patch.File,patch.Changes);
        check(File.ReadAllBytes(battlePath).AsSpan(0x9CE24,4)
                .SequenceEqual(new byte[]{0x00,0xF0,0x20,0xE3})
            &&unlock.Preview().Changes.Count==0,
            "Mega from Start applies once and recognizes the installed hook");
        // Exercise both Gen7 code.bin patchers on compatible synthetic signatures.
        string codePath=Path.Combine(config.ExeFS,"code.bin");
        byte[] code=new byte[0x6000];
        byte[] typeSignature=[
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        0xC3, 0x00, 0x00, 0x00, 0xCB, 0x00, 0x00, 0x00, 0xD3, 0x00, 0x00, 0x00, 0xDB, 0x00, 0x00, 0x00,
        0xF3, 0x00, 0x00, 0x00, 0xFB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
    ];
        typeSignature.CopyTo(code,0x3000);
        Array.Fill(code,(byte)4,0x3000+typeSignature.Length,324);
        new byte[]{0x23,0x00,0xD4,0xE5}.CopyTo(code,0x1000);
        new byte[]{0x01,0x50,0x85,0xE2,0x05,0x00,0x50,0xE1,0xDE,0xFF,0xFF,0xCA}
            .CopyTo(code,0x1004);
        new byte[]{0x00,0x20,0x22,0xE0,0x02,0x30,0x21,0xE2,0x03,0x20,0x92,0xE1,0x1C,0x00,0x00}
            .CopyTo(code,0x2000);
        code[0x200F]=0x0A;
        File.WriteAllBytes(codePath,code);
        var typeChart=new GameTypeChartService(f.Session);
        var draft=typeChart.Read();
        check(draft.Length==324&&draft.All(x=>x==4),
            "Type Chart reads 324 validated cells from original Gen7 signature");
        draft[0]=8;draft[1]=0;
        var typeProposal=typeChart.Preview(draft);
        check(typeProposal.Changes.Count==2&&File.ReadAllBytes(codePath).SequenceEqual(code),
            "Type Chart preview preserves source executable byte-for-byte");
        typeChart.Apply(typeProposal.File,typeProposal.Changes);
        byte[] after=File.ReadAllBytes(codePath);
        check(after[0x3000+typeSignature.Length]==8&&after[0x3000+typeSignature.Length+1]==0,
            "Type Chart saved only selected effectiveness cells");
        // The general status bar reports real count-based completion; it does not
        // invent percentages when no measurable total exists.
        ThreeDS.Progressive.UiProgress.Begin("Test · progress");
        check(ThreeDS.Progressive.UiProgress.Snapshot.Percent is null,
            "General progress remains indeterminate when no measurable total exists");
        ThreeDS.Progressive.UiProgress.Count("2/8 real files",2,8);
        var progress=ThreeDS.Progressive.UiProgress.Snapshot;
        check(progress.Percent==25&&progress.Completed==2&&progress.Total==8,
            "General progress calculates true 25% from measured work");
        ThreeDS.Progressive.UiProgress.End();
        // Reproduces the WinForms handle failure that occurred specifically in
        // CTRUtil.WriteROM during the final .3DS phase. The real rebuild uses
        // Task.Run, so check Control.Invoke on an unshown ProgressBar there too.
        bool ctrProgressWorks=Task.Run(() =>
        {
            using var progressBar=GameRomRebuildService.CreateLegacyProgressBar();
            progressBar.Invoke((Action)(() =>
            {
                progressBar.Minimum=0;
                progressBar.Maximum=2;
                progressBar.Value=0;
                progressBar.Step=1;
                progressBar.PerformStep();
            }));
            return progressBar.IsHandleCreated && progressBar.Value==1;
        }).GetAwaiter().GetResult();
        check(ctrProgressWorks,
            "Rebuild .3DS initializes native progress handle before CTRUtil.Invoke");
        var rebuild=new GameRomRebuildService(f.Session);
        reject(()=>rebuild.BuildAsync(RomRebuildKind.RomFS,
                Path.Combine(f.Session.ProjectRoot,"unsafe.romfs")).GetAwaiter().GetResult(),
            "Rebuild prevents writing output inside the source project");
        // Regression for the exact user failure: the original RomFS writer
        // requires a WinForms ProgressBar even from the new WPF background task.
        // This must produce a real, readable IVFC without opening any window.
        string rebuilt=Path.Combine(Path.GetTempPath(),"progressive-rebuild-smoke-"+
            Guid.NewGuid().ToString("N")+".romfs");
        byte[] personalBefore=File.ReadAllBytes(f.PersonalPath);
        try
        {
            var rebuiltResult=rebuild.BuildAsync(RomRebuildKind.RomFS,rebuilt)
                .GetAwaiter().GetResult();
            check(File.Exists(rebuilt)&&rebuiltResult.Bytes>512&&
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rebuilt)))==rebuiltResult.Hash,
                "Rebuild RomFS succeeds on background task and verifies SHA-256 without null WinForms control");
            var ivfc=new RomFS(rebuilt);
            check(ivfc.SuperBlockHash.Length==32&&
                    File.ReadAllBytes(f.PersonalPath).SequenceEqual(personalBefore),
                "Rebuild produces a readable RomFS IVFC and does not modify the source project");
        }
        finally{if(File.Exists(rebuilt))File.Delete(rebuilt);}
        string invalid3ds=Path.Combine(Path.GetTempPath(),
            "progressive-rebuild-no-exheader-"+Guid.NewGuid().ToString("N")+".3ds");
        reject(()=>rebuild.BuildAsync(RomRebuildKind.Full3DS,invalid3ds)
                .GetAwaiter().GetResult(),
            "Rebuild .3DS rejects missing ExHeader before rebuilding RomFS/ExeFS");
        check(!File.Exists(invalid3ds),"Failed .3DS preflight leaves no output artifact");
        var shiny=new GameShinyRateService(f.Session);
        check(shiny.Read().Vanilla&&shiny.Read().AlwaysShinyOffset is not null,
            "Shiny Rate recognizes original PID routine and Everything Shiny signature");
        var reroll=shiny.Preview(125,true);
        check(File.ReadAllBytes(codePath).SequenceEqual(after)&&reroll.Changes.Count>=1,
            "Shiny Rate read-only preview stages ARM instructions and Everything Shiny");
        shiny.Apply(reroll.File,reroll.Changes,false);
        check(File.ReadAllBytes(codePath)[0x200F]==0xEA,
            "Shiny Rate applies original ARM patch and Everything Shiny with history");
        var restore=shiny.Preview(125,false,true);
        shiny.Apply(restore.File,restore.Changes,true);
        check(File.ReadAllBytes(codePath)[0x200F]==0x0A&&
            File.ReadAllBytes(codePath).AsSpan(0x1000,4).SequenceEqual(new byte[]{0x23,0x00,0xD4,0xE5}),
            "Shiny Rate can safely restore both original instruction sites");
    }
}
