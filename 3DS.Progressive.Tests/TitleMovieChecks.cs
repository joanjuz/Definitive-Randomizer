using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class TitleMovieChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var top=new MovieTrack(0,"video","mobiclip",400,240,"30/1",120,0,0,6);
        var bottom=top with{Index=1,Width=320};
        var audio=new MovieTrack(2,"audio","adpcm_ima_moflex",0,0,"0/0",120,2,32728,6);
        var profile=new MovieProfile([top,bottom,audio]);
        GameTitleMovieService.CheckProfile(profile,new([top with{Rate="60/2"},bottom,audio]));
        check(true,"Movie compatibility accepts equivalent rational FPS");
        GameTitleMovieService.CheckProfile(profile,new([top,bottom,new MovieTrack(2,"data","",0,0,"0/0",1,0,0,6),audio with{Index=3}]));
        check(true,"Movie compatibility accepts regenerated retail seek descriptor");
        foreach(var changed in new[]{top with{Width=320},top with{Height=256},top with{Rate="24/1"},top with{Packets=119},top with{Layout=4},top with{Codec="h264"}})
            reject(()=>GameTitleMovieService.CheckProfile(profile,new([changed,bottom,audio])),"Movie replacement rejects changed video "+changed);
        reject(()=>GameTitleMovieService.CheckProfile(profile,new([top,audio])),"Movie replacement requires bottom screen");
        reject(()=>GameTitleMovieService.CheckProfile(profile,new([top,bottom])),"Movie replacement requires audio");
        reject(()=>GameTitleMovieService.CheckProfile(profile,new([top,bottom,audio with{SampleRate=48000}])),"Movie replacement requires original audio sample rate");
        reject(()=>GameTitleMovieService.CheckProfile(profile,new([top,bottom,audio with{Channels=1}])),"Movie replacement requires original audio channels");
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);string folder=Path.Combine(fixture.Game.RomFS,"m");Directory.CreateDirectory(folder);
            string movie=Path.Combine(folder,fixture.Game.Generation==7?"title_es.moflex":"ay.moflex");File.WriteAllBytes(movie,[0x4C,0x32,0,0]);
            File.WriteAllText(Path.Combine(folder,"dps.cdc"),"not a movie");
            using var service=new GameTitleMovieService(fixture.Session,Path.Combine(fixture.Root,"missing-tools"));
            check(service.Files.Count==1&&service.Files[0].Path==movie,version+" movie enumeration excludes CDC");
            check(!service.Dirty&&service.Profile is null,version+" starts without draft");
            reject(()=>service.Select(new TitleMovieFile(Path.Combine(folder,"alien.moflex"),"foreign")).GetAwaiter().GetResult(),version+" rejects movie outside catalogue before external process");
            reject(()=>service.ExportMoflex(Path.Combine(fixture.Root,"overwrite.moflex")),version+" rejects export into source project");
            File.WriteAllText(movie,"invalid");reject(()=>GameTitleMovieService.CheckMoflex(movie),version+" validates MOFLEX magic");
            check(File.ReadAllText(movie)=="invalid"&&!service.Dirty,version+" failed reads do not mutate source or draft");
        }
        check(GameTitleMovieService.MovieLabel(7,"title_es.moflex").Contains("Español"),"Movie catalogue names Spanish track");
    }

    internal static void RunUi(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var fixture=new Fixture(version);
            var view=new ThreeDS.Progressive.TitleResourcesView(fixture.Session);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var page=(System.Windows.Controls.ContentControl)view.GetType().GetField("_page",flags)!.GetValue(view)!;
            var movies=(ThreeDS.Progressive.TitleMoviesView)view.GetType().GetField("_movies",flags)!.GetValue(view)!;
            try
            {
                check(page.Content is ThreeDS.Progressive.TitleScreenView&&view.ConfirmLeave(),version+" title opens 2D editor with clean navigation");
                var root=(System.Windows.Controls.DockPanel)view.Content;
                var navigation=(System.Windows.Controls.WrapPanel)root.Children[0];
                ((System.Windows.Controls.Button)navigation.Children[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                check(ReferenceEquals(page.Content,movies),version+" title navigation opens MOFLEX editor");
                check(Equals(movies.Background,System.Windows.Application.Current.FindResource("Bg")),version+" movie editor keeps application dark background");
                using var cancellation=new CancellationTokenSource();
                movies.GetType().GetField("_cancellation",flags)!.SetValue(movies,cancellation);
                check(!view.ConfirmLeave(),version+" active movie processing blocks navigation");
                ((System.Windows.Controls.Button)navigation.Children[0]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                check(ReferenceEquals(page.Content,movies),version+" active movie processing blocks tab switching");
                movies.GetType().GetField("_cancellation",flags)!.SetValue(movies,null);
            }
            finally{movies.Dispose();}
        }
    }

    internal static void ReadReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
            using var service=new GameTitleMovieService(project);
            if(service.Files.Count==0)throw new Exception("No title movies found: "+root);
            foreach(var movie in service.Files)
            {
                byte[] before=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(movie.Path));
                service.Select(movie).GetAwaiter().GetResult();
                if(!before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(movie.Path))))throw new Exception("Read-only movie audit changed source");
                Console.WriteLine("PASS READ ONLY MOVIE "+movie.Path+" "+System.Text.Json.JsonSerializer.Serialize(service.Profile));
            }
        }
    }
    internal static void RoundtripReal(string root,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        using var catalog=new GameTitleMovieService(project);
        var source=catalog.Files.FirstOrDefault(f=>Path.GetFileName(f.Path)=="title_es.moflex")??catalog.Files.First(f=>Path.GetFileName(f.Path)=="ay.moflex");
        byte[] before=File.ReadAllBytes(source.Path);
        using var fixture=new Fixture(project.GameConfiguration.Version);
        string folder=Path.Combine(fixture.Game.RomFS,"m");Directory.CreateDirectory(folder);
        string copy=Path.Combine(folder,Path.GetFileName(source.Path));File.WriteAllBytes(copy,before);
        using var service=new GameTitleMovieService(fixture.Session);
        service.Select(service.Files.Single()).GetAwaiter().GetResult();
        string mp4=Path.Combine(output,"title-roundtrip.mp4"),moflex=Path.Combine(output,"title-roundtrip.moflex");
        service.ExportMp4(mp4,0).GetAwaiter().GetResult();
        service.ImportEditedVideo(mp4,0).GetAwaiter().GetResult();
        if(!service.Dirty||!File.ReadAllBytes(copy).SequenceEqual(before))throw new Exception("Staging must preserve source until apply");
        service.ExportMoflex(moflex);service.Apply();
        if(service.Dirty||!File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(moflex))||fixture.Session.History.Read().Entries.Count!=1)throw new Exception("Movie verified commit/history failed");
        if(!File.ReadAllBytes(source.Path).SequenceEqual(before))throw new Exception("Original game changed");
        Console.WriteLine("PASS REAL MOVIE ROUNDTRIP: "+root+"; MP4 export, Mobiclip encode, audiovisual profile, full decode, temporary clone commit/history, original unchanged");
    }
}
