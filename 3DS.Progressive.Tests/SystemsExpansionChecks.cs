using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

namespace ThreeDS.Progressive.Tests;

internal static class SystemsExpansionChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var f=new Fixture(GameVersion.USUM);
        var config=f.Game;
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
        BitConverter.GetBytes((ushort)1).CopyTo(mega[1],0);
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

        // Exercise both Gen7 code.bin patchers on compatible synthetic signatures.
        string codePath=Path.Combine(config.ExeFS,"code.bin");
        byte[] code=new byte[0x6000];
        byte[] typeSignature=[
            0x02,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x02,0x00,0x00,0x00,
            0xC3,0x00,0x00,0x00,0xCB,0x00,0x00,0x00,0xD3,0x00,0x00,0x00,0xDB,0x00,0x00,0x00,
            0xF3,0x00,0x00,0x00,0xFB,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,
            0x02,0x00,0x00,0x00,0x00,0x00,0x00,0x02,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00
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
