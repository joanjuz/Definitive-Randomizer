using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

/// <summary>Format 2 Progressive patches. Evaluates frozen bytes without touching ROM files.</summary>
internal static class CatchZonePatchEngine
{
    internal sealed record Result(string Scope, string Path, byte[]? Bytes, bool AlreadyApplied);
    internal static CatchZoneManifest Load(byte[] json, string family)
    {
        if (family is not ("XY" or "ORAS")) throw new InvalidDataException("Catch Zones requiere XY u ORAS.");
        CatchZoneManifest manifest;
        try { manifest = JsonSerializer.Deserialize<CatchZoneManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Manifiesto Catch Zones vacío."); }
        catch (JsonException ex) { throw new InvalidDataException("Manifiesto Catch Zones inválido.", ex); }
        if (manifest.Format != 2 || !string.Equals(manifest.Game, family, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Formato o familia de Catch Zones incompatible.");
        if (manifest.Files is null || manifest.Files.Count == 0) throw new InvalidDataException("Manifiesto sin archivos.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files!)
        {
            if (file is null || file.Scope is null || file.Path is null || file.Operation is null || file.PatchMode is null)
                throw new InvalidDataException("Entrada Catch Zones incompleta.");
            if (!new[] { "encdata", "mapGR", "raw" }.Contains(file.Scope, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Ámbito Catch Zones desconocido.");
            ValidatePath(file.Path);
            if (!paths.Add(file.Scope + "/" + file.Path.Replace('\\', '/'))) throw new InvalidDataException("Archivo Catch Zones repetido.");
            if (!new[] { "patch", "delete" }.Contains(file.Operation, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Operación Catch Zones desconocida.");
            ValidateHash(file.SourceSha256);
            if (file.Length < 0 || file.Length > int.MaxValue) throw new InvalidDataException("Longitud origen inválida.");
            if (file.Operation.Equals("delete", StringComparison.OrdinalIgnoreCase)) continue;
            ValidateHash(file.TargetSha256);
            if (file.TargetLength < 0 || file.TargetLength > int.MaxValue) throw new InvalidDataException("Longitud destino inválida.");
            if (!new[] { "hunks", "delta" }.Contains(file.PatchMode, StringComparer.OrdinalIgnoreCase) || file.Hunks is null || file.Delta is null)
                throw new InvalidDataException("Modo Catch Zones inválido.");
            if(file.SourceVariants is null)throw new InvalidDataException("Variantes Catch Zones incompletas.");
            var origins=new HashSet<string>(StringComparer.OrdinalIgnoreCase){file.SourceSha256};
            foreach(var variant in file.SourceVariants)
            {
                if(variant is null||string.IsNullOrWhiteSpace(variant.Label))throw new InvalidDataException("Variante Catch Zones sin identificar.");
                ValidateHash(variant.SourceSha256);ValidateHash(variant.TargetSha256);
                if(!origins.Add(variant.SourceSha256))throw new InvalidDataException("Origen Catch Zones repetido o ambiguo.");
                if(variant.Delta is not null&&!file.PatchMode.Equals("delta",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("La migración de una variante requiere modo delta.");
            }
        }
        return manifest;
    }

    internal static IReadOnlyList<Result> Evaluate(byte[] json, string family, Func<string, string, byte[]?> read)
    {
        var manifest = Load(json, family);
        var results = new List<Result>();
        foreach (var file in manifest.Files!)
        {
            byte[]? source = read(file.Scope, file.Path);
            bool delete = file.Operation.Equals("delete", StringComparison.OrdinalIgnoreCase);
            if (source is null)
            {
                if (!delete) throw new InvalidDataException("Falta el archivo limpio: " + file.Scope + "/" + file.Path);
                results.Add(new(file.Scope, file.Path, null, true)); continue;
            }
            string sourceHash=Hash(source);
            if (!delete && MatchesTarget(file,source.LongLength,sourceHash))
            { results.Add(new(file.Scope, file.Path, (byte[])source.Clone(), true)); continue; }
            if (!MatchesSource(file,source.LongLength,sourceHash))
                throw new InvalidDataException("El archivo no coincide con el origen limpio compatible: " + file.Scope + "/" + file.Path);
            if (delete) { results.Add(new(file.Scope, file.Path, null, false)); continue; }
            var variant=sourceHash.Equals(file.SourceSha256,StringComparison.OrdinalIgnoreCase)?null:
                file.SourceVariants.Single(v=>v.SourceSha256.Equals(sourceHash,StringComparison.OrdinalIgnoreCase));
            byte[] output;
            if (file.PatchMode.Equals("delta", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new MemoryStream();
                foreach (var op in variant?.Delta??file.Delta!)
                {
                    if (op is null || op.Length < 0 || op.CopyOffset < -1 || stream.Length + op.Length > file.TargetLength)
                        throw new InvalidDataException("Operación delta fuera de límites.");
                    if (op.CopyOffset >= 0)
                    {
                        if (op.CopyOffset > source.Length - op.Length) throw new InvalidDataException("Copia delta fuera del origen.");
                        stream.Write(source, op.CopyOffset, op.Length);
                    }
                    else
                    {
                        byte[] literal = Decode(op.Data);
                        if (literal.Length != op.Length) throw new InvalidDataException("Longitud literal delta incompatible.");
                        stream.Write(literal);
                    }
                }
                output = stream.ToArray();
            }
            else
            {
                output = (byte[])source.Clone();
                if (file.Hunks!.Any(h => h is null)) throw new InvalidDataException("Fragmento Catch Zones nulo.");
                int end = 0;
                foreach (var hunk in file.Hunks!.OrderBy(h => h.Offset))
                {
                    byte[] bytes = Decode(hunk.Data);
                    if (hunk.Offset < end || hunk.Offset < 0 || hunk.Offset > output.Length - bytes.Length)
                        throw new InvalidDataException("Fragmentos Catch Zones solapados o fuera de límites.");
                    bytes.CopyTo(output, hunk.Offset); end = hunk.Offset + bytes.Length;
                }
            }
            string targetHash=variant?.TargetSha256??file.TargetSha256;
            if (output.LongLength != file.TargetLength || !Hash(output).Equals(targetHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El resultado Catch Zones no supera la verificación SHA-256.");
            results.Add(new(file.Scope, file.Path, output, false));
        }
        return results;
    }
    private static byte[] Decode(string? value)
    {
        try { return Convert.FromBase64String(value ?? throw new InvalidDataException("Faltan bytes del parche.")); }
        catch (FormatException ex) { throw new InvalidDataException("Bytes Catch Zones inválidos.", ex); }
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static bool MatchesSource(CatchZoneFile file,long length,string hash)=>length==file.Length&&
        (hash.Equals(file.SourceSha256,StringComparison.OrdinalIgnoreCase)||file.SourceVariants.Any(v=>hash.Equals(v.SourceSha256,StringComparison.OrdinalIgnoreCase)));
    internal static bool MatchesTarget(CatchZoneFile file,long length,string hash)=>length==file.TargetLength&&
        (hash.Equals(file.TargetSha256,StringComparison.OrdinalIgnoreCase)||file.SourceVariants.Any(v=>hash.Equals(v.TargetSha256,StringComparison.OrdinalIgnoreCase)));
    private static void ValidateHash(string? value)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("SHA-256 Catch Zones inválido.");
    }
    private static void ValidatePath(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.Any(c => c < 32 || ":*?\"<>|".Contains(c))))
            throw new InvalidDataException("Ruta Catch Zones insegura.");
    }
}

internal sealed class CatchZoneManifest
{
    public int Format { get; set; }
    public string? Game { get; set; }
    public List<CatchZoneFile>? Files { get; set; } = [];
}
internal sealed class CatchZoneFile
{
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "";
    public string Operation { get; set; } = "patch";
    public string PatchMode { get; set; } = "hunks";
    public long Length { get; set; } = -1;
    public long TargetLength { get; set; } = -1;
    public string SourceSha256 { get; set; } = "";
    public string TargetSha256 { get; set; } = "";
    public List<CatchZoneHunk>? Hunks { get; set; } = [];
    public List<CatchZoneDelta>? Delta { get; set; } = [];
    public List<CatchZoneSourceVariant> SourceVariants { get; set; } = [];
}
internal sealed class CatchZoneSourceVariant
{
    public string Label {get;set;}="";
    public string SourceSha256 {get;set;}="";
    public string TargetSha256 {get;set;}="";
    public List<CatchZoneDelta>? Delta {get;set;}
}
internal sealed class CatchZoneHunk
{
    public int Offset { get; set; }
    public string Data { get; set; } = "";
}
internal sealed class CatchZoneDelta
{
    public int CopyOffset { get; set; } = -1;
    public int Length { get; set; }
    public string Data { get; set; } = "";
}
