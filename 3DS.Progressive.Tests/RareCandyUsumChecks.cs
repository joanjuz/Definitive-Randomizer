using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Usum = pk3DS.Core.Modding.Research.RareCandyUsumPatch;

/// <summary>
/// «Caramelos Raros personalizados» in Ultra Sun / Ultra Moon. The anchors are fingerprints of the real Bag.cro and
/// FieldBag.cro, so installation paths run in <see cref="RunReal"/> (private files, not CI); <see cref="Run"/> covers
/// the generated routine, refusals and the editor without game files.
/// </summary>
internal static class RareCandyUsumChecks
{
    private static readonly uint[] ExpectedTargets =
        [0x240, 0x350, 0x3A8, 0x4C0, 0x4D60, 0x6148, 0x66B4, 0x6D8, 0x968, 0x9E8, 0x9F0, 0x9F8, 0x9FA8, 0xA00, 0xA08, 0xA18, 0xA5E4, 0xA860, 0xA92C, 0xA944, 0xC2F0, 0xD70, 0xE9A4];

    private static byte[] Ncch(ulong title)
    {
        byte[] ncch = new byte[0x200]; "NCCH"u8.CopyTo(ncch.AsSpan(0x100)); BitConverter.GetBytes(title).CopyTo(ncch, 0x118); return ncch;
    }

    private static HashSet<uint> ExternalTargets(byte[] routine, int block)
    {
        var targets = new HashSet<uint>();
        for (int o = 0; o < routine.Length; o += 4)
        {
            uint w = BitConverter.ToUInt32(routine, o);
            if ((w & 0x0E000000) != 0x0A000000 || w >> 28 == 0xF) continue;
            int t = block + o + 8 + ((int)(w << 8) >> 6);
            if (t < block || t >= block + routine.Length) targets.Add((uint)t);
        }
        return targets;
    }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        foreach (int block in new[] { 0x15E00, 0x16DFC, 0x20000 })
            check(ExternalTargets(Usum.RoutineAt(block), block).SetEquals(ExpectedTargets), "RareCandyUsum_RoutineKeepsItsBagTargetsAt_" + block.ToString("X"));
        check(Usum.Hooks.Select(h => h.Offset).All(o => !UsumBagAnchors.MintRanges.Any(r => o >= r.Start && o < r.End)) &&
            UsumBagAnchors.MintSites.All(s => !Usum.Ranges.Any(r => s >= r.Start && s < r.End)), "RareCandyUsum_NeverWritesWhatTheMintsNeedAndViceVersa");
        byte[] bag = new byte[0x2000]; "CRO0"u8.CopyTo(bag.AsSpan(0x80));
        check(Usum.Inspect(null, bag, bag, bag).State == RareCandyMultiUseState.NotAccredited, "RareCandyUsum_MissingNcchRefused");
        check(Usum.Inspect(0x0004000000175E00, bag, bag, bag).Report.Summary.Contains("sin perfil USUM"), "RareCandyUsum_SunMoonRefused");
        check(Usum.Inspect(Usum.UltraSunTitleId, null, bag, bag).Report.Summary.Contains("falta Bag.cro"), "RareCandyUsum_MissingBagRefused");
        check(!Usum.Inspect(Usum.UltraMoonTitleId, bag, bag, bag).Report.CanApply, "RareCandyUsum_UnknownBagRefused");
        reject(() => Usum.Install(Usum.UltraSunTitleId, bag, bag, bag), "RareCandyUsum_InstallRefusesUnknownBag");

        using var state = new TestState();
        foreach (var (title, name) in new[] { (Usum.UltraSunTitleId, "US"), (Usum.UltraMoonTitleId, "UM") })
            using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
            {
                File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(title));
                check(GameRareCandyMultiUseService.Supports(f.Session) && ProjectFeatureAvailability.Supports(f.Session, "RareCandyMultiUse"), "RareCandyUsumService_Offered_" + name);
                string bagPath = Path.Combine(f.Game.RomFS, "Bag.cro"), crr = Path.Combine(f.Game.RomFS, ".crr", "static.crr");
                Directory.CreateDirectory(Path.GetDirectoryName(crr)!);
                File.WriteAllBytes(bagPath, bag); File.WriteAllBytes(Path.Combine(f.Game.RomFS, "FieldBag.cro"), bag); File.WriteAllBytes(crr, new byte[0x400]);
                try { _ = new GameRareCandyMultiUseService(f.Session); check(false, "RareCandyUsumService_UnknownBagRefusedWithItsReason_" + name); }
                catch (InvalidDataException e) { check(e.Message.Contains("No se modificó ningún archivo"), "RareCandyUsumService_UnknownBagRefusedWithItsReason_" + name); }
                check(File.ReadAllBytes(bagPath).SequenceEqual(bag) && GameRareCandyMultiUseService.InspectProject(f.Session).State == GameRareCandyMultiUseService.CardState.Unavailable,
                    "RareCandyUsumService_RefusalWritesNothing_" + name);
            }
    }

    private static byte[] Rehash(byte[] c)
    {
        int U(int o) => BitConverter.ToInt32(c, o);
        int code = U(0xB0), codeSize = U(0xB4), data = U(0xB8), dataSize = U(0xBC);
        SHA256.HashData(c.AsSpan(0x80, code - 0x80)).CopyTo(c, 0x00);
        SHA256.HashData(c.AsSpan(code, codeSize)).CopyTo(c, 0x20);
        SHA256.HashData(c.AsSpan(code + codeSize, data - code - codeSize)).CopyTo(c, 0x40);
        SHA256.HashData(c.AsSpan(data, dataSize)).CopyTo(c, 0x60);
        return c;
    }

    private static byte[] WithCrrEntry(byte[] crr, byte[] oldBag, byte[] newBag) =>
        RareCandyMultiUsePatch.ReplaceCrrEntry(crr, SHA256.HashData(oldBag.AsSpan(0, 0x80)), SHA256.HashData(newBag.AsSpan(0, 0x80)));

    /// <summary>
    /// Private files, not CI: the RomFS of an original Ultra Sun and an original Ultra Moon (read only), optionally the
    /// Bag.cro of the prototype validated in game. Everything happens in memory or in temporary fixtures.
    /// </summary>
    internal static void RunReal(string sunRomfs, string moonRomfs, string? prototypeBag, Action<bool, string> check)
    {
        foreach (var (romfs, title, name) in new[] { (sunRomfs, Usum.UltraSunTitleId, "US"), (moonRomfs, Usum.UltraMoonTitleId, "UM") })
        {
            byte[] bag = File.ReadAllBytes(Path.Combine(romfs, "Bag.cro")), fieldBag = File.ReadAllBytes(Path.Combine(romfs, "FieldBag.cro")),
                crr = File.ReadAllBytes(Path.Combine(romfs, ".crr", "static.crr"));
            var first = Usum.Inspect(title, bag, fieldBag, crr);
            check(first.State == RareCandyMultiUseState.Original && first.Report.CanApply && first.Report.Resized.Contains("Bag.cro"), "RareCandyUsumReal_OriginalCanBeInstalled_" + name);
            var installed = Usum.Install(title, bag, fieldBag, crr);
            check(Usum.Inspect(title, installed.Bag, fieldBag, installed.Crr).State == RareCandyMultiUseState.Installed, "RareCandyUsumReal_InstalledIsRecognised_" + name);
            if (prototypeBag is not null)
            {
                // The prototype validated in game had the same routine without the trailing marker.
                byte[] prototype = File.ReadAllBytes(prototypeBag), normalized = (byte[])installed.Bag.Clone();
                int marker = normalized.AsSpan().IndexOf("DGCANDY1"u8);
                normalized.AsSpan(marker, 16).Clear(); normalized.AsSpan(0, 0x80).Clear();
                byte[] proto = (byte[])prototype.Clone(); proto.AsSpan(0, 0x80).Clear();
                check(marker > 0 && normalized.SequenceEqual(proto), "RareCandyUsumReal_SameBytesAsThePrototypeValidatedInGame_" + name);
            }
            try { _ = Usum.Install(title, installed.Bag, fieldBag, installed.Crr); check(false, "RareCandyUsumReal_InstallingTwiceRefused_" + name); }
            catch (InvalidDataException) { check(true, "RareCandyUsumReal_InstallingTwiceRefused_" + name); }
            var removed = Usum.Remove(title, installed.Bag, fieldBag, installed.Crr);
            check(Usum.Inspect(title, removed.Bag, fieldBag, removed.Crr).State == RareCandyMultiUseState.Original &&
                Usum.Hooks.All(h => BitConverter.ToUInt32(removed.Bag, h.Offset) == h.Original), "RareCandyUsumReal_RemovingRestoresTheOriginalInstructions_" + name);
            var again = Usum.Install(title, removed.Bag, fieldBag, removed.Crr);
            check(again.Bag.Length == installed.Bag.Length && again.Bag.SequenceEqual(installed.Bag) && again.Crr.SequenceEqual(installed.Crr), "RareCandyUsumReal_ReinstallReusesTheBlock_" + name);
            byte[] partial = Rehash((byte[])installed.Bag.Clone()); BitConverter.GetBytes(Usum.Hooks[1].Original).CopyTo(partial, Usum.Hooks[1].Offset); Rehash(partial);
            check(Usum.Inspect(title, partial, fieldBag, WithCrrEntry(installed.Crr, installed.Bag, partial)).State == RareCandyMultiUseState.Partial, "RareCandyUsumReal_PartialDetected_" + name);
            byte[] foreign = (byte[])bag.Clone(); BitConverter.GetBytes(0xE320F000u).CopyTo(foreign, Usum.Hooks[2].Offset); Rehash(foreign);
            check(Usum.Inspect(title, foreign, fieldBag, WithCrrEntry(crr, bag, foreign)).State == RareCandyMultiUseState.Conflict, "RareCandyUsumReal_ForeignHookIsAConflict_" + name);
            byte[] otherField = (byte[])fieldBag.Clone(); otherField[0x1F74] ^= 1;
            check(!Usum.Inspect(title, bag, otherField, crr).Report.CanApply, "RareCandyUsumReal_ChangedQuantityWindowRefused_" + name);
            byte[] noEntry = (byte[])crr.Clone(), header = SHA256.HashData(bag.AsSpan(0, 0x80));
            int crrStart = BitConverter.ToInt32(crr, 0x350), crrCount = BitConverter.ToInt32(crr, 0x354);
            for (int i = 0; i < crrCount; i++) if (noEntry.AsSpan(crrStart + 32 * i, 32).SequenceEqual(header)) noEntry[crrStart + 32 * i] ^= 0xFF;
            check(!Usum.Inspect(title, bag, fieldBag, noEntry).Report.CanApply, "RareCandyUsumReal_CrrWithoutBagRefused_" + name);
            // Nature mints, in both orders.
            var profile = UsumNatureMintCatalog.Profiles.Single(p => p.Moon == (title == Usum.UltraMoonTitleId));
            byte[] candyThenMints = UsumNatureMintPatch.Prepare(installed.Bag, profile);
            check(UsumNatureMintPatch.Inspect(candyThenMints, profile).State == UsumMintState.Installed &&
                Usum.Inspect(title, candyThenMints, fieldBag, WithCrrEntry(installed.Crr, installed.Bag, candyThenMints)).State == RareCandyMultiUseState.Installed, "RareCandyUsumReal_MintsAfterCandy_" + name);
            byte[] mints = UsumNatureMintPatch.Prepare(bag, profile); byte[] mintsCrr = WithCrrEntry(crr, bag, mints);
            var mintsThenCandy = Usum.Install(title, mints, fieldBag, mintsCrr);
            check(UsumNatureMintPatch.Inspect(mintsThenCandy.Bag, profile).State == UsumMintState.Installed &&
                Usum.Inspect(title, mintsThenCandy.Bag, fieldBag, mintsThenCandy.Crr).State == RareCandyMultiUseState.Installed, "RareCandyUsumReal_CandyAfterMints_" + name);
            var mintsRemoved = Usum.Remove(title, mintsThenCandy.Bag, fieldBag, mintsThenCandy.Crr);
            check(UsumNatureMintPatch.Inspect(mintsRemoved.Bag, profile).State == UsumMintState.Installed, "RareCandyUsumReal_RemovingCandyKeepsMints_" + name);
            RunService(romfs, title, name, bag, check);
        }
    }

    private static void RunService(string romfs, ulong title, string name, byte[] bag, Action<bool, string> check)
    {
        using var state = new TestState();
        using var f = new Fixture(pk3DS.Core.GameVersion.USUM);
        string bagPath = Path.Combine(f.Game.RomFS, "Bag.cro"), crrPath = Path.Combine(f.Game.RomFS, ".crr", "static.crr");
        Directory.CreateDirectory(Path.GetDirectoryName(crrPath)!);
        File.Copy(Path.Combine(romfs, "Bag.cro"), bagPath); File.Copy(Path.Combine(romfs, "FieldBag.cro"), Path.Combine(f.Game.RomFS, "FieldBag.cro"));
        File.Copy(Path.Combine(romfs, ".crr", "static.crr"), crrPath);
        File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(title));
        string Sha(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
        string bagBefore = Sha(bagPath), crrBefore = Sha(crrPath);
        var service = new GameRareCandyMultiUseService(f.Session);
        check(service.Title.Contains("Ultra Sol") && EditorValues.Flatten(service.Read(1))["Enable"] == 0 &&
            GameRareCandyMultiUseService.InspectProject(f.Session).State == GameRareCandyMultiUseService.CardState.NotInstalled, "RareCandyUsumService_StartsNotInstalled_" + name);
        var preview = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
        check(preview.Files.Count == 2 && Sha(bagPath) == bagBefore, "RareCandyUsumService_PreviewWritesNothing_" + name);
        service.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
        string bagInstalled = Sha(bagPath);
        check(GameRareCandyMultiUseService.InspectProject(f.Session).State == GameRareCandyMultiUseService.CardState.Installed && f.Session.History.Read().Entries.Count == 1,
            "RareCandyUsumService_InstallRecorded_" + name);
        var doc = GlobalTemplateDocument.Empty("USUM", 7); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(f.Session.History);
        check(ManualOperations.Read(doc).Any(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameRareCandyMultiUseService)), "RareCandyUsumService_CapturedForGlobalTemplate_" + name);
        RestorePoints.Undo(f.Session.History, f.Session.ProjectRoot, f.Session.History.Read().Entries.Last().Id);
        check(Sha(bagPath) == bagBefore && Sha(crrPath) == crrBefore, "RareCandyUsumService_UndoRestoresTheExactOriginals_" + name);
        ManualEditTemplate.Apply(f.Session, doc);
        check(Sha(bagPath) == bagInstalled, "RareCandyUsumService_GlobalTemplateReplayReinstallsTheSameBytes_" + name);
        new GameRareCandyMultiUseService(f.Session).Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
        check(GameRareCandyMultiUseService.InspectProject(f.Session).State == GameRareCandyMultiUseService.CardState.NotInstalled, "RareCandyUsumService_RemoveThroughTheEditor_" + name);
    }
}
