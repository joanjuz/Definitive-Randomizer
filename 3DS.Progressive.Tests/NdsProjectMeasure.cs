using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Real, read-only measurement for the persistent DS project format (PR 1, decision A). For each private ROM it applies representative operations to an
/// in-memory <see cref="NdsProject"/> (the ROM is only read) and measures, per operation and cumulatively: the size of the changed tables (whole files and
/// changed NARC members), their compressed size, and the time to compress, hash and make durable (write + flush + re-read + replace) a FULL snapshot
/// against an incremental record. Output goes to a new folder; temporary files live in it.
/// </summary>
internal static class NdsProjectMeasure
{
    private sealed record Row(string Rom, string Game, string Operation, int Files, long RawFiles, long RawMembers, long BrotliFast, long BrotliMembers, long DeflateFast, double CompressMs);
    private sealed record Total(string Rom, string Game, string Stage, int Files, long Raw, long Brotli, double SnapshotMs, double AppendMs, double ReadVerifyMs);

    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static byte[] Brotli(byte[] data, CompressionLevel level) { using var ms = new MemoryStream(); using (var b = new BrotliStream(ms, level, true)) b.Write(data); return ms.ToArray(); }
    private static byte[] Deflate(byte[] data) { using var ms = new MemoryStream(); using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true)) d.Write(data); return ms.ToArray(); }
    /// <summary>KiB, except below 1 KiB, where the whole-number division would print a misleading 0: those are given in bytes.</summary>
    private static string Size(long bytes) => bytes is > 0 and < 1024 ? bytes + " B" : bytes / 1024 + " KiB";
    private static double Median(Func<double> run, int n = 5) { var times = Enumerable.Range(0, n).Select(_ => run()).Order().ToArray(); return times[n / 2]; }

    /// <summary>Changed state of a project: file path → bytes (files, then overlays as "overlay:N", then the ARM9).</summary>
    private static Dictionary<string, byte[]> State(NdsProject project)
    {
        var state = new Dictionary<string, byte[]>(Field<Dictionary<string, byte[]>>(project, "_files"), StringComparer.Ordinal);
        foreach (var (id, data) in Field<Dictionary<int, byte[]>>(project, "_overlays")) state["overlay:" + id] = data;
        if (Field<byte[]?>(project, "_arm9") is { } arm9) state["arm9"] = arm9;
        return state;
    }

    /// <summary>Bytes of the members of a NARC that differ from the original (member index + length + bytes), or the whole file when it is not a NARC.</summary>
    private static byte[] MemberDelta(byte[] original, byte[] current)
    {
        try
        {
            var a = Narc.Read(original).Files; var b = Narc.Read(current).Files;
            if (a.Count != b.Count) return current;
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            for (int i = 0; i < a.Count; i++) if (!a[i].AsSpan().SequenceEqual(b[i])) { w.Write(i); w.Write(b[i].Length); w.Write(b[i]); }
            return ms.ToArray();
        }
        catch (InvalidDataException) { return current; }
    }

    private static byte[] Original(NdsProject project, string path)
    {
        var rom = Field<NdsRom>(project, "_rom");
        if (path == "arm9") return []; if (path.StartsWith("overlay:", StringComparison.Ordinal)) return [];
        return rom.ReadFile(path);
    }

    internal static void Run(string output, IReadOnlyList<string> roms)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string io = Path.Combine(output, "io"); Directory.CreateDirectory(io);
        var rows = new List<Row>(); var totals = new List<Total>();
        foreach (string rom in roms)
        {
            using var project = NdsProject.Open(rom, false);
            string name = Path.GetFileNameWithoutExtension(rom); Console.WriteLine($"== {project.Game} ({new FileInfo(rom).Length / 1048576} MiB)");
            var scenarios = new List<(string Name, Func<NdsPreview> Make)>
            {
                ("Edición manual · un entrenador", () => { var cell = project.ReadTrainerFields().First(c => c.Max > c.Value && c.Kind == NdsValueKind.Number); return project.PreviewTrainerEdits(new Dictionary<string, int> { [cell.Key] = cell.Value + 1 }); }),
                ("Edición manual · una especie", () => { var cell = project.Read(NdsSection.Pokemon).First(c => c.Max > c.Value && c.Kind == NdsValueKind.Number); return project.PreviewEdits(NdsSection.Pokemon, new Dictionary<string, int> { [cell.Key] = cell.Value + 1 }); }),
                ("Randomizar entrenadores y salvajes", () => project.PreviewRandom("Especies", new Gen5RandomizerOptions(7) { Trainers = Gen5SpeciesMode.SimilarBst, Wild = Gen5SpeciesMode.SimilarBst })),
                ("Entrenadores (equipos y reglas)", () => project.PreviewTrainers("Entrenadores", new Gen5RandomizerOptions(8) { Trainers = Gen5SpeciesMode.Random }, new NdsTrainerOptions(8) { MinTeamSize = 3 })),
                ("Datos · estadísticas, tipos y habilidades", () => project.PreviewData("Datos", NdsSection.Pokemon, new NdsDataOptions(9) { Stats = NdsStatsMode.Random, Types = true, Abilities = true })),
                ("Ajustes del juego", () => project.PreviewTweaks(new NdsTweakOptions { UpdateTypeEffectiveness = true, GuaranteedCatch = true })),
            };
            foreach (var (label, make) in scenarios)
            {
                NdsPreview preview;
                try { preview = make(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or NotSupportedException) { Console.WriteLine($"   - {label}: no aplicable ({ex.Message.Split('\n')[0]})"); continue; }
                var touched = preview.Result.Files.Keys.Concat(preview.Result.Overlays.Keys.Select(i => "overlay:" + i)).Concat(preview.Result.Arm9 is null ? [] : ["arm9"]).ToArray();
                var before = State(project);
                project.Apply(preview);
                var after = State(project);
                long raw = 0, members = 0, bf = 0, bm = 0, df = 0; var clock = Stopwatch.StartNew();
                foreach (string path in touched)
                {
                    byte[] now = after[path]; byte[] was = before.TryGetValue(path, out var b0) ? b0 : Original(project, path);
                    raw += now.Length; byte[] delta = was.Length == 0 ? now : MemberDelta(was, now); members += delta.Length;
                    bf += Brotli(now, CompressionLevel.Fastest).Length; bm += Brotli(delta, CompressionLevel.Fastest).Length; df += Deflate(now).Length;
                }
                clock.Stop();
                rows.Add(new(name, project.Game, label, touched.Length, raw, members, bf, bm, df, clock.Elapsed.TotalMilliseconds));
                Console.WriteLine($"   + {label}: {touched.Length} archivos · {Size(raw)} · miembros {Size(members)} · brotli {Size(bf)} (miembros {Size(bm)}) · deflate {Size(df)} · {clock.Elapsed.TotalMilliseconds:0} ms");
            }
            // The real container: how much of a project is its history (operations with their change lists) and how much its tables.
            void Container(string stage)
            {
                var st = State(project); var files = st.Where(p => p.Key != "arm9" && !p.Key.StartsWith("overlay:", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                var meta = new ThreeDS.Progressive.Services.Nds.Project.NdsProjectMeta { Writer = "medida", ProjectId = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow, SavedUtc = DateTime.UtcNow, Generation = 1,
                    Rom = new(new string('A', 64), new string('B', 40), project.Identity.Profile.GameCode, 0, project.Game, 1, false, rom),
                    Operations = project.History.Select(ThreeDS.Progressive.Services.Nds.Project.NdsProjectOperationRecord.Of).ToList() };
                var document = new ThreeDS.Progressive.Services.Nds.Project.NdsProjectDocument(meta, new ThreeDS.Progressive.Services.Nds.Project.NdsProjectState { Files = files, Arm9 = st.GetValueOrDefault("arm9") });
                var sw = Stopwatch.StartNew(); byte[] bytes = ThreeDS.Progressive.Services.Nds.Project.NdsProjectCodec.Serialize(document); double write = sw.Elapsed.TotalMilliseconds;
                sw.Restart(); bool ok = ThreeDS.Progressive.Services.Nds.Project.NdsProjectCodec.Read(bytes).Ok; double read = sw.Elapsed.TotalMilliseconds; int manifest = BitConverter.ToInt32(bytes, 28);
                Console.WriteLine($"   # contenedor ({stage}): {project.History.Count} operaciones · {project.History.Sum(o => o.Changes.Count)} cambios · manifiesto {Size(manifest)} (brotli {Size(Brotli(bytes.AsSpan(64, manifest).ToArray(), CompressionLevel.Fastest).Length)}) · archivo {Size(bytes.Length)} · serializar {write:0} ms · leer {read:0} ms · {(ok ? "válido" : "NO VÁLIDO")}");
            }
            Container("seis operaciones");
            for (int extra = 0; extra < 30; extra++)
                project.Apply(project.PreviewRandom("Especies " + extra, new Gen5RandomizerOptions((uint)(100 + extra)) { Trainers = Gen5SpeciesMode.SimilarBst, Wild = Gen5SpeciesMode.SimilarBst }));
            Container("más 30 randomizaciones");
            // Full snapshot of the cumulative state vs. one incremental record, durable on this disk.
            var state = State(project); long rawAll = state.Values.Sum(v => (long)v.Length);
            var compressedAll = new Dictionary<string, byte[]>(); var swCompress = Stopwatch.StartNew();
            foreach (var (path, data) in state) compressedAll[path] = Brotli(data, CompressionLevel.Fastest);
            swCompress.Stop(); long brotliAll = compressedAll.Values.Sum(v => (long)v.Length);
            byte[] snapshot = compressedAll.SelectMany(p => Encoding.UTF8.GetBytes(p.Key).Concat(p.Value)).ToArray();
            string target = Path.Combine(io, name + ".snapshot");
            double Snapshot()
            {
                var sw = Stopwatch.StartNew(); string temp = target + ".tmp";
                using (var output2 = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { output2.Write(snapshot); output2.Flush(true); }
                if (!SHA256.HashData(File.ReadAllBytes(temp)).SequenceEqual(SHA256.HashData(snapshot))) throw new IOException("relectura distinta");
                if (File.Exists(target)) File.Replace(temp, target, target + ".bak"); else File.Move(temp, target);
                return sw.Elapsed.TotalMilliseconds;
            }
            double snapshotMs = Median(Snapshot) + swCompress.Elapsed.TotalMilliseconds;
            var last = rows.LastOrDefault(r => r.Rom == name);
            byte[] record = new byte[Math.Max(1, (int)(last?.BrotliMembers ?? 4096))]; Random.Shared.NextBytes(record);
            string journal = Path.Combine(io, name + ".journal");
            double Append()
            {
                var sw = Stopwatch.StartNew();
                using var f = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.Read); f.Write(record); f.Write(SHA256.HashData(record)); f.Flush(true);
                return sw.Elapsed.TotalMilliseconds;
            }
            double appendMs = Median(Append);
            double readMs = Median(() => { var sw = Stopwatch.StartNew(); var bytes = File.ReadAllBytes(target); _ = SHA256.HashData(bytes); foreach (var c in compressedAll.Values) { using var br = new BrotliStream(new MemoryStream(c), CompressionMode.Decompress); br.CopyTo(Stream.Null); } return sw.Elapsed.TotalMilliseconds; });
            totals.Add(new(name, project.Game, "acumulado", state.Count, rawAll, brotliAll, snapshotMs, appendMs, readMs));
            Console.WriteLine($"   = estado acumulado: {state.Count} archivos · {rawAll / 1024} KiB · brotli {brotliAll / 1024} KiB · instantánea completa {snapshotMs:0} ms · registro incremental {appendMs:0.0} ms · lectura+verificación {readMs:0} ms");
        }
        File.WriteAllText(Path.Combine(output, "medidas.json"), JsonSerializer.Serialize(new { rows, totals }, new JsonSerializerOptions { WriteIndented = true }));
        var md = new StringBuilder("| ROM | Operación | Archivos | Bruto | Miembros | Brotli | Brotli miembros | Deflate | Comprimir (ms) |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var r in rows) md.AppendLine($"| {r.Game} | {r.Operation} | {r.Files} | {Size(r.RawFiles)} | {Size(r.RawMembers)} | {Size(r.BrotliFast)} | {Size(r.BrotliMembers)} | {Size(r.DeflateFast)} | {r.CompressMs:0} |");
        md.AppendLine("\n| ROM | Archivos | Bruto (KiB) | Brotli (KiB) | Instantánea completa (ms) | Registro incremental (ms) | Lectura+verificación (ms) |\n|---|---|---|---|---|---|---|");
        foreach (var t in totals) md.AppendLine($"| {t.Game} | {t.Files} | {t.Raw / 1024} | {t.Brotli / 1024} | {t.SnapshotMs:0} | {t.AppendMs:0.0} | {t.ReadVerifyMs:0} |");
        File.WriteAllText(Path.Combine(output, "medidas.md"), md.ToString());
        Directory.Delete(io, true);
    }
}
