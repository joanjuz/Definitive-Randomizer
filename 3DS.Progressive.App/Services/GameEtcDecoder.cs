using System.Buffers.Binary;
using System.Drawing;
using System.IO;
using pk3DS.Core.CTR;
using pk3DS.Core.CTR.Images;

namespace ThreeDS.Progressive.Services;
internal static class GameEtcDecoder
{
    private static readonly int[,] Modifiers = {{2,8,-2,-8},{5,17,-5,-17},{9,29,-9,-29},{13,42,-13,-42},{18,60,-18,-60},{24,80,-24,-80},{33,106,-33,-106},{47,183,-47,-183}};
    private static int Signed3(int value) => value >= 4 ? value - 8 : value;
    internal static Color Pixel(ulong block, ulong alpha, int x, int y, bool hasAlpha)
    {
        uint high = (uint)(block >> 32), low = (uint)block; bool differential = (high & 2) != 0, flip = (high & 1) != 0;
        int half = (flip ? y : x) >= 2 ? 1 : 0;
        int Rgb(int shift)
        {
            int packed = (int)(high >> shift) & 255;
            if (!differential) return ((half == 0 ? packed >> 4 : packed & 15) * 17);
            int value = packed >> 3; if (half != 0) value += Signed3(packed & 7);
            if (value is < 0 or > 31) throw new InvalidDataException("Bloque ETC1 diferencial inválido.");
            return (value << 3) | (value >> 2);
        }
        int pixel = x * 4 + y, selector = (int)((low >> pixel) & 1) | (int)((low >> (pixel + 16)) & 1) << 1;
        int table = (int)(high >> (half == 0 ? 5 : 2)) & 7, modifier = Modifiers[table, selector];
        return Color.FromArgb(hasAlpha ? (int)((alpha >> (pixel * 4)) & 15) * 17 : 255, Math.Clamp(Rgb(24) + modifier,0,255), Math.Clamp(Rgb(16) + modifier,0,255), Math.Clamp(Rgb(8) + modifier,0,255));
    }
    internal static Bitmap Decode(BXLIM image)
    {
        bool alpha = image.Format == XLIMEncoding.ETC1A4;
        var orienter = new XLIMOrienter(image.Width,image.Height,image.Orientation);
        int pixels = checked((int)(orienter.Width * orienter.Height)), bytes = checked(pixels / (alpha ? 1 : 2));
        if (image.PixelData.Length != bytes) throw new InvalidDataException("El tamaño de los bloques ETC1 no coincide con las dimensiones.");
        byte[] bgra = new byte[checked(image.Width * image.Height * 4)];
        int offset = 0;
        for (uint tile = 0; tile < pixels / 64; tile++) for (uint block = 0; block < 4; block++)
        {
            ulong alphas = alpha ? BinaryPrimitives.ReadUInt64LittleEndian(image.PixelData.AsSpan(offset,8)) : ulong.MaxValue;
            if (alpha) offset += 8;
            ulong colors = BinaryPrimitives.ReadUInt64LittleEndian(image.PixelData.AsSpan(offset,8)); offset += 8;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                uint morton = (uint)((x & 1) | ((y & 1) << 1) | ((x & 2) << 1) | ((y & 2) << 2));
                var coordinate = orienter.Get(tile * 64 + block * 16 + morton);
                if (coordinate.X >= image.Width || coordinate.Y >= image.Height) continue;
                Color color = Pixel(colors,alphas,x,y,alpha);int at = checked((int)((coordinate.Y * image.Width + coordinate.X) * 4));
                bgra[at]=color.B;bgra[at+1]=color.G;bgra[at+2]=color.R;bgra[at+3]=color.A;
            }
        }
        return pk3DS.Core.ImageUtil.GetBitmap(bgra,image.Width,image.Height);
    }
}
