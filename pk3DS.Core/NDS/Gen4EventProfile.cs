#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// Where one Gen 4 game keeps its map and event tables, and the fingerprints of the original data the map editor accepts. The record layouts (event members
/// of four blocks of 20/32/12/16-byte records, 24-byte map headers, matrices, land_data) are the same in Diamond and Platinum (pret/pokediamond
/// <c>include/map_object.h</c> and pret/pokeplatinum <c>include/struct_defs/events.h</c>; both dumps parse with no trailing byte); what differs is listed here.
/// Every value below was measured on the owner's dump of that exact game and revision; there is no profile for a game or revision that was not measured.
/// </summary>
public sealed record Gen4EventProfile(
    string GameCode, byte Revision, string GameName,
    string EventPath, string ScriptPath, string LandDataPath,
    int HeaderOffset, int HeaderCount, int EventFileCount, int ScriptFileCount, int LandFileCount,
    uint Arm9Crc32, uint HeadersCrc32, uint NamesCrc32, uint MatricesCrc32, uint ScriptsCrc32, uint EventsCrc32, uint LandDataCrc32,
    uint AreaDataCrc32, uint AreaBuildCrc32, uint AreaBuildTexturesCrc32, uint MapTexturesCrc32, uint BuildModelCrc32,
    int LocationNamesText, int ItemBallScriptArchive, int HiddenItemScriptArchive, int MaxObjectFacing,
    string NormalizedEventsSha256, string EditableEventsSha256, string EventsEditableSha256,
    bool SupportsNpcCreation)
{
    public const string NamesPath = "fielddata/maptable/mapname.bin", MatrixPath = "fielddata/mapmatrix/map_matrix.narc";
    public string Key => $"{GameCode}/{Revision}";

    // ---- Values that differ in HeartGold. Platinum and Diamond keep the defaults, which are the values the editor always used for them.
    /// <summary>Where each field sits in a 24-byte map header (Sinnoh: Diamond and Platinum; Johto: HeartGold).</summary>
    public Gen4HeaderLayout HeaderLayout { get; init; } = Gen4HeaderLayout.Sinnoh;
    /// <summary>Internal map names (16 ASCII bytes per header) and map matrices of this game.</summary>
    public string MapNamesPath { get; init; } = NamesPath;
    public string MapMatrixPath { get; init; } = MatrixPath;
    /// <summary>Graphics archives: area data, area model lists, area model textures, map textures and prop models.</summary>
    public string AreaDataPath { get; init; } = "fielddata/areadata/area_data.narc";
    public string AreaBuildPath { get; init; } = "fielddata/areadata/area_build_model/area_build.narc";
    public string AreaBuildTexturesPath { get; init; } = "fielddata/areadata/area_build_model/areabm_texset.narc";
    public string MapTexturesPath { get; init; } = "fielddata/areadata/area_map_tex/map_tex_set.narc";
    public string BuildModelPath { get; init; } = "fielddata/build_model/build_model.narc";
    /// <summary>
    /// HeartGold only: the prop models of indoor areas (area byte +6 = 0) come from this archive instead of <see cref="BuildModelPath"/>; null where every
    /// area uses one archive. The engine opens one or the other in AreaDataManager_Alloc (pret/pokeheartgold, overlay_01_021FB878.s).
    /// </summary>
    public string? IndoorBuildModelPath { get; init; }
    public uint IndoorBuildModelCrc32 { get; init; }
    /// <summary>
    /// HeartGold land_data has a background-sound section before the terrain attributes: after the four section sizes, u16 0x1234 and u16 its length.
    /// </summary>
    public bool LandDataHasSoundSection { get; init; }
    /// <summary>Largest side of a map matrix, in blocks, in this game's original data (30 in Diamond and Platinum; 47 in HeartGold: its overworld is 47 × 17).</summary>
    public int MaxMatrixSide { get; init; } = PlatinumMapLayout.MaxDimension;
    /// <summary>Largest sight range of a trainer in this game's original data (6 in Diamond and Platinum, 7 in HeartGold). Larger values are not accredited.</summary>
    public int MaxTrainerSight { get; init; } = PlatinumTrainerVision.MaxAccreditedSight;
    /// <summary>
    /// HeartGold places item balls as normal NPC (type 0) with an item-ball script (7000–7999); Diamond and Platinum give them type 3. Either way the editor
    /// treats them as ground objects: shown with the objects and never moved or turned.
    /// </summary>
    public bool ItemBallsAreScriptedNpc { get; init; }
    /// <summary>Whether <paramref name="npc"/> is a ground item of this game (type 3, or an item-ball script where the game uses one).</summary>
    public bool IsGroundItem(PlatinumEvent npc) => npc.Kind == PlatinumEventKind.Npc && (npc.Type == 3 || ItemBallsAreScriptedNpc && npc.Type == 0 && npc.Script is >= 7000 and < 8000);
    /// <summary>The sight range this game accepts for <paramref name="npc"/>, or why it does not (see <see cref="PlatinumTrainerVision.SightRefusal(PlatinumEvent, int)"/>).</summary>
    public string? SightRefusal(PlatinumEvent npc) => ItemBallsAreScriptedNpc && IsGroundItem(npc) ? "es un objeto de suelo (Poké Ball con script de objeto)" : PlatinumTrainerVision.SightRefusal(npc, MaxTrainerSight);
    /// <summary>Short label for titles and messages, e.g. "Platino CPUE/1".</summary>
    public string Label => $"{GameName} {Key}";

    /// <summary>Pokémon Platinum USA rev. 1 (the reference profile of the editor since phase 1).</summary>
    public static Gen4EventProfile Platinum { get; } = new("CPUE", 1, "Platino",
        "fielddata/eventdata/zone_event.narc", "fielddata/script/scr_seq.narc", "fielddata/land_data/land_data.narc",
        0xE601C, 593, 534, 1124, 666,
        0x4D104949, 0xBB8DDE9D, 0x530174B6, 0x90D98DF7, 0x6D482515, 0xD8BB2D84, 0xE1E68D47,
        0x1B96433F, 0xA511632E, 0x50AA2DA7, 0xE869CBCB, 0xF850843C,
        433, 404, 408, 6,
        "24D1E66E53C5D87DE91CAA26D6D19690C8AC04BAFDE1EAD6BFD7C25F2E1AA7DE", "EAB5C0E3F69E80479F257484E576E70425C45E082A21917A0C0AA716029033B3", "8A66B39F643BE98BDBC09CBCA46D5066A5C330F7A5F9514C10EAB2613D2ED16C",
        SupportsNpcCreation: true);

    /// <summary>
    /// Pokémon Diamond USA rev. 5 (ADAE/5, SHA-1 a46233d8…). Header table found at ARM9 0xEEDBC (559 × 24: the only offset where all 1 118 warps resolve
    /// to an existing warp); names in text bank 382; ground items in scr_seq 370 and hidden items in 374 (256 entries each, the shape of Platinum's 404/408).
    /// Background-object facing goes up to 5 in the original data, so 6 is not accepted. New NPC are not offered: the common scripts they rely on
    /// (Pokémon Center nurse) were only accredited in Platinum.
    /// </summary>
    public static Gen4EventProfile Diamond { get; } = new("ADAE", 5, "Diamante",
        "fielddata/eventdata/zone_event_release.narc", "fielddata/script/scr_seq_release.narc", "fielddata/land_data/land_data_release.narc",
        0xEEDBC, 559, 512, 1051, 578,
        0x08E0337C, 0xB6A50A07, 0xD80BFD77, 0x7DECBBE6, 0x5FDE722D, 0xB15ED699, 0xE2CD753E,
        0xCB20731F, 0xB5B9C2F6, 0x5FDC41D9, 0xA09B71DB, 0x7AD838C4,
        382, 370, 374, 5,
        "2652511F0D1C896219E058AF7393062AB02B5B7A0236EAA0ACDD6E6603B891E3", "CCCC109C69C9AEFDED5C7D922EBDDD9A48B4D26FFDD4234B6415FC8956C7A351", "8CFF7586CE6F43806E830605EB3B514B1319CFAA328A2A50B6AF0489BE50D836",
        SupportsNpcCreation: false);

    /// <summary>
    /// Pokémon HeartGold USA rev. 0 (IPKE/0, SHA-1 4fcded0e…). Its ARM9 is BLZ-compressed: the header table is at 0xF6BE0 of the decompressed ARM9 (540 × 24), the only
    /// offset where every reference is inside its table and all 1 308 warps resolve (plus 9 dynamic ones). Headers follow pret/pokeheartgold
    /// <c>include/map_header.h</c>: area +1, matrix +4, scripts +6, events +16, map section +18. The event records are those of Diamond and Platinum
    /// (<c>include/map_events_internal.h</c>; 491 members parse with no trailing byte). Location names are text bank 279; item balls run scr_seq 141 and
    /// hidden items 145 (<c>src/script_manager.c</c>). Trainer sight goes up to 7 and background-object facing up to 4 in the original data. No new NPC.
    /// </summary>
    public static Gen4EventProfile HeartGold { get; } = new("IPKE", 0, "HeartGold",
        "a/0/3/2", "a/0/1/2", "a/0/6/5",
        0xF6BE0, 540, 491, 965, 676,
        0x99A30D93, 0xAD02E794, 0x5547F5BE, 0xF44F1FBB, 0xA7E3F740, 0x3AC93851, 0x4704244D,
        0x5F864D0B, 0x48F27034, 0x4DA70454, 0xE4E244F1, 0x8B303A17,
        279, 141, 145, 4,
        "3A649BECD05885637AE5B82168ABA5D391E2282E863FD2BFE9BBB301B3AE920C", "4BB3B4F15CE4EF9FEADF0EEB62F45F8936B0FD20B7F0C331FA4084A490A7AFB0", "83F31D9CB12F650F5A1149E0D34F8316CC1A9AB6BE0F99FC543069BB0A9C9B15",
        SupportsNpcCreation: false)
    {
        HeaderLayout = Gen4HeaderLayout.Johto,
        MapMatrixPath = "a/0/4/1",
        AreaDataPath = "a/0/4/2", AreaBuildPath = "a/0/4/3", AreaBuildTexturesPath = "a/0/7/0", MapTexturesPath = "a/0/4/4", BuildModelPath = "a/0/4/0",
        IndoorBuildModelPath = "a/1/4/8", IndoorBuildModelCrc32 = 0x6D398D69,
        LandDataHasSoundSection = true,
        MaxMatrixSide = 47,
        MaxTrainerSight = 7,
        ItemBallsAreScriptedNpc = true,
    };

    public static IReadOnlyList<Gen4EventProfile> All { get; } = [Platinum, Diamond, HeartGold];
    public static Gen4EventProfile? For(string gameCode, int revision) => All.FirstOrDefault(p => p.GameCode == gameCode && p.Revision == revision);
    public static Gen4EventProfile? For(NdsHeader header) => header.IsDsiEnhanced ? null : For(header.GameCode, header.Version);
    /// <summary>The profile of a ROM, or an error naming the games that have one. No profile of another game or revision is ever applied.</summary>
    public static Gen4EventProfile Require(NdsRom rom, string area) => For(rom.Header)
        ?? throw new InvalidDataException($"{area}: solo {string.Join(" y ", All.Select(p => p.Label))}; no se aplican perfiles de otros juegos.");
    public static string SupportedLabels => string.Join(" y ", All.Select(p => p.Label));
}

/// <summary>Byte offsets of the fields the editor reads in a 24-byte Gen 4 map header. Every other byte is never read or written.</summary>
public sealed record Gen4HeaderLayout(int Area, int Matrix, int Scripts, int Events, int Label)
{
    /// <summary>Diamond and Platinum (pret/pokediamond, pret/pokeplatinum): area u8 +0, matrix +2, scripts +4, events +16, map section u8 +18.</summary>
    public static Gen4HeaderLayout Sinnoh { get; } = new(0, 2, 4, 16, 18);
    /// <summary>HeartGold (pret/pokeheartgold <c>include/map_header.h</c>): wild bank +0, area u8 +1, matrix +4, scripts +6, events +16, map section u8 +18.</summary>
    public static Gen4HeaderLayout Johto { get; } = new(1, 4, 6, 16, 18);
}
