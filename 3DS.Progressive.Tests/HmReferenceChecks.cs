using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Services;

internal static class HmReferenceChecks
{
    internal static void Run(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            string path=new[]{"ExeFS","ExtractedExeFS"}.SelectMany(folder=>new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(root,folder,n))).FirstOrDefault(File.Exists)
                ??throw new FileNotFoundException("Falta code.bin descomprimido: "+root);
            byte[] source=File.ReadAllBytes(path);var hash=SHA256.HashData(source);
            bool oras=Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) is "PokemonOR" or "PokemonAS";
            var match=Gen6HmRestriction.Locate(source,oras);
            if(match.Offset<0)throw new InvalidDataException("Firma MO no encontrada: "+root);
            byte[] copy=(byte[])source.Clone();Gen6HmRestriction.Set(copy,oras,!match.Patched);
            if(!source.Where((b,i)=>i<match.Offset||i>=match.Offset+8).SequenceEqual(copy.Where((b,i)=>i<match.Offset||i>=match.Offset+8)))throw new Exception("Cambio fuera de la función MO");
            Gen6HmRestriction.Set(copy,oras,match.Patched);
            if(!copy.SequenceEqual(source)||!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))throw new Exception("Preservación MO fallida");
            Console.WriteLine($"PASS HM READ ONLY: {root}; offset 0x{match.Offset:X}; patched={match.Patched}; toggle/restore in memory; original SHA-256 preserved.");
        }
    }
}
