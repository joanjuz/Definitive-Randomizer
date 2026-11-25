using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Patch = pk3DS.Core.Modding.Research.RareCandyMultiUsePatch;

/// <summary>
/// «Caramelos Raros personalizados» (Rare Candy multi-use, Omega Ruby). Synthetic files carry the hook sites with their
/// original instructions and random bytes elsewhere; a profile is computed from them, so every path of the installer
/// runs without a ROM. The real profile is only exercised by <see cref="RunReal"/> on private copies.
/// </summary>
internal static class RareCandyMultiUseChecks
{
    private const ulong OR = Patch.OmegaRubyTitleId;
    private const int CodeLength = 0x530000, BagLength = 0x8000, BagCode = 0x180, BagCodeSize = 0x7000, BagData = 0x7800, BagDataSize = 0x10;

    private sealed record Files(byte[] ExHeader, byte[] Code, byte[] Bag, byte[] Crr, RareCandyMultiUseProfile Profile)
    {
        public Files Copy() => new((byte[])ExHeader.Clone(), (byte[])Code.Clone(), (byte[])Bag.Clone(), (byte[])Crr.Clone(), Profile);
    }

    private static string Sha(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private static Files Synthetic(int seed = 7)
    {
        var random = new Random(seed);
        byte[] code = new byte[CodeLength]; random.NextBytes(code);
        code.AsSpan(Patch.CaveStart, Patch.CaveEnd - Patch.CaveStart).Clear();
        Patch.DetourOriginalBytes.CopyTo(code.AsSpan(Patch.DetourOffset));
        byte[] bag = new byte[BagLength]; random.NextBytes(bag);
        bag.AsSpan(0, 0x180).Clear();
        "CRO0"u8.CopyTo(bag.AsSpan(0x80));
        Set32(bag, 0xB0, BagCode); Set32(bag, 0xB4, BagCodeSize); Set32(bag, 0xB8, BagData); Set32(bag, 0xBC, BagDataSize);
        foreach (var hook in Patch.Hooks) hook.Original.CopyTo(bag, hook.Offset);
        Rehash(bag);
        byte[] exheader = new byte[0x400];
        Set32(exheader, 0x10, 0x100000); Set32(exheader, 0x14, 0x47A); Set32(exheader, 0x18, 0x479604);
        Set32(exheader, 0x30, 0x5EC000); Set32(exheader, 0x38, 0x439F4); Set32(exheader, 0x3C, 0x7EC2C);
        var hashes = new List<byte[]> { SHA256.HashData(bag.AsSpan(0, 0x80)) };
        for (int i = 0; i < 4; i++) { byte[] h = new byte[32]; random.NextBytes(h); hashes.Add(h); }
        hashes.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
        byte[] crr = new byte[0x360 + 32 * hashes.Count];
        Set32(crr, 0x350, 0x360); Set32(crr, 0x354, hashes.Count);
        for (int i = 0; i < hashes.Count; i++) hashes[i].CopyTo(crr, 0x360 + 32 * i);
        var profile = new RareCandyMultiUseProfile("Perfil sintético", OR, CodeLength, Sha(Patch.MaskedDetourWindow(code)),
            Patch.NativeCalls.Select(c => (c.Address, c.Role, Sha(code.AsSpan((int)(c.Address - 0x100000), 32)))).ToArray(),
            BagLength, Sha(Patch.MaskedBag(bag)), 0x47A, 0x479604, 0x5EC000, 0x439F4, 0x7EC2C);
        return new(exheader, code, bag, crr, profile);
    }

    private static void Set32(byte[] data, int offset, int value) => BitConverter.GetBytes(value).CopyTo(data, offset);

    /// <summary>CRO hash header: 0x80..code, code, code end..data, data.</summary>
    private static void Rehash(byte[] cro)
    {
        int code = BitConverter.ToInt32(cro, 0xB0), codeSize = BitConverter.ToInt32(cro, 0xB4), data = BitConverter.ToInt32(cro, 0xB8), dataSize = BitConverter.ToInt32(cro, 0xBC);
        SHA256.HashData(cro.AsSpan(0x80, code - 0x80)).CopyTo(cro, 0x00);
        SHA256.HashData(cro.AsSpan(code, codeSize)).CopyTo(cro, 0x20);
        SHA256.HashData(cro.AsSpan(code + codeSize, data - code - codeSize)).CopyTo(cro, 0x40);
        SHA256.HashData(cro.AsSpan(data, dataSize)).CopyTo(cro, 0x60);
    }

    private static RareCandyMultiUseInspection Inspect(Files f, ulong? title = OR) => Patch.Inspect(title, f.ExHeader, f.Code, f.Bag, f.Crr, f.Profile);

    private static bool CrrSortedWith(byte[] crr, byte[] bag)
    {
        int start = BitConverter.ToInt32(crr, 0x350), count = BitConverter.ToInt32(crr, 0x354);
        var list = Enumerable.Range(0, count).Select(i => crr.AsSpan(start + 32 * i, 32).ToArray()).ToList();
        bool sorted = list.Zip(list.Skip(1)).All(p => p.First.AsSpan().SequenceCompareTo(p.Second) < 0);
        byte[] header = SHA256.HashData(bag.AsSpan(0, 0x80));
        return sorted && list.Count(h => h.AsSpan().SequenceEqual(header)) == 1;
    }

    private static List<(int Start, int End)> Diff(byte[] a, byte[] b)
    {
        var ranges = new List<(int, int)>();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            int start = i; while (i < a.Length && a[i] != b[i]) i++;
            ranges.Add((start, i));
        }
        return ranges;
    }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        // Generated routine: placement, native calls and branches.
        var blocks = Patch.PayloadBlocks;
        check(blocks.All(b => b.Address - 0x100000 >= Patch.CaveStart && b.Address - 0x100000 + b.Bytes.Length <= Patch.CaveEnd) &&
            blocks.Zip(blocks.Skip(1)).All(p => p.First.Address + p.First.Bytes.Length <= p.Second.Address), "RareCandy_RoutineLivesInTheTextPadding");
        var calls = new HashSet<uint>(); var branches = new HashSet<uint>();
        foreach (var (address, bytes) in blocks)
            for (int i = 0; i + 4 <= bytes.Length; i += 4)
            {
                uint word = BitConverter.ToUInt32(bytes, i);
                if ((word & 0x0E000000) != 0x0A000000 || word >> 28 == 0xF) continue;
                int delta = (int)(word << 8) >> 6;
                uint target = (uint)(address + i + 8 + delta);
                bool inside = blocks.Any(b => target >= b.Address && target < b.Address + b.Bytes.Length);
                if (inside) continue;
                if ((word & 0x01000000) != 0) calls.Add(target); else branches.Add(target);
            }
        check(calls.SetEquals(Patch.NativeCalls.Select(c => c.Address)), "RareCandy_RoutineCallsOnlyFingerprintedNativeFunctions");
        check(branches.SetEquals([0x3B5164u, 0x3B525Cu]) && branches.All(t => t - 0x100000 >= Patch.DetourWindowStart && t - 0x100000 < Patch.DetourWindowEnd),
            "RareCandy_RoutineReturnsOnlyInsideTheFingerprintedMoveLoop");
        uint detourWord = BitConverter.ToUInt32(Patch.DetourInstalled());
        check(detourWord >> 24 == 0xEA && (uint)(Patch.DetourOffset + 0x100000 + 8 + ((int)(detourWord << 8) >> 6)) == blocks.Last().Address + blocks.Last().Bytes.Length - 0x24,
            "RareCandy_DetourBranchesToTheLearnRoutine");
        check(Patch.Hooks.All(h => blocks.Any(b => h.Target >= b.Address && h.Target < b.Address + b.Bytes.Length)) &&
            Patch.Hooks.All(h => Patch.Trampoline(h.Target).AsSpan(0, 8).SequenceEqual(Convert.FromHexString("0FC0A0E104F01FE5"))), "RareCandy_HooksJumpIntoTheRoutine");
        var or = Patch.OmegaRuby;
        uint bssEnd = or.DataAddress + or.DataSize + or.BssSize;
        check(Patch.StateAddress >= bssEnd && Patch.StateAddress + 12 <= ((bssEnd + 0xFFF) & ~0xFFFu), "RareCandy_StateIsInTheUnusedTailOfTheLastBssPage");

        // Original files.
        var original = Synthetic();
        var first = Inspect(original);
        check(first.State == RareCandyMultiUseState.Original && first.Report.Level == PatchCapabilityLevel.Verified && first.Report.CanApply &&
            first.Report.PatchId == Patch.CapabilityId, "RareCandy_OriginalFilesAreVerified");
        check(first.Report.Resources.Count == 8 && first.Report.Changes.Count == 9, "RareCandy_ReportListsEveryResourceAndRange");

        // Identity and evidence: nothing can be written.
        void Refused(Func<Files, RareCandyMultiUseInspection> inspect, string name, string? text = null)
        {
            var copy = original.Copy(); var result = inspect(copy);
            check(result.State == RareCandyMultiUseState.NotAccredited && !result.Report.CanApply && (text is null || result.Report.Summary.Contains(text)), name);
        }
        Refused(f => Inspect(f, null), "RareCandy_MissingNcchIsRefused", "NCCH");
        Refused(f => Inspect(f, Patch.AlphaSapphireTitleId), "RareCandy_AlphaSapphireIsNotInvestigated", "Zafiro Alfa");
        Refused(f => Inspect(f, 0x0004000000055D00), "RareCandy_OtherTitlesAreRefused", "solo se admite Rubí Omega");
        Refused(f => Patch.Inspect(OR, null, f.Code, f.Bag, f.Crr, f.Profile), "RareCandy_MissingExHeaderIsRefused", "ExHeader");
        Refused(f => { Set32(f.ExHeader, 0x3C, 0x7F000); return Inspect(f); }, "RareCandy_LargerBssIsRefused", ".bss");
        Refused(f => Patch.Inspect(OR, f.ExHeader, f.Code[..^0x200], f.Bag, f.Crr, f.Profile), "RareCandy_OtherCodeSizeIsRefused", "descomprimido");
        Refused(f => { f.Code[0x46CC70 - 0x100000 + 4] ^= 1; return Inspect(f); }, "RareCandy_ChangedNativeFunctionIsRefused", "0x46CC70");
        Refused(f => { f.Code[Patch.DetourWindowStart] ^= 1; return Inspect(f); }, "RareCandy_ChangedMoveLoopIsRefused", "aprendizaje");
        Refused(f => { f.Bag[0x5000] ^= 1; Rehash(f.Bag); return Inspect(f); }, "RareCandy_DllBagChangedElsewhereIsRefused", "fuera de los cuatro puntos");
        Refused(f => { f.Bag[0x200] ^= 1; f.Bag[0x200] ^= 1; f.Bag[0x21] ^= 1; return Inspect(f); }, "RareCandy_StaleCroHashIsRefused", "hashes internos");
        Refused(f => { f.Crr[0x360] ^= 0xFF; f.Crr[0x380] ^= 0xFF; f.Crr[0x3A0] ^= 0xFF; f.Crr[0x3C0] ^= 0xFF; f.Crr[0x3E0] ^= 0xFF; return Inspect(f); }, "RareCandy_CrrWithoutDllBagIsRefused", "static.crr");
        Refused(f => Patch.Inspect(OR, f.ExHeader, f.Code, f.Bag, null, f.Profile), "RareCandy_MissingCrrIsRefused", "static.crr");
        Refused(f => Patch.Inspect(OR, f.ExHeader, f.Code, f.Bag, f.Crr, null), "RareCandy_RealProfileRefusesSyntheticFiles");
        reject(() => Patch.Install(Patch.AlphaSapphireTitleId, original.ExHeader, original.Code, original.Bag, original.Crr, original.Profile), "RareCandy_InstallRefusesAlphaSapphire");

        // Install on copies.
        var snapshot = original.Copy();
        var installed = Patch.Install(OR, original.ExHeader, original.Code, original.Bag, original.Crr, original.Profile);
        check(original.Code.SequenceEqual(snapshot.Code) && original.Bag.SequenceEqual(snapshot.Bag) && original.Crr.SequenceEqual(snapshot.Crr), "RareCandy_InstallDoesNotModifyItsInputs");
        var after = original with { Code = installed.Code, Bag = installed.Bag, Crr = installed.Crr };
        check(Inspect(after).State == RareCandyMultiUseState.Installed && Inspect(after).Report.Level == PatchCapabilityLevel.Verified, "RareCandy_InstalledFilesAreRecognised");
        var codeDiff = Diff(original.Code, installed.Code);
        check(codeDiff.All(r => (r.Start >= Patch.CaveStart && r.End <= Patch.CaveEnd) || (r.Start >= Patch.DetourOffset && r.End <= Patch.DetourOffset + 4)) &&
            installed.Code.AsSpan(Patch.DetourOffset, 4).SequenceEqual(Patch.DetourInstalled()), "RareCandy_CodeChangesOnlyTheRoutineAndTheDetour");
        var bagDiff = Diff(original.Bag, installed.Bag);
        check(bagDiff.All(r => r.End <= 0x80 || Patch.Hooks.Any(h => r.Start >= h.Offset && r.End <= h.Offset + 12)) &&
            Patch.Hooks.All(h => installed.Bag.AsSpan(h.Offset, 12).SequenceEqual(Patch.Trampoline(h.Target))), "RareCandy_DllBagChangesOnlyTheHooksAndItsHashes");
        byte[] rehashed = (byte[])installed.Bag.Clone(); Rehash(rehashed);
        check(rehashed.SequenceEqual(installed.Bag) && installed.Bag.AsSpan(0, 0x20).SequenceEqual(original.Bag.AsSpan(0, 0x20)), "RareCandy_DllBagHashesAreRecalculated");
        check(CrrSortedWith(installed.Crr, installed.Bag) && Diff(original.Crr, installed.Crr).All(r => r.Start >= 0x360), "RareCandy_CrrAccreditsTheNewDllBagAndStaysSorted");

        // Double application and partial installations.
        reject(() => Patch.Install(OR, after.ExHeader, after.Code, after.Bag, after.Crr, after.Profile), "RareCandy_InstallingTwiceIsRefused");
        reject(() => Patch.Remove(OR, original.ExHeader, original.Code, original.Bag, original.Crr, original.Profile), "RareCandy_RemovingFromOriginalIsRefused");
        var partial = original with { Code = installed.Code };
        check(Inspect(partial).State == RareCandyMultiUseState.Partial && !Inspect(partial).Report.CanApply, "RareCandy_PartialInstallationIsNotCompleted");
        reject(() => Patch.Install(OR, partial.ExHeader, partial.Code, partial.Bag, partial.Crr, partial.Profile), "RareCandy_PartialIsNotInstalledOver");
        reject(() => Patch.Remove(OR, partial.ExHeader, partial.Code, partial.Bag, partial.Crr, partial.Profile), "RareCandy_PartialIsNotRemovedBlindly");

        // Exact restoration.
        var removed = Patch.Remove(OR, after.ExHeader, after.Code, after.Bag, after.Crr, after.Profile);
        check(removed.Code.SequenceEqual(original.Code) && removed.Bag.SequenceEqual(original.Bag) && removed.Crr.SequenceEqual(original.Crr), "RareCandy_RemovingRestoresTheExactOriginals");

        // Conflicts: another modification occupies a resource.
        var cave = original.Copy(); cave.Code[Patch.CaveEnd - 4] = 0x1E;
        check(Inspect(cave).State == RareCandyMultiUseState.Conflict && Inspect(cave).Report.Level == PatchCapabilityLevel.Conflict, "RareCandy_ForeignCodeInThePaddingIsAConflict");
        var detour = original.Copy(); detour.Code[Patch.DetourOffset + 3] = 0x0A;
        check(Inspect(detour).State == RareCandyMultiUseState.Conflict, "RareCandy_ForeignDetourIsAConflict");
        var hook = original.Copy(); hook.Bag[Patch.Hooks[2].Offset] ^= 0x55; byte[] oldHeader = SHA256.HashData(hook.Bag.AsSpan(0, 0x80)); Rehash(hook.Bag);
        hook = hook with { Crr = Patch.ReplaceCrrEntry(hook.Crr, oldHeader, SHA256.HashData(hook.Bag.AsSpan(0, 0x80))) };
        check(Inspect(hook).State == RareCandyMultiUseState.Conflict, "RareCandy_ForeignHookIsAConflict");
        var reference = after.Copy(); reference.Code[Patch.CaveStart + 0x0C] = 0x58; reference.Code[Patch.CaveStart + 0x0F] = 0xE5;
        var external = Inspect(reference);
        check(external.State == RareCandyMultiUseState.Conflict && external.Report.Summary.Contains("parche de referencia"), "RareCandy_ExternalReferencePatchIsIdentified");
        reject(() => Patch.Install(OR, reference.ExHeader, reference.Code, reference.Bag, reference.Crr, reference.Profile), "RareCandy_ConflictIsNeverOverwritten");
    }

    internal static void RunService(Action<bool, string> check)
    {
        using var state = new TestState();
        var files = Synthetic(11);
        using (var f = new Fixture(pk3DS.Core.GameVersion.ORAS))
        {
            string code = Path.Combine(f.Game.ExeFS, "code.bin"), bag = Path.Combine(f.Game.RomFS, "DllBag.cro"), crr = Path.Combine(f.Game.RomFS, ".crr", "static.crr");
            Directory.CreateDirectory(Path.GetDirectoryName(crr)!);
            File.WriteAllBytes(code, files.Code); File.WriteAllBytes(bag, files.Bag); File.WriteAllBytes(crr, files.Crr);
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "DecryptedExHeader.bin"), files.ExHeader);
            byte[] ncch = new byte[0x200]; "NCCH"u8.CopyTo(ncch.AsSpan(0x100)); BitConverter.GetBytes(OR).CopyTo(ncch, 0x118);
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), ncch);
            check(GameRareCandyMultiUseService.Supports(f.Session) && ProjectFeatureAvailability.Supports(f.Session, "RareCandyMultiUse"), "RareCandyService_OfferedForOmegaRuby");
            var service = new GameRareCandyMultiUseService(f.Session, files.Profile);
            check(service.Entries.Count == 1 && EditorValues.Flatten(service.Read(1))["Enable"] == 0, "RareCandyService_StartsNotInstalled");
            var preview = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
            check(preview.Files.Count == 3 && preview.Changes.Count == 8 && File.ReadAllBytes(code).SequenceEqual(files.Code) && File.ReadAllBytes(bag).SequenceEqual(files.Bag), "RareCandyService_PreviewWritesNothing");
            service.Apply(preview);
            check(EditorValues.Flatten(service.Read(1))["Enable"] == 1 && f.Session.History.Read().Entries.Count == 1 && !File.ReadAllBytes(crr).SequenceEqual(files.Crr), "RareCandyService_ApplyInstallsTheThreeFilesInOneHistoryEntry");
            var again = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
            service.Apply(again);
            check(again.Changes.Count == 0 && f.Session.History.Read().Entries.Count == 1, "RareCandyService_InstallingAgainIsNotAnOperation");
            check(EditorValues.Flatten(new GameRareCandyMultiUseService(f.Session, files.Profile).Read(1))["Enable"] == 1, "RareCandyService_StateSurvivesReopening");
            bool stale; try { service.Apply(preview); stale = false; } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { stale = true; }
            check(stale, "RareCandyService_StalePreviewIsRefused");
            service.Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
            check(File.ReadAllBytes(code).SequenceEqual(files.Code) && File.ReadAllBytes(bag).SequenceEqual(files.Bag) && File.ReadAllBytes(crr).SequenceEqual(files.Crr) &&
                f.Session.History.Read().Entries.Count == 2, "RareCandyService_RemovingRestoresTheExactOriginals");
            // Global Template: the install is captured as a replayable record of this editor.
            service.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
            var doc = GlobalTemplateDocument.Empty("ORAS", 6); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(f.Session.History);
            var records = ManualOperations.Read(doc).Where(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameRareCandyMultiUseService)).ToArray();
            check(records.Length >= 1 && records[^1]["Payload"]!["Values"]!["Enable"]!.GetValue<int>() == 1, "RareCandyService_InstallIsCapturedForGlobalTemplate");
            using (var target = new Fixture(pk3DS.Core.GameVersion.ORAS))
            {
                string targetCode = Path.Combine(target.Game.ExeFS, "code.bin"); string targetCrr = Path.Combine(target.Game.RomFS, ".crr", "static.crr");
                Directory.CreateDirectory(Path.GetDirectoryName(targetCrr)!);
                File.WriteAllBytes(targetCode, files.Code); File.WriteAllBytes(Path.Combine(target.Game.RomFS, "DllBag.cro"), files.Bag); File.WriteAllBytes(targetCrr, files.Crr);
                File.WriteAllBytes(Path.Combine(target.Session.ProjectRoot, "DecryptedExHeader.bin"), files.ExHeader); File.WriteAllBytes(Path.Combine(target.Session.ProjectRoot, "HeaderNCCH0.bin"), ncch);
                bool refused; try { ManualEditTemplate.Apply(target.Session, doc); refused = false; } catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException or InvalidOperationException) { refused = true; }
                check(refused && File.ReadAllBytes(targetCode).SequenceEqual(files.Code), "RareCandyService_ReplayOnUnaccreditedFilesWritesNothing");
            }
            service = new GameRareCandyMultiUseService(f.Session, files.Profile); service.Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), ncch.Select((b, i) => i == 0x118 ? (byte)0x00 : i == 0x119 ? (byte)0xC5 : b).ToArray());
            check(!GameRareCandyMultiUseService.Supports(f.Session) && !ProjectFeatureAvailability.Supports(f.Session, "RareCandyMultiUse"), "RareCandyService_NotOfferedForAlphaSapphire");
            try { _ = new GameRareCandyMultiUseService(f.Session, files.Profile); check(false, "RareCandyService_AlphaSapphireIsRefusedWithItsReason"); }
            catch (InvalidDataException e) { check(e.Message.Contains("Zafiro Alfa") && e.Message.Contains("No se modificó ningún archivo"), "RareCandyService_AlphaSapphireIsRefusedWithItsReason"); }
        }
        foreach (var version in new[] { pk3DS.Core.GameVersion.XY, pk3DS.Core.GameVersion.SM, pk3DS.Core.GameVersion.USUM })
            using (var f = new Fixture(version))
            {
                // XY and Sun/Moon are not supported; a USUM project without NCCH header or Bag.cro is not offered either.
                bool refused; try { _ = new GameRareCandyMultiUseService(f.Session); refused = false; } catch (Exception e) when (e is NotSupportedException or FileNotFoundException or InvalidDataException) { refused = true; }
                check(refused && !ProjectFeatureAvailability.Supports(f.Session, "RareCandyMultiUse"), "RareCandyService_NotOfferedFor" + version);
            }
    }

    /// <summary>
    /// Private project copy, not part of CI: coexistence with the Gen6 editors that write code.bin (their proposals are
    /// read, never applied), install through the editor, undo, Global Template replay, full rebuild and byte-exact
    /// re-extraction. The rebuilt image is the gameplay test ROM; this does not play it.
    /// </summary>
    internal static void RunProject(string project, string image, string reextract, Action<bool, string> check)
    {
        var session = GameProjectSession.Open(project, false);
        var config = session.GameConfiguration;
        string codePath = GamePlayerLevelCapService.Find(config.ExeFS, "code.bin", false);
        var protectedRanges = new List<(int Start, int End, string Name)>
        {
            (Patch.CaveStart, Patch.CaveEnd, "rutina"),
            (Patch.DetourWindowStart, Patch.DetourWindowEnd, "bucle de movimientos"),
        };
        protectedRanges.AddRange(Patch.NativeCalls.Select(c => ((int)(c.Address - 0x100000), (int)(c.Address - 0x100000) + 32, $"funcion 0x{c.Address:X6}")));
        void Coexist(string phase)
        {
            var editors = new List<(string Name, Func<IReadOnlyList<FileChange>> Files)>
            {
                ("Tiendas Gen6", () => Proposal(new GameMart6Service(session))),
                ("MT y MO Gen6", () => Proposal(new GameMachine6Service(session))),
                ("Tutores ORAS", () => Proposal(new GameTutor6Service(session))),
                ("Intercambios Gen6", () => Proposal(new GameTrade6Service(session))),
                ("Encuentros estaticos Gen6", () => Proposal(new GameStaticEncounter6Service(session))),
                ("O-Powers", () => Proposal(new GameOPowerService(session))),
                ("Objetos de campo Gen6", () => Proposal(new GameFieldItems6Service(session))),
                ("Tabla de tipos", () => { var chart = new GameTypeChartService(session); return [chart.Preview(GameTypeChartService.Inverse(chart.Read())).File]; }),
                ("Pickup", () => { var pickup = new GamePickupService(session); return pickup.Preview(pickup.Randomize(pickup.Read(), 5)).Files; }),
            };
            foreach (var (name, files) in editors)
            {
                IReadOnlyList<FileChange> changes;
                try { changes = files(); }
                catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException)
                { Console.WriteLine($"NOTE: {phase} / {name}: sin propuesta ({e.Message})"); continue; }
                var code = changes.Where(c => string.Equals(Path.GetFullPath(c.Path), Path.GetFullPath(codePath), StringComparison.OrdinalIgnoreCase)).ToArray();
                var touched = code.SelectMany(c => Diff(c.Original(), c.Proposed())).ToArray();
                var hit = touched.SelectMany(t => protectedRanges.Where(r => t.Start < r.End && r.Start < t.End).Select(r => r.Name)).Distinct().ToArray();
                check(hit.Length == 0, $"RareCandyProject_{phase}_{name.Replace(' ', '_')}_DoesNotTouchTheRoutine{(code.Length == 0 ? "_NoCodeBin" : "")}");
            }
        }
        static IReadOnlyList<FileChange> Proposal(IProjectEditor editor) =>
            editor.Preview(EditorValues.Flatten(editor.RandomOptions), editor.Entries.Select(e => e.Id).ToArray()).Files;

        Dictionary<string, string> Snapshot() => new[] { codePath, GamePlayerLevelCapService.Find(config.RomFS, "DllBag.cro", true), Path.Combine(config.RomFS, ".crr", "static.crr") }
            .ToDictionary(p => p, p => Sha(File.ReadAllBytes(p)));
        var originalFiles = Snapshot();
        check(GameRareCandyMultiUseService.InspectProject(session).State == GameRareCandyMultiUseService.CardState.NotInstalled, "RareCandyProject_StartsNotInstalled");
        Coexist("Original");
        var service = new GameRareCandyMultiUseService(session);
        service.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
        var installedFiles = Snapshot();
        check(GameRareCandyMultiUseService.InspectProject(session).State == GameRareCandyMultiUseService.CardState.Installed, "RareCandyProject_InstalledThroughTheEditor");
        Coexist("Instalado");
        // Global Template capture, then undo through the history.
        var doc = GlobalTemplateDocument.Empty("ORAS", 6); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(session.History);
        check(ManualOperations.Read(doc).Any(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameRareCandyMultiUseService)), "RareCandyProject_CapturedForGlobalTemplate");
        var entry = session.History.Read().Entries.Last();
        RestorePoints.Undo(session.History, session.ProjectRoot, entry.Id);
        check(Snapshot().SequenceEqual(originalFiles), "RareCandyProject_UndoRestoresTheExactOriginals");
        // Global Template replay reinstalls the same bytes.
        ManualEditTemplate.Apply(session, doc);
        check(Snapshot().SequenceEqual(installedFiles), "RareCandyProject_GlobalTemplateReplayReinstallsTheSameBytes");
        // Full rebuild and byte-exact re-extraction.
        var built = new GameRomRebuildService(session).BuildAsync(RomRebuildKind.Full3DS, image).GetAwaiter().GetResult();
        Console.WriteLine($"Rare Candy full rebuild: {built.File}, {built.Bytes} bytes, SHA256 {built.Hash}");
        RealRebuildChecks.VerifyExisting(project, image, reextract);
        var reopened = GameProjectSession.Open(reextract, false);
        check(GameRareCandyMultiUseService.InspectProject(reopened).State == GameRareCandyMultiUseService.CardState.Installed, "RareCandyProject_InstalledAfterRebuildAndReextraction");
    }

    /// <summary>
    /// Private copies, not part of CI. <paramref name="project"/> is an Omega Ruby extraction (HeaderNCCH0.bin,
    /// DecryptedExHeader.bin, ExtractedExeFS, ExtractedRomFS). Files are only read; results go to a new folder.
    /// <paramref name="reference"/> optionally points to the unzipped reference patch to compare bytes.
    /// </summary>
    internal static void RunReal(string project, string output, string? reference, Action<bool, string> check)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("La carpeta de salida debe ser nueva o estar vacía.");
        Directory.CreateDirectory(output);
        byte[] Read(params string[] parts) => File.ReadAllBytes(Path.Combine([project, .. parts]));
        ulong? title = Patch.ReadTitleId(Read("HeaderNCCH0.bin"));
        byte[] exheader = Read("DecryptedExHeader.bin"), code = Read("ExtractedExeFS", "code.bin"), bag = Read("ExtractedRomFS", "DllBag.cro"), crr = Read("ExtractedRomFS", ".crr", "static.crr");
        var first = Patch.Inspect(title, exheader, code, bag, crr);
        check(first.State == RareCandyMultiUseState.Original && first.Report.Level == PatchCapabilityLevel.Verified, "RareCandyReal_OriginalOmegaRubyIsVerified");
        var shinyBefore = GameShinyRateService.InspectCapability(code);
        var installed = Patch.Install(title, exheader, code, bag, crr);
        check(Patch.Inspect(title, exheader, installed.Code, installed.Bag, installed.Crr).State == RareCandyMultiUseState.Installed, "RareCandyReal_InstalledIsRecognised");
        var shinyAfter = GameShinyRateService.InspectCapability(installed.Code);
        check(shinyBefore.Level == shinyAfter.Level && shinyBefore.CanApply == shinyAfter.CanApply &&
            shinyBefore.Resources.Select(r => (r.State, r.Offset)).SequenceEqual(shinyAfter.Resources.Select(r => (r.State, r.Offset))), "RareCandyReal_ShinyRateReadsTheSameAfterInstalling");
        int hidden = code.AsSpan().IndexOf(GameFieldItems6Service.HiddenSignature);
        int hiddenEnd = hidden + GameFieldItems6Service.HiddenSignature.Length + 170 * 14;
        static bool Overlaps(int start, int end, int otherStart, int otherEnd) => start < otherEnd && otherStart < end;
        check(hidden > 0 && !Overlaps(hidden, hiddenEnd, Patch.CaveStart, Patch.CaveEnd) && !Overlaps(hidden, hiddenEnd, Patch.DetourOffset, Patch.DetourOffset + 4),
            "RareCandyReal_HiddenItemTableDoesNotOverlap");
        var removed = Patch.Remove(title, exheader, installed.Code, installed.Bag, installed.Crr);
        check(removed.Code.SequenceEqual(code) && removed.Bag.SequenceEqual(bag) && removed.Crr.SequenceEqual(crr), "RareCandyReal_RemovingRestoresTheExactOriginals");
        if (reference is not null)
        {
            string root = Path.Combine(reference, "000400000011C400");
            byte[] refBag = File.ReadAllBytes(Path.Combine(root, "romfs", "DllBag.cro")), refCrr = File.ReadAllBytes(Path.Combine(root, "romfs", ".crr", "static.crr"));
            check(installed.Bag.SequenceEqual(refBag), "RareCandyReal_DllBagEqualsTheReferencePatch");
            check(installed.Crr.SequenceEqual(refCrr), "RareCandyReal_StaticCrrEqualsTheReferencePatch");
            var referenceCode = (byte[])code.Clone();
            byte[] ips = File.ReadAllBytes(Path.Combine(root, "exefs", "code.ips"));
            for (int at = 5; !ips.AsSpan(at, 3).SequenceEqual("EOF"u8);)
            {
                int offset = ips[at] << 16 | ips[at + 1] << 8 | ips[at + 2], size = ips[at + 3] << 8 | ips[at + 4]; at += 5;
                ips.AsSpan(at, size).CopyTo(referenceCode.AsSpan(offset)); at += size;
            }
            var differences = Diff(installed.Code, referenceCode);
            check(differences.All(r => r.Start >= Patch.CaveStart && r.End <= Patch.CaveEnd), "RareCandyReal_CodeDiffersFromTheReferenceOnlyInUnreachableLeftovers");
            check(Patch.Inspect(title, exheader, referenceCode, refBag, refCrr).Report.Summary.Contains("parche de referencia"), "RareCandyReal_ExternalReferenceIsIdentified");
        }
        File.WriteAllText(Path.Combine(output, "resultado.txt"), $"Rubí Omega: instalación y retirada verificadas en memoria; SHA-256 code.bin instalado {Sha(installed.Code)}, DllBag {Sha(installed.Bag)}, static.crr {Sha(installed.Crr)}. Sin prueba jugable.\n");
    }
}
