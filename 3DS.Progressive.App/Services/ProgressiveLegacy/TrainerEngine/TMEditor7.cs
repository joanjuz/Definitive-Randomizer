using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;
internal static class TMEditor7 {
    private static readonly byte[] Signature = [0x03, 0x40, 0x03, 0x41, 0x03, 0x42, 0x03, 0x43, 0x03]; // tail end of item::ITEM_CheckBeads
    internal static ushort[] GetTMHMList()
    {
        if (Main.Config.ExeFS == null)
            return [];
        string[] files = Directory.GetFiles(Main.Config.ExeFS);
        if (!File.Exists(files[0]) || !Path.GetFileNameWithoutExtension(files[0]).Contains("code"))
            return [];
        byte[] data = File.ReadAllBytes(files[0]);
        int dataoffset = Util.IndexOfBytes(data, Signature, 0x400000, 0) + Signature.Length;
        if (data.Length % 0x200 != 0)
            return [];

        if (Main.Config.USUM)
            dataoffset += 0x22;
        List<ushort> tms = [];

        for (int i = 0; i < 100; i++) // TMs stored sequentially
            tms.Add(BitConverter.ToUInt16(data, dataoffset + (2 * i)));
        return [.. tms];
    }
}