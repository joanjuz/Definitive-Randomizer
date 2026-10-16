using System.IO;
using System.Net;
using System.Net.Http;
using ThreeDS.Progressive.Services;

internal static class RemoteSpriteChecks
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>run(request,token);}
    private sealed class Clock:TimeProvider
    {internal DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    internal static async Task Run(Action<bool,string> check)
    {
        var legacy=System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(BatchRomExecution.RunnerPath,".dll"));
        var resources=new System.Resources.ResourceManager("pk3DS.WinForms.Properties.Resources",legacy);
        var placeholder=(System.Drawing.Bitmap)resources.GetObject("_25")!;
        check(placeholder.Width==64&&placeholder.Height==64&&placeholder.GetPixel(32,32).A==0,"legacy embedded Pokemon resource is a generic transparent placeholder");
        check(!Directory.Exists(Path.Combine(AppContext.BaseDirectory,"Sprites")),"application output does not bundle a Sprites directory");
        byte[] png;using(var bitmap=new System.Drawing.Bitmap(2,2))using(var stream=new MemoryStream()){bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);png=stream.ToArray();}
        HttpResponseMessage Image()=>new(HttpStatusCode.OK){Content=new ByteArrayContent(png)};
        int requests=0;using var client=new HttpClient(new Handler((request,token)=>{requests++;check(request.RequestUri!.AbsoluteUri=="https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/25.png","species image uses same remote source as tracker");return Task.FromResult(Image());}));
        var sprites=new SpriteService(client);var loaded=await sprites.GetSpriteAsync(25,25);check(loaded.Image is not null&&loaded.Image.IsFrozen,"remote PNG decodes to thread-safe optional image");
        var again=await sprites.GetSpriteAsync(25,25);check(ReferenceEquals(again.Image,loaded.Image)&&requests==1,"same session reuses image in memory without another download");
        check((await sprites.GetSpriteAsync(0,0)).Image is null&&requests==1,"empty species makes no HTTP request");
        using var missingClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));var missing=await new SpriteService(missingClient).GetSpriteAsync(1,1);check(missing.Image is null&&missing.Description=="","404 leaves no image or error message");
        int offlineCalls=0;bool online=false;var time=new Clock();using var offlineClient=new HttpClient(new Handler((_,_)=>{offlineCalls++;return online?Task.FromResult(Image()):Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));}));var offline=new SpriteService(offlineClient,time);
        check((await offline.GetSpriteAsync(1,1)).Image is null,"offline request does not break editor");check((await offline.GetSpriteAsync(2,2)).Image is null&&offlineCalls==1,"offline cooldown prevents repeated team/table connection attempts");online=true;time.Now=time.Now.AddSeconds(21);check((await offline.GetSpriteAsync(1,1)).Image is not null,"network images retry after reconnect cooldown");
        using var badClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[]{1,2,3})})));check((await new SpriteService(badClient).GetSpriteAsync(1,1)).Image is null,"non-PNG response is silently ignored");
        using var largeClient=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[2_000_001])})));check((await new SpriteService(largeClient).GetSpriteAsync(1,1)).Image is null,"oversized response is rejected");
        int simultaneous=0;var gate=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);using var pendingClient=new HttpClient(new Handler((_,_)=>{simultaneous++;return gate.Task;}));var pending=new SpriteService(pendingClient);using var cancel=new CancellationTokenSource();var first=pending.GetSpriteAsync(1,1,cancel.Token);var second=pending.GetSpriteAsync(1,1);cancel.Cancel();bool canceled=false;try{await first;}catch(OperationCanceledException){canceled=true;}gate.SetResult(Image());check((await second).Image is not null&&simultaneous==1&&canceled,"selection cancellation does not cancel a shared request needed by another card");
    }
}
