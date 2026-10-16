using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThreeDS.Progressive.Services;

/// <summary>Optional remote images. No bundled sprites or persistent image cache.</summary>
public sealed class SpriteService
{
    private static readonly HttpClient Client=new(){Timeout=TimeSpan.FromSeconds(4)};
    private static readonly State Shared=new();
    private sealed class State
    {
        internal readonly ConcurrentDictionary<int,BitmapSource> Images=new();
        internal readonly ConcurrentDictionary<int,Lazy<Task<SpriteResult>>> Pending=new();
        internal readonly SemaphoreSlim Downloads=new(4);
        internal long RetryAfter;
    }
    private readonly HttpClient _client;
    private readonly State _state;
    private readonly TimeProvider _time;
    public SpriteService(){_client=Client;_state=Shared;_time=TimeProvider.System;}
    internal SpriteService(HttpClient client,TimeProvider? time=null){_client=client;_state=new();_time=time??TimeProvider.System;}
    public Task<SpriteResult> GetSpriteAsync(int personalEntry,int speciesId,CancellationToken cancellationToken=default)
        =>GetSpriteAsync(personalEntry,speciesId,0,cancellationToken);
    public async Task<SpriteResult> GetSpriteAsync(int personalEntry,int speciesId,int form,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(speciesId is <1 or >1025)return new(null,"");
        // Like the tracker, remote pictures use National Dex species IDs.
        if(_state.Images.TryGetValue(speciesId,out var image))return new(image,"PokéAPI");
        if(_time.GetUtcNow().UtcTicks<Interlocked.Read(ref _state.RetryAfter))return new(null,"");
        var request=_state.Pending.GetOrAdd(speciesId,id=>new Lazy<Task<SpriteResult>>(()=>FetchTracked(id)));
        return await request.Value.WaitAsync(cancellationToken);
    }
    private async Task<SpriteResult> FetchTracked(int speciesId)
    {
        try{return await Download(speciesId).ConfigureAwait(false);}
        finally{_state.Pending.TryRemove(speciesId,out _);}
    }
    private async Task<SpriteResult> Download(int speciesId)
    {
        await _state.Downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            if(_time.GetUtcNow().UtcTicks<Interlocked.Read(ref _state.RetryAfter))return new(null,"");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response=await _client.GetAsync($"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{speciesId}.png",HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength is >2_000_000)return new(null,"");
            using var input=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes=new MemoryStream();
            byte[] buffer=new byte[16384];int count;
            while((count=await input.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0)
            {
                if(bytes.Length+count>2_000_000)return new(null,"");
                bytes.Write(buffer,0,count);
            }
            byte[] data=bytes.ToArray();
            if(data.Length<8||!data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return new(null,"");
            using var source=new MemoryStream(data,false);
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=source;bitmap.EndInit();bitmap.Freeze();
            _state.Images[speciesId]=bitmap;
            return new(bitmap,"PokéAPI");
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException)
        {
            Interlocked.Exchange(ref _state.RetryAfter,_time.GetUtcNow().AddSeconds(20).UtcTicks);
            return new(null,"");
        }
        catch(Exception){return new(null,"");}
        finally{_state.Downloads.Release();}
    }
}
public sealed record SpriteResult(ImageSource? Image,string Description);
