using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.CTR.Images;
using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services;

internal static class UsumMintIconGraphics
{
    // Preserve the game's 32x32 RGB5A1, Morton tiling and Rotate90 orientation.
    // The visible 24x24 area matches the scale of the existing bag item artwork.
    internal static byte[] ConvertPng(byte[] png)
    {
        if (png.Length is <33 or >262144 || !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("Se requiere un PNG válido de hasta 256 KiB.");
        int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        int height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        if (width is <1 or >512 || height is <1 or >512) throw new InvalidDataException("Dimensiones de icono fuera de rango.");
        using var stream=new MemoryStream(png);using var input=new Bitmap(stream);
        int left=input.Width,top=input.Height,right=-1,bottom=-1;
        for (int y=0;y<input.Height;y++) for(int x=0;x<input.Width;x++)
            if(input.GetPixel(x,y).A>0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        if(right<left)throw new InvalidDataException("El icono no contiene píxeles visibles.");
        int cropWidth=right-left+1,cropHeight=bottom-top+1;
        double scale=Math.Min(24d/cropWidth,24d/cropHeight);
        int w=Math.Max(1,(int)Math.Round(cropWidth*scale)),h=Math.Max(1,(int)Math.Round(cropHeight*scale));
        using var canvas=new Bitmap(32,32,PixelFormat.Format32bppArgb);
        // Integer area filtering with premultiplied alpha: deterministic on all
        // supported Windows hosts, with no white fringe from transparent pixels.
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            long a=0,r=0,g=0,b=0,weight=0;
            for(int sy=y*cropHeight/h;sy<((y+1)*cropHeight+h-1)/h;sy++)
            for(int sx=x*cropWidth/w;sx<((x+1)*cropWidth+w-1)/w;sx++)
            {
                int dx=Math.Min((sx+1)*w,(x+1)*cropWidth)-Math.Max(sx*w,x*cropWidth);
                int dy=Math.Min((sy+1)*h,(y+1)*cropHeight)-Math.Max(sy*h,y*cropHeight);
                long area=(long)dx*dy;var color=input.GetPixel(left+sx,top+sy);
                weight+=area;a+=color.A*area;r+=color.R*color.A*area;g+=color.G*color.A*area;b+=color.B*color.A*area;
            }
            if(a!=0)canvas.SetPixel((32-w)/2+x,(32-h)/2+y,Color.FromArgb((int)((a+weight/2)/weight),
                (int)((r+a/2)/a),(int)((g+a/2)/a),(int)((b+a/2)/a)));
        }
        return GameGraphicsCodec.Encode(canvas,XLIMEncoding.RGB5A1,true,XLIMOrientation.Rotate90);
    }
    internal static byte[] Compress(byte[] texture)
    {
        string input=Path.Combine(Path.GetTempPath(),"dgmint-icon-"+Guid.NewGuid().ToString("N"));
        string output=input+".lz";
        try
        {
            File.WriteAllBytes(input,texture);LZSS.Compress(input,output);byte[] result=File.ReadAllBytes(output);
            if(!Decompress(result).AsSpan().SequenceEqual(texture))throw new InvalidDataException("La compresión del icono no supera la relectura.");
            return result;
        }
        finally{if(File.Exists(input))File.Delete(input);if(File.Exists(output))File.Delete(output);}
    }
    internal static byte[] Decompress(byte[] record)
    {
        if(record.Length<4||record[0]!=0x11||record[1]!=0x28||record[2]!=8||record[3]!=0)
            throw new InvalidDataException("El icono no tiene el perfil LZ11 de 2088 bytes.");
        using var result=new MemoryStream();
        try{LZSS.Decompress(new MemoryStream(record),record.Length,result);}
        catch(TooMuchInputException ex){throw new InvalidDataException("El icono contiene datos comprimidos adicionales desconocidos.",ex);}
        byte[] texture=result.ToArray();
        if(texture.Length!=2088)throw new InvalidDataException("Tamaño descomprimido de icono incompatible.");
        var image=new BFLIM(texture);
        if(!image.Valid||image.Width!=32||image.Height!=32||image.Format!=XLIMEncoding.RGB5A1||image.Orientation!=XLIMOrientation.Rotate90)
            throw new InvalidDataException("Se requiere BFLIM 32×32 RGB5A1 Rotate90.");
        using var decoded=GameGraphicsCodec.Decode(texture);
        return texture;
    }
    internal static byte[][] ReadArchive(byte[] source,int count)
    {
        // Reject vectorized/unknown layouts rather than silently flattening them.
        if(source.Length<0x24||!source.AsSpan(0,4).SequenceEqual("CRAG"u8)||BitConverter.ToInt32(source,4)!=0x24||
            BitConverter.ToUInt16(source,8)!=0xFEFF||BitConverter.ToUInt16(source,10)!=GARC.VER_6||
            BitConverter.ToUInt32(source,12)!=4||BitConverter.ToInt32(source,20)!=source.Length||BitConverter.ToUInt32(source,32)!=4)
            throw new InvalidDataException("GARC de iconos USUM fuera del perfil reconocido.");
        int fatoSize=12+4*count,fatbAt=0x24+fatoSize,fatbSize=12+16*count,dataAt=fatbAt+fatbSize+12;
        if(source.Length<dataAt||BitConverter.ToInt32(source,16)!=dataAt||
            !source.AsSpan(0x24,4).SequenceEqual("OTAF"u8)||BitConverter.ToInt32(source,0x28)!=fatoSize||
            BitConverter.ToUInt16(source,0x2c)!=count||BitConverter.ToUInt16(source,0x2e)!=0xffff||
            !source.AsSpan(fatbAt,4).SequenceEqual("BTAF"u8)||BitConverter.ToInt32(source,fatbAt+4)!=fatbSize||BitConverter.ToInt32(source,fatbAt+8)!=count||
            !source.AsSpan(dataAt-12,4).SequenceEqual("BMIF"u8)||BitConverter.ToInt32(source,dataAt-8)!=12||BitConverter.ToInt32(source,dataAt-4)!=source.Length-dataAt)
            throw new InvalidDataException("Tablas del GARC de iconos incompatibles; no se modifica.");
        int previousEnd=0;
        for(int i=0;i<count;i++)
        {
            int at=fatbAt+12+i*16;
            int start=BitConverter.ToInt32(source,at+4),end=BitConverter.ToInt32(source,at+8),length=BitConverter.ToInt32(source,at+12);
            if(BitConverter.ToInt32(source,0x30+i*4)!=i*16||BitConverter.ToUInt32(source,at)!=1||start!=previousEnd||
                start<0||length<4||end<start||(long)end!=start+(((long)length+3)&~3L)||(long)dataAt+end>source.Length)
                throw new InvalidDataException("Entradas gráficas vectorizadas, solapadas o desconocidas.");
            previousEnd=end;
        }
        if(dataAt+previousEnd!=source.Length)throw new InvalidDataException("Datos adicionales fuera de las entradas gráficas.");
        return new GARC.MemGARC(source).Files;
    }
    internal static byte[] Append(byte[] original,IReadOnlyList<byte[]> textures)
    {
        var records=ReadArchive(original,UsumMintIconLayout.OriginalImageCount);
        if(textures.Count!=21)throw new InvalidDataException("Se requieren exactamente 21 iconos.");
        var outputRecords=records.Concat(textures.Select(Compress)).ToArray();
        string temp=Path.Combine(Path.GetTempPath(),"dgmint-icons-"+Guid.NewGuid().ToString("N"));
        try
        {
            new GARCFile(GARC.PackGARC(outputRecords,GARC.VER_6,4),GARCReference.GARCReference_US.Single(r=>r.Name=="itemicons"),temp).Save();
            byte[] output=File.ReadAllBytes(temp);var verified=ReadArchive(output,UsumMintIconLayout.InstalledImageCount);
            if(!outputRecords.Select((bytes,i)=>bytes.AsSpan().SequenceEqual(verified[i])).All(match=>match))
                throw new InvalidDataException("No coinciden los registros gráficos tras reconstruir.");
            return output;
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
