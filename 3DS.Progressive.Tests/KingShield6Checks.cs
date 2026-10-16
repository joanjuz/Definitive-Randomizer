using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class KingShield6Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);string tag=version+" King Shield ";
            string csv=Path.Combine(f.Root,"shield.csv");File.WriteAllText(csv,"Move,PP,KingShieldAttackMinusOne\n588,7,true\n");
            string cro=Path.Combine(f.Game.RomFS,"DLLBattle.cro");byte[] source=Enumerable.Repeat((byte)0xA5,128).ToArray();Gen6KingShieldSignature.Original.CopyTo(source,32);File.WriteAllBytes(cro,source);
            byte[] movesBefore=File.ReadAllBytes(f.MovePath);var service=new GameMoveService(f.Session);var settings=new BatchSettings{TemplatePath=csv,MoveDescriptions=false};
            var preview=service.PreviewMoves(BatchAction.MoveBalance,settings,[588]);
            check(preview.AdditionalFileCount==1&&File.ReadAllBytes(cro).SequenceEqual(source)&&File.ReadAllBytes(f.MovePath).SequenceEqual(movesBefore),tag+"preview stages CRO and moves without writing originals");
            service.ApplyMovePreview(preview);byte[] expected=(byte[])source.Clone();expected[36]=255;
            check(File.ReadAllBytes(cro).SequenceEqual(expected),tag+"only original ARM minus-two byte changed to minus-one");
            check(f.Session.History.Read().Entries.Last().Files.Count==2,tag+"move and CRO committed together");
            Gen6KingShieldSignature.Validate(expected);check(true,tag+"already patched signature accepted");
            byte[] bad=(byte[])source.Clone();Gen6KingShieldSignature.Original.CopyTo(bad,64);File.WriteAllBytes(cro,bad);
            byte[] afterMoves=File.ReadAllBytes(f.MovePath);reject(()=>service.PreviewMoves(BatchAction.MoveBalance,settings,[588]),tag+"duplicate original signature blocks preview");
            check(File.ReadAllBytes(cro).SequenceEqual(bad)&&File.ReadAllBytes(f.MovePath).SequenceEqual(afterMoves),tag+"duplicate signature leaves project unchanged");
            Gen6KingShieldSignature.Patched.CopyTo(bad,64);File.WriteAllBytes(cro,bad);reject(()=>service.PreviewMoves(BatchAction.MoveBalance,settings,[588]),tag+"mixed original and patched signatures rejected");
            File.WriteAllBytes(cro,new byte[128]);reject(()=>service.PreviewMoves(BatchAction.MoveBalance,settings,[588]),tag+"missing signature rejected");
        }
    }
}
