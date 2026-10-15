using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;
internal static class GarcShuffleChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS})
        {
            using var f=new Fixture(version);string path=Path.Combine(f.Game.RomFS,"a","9","9","9");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            f.WriteGarc(path,[new byte[]{1,2},new byte[]{3,4,5,6},new byte[]{7}],f.Game.GetGARCReference("personal"));
            byte[] source=File.ReadAllBytes(path);var service=new GameGarcShuffleService(f.Session);var preview=Enumerable.Range(1,20).Select(seed=>service.Preview("a/9/9/9",seed)).First(p=>p.Changes.Count>0);
            check(File.ReadAllBytes(path).SequenceEqual(source)&&service.Preview("a/9/9/9",preview.Seed).File.Proposed().SequenceEqual(preview.File.Proposed()),version+" GARC Shuffler preview is deterministic and read-only");
            int data=(int)BitConverter.ToUInt32(source,16);check(preview.File.Proposed().AsSpan(data).SequenceEqual(source.AsSpan(data)),version+" GARC Shuffler preserves entire original payload byte-for-byte");
            var oldFiles=new GARC.MemGARC(source).Files;var newFiles=new GARC.MemGARC(preview.File.Proposed()).Files;
            check(oldFiles.Select(Convert.ToHexString).Order().SequenceEqual(newFiles.Select(Convert.ToHexString).Order()),version+" GARC Shuffler reorders original file pool without losing any file");
            string backup=Path.Combine(Path.GetTempPath(),"shuffle-"+Guid.NewGuid().ToString("N")+".zip");
            try
            {
                reject(()=>service.Apply(preview,Path.Combine(f.Root,"backup.zip")),version+" GARC Shuffler refuses backup inside source project");service.Apply(preview,backup);
                var plan=Task.Run(()=>ProjectRestore.InspectAsync(backup)).GetAwaiter().GetResult();var restore=Task.Run(()=>ProjectRestore.PreviewSelectedAsync(f.Session,plan,["RomFS/a/9/9/9"])).GetAwaiter().GetResult();ProjectRestore.ApplySelected(f.Session,restore);
                check(File.ReadAllBytes(path).SequenceEqual(source),version+" GARC Shuffler backup restores exact original bytes through selective restore");
                reject(()=>service.Apply(preview,backup),version+" GARC Shuffler never replaces existing backup");
                reject(()=>service.Preview("a/0/0/5",1),version+" GARC Shuffler retains original protected GARC banlist");reject(()=>service.Preview("../code.bin",1),version+" GARC Shuffler rejects path traversal");
                                using var packed=new MemoryStream();using(var writer=new BinaryWriter(packed,System.Text.Encoding.UTF8,true))
                {
                    writer.Write(0x47415243u);writer.Write(28u);writer.Write((ushort)0xFEFF);writer.Write((ushort)0x400);writer.Write(4u);writer.Write(136u);writer.Write(152u);writer.Write(4u);
                    writer.Write(0x4641544Fu);writer.Write(24);writer.Write((ushort)3);writer.Write((ushort)0);writer.Write(0);writer.Write(16);writer.Write(44);
                    writer.Write(0x46415442u);writer.Write(72);writer.Write(3);
                    writer.Write(1u);writer.Write(0);writer.Write(4);writer.Write(4);
                    writer.Write(3u);writer.Write(4);writer.Write(8);writer.Write(4);writer.Write(8);writer.Write(12);writer.Write(4);
                    writer.Write(1u);writer.Write(12);writer.Write(16);writer.Write(4);
                    writer.Write(0x46494D42u);writer.Write(12u);writer.Write(16u);writer.Write(Enumerable.Range(1,16).Select(i=>(byte)i).ToArray());
                }
                byte[] foldered=packed.ToArray();File.WriteAllBytes(path,foldered);
                var folderPreview=Enumerable.Range(1,20).Select(seed=>service.Preview("a/9/9/9",seed)).First(p=>p.Changes.Count>0);
                check(folderPreview.Changes.All(row=>row.Id!=1)&&folderPreview.File.Proposed().AsSpan(80,28).SequenceEqual(foldered.AsSpan(80,28))&&folderPreview.File.Proposed().AsSpan(136).SequenceEqual(foldered.AsSpan(136)),version+" GARC Shuffler preserves folder subfile references and payload exactly");
                byte[] damaged=(byte[])source.Clone();damaged[16]=0;File.WriteAllBytes(path,damaged);reject(()=>service.Preview("a/9/9/9",1),version+" GARC Shuffler validates broken table before writes");
            }
            finally{if(File.Exists(backup))File.Delete(backup);}
        }
        using var gen7=new Fixture(GameVersion.USUM);reject(()=>new GameGarcShuffleService(gen7.Session).Preview("a/9/9/9",1),"GARC Shuffler refuses Gen7 rather than applying Gen6 banlist");
    }
}
