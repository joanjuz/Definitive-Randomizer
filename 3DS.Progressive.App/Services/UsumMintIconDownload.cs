using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace ThreeDS.Progressive.Services;

/// <summary>Optional local acquisition. Copyrighted images are never application assets.</summary>
internal static class UsumMintIconDownload
{
    internal static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DefinitiveRandomizer","mint-icons",UsumMintIconSources.Commit);
    internal static async Task<IReadOnlyList<byte[]>> LoadAsync(string cache,HttpClient? client=null,CancellationToken cancellation=default,IReadOnlyList<UsumMintIconSource>? sources=null)
    {
        using var owned=client is null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)}:null;
        client??=owned!;
        Directory.CreateDirectory(cache);
        var textures=new List<byte[]>();
        foreach(var source in sources??UsumMintIconSources.All)
        {
            cancellation.ThrowIfCancellationRequested();
            string path=Path.Combine(cache,source.Slug+".png");
            byte[]? png=File.Exists(path)&&new FileInfo(path).Length<=262144?await File.ReadAllBytesAsync(path,cancellation):null;
            if(png is null||Convert.ToHexString(SHA256.HashData(png))!=source.PngSha256)
            {
                var uri=new Uri($"https://raw.githubusercontent.com/PokeAPI/sprites/{UsumMintIconSources.Commit}/sprites/items/{source.Slug}.png");
                using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellation);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is >262144)throw new InvalidDataException("El icono descargado excede el tamaño permitido.");
                using var stream=await response.Content.ReadAsStreamAsync(cancellation);using var buffer=new MemoryStream();
                byte[] chunk=new byte[8192];int read;
                while((read=await stream.ReadAsync(chunk,cancellation))>0)
                {
                    if(buffer.Length+read>262144)throw new InvalidDataException("El icono descargado excede el tamaño permitido.");
                    buffer.Write(chunk,0,read);
                }
                png=buffer.ToArray();
                if(Convert.ToHexString(SHA256.HashData(png))!=source.PngSha256)
                    throw new InvalidDataException("La imagen de PokéAPI no coincide con la fuente fijada: "+source.Slug+". No se modificó el juego.");
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temp,png,cancellation);File.Move(temp,path,true);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            byte[] texture=UsumMintIconGraphics.ConvertPng(png);
            if(Convert.ToHexString(SHA256.HashData(texture))!=source.TextureSha256)
                throw new InvalidDataException("La conversión no coincide con el perfil gráfico v1: "+source.Slug+". No se modificó el juego.");
            textures.Add(texture);
        }
        return textures;
    }
}
