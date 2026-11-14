using System.IO;
using pk3DS.Core;
using ThreeDS.Progressive.Services;

/// <summary>Synthetic: the .3DS rebuild selects and validates the root ExHeader before building
/// RomFS. Covers the encrypted header left by external toolkits on fixed-key images.</summary>
internal static class RebuildExheaderChecks
{
    private const ulong UltraSun=0x00040000001B5000,UltraMoon=0x00040000001B5100;
    private static byte[] Header(ulong title,byte fill=0)
    {
        var bytes=Enumerable.Repeat(fill,0x800).ToArray();"momiji\0\0"u8.CopyTo(bytes);
        BitConverter.GetBytes(title).CopyTo(bytes,0x200);return bytes;
    }
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"rebuild-exheader-checks-"+Guid.NewGuid().ToString("N"));
        try
        {
            var usum=new GameConfig(GameVersion.USUM);var xy=new GameConfig(GameVersion.XY);
            string Case(string name,params (string File,byte[] Bytes)[] files)
            {
                string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);
                foreach(var (file,bytes) in files)File.WriteAllBytes(Path.Combine(folder,file),bytes);
                return folder;
            }
            string? Rejection(string folder,GameConfig? config)
            {
                var before=Directory.GetFiles(folder).ToDictionary(p=>p,File.ReadAllBytes);
                try{GameRomRebuildService.LocateExheader(folder,config);return null;}
                catch(InvalidDataException e){return before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value))?e.Message:"files changed";}
            }
            string valid=Case("valid",("exheader.bin",Header(UltraMoon)));
            check(GameRomRebuildService.LocateExheader(valid,usum)==Path.Combine(valid,"exheader.bin"),"Rebuild_ExheaderOwnExtractionAccepted");
            string toolkit=Case("toolkit",("DecryptedExHeader.bin",Header(UltraSun)));
            check(GameRomRebuildService.LocateExheader(toolkit,usum)==Path.Combine(toolkit,"DecryptedExHeader.bin"),"Rebuild_ExheaderDecryptedToolkitLayoutAccepted");
            // Title ID read from the fixed-key encrypted header that 3dstool leaves in place.
            string encrypted=Case("encrypted",("DecryptedExHeader.bin",Header(0x38D1B731C1460FF2,0x5A)));
            string? message=Rejection(encrypted,usum);
            check(message is not null&&message.Contains("no está descifrado")&&message.Contains("38D1B731C1460FF2")&&message.Contains("Extraer ROM")&&message.Contains("No se modificó"),
                "Rebuild_ExheaderEncryptedRejectedWithReasonAndRecovery");
            check(Rejection(encrypted,null) is not null,"Rebuild_ExheaderUnknownTitleRejectedWithoutProjectFamily");
            string zero=Case("zero",("exheader.bin",new byte[0x800]));
            check(Rejection(zero,usum)?.Contains("Title ID 0000000000000000")==true,"Rebuild_ExheaderZeroTitleRejectedInsteadOfKeyNotFound");
            message=Rejection(valid,xy);
            check(message is not null&&message.Contains("otro juego")&&message.Contains("XY"),"Rebuild_ExheaderOfAnotherFamilyRejected");
            string two=Case("two",("exheader.bin",Header(UltraMoon)),("DecryptedExHeader.bin",Header(UltraSun)));
            message=Rejection(two,usum);
            check(message is not null&&message.Contains("varios ExHeader distintos")&&message.Contains("DecryptedExHeader.bin"),"Rebuild_ExheaderAmbiguousRootRejected");
            string same=Case("same",("exheader.bin",Header(UltraMoon)),("DecryptedExHeader.bin",Header(UltraMoon)));
            check(GameRomRebuildService.LocateExheader(same,usum)==Path.Combine(same,"exheader.bin"),"Rebuild_ExheaderIdenticalCopiesPreferOwnName");
            string legacy=Case("legacy",("game.exh",Header(UltraMoon)));
            check(GameRomRebuildService.LocateExheader(legacy,usum)==Path.Combine(legacy,"game.exh"),"Rebuild_ExheaderLegacyNameStillAccepted");
            string truncated=Case("truncated",("exheader.bin",Header(UltraMoon)[..0x400]));
            check(Rejection(truncated,usum)?.Contains("incompleto")==true,"Rebuild_ExheaderTruncatedRejected");
            bool missing=false;try{GameRomRebuildService.LocateExheader(Case("missing"),usum);}catch(FileNotFoundException){missing=true;}
            check(missing,"Rebuild_ExheaderMissingRejected");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
