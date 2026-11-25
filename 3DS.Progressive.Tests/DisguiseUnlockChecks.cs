using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Dg = pk3DS.Core.Modding.Research.DisguiseUnlockPatch;

/// <summary>«Habilidades especiales · Disfraz» (USUM). Anchors are fingerprints of the real Battle.cro, so installs run in RunReal.</summary>
internal static class DisguiseUnlockChecks
{
    private const ulong US = Dg.UltraSunTitleId, UM = Dg.UltraMoonTitleId;
    private static readonly uint[] Targets = [0x7CCF4, 0x7F7A8, 0x876F8, 0x89C6C, 0x89E28, 0xD8DB4, 0xD8E84, 0xD8E98, 0xDE084, 0xDE098, 0xDE0D0, 0xDE124, 0xDE138, 0xDE164, 0xDE53C, 0xDE550, 0xDE588];
    private static byte[] Ncch(ulong title) { byte[] n = new byte[0x200]; "NCCH"u8.CopyTo(n.AsSpan(0x100)); BitConverter.GetBytes(title).CopyTo(n, 0x118); return n; }

    private static HashSet<uint> External(byte[] routine, int block)
    {
        var set = new HashSet<uint>();
        for (int o = 0; o < routine.Length; o += 4)
        {
            uint w = BitConverter.ToUInt32(routine, o);
            if ((w & 0x0E000000) != 0x0A000000 || w >> 28 == 0xF || w == 0) continue;
            int t = block + o + 8 + ((int)(w << 8) >> 6);
            if (t < block || t >= block + routine.Length) set.Add((uint)t);
        }
        return set;
    }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        foreach (int block in new[] { 0xFC978, 0x100000, 0x13C000 })
            check(External(Dg.RoutineAt(block), block).SetEquals(Targets), "Disguise_RoutineKeepsItsTargetsAt_" + block.ToString("X"));
        check(Dg.Hooks.All(h => h.Offset is not (>= 0x15AD4 and < 0x15AE0)), "Disguise_HooksAwayFromLevelCapsHook");
        byte[] cro = new byte[0x2000]; "CRO0"u8.CopyTo(cro.AsSpan(0x80));
        check(Dg.Inspect(null, cro, cro).State == MultitypeUnlockState.NotAccredited && Dg.Inspect(0x0004000000175E00, cro, cro).Report.Summary.Contains("solo Ultra Sol")
            && Dg.Inspect(US, null, cro).Report.Summary.Contains("falta Battle.cro") && !Dg.Inspect(UM, cro, cro).Report.CanApply, "Disguise_RefusesUnknownTitlesAndFiles");
        reject(() => Dg.Install(US, cro, cro), "Disguise_InstallRefusesUnknownBattle");
        using var state = new TestState();
        using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
        {
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(UM));
            string battle = Path.Combine(f.Game.RomFS, "Battle.cro"), crr = Path.Combine(f.Game.RomFS, ".crr", "static.crr");
            Directory.CreateDirectory(Path.GetDirectoryName(crr)!); File.WriteAllBytes(battle, cro); File.WriteAllBytes(crr, new byte[0x400]);
            check(ProjectFeatureAvailability.Supports(f.Session, "DisguiseUnlock"), "DisguiseService_OfferedForUsum");
            try { _ = new GameDisguiseUnlockService(f.Session); check(false, "DisguiseService_UnknownBattleRefused"); }
            catch (InvalidDataException e) { check(e.Message.Contains("No se modificó ningún archivo") && File.ReadAllBytes(battle).SequenceEqual(cro), "DisguiseService_UnknownBattleRefused"); }
        }
        using (var f = new Fixture(pk3DS.Core.GameVersion.SM))
        {
            bool refused; try { _ = new GameDisguiseUnlockService(f.Session); refused = false; } catch (NotSupportedException) { refused = true; }
            check(refused && !ProjectFeatureAvailability.Supports(f.Session, "DisguiseUnlock"), "DisguiseService_SunMoonNotOffered");
        }
    }

    private static byte[] Crr(byte[] crr, byte[] oldCro, byte[] newCro) =>
        RareCandyMultiUsePatch.ReplaceCrrEntry(crr, SHA256.HashData(oldCro.AsSpan(0, 0x80)), SHA256.HashData(newCro.AsSpan(0, 0x80)));

    /// <summary>Original RomFS of US and UM, ExeFS of UM (for Level Caps and Mega Ability Sync). Read only; in memory and temporary fixtures.</summary>
    internal static void RunReal(string usRomfs, string umRomfs, string umExefs, Action<bool, string> check)
    {
        foreach (var (romfs, title, g) in new[] { (usRomfs, US, "US"), (umRomfs, UM, "UM") })
        {
            byte[] battle = File.ReadAllBytes(Path.Combine(romfs, "Battle.cro")), crr = File.ReadAllBytes(Path.Combine(romfs, ".crr", "static.crr"));
            var first = Dg.Inspect(title, battle, crr);
            check(first.State == MultitypeUnlockState.Original && first.Report.CanApply, "DisguiseReal_OriginalInstallable_" + g);
            var on = Dg.Install(title, battle, crr);
            int block = on.Battle.AsSpan().IndexOf("DGDISG01"u8) - 0xF0;
            check(Dg.Inspect(title, on.Battle, on.Crr).State == MultitypeUnlockState.Installed && on.Battle.AsSpan(block, 0xF0).SequenceEqual(Dg.RoutineAt(block)), "DisguiseReal_InstalledRecognised_" + g);
            try { Dg.Install(title, on.Battle, on.Crr); check(false, "DisguiseReal_DoubleInstallRefused_" + g); } catch (InvalidDataException) { check(true, "DisguiseReal_DoubleInstallRefused_" + g); }
            var off = Dg.Remove(title, on.Battle, on.Crr);
            check(Dg.Inspect(title, off.Battle, off.Crr).State == MultitypeUnlockState.Original && Dg.Hooks.All(h => BitConverter.ToUInt32(off.Battle, h.Offset) == 0xE2401C03), "DisguiseReal_RemoveRestoresInstructions_" + g);
            var again = Dg.Install(title, off.Battle, off.Crr);
            check(again.Battle.SequenceEqual(on.Battle) && again.Crr.SequenceEqual(on.Crr), "DisguiseReal_ReinstallReusesBlock_" + g);
            byte[] foreign = (byte[])battle.Clone(); BitConverter.GetBytes(0xE320F000u).CopyTo(foreign, 0xDE078);
            var tmp = Dg.Inspect(title, foreign, crr);
            check(!tmp.Report.CanApply, "DisguiseReal_ForeignSiteRefused_" + g);
        }
        // Coexistence with the other Battle.cro patches (UM files).
        byte[] b0 = File.ReadAllBytes(Path.Combine(umRomfs, "Battle.cro")), c0 = File.ReadAllBytes(Path.Combine(umRomfs, ".crr", "static.crr"));
        byte[] code = File.ReadAllBytes(Path.Combine(umExefs, "code.bin")), crs = File.ReadAllBytes(Path.Combine(umRomfs, "static.crs"));
        var dg = Dg.Install(UM, b0, c0);
        var capsAfter = LevelCapPatch.InstallManaged(dg.Battle, code, LevelCapTable.Default());
        check(capsAfter.Success && Dg.Inspect(UM, capsAfter.BattleCro, Crr(dg.Crr, dg.Battle, capsAfter.BattleCro)).State == MultitypeUnlockState.Installed, "DisguiseReal_LevelCapsAfterDisguise");
        var capsFirst = LevelCapPatch.InstallManaged(b0, code, LevelCapTable.Default());
        var dgAfterCaps = Dg.Install(UM, capsFirst.BattleCro, Crr(c0, b0, capsFirst.BattleCro));
        check(LevelCapPatch.InspectCapability(dgAfterCaps.Battle, capsFirst.CodeBin).Level is PatchCapabilityLevel.Verified or PatchCapabilityLevel.CompatibleByAdaptation, "DisguiseReal_DisguiseAfterLevelCaps");
        check(MegaAbilitySyncPatch.TryResolveCodeAddresses(crs, code, out var addresses, out _), "DisguiseReal_MegaAddresses");
        check(MegaAbilitySyncPatch.TryInstall(dg.Battle, addresses, MegaAbilitySyncProfile.Usum, out var withMega, out _) &&
            Dg.Inspect(UM, withMega, Crr(dg.Crr, dg.Battle, withMega)).State == MultitypeUnlockState.Installed, "DisguiseReal_MegaSyncAfterDisguise");
        bool megaOk = MegaAbilitySyncPatch.TryInstall(b0, addresses, MegaAbilitySyncProfile.Usum, out var megaFirst, out _);
        var dgAfterMega = Dg.Install(UM, megaFirst, Crr(c0, b0, megaFirst));
        check(megaOk && Dg.Inspect(UM, dgAfterMega.Battle, dgAfterMega.Crr).State == MultitypeUnlockState.Installed &&
            MegaAbilitySyncPatch.Inspect(dgAfterMega.Battle, addresses, MegaAbilitySyncProfile.Usum).State == MegaAbilitySyncState.Installed, "DisguiseReal_DisguiseAfterMegaSync");
        check(GamePersistentItemsService.InspectCapability(dg.Battle).CanApply, "DisguiseReal_PersistentItemsStillInstallable");
        // Editor with history, Global Template and undo on a temporary project.
        using var state = new TestState();
        using var f = new Fixture(pk3DS.Core.GameVersion.USUM);
        string battlePath = Path.Combine(f.Game.RomFS, "Battle.cro"), crrPath = Path.Combine(f.Game.RomFS, ".crr", "static.crr");
        Directory.CreateDirectory(Path.GetDirectoryName(crrPath)!); File.WriteAllBytes(battlePath, b0); File.WriteAllBytes(crrPath, c0);
        File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(UM));
        var service = new GameDisguiseUnlockService(f.Session);
        check(EditorValues.Flatten(service.Read(1))["Enable"] == 0, "DisguiseService_OffByDefault");
        service.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
        byte[] installed = File.ReadAllBytes(battlePath);
        var doc = GlobalTemplateDocument.Empty("USUM", 7); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(f.Session.History);
        check(ManualOperations.Read(doc).Any(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameDisguiseUnlockService)), "DisguiseService_CapturedForGlobalTemplate");
        RestorePoints.Undo(f.Session.History, f.Session.ProjectRoot, f.Session.History.Read().Entries.Last().Id);
        check(File.ReadAllBytes(battlePath).SequenceEqual(b0) && File.ReadAllBytes(crrPath).SequenceEqual(c0), "DisguiseService_UndoRestoresExactFiles");
        ManualEditTemplate.Apply(f.Session, doc);
        check(File.ReadAllBytes(battlePath).SequenceEqual(installed), "DisguiseService_GlobalTemplateReplay");
        new GameDisguiseUnlockService(f.Session).Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
        check(EditorValues.Flatten(new GameDisguiseUnlockService(f.Session).Read(1))["Enable"] == 0, "DisguiseService_DisableThroughEditor");
    }
}
