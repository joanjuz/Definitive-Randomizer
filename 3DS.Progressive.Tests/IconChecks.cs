using System.IO;
using System.Drawing;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

internal static class IconChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);string path=Path.Combine(f.Game.ExeFS,"icon.bin");
            byte[] original=new byte[0x36C0];"SMDH"u8.CopyTo(original);original[6]=71;original.AsSpan(0x2008,0x38).Fill(0xA5);original.AsSpan(0x2040).Fill(0x96);File.WriteAllBytes(path,original);
            var service=new GameIconService(f.Session);service.Edit(5,0,"Definitive Randomizer");service.Edit(5,1,"Descripción Ñ 😀");service.Edit(5,2,"Joan");
            check(File.ReadAllBytes(path).SequenceEqual(original)&&service.Dirty,version+" SMDH draft is read-only until apply");
            byte[] draft=service.Export();check(draft.AsSpan(0,8).SequenceEqual(original.AsSpan(0,8))&&draft.AsSpan(0x2008).SequenceEqual(original.AsSpan(0x2008))&&Enumerable.Range(0,16).Where(i=>i!=5).All(i=>draft.AsSpan(8+i*0x200,0x200).SequenceEqual(original.AsSpan(8+i*0x200,0x200))),version+" SMDH text edits preserve flags icons headers and other languages byte-for-byte");
            reject(()=>service.Edit(0,0,new string('x',65)),version+" SMDH rejects overlong title");reject(()=>service.Edit(0,2,"a\0b"),version+" SMDH rejects embedded NUL");reject(()=>service.Edit(0,0,"\uD800"),version+" SMDH rejects unpaired UTF-16 surrogate");reject(()=>service.Edit(16,0,"bad"),version+" SMDH validates language index");
            service.Apply();check(!service.Dirty&&new GameIconService(f.Session).Read(5,1)=="Descripción Ñ 😀",version+" SMDH commit reads exact UTF-16 metadata");
            byte[] metadata=File.ReadAllBytes(path);using var bitmap=new Bitmap(24,24);
            using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(Color.Black);
            foreach(var point in new[]{(0,0),(1,0),(0,1),(2,0),(8,0),(0,8)})bitmap.SetPixel(point.Item1,point.Item2,Color.Red);
            service.ReplaceImage(false,bitmap);draft=service.Export();
            check(new[]{0,1,2,4,64,192}.All(index=>draft[0x2040+2*index]==0&&draft[0x2040+2*index+1]==0xF8)&&draft.AsSpan(0x24C0).SequenceEqual(metadata.AsSpan(0x24C0))&&draft.AsSpan(0,0x2040).SequenceEqual(metadata.AsSpan(0,0x2040)),version+" SMDH RGB565 tile encoding matches independent Morton coordinates and preserves other sections");
            using var decoded=service.Image(false);check(decoded.GetPixel(8,0).ToArgb()==Color.Red.ToArgb()&&decoded.GetPixel(7,7).ToArgb()==Color.Black.ToArgb(),version+" SMDH image decodes RGB565 across tile boundaries");
            service.ReplaceImage(false,decoded);check(service.Export().SequenceEqual(draft),version+" SMDH RGB565 re-encoding is stable");
            using var wrongSize=new Bitmap(25,24);reject(()=>service.ReplaceImage(false,wrongSize),version+" SMDH rejects incorrect icon dimensions");
            byte[] external=File.ReadAllBytes(path);external[6]^=1;File.WriteAllBytes(path,external);reject(()=>service.Apply(),version+" SMDH blocks external changes after opening");check(File.ReadAllBytes(path).SequenceEqual(external),version+" SMDH conflict preserves external bytes");
            service.Discard();check(!service.Dirty,version+" SMDH discard resets draft");
            reject(()=>service.Import(new byte[0x36C0]),version+" SMDH import validates magic");reject(()=>service.Import(new byte[4]),version+" SMDH import validates size");
        }
    }
}
