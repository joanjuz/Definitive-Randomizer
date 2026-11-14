using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Private real-copy probe: a 3DS/CXI image previously built by this program is extracted,
/// opened, edited, rebuilt and extracted again twice. Every write goes to a new output folder; the
/// source image is hashed before and after. Each stage reports its full exception on failure.</summary>
internal static class RerandomizeCycleChecks
{
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static Dictionary<string,string> Snapshot(string root)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string dir in new[]{"RomFS","ExeFS"})
            foreach(string file in Directory.GetFiles(Path.Combine(root,dir),"*",SearchOption.AllDirectories))
                result[Path.GetRelativePath(root,file)]=Hash(file);
        return result;
    }
    internal static void Run(string output,string image,Action<bool,string> check,string? template=null)
    {
        output=Path.GetFullPath(output);image=Path.GetFullPath(image);if(template is not null)template=Path.GetFullPath(template);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("La salida debe ser una carpeta nueva.");
        Directory.CreateDirectory(output);
        string sourceHash=Hash(image);string stage="inicio";
        try
        {
            stage="extracción 1";string e1=Path.Combine(output,"E1");
            new GameRomExtractionService().ExtractAsync(image,e1).GetAwaiter().GetResult();
            stage="apertura 1";var s1=GameProjectSession.Open(e1,false);
            Console.WriteLine($"Proyecto 1: {s1.GameConfiguration.Version} · {Path.GetFileName(image)}");
            stage="edición manual de entrenador (ciclo 1)";
            var t1=new GameTrainerService(s1);int first=t1.Entries[0].Id,second=t1.Entries[1].Id,third=t1.Entries[2].Id;
            var v1=EditorValues.Flatten(t1.Read(first));int level1=v1["P0.Level"]==57?58:57;v1["P0.Level"]=level1;t1.Save(first,v1);
            check(EditorValues.Flatten(new GameTrainerService(s1).Read(first))["P0.Level"]==level1,"CycleEdit1SavedInProject");
            string mint1=MintState(s1);Console.WriteLine("Mentas (ciclo 1): "+mint1);
            var before1=Snapshot(e1);
            stage="reconstrucción 1";string r1=Path.Combine(output,"R1.3ds");
            var built1=new GameRomRebuildService(s1).BuildAsync(RomRebuildKind.Trimmed3DS,r1).GetAwaiter().GetResult();
            check(Hash(r1)==built1.Hash,"CycleRebuild1HashVerified");
            stage="extracción 2";string e2=Path.Combine(output,"E2");
            new GameRomExtractionService().ExtractAsync(r1,e2).GetAwaiter().GetResult();
            var after1=Snapshot(e2);
            check(before1.Count==after1.Count&&before1.All(p=>after1.TryGetValue(p.Key,out string? h)&&h==p.Value),"CycleExtraction2ByteExactToEditedProject");
            foreach(string header in new[]{"exheader.bin","plain.bin"})
                check(!File.Exists(Path.Combine(e1,header))||Hash(Path.Combine(e1,header))==Hash(Path.Combine(e2,header)),"CycleHeaderPreserved "+header);
            check(SameIdentity(Path.Combine(e1,"ncchheader.bin"),Path.Combine(e2,"ncchheader.bin")),"CycleNcchIdentityPreserved");
            File.Delete(r1);
            stage="apertura 2";var s2=GameProjectSession.Open(e2,false);
            check(EditorValues.Flatten(new GameTrainerService(s2).Read(first))["P0.Level"]==level1,"CycleEdit1PersistsAfterRebuildAndExtraction");
            if(template is not null)
            {
                // Randomizing an already randomized project again: the template may legitimately
                // replace the first manual edit, so that edit is only re-checked without a template.
                stage="vista previa de Global Template sobre el proyecto reextraído";
                var document=GlobalTemplateDocument.Load(template,s2.GameName,s2.Generation);
                using var preview=new GlobalTemplateApplication(s2).PreviewAsync(document).GetAwaiter().GetResult();
                Console.WriteLine($"Global Template: {preview.Template.Actions.Count} acciones · {preview.Files.Count} archivos · semilla {preview.Seed}");
                stage="aplicación de Global Template sobre el proyecto reextraído";
                new GlobalTemplateApplication(s2).Apply(preview);
                check(preview.Files.Count>0,"CycleGlobalTemplateReappliedOnExtractedProject");
                level1=EditorValues.Flatten(new GameTrainerService(s2).Read(first))["P0.Level"];
            }
            var t2=new GameTrainerService(s2);
            stage="edición manual y randomización de entrenadores (ciclo 2)";
            var v2=EditorValues.Flatten(t2.Read(second));int level2=v2["P0.Level"]==61?62:61;v2["P0.Level"]=level2;t2.Save(second,v2);
            var proposal=t2.Preview(EditorValues.Flatten(t2.RandomOptions),[third]);t2.Apply(proposal);
            var random3=EditorValues.Flatten(new GameTrainerService(s2).Read(third));
            stage="randomización de encuentros salvajes (ciclo 2)";
            var w2=new GameEncounterService(s2);int area=w2.Entries[0].Id;
            w2.Apply(w2.Preview(EditorValues.Flatten(w2.RandomOptions),[area]));
            var wild2=EditorValues.Flatten(new GameEncounterService(s2).Read(area));
            string mint2=MintState(s2);Console.WriteLine("Mentas (ciclo 2): "+mint2);
            check(mint1==mint2,"CycleMintStateStable");
            var before2=Snapshot(e2);
            stage="reconstrucción 2";string r2=Path.Combine(output,"R2.3ds");
            var built2=new GameRomRebuildService(s2).BuildAsync(RomRebuildKind.Trimmed3DS,r2).GetAwaiter().GetResult();
            check(Hash(r2)==built2.Hash,"CycleRebuild2HashVerified");
            stage="extracción 3";string e3=Path.Combine(output,"E3");
            new GameRomExtractionService().ExtractAsync(r2,e3).GetAwaiter().GetResult();
            var after2=Snapshot(e3);
            check(before2.Count==after2.Count&&before2.All(p=>after2.TryGetValue(p.Key,out string? h)&&h==p.Value),"CycleExtraction3ByteExactToEditedProject");
            stage="apertura 3";var s3=GameProjectSession.Open(e3,false);var t3=new GameTrainerService(s3);
            check(EditorValues.Flatten(t3.Read(first))["P0.Level"]==level1&&EditorValues.Flatten(t3.Read(second))["P0.Level"]==level2,"CycleBothManualEditsAccumulated");
            check(EditorValues.Flatten(t3.Read(third)).All(p=>random3[p.Key]==p.Value),"CycleRandomizedTrainerAccumulated");
            check(EditorValues.Flatten(new GameEncounterService(s3).Read(area)).All(p=>wild2[p.Key]==p.Value),"CycleRandomizedEncountersAccumulated");
            File.Delete(r2);
            if(s3.GameConfiguration.USUM)
            {
                // Case C on the disposable last copy: one byte inside the species/form lookup that
                // the mint routine calls (code.bin 0x21CC08, both profiles). The executable is no
                // longer playable; only the rejection, its isolation and the absence of writes matter.
                stage="caso C: dependencia nativa de mentas alterada";
                string code=Directory.GetFiles(s3.GameConfiguration.ExeFS).Single(p=>Path.GetFileName(p) is "code.bin" or ".code.bin");
                byte[] bytes=File.ReadAllBytes(code);bytes[0x21CC40]^=0xFF;File.WriteAllBytes(code,bytes);
                var mutated=Snapshot(e3);
                var rejected=new UsumNatureMintService(GameProjectSession.Open(e3,false)).Inspect();
                Console.WriteLine("Mentas con la rutina alterada: "+rejected.State+" · "+rejected.Report);
                check(rejected.State==pk3DS.Core.Modding.Research.UsumMintState.Incompatible&&rejected.Proposal is null&&rejected.Report.Contains("0x21CC08")&&rejected.Report.Contains("1 de 62")&&rejected.Report.Contains("no se modificó ningún archivo"),"CaseCMintDependencyRejectedPrecisely");
                var afterReject=Snapshot(e3);
                check(mutated.All(p=>afterReject[p.Key]==p.Value)&&afterReject.Count==mutated.Count,"CaseCMintRejectionWritesNothing");
                var s4=GameProjectSession.Open(e3,false);var t4=new GameTrainerService(s4);var v4=EditorValues.Flatten(t4.Read(first));v4["P0.Level"]=level1==57?58:57;t4.Save(first,v4);
                check(EditorValues.Flatten(new GameTrainerService(s4).Read(first))["P0.Level"]==v4["P0.Level"],"CaseCIndependentEditorStillSaves");
                string exefs=Path.Combine(output,"C-exefs.bin");var builtExe=new GameRomRebuildService(s4).BuildAsync(RomRebuildKind.ExeFS,exefs).GetAwaiter().GetResult();
                check(Hash(exefs)==builtExe.Hash,"CaseCRebuildStillAvailable");File.Delete(exefs);
            }
            Console.WriteLine($"Ciclos completados. Salida: {output} (E1, E2, E3).");
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(output,"cycle-failure.txt"),"Etapa: "+stage+"\n"+ex);
            Console.WriteLine("FAIL etapa «"+stage+"»: "+ex);
            check(false,"CycleCompletedWithoutException ("+stage+")");
        }
        finally{check(Hash(image)==sourceHash,"CycleSourceImageUnchanged");}
    }
    private static string MintState(GameProjectSession session)
    {
        if(!session.GameConfiguration.USUM)return "no aplica";
        var preview=new UsumNatureMintService(session).Inspect();
        return preview.State+(preview.State==pk3DS.Core.Modding.Research.UsumMintState.Incompatible?" · "+preview.Report:"");
    }
    // The rebuild regenerates the NCCH header from the exheader; the title identity must survive.
    private static bool SameIdentity(string a,string b)
    {
        byte[] x=File.ReadAllBytes(a),y=File.ReadAllBytes(b);
        return x.AsSpan(0x100,4).SequenceEqual("NCCH"u8)&&y.AsSpan(0x100,4).SequenceEqual("NCCH"u8)&&
            BitConverter.ToUInt64(x,0x108)==BitConverter.ToUInt64(y,0x108)&&BitConverter.ToUInt64(x,0x118)==BitConverter.ToUInt64(y,0x118);
    }
}
