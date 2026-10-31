using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Gen7 adjustments from the app and Global/Batch: traded Pokémon always obey, the Ultra Warp Ride hint (USUM) and no Mega form the player can obtain.</summary>
public static class Gen7Adjustments
{
    internal const string ObedienceActionId=Gen7Obedience.ActionId;
    internal const string NoMegasActionId=ArchiveProjectEditor.NoMegasActionId;
    internal const string UltraWarpHintActionId="usum.ultra-warp-hint";
    /// <summary>Parameterless actions: a recipe made from the history is complete with them (the hint only in USUM).</summary>
    internal static string[] ActionIds(bool usum)=>usum?[ObedienceActionId,UltraWarpHintActionId,NoMegasActionId]:[ObedienceActionId,NoMegasActionId];

    private static string CodePath(GameProjectSession project)=>new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(project.GameConfiguration.ExeFS,n)).FirstOrDefault(File.Exists)
        ??throw new FileNotFoundException("Falta code.bin en el ExeFS del proyecto.");

    /// <summary>Null when the table is already patched.</summary>
    internal static FileChange? PreviewObedience(GameProjectSession project)
    {
        if(project.Generation!=7)throw new NotSupportedException("La obediencia de los intercambios solo está disponible en Sol, Luna, Ultra Sol y Ultra Luna.");
        string path=CodePath(project);byte[] original=File.ReadAllBytes(path),output=(byte[])original.Clone();
        return Gen7Obedience.Apply(output,project.GameConfiguration.USUM)?new FileChange(path,original,output):null;
    }
    internal static void ApplyObedience(GameProjectSession project,FileChange file)
    {
        GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        project.History.Commit([file],"Obediencia","Los Pokémon intercambiados siempre obedecen",
            [new BatchChange(0,"Obediencia","Nivel máximo que obedece un Pokémon de otro entrenador","20 / 35 / 50 / 65 / 80 / 100 según las grandes pruebas","100 siempre")],
            new Dictionary<string,string>{["ActionId"]=ObedienceActionId,["Engine"]="pk3DS Progressive · Gen7Obedience"});
    }

    /// <summary>One proposal per editor that has a Mega form: wild (with SOS), static, gifts and trades, evolutions.</summary>
    internal static IReadOnlyList<(ArchiveProjectEditor Editor,EditorProposal Proposal)> PreviewNoMegas(GameProjectSession project)
    {
        if(project.Generation!=7)throw new NotSupportedException("Quitar las formas Mega obtenibles solo está disponible en Gen7.");
        ArchiveProjectEditor[] editors=[new GameEncounterService(project),new GameStaticEncounterService(project),new GameEvolutionService(project)];
        return editors.Select(e=>(Editor:e,Proposal:e.PreviewWithoutMegas())).Where(p=>p.Proposal.Changes.Count>0).ToArray();
    }
    internal static void ApplyNoMegas(IReadOnlyList<(ArchiveProjectEditor Editor,EditorProposal Proposal)> proposals)
    {
        foreach(var (editor,proposal) in proposals)editor.Apply(proposal);
    }

    // Story text 366: the Ultra Recon Squad lends Lunala (line 6) or Solgaleo (line 16) after Necrozma. One page is added before the closing command.
    private const int UltraWarpFile=366;
    private static readonly (int Language,int Line,string Expected,string Hint)[] UltraWarpHints=
    [
        (6,6,"Ultraviaje dimensional","\\c\\nPor cierto, en las oficinas de GAME FREAK de\\nCiudad Kantai te enseñarán otra forma de\\r\\nguiar a Lunala por el Ultraespacio."),
        (6,16,"Ultraviaje dimensional","\\c\\nPor cierto, en las oficinas de GAME FREAK de\\nCiudad Kantai te enseñarán otra forma de\\r\\nguiar a Solgaleo por el Ultraespacio."),
        (2,6,"Ultra Warp Ride","\\c\\nBy the way, the GAME FREAK office in Heahea\\nCity can show you another way to steer\\r\\nLunala through Ultra Space."),
        (2,16,"Ultra Warp Ride","\\c\\nBy the way, the GAME FREAK office in Heahea\\nCity can show you another way to steer\\r\\nSolgaleo through Ultra Space."),
    ];

    /// <summary>Spanish and English story text: after Necrozma, the offer to explore Ultra Space also says that GAME FREAK (Heahea City)
    /// teaches another way to steer. Lines that already mention GAME FREAK are left as they are.</summary>
    internal static (IReadOnlyList<FileChange> Files,IReadOnlyList<BatchChange> Changes) PreviewUltraWarpHint(GameProjectSession project)
    {
        var config=project.GameConfiguration;
        if(!config.USUM)throw new NotSupportedException("La pista del Ultraviaje solo existe en Ultra Sol y Ultra Luna.");
        var reference=config.GetGARCReference("storytext");var files=new List<FileChange>();var changes=new List<BatchChange>();
        foreach(var language in UltraWarpHints.GroupBy(h=>h.Language))
        {
            var relative=reference.GetRelativeGARC(language.Key);string path=Path.Combine(config.RomFS,relative.Reference);
            byte[] source=File.ReadAllBytes(path);var records=new GARC.MemGARC(source).Files;
            if(records.Length<=UltraWarpFile)throw new InvalidDataException("Falta el archivo de texto del Ultraviaje en el idioma "+language.Key+".");
            string[] lines=new TextFile(config,records[UltraWarpFile],config.RemapCharacters).Lines;
            int before=changes.Count;
            foreach(var hint in language)
            {
                string line=hint.Line<lines.Length?lines[hint.Line]:"";
                if(!line.Contains(hint.Expected,StringComparison.Ordinal))throw new InvalidDataException($"El texto del Ultraviaje (idioma {language.Key}, línea {hint.Line}) no es el esperado; no se aplicó nada.");
                if(line.Contains("GAME FREAK",StringComparison.Ordinal))continue;
                // Before the closing wait command, so the new page is shown before the box closes.
                int close=line.LastIndexOf("[VAR 0114(",StringComparison.Ordinal);if(close<0)close=line.Length;
                string after=line.Insert(close,hint.Hint);
                changes.Add(new BatchChange(UltraWarpFile,$"Archivo {UltraWarpFile:000} · idioma {language.Key}","Línea "+hint.Line,line,after));
                lines[hint.Line]=after;
            }
            if(changes.Count==before)continue;
            byte[] encoded=TextFile.GetBytes(config,lines,config.RemapCharacters);
            if(!new TextFile(config,encoded,config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("El texto del Ultraviaje no se pudo codificar sin pérdidas.");
            records[UltraWarpFile]=encoded;
            files.Add(new FileChange(path,source,GarcTransaction.Build(source,relative,records,preserveRecordSize:false)));
        }
        return(files,changes);
    }
    internal static void ApplyUltraWarpHint(GameProjectSession project,IReadOnlyList<FileChange> files,IReadOnlyList<BatchChange> changes)
    {
        foreach(var file in files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        project.History.Commit(files,"Historia y textos","Pista de GAME FREAK en el Ultraviaje",changes,
            new Dictionary<string,string>{["ActionId"]=UltraWarpHintActionId,["Bank"]="storytext",["Languages"]="2,6"});
    }

    /// <summary>Global/Batch entry point (called by the LegacyBatch backend after every other action and the manual edits).</summary>
    public static string Execute(string root,string actionId)
    {
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        switch(actionId)
        {
            case ObedienceActionId:
                var file=PreviewObedience(project);
                if(file is null)return "Traded obedience was already enabled.";
                ApplyObedience(project,file);return "Traded Pokémon always obey.";
            case UltraWarpHintActionId:
                var hint=PreviewUltraWarpHint(project);
                if(hint.Changes.Count==0)return "The Ultra Warp Ride hint was already in the text.";
                ApplyUltraWarpHint(project,hint.Files,hint.Changes);return "Ultra Warp Ride hint added (Spanish and English).";
            case NoMegasActionId:
                var proposals=PreviewNoMegas(project);ApplyNoMegas(proposals);
                return $"Obtainable Mega forms removed ({proposals.Sum(p=>p.Proposal.Changes.Count)} change(s)).";
            default:
                throw new ArgumentException("Acción Gen7 desconocida: "+actionId,nameof(actionId));
        }
    }
}
