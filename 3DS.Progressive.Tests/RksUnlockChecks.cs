using System.IO;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Mt = pk3DS.Core.Modding.Research.MultitypeUnlockPatch;
using Rk = pk3DS.Core.Modding.Research.RksUnlockPatch;

/// <summary>«Habilidades especiales · Sistema Alfa» (USUM) and its coexistence with Multitype.</summary>
internal static class RksUnlockChecks
{
    private const ulong US = 0x00040000001B5000, UM = 0x00040000001B5100;
    private static byte[] Ncch(ulong title) { byte[] n = new byte[0x200]; "NCCH"u8.CopyTo(n.AsSpan(0x100)); BitConverter.GetBytes(title).CopyTo(n, 0x118); return n; }

    /// <summary>Four configurations and both install/remove orders on one game's code.</summary>
    private static void Matrix(ulong title, byte[] code, IReadOnlyList<MultitypeUnlockProfile>? profiles, string g, Action<bool, string> check)
    {
        bool Both(byte[] c, MultitypeUnlockState m, MultitypeUnlockState r) => Mt.Inspect(title, c, profiles).State == m && Rk.Inspect(title, c, profiles).State == r;
        check(Both(code, MultitypeUnlockState.Original, MultitypeUnlockState.Original), "Rks_NoneInstalled_" + g);
        byte[] mt = Mt.Apply(title, code, true, profiles), rk = Rk.Apply(title, code, true, profiles);
        check(Both(mt, MultitypeUnlockState.Installed, MultitypeUnlockState.Original), "Rks_OnlyMultitype_" + g);
        check(Both(rk, MultitypeUnlockState.Original, MultitypeUnlockState.Installed), "Rks_OnlyRks_" + g);
        byte[] mtThenRk = Rk.Apply(title, mt, true, profiles), rkThenMt = Mt.Apply(title, rk, true, profiles);
        check(mtThenRk.SequenceEqual(rkThenMt) && Both(mtThenRk, MultitypeUnlockState.Installed, MultitypeUnlockState.Installed), "Rks_BothInEitherOrder_" + g);
        int changed = Enumerable.Range(0, code.Length).Count(i => code[i] != rk[i]);
        check(changed > 0 && changed <= 16, "Rks_WritesAtMost16Bytes_" + g);
        check(Mt.Apply(title, Rk.Apply(title, mtThenRk, false, profiles), false, profiles).SequenceEqual(code) &&
            Rk.Apply(title, Mt.Apply(title, mtThenRk, false, profiles), false, profiles).SequenceEqual(code), "Rks_RemoveEitherOrderRestoresExactBytes_" + g);
        check(Rk.Apply(title, mtThenRk, false, profiles).SequenceEqual(mt) && Mt.Apply(title, mtThenRk, false, profiles).SequenceEqual(rk), "Rks_RemovingOneKeepsTheOther_" + g);
    }

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var (code, profiles) = MultitypeUnlockChecks.Synthetic();
        foreach (var p in profiles)
        {
            string g = p.TitleId == US ? "US" : "UM";
            Matrix(p.TitleId, code, profiles, g, check);
            reject(() => Rk.Apply(p.TitleId, Rk.Apply(p.TitleId, code, true, profiles), true, profiles), "Rks_DoubleInstallRefused_" + g);
            byte[] partial = (byte[])code.Clone(); BitConverter.GetBytes(0xE35000E1u).CopyTo(partial, p.Type1Site + 0x24);
            check(Rk.Inspect(p.TitleId, partial, profiles).State == MultitypeUnlockState.Partial && !Rk.Inspect(p.TitleId, partial, profiles).Report.CanApply, "Rks_PartialDetected_" + g);
            byte[] foreign = (byte[])code.Clone(); BitConverter.GetBytes(0xE320F000u).CopyTo(foreign, p.Type2Site + 0x24);
            check(Rk.Inspect(p.TitleId, foreign, profiles).State == MultitypeUnlockState.Conflict && Mt.Inspect(p.TitleId, foreign, profiles).State == MultitypeUnlockState.NotAccredited, "Rks_ForeignWordBlocksBoth_" + g);
        }
        check(Rk.Inspect(0x0004000000175E00, code, profiles).State == MultitypeUnlockState.NotAccredited && Rk.Inspect(US, code).State == MultitypeUnlockState.NotAccredited, "Rks_RefusesOtherTitlesAndSyntheticCodeWithRealProfile");

        using var state = new TestState();
        using (var f = new Fixture(pk3DS.Core.GameVersion.USUM))
        {
            string path = Path.Combine(f.Game.ExeFS, "code.bin"); File.WriteAllBytes(path, code);
            File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(UM));
            // The service uses the real profiles; synthetic code is refused without writing.
            try { _ = new GameRksUnlockService(f.Session); check(false, "RksService_SyntheticRefused"); }
            catch (InvalidDataException e) { check(e.Message.Contains("No se modificó ningún archivo") && File.ReadAllBytes(path).SequenceEqual(code), "RksService_SyntheticRefused"); }
            check(ProjectFeatureAvailability.Supports(f.Session, "RksUnlock"), "RksService_OfferedForUsum");
        }
        using (var f = new Fixture(pk3DS.Core.GameVersion.SM))
        {
            bool refused; try { _ = new GameRksUnlockService(f.Session); refused = false; } catch (NotSupportedException) { refused = true; }
            check(refused && !ProjectFeatureAvailability.Supports(f.Session, "RksUnlock"), "RksService_SunMoonNotOffered");
        }
    }

    /// <summary>Original ExeFS of US and UM (read only): matrix on real code, coexistence, editor and IPS files for the in-game test.</summary>
    internal static void RunReal(string usExefs, string umExefs, string umRomfs, string outDir, Action<bool, string> check)
    {
        foreach (var (exefs, title, g) in new[] { (usExefs, US, "US"), (umExefs, UM, "UM") })
        {
            byte[] code = File.ReadAllBytes(Path.Combine(exefs, "code.bin"));
            Matrix(title, code, null, "Real_" + g, check);
            byte[] both = Rk.Apply(title, Mt.Apply(title, code, true), true);
            check(UsumNatureMintCatalog.IdentifyCode(both).Id == UsumNatureMintCatalog.IdentifyCode(code).Id, "RksReal_MintsStillIdentifyCode_" + g);
            check(GameShinyRateService.InspectCapability(both).Level == GameShinyRateService.InspectCapability(code).Level, "RksReal_ShinyRateUnaffected_" + g);
            foreach (var (name, data) in new[] { ("sistema-alfa", Rk.Apply(title, code, true)), ("ambas", both) })
            {
                var ranges = Enumerable.Range(0, code.Length).Where(i => code[i] != data[i]).ToArray();
                using var ips = new MemoryStream(); ips.Write("PATCH"u8);
                foreach (int at in ranges.Select(i => i & ~3).Distinct())
                { ips.WriteByte((byte)(at >> 16)); ips.WriteByte((byte)(at >> 8)); ips.WriteByte((byte)at); ips.WriteByte(0); ips.WriteByte(4); ips.Write(data.AsSpan(at, 4)); }
                ips.Write("EOF"u8);
                string dir = Path.Combine(outDir, name, g, title.ToString("X16"), "exefs"); Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "code.ips"), ips.ToArray());
            }
        }
        byte[] umCode = File.ReadAllBytes(Path.Combine(umExefs, "code.bin")), battle = File.ReadAllBytes(Path.Combine(umRomfs, "Battle.cro"));
        var caps = LevelCapPatch.InstallManaged(battle, umCode, LevelCapTable.Default());
        byte[] capsRk = Rk.Apply(UM, caps.CodeBin, true);
        check(LevelCapPatch.InspectCapability(caps.BattleCro, capsRk).Level == LevelCapPatch.InspectCapability(caps.BattleCro, caps.CodeBin).Level, "RksReal_LevelCapsStillRecognised");
        var capsAfter = LevelCapPatch.InstallManaged(battle, Rk.Apply(UM, umCode, true), LevelCapTable.Default());
        check(capsAfter.Success && Rk.Inspect(UM, capsAfter.CodeBin).State == MultitypeUnlockState.Installed, "RksReal_LevelCapsAfterRks");
        // Editors: both options through the history, Global Template and exact undo.
        using var state = new TestState();
        using var f = new Fixture(pk3DS.Core.GameVersion.USUM);
        string path = Path.Combine(f.Game.ExeFS, "code.bin"); File.WriteAllBytes(path, umCode);
        File.WriteAllBytes(Path.Combine(f.Session.ProjectRoot, "HeaderNCCH0.bin"), Ncch(UM));
        new GameMultitypeUnlockService(f.Session).Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
        new GameRksUnlockService(f.Session).Save(1, new Dictionary<string, int> { ["Enable"] = 1 });
        byte[] installed = File.ReadAllBytes(path);
        check(EditorValues.Flatten(new GameMultitypeUnlockService(f.Session).Read(1))["Enable"] == 1 && EditorValues.Flatten(new GameRksUnlockService(f.Session).Read(1))["Enable"] == 1, "RksService_BothOptionsOnTogether");
        var doc = GlobalTemplateDocument.Empty("USUM", 7); doc.SetSection("ProgressiveApp", new System.Text.Json.Nodes.JsonObject { ["PartialSnapshot"] = true }); doc.MergeHistoryActions(f.Session.History);
        check(ManualOperations.Read(doc).Any(o => o["Payload"]?["Editor"]?.GetValue<string>() == nameof(GameRksUnlockService)), "RksService_CapturedForGlobalTemplate");
        RestorePoints.Undo(f.Session.History, f.Session.ProjectRoot, f.Session.History.Read().Entries.Last().Id);
        check(Mt.Inspect(UM, File.ReadAllBytes(path)).State == MultitypeUnlockState.Installed && Rk.Inspect(UM, File.ReadAllBytes(path)).State == MultitypeUnlockState.Original, "RksService_UndoRksKeepsMultitype");
        RestorePoints.Undo(f.Session.History, f.Session.ProjectRoot, f.Session.History.Read().Entries.First().Id);
        check(File.ReadAllBytes(path).SequenceEqual(umCode), "RksService_UndoBothRestoresExactFile");
        ManualEditTemplate.Apply(f.Session, doc);
        check(File.ReadAllBytes(path).SequenceEqual(installed), "RksService_GlobalTemplateReplaysBoth");
    }
}
