using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

internal static class TemplateChecks
{
    internal static void Run(Action<bool,string> check, Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);
            var moves=new GameMoveService(f.Session);
            if(version==GameVersion.SM)
            {
                byte[] before=File.ReadAllBytes(f.MovePath);
                reject(()=>moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{BattlePatches=false,MoveDescriptions=false},[1]),"SM rejects Progressive balance even with battle patches disabled");
                check(File.ReadAllBytes(f.MovePath).SequenceEqual(before),"SM unsupported balance preserves source");continue;
            }
            var saved=f.MoveRecords;
            byte[] csv=File.ReadAllBytes(moves.DefaultTemplatePath);
            int[] all=moves.Entries.Select(m=>m.Id).ToArray();
            var dataOnly=new BatchSettings {BattlePatches=false,MoveDescriptions=false};
            var balance=moves.PreviewMoves(BatchAction.MoveBalance,dataOnly,all);
            check(f.MoveRecords.SelectMany(r=>r).SequenceEqual(saved.SelectMany(r=>r)),version+" template preview preserves disk and config");
            check(balance.ChangedCount>0 && balance.Changes.Any(c=>c.Field=="Potencia"),version+" complete bundled/default balance loads");
            byte[] spore=balance.CopyRecords()[f.Game.Generation==7?147:15];
            check(f.Game.Generation==7?spore[4]==80:spore[3]==70&&spore[4]==100&&spore[5]==15,version+" reference CSV/default move values");
            check(balance.CopyRecords()[1].SequenceEqual(saved[1]),version+" template leaves unlisted move bytes intact");
            if(f.Game.Generation==7)
            {
                var geomancy=balance.CopyRecords()[601];
                check(geomancy[0x15]==3&&geomancy[0x16]==4&&geomancy[0x17]==5&&geomancy[0x18]==2&&geomancy[0x19]==2&&geomancy[0x1A]==1,
                    version+" Geomancy has exactly three replacement stat slots");
                var brutal=balance.CopyRecords()[693];
                check(brutal[3]==30&&(brutal[7]&15)==2&&(brutal[7]>>4)==5&&brutal[0x14]==5,version+" Giro Vil data retains spread multi-hit route");
            }
            moves.ApplyMovePreview(balance);
            check(f.MoveRecords.SelectMany(r=>r).SequenceEqual(balance.CopyRecords().SelectMany(r=>r)),version+" template apply reloads exact proposed records");
            var twice=moves.PreviewMoves(BatchAction.MoveBalance,dataOnly,all);
            check(twice.Changes.Count==0,version+" repeated template is idempotent including stat slots");
            check(File.ReadAllBytes(moves.DefaultTemplatePath).SequenceEqual(csv),version+" template file never rewritten");
            // CSV aliases, stat replacement, flag removal, reserved/Z bytes and integer formats.
            string path=Path.Combine(f.Root,"custom.csv");
            File.WriteAllText(path,"Move,Type,Category,Power,Priority,Effect,UserStat,UserStatChange,TargetStat,TargetStatChange,SetFlags,UnsetFlags,Param0x0B\nEntry1,Hielo,Especial,91,-2,0x123,Ataque,2,Defensa,-1,1,2,0x0B\n");
            var custom=moves.PreviewMoves(BatchAction.MoveBalance,dataOnly with{TemplatePath=path},[1]);
            byte[] record=custom.CopyRecords()[1];
            Move m=f.Game.Generation==7?new Move7(record):new Move6(record);
            check(m.Type==14&&m.Category==2&&m.Power==91&&unchecked((sbyte)m.Priority)==-2&&m.EffectSequence==0x123,version+" bilingual named moves and hex/signed values");
            check(m.Stat1==1&&m.Stat2==2&&m.Stat3==0&&m.Stat1Stage==2&&unchecked((sbyte)m.Stat2Stage)==-1,version+" user and target stat replacement");
            uint flags=Convert.ToUInt32(m.GetType().GetProperty("Flags")!.GetValue(m));
            check((flags&1)!=0&&(flags&2)==0&&record[0x0B]==11,version+" template flags and raw effect parameter");
            if(f.Game.Generation==7)check(record[0x1E]==saved[1][0x1E]&&record[0x1F]==saved[1][0x1F]&&record[0x20]==saved[1][0x20],version+" template preserves unspecified custom Z association");
            foreach(var pair in new[]{("Power","256"),("Power","oops"),("Type","Imaginary"),("ClearFlags","perhaps"),("SetFlags","UnknownFlag"),("Move","UnknownMove"),("HitMin","16"),("BattlePatch","UninstalledMechanic")})
            {
                File.WriteAllText(path,$"Move,{pair.Item1}\n1,{pair.Item2}\n");
                if(pair.Item1=="Move")File.WriteAllText(path,"Move,Power\nUnknownMove,91\n");
                reject(()=>moves.PreviewMoves(BatchAction.MoveBalance,dataOnly with{TemplatePath=path},[1]),version+" rejects invalid template "+pair.Item1+"="+pair.Item2);
            }
            File.WriteAllText(path,"Move,Power\n1,93\n");
            var stale=moves.PreviewMoves(BatchAction.MoveBalance,dataOnly with{TemplatePath=path},[1]);
            File.AppendAllText(path,"2,99\n");
            reject(()=>moves.ApplyMovePreview(stale),version+" rejects CSV changed after preview");
            // A real external patch is staged alongside the move data, even when move data is already balanced.
            string croName=f.Game.Generation==7?"Battle.cro":"DLLBattle.cro";
            string cro=Path.Combine(f.Game.RomFS,croName);
            byte[] pattern=f.Game.Generation==7?Convert.FromHexString("0120A0E3FE00A0E30420C4E51100C4E5260081E31020C4E5"):Convert.FromHexString("0120A0E3FE30A0E3");
            byte[] croBefore=new byte[512];pattern.CopyTo(croBefore,64);File.WriteAllBytes(cro,croBefore);
            File.WriteAllText(path,"Move,PP,KingShieldAttackMinusOne\n588,5,true\n");
            var external=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path,MoveDescriptions=false},[588]);
            check(external.AdditionalFileCount==1&&external.Changes.Any(c=>c.Name==croName),version+" external patch shown in preview");
            check(File.ReadAllBytes(cro).SequenceEqual(croBefore),version+" combat preview writes only its temporary copy");
            moves.ApplyMovePreview(external);
            byte[] croAfter=File.ReadAllBytes(cro);croBefore[68]=0xFF;
            check(croAfter.SequenceEqual(croBefore),version+" King's Shield patch changes only its known instruction byte");
            check(moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path,MoveDescriptions=false},[588]).Changes.Count==0,version+" installed combat patch is idempotent");
            File.WriteAllBytes(cro,new byte[512]);
            byte[] unchanged=File.ReadAllBytes(f.MovePath);
            reject(()=>moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path,MoveDescriptions=false},[588]),version+" incompatible CRO rejects whole proposal");
            check(File.ReadAllBytes(f.MovePath).SequenceEqual(unchanged)&&File.ReadAllBytes(cro).All(b=>b==0),version+" failed CRO preview preserves every project file");
            // Spanish descriptions are an independent bank; newline normalization is verified after writing.
            string textPath=CreateSpanishBank(f);
            var banks=new GARC.MemGARC(File.ReadAllBytes(textPath)).Files;
            int flavor=f.Game.GameText.First(t=>t.Name==TextName.MoveFlavor).Index;
            var descriptions=new TextFile(f.Game,banks[flavor]).Lines;descriptions[1]="xxUntouchedxx";
            byte[] raw=TextFile.GetBytes(f.Game,descriptions);int start=16+BitConverter.ToInt32(raw,20+8);
            foreach(int character in new[]{0,1,11,12})
            {int offset=start+character*2;ushort value=(ushort)(BitConverter.ToUInt16(raw,offset)^('x'^' '));BitConverter.GetBytes(value).CopyTo(raw,offset);}
            banks[flavor]=raw;f.WriteGarc(textPath,banks,f.Game.GetGARCReference("gametext").GetRelativeGARC(6));
            check(new TextFile(f.Game,raw).Lines[1]=="  Untouched  ",version+" fixture retains original edge whitespace");
            File.WriteAllText(path,"Move,Accuracy,InGameDescription\n147,81,\"Primera línea.\\nSegunda línea.\"\n");
            var description=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path},[147]);
            check(description.AdditionalFileCount==1&&description.Changes.Any(c=>c.Field=="Descripción en español"),version+" text changes included in frozen proposal");
            check(new GARC.MemGARC(File.ReadAllBytes(textPath)).Files[flavor].SequenceEqual(banks[flavor]),version+" description preview is read-only");
            int language=f.Game.Language;
            moves.ApplyMovePreview(description);
            var written=new GARC.MemGARC(File.ReadAllBytes(textPath)).Files;
            check(new TextFile(f.Game,written[flavor]).Lines[147]=="Primera línea.\\nSegunda línea.",version+" Spanish description persists with normalized newlines");
            check(written.Where((r,i)=>i!=flavor).SelectMany(r=>r).SequenceEqual(banks.Where((r,i)=>i!=flavor).SelectMany(r=>r)),version+" every other text-bank entry stays byte-identical");
            check(new TextFile(f.Game,written[flavor]).Lines[1]=="  Untouched  ",version+" description patch preserves unedited line whitespace");
            check(f.Game.Language==language,version+" template never switches the live project language");
            check(moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path},[147]).Changes.Count==0,version+" repeated descriptions are idempotent");
            File.WriteAllText(path,"Move,Power,InGameDescription\n147,89,Cambiada\n");
            var pending=moves.PreviewMoves(BatchAction.MoveBalance,new BatchSettings{TemplatePath=path},[147]);
            byte[] touched=File.ReadAllBytes(textPath);touched[^1]^=1;File.WriteAllBytes(textPath,touched);
            byte[] moveBefore=File.ReadAllBytes(f.MovePath);
            reject(()=>moves.ApplyMovePreview(pending),version+" external text edit rejects the whole multi-file save");
            check(File.ReadAllBytes(f.MovePath).SequenceEqual(moveBefore)&&File.ReadAllBytes(textPath).SequenceEqual(touched),version+" stale text prevents any move write");
            check(!Directory.EnumerateFiles(f.Root,"*.tmp",SearchOption.AllDirectories).Any()&&!Directory.EnumerateFiles(f.Root,"*.bak*",SearchOption.AllDirectories).Any(),version+" no temporary files or automatic backups remain");
        }
        foreach(var version in new[]{GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);
            byte[][] baseline=f.PersonalRecords;
            var proposal=f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true},[1,289]);
            var stats=new pk3DS.Core.Structures.PersonalInfo.PersonalTable(proposal.CopyRecords()[289],version).Table[0];
            check(stats.HP==130&&stats.ATK==130&&stats.DEF==100&&stats.SPA==90&&stats.SPD==65&&stats.SPE==85,version+" bundled Slaking template matches Progressive");
            check(proposal.ChangedCount==1&&proposal.CopyRecords()[1].SequenceEqual(baseline[1]),version+" Pokemon stats template respects scope and omitted entries");
            check(proposal.CopyRecords()[289].Skip(6).SequenceEqual(baseline[289].Skip(6)),version+" stats template preserves all unrelated personal bytes");
            check(f.PersonalRecords.SelectMany(r=>r).SequenceEqual(baseline.SelectMany(r=>r)),version+" Pokemon stats preview is read-only");
            f.Session.ApplyPersonalPreview(proposal);
            check(f.Session.ReadPersonal(289).Attack==130,version+" Pokemon stats apply reloads the GARC");
            check(f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true},[289]).Changes.Count==0,version+" Pokemon stats template is idempotent");
            string csv=Path.Combine(f.Root,"pokemon.csv");File.WriteAllText(csv,"Entry,Pokemon,ATK\n1,,210\n");
            var custom=f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true,TemplatePath=csv},[1]);
            check(custom.CopyRecords()[1][1]==210&&custom.CopyRecords()[1][0]==baseline[1][0],version+" Pokemon CSV blank stats preserve existing values");
            File.WriteAllText(csv,"Entry,HP\n1,0\n");
            reject(()=>f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true,TemplatePath=csv},[1]),version+" invalid base stat zero rejected");
            File.WriteAllText(csv,"Entry,ATK\n1,211\n");
            var pending=f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings{PersonalTemplate=true,TemplatePath=csv},[1]);
            byte[] original=File.ReadAllBytes(f.PersonalPath);File.AppendAllText(csv,"2,212\n");
            reject(()=>f.Session.ApplyPersonalPreview(pending),version+" Pokemon stats CSV changed after preview rejected");
            check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(original),version+" stale Pokemon CSV leaves the archive untouched");
        }
        // Fail after replacing the first file: recover all completed writes from memory.
        string work=Path.Combine(Path.GetTempPath(),"progressive-transaction-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            string first=Path.Combine(work,"one"),second=Path.Combine(work,"two");File.WriteAllBytes(first,[1,2]);File.WriteAllBytes(second,[3,4]);
            var files=new[]{new FileChange(first,[1,2],[5,6]),new FileChange(second,[3,4],[7,8])};
            reject(()=>FileTransaction.Commit(files,_=>throw new IOException("Injected IO failure")),"multi-file failure is reported");
            check(File.ReadAllBytes(first).SequenceEqual(new byte[]{1,2})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{3,4}),"multi-file failure restores already replaced files");
            reject(()=>FileTransaction.Commit(files,n=>{if(n==2)throw new IOException("Injected final readback failure");}),"failure after last replacement is reported");
            check(File.ReadAllBytes(first).SequenceEqual(new byte[]{1,2})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{3,4}),"failure after last replacement restores every file");
            check(Directory.EnumerateFiles(work).Count()==2,"rollback leaves no backup or staging files");
            FileTransaction.Commit(files);
            check(File.ReadAllBytes(first).SequenceEqual(new byte[]{5,6})&&File.ReadAllBytes(second).SequenceEqual(new byte[]{7,8}),"multi-file success reloads exact final bytes");
        }
        finally{Directory.Delete(work,true);}
    }
    private static string CreateSpanishBank(Fixture f)
    {
        var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(6);
        string path=Path.Combine(f.Game.RomFS,reference.Reference);
        var banks=f.Game.GameTextStrings.Select(lines=>TextFile.GetBytes(f.Game,lines)).ToArray();
        f.WriteGarc(path,banks,reference);return path;
    }
}
