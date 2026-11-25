using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using static NdsMapRenderChecks;

/// <summary>
/// Gen 5 buildings (read-only graphic layer): the building table of a map block, the AB packs, the area → pack rule of each game, seasons, placement and
/// the translucent pass of the renderer. Synthetic checks, off-screen checks of the season selector, and a read-only probe on a private Black, White or
/// White 2 copy (the copy is opened, never copied or written). No gameplay.
/// </summary>
internal static class NdsGen5BuildingsChecks
{
    private static void P16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);
    private static void P32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);

    /// <summary>A building record as the game stores it: X/Y/Z fx32 little-endian, yaw little-endian, the id big-endian.</summary>
    internal static byte[] Building(int x, int y, int z, int yaw, int id)
    {
        var r = new byte[16]; P32(r, 0, x); P32(r, 4, y); P32(r, 8, z); P16(r, 12, yaw); r[14] = (byte)(id >> 8); r[15] = (byte)id; return r;
    }
    internal static byte[] Table(params byte[][] records) => [.. BitConverter.GetBytes(records.Length), .. records.SelectMany(r => r)];
    /// <summary>A container (map block "WB"/"GC"…, or a building pack "AB"): u16 magic, u16 count, u32 offsets and the end.</summary>
    internal static byte[] Container(string magic, params byte[][] sections)
    {
        int n = sections.Length, at = 4 + 4 * (n + 1); var b = new List<byte> { (byte)magic[0], (byte)magic[1], (byte)n, (byte)(n >> 8) };
        foreach (var s in sections) { b.AddRange(BitConverter.GetBytes(at)); at += s.Length; }
        b.AddRange(BitConverter.GetBytes(at)); foreach (var s in sections) b.AddRange(s); return [.. b];
    }
    internal static byte[] Descriptor(int id) { var d = new byte[36]; P16(d, 0, id); return d; }

    /// <summary>A one-tile square model (16 units) at height <paramref name="y"/>, bound to texture "t" and palette "p".</summary>
    internal static byte[] Square(float y = 0) => Bmd(16, [DisplayList([(0x40, [1u]), TexCoord(0, 0), .. Vtx16((0, y / 16, 0)), TexCoord(8, 0), .. Vtx16((1, y / 16, 0)), TexCoord(8, 8), .. Vtx16((1, y / 16, 1)), TexCoord(0, 8), .. Vtx16((0, y / 16, 1)), (0x41, [])])]);

    internal sealed record Fixture(byte[] Headers, byte[] Blocks, byte[] Textures, Gen5BuildingProfile Profile, byte[] Areas, byte[] ExteriorPacks, byte[] InteriorPacks, byte[] PackTextures)
    {
        internal Gen5BuildingArchives Archives() => new(Profile, Areas, ExteriorPacks, InteriorPacks, PackTextures, PackTextures);
        internal Gen5MapGraphics Graphics(bool withBuildings = true) => new(Headers, Blocks, Textures, withBuildings ? Archives() : null, withBuildings ? null : "sin archivos de prueba");
    }
    internal const int Seasonal = 0, Interior = 1, NoPack = 2;
    /// <summary>
    /// Areas 0–7 with the Black/White rule (1 exterior and 1 interior pack): 2–5 are the season group of exterior pack 0, 6 is interior pack 0, 7 would be
    /// interior pack 1, which does not exist. Header 0 names area 2, header 1 area 6, header 2 area 7. One WB block with a terrain square and four buildings.
    /// </summary>
    internal static Fixture Make(Gen5PackRule rule = Gen5PackRule.SeasonGroups)
    {
        var profile = new Gen5BuildingProfile("TEST", 0, rule, "x", "y", "z", "w", 8, 1, 1, 0, 0, 0, 0, 0);
        byte[] headers = new byte[3 * 48]; P16(headers, 2, 2); P16(headers, 48 + 2, 6); P16(headers, 96 + 2, 7);
        byte[] areas = new byte[8 * 10];
        for (int a = 0; a < 8; a++) { P16(areas, a * 10 + 2, a); areas[a * 10 + 6] = (byte)(a is >= 2 and <= 5 ? 1 : 0); }
        if (rule == Gen5PackRule.AreaTable) P16(areas, 7 * 10, 5);   // the table names a pack that does not exist
        var permissions = new byte[4 + 8192]; P16(permissions, 0, 32); P16(permissions, 2, 32);
        var table = Table(Building(2 << 16, 0, 3 << 16, 0x4000, 0x105), Building(0, 0, 0, 0, 7), Building(0, 0, 0, 0, 9), Building(-(4 << 16), 1 << 12, 0, 0, 0x105));
        byte[] blocks = NdsGen5RandomizerChecks.BuildNarc([Container("WB", Square(), permissions, table)]);
        byte[] one = Square(), other = Square(8);
        byte[] pack = Container("AB", Descriptor(0x105), Descriptor(9), Descriptor(9), Descriptor(0x105), one, one, other, one);
        var red = (ushort)0x001F; var white = (ushort)0x7FFF;
        byte[] btx = Btx([new("t", NitroTextureFormat.Palette4, 8, 8, Enumerable.Repeat((byte)0x55, 16).ToArray(), Repeat: true)], [("p", [red, white, red, white])]);
        byte[] textures = NdsGen5RandomizerChecks.BuildNarc(Enumerable.Range(0, 8).Select(_ => btx).ToArray());
        return new(NdsGen5RandomizerChecks.BuildNarc([headers]), blocks, textures, profile, areas, NdsGen5RandomizerChecks.BuildNarc([pack]), NdsGen5RandomizerChecks.BuildNarc([pack]), NdsGen5RandomizerChecks.BuildNarc([btx]));
    }

    private static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }

    internal static void Run(Action<bool, string> check)
    {
        // ---- profiles (measured on the owner's dumps)
        var black = Gen5BuildingProfile.Black; var white = Gen5BuildingProfile.White; var white2 = Gen5BuildingProfile.White2;
        check(Gen5BuildingProfile.For("IRBO", 0) == black && Gen5BuildingProfile.For("IRAS", 0) == white && Gen5BuildingProfile.For("IRDO", 0) == white2
            && Gen5BuildingProfile.For("IREO", 0) is null && Gen5BuildingProfile.For("IRBO", 1) is null, "Gen5Buildings_OnlyMeasuredGamesHaveAProfile");
        check(white with { GameCode = black.GameCode, ExteriorPacksCrc32 = black.ExteriorPacksCrc32, ExteriorTexturesCrc32 = black.ExteriorTexturesCrc32 } == black
            && white.ExteriorPacksCrc32 != black.ExteriorPacksCrc32, "Gen5Buildings_WhiteSharesBlacksAreasAndInteriors_NotItsExteriors");
        check(black is { Rule: Gen5PackRule.SeasonGroups, ExteriorPacksPath: "a/2/2/9", InteriorPacksPath: "a/2/3/0", ExteriorTexturesPath: "a/1/7/6", InteriorTexturesPath: "a/1/7/7", AreaCount: 282, ExteriorPackCount: 52, InteriorPackCount: 64 }
            && white2 is { Rule: Gen5PackRule.AreaTable, ExteriorPacksPath: "a/2/2/5", InteriorPacksPath: "a/2/2/6", ExteriorTexturesPath: "a/1/7/4", InteriorTexturesPath: "a/1/7/5", AreaCount: 409, ExteriorPackCount: 70, InteriorPackCount: 93 },
            "Gen5Buildings_PathsAndCountsPerGame");
        check(white2.SeasonalAreaEnd == 0x11A && black.SeasonalAreaEnd == 210, "Gen5Buildings_SeasonalAreasEndWhereTheDocumentedLimitIs_0x11AInWhite2");

        // ---- area → pack
        byte[] bwAreas = new byte[282 * 10]; foreach (int a in new[] { 6, 9 }) bwAreas[a * 10 + 6] = 1;
        check(black.Pack(bwAreas, 6) == (1, false) && black.Pack(bwAreas, 9) == (1, false) && black.Pack(bwAreas, 211) == (1, true) && black.Pack(bwAreas, 273) == (63, true),
            "Gen5Buildings_BlackWhite_ExteriorIsTheSeasonGroup_InteriorFollowsTheSeasonalAreas");
        check(black.Pack(bwAreas, 274) is null && black.Pack(bwAreas, 0) is null && black.Pack(bwAreas, 282) is null && black.Pack(new byte[10], 6) is null,
            "Gen5Buildings_AreasWithoutAnExistingPackResolveToNothing");
        byte[] b2Areas = new byte[409 * 10]; P16(b2Areas, 300 * 10, 5); b2Areas[300 * 10 + 6] = 1; P16(b2Areas, 301 * 10, 70); b2Areas[301 * 10 + 6] = 1; P16(b2Areas, 302 * 10, 92);
        check(white2.Pack(b2Areas, 300) == (5, false) && white2.Pack(b2Areas, 301) is null && white2.Pack(b2Areas, 302) == (92, true), "Gen5Buildings_White2_ThePackIsField0OfTheAreaRecord");
        check(black.SeasonalArea(6, 3) == 9 && black.SeasonalArea(7, 1) == 7 && black.SeasonalArea(211, 2) == 211 && white2.SeasonalArea(278, 3) == 281 && white2.SeasonalArea(282, 1) == 282
            && Throws<ArgumentOutOfRangeException>(() => black.SeasonalArea(6, 4)), "Gen5Buildings_SeasonsShiftOnlyTheSpringAreaOfAGroup");

        // ---- building table and packs
        var read = Gen5Building.Read(Table(Building(-(3 << 16) - 0x8000, 0x1800, 5 << 16, 0xC000, 0x01C1)));
        check(read.Single() is { RawX: -0x38000, RawY: 0x1800, RawZ: 0x50000, Yaw: 0xC000, Id: 0x1C1 }, "Gen5Buildings_TheIdIsBigEndian_PositionsAndYawLittleEndian");
        check(Throws<InvalidDataException>(() => Gen5Building.Read([2, 0, 0, 0, .. new byte[16]])) && Gen5Building.Read([0, 0, 0, 0]).Count == 0, "Gen5Buildings_ATableMustHoldExactlyItsRecords");
        var f = Make(); var pack = new Gen5BuildingPack(Narc.Read(f.ExteriorPacks).Files[0]);
        check(pack.Count == 4 && pack.Model(0x105) is { } m && m.AsSpan(0, 4).SequenceEqual("BMD0"u8) && pack.Model(9) is null && pack.IsAmbiguous(9) && !pack.IsAmbiguous(0x105) && pack.Model(7) is null,
            "Gen5Buildings_PackHalves_IdenticalDuplicatesResolve_DifferentOnesAreAmbiguous");
        check(Throws<InvalidDataException>(() => new Gen5BuildingPack(Container("AB", Descriptor(1), Square(), Descriptor(2))))
            && Throws<InvalidDataException>(() => new Gen5BuildingPack(Container("AB", Descriptor(1), Descriptor(2))))
            && Throws<InvalidDataException>(() => new Gen5BuildingPack(Container("XX", Descriptor(1), Square()))), "Gen5Buildings_MalformedPacksAreRefused");
        check(Throws<InvalidDataException>(() => new Gen5BuildingArchives(f.Profile, f.Areas[..^10], f.ExteriorPacks, f.InteriorPacks, f.PackTextures, f.PackTextures))
            && Throws<InvalidDataException>(() => new Gen5BuildingArchives(f.Profile, f.Areas, NdsGen5RandomizerChecks.BuildNarc([]), f.InteriorPacks, f.PackTextures, f.PackTextures)),
            "Gen5Buildings_ArchivesMustMatchTheProfileCounts");

        // ---- scene: placement, textures, what is not drawn
        var g = f.Graphics();
        var scene = g.BlockScene(0, Seasonal, [], out var skipped);
        var expected = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(32, 0, -48);
        check(scene.Count == 3 && scene[0].Transform == Matrix4x4.Identity && scene[1].Transform == expected && scene[2].Transform == Matrix4x4.CreateTranslation(-64, 1, 0),
            "Gen5Buildings_PlacedByYawThenPosition_StoredZIsNegated");
        check(Vector3.Transform(new Vector3(16, 0, 0), scene[1].Transform) is var east && MathF.Abs(east.X - 32) < 1e-3 && MathF.Abs(east.Z + 64) < 1e-3, "Gen5Buildings_AQuarterTurnIsCounterClockwiseSeenFromAbove");
        check(skipped.Count == 2 && skipped.Any(s => s.Contains("id 7") && s.Contains("no está")) && skipped.Any(s => s.Contains("id 9") && s.Contains("ambiguo")), "Gen5Buildings_MissingAndAmbiguousIdsAreReportedNotDrawn");
        var r = new NitroTopDownRenderer(32, 32, -256, -256, 32 / 512f) { BlendTranslucentTexels = true }; r.DrawScene(scene);
        check(r.Untextured == 0 && r.Drawn > 0, "Gen5Buildings_BuildingsUseThePackTextures");
        check(g.BlockScene(0, Interior, [], out var inner).Count == 3 && inner.Count == 2 && g.BuildingPack(Interior) == (0, true), "Gen5Buildings_InteriorAreasUseTheInteriorPack");
        check(g.BlockScene(0, NoPack, [], out var none).Count == 1 && none.Single().Contains("4 estructuras") && none.Single().Contains("área 7"), "Gen5Buildings_AnAreaWithoutPackDrawsTheTerrainAlone");
        var without = f.Graphics(false);
        check(without.BlockScene(0, Seasonal, [], out var old).Count == 1 && old.Single().Contains("4 estructuras") && !without.DrawsBuildings && without.BuildingsUnavailable == "sin archivos de prueba",
            "Gen5Buildings_WithoutArchivesTheTerrainIsDrawnAndTheBuildingsReported");
        var tableRule = Make(Gen5PackRule.AreaTable).Graphics();
        check(tableRule.BuildingPack(Seasonal) == (0, false) && tableRule.BuildingPack(NoPack) is null, "Gen5Buildings_TheTableRuleReadsField0");

        // ---- seasons (preview only)
        check(g.Season == 0 && g.TextureSet(Seasonal) == 2 && g.LoadedArea(Seasonal) == 2, "Gen5Buildings_SpringIsTheAreaTheHeaderNames");
        g.Season = 3;
        check(g.TextureSet(Seasonal) == 5 && g.LoadedArea(Interior) == 6 && g.TextureSet(Interior) == 6 && g.BuildingPack(Seasonal) == (0, false), "Gen5Buildings_WinterLoadsTheLastAreaOfTheGroup_InteriorsDoNotChange");
        check(Throws<ArgumentOutOfRangeException>(() => g.Season = 4) && g.Season == 3, "Gen5Buildings_OnlyFourSeasons");
        g.Season = 0;
        check(without.Season == 0 && without.TextureSet(Seasonal) == 2 && (without.Season = 2) == 2 && without.TextureSet(Seasonal) == 2, "Gen5Buildings_WithoutTheAreaTableSeasonsDoNotMoveTheTerrain");

        // ---- renderer: translucent texels (Gen 5 only)
        ushort green = 0x03E0, grey = 0x4210;
        var opaque = NitroTextureSet.FromBtx(Btx([new("t", NitroTextureFormat.Palette4, 8, 8, new byte[16], Repeat: true)], [("p", [green, green, green, green])]));
        var shadow = NitroTextureSet.FromBtx(Btx([new("t", NitroTextureFormat.A3I5, 8, 8, Enumerable.Repeat((byte)((3 << 5) | 1), 64).ToArray(), Repeat: true)], [("p", Enumerable.Repeat(grey, 32).ToArray())]));
        var floor = NitroModels.Read(Square()).Single(); var above = NitroModels.Read(Square(4)).Single();
        NitroPlacedModel[] layers = [new(above, Matrix4x4.Identity, shadow), new(floor, Matrix4x4.Identity, opaque)];
        var legacy = new NitroTopDownRenderer(4, 4, 0, 0, 0.25f); legacy.DrawScene(layers);
        var sequential = new NitroTopDownRenderer(4, 4, 0, 0, 0.25f); foreach (var l in layers) sequential.Draw(l);
        check(legacy.Pixels.SequenceEqual(sequential.Pixels) && legacy.Drawn == sequential.Drawn, "Gen5Buildings_WithoutTheOptionDrawSceneIsTheOldDraw");
        var blended = new NitroTopDownRenderer(4, 4, 0, 0, 0.25f) { BlendTranslucentTexels = true }; blended.DrawScene(layers);
        uint G = 0xFF000000 | (uint)(31 * 255 / 31) << 8; uint px = blended.Pixels[5];
        check(px != legacy.Pixels[5] && px != G && ((px >> 8) & 0xFF) > ((px >> 16) & 0xFF) && blended.Drawn == legacy.Drawn, "Gen5Buildings_TranslucentTexelsAreMixedOverWhatIsBelow");
    }

    internal static void RunUi(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "nds-gen5-buildings-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var b = NdsGen5BlackChecks.Make(); string source = Path.Combine(temp, "negro.nds"); File.WriteAllBytes(source, b.Rom()); string hash = NdsMapCanvasChecks.Sha(source);
            using var rom = NdsRom.Open(source); var session = new NdsEventSession(rom, b.Read());
            var graphics = Make().Graphics();
            var view = new NdsEventEditorView(session, assets: graphics); NdsMapCanvasChecks.Arrange(view, 1100, 760);
            var select = view.SeasonSelect;
            check(select.Visibility == Visibility.Visible && AutomationProperties.GetName(select) == "Eventos DS · estación de la vista gráfica" && select.Items.Count == 4 && select.SelectedIndex == 0
                && ((string)select.ToolTip).Contains("no modifica la ROM"), "Gen5BuildingsUi_TheSeasonSelectorIsOfferedWithBuildings");
            var first = view.Renders; select.SelectedIndex = 3;
            check(graphics.Season == 3 && view.Renders is not null && !ReferenceEquals(first, view.Renders) && session.Revision == 0 && NdsMapCanvasChecks.Sha(source) == hash,
                "Gen5BuildingsUi_ChangingTheSeasonRebuildsThePreviewOnly");
            var withoutBuildings = new NdsEventEditorView(new NdsEventSession(rom, b.Read()), assets: Make().Graphics(false)); NdsMapCanvasChecks.Arrange(withoutBuildings, 1100, 760);
            var plain = new NdsEventEditorView(new NdsEventSession(rom, b.Read())); NdsMapCanvasChecks.Arrange(plain, 1100, 760);
            check(withoutBuildings.SeasonSelect.Visibility == Visibility.Collapsed && plain.SeasonSelect.Visibility == Visibility.Collapsed, "Gen5BuildingsUi_NoSelectorWithoutBuildingArchives");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }

    /// <summary>
    /// Read-only probe on a private Black, White or White 2 copy: the source is opened in place (never copied or written; its SHA-256 is compared before and
    /// after). Census of every block with buildings, the placement measures (blocked tiles under buildings, warps touching a building), bounded caches, and
    /// comparative PNGs of representative maps (without / with buildings, events on top, the four seasons). The output folder must be new.
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        int failures = 0; void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
        using (var rom = NdsRom.Open(source))
        {
            var profile = Gen5MapProfile.Require(rom, "Edificios");
            var events = Gen5ZoneEvents.Open(rom, Gen5Data.Open(rom).Text(profile.LocationNamesText)); var g = Gen5MapGraphics.Open(rom);
            var blocks = Narc.Read(rom.ReadFile(Gen5MapProfile.BlocksPath));
            Check(g.DrawsBuildings, $"{profile.Key}: archivos de edificios acreditados ({g.BuildingsUnavailable ?? "sí"})");
            int buildings = 0, drawn = 0, untextured = 0; long footprint = 0, blocked = 0, tiles = 0, blockedAll = 0; int warps = 0, doors = 0; var seen = new HashSet<(int, int)>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var map in events.Maps)
            {
                var layout = events.Layout(map.Id);
                foreach (var cell in layout.Cells)
                {
                    if (cell.LandDataId == PlatinumTerrain.EmptyLandId || cell.LandDataId >= g.LandCount || layout.HasStoredHeaders && cell.HeaderId != map.Id) continue;
                    var list = g.BlockBuildings(cell.LandDataId); if (list.Count == 0) continue;
                    var scene = g.BlockScene(cell.LandDataId, map.Id, [], out var skip); int terrainModels = g.LandModel(cell.LandDataId).Count;
                    if (!seen.Add((cell.LandDataId, map.Id))) continue;
                    buildings += list.Count; drawn += skip.Any(s => s.Contains("no se dibujan")) ? 0 : list.Count - skip.Count;
                    var only = new NitroTopDownRenderer(128, 128, -256, -256, 4 / 16f, 0); only.DrawScene(scene.Skip(terrainModels)); untextured += only.Untextured;
                    var perm = Gen5MapGraphics.Permissions(blocks.Files[cell.LandDataId]); if (perm is null) continue;
                    var mask = new bool[32, 32];
                    for (int z = 0; z < 32; z++)
                        for (int x = 0; x < 32; x++)
                        {
                            int c = 0; for (int py = 0; py < 4; py++) for (int pxx = 0; pxx < 4; pxx++) if (only.Pixels[(z * 4 + py) * 128 + x * 4 + pxx] != 0) c++;
                            bool col = Gen5MapGraphics.Collides(perm, x, z); tiles++; if (col) blockedAll++;
                            if (c >= 10) { mask[x, z] = true; footprint++; if (col) blocked++; }
                        }
                    foreach (var w in events.Events(map.Id).Where(e => e.Kind == PlatinumEventKind.Warp && e.EditLock is null && e.X / 32 == cell.BlockX && e.Y / 32 == cell.BlockY))
                    {
                        int x = w.X % 32, z = w.Y % 32; warps++;
                        if (mask[x, z] || z > 0 && mask[x, z - 1] || x > 0 && mask[x - 1, z] || x < 31 && mask[x + 1, z]) doors++;
                    }
                }
            }
            Console.WriteLine($"INFO censo: {buildings} edificios en bloques de mapas, {drawn} dibujados, {buildings - drawn} omitidos con motivo; {untextured} triángulos de edificio sin textura; {clock.ElapsedMilliseconds} ms");
            Console.WriteLine($"INFO colocación: {(double)blocked / footprint:P1} de las casillas bajo un edificio están bloqueadas (todas las casillas: {(double)blockedAll / tiles:P1}); {doors}/{warps} warps tocan un edificio");
            Check(buildings > 0 && buildings - drawn <= buildings / 200, $"{profile.Key}: se dibujan los edificios salvo los ids que su paquete no contiene ({buildings - drawn} de {buildings})");
            Check(untextured == 0, $"{profile.Key}: ningún triángulo de edificio sin textura");
            Check((double)blocked / footprint > (double)blockedAll / tiles + 0.10, $"{profile.Key}: los edificios caen sobre las casillas bloqueadas (claramente por encima del azar)");
            Check(doors * 4 >= warps, $"{profile.Key}: al menos un cuarto de los warps de puerta tocan un edificio");
            Check(g.CachedBuildings.Packs <= Gen5MapGraphics.PackCacheLimit && g.CachedBuildings.Models <= Gen5MapGraphics.ModelCacheLimit, $"{profile.Key}: cachés acotadas {g.CachedBuildings}");

            // Comparative images: towns, cities, routes with trees, an interior, seasons.
            string[] wanted = profile.GameCode switch
            {
                "IRDO" => ["Aspertia City", "Floccesy Town", "Virbank City", "Castelia City", "Route 19", "Aspertia City|int"],
                "IRAS" => ["Pueblo Arcilla", "Pueblo Terracota", "Ciudad Gres", "Ciudad Mayólica", "Bosque Azulejo", "Pueblo Arcilla|int"],
                _ => ["Nuvema Town", "Accumula Town", "Striaton City", "Nimbasa City", "Pinwheel Forest", "Route 9", "Nuvema Town|int"],
            };
            foreach (var w in wanted)
            {
                bool interior = w.EndsWith("|int"); string name = w.Split('|')[0];
                var map = events.Maps.Where(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && g.BuildingPack(m.Id) is { } p && p.Interior == interior)
                    .OrderByDescending(m => Cells(g, events, m).Sum(c => g.BlockBuildings(c.LandDataId).Count)).FirstOrDefault();
                if (map is null) { Console.WriteLine($"INFO sin mapa {w}"); continue; }
                string file = Path.Combine(output, $"{profile.GameCode}-{map.Id:D3}-{(interior ? "interior" : "exterior")}");
                var (W, H, plain, _, _) = Render(g, events, map, false); var (_, _, full, overlay, notes) = Render(g, events, map, true);
                var pair = new uint[(W * 2 + 8) * H]; Array.Fill(pair, 0xFFFFFFFF); for (int y = 0; y < H; y++) { Array.Copy(plain, y * W, pair, y * (W * 2 + 8), W); Array.Copy(full, y * W, pair, y * (W * 2 + 8) + W + 8, W); }
                WritePng(file + "-sin-y-con-edificios.png", W * 2 + 8, H, pair); WritePng(file + "-eventos.png", W, H, overlay);
                Console.WriteLine($"INFO {map.Id} {map.Name}: área {g.Area(map.Id)}, paquete {g.BuildingPack(map.Id)}, {notes.Count} avisos{(notes.Count > 0 ? ": " + notes[0] : "")}");
                if (!interior && g.LoadedArea(map.Id) is var spring && Gen5BuildingProfile.For(profile.GameCode, profile.Revision)!.IsSeasonal(g.Area(map.Id)))
                {
                    var grid = new uint[(W * 2 + 8) * (H * 2 + 8)]; Array.Fill(grid, 0xFFFFFFFF);
                    for (int s = 0; s < 4; s++) { g.Season = s; var px = Render(g, events, map, true).Pixels; for (int y = 0; y < H; y++) Array.Copy(px, y * W, grid, ((s / 2) * (H + 8) + y) * (W * 2 + 8) + (s % 2) * (W + 8), W); }
                    g.Season = 0; WritePng(file + "-estaciones.png", W * 2 + 8, H * 2 + 8, grid);
                }
            }
        }
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == hash, "La ROM de origen no cambia (SHA-256 idéntico)");
        Console.WriteLine(failures == 0 ? "Gen5 buildings probe: all checks passed." : $"Gen5 buildings probe: {failures} FAILED.");
        if (failures > 0) Environment.ExitCode = 1;
    }

    private static List<PlatinumMapCell> Cells(Gen5MapGraphics g, Gen5ZoneEvents e, PlatinumMap m)
    {
        var l = e.Layout(m.Id);
        return l.Cells.Where(c => c.LandDataId != PlatinumTerrain.EmptyLandId && c.LandDataId < g.LandCount && (!l.HasStoredHeaders || c.HeaderId == m.Id)).ToList();
    }
    private static (int W, int H, uint[] Pixels, uint[] Overlay, List<string> Notes) Render(Gen5MapGraphics g, Gen5ZoneEvents e, PlatinumMap m, bool buildings)
    {
        var cells = Cells(g, e, m); int bx0 = cells.Min(c => c.BlockX), by0 = cells.Min(c => c.BlockY), bw = cells.Max(c => c.BlockX) - bx0 + 1, bh = cells.Max(c => c.BlockY) - by0 + 1;
        int ppt = bw * bh > 6 ? 4 : 8, block = 32 * ppt, W = bw * block, H = bh * block; var full = new uint[W * H]; Array.Fill(full, 0xFF202830u); var notes = new List<string>();
        foreach (var cell in cells)
        {
            var scene = g.BlockScene(cell.LandDataId, m.Id, [], out var skip); notes.AddRange(skip); int terrain = g.LandModel(cell.LandDataId).Count;
            var r = new NitroTopDownRenderer(block, block, -256, -256, ppt / 16f, 0xFF202830) { BlendTranslucentTexels = true }; r.DrawScene(buildings ? scene : scene.Take(terrain));
            int ox = (cell.BlockX - bx0) * block, oy = (cell.BlockY - by0) * block; for (int y = 0; y < block; y++) Array.Copy(r.Pixels, y * block, full, (oy + y) * W + ox, block);
        }
        var overlay = (uint[])full.Clone();
        void Box(int tx, int tz, uint colour, bool filled)
        {
            int px0 = (tx - bx0 * 32) * ppt, pz0 = (tz - by0 * 32) * ppt;
            for (int y = 0; y < ppt; y++) for (int x = 0; x < ppt; x++)
            {
                int X = px0 + x, Y = pz0 + y; if (X < 0 || Y < 0 || X >= W || Y >= H) continue;
                if (filled ? x > 0 && y > 0 && x < ppt - 1 && y < ppt - 1 : x == 0 || y == 0 || x == ppt - 1 || y == ppt - 1) overlay[Y * W + X] = colour;
            }
        }
        foreach (var ev in e.Events(m.Id))
            switch (ev.Kind)
            {
                case PlatinumEventKind.Npc: Box(ev.X, ev.Y, 0xFFF4C95D, true); break;
                case PlatinumEventKind.Warp: Box(ev.X, ev.Y, 0xFFFF5A2A, true); break;
                case PlatinumEventKind.Object: Box(ev.X, ev.Y, 0xFFC3A6F2, true); break;
                default: for (int dx = 0; dx < Math.Max(1, ev.RangeX); dx++) for (int dz = 0; dz < Math.Max(1, ev.RangeY); dz++) Box(ev.X + dx, ev.Y + dz, 0xFF6CD2E8, false); break;
            }
        return (W, H, full, overlay, notes);
    }
}
