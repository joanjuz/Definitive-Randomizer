using System.IO;
using System.Text;
using pk3DS.Core.NDS;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// One opened DS ROM for the randomizer window: a Gen5 game (Black, White, White 2) or a Gen4 game (Diamond, Platinum,
/// HeartGold), where starters cannot change yet. Heavy work runs off the UI thread; callers must not
/// overlap operations. A preview is tied to the options that produced it, so a stale preview is never exported.
/// </summary>
internal sealed class NdsGen5Session : IDisposable
{
    private readonly NdsRom _rom;
    private readonly IReadOnlyList<string> _names;
    private readonly IReadOnlyList<string> _items;

    /// <summary>Gen5 data, or null for a Gen4 game.</summary>
    internal Gen5Data? Data { get; }
    /// <summary>Gen4 data, or null for a Gen5 game.</summary>
    internal Gen4Data? Gen4 { get; }
    internal NdsIdentityResult Identity { get; }
    internal string SourcePath => _rom.Path;
    internal string Game => Identity.Profile.Game;
    internal string GameCode => Identity.Profile.GameCode;
    internal bool KnownDump => Identity.KnownDump;
    /// <summary>Starters can only change in Gen5 games for now.</summary>
    internal bool SupportsStarters => Data is not null;
    internal int MaxSpecies => Data is not null ? Gen5Randomizer.MaxSpecies : Gen4Data.MaxSpecies;
    internal int TrainerCount { get; }
    internal int AreaCount { get; }
    internal IReadOnlyList<string> SpeciesNames => _names;
    /// <summary>Original starters (Grass, Fire, Water); empty for Gen4 games.</summary>
    internal IReadOnlyList<int> OriginalStarters { get; }

    private NdsGen5Session(NdsRom rom, Gen5Data data)
    {
        _rom = rom;
        Data = data;
        Identity = data.Identity;
        _names = data.SpeciesNames();
        _items = data.Text(data.Profile.ItemNamesText);
        TrainerCount = data.Trainers().Count;
        AreaCount = data.Encounters().Count;
        OriginalStarters = data.Starters();
    }

    private NdsGen5Session(NdsRom rom, Gen4Data data)
    {
        _rom = rom;
        Gen4 = data;
        Identity = data.Identity;
        _names = data.SpeciesNames();
        _items = data.Text(data.Profile.ItemNamesText);
        TrainerCount = data.Trainers().Count;
        AreaCount = Gen4Areas(data);
        OriginalStarters = [];
    }

    // A Gen4 encounter table is one group of slots in one file.
    private static int Gen4Areas(Gen4Data data) => data.Encounters().Select(s => (s.File, s.Group)).Distinct().Count();

    internal static Task<NdsGen5Session> OpenAsync(string path) => Task.Run(() =>
    {
        var rom = NdsRom.Open(path);
        try
        {
            return NdsIdentity.Identify(rom).Profile.Generation == 4
                ? new NdsGen5Session(rom, Gen4Data.Open(rom))
                : new NdsGen5Session(rom, Gen5Data.Open(rom));
        }
        catch
        {
            rom.Dispose();
            throw;
        }
    });

    internal Task<NdsGen5Preview> PreviewAsync(Gen5RandomizerOptions options) =>
        Task.Run(() => new NdsGen5Preview(options, Data is not null ? Gen5Randomizer.Generate(Data, options) : Gen4Randomizer.Generate(Gen4!, options)));

    internal string Describe(Gen5Change change) => change.Field switch
    {
        "Especie" => $"{change.Location}: {Species(change.Before)} → {Species(change.After)}",
        "Objeto" => $"{change.Location}: {Item(change.Before)} → {Item(change.After)}",
        _ => $"{change.Location}: {change.Field} {change.Before} → {change.After}",
    };

    /// <summary>Writes the new ROM and its change log, then re-reads the ROM to confirm the tables still parse.</summary>
    internal Task<string> ExportAsync(string output, NdsGen5Preview preview) => Task.Run(() =>
    {
        string log = Path.ChangeExtension(output, ".cambios.txt");
        if (File.Exists(log))
            throw new IOException("Ya existe el registro de cambios: " + log);
        if (Data is not null)
            Data.Export(output, preview.Result);
        else
            Gen4!.Export(output, preview.Result);
        using (var exported = NdsRom.Open(output))
        {
            (int trainers, int areas) = Data is not null
                ? (Gen5Data.Open(exported, requireOriginalTables: false) is var gen5 ? (gen5.Trainers().Count, gen5.Encounters().Count) : default)
                : (Gen4Data.Open(exported, requireOriginalTables: false) is var gen4 ? (gen4.Trainers().Count, Gen4Areas(gen4)) : default);
            if (trainers != TrainerCount || areas != AreaCount)
                throw new InvalidDataException("La ROM exportada no conserva el número de entrenadores o de áreas.");
        }
        var text = new StringBuilder();
        text.AppendLine($"Juego: {Game} ({GameCode}); volcado original verificado: {(KnownDump ? "sí" : "no")}");
        text.AppendLine($"Origen: {SourcePath}");
        text.AppendLine($"Semilla: {preview.Options.Seed}. Entrenadores: {ModeName(preview.Options.Trainers)}. Encuentros: {ModeName(preview.Options.Wild)}. Iniciales: {StarterModeName(preview.Options.Starters)}. Objetos del mapa: {ItemModeName(preview.Options.FieldItems)}. Tiendas: {ItemModeName(preview.Options.Shops)}.");
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

    internal static string ItemModeName(Gen5ItemMode mode) => mode == Gen5ItemMode.Shuffle ? "barajados" : "sin cambios";

    // Item changes are logged with the game's item names.
    private string Item(int id) => id >= 0 && id < _items.Count ? _items[id] : "#" + id;

    private string Species(int id) => id >= 0 && id < _names.Count ? _names[id] : "#" + id;

    public void Dispose() => _rom.Dispose();
}

internal sealed record NdsGen5Preview(Gen5RandomizerOptions Options, Gen5RandomizerResult Result);
