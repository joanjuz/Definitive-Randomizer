using System.IO;
using System.Drawing;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.CTR.Images;

namespace ThreeDS.Progressive.Services;
internal static partial class GameGraphicsCodec
{
    internal static Bitmap Decode(byte[] data)
    {
        if(data.Length<40||data.Length>128*1024*1024)throw new InvalidDataException("Tamaño gráfico incompatible.");
        uint magic=BitConverter.ToUInt32(data,data.Length-40);BXLIM image=magic switch
        {0x4D494C43=>new BCLIM(data),0x4D494C46=>new BFLIM(data),_=>throw new InvalidDataException("El archivo no tiene un footer BCLIM/BFLIM válido.")};
        if(!image.Valid||image.Width==0||image.Height==0||image.Width>4096||image.Height>4096||!Enum.IsDefined(image.Format))throw new InvalidDataException("Encabezado gráfico incompatible o dimensiones fuera de rango.");
        if(BitConverter.ToUInt32(data,data.Length-28)!=data.Length||BitConverter.ToUInt32(data,data.Length-4)!=image.PixelData.Length)throw new InvalidDataException("Longitudes del gráfico incompatibles.");
        if(image.Format is XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4)return GameEtcDecoder.Decode(image);
        // Palette images have their own variable length layout.
        if(magic==0x4D494C43&&image.Format==XLIMEncoding.RGB5A1&&image.PixelData.Length>=4&&BitConverter.ToUInt16(image.PixelData,0)==2)return DecodePalette(image);
        var orienter=new XLIMOrienter(image.Width,image.Height,image.Orientation);
        int pixels=checked((int)(orienter.Width*orienter.Height)),bpp=image.Format.GetBitsPerPixel();
        if(image.PixelData.Length!=pixels*bpp/8)throw new InvalidDataException("El tamaño de los píxeles no coincide con las dimensiones.");
        byte[] bgra=new byte[image.Width*image.Height*4];
        for(uint i=0;i<pixels;i++)
        {
            var c=orienter.Get(i);if(c.X>=image.Width||c.Y>=image.Height)continue;
            uint value=bpp switch{4=>(uint)((image.PixelData[i/2]>>((int)(i%2)*4))&15),24=>(uint)(image.PixelData[i*3]|image.PixelData[i*3+1]<<8|image.PixelData[i*3+2]<<16),_=>PixelConverter.GetEncodedPixelValue(image.PixelData,(int)i*bpp/8,bpp)};
            uint argb;
            if(image.Format==XLIMEncoding.L4){uint l=value*17;argb=0xFF000000|l<<16|l<<8|l;}
            else if(image.Format==XLIMEncoding.A4)argb=value*17<<24|0xFFFFFF;
            else if(image.Format==XLIMEncoding.LA4){uint l=(value>>4)*17;argb=(value&15)*17<<24|l<<16|l<<8|l;}
            else
            {
                // Decode one pixel with the original format definitions, including 24 bit RGB.
                argb=DecodePixel(value,image.Format);
            }
            int at=(int)((c.Y*image.Width+c.X)*4);bgra[at]=(byte)argb;bgra[at+1]=(byte)(argb>>8);bgra[at+2]=(byte)(argb>>16);bgra[at+3]=(byte)(argb>>24);
        }
        return ImageUtil.GetBitmap(bgra,image.Width,image.Height);
    }
    private static uint DecodePixel(uint v,XLIMEncoding f)
    {
        uint Expand(uint n)=>(n<<3)|(n>>2);
        return f switch
        {
            XLIMEncoding.L8=>0xFF000000|v<<16|v<<8|v,XLIMEncoding.A8=>v<<24|0xFFFFFF,
            XLIMEncoding.LA8=>(v&255)<<24|(v>>8)*0x010101,
            XLIMEncoding.HILO8=>0xFF0000FF|(v>>8)<<16|(v&255)<<8,
            XLIMEncoding.RGBX8=>0xFF000000|v,
            XLIMEncoding.RGBA8=>(v&255)<<24|(v>>8),
            XLIMEncoding.RGBA4=>(v&15)*17<<24|((v>>12)&15)*17<<16|((v>>8)&15)*17<<8|((v>>4)&15)*17,
            XLIMEncoding.RGB565=>0xFF000000|Expand(v>>11)<<16|(((v>>5)&63)*255/63)<<8|Expand(v&31),
            XLIMEncoding.RGB5A1=>(v&1)*255<<24|Expand(v>>11)<<16|Expand((v>>6)&31)<<8|Expand((v>>1)&31),
            _=>throw new InvalidDataException("Formato gráfico incompatible.")
        };
    }
    // Title layouts are allocated for their original texture format and size.
    // Keep the complete original footer, including version, alignment and orientation.
    internal static byte[] EncodeLike(Bitmap bitmap,byte[] original)
    {
        using var decoded=Decode(original);
        if(bitmap.Width!=decoded.Width||bitmap.Height!=decoded.Height)throw new InvalidDataException("La imagen debe conservar las dimensiones del título.");
        bool bflim=original.AsSpan(original.Length-40,4).SequenceEqual("FLIM"u8);
        BXLIM image=bflim?new BFLIM(original):new BCLIM(original);
        byte[] encoded=Encode(bitmap,image.Format,bflim,image.Orientation);
        if(encoded.Length!=original.Length)throw new InvalidDataException("La textura no conserva el tamaño original; no se guardará.");
        original.AsSpan(original.Length-40).CopyTo(encoded.AsSpan(encoded.Length-40));
        using var check=Decode(encoded);
        return encoded;
    }
    internal static byte[] EncodeRgba8(Bitmap bitmap)=>Encode(bitmap,XLIMEncoding.RGBA8);
    internal static byte[] Encode(Bitmap bitmap,XLIMEncoding format,bool bflim=false,XLIMOrientation orientation=XLIMOrientation.None)
    {
        if(bitmap.Width is <1 or >4096||bitmap.Height is <1 or >4096||!Enum.IsDefined(format))throw new ArgumentException("Formato o dimensiones incompatibles (máximo 4096 × 4096).");
        var orienter=new XLIMOrienter(bitmap.Width,bitmap.Height,orientation);
        int pixels=checked((int)(orienter.Width*orienter.Height));
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        if(format is XLIMEncoding.ETC1 or XLIMEncoding.ETC1A4)writer.Write(GameEtcEncoder.Encode(bitmap,orienter,format==XLIMEncoding.ETC1A4));
        else
        {
            int bpp=format.GetBitsPerPixel();byte[] raw=new byte[pixels*bpp/8];
            for(uint i=0;i<pixels;i++)
            {
                var at=orienter.Get(i);Color c=at.X<bitmap.Width&&at.Y<bitmap.Height?bitmap.GetPixel((int)at.X,(int)at.Y):Color.Transparent;
                uint l=(uint)((c.R*299+c.G*587+c.B*114+500)/1000);
                uint value=format switch
                {
                    XLIMEncoding.L8=>l,XLIMEncoding.A8=>c.A,XLIMEncoding.LA4=>(l/17)<<4|(uint)(c.A/17),XLIMEncoding.LA8=>l<<8|c.A,
                    XLIMEncoding.HILO8=>(uint)c.R<<8|c.G,XLIMEncoding.RGB565=>(uint)(c.R>>3)<<11|(uint)(c.G>>2)<<5|(uint)(c.B>>3),
                    XLIMEncoding.RGBX8=>(uint)c.R<<16|(uint)c.G<<8|c.B,XLIMEncoding.RGB5A1=>(uint)(c.R>>3)<<11|(uint)(c.G>>3)<<6|(uint)(c.B>>3)<<1|(uint)(c.A>=128?1:0),
                    XLIMEncoding.RGBA4=>(uint)(c.R/17)<<12|(uint)(c.G/17)<<8|(uint)(c.B/17)<<4|(uint)(c.A/17),
                    XLIMEncoding.RGBA8=>(uint)c.R<<24|(uint)c.G<<16|(uint)c.B<<8|c.A,XLIMEncoding.L4=>l/17,XLIMEncoding.A4=>(uint)(c.A/17),_=>throw new ArgumentException("Formato incompatible.")
                };
                if(bpp==4)raw[i/2]|=(byte)(value<<((int)(i%2)*4));
                else for(int b=0;b<bpp/8;b++)raw[(int)i*bpp/8+b]=(byte)(value>>(b*8));
            }
            if(!bflim&&format==XLIMEncoding.RGB5A1&&BitConverter.ToUInt16(raw,0)==2)throw new ArgumentException("El primer píxel coincide con el marcador de paleta Gen6; usá BFLIM o RGBA8.");
            writer.Write(raw);
        }
        int length=(int)stream.Length;
        writer.Write(bflim?0x4D494C46u:0x4D494C43u);writer.Write((ushort)0xFEFF);
        if(bflim){writer.Write((ushort)0x14);writer.Write(0x02020000u);}else{writer.Write(0x14u);writer.Write((ushort)0x0202);}
        writer.Write((uint)(length+40));writer.Write(1u);writer.Write(0x67616D69u);writer.Write(0x10u);writer.Write((ushort)bitmap.Width);writer.Write((ushort)bitmap.Height);
        if(bflim){writer.Write((ushort)0x80);writer.Write((byte)format);writer.Write((byte)orientation);}else{writer.Write((byte)format);writer.Write((byte)orientation);writer.Write((ushort)0);}
        writer.Write((uint)length);return stream.ToArray();
    }
}
