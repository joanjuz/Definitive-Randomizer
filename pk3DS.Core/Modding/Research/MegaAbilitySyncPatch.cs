using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace pk3DS.Core.Modding.Research;

/// <summary>Addresses inside the game's static module (code.bin) that the routine calls directly.</summary>
public sealed record MegaAbilitySyncCodeAddresses(
    uint EvolveManager,
    uint MegaFormNo,
    uint GetTokuseiIndex,
    uint GetMemories,
    uint SetTokuseiNo);

/// <summary>Where the patch attaches inside Battle.cro and which routines it relies on.</summary>
public sealed record MegaAbilitySyncProfile(
    string Name,
    uint SizeSite,
    uint SendSite,
    uint ReceiveSite,
    uint MegaSite,
    uint NetLiteral,
    uint NetBssOffset,
    uint PersonalHelper,
    uint ClientBuffer,
    IReadOnlyList<(uint Start, uint End)> DependencyRanges,
    string DependencySha256)
{
    /// <summary>
    /// Ultra Sun and Ultra Moon ship the same Battle.cro, so one profile covers both. The hash covers the routines
    /// the patch depends on (network start-up, party notification, party reception, the lookup of the buffer that
    /// holds a client's packets, the Mega Evolution entry and its helpers), with the four hooked instructions left
    /// out so it stays valid after installing.
    /// </summary>
    public static MegaAbilitySyncProfile Usum { get; } = new(
        "USUM",
        SizeSite: 0xAE2DC,
        SendSite: 0xAE308,
        ReceiveSite: 0x11FE0,
        MegaSite: 0x7DD3C,
        NetLiteral: 0x8A2E4,
        NetBssOffset: 0x3DF8,
        PersonalHelper: 0x7DF28,
        ClientBuffer: 0x66A10,
        DependencyRanges:
        [
            (0x7DD24, 0x7DDF4),
            (0xAE29C, 0xAE354),
            (0x11F48, 0x120F4),
            (0x8A238, 0x8A2F0),
            (0x66858, 0x66B18),
            (0x7DF28, 0x7DF48),
            (0xAD4F8, 0xAD7B4),
            (0xADC44, 0xADC84),
        ],
        DependencySha256: "9803B63914EA1A01D792F4836835A11493A891493097AACA5194EA4FA52F7C3F");

    public IEnumerable<uint> Sites => [SizeSite, SendSite, ReceiveSite, MegaSite];

    /// <summary>Hash of the routines the patch depends on, ignoring the four hooked instructions.</summary>
    public string ComputeDependencySha256(byte[] cro)
    {
        if (cro is null)
            return string.Empty;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] blank = new byte[4];

        foreach (var (start, end) in DependencyRanges)
        {
            if (end < start || end > (uint)cro.Length || ((end - start) & 3) != 0)
                return string.Empty;

            for (uint at = start; at < end; at += 4)
            {
                if (at == SizeSite || at == SendSite || at == ReceiveSite || at == MegaSite)
                    hash.AppendData(blank);
                else
                    hash.AppendData(cro, (int)at, 4);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public enum MegaAbilitySyncState
{
    /// <summary>The four hook sites hold the original calls.</summary>
    Original,

    /// <summary>The four hook sites call one block that matches the routine byte for byte.</summary>
    Installed,

    /// <summary>
    /// The four hook sites call one block that matches, byte for byte, the routine of an earlier layout. That routine
    /// only read the appendix when the game deserialized the party straight from the received packet, which is not
    /// what happens in a link battle; installing again replaces it in place.
    /// </summary>
    InstalledEarlierFormat,

    /// <summary>Some sites are hooked and others are not, or the block is not the expected one.</summary>
    Partial,

    /// <summary>A site or a routine the patch depends on was changed by something else.</summary>
    External,

    /// <summary>The module is not the Battle.cro this patch was studied on.</summary>
    Unsupported,
}

public sealed record MegaAbilitySyncInspection(
    MegaAbilitySyncState State,
    uint? PayloadOffset,
    IReadOnlyList<PatchResourceReport> Sites,
    string Detail);

/// <summary>
/// Mega Ability Sync for Ultra Sun / Ultra Moon link battles between games whose Personal tables differ.
/// <para>
/// The game resolves the Ability of a Mega Evolution from its own Personal table. With this patch each game adds,
/// after the party it already sends, a 12-byte appendix with the Ability each of its Pokémon gets when it Mega
/// Evolves; the receiving game keeps that value with its copy of the remote Pokémon and uses it when that Pokémon
/// Mega Evolves. The appendix is looked up in the buffer that holds that player's packets, because in a link battle
/// the game already knows the other party and deserializes its own copy instead of the packet. The player's own
/// party and parties that belong to no network player (a partner run by the game) are not touched. A game without
/// the patch ignores the appendix, and a patched game that receives no valid appendix keeps the original behaviour,
/// so the synchronisation only happens when both sides carry the same protocol.
/// </para>
/// <para>Only Battle.cro is written. code.bin and static.crs are read to resolve addresses.</para>
/// </summary>
public static class MegaAbilitySyncPatch
{
    public const string ResourceName = "Battle.cro";

    /// <summary>Version carried in the appendix. Two games synchronise only when it is equal.</summary>
    public const byte ProtocolVersion = 1;

    /// <summary>Layout version of the installed block. Version 1 looked for the appendix only in the packet being deserialized.</summary>
    public const uint PayloadFormat = 2;

    public const int PartyBytes = 0x618;
    public const int AppendixBytes = 12;
    public const int PacketBytes = PartyBytes + AppendixBytes;
    public const int PartyMembers = 6;

    /// <summary>High byte of the value kept with a remote Pokémon; the low byte is the Ability.</summary>
    public const int StampTag = 0xA5;

    private const uint HeaderMagic = 0x5953414D; // "MASY"
    private const uint StubInstruction = 0xE51FF004; // ldr pc, [pc, #-4]
    private const uint StaticBase = 0x100000;

    private const int EntrySize = 2;
    private const int EntrySend = 5;

    // The eight literals follow the code, in this order.
    private const int LiteralMagic = 0;
    private const int LiteralPacket = 1;
    private const int LiteralNetDelta = 2;
    private const int LiteralEvolveManager = 3;
    private const int LiteralMegaFormNo = 4;
    private const int LiteralGetTokuseiIndex = 5;
    private const int LiteralGetMemories = 6;
    private const int LiteralSetTokuseiNo = 7;
    private const int LiteralCount = 8;

    public const int PayloadBytes = (148 + LiteralCount) * 4;

    /// <summary>
    /// Zero bytes kept before the block. Free space is found by looking for zeros, and data placed there by another
    /// patch may legitimately end in zeros (a table terminator), so the block never starts right after it.
    /// </summary>
    private const int GuardBytes = 8;

    private enum Target
    {
        Serialize,
        Deserialize,
        GetSize,
        GetMemberCount,
        GetMemberPointerConst,
        GetMemberPointer,
        GetFormNo,
        GetMonsNo,
        SetMemories,
        MegaEvolve,
        LoadPersonalData,
        GetPersonalParam,
        PersonalHelper,
        ClientBuffer,
    }

    /// <summary>One layout of the block: its code with the outgoing calls blank, where they go and where it is entered.</summary>
    private sealed record Layout(
        uint Format,
        uint[] Template,
        (int Index, Target Target)[] Calls,
        int EntryReceive,
        int EntryMega,
        int ReceiveReference)
    {
        public int Bytes => (Template.Length + LiteralCount) * 4;

        public int Entry(int site) => site switch { 0 => EntrySize, 1 => EntrySend, 2 => EntryReceive, _ => EntryMega };
    }

    private static readonly (Target Target, string Symbol)[] Imports =
    [
        (Target.Serialize, "_ZNK3pml9PokeParty22SerializeToShortStructEPv"),
        (Target.Deserialize, "_ZN3pml9PokeParty26DeserializeFromShortStructEPKv"),
        (Target.GetSize, "_ZN3pml9PokeParty27GetSerializeShortStructSizeEv"),
        (Target.GetMemberCount, "_ZNK3pml9PokeParty14GetMemberCountEv"),
        (Target.GetMemberPointerConst, "_ZNK3pml9PokeParty21GetMemberPointerConstEj"),
        (Target.GetMemberPointer, "_ZN3pml9PokeParty16GetMemberPointerEj"),
        (Target.GetFormNo, "_ZNK3pml8pokepara9CoreParam9GetFormNoEv"),
        (Target.GetMonsNo, "_ZNK3pml8pokepara9CoreParam9GetMonsNoEv"),
        (Target.SetMemories, "_ZN3pml8pokepara9CoreParam11SetMemoriesENS0_8MemoriesEj"),
        (Target.MegaEvolve, "_ZN3pml8pokepara9CoreParam10MegaEvolveEv"),
        (Target.LoadPersonalData, "_ZN3pml8personal16LoadPersonalDataE6MonsNoh"),
        (Target.GetPersonalParam, "_ZN3pml8personal16GetPersonalParamENS0_7ParamIDE"),
    ];

    private const string ExportMegaEvolve = "_ZN3pml8pokepara9CoreParam10MegaEvolveEv";
    private const string ExportSetTokuseiIndex = "_ZN3pml8pokepara9CoreParam15SetTokuseiIndexEh";
    private const string ExportGetTokuseiIndex = "_ZNK3pml8pokepara9CoreParam15GetTokuseiIndexEv";
    private const string ExportGetMemories = "_ZNK3pml8pokepara9CoreParam11GetMemoriesENS0_8MemoriesE";
    private const string ExportSetMemories = "_ZN3pml8pokepara9CoreParam11SetMemoriesENS0_8MemoriesEj";

    // Assembled from the reference source kept with the research notes; the outgoing calls are blank here and the
    // eight literals follow.
    private static readonly Layout Current = new(
        PayloadFormat,
        [
            0x5953414D, 0x00000002, 0xE3A00E62, 0xE2800004, 0xE12FFF1E, 0xE92D41F0,
            0xE1A04000, 0xE1A05001, 0xEB000000, 0xE2856C06, 0xE2866018, 0xE59F021C,
            0xE5860000, 0xE1A00004, 0xEB000000, 0xE1A08000, 0xE3A07000, 0xE3A00000,
            0xE1570008, 0x2A000003, 0xE1A00004, 0xE1A01007, 0xEB000000, 0xEB000013,
            0xE0861007, 0xE5C10004, 0xE2877001, 0xE3570006, 0xBAFFFFF3, 0xE1A00006,
            0xEB000003, 0xE5C6000A, 0xE3A00000, 0xE5C6000B, 0xE8BD81F0, 0xE3A0105A,
            0xE3A02004, 0xE7D03002, 0xE0211003, 0xE2822001, 0xE352000A, 0xBAFFFFFA,
            0xE1A00001, 0xE12FFF1E, 0xE92D4070, 0xE1B04000, 0x0A000014, 0xE59F0198,
            0xE5900000, 0xE1A01004, 0xE59FC190, 0xE12FFF3C, 0xE1A05000, 0xE1A00004,
            0xEB000000, 0xE1500005, 0x0A00000A, 0xE1A00004, 0xE59FC174, 0xE12FFF3C,
            0xE1A06000, 0xE1A00004, 0xEB000000, 0xE1A01005, 0xE286201A, 0xEB000000,
            0xE20000FF, 0xE8BD8070, 0xE3A00000, 0xE8BD8070, 0xE92D47F0, 0xE5948000,
            0xE1A06000, 0xE207A0FF, 0xEB000000, 0xE3A09000, 0xE59F0120, 0xE79F0000,
            0xE5D0000A, 0xE150000A, 0x0A000025, 0xE1A0000A, 0xEB000000, 0xE1B07000,
            0x0A000021, 0xE2872C17, 0xE1D207B2, 0xE3500003, 0x1A00001D, 0xE1D207B0,
            0xE59F10E4, 0xE1500001, 0x1A00000B, 0xE2875C06, 0xE2855018, 0xE5950000,
            0xE59F10C8, 0xE1500001, 0x1A000005, 0xE1A00005, 0xEBFFFFBD, 0xE5D5100A,
            0xE1500001, 0x1A000000, 0xE1A09005, 0xE3A04000, 0xE3A05000, 0xE3590000,
            0x10890004, 0x15D05004, 0xE3550000, 0x13855CA5, 0xE1A00008, 0xEB000005,
            0xE1A00006, 0xEB000003, 0xE2844001, 0xE3540006, 0xBAFFFFF2, 0xE8BD87F0,
            0xE92D4080, 0xE1A07000, 0xEB000000, 0xE1540000, 0x2A000005, 0xE1A00007,
            0xE1A01004, 0xEB000000, 0xE3A01010, 0xE1A02005, 0xEB000000, 0xE8BD8080,
            0xE92D4070, 0xE1A04000, 0xEB000000, 0xE1A00004, 0xE3A01010, 0xE59FC03C,
            0xE12FFF3C, 0xE1A05000, 0xE1A00425, 0xE35000A5, 0x1A000003, 0xE594000C,
            0xE20510FF, 0xE59FC020, 0xE12FFF3C, 0xE8BD8070,
        ],
        [
            (8, Target.Serialize),
            (14, Target.GetMemberCount),
            (22, Target.GetMemberPointerConst),
            (54, Target.GetFormNo),
            (62, Target.GetMonsNo),
            (65, Target.PersonalHelper),
            (74, Target.Deserialize),
            (82, Target.ClientBuffer),
            (122, Target.GetMemberCount),
            (127, Target.GetMemberPointer),
            (130, Target.SetMemories),
            (134, Target.MegaEvolve),
        ],
        EntryReceive: 70,
        EntryMega: 132,
        ReceiveReference: 77);

    // The first published layout, kept only to recognise it and replace or remove it.
    private static readonly Layout Earlier = new(
        1,
        [
            0x5953414D, 0x00000001, 0xE3A00E62, 0xE2800004, 0xE12FFF1E, 0xE92D41F0,
            0xE1A04000, 0xE1A05001, 0xEB000000, 0xE2856C06, 0xE2866018, 0xE59F0228,
            0xE5860000, 0xE1A00004, 0xEB000000, 0xE1A08000, 0xE3A07000, 0xE3A00000,
            0xE1570008, 0x2A000003, 0xE1A00004, 0xE1A01007, 0xEB000000, 0xEB000013,
            0xE0861007, 0xE5C10004, 0xE2877001, 0xE3570006, 0xBAFFFFF3, 0xE1A00006,
            0xEB000003, 0xE5C6000A, 0xE3A00000, 0xE5C6000B, 0xE8BD81F0, 0xE3A0105A,
            0xE3A02004, 0xE7D03002, 0xE0211003, 0xE2822001, 0xE352000A, 0xBAFFFFFA,
            0xE1A00001, 0xE12FFF1E, 0xE92D4070, 0xE1B04000, 0x0A000014, 0xE59F01A4,
            0xE5900000, 0xE1A01004, 0xE59FC19C, 0xE12FFF3C, 0xE1A05000, 0xE1A00004,
            0xEB000000, 0xE1500005, 0x0A00000A, 0xE1A00004, 0xE59FC180, 0xE12FFF3C,
            0xE1A06000, 0xE1A00004, 0xEB000000, 0xE1A01005, 0xE286201A, 0xEB000000,
            0xE20000FF, 0xE8BD8070, 0xE3A00000, 0xE8BD8070, 0xE92D47F0, 0xE5948000,
            0xE1A06000, 0xE1A07001, 0xEB000000, 0xE3A09000, 0xE59F012C, 0xE79F0000,
            0xE2800B06, 0xE2800FAE, 0xE3A01004, 0xE1500007, 0x0A000004, 0xE2800C17,
            0xE2800084, 0xE2511001, 0x1AFFFFF9, 0xEA000021, 0xE2872C17, 0xE1D207B2,
            0xE3500003, 0x1A00000F, 0xE1D207B0, 0xE59F10E4, 0xE1500001, 0x1A00000B,
            0xE2875C06, 0xE2855018, 0xE5950000, 0xE59F10C8, 0xE1500001, 0x1A000005,
            0xE1A00005, 0xEBFFFFBA, 0xE5D5100A, 0xE1500001, 0x1A000000, 0xE1A09005,
            0xE3A04000, 0xE3A05000, 0xE3590000, 0x10890004, 0x15D05004, 0xE3550000,
            0x13855CA5, 0xE1A00008, 0xEB000005, 0xE1A00006, 0xEB000003, 0xE2844001,
            0xE3540006, 0xBAFFFFF2, 0xE8BD87F0, 0xE92D4080, 0xE1A07000, 0xEB000000,
            0xE1540000, 0x2A000005, 0xE1A00007, 0xE1A01004, 0xEB000000, 0xE3A01010,
            0xE1A02005, 0xEB000000, 0xE8BD8080, 0xE92D4070, 0xE1A04000, 0xEB000000,
            0xE1A00004, 0xE3A01010, 0xE59FC03C, 0xE12FFF3C, 0xE1A05000, 0xE1A00425,
            0xE35000A5, 0x1A000003, 0xE594000C, 0xE20510FF, 0xE59FC020, 0xE12FFF3C,
            0xE8BD8070,
        ],
        [
            (8, Target.Serialize),
            (14, Target.GetMemberCount),
            (22, Target.GetMemberPointerConst),
            (54, Target.GetFormNo),
            (62, Target.GetMonsNo),
            (65, Target.PersonalHelper),
            (74, Target.Deserialize),
            (125, Target.GetMemberCount),
            (130, Target.GetMemberPointer),
            (133, Target.SetMemories),
            (137, Target.MegaEvolve),
        ],
        EntryReceive: 70,
        EntryMega: 135,
        ReceiveReference: 77);

    private static readonly Layout[] Layouts = [Current, Earlier];

    // ---------------------------------------------------------------------------------------------------------
    // Protocol model: the same rules the routine applies, for the installer's checks and for simulations.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Builds the appendix a game adds after its serialized party.</summary>
    public static byte[] BuildAppendix(ReadOnlySpan<byte> megaAbilities, byte protocol = ProtocolVersion)
    {
        if (megaAbilities.Length != PartyMembers)
            throw new ArgumentException("An appendix carries exactly six abilities.", nameof(megaAbilities));

        byte[] appendix = new byte[AppendixBytes];
        appendix[0] = (byte)'M';
        appendix[1] = (byte)'A';
        appendix[2] = (byte)'S';
        appendix[3] = protocol;
        megaAbilities.CopyTo(appendix.AsSpan(4));
        appendix[10] = Checksum(appendix);
        return appendix;
    }

    /// <summary>
    /// Reads the appendix of a received party packet. Returns false, as the routine does, unless the packet is a
    /// party command of exactly the expected size with this protocol version and a correct checksum.
    /// </summary>
    public static bool TryReadAppendix(ReadOnlySpan<byte> packet, int command, out byte[] megaAbilities)
    {
        megaAbilities = null;

        if (command != 3 || packet.Length != PacketBytes)
            return false;

        var appendix = packet[PartyBytes..];
        if (appendix[0] != (byte)'M' || appendix[1] != (byte)'A' || appendix[2] != (byte)'S' || appendix[3] != ProtocolVersion)
            return false;

        if (appendix[10] != Checksum(appendix))
            return false;

        megaAbilities = appendix.Slice(4, PartyMembers).ToArray();
        return true;
    }

    /// <summary>Value kept with a remote Pokémon: zero when it has no synchronised Mega Ability.</summary>
    public static ushort Stamp(byte megaAbility) =>
        megaAbility == 0 ? (ushort)0 : (ushort)((StampTag << 8) | megaAbility);

    /// <summary>Ability to use when a Pokémon carrying <paramref name="stamp"/> Mega Evolves, or null to keep the local one.</summary>
    public static byte? StampedAbility(ushort stamp) =>
        stamp >> 8 == StampTag ? (byte)stamp : null;

    private static byte Checksum(ReadOnlySpan<byte> appendix)
    {
        byte value = 0x5A;
        for (int i = 4; i < 10; i++)
            value ^= appendix[i];
        return value;
    }

    // ---------------------------------------------------------------------------------------------------------
    // code.bin addresses
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the addresses the routine needs from the game's own files: exported names from static.crs, plus two
    /// internal routines reached from exported ones, each accepted only if the surrounding instructions are the
    /// expected ones. Nothing is written.
    /// </summary>
    public static bool TryResolveCodeAddresses(
        byte[] staticCrs,
        byte[] code,
        out MegaAbilitySyncCodeAddresses addresses,
        out string error)
    {
        addresses = null;

        if (!TryReadStaticExports(staticCrs, out var exports, out error))
            return false;

        if (code is null || code.Length < 0x1000 || (code.Length & 0x1FF) != 0)
        {
            error = "code.bin is missing or is not decompressed.";
            return false;
        }

        bool Word(uint address, out uint value)
        {
            value = 0;
            if (address < StaticBase || (address & 3) != 0)
                return false;
            ulong offset = address - StaticBase;
            if (offset + 4 > (ulong)code.Length)
                return false;
            value = BitConverter.ToUInt32(code, (int)offset);
            return true;
        }

        bool Branch(uint address, uint opcode, out uint target)
        {
            target = 0;
            if (!Word(address, out uint word) || word >> 24 != opcode)
                return false;
            target = BranchTarget(address, word);
            return true;
        }

        foreach (string name in new[] { ExportMegaEvolve, ExportSetTokuseiIndex, ExportGetTokuseiIndex, ExportGetMemories, ExportSetMemories })
        {
            if (!exports.ContainsKey(name))
            {
                error = "static.crs does not export " + name + ".";
                return false;
            }
        }

        // CoreParam::MegaEvolve: ldr r6, =evolveManager ... ldr r0, [r6] ; bl megaFormNo
        uint mega = exports[ExportMegaEvolve];
        if (!Word(mega + 0x10, out uint loadManager) || (loadManager & 0xFFFFF000) != 0xE59F6000 ||
            !Word(mega + 0x10 + 8 + (loadManager & 0xFFF), out uint evolveManager) ||
            !Word(mega + 0x1C, out uint readManager) || readManager != 0xE5960000 ||
            !Branch(mega + 0x20, 0xEB, out uint megaFormNo))
        {
            error = "CoreParam::MegaEvolve does not have the expected shape in code.bin.";
            return false;
        }

        // CoreParam::SetTokuseiIndex ends by storing the Ability: ldr r0, [r4, #0xC] ; mov r1, r6 ; pop ; b setTokuseiNo
        uint index = exports[ExportSetTokuseiIndex];
        if (!Word(index + 0x78, out uint a) || a != 0xE594000C ||
            !Word(index + 0x7C, out uint b) || b != 0xE1A01006 ||
            !Word(index + 0x80, out uint c) || c != 0xE8BD4070 ||
            !Branch(index + 0x84, 0xEA, out uint setTokuseiNo) ||
            !Word(setTokuseiNo + 0x1C, out uint store) || store != 0xE5C0400C)
        {
            error = "CoreParam::SetTokuseiIndex does not end in the expected Ability store in code.bin.";
            return false;
        }

        // Memory field 16 must be the same 16-bit field for the getter and the setter.
        uint get = exports[ExportGetMemories], set = exports[ExportSetMemories];
        if (!Word(get, out uint getHead) || getHead != 0xE3510012 ||
            !Word(get + 0x10 + 16 * 4, out uint getCase) || !Branch(getCase + 8, 0xEA, out uint getField) ||
            !Word(getField + 0x18, out uint load) || load != 0xE1D043B0 || !Branch(getField + 0x14, 0xEB, out uint getBlock) ||
            !Word(set, out uint setHead) || setHead != 0xE3510012 ||
            !Word(set + 0x0C + 16 * 4, out uint setCase) || !Word(setCase + 4, out uint narrow) || narrow != 0xE6FF1072 ||
            !Branch(setCase + 8, 0xEA, out uint setField) ||
            !Word(setField + 0x1C, out uint save) || save != 0xE1C043B0 || !Branch(setField + 0x18, 0xEB, out uint setBlock) ||
            getBlock != setBlock)
        {
            error = "The auxiliary 16-bit field used to keep the value is not where it is expected in code.bin.";
            return false;
        }

        addresses = new(
            evolveManager,
            megaFormNo,
            exports[ExportGetTokuseiIndex],
            get,
            setTokuseiNo);

        error = string.Empty;
        return true;
    }

    private static bool TryReadStaticExports(byte[] crs, out Dictionary<string, uint> exports, out string error)
    {
        exports = null;

        if (crs is null || crs.Length < 0x138)
        {
            error = "static.crs is missing.";
            return false;
        }

        uint segmentTable = BitConverter.ToUInt32(crs, 0xC8), segmentCount = BitConverter.ToUInt32(crs, 0xCC);
        uint exportTable = BitConverter.ToUInt32(crs, 0xD0), exportCount = BitConverter.ToUInt32(crs, 0xD4);

        if (segmentCount == 0 || segmentCount > 16 || !Fits(crs, segmentTable, segmentCount * 12) ||
            exportCount > 0x100000 || !Fits(crs, exportTable, exportCount * 8))
        {
            error = "static.crs has an unexpected layout.";
            return false;
        }

        uint[] starts = new uint[segmentCount];
        for (int i = 0; i < segmentCount; i++)
            starts[i] = BitConverter.ToUInt32(crs, (int)segmentTable + i * 12);

        if (starts[0] != StaticBase)
        {
            error = "static.crs does not describe a static module loaded at 0x100000.";
            return false;
        }

        exports = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int i = 0; i < exportCount; i++)
        {
            uint nameOffset = BitConverter.ToUInt32(crs, (int)exportTable + i * 8);
            uint segmentOffset = BitConverter.ToUInt32(crs, (int)exportTable + i * 8 + 4);
            uint segment = segmentOffset & 0xF;

            if (segment >= segmentCount || !TryReadName(crs, nameOffset, out string name))
                continue;

            exports[name] = starts[segment] + (segmentOffset >> 4);
        }

        error = string.Empty;
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Battle.cro
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the block for one position. Exposed so its bytes can be compared with the reference;
    /// <paramref name="earlierFormat"/> gives the first published layout, which is only recognised, never installed.
    /// </summary>
    public static byte[] BuildPayload(
        uint offset,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        IReadOnlyDictionary<string, uint> stubs,
        bool earlierFormat = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stubs);

        var targets = new Dictionary<Target, uint> { [Target.PersonalHelper] = profile.PersonalHelper, [Target.ClientBuffer] = profile.ClientBuffer };
        foreach (var (target, symbol) in Imports)
        {
            if (!stubs.TryGetValue(symbol, out uint stub))
                throw new ArgumentException("Missing import stub " + symbol + ".", nameof(stubs));
            targets[target] = stub;
        }

        return BuildPayload(offset, code, profile, targets, earlierFormat ? Earlier : Current);
    }

    private static byte[] BuildPayload(
        uint offset,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        Dictionary<Target, uint> targets,
        Layout layout)
    {
        uint[] words = new uint[layout.Bytes / 4];
        layout.Template.CopyTo(words, 0);

        foreach (var (index, target) in layout.Calls)
            words[index] = Call(offset + (uint)index * 4, targets[target]);

        int literals = layout.Template.Length;
        words[literals + LiteralMagic] = (uint)('M' | 'A' << 8 | 'S' << 16 | ProtocolVersion << 24);
        words[literals + LiteralPacket] = PacketBytes;
        words[literals + LiteralNetDelta] = profile.NetLiteral - (offset + (uint)layout.ReceiveReference * 4 + 8);
        words[literals + LiteralEvolveManager] = code.EvolveManager;
        words[literals + LiteralMegaFormNo] = code.MegaFormNo;
        words[literals + LiteralGetTokuseiIndex] = code.GetTokuseiIndex;
        words[literals + LiteralGetMemories] = code.GetMemories;
        words[literals + LiteralSetTokuseiNo] = code.SetTokuseiNo;

        byte[] payload = new byte[layout.Bytes];
        Buffer.BlockCopy(words, 0, payload, 0, payload.Length);
        return payload;
    }

    /// <summary>Import stubs of a CRO by symbol name: the address of each <c>ldr pc, [pc, #-4]</c> veneer.</summary>
    public static bool TryReadImportStubs(byte[] cro, out Dictionary<string, uint> stubs, out string error)
    {
        stubs = null;

        if (!CroRelocationMap.TryCreate(cro, out var map, out error))
            return false;

        uint table = BitConverter.ToUInt32(cro, 0x100), count = BitConverter.ToUInt32(cro, 0x104);
        if (count > 0x100000 || !Fits(cro, table, count * 8))
        {
            error = "The named import table lies outside the file.";
            return false;
        }

        stubs = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            uint nameOffset = BitConverter.ToUInt32(cro, (int)table + i * 8);
            uint patch = BitConverter.ToUInt32(cro, (int)table + i * 8 + 4);

            if (!TryReadName(cro, nameOffset, out string name) || !Fits(cro, patch, 12))
                continue;

            uint segmentOffset = BitConverter.ToUInt32(cro, (int)patch);
            byte type = cro[patch + 4], last = cro[patch + 5];
            uint addend = BitConverter.ToUInt32(cro, (int)patch + 8);

            // One absolute word in .text, preceded by the veneer instruction.
            if ((segmentOffset & 0xF) != 0 || type != 2 || last == 0 || addend != 0)
                continue;

            uint site = map.CodeStart + (segmentOffset >> 4);
            if (site < map.CodeStart + 4 || site + 4 > map.CodeEnd || (site & 3) != 0)
                continue;

            if (BitConverter.ToUInt32(cro, (int)site - 4) == StubInstruction)
                stubs[name] = site - 4;
        }

        error = string.Empty;
        return true;
    }

    public static MegaAbilitySyncInspection Inspect(byte[] cro, MegaAbilitySyncCodeAddresses code) =>
        Inspect(cro, code, MegaAbilitySyncProfile.Usum);

    /// <summary>Classifies the module without writing. <paramref name="code"/> may be null: the block is then compared without its code.bin addresses.</summary>
    public static MegaAbilitySyncInspection Inspect(
        byte[] cro,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!TryReadLayout(cro, profile, out var targets, out string error))
            return new(MegaAbilitySyncState.Unsupported, null, [], error);

        var names = new (string Name, string Role, uint Site, Target Original)[]
        {
            (ResourceName + " · tamaño del paquete del equipo", "Tamaño que se anuncia al enviar el equipo", profile.SizeSite, Target.GetSize),
            (ResourceName + " · envío del equipo", "Añade las habilidades Mega propias al paquete", profile.SendSite, Target.Serialize),
            (ResourceName + " · recepción del equipo", "Guarda las habilidades Mega del otro jugador", profile.ReceiveSite, Target.Deserialize),
            (ResourceName + " · megaevolución", "Aplica la habilidad del dueño al megaevolucionar", profile.MegaSite, Target.MegaEvolve),
        };

        var sites = new List<PatchResourceReport>();
        var bases = new HashSet<uint>();
        int original = 0, own = 0, earlier = 0, external = 0;

        for (int i = 0; i < names.Length; i++)
        {
            var (name, role, site, target) = names[i];
            uint word = BitConverter.ToUInt32(cro, (int)site);

            if (word == Call(site, targets[target]))
            {
                original++;
                sites.Add(new(name, role, PatchResourceState.Original, site, "Llamada original."));
                continue;
            }

            if (word >> 24 != 0xEB)
            {
                external++;
                sites.Add(new(name, role, PatchResourceState.ExternalModification, site, "La instrucción no es la original ni una llamada de este parche."));
                continue;
            }

            // The call must land on the entry of a block of a known layout; where the entry is depends on the layout.
            uint destination = BranchTarget(site, word);
            Layout layout = null;
            uint start = 0;
            bool header = false;

            foreach (var candidate in Layouts)
            {
                uint at = destination - (uint)candidate.Entry(i) * 4;
                if (!Fits(cro, at, 8) || BitConverter.ToUInt32(cro, (int)at) != HeaderMagic)
                    continue;

                header = true;
                if (BitConverter.ToUInt32(cro, (int)at + 4) == candidate.Format && Fits(cro, at, (uint)candidate.Bytes))
                {
                    layout = candidate;
                    start = at;
                    break;
                }
            }

            if (!header)
            {
                external++;
                sites.Add(new(name, role, PatchResourceState.ExternalModification, site, "La llamada lleva a código que no pertenece a este parche."));
                continue;
            }

            if (layout is null)
            {
                sites.Add(new(name, role, PatchResourceState.OwnInstallEarlierFormat, site, "La rutina instalada es de otro formato."));
                continue;
            }

            if (!MatchesPayload(cro, start, code, profile, targets, layout))
            {
                sites.Add(new(name, role, PatchResourceState.PartialInstall, site, "La rutina instalada no coincide con la esperada."));
                continue;
            }

            bases.Add(start);

            if (layout == Current)
            {
                own++;
                sites.Add(new(name, role, PatchResourceState.OwnInstall, site, $"Llamada a la rutina instalada en 0x{start:X6}."));
            }
            else
            {
                earlier++;
                sites.Add(new(name, role, PatchResourceState.OwnInstallEarlierFormat, site, $"Llamada a la rutina anterior instalada en 0x{start:X6}; al instalar de nuevo se sustituye."));
            }
        }

        if (external != 0)
            return new(MegaAbilitySyncState.External, null, sites, "Otra modificación ocupa un punto de enganche.");

        if (original == names.Length)
            return new(MegaAbilitySyncState.Original, null, sites, "Los cuatro puntos tienen el código original.");

        if (own == names.Length && bases.Count == 1)
            return new(MegaAbilitySyncState.Installed, bases.First(), sites, "El parche está instalado.");

        if (earlier == names.Length && bases.Count == 1)
            return new(MegaAbilitySyncState.InstalledEarlierFormat, bases.First(), sites,
                "El parche está instalado con la rutina anterior, que no sincroniza en los combates por enlace. Instálalo de nuevo para sustituirla.");

        return new(MegaAbilitySyncState.Partial, bases.Count == 1 ? bases.First() : null, sites,
            "El parche está instalado solo en parte o con una rutina distinta.");
    }

    public static bool TryInstall(byte[] cro, MegaAbilitySyncCodeAddresses code, out byte[] patched, out string error) =>
        TryInstall(cro, code, MegaAbilitySyncProfile.Usum, out patched, out error);

    /// <summary>
    /// Installs the routine on a detached copy. Installing over an identical installation returns the same bytes, and
    /// installing over the earlier routine replaces it in the space it already had. A partial or foreign state is
    /// refused: nothing is forced.
    /// </summary>
    public static bool TryInstall(
        byte[] cro,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        out byte[] patched,
        out string error)
    {
        patched = null;

        if (code is null)
        {
            error = "No se pudieron resolver las direcciones de code.bin.";
            return false;
        }

        var inspection = Inspect(cro, code, profile);
        switch (inspection.State)
        {
            case MegaAbilitySyncState.Installed:
                patched = (byte[])cro.Clone();
                error = string.Empty;
                return true;
            case MegaAbilitySyncState.Original:
                break;
            case MegaAbilitySyncState.InstalledEarlierFormat when inspection.PayloadOffset is uint earlierAt:
                return TryReplaceEarlier(cro, code, profile, earlierAt, out patched, out error);
            default:
                error = inspection.Detail;
                return false;
        }

        if (!TryReadLayout(cro, profile, out var targets, out error))
            return false;

        if (!CroPatchSession.TryCreate(cro, out var session, out error))
            return false;

        if (!session.TryAllocateCode(GuardBytes + PayloadBytes, "mega-ability-sync", out var grant, out error))
            return false;

        uint start = grant.Offset + GuardBytes;
        byte[] block = new byte[GuardBytes + PayloadBytes];
        BuildPayload(start, code, profile, targets, Current).CopyTo(block, GuardBytes);

        if (!session.TryWriteCode(grant, block, out error))
            return false;

        if (!session.TryBuildImage(out byte[] image, out _, out error))
            return false;

        // Growing the code segment inserts bytes after .text, so the original routines keep their offsets.
        return TryHook(image, code, profile, start, out patched, out error);
    }

    /// <summary>The earlier routine is at least as long as the current one, so the new one takes its place.</summary>
    private static bool TryReplaceEarlier(
        byte[] cro,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        uint start,
        out byte[] patched,
        out string error)
    {
        patched = null;

        if (Current.Bytes > Earlier.Bytes)
        {
            error = "La rutina anterior no se puede sustituir en su sitio.";
            return false;
        }

        if (!TryReadLayout(cro, profile, out var targets, out error))
            return false;

        byte[] image = (byte[])cro.Clone();
        Array.Clear(image, (int)start, Earlier.Bytes);
        BuildPayload(start, code, profile, targets, Current).CopyTo(image, (int)start);
        return TryHook(image, code, profile, start, out patched, out error);
    }

    /// <summary>Points the four calls at the block, refreshes the integrity hashes and reads everything back.</summary>
    private static bool TryHook(
        byte[] image,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        uint start,
        out byte[] patched,
        out string error)
    {
        patched = null;

        uint[] sites = [profile.SizeSite, profile.SendSite, profile.ReceiveSite, profile.MegaSite];
        for (int i = 0; i < sites.Length; i++)
            Write(image, sites[i], Call(sites[i], start + (uint)Current.Entry(i) * 4));

        if (!CroSegmentExpander.TryUpdateHashes(image, out error))
            return false;

        var result = Inspect(image, code, profile);
        if (result.State != MegaAbilitySyncState.Installed || result.PayloadOffset != start)
        {
            error = "La instalación no se pudo leer de vuelta: " + result.Detail;
            return false;
        }

        patched = image;
        error = string.Empty;
        return true;
    }

    public static bool TryUninstall(byte[] cro, MegaAbilitySyncCodeAddresses code, out byte[] restored, out string error) =>
        TryUninstall(cro, code, MegaAbilitySyncProfile.Usum, out restored, out error);

    /// <summary>
    /// Restores the four original calls and clears the routine, of the current or the earlier layout. If installing had to grow the code segment, the
    /// module keeps its new size: the result behaves like the original but is not byte-identical to it.
    /// </summary>
    public static bool TryUninstall(
        byte[] cro,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        out byte[] restored,
        out string error)
    {
        restored = null;

        var inspection = Inspect(cro, code, profile);
        if (inspection.State == MegaAbilitySyncState.Original)
        {
            restored = (byte[])cro.Clone();
            error = string.Empty;
            return true;
        }

        if (inspection.State is not (MegaAbilitySyncState.Installed or MegaAbilitySyncState.InstalledEarlierFormat) ||
            inspection.PayloadOffset is not uint installedAt)
        {
            error = inspection.Detail;
            return false;
        }

        int installedBytes = inspection.State == MegaAbilitySyncState.Installed ? Current.Bytes : Earlier.Bytes;

        if (!TryReadLayout(cro, profile, out var targets, out error))
            return false;

        byte[] image = (byte[])cro.Clone();
        Write(image, profile.SizeSite, Call(profile.SizeSite, targets[Target.GetSize]));
        Write(image, profile.SendSite, Call(profile.SendSite, targets[Target.Serialize]));
        Write(image, profile.ReceiveSite, Call(profile.ReceiveSite, targets[Target.Deserialize]));
        Write(image, profile.MegaSite, Call(profile.MegaSite, targets[Target.MegaEvolve]));
        Array.Clear(image, (int)installedAt, installedBytes);

        if (!CroSegmentExpander.TryUpdateHashes(image, out error))
            return false;

        if (Inspect(image, code, profile).State != MegaAbilitySyncState.Original)
        {
            error = "La restauración no se pudo leer de vuelta.";
            return false;
        }

        restored = image;
        error = string.Empty;
        return true;
    }

    /// <summary>Checks everything the routine assumes about the module and resolves what it calls.</summary>
    private static bool TryReadLayout(
        byte[] cro,
        MegaAbilitySyncProfile profile,
        out Dictionary<Target, uint> targets,
        out string error)
    {
        targets = null;

        if (!CroRelocationMap.TryCreate(cro, out var map, out error))
            return false;

        foreach (uint site in profile.Sites.Append(profile.NetLiteral).Append(profile.PersonalHelper).Append(profile.ClientBuffer))
        {
            if ((site & 3) != 0 || site < map.CodeStart || site + 4 > map.CodeEnd)
            {
                error = "Battle.cro no tiene el código de combate de Ultra Sol / Ultra Luna en las posiciones esperadas.";
                return false;
            }
        }

        if (!TryReadImportStubs(cro, out var stubs, out error))
            return false;

        targets = new Dictionary<Target, uint> { [Target.PersonalHelper] = profile.PersonalHelper, [Target.ClientBuffer] = profile.ClientBuffer };
        foreach (var (target, symbol) in Imports)
        {
            if (!stubs.TryGetValue(symbol, out uint stub))
            {
                error = "Battle.cro no importa " + symbol + ": no es el módulo esperado.";
                targets = null;
                return false;
            }

            targets[target] = stub;
        }

        string dependency = profile.ComputeDependencySha256(cro);
        if (!string.Equals(dependency, profile.DependencySha256, StringComparison.OrdinalIgnoreCase))
        {
            error = "Las rutinas de combate de las que depende el parche no son las originales: otra revisión u otra modificación las cambió.";
            targets = null;
            return false;
        }

        // The helper must load the Personal record and read one field through the imported routines.
        if (BranchTarget(profile.PersonalHelper + 0x10, BitConverter.ToUInt32(cro, (int)profile.PersonalHelper + 0x10)) != targets[Target.LoadPersonalData] ||
            BranchTarget(profile.PersonalHelper + 0x1C, BitConverter.ToUInt32(cro, (int)profile.PersonalHelper + 0x1C)) != targets[Target.GetPersonalParam])
        {
            error = "La rutina auxiliar de la tabla Personal no llama a las funciones esperadas.";
            targets = null;
            return false;
        }

        if (!HasBssPointer(cro, map, profile))
        {
            error = "No se encuentra la referencia al estado de red del combate en la posición esperada.";
            targets = null;
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The word the routine reads must be relocated by the loader to the battle network state in .bss.</summary>
    private static bool HasBssPointer(byte[] cro, CroRelocationMap map, MegaAbilitySyncProfile profile)
    {
        uint wanted = ((profile.NetLiteral - map.CodeStart) << 4) | 0;

        for (uint i = 0; i < map.PatchTableCount; i++)
        {
            int entry = (int)(map.PatchTableOffset + i * 12);
            if (BitConverter.ToUInt32(cro, entry) == wanted &&
                cro[entry + 4] == 2 &&
                cro[entry + 5] == 3 &&
                BitConverter.ToUInt32(cro, entry + 8) == profile.NetBssOffset)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPayload(
        byte[] cro,
        uint start,
        MegaAbilitySyncCodeAddresses code,
        MegaAbilitySyncProfile profile,
        Dictionary<Target, uint> targets,
        Layout layout)
    {
        var placeholder = code ?? new MegaAbilitySyncCodeAddresses(0, 0, 0, 0, 0);
        byte[] expected = BuildPayload(start, placeholder, profile, targets, layout);
        int compare = code is null ? (layout.Template.Length + LiteralEvolveManager) * 4 : layout.Bytes;

        return cro.AsSpan((int)start, compare).SequenceEqual(expected.AsSpan(0, compare));
    }

    private static uint Call(uint from, uint to) =>
        0xEB000000u | ((uint)((int)(to - (from + 8)) >> 2) & 0x00FFFFFF);

    private static uint BranchTarget(uint address, uint word)
    {
        int offset = (int)(word & 0x00FFFFFF);
        if ((offset & 0x00800000) != 0)
            offset -= 0x01000000;
        return (uint)(address + 8 + offset * 4);
    }

    private static void Write(byte[] data, uint offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(data, (int)offset);

    private static bool Fits(byte[] data, uint offset, uint length) =>
        (ulong)offset + length <= (ulong)data.Length;

    private static bool TryReadName(byte[] data, uint offset, out string name)
    {
        name = null;
        if (offset >= data.Length)
            return false;

        int end = Array.IndexOf(data, (byte)0, (int)offset);
        if (end < 0 || end - offset > 512)
            return false;

        name = Encoding.ASCII.GetString(data, (int)offset, end - (int)offset);
        return true;
    }
}
