using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class Gen6MechanicsChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);string tag=version+" mechanics Gen6 ";
            int offset=f.Game.ORAS?0xDB428:0xD12A8;
            byte[] battle=Enumerable.Repeat((byte)0xA5,offset+324+64).ToArray();
            "CRO0"u8.CopyTo(battle.AsSpan(0x80));battle.AsSpan(offset,324).Fill(4);
            string path=Path.Combine(f.Game.RomFS,"DllBattle.cro");File.WriteAllBytes(path,battle);
            var chart=new GameTypeChartService(f.Session);var draft=chart.Read();draft[17]=8;
            var preview=chart.Preview(draft);
            check(File.ReadAllBytes(path).SequenceEqual(battle)&&preview.Changes.Count==1,tag+"type chart preview is read-only");
            chart.Apply(preview.File,preview.Changes);
            byte[] expected=(byte[])battle.Clone();expected[offset+17]=8;
            check(File.ReadAllBytes(path).SequenceEqual(expected),tag+"type chart changes one requested byte only");
            reject(()=>chart.Apply(preview.File,preview.Changes),tag+"stale type chart preview rejected");
            draft[2]=3;reject(()=>chart.Preview(draft),tag+"unknown effectiveness rejected");
            expected[offset]=3;File.WriteAllBytes(path,expected);
            reject(()=>new GameTypeChartService(f.Session),tag+"invalid source effectiveness rejected");
            File.WriteAllBytes(path,battle[..0x180]);
            reject(()=>new GameTypeChartService(f.Session),tag+"truncated CRO rejected");
            byte[] code=new byte[0x4000];
            new byte[]{0x23,0x00,0xD4,0xE5,0x01,0x50,0x85,0xE2,0x05,0x00,0x50,0xE1,0xDE,0xFF,0xFF,0xCA}.CopyTo(code,0x1000);
            new byte[]{0x00,0x20,0x22,0xE0,0x02,0x30,0x21,0xE2,0x03,0x20,0x92,0xE1,0x1C,0x00,0x00,0x0A}.CopyTo(code,0x2000);
            string codePath=Path.Combine(f.Game.ExeFS,"code.bin");File.WriteAllBytes(codePath,code);
            var shiny=new GameShinyRateService(f.Session);var roll=shiny.Preview(125,true);
            check(shiny.Read().Vanilla&&File.ReadAllBytes(codePath).SequenceEqual(code),tag+"shiny signature recognized without writes");
            shiny.Apply(roll.File,roll.Changes,false);
            check(shiny.Read().AlwaysShiny&&shiny.Read().Rerolls==roll.AppliedRerolls,tag+"shiny ARM patch reads back");
            var restore=shiny.Preview(125,false,true);shiny.Apply(restore.File,restore.Changes,true);
            check(File.ReadAllBytes(codePath).SequenceEqual(code),tag+"shiny restore exact source bytes");
            roll=shiny.Preview(125,true);code[^1]^=1;File.WriteAllBytes(codePath,code);
            reject(()=>shiny.Apply(roll.File,roll.Changes,false),tag+"external shiny code write rejected");
            check(File.ReadAllBytes(codePath).SequenceEqual(code),tag+"external code preserved");
        }
    }
}
