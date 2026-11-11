using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Nds.Project;

/// <summary>
/// Cost of saving a persistent DS project as its history grows (read-only probe over private dumps). Applies full randomizations one after another and, at
/// fixed points, measures the confirmation of a manual edit (the interactive case), the parts of a save (serialize, verify, read) and reopening the project.
/// The ROMs are only read; the projects are written under a new output folder.
/// </summary>
internal static class NdsProjectSaveBench
{
    private static readonly int[] Points = [0, 1, 5, 10, 20, 30, 40, 60, 80, 100];

    private static NdsPreview Randomization(NdsProject project, int r) => r % 2 == 1
        ? project.PreviewRandom("Especies " + r, new Gen5RandomizerOptions((uint)(5000 + r)) { Trainers = Gen5SpeciesMode.Random, Wild = Gen5SpeciesMode.Random })
        : project.PreviewData("Datos " + r, NdsSection.Pokemon, new NdsDataOptions((uint)(6000 + r)) { Stats = NdsStatsMode.Random, Types = true, Abilities = true });

    private static NdsPreview ManualEdit(NdsProject project, int i)
    {
        var cells = project.Read(NdsSection.Pokemon).Where(c => c.Kind == NdsValueKind.Number && c.Max > c.Min).ToList(); var cell = cells[i * 13 % cells.Count];
        return project.PreviewEdits(NdsSection.Pokemon, new Dictionary<string, int> { [cell.Key] = cell.Value < cell.Max ? cell.Value + 1 : cell.Value - 1 });
    }

    private static double Median(List<double> values) { values.Sort(); return values[values.Count / 2]; }
    private static double Time(Action action) { var sw = Stopwatch.StartNew(); action(); return sw.Elapsed.TotalMilliseconds; }

    internal static void Run(string output, int randomizations, IReadOnlyList<string> roms)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        Console.WriteLine("| ROM | Randomizaciones | Operaciones | Cambios | Archivo (KiB) | Historial bruto (KiB) | Tablas de historial | Guardar edición manual (ms, mediana de 5) | Serializar (ms) | Verificar (ms) | Leer (ms) | Reabrir (ms) | Primer guardado tras reabrir (ms) |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (string rom in roms)
        {
            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rom))), name = Path.GetFileNameWithoutExtension(rom);
            string folder = Directory.CreateDirectory(Path.Combine(output, new string(name.Where(char.IsLetterOrDigit).Take(24).ToArray()))).FullName, projectPath = Path.Combine(folder, "proyecto.nds.drproj");
            var live = NdsProjectTransactionChecks.OpenPersistent(rom, projectPath, false); int edit = 0;
            try
            {
                for (int r = 0; r <= randomizations; r++)
                {
                    if (r > 0) live.Apply(Randomization(live, r));
                    if (!Points.Contains(r) && r != randomizations) continue;
                    var saves = new List<double>();
                    for (int k = 0; k < 5; k++) { var preview = ManualEdit(live, ++edit); saves.Add(Time(() => live.Apply(preview))); }
                    var document = live.SavedDocument!; byte[] bytes = NdsProjectCodec.Serialize(document);
                    double serialize = Median(Enumerable.Range(0, 3).Select(_ => Time(() => NdsProjectCodec.Serialize(document))).ToList());
                    double verify = Median(Enumerable.Range(0, 3).Select(_ => Time(() => NdsProjectCodec.Verify(bytes))).ToList());
                    double read = Median(Enumerable.Range(0, 3).Select(_ => Time(() => NdsProjectCodec.Read(bytes))).ToList());
                    var blobs = NdsProjectCodec.Verify(bytes).Document!.Meta.Blobs!.Where(b => b.Kind == NdsProjectBlobKind.History).ToList();
                    string reopen = "", firstSave = "";
                    if (r is 20 or 40 or 100 || r == randomizations)
                    {
                        double open = Time(() => { live.Dispose(); live = NdsProjectTransactionChecks.OpenPersistent(rom, projectPath, false); });
                        var preview = ManualEdit(live, ++edit); double first = Time(() => live.Apply(preview));
                        reopen = open.ToString("0"); firstSave = first.ToString("0");
                    }
                    Console.WriteLine($"| {live.Game} | {r} | {live.History.Count} | {live.History.Sum(o => o.Changes.Count)} | {new FileInfo(projectPath).Length / 1024} | {blobs.Sum(b => b.RawLength) / 1024} | {blobs.Count} | {Median(saves):0} | {serialize:0} | {verify:0} | {read:0} | {reopen} | {firstSave} |");
                }
            }
            finally { live.Dispose(); }
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rom))) != before || Directory.GetFiles(Path.GetDirectoryName(rom)!, "*.drproj*").Length > 0) throw new Exception("FAIL la ROM original cambió o apareció un proyecto junto a ella");
        }
        Console.WriteLine("Completed DS project save benchmark (private dumps, read-only sources).");
    }
}
