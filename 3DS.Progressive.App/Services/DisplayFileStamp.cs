using System.IO;
namespace ThreeDS.Progressive.Services;

// Cheap display invalidation only. Mutation paths MUST still verify SHA-256.
internal readonly record struct DisplayFileStamp(long Length,DateTime LastWrite)
{
    internal static DisplayFileStamp Capture(string path)
    {
        var file=new FileInfo(path);
        if(!file.Exists)throw new FileNotFoundException("El archivo del proyecto ya no existe.",path);
        return new(file.Length,file.LastWriteTimeUtc);
    }
    internal void EnsureCurrent(string path)
    {
        if(this!=Capture(path))throw new IOException("El archivo cambió desde que abriste el proyecto. Reabrí el proyecto.");
    }
}
