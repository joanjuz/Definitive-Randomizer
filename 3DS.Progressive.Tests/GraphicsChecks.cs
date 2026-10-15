using System.Drawing;
using ThreeDS.Progressive.Services;
internal static class GraphicsChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var image=new Bitmap(17,25);
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)image.SetPixel(x,y,Color.FromArgb(255,x*13,y*9,(x+y)*5));
        byte[] encoded=GameGraphicsCodec.EncodeRgba8(image);using var decoded=GameGraphicsCodec.Decode(encoded);
        check(decoded.Width==17&&decoded.Height==25,"BCLIM RGBA8 preserves nonsquare cropped dimensions");
        check(Enumerable.Range(0,17*25).All(index=>image.GetPixel(index%17,index/17).ToArgb()==decoded.GetPixel(index%17,index/17).ToArgb()),"BCLIM RGBA8 preserves every opaque color across tiled padded image");
        check(encoded.AsSpan(encoded.Length-40,4).SequenceEqual("CLIM"u8),"BCLIM output contains original footer format");

        using var small=new Bitmap(9,7);
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)small.SetPixel(x,y,Color.FromArgb((x*29+y*13)%256,x*28,y*40,(x+y)*15));
        foreach(var format in Enum.GetValues<pk3DS.Core.CTR.XLIMEncoding>())foreach(bool flim in new[]{false,true})
        {
            byte[] data=GameGraphicsCodec.Encode(small,format,flim);using var back=GameGraphicsCodec.Decode(data);
            check(back.Width==9&&back.Height==7,$"{format} {(flim?"BFLIM":"BCLIM")} padded dimensions");
            check(data.AsSpan(data.Length-40,4).SequenceEqual(flim?"FLIM"u8:"CLIM"u8),$"{format} selected container footer");
            for(int y=0;y<7;y++)for(int x=0;x<9;x++)
            {
                var source=small.GetPixel(x,y);var actual=back.GetPixel(x,y);
                if(format==pk3DS.Core.CTR.XLIMEncoding.RGBA8)check(source.ToArgb()==actual.ToArgb(),"RGBA8 preserves transparent RGB and alpha");
                if(format==pk3DS.Core.CTR.XLIMEncoding.A4)check(actual.A==source.A/17*17,"A4 preserves independently packed adjacent alpha");
                if(format==pk3DS.Core.CTR.XLIMEncoding.LA4)check(actual.A==source.A/17*17&&actual.R==actual.G&&actual.G==actual.B,"LA4 expands both channels to eight bits");
                if(format==pk3DS.Core.CTR.XLIMEncoding.ETC1A4)check(Math.Abs(source.A-actual.A)<=8,"ETC1A4 preserves rounded alpha independently of RGB compression");
                if(format==pk3DS.Core.CTR.XLIMEncoding.L4)check(actual.R==((source.R*299+source.G*587+source.B*114+500)/1000)/17*17,"L4 preserves independently packed adjacent luminance");
            }
            var corrupt=(byte[])data.Clone();corrupt[^4]^=1;reject(()=>GameGraphicsCodec.Decode(corrupt),"Reject inconsistent pixel length");
        }
        using var constant=new Bitmap(8,8);
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)constant.SetPixel(x,y,Color.FromArgb(119,68,136,204));
        using var etc=GameGraphicsCodec.Decode(GameGraphicsCodec.Encode(constant,pk3DS.Core.CTR.XLIMEncoding.ETC1A4));
        check(Enumerable.Range(0,64).All(i=>{var c=etc.GetPixel(i%8,i/8);return c.A==119&&Math.Abs(c.R-68)<=3&&Math.Abs(c.G-136)<=3&&Math.Abs(c.B-204)<=3;}),"Managed ETC compressor preserves solid color with bounded error across four blocks");
        foreach(int count in new[]{3,20,69})
        {
            using var pal=new Bitmap(9,25);
            for(int y=0;y<25;y++)for(int x=0;x<9;x++){int n=(x+y*9)%count;pal.SetPixel(x,y,n==0?Color.Transparent:Color.FromArgb(255,(n%32)*8,((n/32)%64)*4,(n%31)*8));}
            byte[] data=GameGraphicsCodec.EncodePalette(pal);using var back=GameGraphicsCodec.Decode(data);
            check(back.Width==9&&back.Height==25,"Gen6 palette preserves nonsquare dimensions and square padded tile stride");
            check(Enumerable.Range(0,225).All(i=>{var a=pal.GetPixel(i%9,i/9);var b=back.GetPixel(i%9,i/9);return a.A==b.A&&(a.A==0||Math.Abs(a.R-b.R)<=7&&Math.Abs(a.G-b.G)<=3&&Math.Abs(a.B-b.B)<=7);}),"Gen6 4/8 bit palette preserves every index, transparent pixel and quantized color");
            var bad=(byte[])data.Clone();bad[4+BitConverter.ToUInt16(data,2)*2]=255;reject(()=>GameGraphicsCodec.Decode(bad),"Gen6 palette rejects invalid indices");
        }
        using var tooMany=new Bitmap(16,8);for(int y=0;y<8;y++)for(int x=0;x<16;x++)tooMany.SetPixel(x,y,Color.FromArgb(255,x*8,y*8,0));
        reject(()=>GameGraphicsCodec.EncodePalette(tooMany),"Gen6 palette rejects excess colors instead of dropping entries");
        using var partial=new Bitmap(8,8);partial.SetPixel(0,0,Color.FromArgb(127,1,2,3));reject(()=>GameGraphicsCodec.EncodePalette(partial),"Gen6 palette rejects intermediate transparency");
        reject(()=>GameGraphicsCodec.Decode(new byte[39]),"Graphics reject truncated footer");reject(()=>GameGraphicsCodec.Decode(new byte[40]),"Graphics reject unknown footer");
        var invalid=(byte[])encoded.Clone();invalid[^12]=0;invalid[^11]=0;reject(()=>GameGraphicsCodec.Decode(invalid),"Graphics reject invalid dimensions before decode");
    }
}
