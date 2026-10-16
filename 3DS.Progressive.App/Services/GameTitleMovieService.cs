using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record TitleMovieFile(string Path,string Label);
internal sealed record MovieTrack(int Index,string Kind,string Codec,int Width,int Height,string Rate,long Packets,int Channels,int SampleRate,int Layout);
internal sealed record MovieProfile(IReadOnlyList<MovieTrack> Tracks)
{
    internal MovieTrack[] Videos=>Tracks.Where(t=>t.Kind=="video").ToArray();
    internal MovieTrack[] Audio=>Tracks.Where(t=>t.Kind=="audio").ToArray();
}
internal sealed class GameTitleMovieService : IDisposable
{
    private readonly GameProjectSession _project;
    private readonly string _tools;
    private readonly string _work=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"definitive-title-movie-"+Guid.NewGuid().ToString("N"));
    private string? _source,_draft;
    private byte[]? _sourceHash;
    internal IReadOnlyList<TitleMovieFile> Files {get;}
    internal MovieProfile? Profile {get;private set;}
    internal IProgress<string>? Progress {get;set;}
    internal bool Dirty=>_draft is not null;
    internal string? DraftPath=>_draft;
    internal GameTitleMovieService(GameProjectSession project,string? tools=null)
    {
        _project=project;_tools=tools??System.IO.Path.Combine(AppContext.BaseDirectory,"Tools","Mobipeg");
        string folder=System.IO.Path.Combine(project.GameConfiguration.RomFS,"m");
        Files=Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.moflex",SearchOption.TopDirectoryOnly).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new TitleMovieFile(p,MovieLabel(project.Generation,p))).ToArray():[];
        Directory.CreateDirectory(_work);
    }
    internal static string MovieLabel(int generation,string path)
    {
        string name=System.IO.Path.GetFileName(path),code=System.IO.Path.GetFileNameWithoutExtension(path).Replace("title_","");
        string language=code switch{"es"=>"Español","us"=>"Inglés","jp"=>"Japonés","de"=>"Alemán","fr"=>"Francés","it"=>"Italiano","kr"=>"Coreano","chs"=>"Chino simplificado","cht"=>"Chino tradicional",_=>"Clip "+code};
        return $"{(generation==7?language:"Animación Gen6")} · {name}";
    }
    private string Tool(string name)
    {
        string path=System.IO.Path.Combine(_tools,name+".exe");
        if(!File.Exists(path))throw new FileNotFoundException("No se encontró el motor de vídeo Mobipeg. Conservá la carpeta Tools/Mobipeg junto a la aplicación.",path);
        return path;
    }
    private async Task<string> Run(string tool,IEnumerable<string> arguments,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        var info=new ProcessStartInfo(Tool(tool)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(tool=="ffmpeg")foreach(string argument in new[]{"-nostdin","-progress","pipe:1","-nostats"})info.ArgumentList.Add(argument);
        foreach(string argument in arguments)info.ArgumentList.Add(argument);
        using var process=Process.Start(info)??throw new IOException("No se pudo iniciar el motor de vídeo.");
        var stdout=tool=="ffmpeg"?ReadProgress(process.StandardOutput,cancellation):process.StandardOutput.ReadToEndAsync(cancellation);var stderr=process.StandardError.ReadToEndAsync(cancellation);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch{try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}throw;}
        string output=await stdout,error=await stderr;
        if(process.ExitCode!=0)throw new InvalidDataException("El motor de vídeo rechazó la operación: "+error[^Math.Min(error.Length,3500)..]);
        return output;
    }
    private async Task<string> ReadProgress(StreamReader reader,CancellationToken cancellation)
    {
        while(await reader.ReadLineAsync(cancellation) is {} line)
            if(line.StartsWith("out_time=",StringComparison.Ordinal))Progress?.Report("Procesando vídeo · "+line[9..]);
        return string.Empty;
    }
    internal async Task<MovieProfile> Probe(string path,CancellationToken cancellation=default)
    {
        if(!File.Exists(path)||new FileInfo(path).Length is <16 or >536870912)throw new InvalidDataException("El vídeo debe tener entre 16 bytes y 512 MiB.");
        string json=await Run("ffprobe",["-v","error","-count_packets","-show_streams","-of","json",path],cancellation);
        using var document=JsonDocument.Parse(json);var tracks=new List<MovieTrack>();
        foreach(var s in document.RootElement.GetProperty("streams").EnumerateArray())
        {
            string S(string key)=>s.TryGetProperty(key,out var value)?value.ToString():"";
            int N(string key)=>int.TryParse(S(key),out int n)?n:0;
            long count=long.TryParse(S("nb_read_packets"),out long packets)?packets:0;int layout=6;
            if(s.TryGetProperty("side_data_list",out var list))foreach(var side in list.EnumerateArray())if(side.TryGetProperty("side_data_type",out var type)&&type.GetString()=="Stereo 3D")
            {string stereo=side.TryGetProperty("type",out var st)?st.GetString()??"":"";layout=stereo switch{"2D"=>6,"side by side"=>4,"top and bottom"=>2,"frame alternate"=>0,_=>throw new InvalidDataException("Disposición 3D no compatible: "+stereo)};if(side.TryGetProperty("inverted",out var inverted)&&inverted.GetInt32()!=0&&layout!=6)layout++;}
            tracks.Add(new(N("index"),S("codec_type"),S("codec_name"),N("width"),N("height"),S("r_frame_rate"),count,N("channels"),N("sample_rate"),layout));
        }
        return new(tracks);
    }
    internal static void CheckMoflex(string path)
    {
        using var stream=File.OpenRead(path);if(stream.ReadByte()!=0x4C||stream.ReadByte()!=0x32)throw new InvalidDataException("El archivo no es un MOFLEX válido.");
    }
    internal static void CheckProfile(MovieProfile expected,MovieProfile proposed)
    {
        // The retail muxer adds a data descriptor containing its regenerated seek table.
        // Compare audiovisual tracks in order; the seek table is not a third screen.
        var expectedTracks=expected.Tracks.Where(t=>t.Kind!="data").ToArray();
        var proposedTracks=proposed.Tracks.Where(t=>t.Kind!="data").ToArray();
        if(expectedTracks.Length!=proposedTracks.Length)throw new InvalidDataException("Deben conservarse todas las pistas de vídeo y audio originales.");
        for(int i=0;i<expectedTracks.Length;i++)
        {
            var a=expectedTracks[i];var b=proposedTracks[i];
            if(a.Kind!=b.Kind||a.Codec!=b.Codec||a.Kind=="video"&&(a.Width!=b.Width||a.Height!=b.Height||!SameRate(a.Rate,b.Rate)||a.Packets!=b.Packets||a.Layout!=b.Layout)||a.Kind=="audio"&&(a.Channels!=b.Channels||a.SampleRate!=b.SampleRate))
                throw new InvalidDataException($"La pista {i} debe conservar formato, dimensiones, velocidad, disposición, número de fotogramas y características de audio del original.");
        }
    }
    private static bool SameRate(string a,string b)=>Rate(a)==Rate(b);
    private static decimal Rate(string rate){var parts=rate.Split('/');return decimal.TryParse(parts[0],CultureInfo.InvariantCulture,out decimal n)&&parts.Length==2&&decimal.TryParse(parts[1],CultureInfo.InvariantCulture,out decimal d)&&d!=0?n/d:0;}
    private static byte[] Hash(string path){using var stream=File.OpenRead(path);return SHA256.HashData(stream);}
    private void Unchanged(){if(_source is null||_sourceHash is null||!Hash(_source).AsSpan().SequenceEqual(_sourceHash))throw new IOException("El vídeo original cambió; volvé a seleccionarlo.");}
    internal async Task Select(TitleMovieFile file,CancellationToken cancellation=default)
    {
        if(!Files.Any(f=>string.Equals(f.Path,file.Path,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Vídeo ajeno al proyecto.");
        CheckMoflex(file.Path);byte[] hash=Hash(file.Path);var profile=await Probe(file.Path,cancellation);
        if(profile.Videos.Length is <1 or >2||profile.Videos.Any(v=>v.Codec!="mobiclip"||v.Width<=0||v.Height<=0||v.Packets<1||Rate(v.Rate)<=0))throw new InvalidDataException("MOFLEX sin vídeo compatible.");
        if(!hash.AsSpan().SequenceEqual(Hash(file.Path)))throw new IOException("El vídeo cambió durante la lectura.");
        Discard();_source=file.Path;_sourceHash=hash;Profile=profile;
    }
    private void SafeExport(string target)
    {
        string root=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(_project.ProjectRoot))+System.IO.Path.DirectorySeparatorChar;
        if(System.IO.Path.GetFullPath(target).StartsWith(root,StringComparison.OrdinalIgnoreCase)||File.Exists(target))throw new IOException("Exportá fuera del proyecto a un archivo que todavía no exista.");
        Unchanged();
    }
    internal void ExportMoflex(string target){SafeExport(target);File.Copy(_draft??_source!,target);}
    internal async Task ExportMp4(string target,int video,CancellationToken cancellation=default)
    {
        SafeExport(target);if(Profile is null||video<0||video>=Profile.Videos.Length)throw new ArgumentException("Pista de vídeo no disponible.");
        string temporary=System.IO.Path.Combine(_work,Guid.NewGuid()+".mp4");
        await Run("ffmpeg",["-v","error","-xerror","-n","-i",_draft??_source!,"-map",$"0:v:{video}","-map","0:a?","-c:v","libx264","-preset","fast","-crf","18","-fps_mode","passthrough","-c:a","libmp3lame",temporary],cancellation);
        Unchanged();File.Move(temporary,target);
    }
    internal async Task<byte[]> Preview(decimal seconds,int video,CancellationToken cancellation=default)
    {
        Unchanged();if(Profile is null||video<0||video>=Profile.Videos.Length||seconds<0||seconds>=Profile.Videos[video].Packets/Rate(Profile.Videos[video].Rate))throw new ArgumentException("Fotograma fuera del vídeo.");
        string path=System.IO.Path.Combine(_work,Guid.NewGuid()+".bmp");
        try{await Run("ffmpeg",["-v","error","-xerror","-n","-i",_draft??_source!,"-ss",seconds.ToString(CultureInfo.InvariantCulture),"-map",$"0:v:{video}","-frames:v","1","-c:v","bmp",path],cancellation);return await File.ReadAllBytesAsync(path,cancellation);}finally{if(File.Exists(path))File.Delete(path);}
    }
    private async Task Validate(string path,CancellationToken cancellation)
    {
        CheckMoflex(path);CheckProfile(Profile??throw new InvalidOperationException("Elegí un vídeo."),await Probe(path,cancellation));
        await Run("ffmpeg",["-v","error","-xerror","-i",path,"-map","0:v","-map","0:a?","-f","null","NUL"],cancellation);
        Unchanged();
    }
    internal async Task ImportMoflex(string input,CancellationToken cancellation=default)
    {
        Unchanged();if(!File.Exists(input)||new FileInfo(input).Length is <16 or >536870912)throw new InvalidDataException("El vídeo debe tener entre 16 bytes y 512 MiB.");string stage=System.IO.Path.Combine(_work,Guid.NewGuid()+".moflex");
        try{File.Copy(input,stage);await Validate(stage,cancellation);Discard();_draft=stage;}catch{if(File.Exists(stage))File.Delete(stage);throw;}
    }
    internal async Task ImportEditedVideo(string input,int video,CancellationToken cancellation=default)
    {
        Unchanged();var original=Profile??throw new InvalidOperationException("Elegí un vídeo.");
        if(video<0||video>=original.Videos.Length)throw new ArgumentException("Pista inválida.");
        var edited=await Probe(input,cancellation);if(edited.Videos.Length!=1)throw new InvalidDataException("El vídeo editado debe contener una sola pista de vídeo, como el MP4 exportado.");
        var a=original.Videos[video];var b=edited.Videos[0];
        if(a.Width!=b.Width||a.Height!=b.Height||!SameRate(a.Rate,b.Rate)||a.Packets!=b.Packets)throw new InvalidDataException("El vídeo editado debe conservar dimensiones, FPS y número de fotogramas. Exportá el MP4 original para usarlo como base.");
        string stage=System.IO.Path.Combine(_work,Guid.NewGuid()+".moflex");
        var args=new List<string>{"-v","error","-xerror","-n","-i",_draft??_source!,"-i",input};
        for(int i=0;i<original.Videos.Length;i++){args.Add("-map");args.Add(i==video?"1:v:0":$"0:v:{i}");}
        args.AddRange(["-map","0:a?","-c","copy",$"-c:v:{video}","mobiclip",$"-pix_fmt:v:{video}","yuv420p",$"-fps_mode:v:{video}","passthrough","-preset","medium","-qp","22","-mo_layout",original.Videos[0].Layout.ToString(CultureInfo.InvariantCulture)]);
        if(original.Videos.Length==2)args.AddRange(["-mo_layout2",original.Videos[1].Layout.ToString(CultureInfo.InvariantCulture)]);
        if(original.Audio.Length>0)
        {
            if(original.Audio.Length!=1)throw new InvalidDataException("Solo se admite una pista de audio original.");
            string mode=original.Audio[0].Codec switch{"adpcm_ima_moflex"=>"1","fastaudio"=>"0","pcm_s16le"=>"2",_=>throw new InvalidDataException("Códec de audio no compatible.")};
            args.AddRange(["-c:a","pcm_s16le","-mo_audio",mode]);
        }
        args.AddRange(["-mo_block","4096","-mo_vdesc","3","-f","moflex",stage]);
        try{await Run("ffmpeg",args,cancellation);await Validate(stage,cancellation);Discard();_draft=stage;}catch{if(File.Exists(stage))File.Delete(stage);throw;}
    }
    internal void Apply()
    {
        if(_draft is null)return;Unchanged();byte[] before=File.ReadAllBytes(_source!),after=File.ReadAllBytes(_draft);
        _project.History.Commit([new FileChange(_source!,before,after)],"Animaciones de título · Gen"+_project.Generation,"Sustituir vídeo MOFLEX",[new BatchChange(0,System.IO.Path.GetFileName(_source!),"Vídeo","Original","Sustitución verificada")]);
        _sourceHash=SHA256.HashData(after);Discard();
    }
    internal void Discard(){if(_draft is not null&&File.Exists(_draft))File.Delete(_draft);_draft=null;}
    public void Dispose(){try{if(Directory.Exists(_work))Directory.Delete(_work,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
