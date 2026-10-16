using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;
internal static class TMHMEditor6 {
    private static readonly byte[] Signature = [0xD4, 0x00, 0xAE, 0x02, 0xAF, 0x02, 0xB0, 0x02];
    internal static void GetTMHMList(out ushort[] TMs, out ushort[] HMs)
    {
        TMs = [];
        HMs = [];
        if (Main.Config.ExeFS == null) return;
        string[] files = Directory.GetFiles(Main.Config.ExeFS);
        if (!File.Exists(files[0]) || !Path.GetFileNameWithoutExtension(files[0]).Contains("code")) return;
        byte[] data = File.ReadAllBytes(files[0]);
        int dataoffset = Util.IndexOfBytes(data, Signature, 0x400000, 0) + 8;
        if (data.Length % 0x200 != 0) return;

        List<ushort> tms = [];
        List<ushort> hms = [];

        for (int i = 0; i < 92; i++) // 1-92 TMs stored sequentially
            tms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * i)));
        for (int i = 92; i < 92 + 5; i++)
            hms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * i)));
        if (Main.Config.ORAS)
        {
            hms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * 97)));
            for (int i = 98; i < 106; i++)
                tms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * i)));
            hms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * 106)));
        }
        else
        {
            for (int i = 97; i < 105; i++)
                tms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * i)));
        }

        TMs = [.. tms];
        HMs = [.. hms];
    }
}