using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.ProjectDetection;

/// <summary>Universal project detector (B1): synthetic entries per platform, rejections, misleading names and proof that nothing is written.</summary>
internal static class ProjectDetectorChecks
{
    private static readonly Dictionary<string, byte[]> RomFiles = new() { ["untouched.bin"] = [7, 1, 99, 18] };

    private static byte[] Rom(string code, byte version) => NdsGen5RandomizerChecks.BuildRom(RomFiles, code, version);

    /// <summary>Digest of a whole tree: relative path, length, last write time and the SHA-256 of every file, plus the folders themselves.</summary>
    private static string Snapshot(string root)
    {
        var sb = new StringBuilder();
        if (File.Exists(root)) { var f = new FileInfo(root); return $"{f.Length}|{f.LastWriteTimeUtc.Ticks}|{(int)f.Attributes}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(root)))}"; }
        foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(root, entry);
            if (Directory.Exists(entry)) sb.Append("D|").Append(rel).Append('|').Append(Directory.GetLastWriteTimeUtc(entry).Ticks).Append('\n');
            else { var f = new FileInfo(entry); sb.Append("F|").Append(rel).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).Append('|').Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))).Append('\n'); }
        }
        return sb.ToString();
    }

    private static string Write(string folder, string name, byte[] bytes) { string path = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path; }

    private static string Make3ds(string root, string name, int garc, bool exe = true, string exeName = "ExeFS", string romName = "RomFS", bool code = true)
    {
        string game = Path.Combine(root, name);
        string a = Path.Combine(game, romName, "a");
        for (int i = 0; i < garc; i++) Write(a, $"{i / 100}/{i / 10 % 10}/{i % 10}", [1]);
        if (exe) { Directory.CreateDirectory(Path.Combine(game, exeName)); if (code) File.WriteAllBytes(Path.Combine(game, exeName, "code.bin"), [1, 2, 3]); }
        return game;
    }

    private static readonly string SwordBuild = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000";
    private static readonly string ShieldBuild = "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000";

    private static byte[] Npdm(ulong titleId, bool validMeta = true, bool validAci = true)
    {
        var b = new byte[0xC0]; if (validMeta) "META"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x70), 0x80); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x74), 0x40);
        if (validAci) "ACI0"u8.CopyTo(b.AsSpan(0x80)); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x90), titleId); return b;
    }
    private static byte[] Nso(string buildHex, bool valid = true)
    {
        var b = new byte[0x200]; if (valid) "NSO0"u8.CopyTo(b); Convert.FromHexString(buildHex).CopyTo(b, 0x40); return b;
    }
    private static string MakeSwitch(string root, string name, ulong titleId, string build, bool personal = true, bool npdm = true, bool main = true, bool validMeta = true, bool validNso = true)
    {
        string game = Path.Combine(root, name);
        if (npdm) Write(game, "exefs/main.npdm", Npdm(titleId, validMeta)); else Directory.CreateDirectory(Path.Combine(game, "exefs"));
        if (main) Write(game, "exefs/main", Nso(build, validNso));
        if (personal) Write(game, "romfs/bin/pml/personal/personal_total.bin", [1, 2, 3, 4]);
        return game;
    }

    private static string Checkpoint(string folder, string name, string source, int version = 1, string game = "CPUE", int revision = 1, string? format = null, bool addOps = false)
    {
        var doc = new NdsEventDocument(format ?? NdsEventSession.Format, version, game, revision, source, new string('A', 64), [], [], version >= 2 ? Array.Empty<NdsEventOperation>() : null);
        return Write(folder, name, JsonSerializer.SerializeToUtf8Bytes(doc));
    }

    internal static void Run(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "project-detector-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var all = new List<(string Path, ProjectDetection Result)>();
            ProjectDetection Do(string path) { var r = ProjectDetector.Detect(path); all.Add((path, r)); return r; }

            // ---- Nintendo DS: the six accredited profiles
            var profiles = new[] { ("ADAE", (byte)5, "Diamante"), ("CPUE", (byte)1, "Platino"), ("IPKE", (byte)0, "HeartGold"), ("IRBO", (byte)0, "Negro"), ("IRAS", (byte)0, "Blanco"), ("IRDO", (byte)0, "Blanco 2") };
            string roms = Path.Combine(temp, "roms"); Directory.CreateDirectory(roms);
            foreach (var (code, version, label) in profiles)
            {
                var d = Do(Write(roms, $"{label}.nds", Rom(code, version)));
                check(d is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.NintendoDS, Kind: DetectedKind.DsRom } && d.Game.Contains(label, StringComparison.OrdinalIgnoreCase) && d.IsCandidate, "Detector_DsRom_" + label + "_IsCompatible");
            }
            var wrongRev = Do(Write(roms, "Diamante-rev4.nds", Rom("ADAE", 4)));
            check(wrongRev is { Status: DetectionStatus.Unsupported, Platform: DetectedPlatform.NintendoDS } && wrongRev.Message.Contains("ADAE revisión 4") && !wrongRev.IsCandidate, "Detector_DsRom_UnsupportedRevisionIsNotACandidate");
            var otherGame = Do(Write(roms, "Otro.nds", Rom("BPRE", 0)));
            check(otherGame is { Status: DetectionStatus.Unsupported } && otherGame.Message.Contains("BPRE"), "Detector_DsRom_UnknownGameCodeIsRecognisedButUnsupported");
            var pokeJapanese = Do(Write(roms, "PlatinoJ.nds", Rom("CPUJ", 0)));
            check(pokeJapanese.Status == DetectionStatus.Unsupported, "Detector_DsRom_NoGenerationOrRegionIsAssumed");

            // ---- invalid, truncated and misleading DS entries
            var corrupt = Rom("CPUE", 1); corrupt[0x14] ^= 0xFF;
            var d1 = Do(Write(roms, "crc.nds", corrupt));
            check(d1.Status == DetectionStatus.Incomplete && d1.Message.Contains("cabecera") && d1.Message.Contains(".nds"), "Detector_DsRom_BadHeaderCrcIsIncomplete");
            var d2 = Do(Write(roms, "corta.nds", Rom("CPUE", 1)[..0x300]));
            check(d2.Status == DetectionStatus.Incomplete, "Detector_DsRom_TruncatedHeaderIsIncomplete");
            var d3 = Do(Write(roms, "texto.nds", Encoding.UTF8.GetBytes(new string('x', 5000))));
            check(d3.Status == DetectionStatus.Incomplete, "Detector_DsRom_GarbageNamedNdsIsIncomplete");
            var d4 = Do(Write(roms, "basura.bin", new byte[5000].Select((_, i) => (byte)(i * 31 + 7)).ToArray()));
            check(d4.Status == DetectionStatus.Unknown && !d4.IsCandidate, "Detector_UnknownBinaryIsUnknown");
            var d5 = Do(Write(roms, "vacio.nds", []));
            check(d5.Status == DetectionStatus.Unknown && d5.Message.Contains("vacío"), "Detector_EmptyFileIsUnknown");
            var d6 = Do(Write(roms, "Platino-sin-extension-ROM.txt", Rom("CPUE", 1)));
            check(d6 is { Status: DetectionStatus.Compatible, Kind: DetectedKind.DsRom }, "Detector_ContentWinsOverExtension_RomNamedTxt");
            var png = Do(Write(roms, "imagen.nds", [0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10, .. new byte[0x2000]]));
            check(png.Status == DetectionStatus.Incomplete && !png.IsCandidate, "Detector_ImageNamedNdsIsNotARom");

            // ---- containers that are recognised and never opened
            byte[] Sig(int offset, string ascii, int size = 0x400) { var b = new byte[size]; Encoding.ASCII.GetBytes(ascii).CopyTo(b, offset); return b; }
            check(Do(Write(roms, "juego.nsp", Sig(0, "PFS0"))) is { Status: DetectionStatus.Unsupported } n && n.Message.Contains("NSP") && n.Platform == DetectedPlatform.None, "Detector_Nsp_IsUnsupportedWithAdvice");
            check(Do(Write(roms, "juego.xci", Sig(0x100, "HEAD"))).Message.Contains("XCI"), "Detector_Xci_IsUnsupportedWithAdvice");
            check(Do(Write(roms, "juego.3ds", Sig(0x100, "NCSD"))).Message.Contains("descifra"), "Detector_3dsImage_IsUnsupportedWithAdvice");
            check(Do(Write(roms, "juego.cxi", Sig(0x100, "NCCH"))).Message.Contains("CXI"), "Detector_Cxi_IsUnsupportedWithAdvice");
            var cia = new byte[0x400]; BinaryPrimitives.WriteUInt32LittleEndian(cia, 0x2020);
            check(Do(Write(roms, "juego.cia", cia)).Message.Contains("CIA"), "Detector_Cia_IsUnsupportedWithAdvice");
            check(Do(Write(roms, "Platino.zip", Sig(0, "PK\u0003\u0004"))).Message.Contains("comprimido"), "Detector_Archive_AsksToExtractFirst");
            check(Do(Write(roms, "renombrada.nds", Sig(0, "PFS0"))) is { Status: DetectionStatus.Unsupported }, "Detector_NspRenamedNdsIsStillAnNsp");

            // ---- Nintendo DS checkpoints v1, v2 and v3
            string cps = Path.Combine(temp, "checkpoints"); Directory.CreateDirectory(cps);
            string source = Write(cps, "Platino.nds", Rom("CPUE", 1));
            foreach (int v in new[] { 1, 2, 3 })
            {
                var d = Do(Checkpoint(cps, $"v{v}.ndsevents.json", source, v));
                check(d is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.NintendoDS, Kind: DetectedKind.DsEventCheckpoint } && d.SourcePath == source && d.Game.Contains("Platino"), $"Detector_Checkpoint_V{v}_IsCompatible");
            }
            check(Do(Checkpoint(cps, "v4.ndsevents.json", source, 4)) is { Status: DetectionStatus.Unsupported } v4 && v4.Message.Contains("versión 4"), "Detector_Checkpoint_NewerVersionIsUnsupported");
            check(Do(Checkpoint(cps, "dia.ndsevents.json", source, 1, "ADAE")).Status == DetectionStatus.Unsupported, "Detector_Checkpoint_OtherGameIsUnsupported");
            check(Do(Checkpoint(cps, "rev0.ndsevents.json", source, 1, "CPUE", 0)).Status == DetectionStatus.Unsupported, "Detector_Checkpoint_OtherRevisionIsUnsupported");
            check(Do(Checkpoint(cps, "sinrom.ndsevents.json", Path.Combine(cps, "no-existe.nds"))) is { Status: DetectionStatus.Incomplete } missing && missing.Message.Contains("no existe"), "Detector_Checkpoint_MissingSourceRomIsIncomplete");
            string hg = Write(cps, "HeartGold.nds", Rom("IPKE", 0));
            check(Do(Checkpoint(cps, "otrarom.ndsevents.json", hg)).Status == DetectionStatus.Unsupported, "Detector_Checkpoint_SourceOfAnotherGameIsUnsupported");
            string badSource = Write(cps, "roto.nds", [1, 2, 3, 4]);
            check(Do(Checkpoint(cps, "origenroto.ndsevents.json", badSource)).Status == DetectionStatus.Incomplete, "Detector_Checkpoint_UnreadableSourceIsIncomplete");
            check(Do(Checkpoint(cps, "sinnds.ndsevents.json", Path.Combine(cps, "Platino.bin"))).Status == DetectionStatus.Incomplete, "Detector_Checkpoint_SourceMustBeNds");
            string whole = File.ReadAllText(Path.Combine(cps, "v3.ndsevents.json"));
            check(Do(Write(cps, "truncado.ndsevents.json", Encoding.UTF8.GetBytes(whole[..(whole.Length / 2)]))).Status == DetectionStatus.Incomplete, "Detector_Checkpoint_TruncatedJsonIsIncomplete");
            check(Do(Write(cps, "otro.ndsevents.json", Encoding.UTF8.GetBytes("{\"Format\":\"Otro\",\"Version\":1}"))).Status == DetectionStatus.Unknown, "Detector_Json_OtherFormatIsUnknown");
            check(Do(Write(cps, "lista.json", Encoding.UTF8.GetBytes("[1,2,3]"))).Status == DetectionStatus.Unknown, "Detector_Json_ArrayIsUnknown");
            check(Do(Write(cps, "plantilla.json", Encoding.UTF8.GetBytes("{\"Name\":\"Global Template\",\"Steps\":[]}"))).Status == DetectionStatus.Unknown, "Detector_Json_TemplateIsNotAProject");
            check(Do(Checkpoint(cps, "enganoso.nds", source)) is { Status: DetectionStatus.Unsupported } mis && mis.Kind == DetectedKind.DsEventCheckpoint && mis.Message.Contains("ndsevents.json"), "Detector_Checkpoint_NamedNdsIsNotOpenedWithThatName");
            check(Do(Write(cps, "rom.ndsevents.json", Rom("CPUE", 1))).Status == DetectionStatus.Ambiguous, "Detector_Name_And_Content_Disagree_IsAmbiguous");
            var big = new byte[9 * 1024 * 1024]; Array.Fill(big, (byte)' '); big[0] = (byte)'{';
            check(Do(Write(cps, "enorme.ndsevents.json", big)).Status == DetectionStatus.Unknown, "Detector_Json_OversizedIsUnknown");

            // ---- Nintendo 3DS extracted folders
            string trees = Path.Combine(temp, "trees"); Directory.CreateDirectory(trees);
            foreach (var (name, garc, label) in new[] { ("xy", 271, "X/Y"), ("oras", 299, "Rubí Omega"), ("sm", 311, "Sol/Luna"), ("usum", 333, "Ultrasol") })
            {
                var d = Do(Make3ds(trees, name, garc));
                check(d is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.Nintendo3DS, Kind: DetectedKind.Extracted3dsFolder } && d.Game.Contains(label) && d.Evidence.Any(e => e.Contains(garc + " archivos GARC")), "Detector_3ds_" + name + "_IsCompatible");
            }
            // Demos: recognised by their structure, candidates WITH a warning (the engine keeps opening them); never presented as accredited games.
            var demoOras = Do(Make3ds(trees, "demo-oras", 301)); var demoSm = Do(Make3ds(trees, "demo-sm", 239));
            check(demoOras is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.Nintendo3DS, Kind: DetectedKind.Extracted3dsFolder, IsCandidate: true, HasWarning: true } && demoOras.Game == "Pokémon Rubí Omega/Zafiro Alfa (demo)" && demoOras.Warning!.Contains("DEMO") && demoOras.Warning.Contains("matriz pública") && demoOras.Evidence.Any(e => e.Contains("demo") && e.Contains("301")), "Detector_3ds_OrasDemoIsACandidateWithAWarning");
            check(demoSm is { Status: DetectionStatus.Compatible, IsCandidate: true, HasWarning: true } && demoSm.Game == "Pokémon Sol/Luna (demo)" && demoSm.Evidence.Any(e => e.Contains("239")), "Detector_3ds_SunMoonDemoIsACandidateWithAWarning");
            check(all.Where(a => a.Result.Kind == DetectedKind.Extracted3dsFolder && a.Result.IsCandidate && !a.Result.Game.Contains("demo")).All(a => !a.Result.HasWarning), "Detector_3ds_FullGamesCarryNoWarning");
            check(all.Where(a => a.Result.HasWarning).All(a => a.Result.Game.Contains("(demo)")) && all.Count(a => a.Result.HasWarning) == 2, "Detector_OnlyDemosCarryAWarning");
            foreach (int other in new[] { 238, 240, 270, 272, 298, 300, 302, 310, 312, 332, 334 })
                check(Do(Make3ds(trees, "garc-" + other, other)) is { Status: DetectionStatus.Unsupported, IsCandidate: false, HasWarning: false } u && u.Message.Contains(other.ToString()) && u.Message.Contains("no reconocida"), "Detector_3ds_UnknownGarcCount_" + other + "_IsRejectedNotAnUnrecognisedDemo");
            check(Do(Make3ds(trees, "sinexe", 311, exe: false)) is { Status: DetectionStatus.Incomplete, Platform: DetectedPlatform.Nintendo3DS } ne && ne.Message.Contains("ExeFS"), "Detector_3ds_MissingExeFsIsIncomplete");
            check(Do(Make3ds(trees, "sincode", 311, code: false)).Status == DetectionStatus.Incomplete, "Detector_3ds_ExeFsWithoutCodeIsIncomplete");
            string noRom = Path.Combine(trees, "sinrom"); Directory.CreateDirectory(Path.Combine(noRom, "ExeFS")); File.WriteAllBytes(Path.Combine(noRom, "ExeFS", "code.bin"), [1]);
            check(Do(noRom) is { Status: DetectionStatus.Incomplete } nr && nr.Message.Contains("RomFS"), "Detector_3ds_MissingRomFsIsIncomplete");
            check(Do(Path.Combine(Make3ds(trees, "inner", 311), "RomFS")) is { Status: DetectionStatus.Incomplete, Platform: DetectedPlatform.Nintendo3DS } inner && inner.Message.Contains("raíz"), "Detector_3ds_InnerRomFsAsksForTheRoot");
            check(Do(Make3ds(trees, "otronombre", 311, romName: "romfs-extraido", exeName: "exefs_ok")).Status == DetectionStatus.Compatible, "Detector_3ds_SameFolderNameRulesAsTheEngine");

            // ---- the existing engine really opens a demo structure (synthetic on-disk projects: every GARC the engine reads, valid and minimal)
            foreach (var (version, count, label) in new[] { (GameVersion.ORASDEMO, 301, "OrasDemo"), (GameVersion.SMDEMO, 239, "SunMoonDemo"), (GameVersion.ORAS, 299, "OrasFullGameControl") })
            {
                string root = MakeDemoProject(trees, "motor-" + label, version, count);
                var detected = Do(root);
                bool demo = version != GameVersion.ORAS;
                check(detected is { Status: DetectionStatus.Compatible } && detected.HasWarning == demo && detected.Evidence.Any(e => e.Contains(count + " archivos GARC")), "Detector_" + label + "_OnDiskProjectIsACandidate" + (demo ? "WithAWarning" : "WithoutWarning"));
                string before = Snapshot(root);
                try { var session = GameProjectSession.Open(root, false); check(session.GameName == version.ToString() && session.Entries.Count > 0 && Snapshot(root) == before, "Engine_" + label + "_StillOpensThroughGameProjectSession"); }
                catch (Exception ex) { check(false, "Engine_" + label + "_StillOpensThroughGameProjectSession: " + ex.GetType().Name + ": " + ex.Message); }
            }
            // A Gen 6 demo is judged by the engine exactly like a Gen 6 full game: without the machine-table signature in code.bin both are rejected with the same message.
            foreach (var (version, count, label) in new[] { (GameVersion.ORASDEMO, 301, "OrasDemo"), (GameVersion.ORAS, 299, "OrasFullGameControl") })
            {
                string root = MakeDemoProject(trees, "motor-sin-tabla-" + label, version, count, machineTable: false);
                try { GameProjectSession.Open(root, false); check(false, "Engine_" + label + "_WithoutMachineTableIsRejected"); }
                catch (InvalidDataException ex) { check(ex.Message.Contains("code.bin"), "Engine_" + label + "_WithoutMachineTableIsRejectedLikeAnyGen6Game"); }
            }

            // ---- Switch extractions
            check(Do(MakeSwitch(trees, "espada", 0x0100ABF008968000, SwordBuild)) is { Status: DetectionStatus.Compatible, Platform: DetectedPlatform.NintendoSwitch, Kind: DetectedKind.SwitchExtraction } sw && sw.Game == "Pokémon Espada" && sw.Evidence.Any(e => e.Contains("0100ABF008968000")), "Detector_Switch_SwordIsCompatible");
            check(Do(MakeSwitch(trees, "escudo", 0x01008DB008C2C000, ShieldBuild)) is { Status: DetectionStatus.Compatible } sh && sh.Game == "Pokémon Escudo", "Detector_Switch_ShieldIsCompatible");
            check(Do(MakeSwitch(trees, "espada-otra", 0x0100ABF008968000, ShieldBuild)) is { Status: DetectionStatus.Unsupported } ob && ob.Message.Contains("1.3.2") && ob.Game == "Pokémon Espada", "Detector_Switch_OtherBuildIsUnsupported");
            check(Do(MakeSwitch(trees, "otro-juego", 0x0100000000001000, SwordBuild)).Status == DetectionStatus.Unsupported, "Detector_Switch_OtherTitleIdIsUnsupported");
            check(Do(MakeSwitch(trees, "sin-personal", 0x0100ABF008968000, SwordBuild, personal: false)) is { Status: DetectionStatus.Incomplete } np && np.Message.Contains("personal_total"), "Detector_Switch_MissingPersonalIsIncomplete");
            check(Do(MakeSwitch(trees, "sin-npdm", 0x0100ABF008968000, SwordBuild, npdm: false)) is { Status: DetectionStatus.Incomplete, Platform: DetectedPlatform.NintendoSwitch } nn && nn.Message.Contains("main.npdm"), "Detector_Switch_MissingNpdmIsIncomplete");
            check(Do(MakeSwitch(trees, "sin-main", 0x0100ABF008968000, SwordBuild, main: false)).Status == DetectionStatus.Incomplete, "Detector_Switch_MissingMainIsIncomplete");
            check(Do(MakeSwitch(trees, "meta-mala", 0x0100ABF008968000, SwordBuild, validMeta: false)).Status == DetectionStatus.Unsupported, "Detector_Switch_InvalidNpdmHeaderIsRejected");
            check(Do(MakeSwitch(trees, "nso-malo", 0x0100ABF008968000, SwordBuild, validNso: false)).Status == DetectionStatus.Unsupported, "Detector_Switch_InvalidNsoHeaderIsRejected");
            string innerExe = Path.Combine(trees, "solo-exefs"); Write(innerExe, "main.npdm", Npdm(0x0100ABF008968000)); Write(innerExe, "main", Nso(SwordBuild));
            check(Do(innerExe) is { Status: DetectionStatus.Incomplete, Platform: DetectedPlatform.NintendoSwitch } ie && ie.Message.Contains("exefs"), "Detector_Switch_InnerExeFsAsksForTheRoot");

            // ---- ambiguous, empty and foreign folders
            string both = Make3ds(trees, "ambigua", 311); MakeSwitchInto(both);
            // exefs/ and romfs/ of Switch next to the 3DS RomFS/ExeFS (the same folders on Windows): one root, two platforms.
            var amb = Do(both);
            check(amb is { Status: DetectionStatus.Ambiguous, Platform: DetectedPlatform.None } && !amb.IsCandidate && amb.Message.Contains("3DS") && amb.Message.Contains("Switch"), "Detector_CompleteRootOfTwoPlatformsIsAmbiguousAndNeverCandidate");
            string mixed = Path.Combine(trees, "mezcla"); Directory.CreateDirectory(Path.Combine(mixed, "RomFS", "a", "0")); Write(mixed, "exefs/main.npdm", Npdm(0x0100ABF008968000));
            check(Do(mixed).Status == DetectionStatus.Ambiguous, "Detector_PiecesOfTwoPlatformsAreAmbiguous");
            string empty = Path.Combine(temp, "vacia"); Directory.CreateDirectory(empty);
            check(Do(empty).Status == DetectionStatus.Unknown, "Detector_EmptyFolderIsUnknown");
            string withNds = Path.Combine(temp, "con-nds"); Write(withNds, "a.nds", Rom("CPUE", 1)); Write(withNds, "b.nds", Rom("ADAE", 5));
            check(Do(withNds) is { Status: DetectionStatus.Unknown } wn && wn.Message.Contains("2 archivo(s) .nds") && wn.Message.Contains("directamente"), "Detector_FolderOfRomsIsNeverGuessed");
            string docs = Path.Combine(temp, "documentos"); Write(docs, "notas.txt", [65]);
            check(Do(docs).Status == DetectionStatus.Unknown, "Detector_UnrelatedFolderIsUnknown");

            // ---- inaccessible entries and odd input
            check(Do(Path.Combine(temp, "no-existe.nds")) is { Status: DetectionStatus.Inaccessible } gone && gone.Message.Contains("no existe"), "Detector_MissingPathIsInaccessible");
            check(Do("") is { Status: DetectionStatus.Unknown } && Do("   ").Status == DetectionStatus.Unknown && ProjectDetector.Detect(null).Status == DetectionStatus.Unknown, "Detector_EmptyInputIsUnknown");
            check(ProjectDetector.Detect("\"" + Path.Combine(roms, "Platino.nds") + "\"").Status == DetectionStatus.Compatible, "Detector_QuotedPathIsAccepted");
            check(Do("bad\0path").Status == DetectionStatus.Inaccessible, "Detector_InvalidPathIsInaccessible");
            string locked = Write(roms, "bloqueada.nds", Rom("CPUE", 1));
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                check(Do(locked) is { Status: DetectionStatus.Inaccessible } lk && lk.Message.Contains("otro programa"), "Detector_FileLockedByAnotherProgramIsInaccessible");
            string held = Write(roms, "abierta-por-emulador.nds", Rom("CPUE", 1));
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
                check(Do(held).Status == DetectionStatus.Compatible, "Detector_FileOpenForReadingByAnotherProgramStillDetects");

            // ---- only compatible entries are candidates, and the messages are never raw exceptions
            check(all.Where(a => a.Result.IsCandidate).All(a => a.Result.Status == DetectionStatus.Compatible && a.Result.Platform != DetectedPlatform.None && a.Result.Kind != DetectedKind.None && a.Result.Game.Length > 0), "Detector_EveryCandidateHasPlatformKindAndGame");
            check(all.All(a => a.Result.Message.Length > 0 && !a.Result.Message.Contains("Exception") && !a.Result.Message.Contains("   at ")), "Detector_MessagesAreHumanReadable");
            check(all.Where(a => a.Result.Status == DetectionStatus.Compatible).All(a => a.Result.Evidence.Count >= 1), "Detector_CompatibleResultsExplainTheirEvidence");

            // ---- the detector never writes: the fixtures are byte-, size- and timestamp-identical afterwards
            string beforeAll = Snapshot(temp);
            foreach (var (path, _) in all.Where(a => File.Exists(a.Path) || Directory.Exists(a.Path))) ProjectDetector.Detect(path);
            check(Snapshot(temp) == beforeAll, "Detector_NeverModifiesFilesOrFolders");
            var readOnly = new List<string>();
            foreach (string file in Directory.EnumerateFiles(roms)) { var at = File.GetAttributes(file); File.SetAttributes(file, at | FileAttributes.ReadOnly); readOnly.Add(file); }
            check(ProjectDetector.Detect(Path.Combine(roms, "Platino.nds")).Status == DetectionStatus.Compatible && Directory.EnumerateFiles(roms).All(f => (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0), "Detector_WorksOnReadOnlyFilesAndKeepsThem");
            foreach (string file in readOnly) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            string entries = string.Join("|", Directory.EnumerateFileSystemEntries(temp, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            ProjectDetector.Detect(temp); ProjectDetector.Detect(roms);
            check(entries == string.Join("|", Directory.EnumerateFileSystemEntries(temp, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)), "Detector_CreatesNoTemporaryFilesNextToTheInput");
            // A 3DS project with leftovers of an interrupted save: recovery belongs to the engine (GameProjectSession.Open), never to the detector.
            string marker = Path.Combine(Make3ds(trees, "con-guardado-pendiente", 311), "history.json.pending"); File.WriteAllText(marker, "x");
            string pendingBefore = Snapshot(Path.Combine(trees, "con-guardado-pendiente")); ProjectDetector.Detect(Path.Combine(trees, "con-guardado-pendiente"));
            check(Snapshot(Path.Combine(trees, "con-guardado-pendiente")) == pendingBefore, "Detector_DoesNotTouchAProjectWithLeftovers");

            // ---- robustness: arbitrary bytes never throw and are never accepted
            var random = new Random(20261110); bool accepted = false;
            for (int i = 0; i < 150; i++)
            {
                var bytes = new byte[random.Next(1, 0x1400)]; random.NextBytes(bytes);
                if (i % 3 == 0 && bytes.Length > 4) bytes[0] = (byte)'{';
                string fuzz = Write(Path.Combine(temp, "fuzz"), $"f{i}{(i % 2 == 0 ? ".nds" : ".ndsevents.json")}", bytes);
                if (ProjectDetector.Detect(fuzz).IsCandidate) accepted = true;
            }
            check(!accepted, "Detector_RandomBytesNeverProduceACandidate");
            // a single flipped byte in the header of every accredited game is enough to reject it
            bool flipped = true;
            foreach (var (code, version, _) in profiles) { var bytes = Rom(code, version); bytes[0x0C + 1] ^= 1; flipped &= ProjectDetector.Detect(Write(Path.Combine(temp, "flip"), code + ".nds", bytes)).Status == DetectionStatus.Incomplete; }
            check(flipped, "Detector_AnyHeaderTamperingIsRejectedByTheCrc");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }

    /// <summary>
    /// A synthetic but complete on-disk demo project: every GARC that GameConfig.InitializeAll reads (personal, level-up, game text, moves, evolutions)
    /// plus filler one-character files up to the exact GARC count that identifies the demo.
    /// </summary>
    private static string MakeDemoProject(string parent, string name, GameVersion version, int garcCount, bool machineTable = true)
    {
        string root = Path.Combine(parent, name), romfs = Path.Combine(root, "RomFS"), exefs = Path.Combine(root, "ExeFS");
        Directory.CreateDirectory(romfs); Directory.CreateDirectory(exefs);
        bool gen6 = version is GameVersion.ORASDEMO or GameVersion.ORAS;
        // Gen 6 (demo or not) needs a code.bin of at least 4 MiB, 512-aligned, with the MT/MO table signature exactly once: that is a requirement of the engine, not of the detector.
        var code = machineTable && gen6 ? new byte[0x400200] : [1, 2, 3];
        if (machineTable && gen6) GameMachine6Service.Signature.CopyTo(code, 0x400010);
        File.WriteAllBytes(Path.Combine(exefs, "code.bin"), code);
        int language = AppPreferences.Load().GameTextLanguageIndex; if (gen6 && language > 7) language = 2;   // the same rule GameProjectSession.Open applies
        var game = new GameConfig(version) { RomFS = romfs, ExeFS = exefs, Language = language };
        var refs = gen6 ? GARCReference.GARCReference_AO : GARCReference.GARCReference_SMDEMO; var text = gen6 ? TextReference.GameText_AO : TextReference.GameText_SMDEMO;
        void Set(string property, object value) => typeof(GameConfig).GetProperty(property)!.SetValue(game, value);
        Set("Files", refs); Set("GameText", text); Set("Info", new GameInfo(game)); Set("Variables", gen6 ? TextVariableCode.VariableCodes_AO : TextVariableCode.VariableCodes_SM);
        void Write(string garcName, byte[][] files)
        {
            var reference = game.GetGARCReference(garcName); if (reference.LanguageVariant) reference = reference.GetRelativeGARC(game.Language, reference.Name);
            string path = Path.Combine(romfs, reference.Reference); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new GARCFile(GARC.PackGARC(files, game.GARCVersion, 4), reference, path).Save();
        }
        int size = gen6 ? 0x50 : 0x54;
        byte[][] personal = Enumerable.Range(0, 840).Select(i => { var data = new byte[size]; data[0] = 60; data[1] = 80; return data; }).ToArray();
        Write("personal", [.. personal, personal.SelectMany(r => r).ToArray()]);
        Write("levelup", Enumerable.Range(0, 840).Select(_ => new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).ToArray());
        var moves = Enumerable.Range(0, 800).Select(_ => new byte[gen6 ? 0x22 : 0x28]).ToArray();
        Write("move", [Mini.PackMini(moves, "WD")]);
        Write("evolution", Enumerable.Range(0, 840).Select(_ => new byte[gen6 ? 0x30 : 0x40]).ToArray());
        string[] lines = Enumerable.Range(0, 1000).Select(i => "Entry" + i).ToArray();
        byte[] textFile = TextFile.GetBytes(game, lines);
        Write("gametext", Enumerable.Range(0, text.Max(t => t.Index) + 1).Select(_ => textFile).ToArray());
        string a = Path.Combine(romfs, "a"); int have = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Count(f => Path.GetFileName(f).Length == 1);
        if (have > garcCount) throw new InvalidOperationException($"The demo fixture already has {have} GARC files (target {garcCount}).");
        for (int i = 0; have + i < garcCount; i++) { string filler = Path.Combine(a, "z", (i / 10).ToString(), (i % 10).ToString()); Directory.CreateDirectory(Path.GetDirectoryName(filler)!); File.WriteAllBytes(filler, [1]); }
        return root;
    }

    private static void MakeSwitchInto(string root)
    {
        Write(root, "exefs/main", Nso(SwordBuild)); Write(root, "exefs/main.npdm", Npdm(0x0100ABF008968000)); Write(root, "romfs/bin/pml/personal/personal_total.bin", [1]);
    }

    /// <summary>Real, read-only probe: detects each path and proves, by SHA-256 (files) or by a size/time/hash snapshot (folders), that nothing changed.</summary>
    internal static void Probe(string output, IReadOnlyList<string> paths)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        var rows = new List<object>(); bool failed = false;
        foreach (string path in paths)
        {
            string before = FastSnapshot(path); var sw = System.Diagnostics.Stopwatch.StartNew(); var r = ProjectDetector.Detect(path); sw.Stop();
            // Agreement with the real engines (read-only opens): a candidate must open; a 3DS project is only opened by the engine in B2, on a copy,
            // because GameProjectSession.Open recovers interrupted saves by default.
            string engine = "no ejecutado";
            if (r.IsCandidate && r.Platform == DetectedPlatform.NintendoDS) { try { using var p = ThreeDS.Progressive.Services.Nds.NdsProject.Open(path, false); engine = "NdsProject.Open: abierto"; } catch (Exception ex) { engine = "NdsProject.Open: rechazado · " + ex.Message; failed = true; } }
            else if (r.IsCandidate && r.Platform == DetectedPlatform.NintendoSwitch) { try { _ = ThreeDS.Progressive.Services.Switch.SwordShieldProject.Open(path); engine = "SwordShieldProject.Open: abierto"; } catch (Exception ex) { engine = "SwordShieldProject.Open: rechazado · " + ex.Message; failed = true; } }
            else if (r.IsCandidate) engine = "GameProjectSession.Open: se comprobará en B2 sobre una copia";
            string after = FastSnapshot(path);
            bool same = before == after; failed |= !same;
            Console.WriteLine($"{(same ? "PASS" : "FAIL")} {r.Status,-12} {r.PlatformName,-15} {r.Kind,-18} {r.Game} · {sw.ElapsedMilliseconds} ms · {Path.GetFileName(path.TrimEnd('\\', '/'))}");
            Console.WriteLine("     " + r.Message); if (r.Warning is not null) Console.WriteLine("     ADVERTENCIA: " + r.Warning); Console.WriteLine("     motor: " + engine); foreach (string e in r.Evidence) Console.WriteLine("       · " + e);
            rows.Add(new { path = Path.GetFileName(path.TrimEnd('\\', '/')), status = r.Status.ToString(), platform = r.PlatformName, kind = r.Kind.ToString(), r.Game, r.Message, r.Warning, r.Evidence, milliseconds = sw.ElapsedMilliseconds, engine, inputUnchanged = same });
        }
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        if (failed) throw new Exception("FAIL: una entrada cambió durante la detección o un motor rechazó un candidato.");
        Console.WriteLine("Detector probe finished: every input is unchanged.");
    }

    /// <summary>Files: full SHA-256. Folders: relative path, size and last write time of every entry, plus the SHA-256 of the small control files.</summary>
    private static string FastSnapshot(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path); using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (info.Length <= 512L * 1024 * 1024) return Convert.ToHexString(SHA256.HashData(s)) + "|" + info.LastWriteTimeUtc.Ticks;
            // Huge images: size, time and the SHA-256 of the first and last MiB (a full hash would take minutes).
            var edge = new byte[1024 * 1024]; var sha = SHA256.Create(); s.ReadExactly(edge); sha.TransformBlock(edge, 0, edge.Length, null, 0); s.Seek(-edge.Length, SeekOrigin.End); s.ReadExactly(edge); sha.TransformFinalBlock(edge, 0, edge.Length);
            return Convert.ToHexString(sha.Hash!) + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        }
        if (!Directory.Exists(path)) return "missing";
        var sb = new StringBuilder();
        foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(Path.GetRelativePath(path, entry)).Append('|');
            if (File.Exists(entry)) { var f = new FileInfo(entry); sb.Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks); if (f.Length < 2 * 1024 * 1024 && (f.Name.Equals("main.npdm", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("code", StringComparison.OrdinalIgnoreCase))) sb.Append('|').Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))); }
            else sb.Append("dir|").Append(Directory.GetLastWriteTimeUtc(entry).Ticks);
            sb.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
