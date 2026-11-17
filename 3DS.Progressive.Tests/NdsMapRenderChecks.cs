using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using pk3DS.Core.NDS;
using pk3DS.Core.NDS.Nitro;

/// <summary>Map graphics readers (BTX0/BMD0) and the Platinum resource resolver. Synthetic checks build minimal Nitro files; the probe reads a private copy.</summary>
internal static partial class NdsMapRenderChecks
{
    /// <summary>
    /// Real CPUE/1 copy, read-only: every terrain texture set and prop texture set decodes; every land_data terrain model and every build_model parses; every
    /// terrain material of the blocks a header uses finds its texture and palette in that header's area. Writes statistics and an atlas PNG (private).
    /// </summary>
    internal static void Probe(string output, string source)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        using (var rom = NdsRom.Open(source))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew(); var assets = PlatinumMapAssets.Open(rom);
            Console.WriteLine($"PASS MapRenderReal_ArchivesPinned ({clock.ElapsedMilliseconds} ms)");
            var formats = new Dictionary<NitroTextureFormat, int>(); int decoded = 0, unpaired = 0; var failures = new List<string>();
            void DecodeAll(NitroTextureSet set, string where)
            {
                foreach (var t in set.Textures)
                {
                    formats[t.Format] = formats.GetValueOrDefault(t.Format) + 1;
                    var palette = t.Format == NitroTextureFormat.Direct ? null : set.Palette(t.Name) ?? set.Palette(t.Name + "_pl");
                    if (palette is null && t.Format != NitroTextureFormat.Direct) { unpaired++; continue; }
                    try { var image = set.Decode(t, palette); if (image.Pixels.Length != t.Width * t.Height) failures.Add($"{where}/{t.Name}: tamaño"); decoded++; }
                    catch (InvalidDataException ex) { failures.Add($"{where}/{t.Name}: {ex.Message}"); }
                }
            }
            clock.Restart();
            for (int i = 0; i < assets.MapTextureCount; i++) DecodeAll(assets.MapTextures(i), "map_tex_set " + i);
            for (int i = 0; i < assets.PropTextureCount; i++) DecodeAll(assets.PropTextures(i), "areabm_texset " + i);
            Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")} MapRenderReal_TexturesDecode: {decoded} decoded, {unpaired} without a same-name palette (bound by materials), formats {string.Join(", ", formats.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))} ({clock.ElapsedMilliseconds} ms)");
            foreach (var f in failures.Take(10)) Console.WriteLine("  " + f);

            clock.Restart(); int landTriangles = 0; var landFailures = new List<string>();
            for (int i = 0; i < assets.LandCount; i++)
                try { landTriangles += assets.LandModel(i).Sum(m => m.Triangles.Count); }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { landFailures.Add($"land {i}: {ex.Message}"); }
            Console.WriteLine($"{(landFailures.Count == 0 ? "PASS" : "FAIL")} MapRenderReal_TerrainModelsParse: {assets.LandCount - landFailures.Count}/{assets.LandCount}, {landTriangles} triangles ({clock.ElapsedMilliseconds} ms)");
            foreach (var f in landFailures.Take(10)) Console.WriteLine("  " + f);
            clock.Restart(); int propTriangles = 0; var propFailures = new List<string>();
            for (int i = 0; i < assets.BuildModelCount; i++)
                try { propTriangles += assets.PropModel(i).Sum(m => m.Triangles.Count); }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { propFailures.Add($"build {i}: {ex.Message}"); }
            Console.WriteLine($"{(propFailures.Count == 0 ? "PASS" : "FAIL")} MapRenderReal_PropModelsParse: {assets.BuildModelCount - propFailures.Count}/{assets.BuildModelCount}, {propTriangles} triangles ({clock.ElapsedMilliseconds} ms)");
            foreach (var f in propFailures.Take(10)) Console.WriteLine("  " + f);

            // Twinleaf (header 411, matrix 0 cell 3,27 → land_data 0): materials bound to the area's textures, model extent close to one block.
            var area = assets.Area(411); var twin = assets.LandModel(0).Single(); var textures = assets.MapTextures(area.MapTextureArchive);
            var missing = twin.Materials.Where(m => m.Texture is not null && (textures.Texture(m.Texture) is null || textures.Texture(m.Texture)!.Format != NitroTextureFormat.Direct && (m.Palette is null || textures.Palette(m.Palette) is null))).Select(m => m.Name).ToList();
            var (min, max) = twin.Bounds();
            Console.WriteLine($"{(missing.Count == 0 ? "PASS" : "FAIL")} MapRenderReal_TwinleafMaterialsBind: area {area.Area}, textures {area.MapTextureArchive}, {twin.Materials.Count} materials, {twin.Triangles.Count} triangles, {missing.Count} unbound {string.Join(" ", missing.Take(8))}");
            Console.WriteLine($"INFO Twinleaf terrain extent X {min.X:0.##}..{max.X:0.##} Y {min.Y:0.##}..{max.Y:0.##} Z {min.Z:0.##}..{max.Z:0.##} (one block = 32 tiles)");
            Console.WriteLine("INFO Twinleaf materials: " + string.Join("; ", twin.Materials.Take(12).Select(m => $"{m.Name}→{m.Texture}/{m.Palette} a{m.Alpha} {m.Width}x{m.Height} s{m.ScaleS:0.##}")));
            WriteAtlas(Path.Combine(output, "twinleaf-textures.png"), twin.Materials.Where(m => m.Texture is not null).Select(m => textures.Texture(m.Texture!)).OfType<NitroTextureInfo>().Distinct().Select(t => textures.Decode(t, textures.Palette(twin.Materials.First(m => m.Texture == t.Name).Palette ?? t.Name))).ToList());
            Console.WriteLine("INFO atlas: " + Path.Combine(output, "twinleaf-textures.png"));
            // Top-down render of the Twinleaf block (8 px per tile) with its props, and the same without props.
            var props = PlatinumLandProps.Open(rom).Props(0);
            foreach (bool withProps in new[] { true, false })
            {
                clock.Restart();
                var scene = assets.BlockScene(0, 411, withProps ? props : [], out var skipped);
                var renderer = new NitroTopDownRenderer(256, 256, -256, -256, 0.5f, 0xFF202830);
                foreach (var placed in scene) renderer.Draw(placed);
                string file = Path.Combine(output, withProps ? "twinleaf-top.png" : "twinleaf-terrain.png"); WritePng(file, 256, 256, renderer.Pixels);
                int covered = renderer.Pixels.Count(p => p != 0xFF202830);
                Console.WriteLine($"INFO {Path.GetFileName(file)}: {scene.Count} models, {renderer.Drawn} triangles, {renderer.Untextured} untextured, {covered * 100 / (256 * 256)} % covered, skipped {skipped.Count}, {clock.ElapsedMilliseconds} ms");
            }
            if (failures.Count + landFailures.Count + propFailures.Count + missing.Count > 0) throw new Exception("Map render probe failed");
        }
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) != hash) throw new Exception("Source changed");
        Console.WriteLine("PASS MapRenderReal_SourceUnchanged");
    }

    internal static void WriteAtlas(string path, IReadOnlyList<NitroImage> images)
    {
        int width = Math.Max(1, images.Sum(i => i.Width + 2)), height = Math.Max(1, images.Count == 0 ? 1 : images.Max(i => i.Height));
        var pixels = new uint[width * height]; int x = 0;
        foreach (var image in images) { for (int y = 0; y < image.Height; y++) Array.Copy(image.Pixels, y * image.Width, pixels, y * width + x, image.Width); x += image.Width + 2; }
        WritePng(path, width, height, pixels);
    }
    internal static void WritePng(string path, int width, int height, uint[] argb)
    {
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, argb.SelectMany(BitConverter.GetBytes).ToArray(), width * 4);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
