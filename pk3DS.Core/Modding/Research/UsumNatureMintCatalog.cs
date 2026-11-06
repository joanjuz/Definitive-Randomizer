using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace pk3DS.Core.Modding.Research;

public sealed record UsumNatureMint(int ItemId, int NatureId, string EnglishNature)
{
    public string Identity => "mint:" + NatureId;
}

public sealed record UsumNatureMintProfile(string Id, string CodeSha256, string BagSha256, bool Moon)
{
    public IReadOnlyDictionary<string,string> TextSlotHashes {get;init;} = UsumNatureMintTextProfile.Hashes;
}

public static class UsumNatureMintCatalog
{
    public const string ReferenceCommit = "274d836e88f840c9c326a569198ebff8031969d6";
    public static IReadOnlyList<UsumNatureMint> Mints { get; } = Array.AsReadOnly(new[] {
        new UsumNatureMint(113,1,"Lonely"),new(114,3,"Adamant"),new(115,4,"Naughty"),new(123,2,"Brave"),
        new(124,5,"Bold"),new(125,8,"Impish"),new(126,9,"Lax"),new(127,7,"Relaxed"),
        new(128,15,"Modest"),new(129,16,"Mild"),new(130,19,"Rash"),new(131,17,"Quiet"),
        new(132,20,"Calm"),new(133,21,"Gentle"),new(426,23,"Careful"),new(427,22,"Sassy"),
        new(622,10,"Timid"),new(837,11,"Hasty"),new(838,13,"Jolly"),new(839,14,"Naive"),new(840,12,"Serious") });
    private const string BagHash = "BD97393E3440A04CA6F61C98771C11E595C4EE649F9C4985E793A81E7721155C";
    public static IReadOnlyList<UsumNatureMintProfile> Profiles { get; } = Array.AsReadOnly(new[] {
        new UsumNatureMintProfile("US-code-d2e9b4d9", "D2E9B4D91D7B14BCEF0C34D869C1DA60EF85D98B1DF3C34CBC42E42F54CFBA27",BagHash,false),
        new UsumNatureMintProfile("UM-code-a6aa17b6", "A6AA17B6F409264F6ED15D51735E9638D3F44896C0CEC0F05C39940B79FE30E7",BagHash,true) });
    public static UsumNatureMintProfile IdentifyCode(byte[] code) => Profiles.SingleOrDefault(p =>
        Convert.ToHexString(SHA256.HashData(UsumMintIconLayout.NormalizeCode(code))) == p.CodeSha256) ?? throw new InvalidDataException(
        "La revisión de code.bin no está acreditada para mentas. Se requiere una de las dos huellas US/UM documentadas; no se aplican offsets aproximados.");
    public static string OriginalItemHash(int id) => id switch {
        113 or 114 or 115 => "8E9DB2EEB35F843F2A351998BEE1752A4E52BA38CD9E430469071CB7F5E69017",
        126 => "6DB65FD59FD356F6729140571B5BCD6BB3B83492A16E1BF0A3884442FC3C8A0E",
        622 => "C9AE86408C8639A016F217E40BE631CBA63984E7242A0F614E0E6EE7739896CB",
        >=837 and <=840 => "71AB318D7647872728741B62449B3C72F004B20C9B8D286A285EE068F5AC8D36",
        _ when Mints.Any(m=>m.ItemId==id) => "D30FA778BF3D050B5F03D5579ECB1592C15A629DF0DFAFF32D8D1D16435A054E",
        _ => throw new ArgumentOutOfRangeException(nameof(id)) };
}

public static class UsumItemPocketTable
{
    public static int Read(byte[] table,int item) { Validate(table,item);return (item&1)==0?table[item/2]>>4:table[item/2]&15; }
    public static void Write(byte[] table,int item,int pocket) {
        Validate(table,item);if(pocket is <0 or >15)throw new ArgumentOutOfRangeException(nameof(pocket));
        int at=item/2;table[at]=(byte)((item&1)==0?(table[at]&15)|(pocket<<4):(table[at]&240)|pocket);
    }
    private static void Validate(byte[] table,int item) {
        if(table.Length!=480||item is <0 or >=960)throw new InvalidDataException("Tabla de bolsillos USUM o ID fuera de límites (960 registros).");
    }
}
