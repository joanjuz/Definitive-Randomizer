using System.IO;
using System.Security.Cryptography;
using System.Text;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Level = pk3DS.Core.Modding.Research.PatchCapabilityLevel;
using State = pk3DS.Core.Modding.Research.PatchResourceState;
using Sync = pk3DS.Core.Modding.Research.MegaAbilitySyncPatch;

/// <summary>
/// Mega Ability Sync (Ultra Sun / Ultra Moon). Three kinds of evidence, none of which is an in-game battle:
/// a model of two games with different Personal tables that follows the rules of the installed routine;
/// the installer on synthetic modules (no game code); and, only when a real extraction is given on the command
/// line, the installer on the real files.
/// </summary>
internal static class MegaAbilitySyncChecks
{
    // ---------------------------------------------------------------------------------------------------------
    // Model of two games
    // ---------------------------------------------------------------------------------------------------------
    private const byte Levitate = 26, FlashFire = 18, Intimidate = 22, Blaze = 66, Sturdy = 5, Drizzle = 2, Static = 9;
    private const int Charizard = 6, Pikachu = 25;

    private sealed record Mon(int Species, int Form, int Slot, int? MegaForm)
    {
        public ushort Stamp { get; set; }
        public bool Mega { get; set; }
        public byte Ability { get; set; }
    }

    private sealed class Game(string name, Dictionary<(int, int), byte[]> personal, bool patched = true, byte protocol = Sync.ProtocolVersion)
    {
        public string Name => name;
        public bool Patched => patched;
        public byte Local(int species, int form, int slot) => personal[(species, form)][slot];

        /// <summary>What the game sends for its own party: the serialized party and, if patched, the appendix.</summary>
        public byte[] Send(IReadOnlyList<Mon> party)
        {
            byte[] packet = new byte[patched ? Sync.PacketBytes : Sync.PartyBytes];
            for (int i = 0; i < party.Count; i++)
            {
                packet[i * 0x104] = (byte)party[i].Species; packet[i * 0x104 + 1] = (byte)party[i].Form; packet[i * 0x104 + 2] = (byte)party[i].Slot;
                packet[i * 0x104 + 3] = (byte)(party[i].MegaForm ?? 0xFF); packet[i * 0x104 + 4] = 1;
            }
            if (!patched) return packet;
            byte[] megas = new byte[Sync.PartyMembers];
            for (int i = 0; i < party.Count; i++)
                megas[i] = party[i].MegaForm is int form && form != party[i].Form ? Local(party[i].Species, form, party[i].Slot) : (byte)0;
            Sync.BuildAppendix(megas, protocol).CopyTo(packet, Sync.PartyBytes);
            return packet;
        }

        /// <summary>The game's copy of a remote party. A patched game always rewrites the kept value: zero unless the appendix is valid.</summary>
        public List<Mon> Receive(byte[] packet, int command = 3)
        {
            var party = new List<Mon>();
            for (int i = 0; i < Sync.PartyMembers && packet[i * 0x104 + 4] == 1; i++)
                party.Add(new Mon(packet[i * 0x104], packet[i * 0x104 + 1], packet[i * 0x104 + 2], packet[i * 0x104 + 3] == 0xFF ? null : packet[i * 0x104 + 3]));
            foreach (var mon in party) mon.Ability = Local(mon.Species, mon.Form, mon.Slot);
            if (!patched) return party;
            byte[]? megas = protocol == Sync.ProtocolVersion && Sync.TryReadAppendix(packet, command, out var read) ? read : null;
            for (int i = 0; i < party.Count; i++) party[i].Stamp = megas is null ? (ushort)0 : Sync.Stamp(megas[i]);
            return party;
        }

        public List<Mon> Own(IReadOnlyList<Mon> party)
        {
            var copy = party.Select(m => m with { }).ToList();
            foreach (var mon in copy) mon.Ability = Local(mon.Species, mon.Form, mon.Slot);
            return copy;
        }

        /// <summary>Mega Evolution of one of this game's copies: the form change reads the local table, then the kept value wins.</summary>
        public byte MegaEvolve(Mon mon)
        {
            if (mon.MegaForm is not int form) return mon.Ability;
            mon.Mega = true;
            mon.Ability = Local(mon.Species, form, mon.Slot);
            if (patched && Sync.StampedAbility(mon.Stamp) is byte owner) mon.Ability = owner;
            return mon.Ability;
        }
    }

    /// <summary>One link battle: each game holds a copy of both parties; the server computes, both show.</summary>
    private sealed class Battle
    {
        public readonly Game[] Games;
        public readonly List<Mon>[][] Parties; // [game][owner]
        public readonly int Server;
        public Battle(Game first, Game second, IReadOnlyList<Mon> firstParty, IReadOnlyList<Mon> secondParty, int firstVersion = 0x6E, int secondVersion = 0x6E)
        {
            Games = [first, second];
            byte[][] wire = [first.Send(firstParty), second.Send(secondParty)];
            Parties =
            [
                [first.Own(firstParty), first.Receive(wire[1])],
                [second.Receive(wire[0]), second.Own(secondParty)],
            ];
            // The server is the first participant, in slot order, whose version is the highest.
            Server = secondVersion > firstVersion ? 1 : 0;
        }
        public (byte Server, byte OwnerView, byte OtherView) Mega(int owner, int member = 0)
        {
            byte[] seen = [Games[0].MegaEvolve(Parties[0][owner][member]), Games[1].MegaEvolve(Parties[1][owner][member])];
            return (seen[Server], seen[owner], seen[1 - owner]);
        }
        /// <summary>Damage a Ground or Fire move does to the target, as the server computes it.</summary>
        public int Hit(int target, string type) => (Parties[Server][target][0].Ability, type) switch { (Levitate, "ground") => 0, (FlashFire, "fire") => 0, _ => 100 };
        /// <summary>Attack stage of the opponent after the Mega's entry ability, as the server computes it.</summary>
        public int AttackStageAfterEntry(int megaOwner) => Parties[Server][megaOwner][0].Ability == Intimidate ? -1 : 0;
    }

    private static Dictionary<(int, int), byte[]> TableA() => new() { [(Charizard, 0)] = [Blaze, Blaze, Sturdy], [(Charizard, 1)] = [Levitate, Intimidate, Drizzle], [(Pikachu, 0)] = [Static, Static, Static] };
    private static Dictionary<(int, int), byte[]> TableB() => new() { [(Charizard, 0)] = [Sturdy, Sturdy, Sturdy], [(Charizard, 1)] = [FlashFire, Sturdy, Blaze], [(Pikachu, 0)] = [Static, Static, Static] };
    private static List<Mon> Team(int slot = 0) => [new(Charizard, 0, slot, 1), new(Pikachu, 0, 0, null)];

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        // Appendix: layout, checksum and the rules for accepting it.
        byte[] appendix = Sync.BuildAppendix([Levitate, 0, 0, 0, 0, 0]);
        check(appendix.Length == 12 && Encoding.ASCII.GetString(appendix, 0, 3) == "MAS" && appendix[3] == Sync.ProtocolVersion && appendix[4] == Levitate &&
            appendix[10] == (0x5A ^ Levitate) && appendix[11] == 0, "MegaSync_AppendixLayout");
        reject(() => Sync.BuildAppendix([1, 2, 3]), "MegaSync_AppendixNeedsSixAbilities");
        byte[] packet = new byte[Sync.PacketBytes]; appendix.CopyTo(packet, Sync.PartyBytes);
        check(Sync.TryReadAppendix(packet, 3, out var abilities) && abilities.SequenceEqual(new byte[] { Levitate, 0, 0, 0, 0, 0 }), "MegaSync_AppendixRoundTrip");
        check(!Sync.TryReadAppendix(packet, 4, out _), "MegaSync_OnlyThePartyCommandCarriesIt");
        check(!Sync.TryReadAppendix(packet.AsSpan(0, Sync.PartyBytes), 3, out _), "MegaSync_PartyWithoutAppendixIsNotSynchronised");
        check(!Sync.TryReadAppendix(packet.Concat(new byte[4]).ToArray(), 3, out _), "MegaSync_UnexpectedSizeIsRejected");
        check(!Sync.TryReadAppendix(Edit(packet, Sync.PartyBytes + 4, FlashFire), 3, out _), "MegaSync_WrongChecksumIsRejected");
        check(!Sync.TryReadAppendix(Edit(packet, Sync.PartyBytes, (byte)'X'), 3, out _), "MegaSync_WrongMarkIsRejected");
        byte[] other = new byte[Sync.PacketBytes]; Sync.BuildAppendix([Levitate, 0, 0, 0, 0, 0], 2).CopyTo(other, Sync.PartyBytes);
        check(!Sync.TryReadAppendix(other, 3, out _), "MegaSync_AnotherProtocolVersionIsRejected");
        check(Sync.Stamp(0) == 0 && Sync.Stamp(Levitate) == 0xA51A && Sync.StampedAbility(0xA51A) == Levitate && Sync.StampedAbility(0) is null &&
            Sync.StampedAbility(0x001A) is null && Sync.StampedAbility(0xFFFF) is null, "MegaSync_KeptValueIsUnambiguous");

        // Both patched, tables differ: each Mega keeps its owner's Ability in both games, whoever is the server.
        foreach (var (hostFirst, name) in new[] { (true, "AHosts"), (false, "BHosts") })
        {
            var a = new Game("A", TableA()); var b = new Game("B", TableB());
            var battle = hostFirst ? new Battle(a, b, Team(), Team()) : new Battle(b, a, Team(), Team());
            int ownerA = hostFirst ? 0 : 1, ownerB = 1 - ownerA;
            var megaA = battle.Mega(ownerA); var megaB = battle.Mega(ownerB);
            check(megaA == (Levitate, Levitate, Levitate), "MegaSync_OwnerAbilityInBothGames_A_" + name);
            check(megaB == (FlashFire, FlashFire, FlashFire), "MegaSync_OwnerAbilityInBothGames_B_" + name);
            check(battle.Hit(ownerA, "ground") == 0 && battle.Hit(ownerA, "fire") == 100, "MegaSync_ImmunityFollowsTheOwner_A_" + name);
            check(battle.Hit(ownerB, "fire") == 0 && battle.Hit(ownerB, "ground") == 100, "MegaSync_ImmunityFollowsTheOwner_B_" + name);
            check(battle.Parties[0][ownerA][1].Stamp == 0 && battle.Parties[1][ownerA][1].Stamp == 0, "MegaSync_PokemonWithoutMegaKeepsNothing_" + name);
        }

        // Same Mega and same slot in both parties, different Abilities per game; and the three slots.
        for (int slot = 0; slot < 3; slot++)
        {
            foreach (bool hostFirst in new[] { true, false })
            {
                var a = new Game("A", TableA()); var b = new Game("B", TableB());
                var battle = hostFirst ? new Battle(a, b, Team(slot), Team(slot)) : new Battle(b, a, Team(slot), Team(slot));
                int ownerA = hostFirst ? 0 : 1;
                byte expectA = TableA()[(Charizard, 1)][slot], expectB = TableB()[(Charizard, 1)][slot];
                check(battle.Mega(ownerA) == (expectA, expectA, expectA) && battle.Mega(1 - ownerA) == (expectB, expectB, expectB),
                    $"MegaSync_SameMegaSameSlotDifferentTables_Slot{slot}_{(hostFirst ? "AHosts" : "BHosts")}");
            }
        }

        // A conditional activation, not a message: the entry ability only acts when the owner's Mega has it.
        {
            var battle = new Battle(new Game("A", TableA()), new Game("B", TableB()), Team(1), Team(1));
            battle.Mega(0); battle.Mega(1);
            check(battle.AttackStageAfterEntry(0) == -1 && battle.AttackStageAfterEntry(1) == 0, "MegaSync_EntryAbilityActsOnlyForItsOwner");
            var inverted = new Battle(new Game("B", TableB()), new Game("A", TableA()), Team(1), Team(1));
            inverted.Mega(0); inverted.Mega(1);
            check(inverted.AttackStageAfterEntry(1) == -1 && inverted.AttackStageAfterEntry(0) == 0, "MegaSync_EntryAbilityActsOnlyForItsOwner_RolesInverted");
        }

        // Without the patch the result depends on who is the server: this is the reported defect.
        {
            var battle = new Battle(new Game("A", TableA(), false), new Game("B", TableB(), false), Team(), Team());
            var megaB = battle.Mega(1);
            check(megaB == (Levitate, FlashFire, Levitate) && battle.Hit(1, "fire") == 100, "MegaSync_WithoutThePatchTheServerTableDecides");
            var inverted = new Battle(new Game("B", TableB(), false), new Game("A", TableA(), false), Team(), Team());
            check(inverted.Mega(0) == (FlashFire, FlashFire, Levitate) && inverted.Hit(0, "fire") == 0, "MegaSync_WithoutThePatchTheResultChangesWithTheRoles");
        }

        // Only one side patched, or protocols that differ: both games behave exactly as without the patch.
        foreach (var (a, b, name) in new[]
        {
            (new Game("A", TableA()), new Game("B", TableB(), false), "RemoteWithoutPatch"),
            (new Game("A", TableA(), false), new Game("B", TableB()), "LocalWithoutPatch"),
            (new Game("A", TableA()), new Game("B", TableB(), true, 2), "IncompatibleProtocol"),
        })
        {
            var battle = new Battle(a, b, Team(), Team());
            var stock = new Battle(new Game("A", TableA(), false), new Game("B", TableB(), false), Team(), Team());
            check(battle.Mega(0) == stock.Mega(0) && battle.Mega(1) == stock.Mega(1), "MegaSync_NotBilateralMeansOriginalBehaviour_" + name);
            check(battle.Parties.SelectMany(g => g).SelectMany(p => p).All(m => m.Stamp == 0), "MegaSync_NotBilateralKeepsNothing_" + name);
        }

        // Events: repeated, out of order, late, and missing information.
        {
            var a = new Game("A", TableA()); var b = new Game("B", TableB());
            var battle = new Battle(a, b, Team(), Team());
            check(battle.Mega(1) == battle.Mega(1) && battle.Mega(1).Server == FlashFire, "MegaSync_RepeatedMegaEvolutionChangesNothing");
            var reversed = new Battle(a, b, Team(), Team());
            var second = reversed.Mega(1); var first = reversed.Mega(0);
            check(first == (Levitate, Levitate, Levitate) && second == (FlashFire, FlashFire, FlashFire), "MegaSync_OrderOfMegaEvolutionsDoesNotMatter");
            byte[] wire = b.Send(Team());
            var twice = a.Receive(wire); var again = a.Receive(wire);
            check(twice[0].Stamp == again[0].Stamp && again[0].Stamp == Sync.Stamp(FlashFire), "MegaSync_RepeatedPartyPacketGivesTheSameResult");
            var stale = a.Receive(wire.AsSpan(0, Sync.PartyBytes).ToArray());
            check(stale[0].Stamp == 0 && a.MegaEvolve(stale[0]) == Levitate, "MegaSync_ShortPacketAfterAValidOneIsNotSynchronised");
            var lone = new List<Mon> { new(Pikachu, 0, 0, null) };
            var single = a.Receive(b.Send(lone));
            check(single.Count == 1 && single[0].Stamp == 0 && a.MegaEvolve(single[0]) == Static, "MegaSync_ShortPartyAndNoMegaAreSafe");
            var forged = a.Receive(new Game("F", TableB(), false).Send(Team()));
            forged[0].Stamp = 0; // a patched receiver rewrites the kept value of every remote Pokémon
            check(a.MegaEvolve(forged[0]) == Levitate, "MegaSync_RemoteDataCannotCarryAValueWithoutAppendix");
        }

        // Offline: the player's own party and an opponent that never came through the network use the local table.
        {
            var a = new Game("A", TableA());
            var own = a.Own(Team(1));
            check(a.MegaEvolve(own[0]) == Intimidate && own[0].Stamp == 0, "MegaSync_OfflineIsUnchanged");
        }

        // Version election: a higher server version on the second participant makes it the server; the result is the same.
        {
            var battle = new Battle(new Game("A", TableA()), new Game("B", TableB()), Team(), Team(), 0x6E, 0x6F);
            check(battle.Server == 1 && battle.Mega(0) == (Levitate, Levitate, Levitate) && battle.Mega(1) == (FlashFire, FlashFire, FlashFire), "MegaSync_ServerElectionDoesNotChangeTheResult");
        }
    }

    private static byte[] Edit(byte[] data, int at, byte value) { var copy = (byte[])data.Clone(); copy[at] = value; return copy; }

    // ---------------------------------------------------------------------------------------------------------
    // Synthetic binaries
    // ---------------------------------------------------------------------------------------------------------
    private static readonly string[] Symbols =
    [
        "_ZNK3pml9PokeParty22SerializeToShortStructEPv", "_ZN3pml9PokeParty26DeserializeFromShortStructEPKv", "_ZN3pml9PokeParty27GetSerializeShortStructSizeEv",
        "_ZNK3pml9PokeParty14GetMemberCountEv", "_ZNK3pml9PokeParty21GetMemberPointerConstEj", "_ZN3pml9PokeParty16GetMemberPointerEj",
        "_ZNK3pml8pokepara9CoreParam9GetFormNoEv", "_ZNK3pml8pokepara9CoreParam9GetMonsNoEv", "_ZN3pml8pokepara9CoreParam11SetMemoriesENS0_8MemoriesEj",
        "_ZN3pml8pokepara9CoreParam10MegaEvolveEv", "_ZN3pml8personal16LoadPersonalDataE6MonsNoh", "_ZN3pml8personal16GetPersonalParamENS0_7ParamIDE",
    ];
    private const int StubBase = 0x200, SizeSite = 0x2010, SendSite = 0x2020, ReceiveSite = 0x2110, MegaSite = 0x2210, NetLiteral = 0x2300, Helper = 0x2400, ClientBuffer = 0x2500;
    private static void Set32(byte[] data, int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
    private static uint Get32(byte[] data, int at) => BitConverter.ToUInt32(data, at);
    private static uint Bl(int from, int to, uint op = 0xEB000000) => op | ((uint)((to - (from + 8)) >> 2) & 0x00FFFFFF);
    private static int Stub(string symbol) => StubBase + Array.IndexOf(Symbols, symbol) * 8;
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>
    /// A CRO0 skeleton shaped like the part of Battle.cro the patch touches: named imports with their veneers, four
    /// routines with the hooked calls, the Personal helper and a word relocated to .bss. It contains no game code.
    /// </summary>
    internal static byte[] Module(bool tight = false)
    {
        byte[] cro = DefinitiveGameDataCapsChecks.StockBattle();
        Review73Checks.Cro().AsSpan(0x180, 0x40).CopyTo(cro.AsSpan(0x10000)); // the two Persistent Items gates
        Set32(cro, 0xC0, 0x30000);
        const int table = 0x30900, names = 0x30A00, patches = 0x30F00;
        int name = names;
        for (int i = 0; i < Symbols.Length; i++)
        {
            int stub = StubBase + i * 8;
            Set32(cro, stub, 0xE51FF004);
            Set32(cro, table + i * 8, (uint)name); Set32(cro, table + i * 8 + 4, (uint)(patches + i * 12));
            Encoding.ASCII.GetBytes(Symbols[i]).CopyTo(cro, name); name += Symbols[i].Length + 1;
            Set32(cro, patches + i * 12, (uint)((stub + 4 - 0x180) << 4)); cro[patches + i * 12 + 4] = 2; cro[patches + i * 12 + 5] = 1;
        }
        Set32(cro, 0x100, table); Set32(cro, 0x104, (uint)Symbols.Length); Set32(cro, 0x118, names); Set32(cro, 0x11C, (uint)(name - names));
        foreach (var (start, end) in Ranges)
            for (uint at = start; at < end; at += 4) Set32(cro, (int)at, 0xE1A00000 | (at & 0xF00)); // placeholder instructions
        Set32(cro, SizeSite, Bl(SizeSite, Stub(Symbols[2]))); Set32(cro, SendSite, Bl(SendSite, Stub(Symbols[0])));
        Set32(cro, ReceiveSite, Bl(ReceiveSite, Stub(Symbols[1]))); Set32(cro, MegaSite, Bl(MegaSite, Stub(Symbols[9])));
        Set32(cro, Helper + 0x10, Bl(Helper + 0x10, Stub(Symbols[10]))); Set32(cro, Helper + 0x1C, Bl(Helper + 0x1C, Stub(Symbols[11]), 0xEA000000));
        Set32(cro, 0x32000, (uint)((NetLiteral - 0x180) << 4)); cro[0x32004] = 2; cro[0x32005] = 3; Set32(cro, 0x32008, 0x3DF8);
        if (tight)
        {
            Array.Copy(cro, 0x140, cro, 0x30800, 0x40); Array.Clear(cro, 0x140, 0x40); Set32(cro, 0xC8, 0x30800);
            for (int i = 0x16000; i < 0x30000; i++) cro[i] = 0xAA;
        }
        return CroIntegrity.Rebuild(cro); // valid integrity hashes, as a real module has
    }
    private static readonly (uint Start, uint End)[] Ranges = [(0x2000, 0x2040), (0x2100, 0x2120), (0x2200, 0x2220), (0x2400, 0x2420), (0x2500, 0x2520)];
    internal static MegaAbilitySyncProfile Profile()
    {
        var profile = new MegaAbilitySyncProfile("Synthetic", SizeSite, SendSite, ReceiveSite, MegaSite, NetLiteral, 0x3DF8, Helper, ClientBuffer, Ranges, "");
        return profile with { DependencySha256 = profile.ComputeDependencySha256(Module()) };
    }

    /// <summary>static.crs and code.bin with the shapes the address resolution accepts, shifted by <paramref name="shift"/> like a second game.</summary>
    internal static (byte[] Crs, byte[] Code, MegaAbilitySyncCodeAddresses Expected) StaticModule(uint shift = 0)
    {
        byte[] code = new byte[0x1200];
        uint B(uint a) => a - 0x100000;
        uint mega = 0x100100 + shift, setIndex = 0x100200 + shift, getIndex = 0x100300 + shift, megaForm = 0x100400 + shift, setNo = 0x100500 + shift;
        uint get = 0x100600 + shift, getCase = 0x100700 + shift, getField = 0x100800 + shift, block = 0x100900 + shift, set = 0x100A00 + shift, setCase = 0x100B00 + shift, setField = 0x100C00 + shift;
        Set32(code, (int)B(mega + 0x10), 0xE59F6058); Set32(code, (int)B(mega + 0x70), 0x0066F0D0); Set32(code, (int)B(mega + 0x1C), 0xE5960000);
        Set32(code, (int)B(mega + 0x20), Bl((int)(mega + 0x20), (int)megaForm));
        Set32(code, (int)B(setIndex + 0x78), 0xE594000C); Set32(code, (int)B(setIndex + 0x7C), 0xE1A01006); Set32(code, (int)B(setIndex + 0x80), 0xE8BD4070);
        Set32(code, (int)B(setIndex + 0x84), Bl((int)(setIndex + 0x84), (int)setNo, 0xEA000000)); Set32(code, (int)B(setNo + 0x1C), 0xE5C0400C);
        Set32(code, (int)B(get), 0xE3510012); Set32(code, (int)B(get + 0x10 + 64), getCase); Set32(code, (int)B(getCase + 8), Bl((int)(getCase + 8), (int)getField, 0xEA000000));
        Set32(code, (int)B(getField + 0x14), Bl((int)(getField + 0x14), (int)block)); Set32(code, (int)B(getField + 0x18), 0xE1D043B0);
        Set32(code, (int)B(set), 0xE3510012); Set32(code, (int)B(set + 0x0C + 64), setCase); Set32(code, (int)B(setCase + 4), 0xE6FF1072);
        Set32(code, (int)B(setCase + 8), Bl((int)(setCase + 8), (int)setField, 0xEA000000));
        Set32(code, (int)B(setField + 0x18), Bl((int)(setField + 0x18), (int)block)); Set32(code, (int)B(setField + 0x1C), 0xE1C043B0);

        (string Name, uint Address)[] exports =
        [
            ("_ZN3pml8pokepara9CoreParam10MegaEvolveEv", mega), ("_ZN3pml8pokepara9CoreParam15SetTokuseiIndexEh", setIndex),
            ("_ZNK3pml8pokepara9CoreParam15GetTokuseiIndexEv", getIndex), ("_ZNK3pml8pokepara9CoreParam11GetMemoriesENS0_8MemoriesE", get),
            ("_ZN3pml8pokepara9CoreParam11SetMemoriesENS0_8MemoriesEj", set),
        ];
        byte[] crs = new byte[0x800];
        Set32(crs, 0x80, 0x304F5243); Set32(crs, 0xC8, 0x140); Set32(crs, 0xCC, 1); Set32(crs, 0x140, 0x100000); Set32(crs, 0x144, 0x1200);
        Set32(crs, 0xD0, 0x200); Set32(crs, 0xD4, (uint)exports.Length);
        int at = 0x300;
        for (int i = 0; i < exports.Length; i++)
        {
            Set32(crs, 0x200 + i * 8, (uint)at); Set32(crs, 0x200 + i * 8 + 4, (exports[i].Address - 0x100000) << 4);
            Encoding.ASCII.GetBytes(exports[i].Name).CopyTo(crs, at); at += exports[i].Name.Length + 1;
        }
        return (crs, code, new(0x0066F0D0, megaForm, getIndex, get, setNo));
    }

    internal static void RunBinary(Action<bool, string> check, Action<Action, string> reject)
    {
        var profile = Profile();
        var (crs, code, expected) = StaticModule();

        // Addresses come from the game's own files and are refused when the shape is not the expected one.
        check(Sync.TryResolveCodeAddresses(crs, code, out var addresses, out _) && addresses == expected, "MegaSyncBinary_AddressesResolvedFromTheGameFiles");
        var (crs2, code2, expected2) = StaticModule(0x20);
        check(Sync.TryResolveCodeAddresses(crs2, code2, out var shifted, out _) && shifted == expected2 && shifted != expected, "MegaSyncBinary_AnotherGameResolvesItsOwnAddresses");
        check(!Sync.TryResolveCodeAddresses(crs, code2, out _, out string mixed) && mixed.Length != 0, "MegaSyncBinary_FilesOfDifferentGamesAreRefused");
        check(!Sync.TryResolveCodeAddresses(null, code, out _, out _) && !Sync.TryResolveCodeAddresses(crs, null, out _, out _) &&
            !Sync.TryResolveCodeAddresses(crs, new byte[0x1201], out _, out _), "MegaSyncBinary_MissingOrCompressedFilesAreRefused");
        check(!Sync.TryResolveCodeAddresses(crs, Edit(code, 0x100 + 0x1C, 1), out _, out string shape) && shape.Contains("MegaEvolve"), "MegaSyncBinary_ChangedMegaEvolveIsRefused");
        check(!Sync.TryResolveCodeAddresses(crs, Edit(code, 0x500 + 0x1C, 1), out _, out _), "MegaSyncBinary_ChangedAbilityStoreIsRefused");
        check(!Sync.TryResolveCodeAddresses(crs, Edit(code, 0xC00 + 0x1C, 1), out _, out string field) && field.Contains("16 bits") || field.Contains("16-bit"), "MegaSyncBinary_ChangedAuxiliaryFieldIsRefused");
        byte[] renamed = (byte[])crs.Clone(); renamed[0x300 + 4] = (byte)'X';
        check(!Sync.TryResolveCodeAddresses(renamed, code, out _, out string missing) && missing.Contains("static.crs"), "MegaSyncBinary_MissingExportIsRefused");

        // The block for the real layout is the reference one (hash of the assembled reference for each game).
        var stubs = new Dictionary<string, uint>
        {
            [Symbols[0]] = 0x318, [Symbols[1]] = 0x320, [Symbols[2]] = 0x310, [Symbols[3]] = 0x288, [Symbols[4]] = 0x290, [Symbols[5]] = 0x2F8,
            [Symbols[6]] = 0x538, [Symbols[7]] = 0x298, [Symbols[8]] = 0x1FD0, [Symbols[9]] = 0x1810, [Symbols[10]] = 0x17E8, [Symbols[11]] = 0x17F0,
        };
        var ultraSun = new MegaAbilitySyncCodeAddresses(0x66F0D0, 0x4ABE34, 0x4AE80C, 0x4AE1EC, 0x321758);
        var ultraMoon = new MegaAbilitySyncCodeAddresses(0x66F0D0, 0x4ABE3C, 0x4AE814, 0x4AE1F4, 0x321758);
        check(Sha(Sync.BuildPayload(0xFC974, ultraSun, MegaAbilitySyncProfile.Usum, stubs)) == "7BD8F5BD80C96523E11DCA64CFE29AD8E461E08916024EA8BD594CEABA41EE37" &&
            Sha(Sync.BuildPayload(0x100000, ultraSun, MegaAbilitySyncProfile.Usum, stubs)) == "5BC0E7E638B52FD6D56ACA0ED9C290E04230F98EE179085F70E38E2D4519CF02", "MegaSyncBinary_BlockMatchesTheReference_UltraSun");
        check(Sha(Sync.BuildPayload(0xFC974, ultraMoon, MegaAbilitySyncProfile.Usum, stubs)) == "157F7DBD9CC1F793569E859E149C6EC5BFA280A9427FB8A176C82C66F7ED737E" &&
            Sha(Sync.BuildPayload(0x100000, ultraMoon, MegaAbilitySyncProfile.Usum, stubs)) == "A980C0F4F404304CE9937D9460160D1B7DE1F855303ACC11967644B2EC8822A4", "MegaSyncBinary_BlockMatchesTheReference_UltraMoon");
        // The first published routine is still recognised exactly as it was installed.
        check(Sha(Sync.BuildPayload(0xFC974, ultraSun, MegaAbilitySyncProfile.Usum, stubs, earlierFormat: true)) == "A822A148F7472FD276011ECB43F040F2C0C0383A78A28D1189626CF4A9EC839C" &&
            Sha(Sync.BuildPayload(0x100000, ultraSun, MegaAbilitySyncProfile.Usum, stubs, earlierFormat: true)) == "515E958E3EB44291F64768A615950FE491C252EB6557F5B9FF2993A96F85595E" &&
            Sha(Sync.BuildPayload(0xFC974, ultraMoon, MegaAbilitySyncProfile.Usum, stubs, earlierFormat: true)) == "DC34481869BEFC5F6E44E2CFDC1203E08F74198F7B6F8C0306AF13B2C801B1C3" &&
            Sha(Sync.BuildPayload(0x100000, ultraMoon, MegaAbilitySyncProfile.Usum, stubs, earlierFormat: true)) == "3B2357912FA934B4331D24C4175E8A5C1CB7108F91D148E518EC1709FC1BA3BD", "MegaSyncBinary_EarlierBlockMatchesWhatWasPublished");
        check(Sync.PayloadBytes == 624 && Sync.PayloadFormat == 2 && Sync.ProtocolVersion == 1 && Sync.PacketBytes == 0x624, "MegaSyncBinary_Sizes");

        foreach (bool tight in new[] { false, true })
        {
            string name = tight ? "GrowingModule" : "RoomyModule";
            byte[] stock = Module(tight); string stockHash = Sha(stock);
            check(Sync.TryReadImportStubs(stock, out var read, out _) && Symbols.All(s => read[s] == (uint)Stub(s)), "MegaSyncBinary_ImportVeneersFoundByName_" + name);
            var before = Sync.Inspect(stock, addresses, profile);
            check(before.State == MegaAbilitySyncState.Original && before.Sites.Count == 4 && before.Sites.All(s => s.State == State.Original), "MegaSyncBinary_OriginalIsRecognised_" + name);

            check(Sync.TryInstall(stock, addresses, profile, out byte[] installed, out string error) && error.Length == 0 && Sha(stock) == stockHash, "MegaSyncBinary_InstallsOnACopy_" + name);
            var after = Sync.Inspect(installed, addresses, profile);
            check(after.State == MegaAbilitySyncState.Installed && after.PayloadOffset is uint at && after.Sites.All(s => s.State == State.OwnInstall) &&
                installed.AsSpan((int)at, Sync.PayloadBytes).SequenceEqual(Sync.BuildPayload(at, addresses, profile, read)), "MegaSyncBinary_InstalledIsReadBack_" + name);
            check((installed.Length != stock.Length) == tight, "MegaSyncBinary_GrowsOnlyWithoutRoom_" + name);
            check(new[] { SizeSite, SendSite, ReceiveSite, MegaSite }.All(site => Get32(installed, site) >> 24 == 0xEB && Get32(installed, site) != Get32(stock, site)) &&
                profile.ComputeDependencySha256(installed) == profile.DependencySha256, "MegaSyncBinary_OnlyTheFourCallsChangeInTheRoutines_" + name);
            check(Sync.Inspect(installed, null, profile).State == MegaAbilitySyncState.Installed, "MegaSyncBinary_InstallationRecognisedWithoutCodeBin_" + name);

            check(Sync.TryInstall(installed, addresses, profile, out byte[] again, out _) && again.SequenceEqual(installed), "MegaSyncBinary_ReinstallingChangesNothing_" + name);
            check(Sync.TryUninstall(installed, addresses, profile, out byte[] removed, out _) && Sync.Inspect(removed, addresses, profile).State == MegaAbilitySyncState.Original &&
                new[] { SizeSite, SendSite, ReceiveSite, MegaSite }.All(site => Get32(removed, site) == Get32(stock, site)), "MegaSyncBinary_UninstallRestoresTheFourCalls_" + name);
            if (!tight) check(removed.SequenceEqual(stock), "MegaSyncBinary_UninstallIsByteIdentical_" + name);
            check(Sync.TryUninstall(stock, addresses, profile, out byte[] untouched, out _) && untouched.SequenceEqual(stock), "MegaSyncBinary_UninstallingTheOriginalChangesNothing_" + name);
            check(Sync.TryInstall(removed, addresses, profile, out byte[] back, out _) && Sync.Inspect(back, addresses, profile).State == MegaAbilitySyncState.Installed &&
                (tight || back.SequenceEqual(installed)), "MegaSyncBinary_ReinstallAfterUninstall_" + name);

            // The routine published first: recognised byte for byte, replaced in place by installing again, and removable.
            const int earlierBytes = 636;
            check(installed.AsSpan((int)after.PayloadOffset!.Value + Sync.PayloadBytes, earlierBytes - Sync.PayloadBytes).IndexOfAnyExcept((byte)0) < 0, "MegaSyncEarlier_FixtureHasRoom_" + name);
            byte[] earlier = (byte[])installed.Clone();
            uint earlierAt = after.PayloadOffset!.Value;
            Sync.BuildPayload(earlierAt, addresses, profile, read, earlierFormat: true).CopyTo(earlier, (int)earlierAt);
            foreach (var (site, entry) in new[] { (SizeSite, 2), (SendSite, 5), (ReceiveSite, 70), (MegaSite, 135) })
                Set32(earlier, site, Bl(site, (int)earlierAt + entry * 4));
            earlier = CroIntegrity.Rebuild(earlier);
            string earlierHash = Sha(earlier);
            var old = Sync.Inspect(earlier, addresses, profile);
            check(old.State == MegaAbilitySyncState.InstalledEarlierFormat && old.PayloadOffset == earlierAt && old.Sites.All(s => s.State == State.OwnInstallEarlierFormat) &&
                Sync.Inspect(earlier, null, profile).State == MegaAbilitySyncState.InstalledEarlierFormat, "MegaSyncEarlier_IsRecognised_" + name);
            check(Sync.TryInstall(earlier, addresses, profile, out byte[] replaced, out string replaceError) && replaceError.Length == 0 && replaced.SequenceEqual(installed) && Sha(earlier) == earlierHash,
                "MegaSyncEarlier_InstallingAgainLeavesExactlyTheCurrentInstallation_" + name);
            check(Sync.TryUninstall(earlier, addresses, profile, out byte[] earlierRemoved, out _) && earlierRemoved.SequenceEqual(removed), "MegaSyncEarlier_CanBeRemoved_" + name);
            byte[] earlierDamaged = (byte[])earlier.Clone(); earlierDamaged[(int)earlierAt + 0x150] ^= 1;
            check(Sync.Inspect(earlierDamaged, addresses, profile).State == MegaAbilitySyncState.Partial && !Sync.TryInstall(earlierDamaged, addresses, profile, out _, out _) &&
                !Sync.TryUninstall(earlierDamaged, addresses, profile, out _, out _), "MegaSyncEarlier_ChangedBlockIsNotAccepted_" + name);
            check(Sync.Inspect(earlier, shifted, profile).State == MegaAbilitySyncState.Partial, "MegaSyncEarlier_BlockOfAnotherGameIsNotAccepted_" + name);
            byte[] mixed2 = (byte[])earlier.Clone(); Set32(mixed2, MegaSite, Get32(installed, MegaSite));
            check(Sync.Inspect(mixed2, addresses, profile).State == MegaAbilitySyncState.Partial && !Sync.TryInstall(mixed2, addresses, profile, out _, out _), "MegaSyncEarlier_MixedLayoutsAreRefused_" + name);
            check(GameMegaAbilitySyncService.InspectCapability(earlier, crs, code, profile) is { CanApply: true, Level: Level.Verified } upgrade && upgrade.Changes.Count >= 2 &&
                upgrade.Resources.All(r => r.State == State.OwnInstallEarlierFormat) && upgrade.Summary.Contains("rutina anterior", StringComparison.Ordinal), "MegaSyncEarlier_CapabilityExplainsTheReplacement_" + name);
            check(GameMegaAbilitySyncService.Patch(earlier, crs, code, true, profile) is { Changed: true, Installed: true } upgraded && upgraded.Bytes.SequenceEqual(installed) &&
                GameMegaAbilitySyncService.Patch(earlier, crs, code, false, profile) is { Changed: true, Installed: false }, "MegaSyncEarlier_EditorReplacesOrRemoves_" + name);

            // Another game's addresses in the installed block: not this installation.
            check(Sync.Inspect(installed, shifted, profile).State == MegaAbilitySyncState.Partial && !Sync.TryInstall(installed, shifted, profile, out _, out _), "MegaSyncBinary_BlockOfAnotherGameIsNotAccepted_" + name);

            // Conflicts and unknown modules are refused, never forced.
            byte[] foreign = (byte[])stock.Clone(); Set32(foreign, MegaSite, Bl(MegaSite, 0x8000));
            check(Sync.Inspect(foreign, addresses, profile).State == MegaAbilitySyncState.External && !Sync.TryInstall(foreign, addresses, profile, out _, out string foreignError) && foreignError.Length != 0, "MegaSyncBinary_ForeignHookIsAConflict_" + name);
            byte[] notCall = (byte[])stock.Clone(); Set32(notCall, SendSite, 0xE1A00000);
            check(Sync.Inspect(notCall, addresses, profile).State == MegaAbilitySyncState.External, "MegaSyncBinary_ReplacedInstructionIsAConflict_" + name);
            byte[] half = (byte[])installed.Clone(); Set32(half, ReceiveSite, Get32(stock, ReceiveSite));
            check(Sync.Inspect(half, addresses, profile).State == MegaAbilitySyncState.Partial && !Sync.TryInstall(half, addresses, profile, out _, out _) &&
                !Sync.TryUninstall(half, addresses, profile, out _, out _), "MegaSyncBinary_PartialInstallationIsRefused_" + name);
            byte[] damaged = (byte[])installed.Clone(); damaged[(int)after.PayloadOffset!.Value + 0x40] ^= 1;
            check(Sync.Inspect(damaged, addresses, profile).State == MegaAbilitySyncState.Partial, "MegaSyncBinary_ChangedBlockIsNotAccepted_" + name);
            byte[] olderFormat = (byte[])installed.Clone(); Set32(olderFormat, (int)after.PayloadOffset!.Value + 4, 0);
            check(Sync.Inspect(olderFormat, addresses, profile).Sites.All(s => s.State == State.OwnInstallEarlierFormat), "MegaSyncBinary_OtherFormatIsReported_" + name);
            byte[] changedRoutine = (byte[])stock.Clone(); Set32(changedRoutine, 0x2104, 0xE1A01001);
            check(Sync.Inspect(changedRoutine, addresses, profile) is { State: MegaAbilitySyncState.Unsupported } routine && routine.Detail.Contains("no son las originales") &&
                !Sync.TryInstall(changedRoutine, addresses, profile, out _, out _), "MegaSyncBinary_ChangedRoutineIsRefused_" + name);
            byte[] noReloc = (byte[])stock.Clone(); Set32(noReloc, 0x32008, 0x3DFC);
            check(Sync.Inspect(noReloc, addresses, profile).State == MegaAbilitySyncState.Unsupported, "MegaSyncBinary_MissingNetworkStateReferenceIsRefused_" + name);
            byte[] noImport = (byte[])stock.Clone(); noImport[0x30A00 + 5] = (byte)'X';
            check(Sync.Inspect(noImport, addresses, profile).State == MegaAbilitySyncState.Unsupported, "MegaSyncBinary_MissingImportIsRefused_" + name);
            byte[] noVeneer = (byte[])stock.Clone(); Set32(noVeneer, Stub(Symbols[8]), 0);
            check(Sync.Inspect(noVeneer, addresses, profile).State == MegaAbilitySyncState.Unsupported, "MegaSyncBinary_MissingVeneerIsRefused_" + name);
            check(Sync.Inspect(new byte[0x400], addresses, profile).State == MegaAbilitySyncState.Unsupported && Sync.Inspect(stock, addresses).State == MegaAbilitySyncState.Unsupported,
                "MegaSyncBinary_OtherModulesAreRefused_" + name);
            check(!Sync.TryInstall(stock, null, profile, out _, out _), "MegaSyncBinary_NoAddressesNoInstallation_" + name);

            // Capability report: same decision as the installer, and it never writes.
            var cases = new (string Name, byte[] Cro)[] { ("Original", stock), ("Installed", installed), ("Foreign", foreign), ("Half", half), ("ChangedRoutine", changedRoutine), ("NotACro", new byte[0x400]) };
            foreach (var (label, data) in cases)
            {
                string hash = Sha(data);
                var report = GameMegaAbilitySyncService.InspectCapability(data, crs, code, profile);
                bool installs; try { _ = GameMegaAbilitySyncService.Patch(data, crs, code, true, profile); installs = true; } catch (InvalidDataException) { installs = false; }
                check(report.CanApply == installs && Sha(data) == hash && report.PatchId == "usum.mega-ability-sync", $"MegaSyncCapability_AgreesWithTheInstaller_{label}_{name}");
            }
            var capability = GameMegaAbilitySyncService.InspectCapability(stock, crs, code, profile);
            check(capability.Level == (tight ? Level.CompatibleByAdaptation : Level.Verified) && (tight ? capability.Resized.Contains("Battle.cro") : capability.Changes.Count >= 5),
                "MegaSyncCapability_LevelFollowsTheSpaceNeeded_" + name);
            check(GameMegaAbilitySyncService.InspectCapability(installed, crs, code, profile) is { CanApply: true } own && own.Resources.All(r => r.State == State.OwnInstall) && own.Changes.Count == 0 &&
                own.Summary.StartsWith("El parche está instalado.", StringComparison.Ordinal), "MegaSyncCapability_OwnInstallationChangesNothing_" + name);
            check(GameMegaAbilitySyncService.InspectCapability(foreign, crs, code, profile).Level == Level.Conflict &&
                GameMegaAbilitySyncService.InspectCapability(half, crs, code, profile).Level == Level.NotAccredited &&
                GameMegaAbilitySyncService.InspectCapability(changedRoutine, crs, code, profile).Level == Level.NotAccredited, "MegaSyncCapability_ConflictAndInsufficientEvidence_" + name);

            // Coexistence with the other two Battle.cro patches, in both orders.
            var table = PlayerLevelCapRecipeChecks.Table(); byte[] capsCode = DefinitiveGameDataCapsChecks.StockCode();
            bool Mega(byte[] battle) => Sync.Inspect(battle, addresses, profile).State == MegaAbilitySyncState.Installed;
            bool Caps(byte[] battle, byte[] codeBin) => LevelCapPatch.InspectBattle(battle, out var t) == LevelCapCopyState.Applied && t.ToBytes().SequenceEqual(table.ToBytes()) &&
                LevelCapPatch.InspectCapability(battle, codeBin).Resources.All(r => r.State == State.OwnInstall);
            bool Items(byte[] battle) => GamePersistentItemsService.InspectCapability(battle).Resources.All(r => r.State == State.OwnInstall);
            var capsFirst = LevelCapPatch.InstallManaged(stock, capsCode, table);
            check(capsFirst.Success && Sync.Inspect(capsFirst.BattleCro, addresses, profile).State == MegaAbilitySyncState.Original, "MegaSyncInteraction_CapsLeaveTheSitesOriginal_" + name);
            byte[] capsItems = GamePersistentItemsService.Patch(capsFirst.BattleCro).Bytes;
            check(Sync.TryInstall(capsItems, addresses, profile, out byte[] all, out _) && Mega(all) && Caps(all, capsFirst.CodeBin) && Items(all), "MegaSyncInteraction_AfterCapsAndItemsAllThreeRemain_" + name);
            var megaThenCaps = LevelCapPatch.InstallManaged(GamePersistentItemsService.Patch(installed).Bytes, capsCode, table);
            check(megaThenCaps.Success && Mega(megaThenCaps.BattleCro) && Caps(megaThenCaps.BattleCro, megaThenCaps.CodeBin) && Items(megaThenCaps.BattleCro), "MegaSyncInteraction_BeforeCapsAndItemsAllThreeRemain_" + name);
            check(Sync.TryUninstall(all, addresses, profile, out byte[] others, out _) && !Mega(others) && Caps(others, capsFirst.CodeBin) && Items(others), "MegaSyncInteraction_UninstallKeepsTheOtherPatches_" + name);
        }

        check(GameMegaAbilitySyncService.InspectCapability(null, crs, code, profile) is { Level: Level.NotAccredited } none && none.Resources.All(r => r.State == State.Missing) &&
            GameMegaAbilitySyncService.InspectCapability(Module(), null, code, profile).Level == Level.NotAccredited &&
            GameMegaAbilitySyncService.InspectCapability(Module(), crs, null, profile).Level == Level.NotAccredited, "MegaSyncCapability_MissingFilesAreInsufficientData");
        check(GameMegaAbilitySyncService.InspectCapability(Module(), crs, Edit(code, 0x100 + 0x1C, 1), profile) is { Level: Level.NotAccredited, CanApply: false }, "MegaSyncCapability_UnknownCodeBinIsNotAccredited");
    }

    /// <summary>The editor on a synthetic project: preview, apply, history, read back, remove, and refusals.</summary>
    internal static void RunService(Action<bool, string> check)
    {
        var profile = Profile(); var (crs, code, _) = StaticModule();
        using var state = new TestState();
        using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
        {
            string battle = Path.Combine(f.Game.RomFS, "Battle.cro"), codePath = Path.Combine(f.Game.ExeFS, "code.bin"), crsPath = Path.Combine(f.Game.RomFS, "static.crs");
            File.WriteAllBytes(battle, Module()); File.WriteAllBytes(crsPath, crs); File.WriteAllBytes(codePath, code);
            string codeHash = Sha(code), crsHash = Sha(crs);
            var service = new GameMegaAbilitySyncService(f.Session, profile);
            check(service.Title == "Mega Ability Sync · USUM" && service.Entries.Count == 1 && EditorValues.Flatten(service.Read(1))["Enable"] == 0, "MegaSyncService_StartsNotInstalled");
            var preview = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
            check(preview.Files.Count == 1 && preview.Changes.Count == 1 && File.ReadAllBytes(battle).SequenceEqual(Module()), "MegaSyncService_PreviewWritesNothing");
            service.Apply(preview);
            check(EditorValues.Flatten(service.Read(1))["Enable"] == 1 && f.Session.History.Read().Entries.Count == 1 &&
                Sha(File.ReadAllBytes(codePath)) == codeHash && Sha(File.ReadAllBytes(crsPath)) == crsHash, "MegaSyncService_ApplyInstallsRecordsAndLeavesTheOtherFiles");
            var same = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
            service.Apply(same);
            check(same.Changes.Count == 0 && f.Session.History.Read().Entries.Count == 1, "MegaSyncService_InstallingAgainIsNotAnOperation");
            service.Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
            check(EditorValues.Flatten(service.Read(1))["Enable"] == 0 && File.ReadAllBytes(battle).SequenceEqual(Module()) && f.Session.History.Read().Entries.Count == 2, "MegaSyncService_RemovingRestoresTheOriginalFile");
            // A project that still carries the routine published first: shown as installed, replaced by applying 1.
            {
                byte[] current = Module(); check(Sync.TryResolveCodeAddresses(crs, code, out var resolved, out _) && Sync.TryInstall(current, resolved, profile, out current, out _), "MegaSyncService_EarlierFixture");
                uint at = Sync.Inspect(current, resolved, profile).PayloadOffset!.Value; Sync.TryReadImportStubs(current, out var veneers, out _);
                byte[] earlier = (byte[])current.Clone(); Sync.BuildPayload(at, resolved, profile, veneers, earlierFormat: true).CopyTo(earlier, (int)at);
                foreach (var (site, entry) in new[] { (SizeSite, 2), (SendSite, 5), (ReceiveSite, 70), (MegaSite, 135) }) Set32(earlier, site, Bl(site, (int)at + entry * 4));
                File.WriteAllBytes(battle, CroIntegrity.Rebuild(earlier));
                var old = new GameMegaAbilitySyncService(f.Session, profile);
                int entries = f.Session.History.Read().Entries.Count;
                var replace = old.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
                check(EditorValues.Flatten(old.Read(1))["Enable"] == 1 && replace.Action == "Actualizar Mega Ability Sync" && replace.Changes.Count == 1, "MegaSyncService_EarlierRoutineIsOfferedForReplacement");
                old.Apply(replace);
                check(File.ReadAllBytes(battle).SequenceEqual(current) && f.Session.History.Read().Entries.Count == entries + 1 &&
                    old.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]).Changes.Count == 0, "MegaSyncService_EarlierRoutineIsReplacedAndRecorded");
                new GameMegaAbilitySyncService(f.Session, profile).Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
                check(File.ReadAllBytes(battle).SequenceEqual(Module()), "MegaSyncService_RemovingAfterTheReplacementRestoresTheOriginalFile");
            }
            var reopened = new GameMegaAbilitySyncService(f.Session, profile);
            reopened.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
            check(EditorValues.Flatten(new GameMegaAbilitySyncService(f.Session, profile).Read(1))["Enable"] == 1, "MegaSyncService_StateSurvivesReopening");
            bool stale; try { service.Apply(service.Preview(new Dictionary<string, int> { ["Enable"] = 0 }, [1])); stale = false; } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { stale = true; }
            check(stale, "MegaSyncService_StaleSessionIsRefused");
        }
        using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
        {
            string battle = Path.Combine(f.Game.RomFS, "Battle.cro");
            byte[] foreign = Module(); Set32(foreign, MegaSite, Bl(MegaSite, 0x8000));
            File.WriteAllBytes(battle, foreign); File.WriteAllBytes(Path.Combine(f.Game.RomFS, "static.crs"), crs); File.WriteAllBytes(Path.Combine(f.Game.ExeFS, "code.bin"), code);
            try { _ = new GameMegaAbilitySyncService(f.Session, profile); check(false, "MegaSyncService_RefusesAConflictWithItsExplanation"); }
            catch (InvalidDataException e) { check(e.Message.Contains("No se modificó ningún archivo") && e.Message.Contains("conflicto"), "MegaSyncService_RefusesAConflictWithItsExplanation"); }
            check(File.ReadAllBytes(battle).SequenceEqual(foreign), "MegaSyncService_RefusalWritesNothing");
            // The real profile on a synthetic module: not the module it was studied on.
            File.WriteAllBytes(battle, Module());
            try { _ = new GameMegaAbilitySyncService(f.Session); check(false, "MegaSyncService_RealProfileRefusesAnUnknownModule"); }
            catch (InvalidDataException e) { check(e.Message.Contains("no acreditada"), "MegaSyncService_RealProfileRefusesAnUnknownModule"); }
        }
        using (var f = new Fixture(pk3DS.Core.GameVersion.SM))
        {
            try { _ = new GameMegaAbilitySyncService(f.Session); check(false, "MegaSyncService_OnlyUltraSunAndUltraMoon"); }
            catch (NotSupportedException) { check(true, "MegaSyncService_OnlyUltraSunAndUltraMoon"); }
        }
        check(ProjectFeatureAvailabilityIsUsumOnly(), "MegaSyncService_SectionOnlyOfferedForUsum");
    }
    private static bool ProjectFeatureAvailabilityIsUsumOnly()
    {
        using var usum = new Fixture(pk3DS.Core.GameVersion.USUM); using var sm = new Fixture(pk3DS.Core.GameVersion.SM);
        return ProjectFeatureAvailability.Supports(usum.Session, "MegaAbilitySync") && !ProjectFeatureAvailability.Supports(sm.Session, "MegaAbilitySync");
    }

    /// <summary>
    /// Real extraction (not part of CI): <paramref name="romfs"/> and <paramref name="exefs"/> of an Ultra Sun or
    /// Ultra Moon project. Everything happens on copies in memory; the files are only read.
    /// </summary>
    internal static void RunReal(string romfs, string exefs, Action<bool, string> check)
    {
        string battlePath = GamePlayerLevelCapService.Find(romfs, "Battle.cro", true), codePath = GamePlayerLevelCapService.Find(exefs, "code.bin", false), crsPath = Path.Combine(romfs, "static.crs");
        byte[] battle = File.ReadAllBytes(battlePath), code = File.ReadAllBytes(codePath), crs = File.ReadAllBytes(crsPath);
        string battleHash = Sha(battle);
        Console.WriteLine($"REAL Battle.cro {battleHash} ({battle.Length} bytes) · code.bin {Sha(code)} · static.crs {Sha(crs)}");
        check(Sync.TryResolveCodeAddresses(crs, code, out var addresses, out string addressError), "MegaSyncReal_AddressesResolved " + addressError);
        Console.WriteLine("REAL addresses " + addresses);
        var capability = GameMegaAbilitySyncService.InspectCapability(battle, crs, code);
        Console.WriteLine("REAL " + GamePlayerLevelCapService.Describe(capability));
        check(capability.CanApply, "MegaSyncReal_Accredited");
        var before = Sync.Inspect(battle, addresses);
        check(before.State is MegaAbilitySyncState.Original or MegaAbilitySyncState.Installed or MegaAbilitySyncState.InstalledEarlierFormat, "MegaSyncReal_KnownState_" + before.State);
        check(Sync.TryInstall(battle, addresses, out byte[] installed, out string error), "MegaSyncReal_Installs " + error);
        var after = Sync.Inspect(installed, addresses);
        check(after.State == MegaAbilitySyncState.Installed && Sync.TryReadImportStubs(installed, out var stubs, out _) &&
            installed.AsSpan((int)after.PayloadOffset!.Value, Sync.PayloadBytes).SequenceEqual(Sync.BuildPayload(after.PayloadOffset.Value, addresses, MegaAbilitySyncProfile.Usum, stubs)), "MegaSyncReal_ReadBack");
        Console.WriteLine($"REAL block at 0x{after.PayloadOffset:X6}; size {battle.Length} -> {installed.Length}; block SHA-256 {Sha(installed.AsSpan((int)after.PayloadOffset!.Value, Sync.PayloadBytes).ToArray())}");
        check(Sync.TryInstall(installed, addresses, out byte[] again, out _) && again.SequenceEqual(installed), "MegaSyncReal_ReinstallChangesNothing");
        check(Sync.TryUninstall(installed, addresses, out byte[] removed, out _) && Sync.Inspect(removed, addresses).State == MegaAbilitySyncState.Original, "MegaSyncReal_Uninstalls");
        if (before.State == MegaAbilitySyncState.Original && installed.Length == battle.Length) check(removed.SequenceEqual(battle), "MegaSyncReal_UninstallIsByteIdentical");
        if (before.State == MegaAbilitySyncState.InstalledEarlierFormat)
            check(installed.Length == battle.Length && after.PayloadOffset == before.PayloadOffset && Sync.TryUninstall(battle, addresses, out byte[] earlierRemoved, out _) && earlierRemoved.SequenceEqual(removed),
                "MegaSyncReal_EarlierRoutineReplacedInPlaceAndRemovable");
        check(CroRelocationMap.TryCreate(installed, out _, out _) && Sha(File.ReadAllBytes(battlePath)) == battleHash, "MegaSyncReal_ModuleStaysValidAndSourceUntouched");
        if (LevelCapPatch.InspectBattle(battle, out var caps) == LevelCapCopyState.Applied)
            check(LevelCapPatch.InspectBattle(installed, out var kept) == LevelCapCopyState.Applied && kept.ToBytes().SequenceEqual(caps.ToBytes()), "MegaSyncReal_PlayerLevelCapsKept");
        try { bool items = GamePersistentItemsService.Patch(battle).Changes == 0; check((GamePersistentItemsService.Patch(installed).Changes == 0) == items, "MegaSyncReal_PersistentItemsStateKept"); }
        catch (InvalidDataException) { Console.WriteLine("REAL Persistent Items: firmas no localizadas en este módulo (no se evalúa)."); }
    }
}
