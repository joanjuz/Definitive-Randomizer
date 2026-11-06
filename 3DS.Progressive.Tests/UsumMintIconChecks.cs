using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

internal static partial class UsumMintIconChecks
{
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    private static byte[] Png(int index=0)
    {
        using var bitmap=new Bitmap(24,24);
        for(int y=0;y<24;y++)for(int x=0;x<24;x++)
            bitmap.SetPixel(x,y,Color.FromArgb(x==12&&y==12?64:255,x<12?255:0,y<12?255:0,(index*11)%256));
        using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
    }
    private sealed class FakeSource(Dictionary<string,byte[]> images):HttpMessageHandler
    {
        internal int Calls;
        internal bool Corrupt;
        internal HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();Calls++;
            byte[] bytes=images[Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath)];
            return Task.FromResult(new HttpResponseMessage(Status){Content=new ByteArrayContent(Corrupt?[1,2,3]:bytes)});
        }
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        byte[][] pngs=Enumerable.Range(0,21).Select(Png).ToArray();
        byte[][] textures=pngs.Select(UsumMintIconGraphics.ConvertPng).ToArray();
        var sources=UsumMintIconSources.All.Select((source,i)=>source with{PngSha256=Hash(pngs[i]),TextureSha256=Hash(textures[i])}).ToArray();
        int[] expected=[113,114,115,123,124,125,126,127,128,129,130,131,132,133,426,427,622,837,838,839,840];
        check(sources.Select(s=>s.ItemId).SequenceEqual(expected)&&sources.Select(s=>s.Slug).Distinct().Count()==21,"MintIconsCatalog21StableMappings");
        for(int i=0;i<textures.Length;i++)check(UsumMintIconGraphics.Decompress(UsumMintIconGraphics.Compress(textures[i])).SequenceEqual(textures[i]),"MintIconsCompressionRoundtrip"+i);
        using(var decoded=GameGraphicsCodec.Decode(textures[0]))
        {
            check(decoded.Width==32&&decoded.Height==32&&decoded.GetPixel(0,0).A==0&&decoded.GetPixel(16,16).A==0,"MintIconsAlphaAndCanvas");
            check(decoded.GetPixel(4,4).ToArgb()==Color.Yellow.ToArgb()&&decoded.GetPixel(27,4).ToArgb()==Color.Lime.ToArgb()&&
                decoded.GetPixel(4,27).ToArgb()==Color.Red.ToArgb()&&decoded.GetPixel(27,27).ToArgb()==Color.Black.ToArgb(),"MintIconsOrientationIndependentQuadrants");
        }
        reject(()=>UsumMintIconGraphics.ConvertPng([1,2,3]),"MintIconsRejectNonPng");
        byte[] large=Png();System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(large.AsSpan(16),513);
        reject(()=>UsumMintIconGraphics.ConvertPng(large),"MintIconsRejectOversizeDimensions");
        using(var blank=new Bitmap(8,8))using(var stream=new MemoryStream())
        {blank.Save(stream,ImageFormat.Png);reject(()=>UsumMintIconGraphics.ConvertPng(stream.ToArray()),"MintIconsRejectInvisibleImage");}
        using(var sm=new Fixture(GameVersion.SM))
            check(new UsumMintIconService(sm.Session).Inspect().State==UsumMintState.Incompatible,"MintIconsRejectSm");
        DownloadChecks(check,reject,pngs,sources);
        foreach(bool moon in new[]{false,true})Lifecycle(check,reject,moon,textures,sources);
    }
    private static void DownloadChecks(Action<bool,string> check,Action<Action,string> reject,byte[][] pngs,UsumMintIconSource[] sources)
    {
        using var f=new Fixture(GameVersion.USUM);string cache=Path.Combine(f.Root,"cache");
        using var handler=new FakeSource(sources.Select((source,i)=>(source.Slug,Bytes:pngs[i])).ToDictionary(p=>p.Slug,p=>p.Bytes));
        using var client=new HttpClient(handler);
        var downloaded=UsumMintIconDownload.LoadAsync(cache,client,sources:sources).GetAwaiter().GetResult();
        check(handler.Calls==21&&downloaded.Select(Hash).SequenceEqual(sources.Select(s=>s.TextureSha256)),"MintIconsDownloadAll21Automatically");
        UsumMintIconDownload.LoadAsync(cache,client,sources:sources).GetAwaiter().GetResult();
        check(handler.Calls==21,"MintIconsOfflineVerifiedCache");
        File.WriteAllBytes(Path.Combine(cache,sources[0].Slug+".png"),[9,8,7]);
        UsumMintIconDownload.LoadAsync(cache,client,sources:sources).GetAwaiter().GetResult();
        check(handler.Calls==22,"MintIconsCorruptCacheReacquired");
        var wrongConversion=(UsumMintIconSource[])sources.Clone();wrongConversion[0]=wrongConversion[0] with{TextureSha256=new string('0',64)};
        reject(()=>UsumMintIconDownload.LoadAsync(cache,client,sources:wrongConversion).GetAwaiter().GetResult(),"MintIconsRejectUnexpectedConversion");
        handler.Corrupt=true;
        reject(()=>UsumMintIconDownload.LoadAsync(Path.Combine(f.Root,"bad-cache"),client,sources:sources).GetAwaiter().GetResult(),"MintIconsRejectRemoteHashMismatch");
        check(!Directory.GetFiles(Path.Combine(f.Root,"bad-cache")).Any(),"MintIconsBadSourceNeverCached");
        handler.Corrupt=false;handler.Status=HttpStatusCode.NotFound;
        try{UsumMintIconDownload.LoadAsync(Path.Combine(f.Root,"missing-cache"),client,sources:sources).GetAwaiter().GetResult();check(false,"MintIconsHttpErrorMustFail");}
        catch(HttpRequestException){check(true,"MintIconsHttpErrorDoesNotInstall");}
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{UsumMintIconDownload.LoadAsync(cache,client,cancelled.Token,sources).GetAwaiter().GetResult();check(false,"MintIconsCancellationMustFail");}
        catch(OperationCanceledException){check(true,"MintIconsCancellationDoesNotInstall");}
    }
}
