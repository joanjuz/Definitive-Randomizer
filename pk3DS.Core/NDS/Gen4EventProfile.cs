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

    public static IReadOnlyList<Gen4EventProfile> All { get; } = [Platinum, Diamond];
    public static Gen4EventProfile? For(string gameCode, int revision) => All.FirstOrDefault(p => p.GameCode == gameCode && p.Revision == revision);
    public static Gen4EventProfile? For(NdsHeader header) => header.IsDsiEnhanced ? null : For(header.GameCode, header.Version);
    /// <summary>The profile of a ROM, or an error naming the games that have one. No profile of another game or revision is ever applied.</summary>
    public static Gen4EventProfile Require(NdsRom rom, string area) => For(rom.Header)
        ?? throw new InvalidDataException($"{area}: solo {string.Join(" y ", All.Select(p => p.Label))}; no se aplican perfiles de otros juegos.");
    public static string SupportedLabels => string.Join(" y ", All.Select(p => p.Label));
}
