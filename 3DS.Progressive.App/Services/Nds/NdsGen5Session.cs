using System.IO;
using System.Text;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// One opened Black/White ROM for the DS randomizer window. Heavy work runs off the UI thread; callers must not overlap operations.
/// A preview is tied to the options that produced it, so a stale preview is never exported.
/// </summary>
internal sealed class NdsGen5Session : IDisposable
{
    private readonly NdsRom _rom;
    private readonly IReadOnlyList<string> _names;

    internal Gen5Data Data { get; }
    internal string SourcePath => _rom.Path;
    internal string Game => Data.Identity.Profile.Game;
    internal bool KnownDump => Data.Identity.KnownDump;
    internal int TrainerCount { get; }
    internal int AreaCount { get; }
    internal IReadOnlyList<string> SpeciesNames => _names;
    internal IReadOnlyList<int> OriginalStarters { get; }

    private NdsGen5Session(NdsRom rom, Gen5Data data)
    {
        _rom = rom;
        Data = data;
        _names = data.SpeciesNames();
        TrainerCount = data.Trainers().Count;
        AreaCount = data.Encounters().Count;
        OriginalStarters = data.Starters();
    }

    internal static Task<NdsGen5Session> OpenAsync(string path) => Task.Run(() =>
    {
        var rom = NdsRom.Open(path);
        try
        {
            return new NdsGen5Session(rom, Gen5Data.Open(rom));
        }
        catch
        {
            rom.Dispose();
            throw;
        }
    });

    internal Task<NdsGen5Preview> PreviewAsync(Gen5RandomizerOptions options) =>
        Task.Run(() => new NdsGen5Preview(options, Gen5Randomizer.Generate(Data, options)));

    internal string Describe(Gen5Change change) => change.Field == "Especie"
        ? $"{change.Location}: {Species(change.Before)} → {Species(change.After)}"
        : $"{change.Location}: {change.Field} {change.Before} → {change.After}";

    /// <summary>Writes the new ROM and its change log, then re-reads the ROM to confirm the tables still parse.</summary>
    internal Task<string> ExportAsync(string output, NdsGen5Preview preview) => Task.Run(() =>
    {
        string log = Path.ChangeExtension(output, ".cambios.txt");
        if (File.Exists(log))
            throw new IOException("Ya existe el registro de cambios: " + log);
        Data.Export(output, preview.Result);
        using (var exported = NdsRom.Open(output))
        {
            var check = Gen5Data.Open(exported, requireOriginalTables: false);
            if (check.Trainers().Count != TrainerCount || check.Encounters().Count != AreaCount)
                throw new InvalidDataException("La ROM exportada no conserva el número de entrenadores o de áreas.");
        }
        var text = new StringBuilder();
        text.AppendLine($"Juego: {Game} ({Data.Profile.GameCode}); volcado original verificado: {(KnownDump ? "sí" : "no")}");
        text.AppendLine($"Origen: {SourcePath}");
        text.AppendLine($"Semilla: {preview.Options.Seed}. Entrenadores: {ModeName(preview.Options.Trainers)}. Encuentros: {ModeName(preview.Options.Wild)}. Iniciales: {StarterModeName(preview.Options.Starters)}.");
        text.AppendLine($"Cambios: {preview.Result.Changes.Count}");
        text.AppendLine();
        foreach (var change in preview.Result.Changes)
            text.AppendLine(Describe(change));
        File.WriteAllText(log, text.ToString(), new UTF8Encoding(true));
        return log;
    });

    internal static string ModeName(Gen5SpeciesMode mode) => mode switch
    {
        Gen5SpeciesMode.Random => "aleatorio",
        Gen5SpeciesMode.SimilarBst => "BST similar",
        Gen5SpeciesMode.ProgressiveBst => "BST progresivo por nivel",
        _ => "sin cambios",
    };

    internal static string StarterModeName(Gen5StarterMode mode) => mode switch
    {
        Gen5StarterMode.Random => "aleatorios",
        Gen5StarterMode.Custom => "personalizados",
        _ => "sin cambios",
    };

    private string Species(int id) => id >= 0 && id < _names.Count ? _names[id] : "#" + id;

    public void Dispose() => _rom.Dispose();
}

internal sealed record NdsGen5Preview(Gen5RandomizerOptions Options, Gen5RandomizerResult Result);
