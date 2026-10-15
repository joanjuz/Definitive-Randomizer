using System.IO;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;

internal static class TitleScreenChecks
{
    private static byte[] Archive(byte[] image, bool presentation=false)
    {
        const int prefix=16, data=256;
        byte[] names=Encoding.Unicode.GetBytes(presentation?"\0folder\0gf_logo.bclim\0presented_by.bclim\0":"\0folder\0first.bclim\0second.bin\0");
        byte[] result=new byte[prefix+data+image.Length+(presentation?image.Length:16)];result.AsSpan(0,prefix).Fill(0xA7);
        using var writer=new BinaryWriter(new MemoryStream(result));writer.BaseStream.Position=prefix;
        writer.Write("darc"u8);writer.Write((ushort)0xFEFF);writer.Write((ushort)28);writer.Write(0x01000000u);writer.Write((uint)(result.Length-prefix));writer.Write(28u);writer.Write((uint)(48+names.Length));writer.Write((uint)data);
        writer.Write(0x01000000u);writer.Write(0u);writer.Write(4u);
        writer.Write(0x01000002u);writer.Write(0u);writer.Write(4u);
        writer.Write(16u);writer.Write((uint)data);writer.Write((uint)image.Length);
        writer.Write(presentation?44u:40u);writer.Write((uint)(data+image.Length));writer.Write((uint)(presentation?image.Length:16));
        writer.Write(names);writer.BaseStream.Position=prefix+data;writer.Write(image);writer.Write(presentation?image:Enumerable.Range(0,16).Select(i=>(byte)(i+10)).ToArray());return result;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        check(GameEtcDecoder.Pixel(0x8888880000000000UL,0,0,0,false).R==138,"ETC1 individual base color and positive selector match Khronos vector");
        check(GameEtcDecoder.Pixel(0x8888880000010000UL,0,0,0,false).R==134,"ETC1 negative selector decodes signed modifier");
        check(GameEtcDecoder.Pixel(0x8888880000000001UL,0,0,0,false).R==144,"ETC1 large positive selector uses correct bitplane");
        check(GameEtcDecoder.Pixel(0x8787870200000000UL,0,2,0,false).R==125,"ETC1 differential negative three-bit delta decoded independently");
        check(GameEtcDecoder.Pixel(0x8888880000000000UL,7,0,0,true).A==119,"ETC1A4 four-bit alpha expanded to eight bits");
        reject(()=>GameEtcDecoder.Pixel(0x0404040200000000UL,0,2,0,false),"ETC1 invalid differential base rejected");
        byte[] source=Archive([1,2,3,4]);var codec=new TitleDarcCodec(source);
        foreach(int length in new[]{1,4,128,260})
        {
            byte[] replacement=Enumerable.Range(0,length).Select(i=>(byte)i).ToArray();byte[] output=codec.Replace(2,replacement);var reread=new TitleDarcCodec(output);
            check(reread.Read(2).SequenceEqual(replacement)&&reread.Read(3).SequenceEqual(codec.Read(3)),"Title DARC replacement length "+length+" preserves neighboring payload");
            check(output.AsSpan(0,16).SequenceEqual(source.AsSpan(0,16))&&reread.Entries[1]==codec.Entries[1],"Title DARC prefix and folder indices preserved for length "+length);
        }
        reject(()=>codec.Replace(1,[1]),"Title DARC folders cannot be replaced");
        byte[] bad=(byte[])source.Clone();BitConverter.GetBytes(257u).CopyTo(bad,16+28+36+4);reject(()=>new TitleDarcCodec(bad),"Title DARC overlapping payloads rejected");
        using var bitmap=new System.Drawing.Bitmap(8,8);bitmap.SetPixel(0,0,System.Drawing.Color.Red);byte[] bclim=GameGraphicsCodec.EncodeRgba8(bitmap);
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var fixture=new Fixture(version);string path=Path.Combine(fixture.Game.RomFS,version==GameVersion.ORAS?"a/1/5/2":"a/1/6/5");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            int first=version==GameVersion.ORAS?1120:467,count=version==GameVersion.ORAS?1136:481;byte[][] records=Enumerable.Range(0,count).Select(_=>new byte[]{0,5,8}).ToArray();for(int i=first;i<count;i++)records[i]=Archive(bclim);
            if(version==GameVersion.ORAS)records[1119]=Archive(bclim,true);
            new pk3DS.Core.GARCFile(GARC.PackGARC(records,fixture.Game.GARCVersion,4),fixture.Game.GetGARCReference("titlescreen"),path).Save();byte[] before=File.ReadAllBytes(path);var service=new GameTitleScreenService(fixture.Session);
            check(service.Archives.Count==(version==GameVersion.ORAS?17:14)&&service.Images(first).Count==1,version+" title games languages and image entries exposed");
            check(service.Archives[0].Description.Contains("#"+first)&&service.Archives[0].Description.Contains("first.bclim"),version+" title catalogue identifies actual record and textures");
            if(version==GameVersion.ORAS)check(!service.Archives.Any(a=>a.Label.Contains("JP1")),"ORAS catalogue does not invent JP1 language");
            if(version==GameVersion.ORAS)
            {
                check(service.Images(1119).Select(e=>e.Name).SequenceEqual(new[]{"gf_logo.bclim","presented_by.bclim"}),"ORAS exposes both forest presentation textures");
                using var logo=new System.Drawing.Bitmap(8,8);logo.SetPixel(1,1,System.Drawing.Color.Magenta);
                byte[] logoBytes=GameGraphicsCodec.EncodeRgba8(logo),neighbor=service.Read(1119,3);
                service.Replace(1119,2,logoBytes);service.Apply();
                check(new GameTitleScreenService(fixture.Session).Read(1119,2).SequenceEqual(logoBytes)&&service.Read(1119,3).SequenceEqual(neighbor),"ORAS presentation edit saves and preserves neighboring Presented by texture");
                before=File.ReadAllBytes(path);
            }
            using var updated=new System.Drawing.Bitmap(8,8);updated.SetPixel(0,0,System.Drawing.Color.Blue);byte[] replacement=GameGraphicsCodec.EncodeRgba8(updated);
            service.Replace(first,2,replacement);check(service.Dirty&&File.ReadAllBytes(path).SequenceEqual(before),version+" title import writes nothing until apply");
            service.Apply();var reread=new GameTitleScreenService(fixture.Session);check(!service.Dirty&&reread.Read(first,2).SequenceEqual(replacement),version+" title save roundtrips and clears dirty state");
            var rawBefore=new GARC.MemGARC(before);var rawAfter=new GARC.MemGARC(File.ReadAllBytes(path));check(Enumerable.Range(0,count).Where(i=>i!=first).All(i=>rawBefore.GetFile(i).SequenceEqual(rawAfter.GetFile(i))),version+" title save preserves every unrelated raw record");
            reread.Replace(first,2,bclim);File.AppendAllText(path,"changed");reject(reread.Apply,version+" title stale source rejected before history commit");
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        using var fixture=new Fixture(GameVersion.XY);using var bitmap=new System.Drawing.Bitmap(8,8);bitmap.SetPixel(0,0,System.Drawing.Color.Red);var record=Archive(GameGraphicsCodec.EncodeRgba8(bitmap));
        string path=Path.Combine(fixture.Game.RomFS,"a/1/6/5");Directory.CreateDirectory(Path.GetDirectoryName(path)!);fixture.WriteGarc(path,Enumerable.Range(0,481).Select(i=>i>=467?record:new byte[]{0,5,8}).ToArray(),fixture.Game.GetGARCReference("titlescreen"));
        var view=new ThreeDS.Progressive.TitleScreenView(fixture.Session);var service=new GameTitleScreenService(fixture.Session);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(ThreeDS.Progressive.TitleScreenView).GetField("_service",flags)!.SetValue(view,service);
        var archives=(System.Windows.Controls.ComboBox)typeof(ThreeDS.Progressive.TitleScreenView).GetField("_archives",flags)!.GetValue(view)!;archives.ItemsSource=service.Archives;archives.SelectedIndex=0;
        var window=new System.Windows.Window{Width=1150,Height=820,Content=view};window.Show();
        try
        {
            var image=(System.Windows.Controls.Image)typeof(ThreeDS.Progressive.TitleScreenView).GetField("_image",flags)!.GetValue(view)!;
            check(image.Source is not null && view.ConfirmLeave(),"WPF title editor displays selected image and clean navigation");
            check(Equals(view.Background,System.Windows.Application.Current.FindResource("Bg")),"WPF title editor uses application dark background");
            typeof(ThreeDS.Progressive.TitleScreenView).GetField("_busy",flags)!.SetValue(view,true);check(!view.ConfirmLeave(),"WPF title editor blocks navigation during active save");typeof(ThreeDS.Progressive.TitleScreenView).GetField("_busy",flags)!.SetValue(view,false);
            typeof(UtilityCompletionUiChecks).GetMethod("Capture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[view,"title-screen-gen6.png"]);
        }
        finally{window.Close();}
        check(ProjectFeatureAvailability.Supports(fixture.Session,"TitleScreen6"),"Gen6 title tool visible in Gen6");
        using var gen7=new Fixture(GameVersion.USUM);check(!ProjectFeatureAvailability.Supports(gen7.Session,"TitleScreen6"),"Gen6 title tool hidden in Gen7");
    }
    internal static void ReadReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var project=GameProjectSession.Open(root,recoverInterruptedSave:false);string path=Path.Combine(project.GameConfiguration.RomFS,project.GameConfiguration.ORAS?"a/1/5/2":"a/1/6/5");byte[] before=SHA256.HashData(File.ReadAllBytes(path));var service=new GameTitleScreenService(project);int images=0;
            foreach(var archive in service.Archives)foreach(var image in service.Images(archive.Id)){using var bitmap=GameGraphicsCodec.Decode(service.Read(archive.Id,image.Id));if(bitmap.Width<1||bitmap.Height<1)throw new Exception("Empty decoded title image");images++;}
            using(var fixture=new Fixture(project.GameConfiguration.Version))
            {
                string copy=Path.Combine(fixture.Game.RomFS,project.GameConfiguration.ORAS?"a/1/5/2":"a/1/6/5");if(!Path.GetFullPath(copy).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(copy).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Clone destination must be in temporary fixture outside original ROM");Console.WriteLine("TEMPORARY FIXTURE WRITE TARGET: "+fixture.Root);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(path,copy,true);
                var cloned=new GameTitleScreenService(fixture.Session);int archive=cloned.Archives[^1].Id,image=cloned.Images(archive)[^1].Id;using var bitmap=GameGraphicsCodec.Decode(cloned.Read(archive,image));bitmap.SetPixel(0,0,System.Drawing.Color.Magenta);byte[] replacement=GameGraphicsCodec.EncodeRgba8(bitmap);cloned.Replace(archive,image,replacement);cloned.Apply();if(!new GameTitleScreenService(fixture.Session).Read(archive,image).SequenceEqual(replacement))throw new Exception("Real title clone roundtrip failed");
                Console.WriteLine("PASS REAL TITLE CLONE SAVE: "+root+"; PNG/RGBA8 replacement, DARC neighbors and every unrelated raw GARC record verified");
            }
            if(!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))throw new Exception("Read-only title audit changed source");
            Console.WriteLine($"PASS REAL TITLE READ: {root}; {service.Archives.Count} archives; {images} decoded images; original SHA-256 unchanged");
        }
    }
}
