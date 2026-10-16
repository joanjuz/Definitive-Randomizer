using System.Drawing;
using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.CTR.Images;

namespace ThreeDS.Progressive.Services;
internal static partial class GameGraphicsCodec
{
    private static int PowerOfTwo(int value){int size=1;while(size<value)size=checked(size*2);return size;}
    internal static byte[] EncodePalette(Bitmap bitmap)
    {
        if(bitmap.Width is <1 or >4096||bitmap.Height is <1 or >4096)throw new ArgumentException("Dimensiones máximas 4096 × 4096.");
        int side=Math.Max(PowerOfTwo(Math.Max(8,bitmap.Width)),PowerOfTwo(Math.Max(8,bitmap.Height)));
        var palette=new List<ushort>{0xFFFF};var colors=new Dictionary<ushort,byte>();var indices=new byte[side*side];var orienter=new XLIMOrienter(side,side,XLIMOrientation.None);
        for(uint i=0;i<indices.Length;i++)
        {
            var at=orienter.Get(i);if(at.X>=bitmap.Width||at.Y>=bitmap.Height)continue;var c=bitmap.GetPixel((int)at.X,(int)at.Y);
            if(c.A==0)continue;if(c.A!=255)throw new ArgumentException("La paleta Gen6 requiere alfa 0 o 255; usá RGBA8 para transparencias intermedias.");
            ushort packed=(ushort)((c.R>>3)<<11|(c.G>>2)<<5|(c.B>>3));
            if(!colors.TryGetValue(packed,out byte index))
            {
                if(palette.Count==70)throw new ArgumentException("La paleta Gen6 admite hasta 70 entradas, incluida la transparencia.");
                index=(byte)palette.Count;colors.Add(packed,index);palette.Add(packed);
            }
            indices[i]=index;
        }
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((ushort)2);writer.Write((ushort)palette.Count);
        foreach(ushort color in palette)writer.Write(color);
        if(palette.Count<16)for(int i=0;i<indices.Length;i+=2)writer.Write((byte)(indices[i]|indices[i+1]<<4));else writer.Write(indices);
        int padded=PowerOfTwo((int)stream.Length);while(stream.Length<padded)writer.Write((byte)0);
        writer.Write(0x4D494C43u);writer.Write((ushort)0xFEFF);writer.Write(0x14u);writer.Write((ushort)0x0202);writer.Write((uint)(padded+40));writer.Write(1u);writer.Write(0x67616D69u);writer.Write(0x10u);writer.Write((ushort)bitmap.Width);writer.Write((ushort)bitmap.Height);writer.Write(7u);writer.Write((uint)padded);
        return stream.ToArray();
    }
    private static Bitmap DecodePalette(BXLIM image)
    {
        byte[] data=image.PixelData;int count=BitConverter.ToUInt16(data,2);
        if(count is <1 or >70)throw new InvalidDataException("Cantidad de colores Gen6 inválida.");
        int side=image.BaseSize;side=Math.Max(8,side);int pixels=side*side,start=4+count*2;bool half=count<16;
        if(data.Length<start+pixels/(half?2:1))throw new InvalidDataException("Paleta Gen6 truncada.");
        var palette=new uint[count];for(int i=1;i<count;i++)palette[i]=DecodePixel(BitConverter.ToUInt16(data,4+i*2),XLIMEncoding.RGB565);
        var orienter=new XLIMOrienter(side,side,image.Orientation);byte[] bgra=new byte[image.Width*image.Height*4];
        for(uint i=0;i<pixels;i++)
        {
            int index=half?(data[start+i/2]>>((int)(i%2)*4))&15:data[start+i];if(index>=count)throw new InvalidDataException("Índice fuera de la paleta Gen6.");
            var at=orienter.Get(i);if(at.X>=image.Width||at.Y>=image.Height)continue;uint color=palette[index];int pos=(int)((at.Y*image.Width+at.X)*4);
            bgra[pos]=(byte)color;bgra[pos+1]=(byte)(color>>8);bgra[pos+2]=(byte)(color>>16);bgra[pos+3]=(byte)(color>>24);
        }
        return ImageUtil.GetBitmap(bgra,image.Width,image.Height);
    }
}
