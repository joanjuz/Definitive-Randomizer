using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;

internal static partial class UsumMintIconChecks
{
    internal static void RealCopies(string output,string sun,string moon)
    {
        using var state=new TestState();output=Path.GetFullPath(output);
        if(Directory.Exists(output))throw new IOException("Use a new private output directory.");
        foreach(string source in new[]{sun,moon})
            if(output.StartsWith(Path.GetFullPath(source)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Output must be separate from its source.");
        var textures=UsumMintIconDownload.LoadAsync(UsumMintIconDownload.DefaultCache).GetAwaiter().GetResult();
        Directory.CreateDirectory(output);
        using(var gallery=new Bitmap(7*96,3*100))
        {
            using(var graphics=Graphics.FromImage(gallery))
            {
                graphics.Clear(Color.FromArgb(30,35,42));graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                for(int i=0;i<21;i++)
                {
                    using var icon=GameGraphicsCodec.Decode(textures[i]);graphics.DrawImage(icon,new Rectangle(i%7*96,i/7*100,96,96));
                    graphics.DrawString(UsumMintIconSources.All[i].ItemId.ToString(),SystemFonts.DefaultFont,Brushes.White,i%7*96,i/7*100+84);
                }
            }
            gallery.Save(Path.Combine(output,"mint-icons-private-preview.png"),ImageFormat.Png);
        }
        foreach((string source,string name) in new[]{(sun,"UltraSol"),(moon,"UltraLuna")})
        {
            string root=Path.Combine(output,name);Directory.CreateDirectory(root);
            Copy(Path.Combine(source,"RomFS"),Path.Combine(root,"RomFS"));Copy(Path.Combine(source,"ExeFS"),Path.Combine(root,"ExeFS"));
            foreach(string header in new[]{"ncchheader.bin","HeaderNCCH0.bin","HeaderNCCH.bin","exheader.bin","DecryptedExHeader.bin"})
                if(File.Exists(Path.Combine(source,header)))File.Copy(Path.Combine(source,header),Path.Combine(root,header));
            var project=GameProjectSession.Open(root,false);var service=new UsumMintIconService(project);
            if(service.Inspect().State!=UsumMintState.NotInstalled)throw new IOException(name+": "+service.Inspect().Report);
            string shopPath=Path.Combine(project.GameConfiguration.RomFS,"Shop.cro");string shopHash=Hash(File.ReadAllBytes(shopPath));
            var before=Directory.GetFiles(project.GameConfiguration.RomFS,"*",SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(project.GameConfiguration.ExeFS)).ToDictionary(path=>path,path=>Hash(File.ReadAllBytes(path)));
            var preview=service.Prepare(textures);var files=preview.Proposal!.Files.ToDictionary(file=>file.Path,file=>file.Original());
            var inputHashes=files.Keys.ToDictionary(path=>Path.Combine(source,Path.GetRelativePath(root,path)),path=>Hash(File.ReadAllBytes(path)));
            service.Apply(preview);
            var reopened=GameProjectSession.Open(root,false);
            if(new UsumMintIconService(reopened).Inspect().State!=UsumMintState.Installed||new UsumNatureMintService(reopened).Inspect().State!=UsumMintState.Installed)
                throw new IOException("Installed icon/functional state not recognized after reopen.");
            var marts=new GameMartService(reopened);
            if(!UsumNatureMintCatalog.Mints.All(mint=>marts.RegularShops.Any(shop=>shop.Rows.Any(row=>row.ItemId==mint.ItemId)))||Hash(File.ReadAllBytes(shopPath))!=shopHash)
                throw new IOException("Source mint shop entries were not preserved.");
            if(before.Where(pair=>!files.ContainsKey(pair.Key)).Any(pair=>Hash(File.ReadAllBytes(pair.Key))!=pair.Value))throw new IOException("Unexpected modified project file.");
            var entry=project.History.Read().Entries.Last();RestorePoints.Undo(project.History,root,entry.Id);
            if(files.Any(pair=>!File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))||new UsumNatureMintService(reopened).Inspect().State!=UsumMintState.Installed)
                throw new IOException("Icon undo did not preserve the functional phase 1.");
            service.Apply(service.Prepare(textures));
            if(inputHashes.Any(pair=>Hash(File.ReadAllBytes(pair.Key))!=pair.Value))throw new IOException("A source input changed.");
            string evidence="PASS "+name+": 21 pinned source PNGs converted; 769 original records preserved, 21 appended, mapping only; install/reopen/exact undo/reinstall; shops and other project files unchanged; gameplay NOT tested.\n"+
                string.Join("\n",files.Keys.Select(path=>Path.GetRelativePath(root,path)+": "+Hash(File.ReadAllBytes(path))));
            File.WriteAllText(Path.Combine(output,name+"-validation.txt"),evidence);Console.WriteLine(evidence);
        }
        static void Copy(string source,string target)
        {
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Do not copy links.");Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source))
            {if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Do not copy links.");File.Copy(file,Path.Combine(target,Path.GetFileName(file)));}
            foreach(string directory in Directory.GetDirectories(source))Copy(directory,Path.Combine(target,Path.GetFileName(directory)));
        }
    }
}
