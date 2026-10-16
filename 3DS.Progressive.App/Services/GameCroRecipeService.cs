using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services;

/// <summary>Original pk3DS Progressive CRO recipe engine, with frozen preview
/// and verified history transaction rather than a WinForms dialog/write.</summary>
internal sealed record CroRecipePreview(string BattlePath,byte[] Original,byte[] Proposed,
    string[] RecipePaths,string Summary,string OriginalSha256);
internal sealed class GameCroRecipeService(GameProjectSession project)
{
    internal string? BattlePath
    {
        get
        {
            if(!project.GameConfiguration.USUM)return null;
            string rom=project.GameConfiguration.RomFS;
            if(!Directory.Exists(rom))return null;
            var direct=new[]{Path.Combine(rom,"Battle.cro"),Path.Combine(rom,"dll","Battle.cro")};
            return direct.FirstOrDefault(File.Exists) ??
                Directory.EnumerateFiles(rom,"Battle.cro",SearchOption.AllDirectories).FirstOrDefault();
        }
    }
    internal CroRecipePreview Prepare(IEnumerable<string> paths)
    {
        if(!project.GameConfiguration.USUM)
            throw new NotSupportedException("CRO Recipes está disponible únicamente para USUM.");
        string battle=BattlePath??throw new FileNotFoundException("Battle.cro no existe en RomFS.");
        string[] selected=paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        if(selected.Length==0)throw new InvalidDataException("Seleccioná uno o más JSON de recetas.");
        string[] docs=selected.Select(File.ReadAllText).ToArray();
        byte[] original=File.ReadAllBytes(battle);
        if(!CroMechanicRecipeApplication.TryApplyJson(original,docs,out byte[] updated,
            out CroMechanicRecipeApplicationReport report,out string error))
            throw new InvalidDataException("Recetas CRO incompatibles: "+error);
        if(updated.Length==0)throw new InvalidDataException("El resultado CRO está vacío.");
        string summary=$"Motor: {report.Compatibility.EngineVersion} · Recetas: "+
            $"{report.Compatibility.DocumentCount} · Dependencias: {report.Compatibility.DependencyCount}"+
            $" · Entradas: {report.BinaryRecipe.EntryCount} · Relocaciones: "+
            $"{report.BinaryRecipe.OriginalPatchCount} → {report.BinaryRecipe.FinalPatchCount}"+
            $" · Tamaño: {report.BinaryRecipe.OriginalFileSize} → {report.BinaryRecipe.FinalFileSize} bytes"+
            Environment.NewLine+string.Join(Environment.NewLine,report.Documents.Select(d=>
                $"• {(string.IsNullOrWhiteSpace(d.RecipeId)?d.Name:d.RecipeId)}: {d.EntryCount} entradas"));
        return new(battle,original,updated,selected,summary,ProjectHistory.Hash(original));
    }
    internal void Apply(CroRecipePreview preview)
    {
        if(!StringComparer.OrdinalIgnoreCase.Equals(BattlePath,preview.BattlePath))
            throw new InvalidOperationException("Battle.cro cambió de ubicación desde la vista previa.");
        if(!SHA256.HashData(File.ReadAllBytes(preview.BattlePath)).AsSpan()
            .SequenceEqual(SHA256.HashData(preview.Original)))
            throw new IOException("Battle.cro cambió desde la vista previa; no se sobrescribió.");
        project.History.Commit([new FileChange(preview.BattlePath,preview.Original,preview.Proposed)],
            "Mecánicas/CRO","Aplicar recetas CRO",[],
            new Dictionary<string,string>{
                ["Recipes"]=string.Join(" | ",preview.RecipePaths.Select(Path.GetFileName)),
                ["InputSHA256"]=preview.OriginalSha256,
                ["RecipeCount"]=preview.RecipePaths.Length.ToString(),
                ["OutputSHA256"]=ProjectHistory.Hash(preview.Proposed)
            });
    }
}
