using System.IO;
using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed record SwordSurpriseOperation(string Algorithm,uint Seed,IReadOnlyList<int> Pool,string SourceMainSha256,string MainSha256,string ModuleSha256,string InputRevision,string ResultRevision);
internal sealed record SwordSurprisePreview(Guid Owner,string Revision,SwordSurpriseMod Result);
internal sealed partial class SwordShieldProject
{
    private readonly Dictionary<string,byte[]> _surpriseDraft=new(StringComparer.Ordinal);
    private readonly List<SwordSurpriseOperation> _surpriseHistory=new();
    private bool _surpriseSourceChecked;
    internal bool SurpriseEnabled=>_surpriseDraft.Count!=0;
    internal IReadOnlyList<SwordSurpriseOperation> SurpriseHistory=>_surpriseHistory.AsReadOnly();
    internal static byte[] LoadSurpriseRuntime()
    {
        using var stream=typeof(SwordShieldProject).Assembly.GetManifestResourceStream("ThreeDS.Progressive.sword-surprise.nso")??throw new FileNotFoundException("Falta el módulo propio de combate sorpresa.");
        using var data=new MemoryStream();stream.CopyTo(data);return data.ToArray();
    }
    internal SwordSurprisePreview PreviewSurprise(uint seed)
    {
        RequireEditable();VerifySource();
        if(PokeFeurEnabled)throw new InvalidOperationException("PokeFeur ya está activo en esta sesión; no se mezclan runtimes ExeFS.");
        if(Identity.TitleId!="0100ABF008968000")throw new NotSupportedException("El combate sorpresa se está integrando únicamente en Espada.");
        VerifyNoSubsdk9();_surpriseSourceChecked=true;
        byte[] main=File.ReadAllBytes(Path.Combine(Root,"exefs/main"));
        var result=SwordSurpriseMod.Build(main,LoadSurpriseRuntime(),_draft,seed);VerifySource();
        return new(_id,RevisionHash,result);
    }
    internal void ApplySurprise(SwordSurprisePreview preview)
    {
        RequireEditable();VerifySource();
        if(preview.Owner!=_id||preview.Revision!=RevisionHash)throw new InvalidOperationException("Vista previa desactualizada o de otro proyecto.");
        string before=RevisionHash;_surpriseDraft["exefs/main"]=preview.Result.Main();_surpriseDraft["exefs/subsdk9"]=preview.Result.Module();
        _surpriseHistory.Add(new(SwordSurpriseMod.Algorithm,preview.Result.Seed,preview.Result.Pool,preview.Result.SourceMainSha256,Hash(_surpriseDraft["exefs/main"]),Hash(_surpriseDraft["exefs/subsdk9"]),before,RevisionHash));
    }
    private void VerifyNoSubsdk9()
    {
        string path=Path.Combine(Root,"exefs/subsdk9");CheckNoLinks(path);
        if(File.Exists(path)||Directory.Exists(path))throw new IOException("La extracción ya contiene subsdk9. No se sustituye otro módulo nativo.");
    }
    private void ExportSurprise(string stage,SwitchModLayout layout)
    {
        if(!SurpriseEnabled)return;
        if(layout!=SwitchModLayout.Ryujinx)throw new NotSupportedException("El prototipo de combate sorpresa se exporta para Ryujinx; consola pendiente de validación.");
        foreach(var pair in _surpriseDraft)
        {
            string path=Path.Combine(stage,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,pair.Value);
            if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(pair.Value))throw new IOException("Falló la reapertura de "+pair.Key);
        }
    }
}
