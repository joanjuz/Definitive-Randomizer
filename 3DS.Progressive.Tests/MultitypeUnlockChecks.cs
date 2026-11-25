using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Mt = pk3DS.Core.Modding.Research.MultitypeUnlockPatch;

/// <summary>«Habilidades especiales · Multitipo» (USUM). Synthetic code.bin with its own profile for CI; real files in RunReal.</summary>
internal static class MultitypeUnlockChecks
{
    private const ulong US = 0x00040000001B5000, UM = 0x00040000001B5100;

    /// <summary>Synthetic code.bin with the Multitype and RKS System sites of both games in their original state.</summary>
    internal static (byte[] Code, MultitypeUnlockProfile[] Profiles) Synthetic()
    {
        byte[] code = new byte[0x5A4000]; new Random(5).NextBytes(code);
        foreach (var real in Mt.Profiles)
            foreach (int site in new[] { real.Type1Site, real.Type2Site })
            {
                Mt.OriginalBytes.CopyTo(code.AsSpan(site));
                BitConverter.GetBytes(0x1A000007u).CopyTo(code, site + 0x10); // bne → Personal path
                BitConverter.GetBytes(0xE1550001u).CopyTo(code, site + 0x24); // cmp r5, r1 (Silvally)
            }
        var profiles = Mt.Profiles.Select(real => real with { WindowHash = Convert.ToHexString(SHA256.HashData(Mt.MaskedWindow(code, real))) }).ToArray();
        return (code, profiles);
    }

    private static byte[] Ncch(ulong title) { byte[] n = new byte[0x200]; "NCCH"u8.CopyTo(n.AsSpan(0x100)); BitConverter.GetBytes(title).CopyTo(n, 0x118); return n; }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var (code, profiles) = Synthetic();
        foreach (var profile in profiles)
        {
            string g = profile.TitleId == US ? "US" : "UM";
            var other = profiles.First(p => p != profile);
            // The synthetic file carries both games' sites; only this game's window hash is valid for this title.
            check(Mt.Inspect(profile.TitleId, code, profiles).State == MultitypeUnlockState.Original, "Multitype_OriginalVerified_" + g);
            byte[] on = Mt.Apply(profile.TitleId, code, true, profiles);
            var diff = Enumerable.Range(0, code.Length).Where(i => code[i] != on[i]).ToArray();
            check(diff.Length > 0 && diff.All(i => (i >= profile.Type1Site && i < profile.Type1Site + 8) || (i >= profile.Type2Site && i < profile.Type2Site + 8)) &&
                on.AsSpan(profile.Type1Site, 8).SequenceEqual(Mt.PatchedBytes) && on.AsSpan(profile.Type2Site, 8).SequenceEqual(Mt.PatchedBytes), "Multitype_WritesOnlyTheTwoConditions_" + g);
            check(Mt.Inspect(profile.TitleId, on, profiles).State == MultitypeUnlockState.Installed, "Multitype_InstalledRecognised_" + g);
            reject(() => Mt.Apply(profile.TitleId, on, true, profiles), "Multitype_DoubleInstallRefused_" + g);
            check(Mt.Apply(profile.TitleId, on, false, profiles).SequenceEqual(code), "Multitype_RemoveRestoresExactBytes_" + g);
            byte[] partial = (byte[])code.Clone(); Mt.PatchedBytes.CopyTo(partial.AsSpan(profile.Type1Site));
            check(Mt.Inspect(profile.TitleId, partial, profiles).State == MultitypeUnlockState.Partial, "Multitype_PartialDetected_" + g);
            byte[] foreign = (byte[])code.Clone(); foreign[profile.Type2Site] = 0x00; foreign[profile.Type2Site + 1] = 0x00; foreign[profile.Type2Site + 2] = 0x50; foreign[profile.Type2Site + 3] = 0xE1;
            check(Mt.Inspect(profile.TitleId, foreign, profiles).State == MultitypeUnlockState.Conflict, "Multitype_ForeignPatchIsAConflict_" + g);
            byte[] moved = (byte[])code.Clone(); moved[profile.WindowStart + 4] ^= 1;
            check(Mt.Inspect(profile.TitleId, moved, profiles).State == MultitypeUnlockState.NotAccredited, "Multitype_OtherRevisionRefused_" + g);
        }
        check(Mt.Inspect(0x0004000000175E00, code, profiles).State == MultitypeUnlockState.NotAccredited && Mt.Inspect(null, code, profiles).State == MultitypeUnlockState.NotAccredited &&
            Mt.Inspect(US, code[..^0x200], profiles).State == MultitypeUnlockState.NotAccredited, "Multitype_TitleAndSizeRefused");
        check(Mt.Inspect(US, code).State == MultitypeUnlockState.NotAccredited, "Multitype_RealProfileRefusesSyntheticCode");
        check(Mt.Profiles[0].Type1Site != Mt.Profiles[1].Type1Site && Mt.Profiles[0].WindowHash != Mt.Profiles[1].WindowHash, "Multitype_IndependentProfilesForUsAndUm");

        using var state = new TestState();
        using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
        {
            string path = Path.Combine(f.Game.ExeFS, "code.bin"); File.WriteAllBytes(path, code);
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(UM));
            var service = new GameMultitypeUnlockService(f.Session, profiles);
            check(EditorValues.Flatten(service.Read(1))["Enable"] == 0 && ProjectFeatureAvailability.Supports(f.Session, "MultitypeUnlock"), "MultitypeService_OffByDefault");
            var preview = service.Preview(new Dictionary<string, int> { ["Enable"] = 1 }, [1]);
            check(preview.Files.Count == 1 && File.ReadAllBytes(path).SequenceEqual(code), "MultitypeService_PreviewWritesNothing");
            service.Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
            check(EditorValues.Flatten(service.Read(1))["Enable"] == 1 && f.Session.History.Read().Entries.Count == 1, "MultitypeService_InstallRecorded");
            var doc = GlobalTemplateDocument.Empty("USUM", 7); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(f.Session.History);
            check(ManualOperations.Read(doc).Any(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameMultitypeUnlockService)), "MultitypeService_CapturedForGlobalTemplate");
            RestorePoints.Undo(f.Session.History, f.Session.ProjectRoot, f.Session.History.Read().Entries.Last().Id);
            check(File.ReadAllBytes(path).SequenceEqual(code), "MultitypeService_UndoRestoresExactBytes");
            var again = new GameMultitypeUnlockService(f.Session, profiles); again.Save(1, new Dictionary<string, int> { ["Enable"] = 1 }); again.Save(1, new Dictionary<string, int> { ["Enable"] = 0 });
            check(File.ReadAllBytes(path).SequenceEqual(code), "MultitypeService_DisableRestoresExactBytes");
        }
        using (var f = new Fixture(pk3DS.Core.GameVersion.SM))
        {
            bool refused; try { _ = new GameMultitypeUnlockService(f.Session); refused = false; } catch (NotSupportedException) { refused = true; }
            check(refused && !ProjectFeatureAvailability.Supports(f.Session, "MultitypeUnlock"), "MultitypeService_SunMoonNotOffered");
        }
    }

    /// <summary>Original USUM ExeFS/RomFS folders (read only, in memory): real profiles and coexistence.</summary>
    internal static void RunReal(string usExefs, string umExefs, string umRomfs, string outDir, Action<bool, string> check)
    {
        foreach (var (exefs, title, g) in new[] { (usExefs, US, "US"), (umExefs, UM, "UM") })
        {
            byte[] code = File.ReadAllBytes(Path.Combine(exefs, "code.bin"));
            check(Mt.Inspect(title, code).State == MultitypeUnlockState.Original, "MultitypeReal_OriginalVerified_" + g);
            check(Mt.Inspect(title == US ? UM : US, code).State == MultitypeUnlockState.NotAccredited, "MultitypeReal_OtherGameProfileRefused_" + g);
            byte[] on = Mt.Apply(title, code, true);
            int changed = Enumerable.Range(0, code.Length).Count(i => code[i] != on[i]);
            check(changed <= 16 && Mt.Apply(title, on, false).SequenceEqual(code), "MultitypeReal_InstallAndExactRemoval_" + g);
            var shinyBefore = GameShinyRateService.InspectCapability(code); var shinyAfter = GameShinyRateService.InspectCapability(on);
            check(shinyBefore.Level == shinyAfter.Level, "MultitypeReal_ShinyRateUnaffected_" + g);
            var mintProfile = UsumNatureMintCatalog.IdentifyCode(code);
            check(UsumNatureMintCatalog.IdentifyCode(on).Id == mintProfile.Id, "MultitypeReal_MintsStillIdentifyCode_" + g);
            // Write a tiny IPS (LayeredFS) for the in-game test: two 8-byte records.
            using var ips = new MemoryStream(); ips.Write("PATCH"u8);
            var profile = Mt.Profiles.First(p => p.TitleId == title);
            foreach (int site in new[] { profile.Type1Site, profile.Type2Site })
            { ips.WriteByte((byte)(site >> 16)); ips.WriteByte((byte)(site >> 8)); ips.WriteByte((byte)site); ips.WriteByte(0); ips.WriteByte(8); ips.Write(Mt.PatchedBytes); }
            ips.Write("EOF"u8);
            string dir = Path.Combine(outDir, g, title.ToString("X16"), "exefs"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "code.ips"), ips.ToArray());
        }
        // Player Level Caps writes code.bin too (UM shown); both orders must leave each other recognised.
        byte[] umCode = File.ReadAllBytes(Path.Combine(umExefs, "code.bin")), battle = File.ReadAllBytes(Path.Combine(umRomfs, "Battle.cro"));
        var caps = LevelCapPatch.InstallManaged(battle, umCode, LevelCapTable.Default());
        check(caps.Success && Mt.Inspect(UM, caps.CodeBin).State == MultitypeUnlockState.Original, "MultitypeReal_InstallableAfterLevelCaps");
        byte[] both = Mt.Apply(UM, caps.CodeBin, true);
        check(LevelCapPatch.InspectCapability(caps.BattleCro, both).Level == LevelCapPatch.InspectCapability(caps.BattleCro, caps.CodeBin).Level, "MultitypeReal_LevelCapsStillRecognised");
        var capsAfter = LevelCapPatch.InstallManaged(battle, Mt.Apply(UM, umCode, true), LevelCapTable.Default());
        check(capsAfter.Success && Mt.Inspect(UM, capsAfter.CodeBin).State == MultitypeUnlockState.Installed, "MultitypeReal_LevelCapsAfterMultitype");
    }
}
