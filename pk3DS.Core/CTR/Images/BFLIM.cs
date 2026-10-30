using System;
using System.IO;
using pk3DS.Core.CTR.Images;

namespace pk3DS.Core.CTR;

public class BFLIM : BXLIM
{
    public BFLIM(Stream data) => ReadBFLIM(data);

    public BFLIM(byte[] data)
    {
        using var ms = new MemoryStream(data);
        ReadBFLIM(ms);
    }

    public BFLIM(string path)
    {
        var data = File.ReadAllBytes(path);
        using var ms = new MemoryStream(data);
        ReadBFLIM(ms);
    }

    private void ReadBFLIM(Stream ms)
    {
        PixelData = new byte[ms.Length - FLIMHeader.SIZE];
        ms.ReadAtLeast(PixelData.AsSpan(0, PixelData.Length), PixelData.Length, throwOnEndOfStream: false);
        var footer = new byte[FLIMHeader.SIZE];
        ms.ReadAtLeast(footer.AsSpan(0, footer.Length), footer.Length, throwOnEndOfStream: false);
        Footer = footer.ToStructure<FLIMHeader>();
    }
}