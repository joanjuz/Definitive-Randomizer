using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using pk3DS.Core.NDS;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.Nds;

/// <summary>
/// Adapts one DS section to the editor contract shared with 3DS and Sword/Shield: entries, fields with their ranges and names,
/// manual saves and randomization proposals. Storage stays in <see cref="NdsProject"/>; this view never writes ROM files.
/// </summary>
internal sealed partial class NdsRecordEditor : IProjectEditor, ITrainerEditorPresentation, IWildEditorPresentation, ITableEditorPresentation, ITrainerRuleTableEditor,
    IOptionTemplateProjectEditor, IOptionTemplatePresentation, IRecordSummary
{
    // The Pokémon section shows the header card of the 3DS Pokémon editor; the other sections have no species header.
    public RecordSummary? Summary(int id, IReadOnlyDictionary<string, int> values)
    {
        if (_section != NdsSection.Pokemon)
            return null;
        int V(string key) => values.TryGetValue(key, out int v) ? v : _cells.FirstOrDefault(c => c.Entry == id && c.Local == key)?.Value ?? 0;
        string Type(int t) => t >= 0 && t < _project.TypeNames.Count ? _project.TypeNames[t] : "?";
        var types = new[] { Type(V("Type1")), Type(V("Type2")) }.Distinct().ToArray();
        string name = id < _project.SpeciesNames.Count ? _project.SpeciesNames[id] : $"#{id:000}";
        return new RecordSummary(id, name, $"Especie #{id:000}", types, Enumerable.Range(0, 6).Sum(i => V("Stat" + i)));
    }

    private static readonly string[] SpeciesModes = ["Aleatorio", "BST similar", "BST progresivo por nivel"];
    private readonly NdsProject _project;
    private readonly NdsSection _section;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ConditionalWeakTable<EditorProposal, NdsPreview> _proposals = new();
    private IReadOnlyList<NdsCell> _cells = [];
    private int _revision = -1;
    private IReadOnlyList<MoveChoice>? _species, _items, _moves, _types, _abilities;

    internal event Action? Changed;
    internal NdsSection Section => _section;
    public string Title => NdsProject.SectionName(_section);
    public bool UsesTrainerLayout => _section == NdsSection.Trainers;
    public bool UsesWildLayout => _section == NdsSection.Encounters;
    // Long lists of similar values read best as a table, as in Sword/Shield. Egg moves are a form, as the 3DS Egg Moves editor:
    // in one table row their move lists were too narrow to read.
    public bool UsesRecordTable => _section is NdsSection.Learnsets or NdsSection.Evolutions or NdsSection.Machines or NdsSection.Shops or NdsSection.Tutors;
    private static bool IsData(NdsSection section) => section >= NdsSection.Pokemon;
    public IReadOnlyList<RecordEntry> Entries { get; private set; } = [];

    // Level Caps and Trainer Move Rules rows (as the 3DS tables), kept while the editor lives and used by every preview.
    private Dictionary<int, TrainerRuleTableRow>? _rules;

    public IReadOnlyList<TrainerRuleTableRow> GetRuleRows()
    {
        if (_section != NdsSection.Trainers)
            return [];
        var kept = _rules ?? [];
        _rules = _project.Trainers().Where(t => t.Team.Count > 0 && NdsTrainerTools.Kind(t.ClassName, t.Name) != "Normal").ToDictionary(t => t.Id, t => kept.GetValueOrDefault(t.Id) ?? new TrainerRuleTableRow
        {
            // Gen4 writes the PK/MN glyphs of "PKMN Trainer" as [PK][MN].
            Id = t.Id, Group = NdsTrainerTools.Kind(t.ClassName, t.Name), Trainer = $"{t.ClassName.Replace("[PK][MN]", "PKMN", StringComparison.Ordinal)} {t.Name}".Trim(), Ace = t.Team.Max(p => p.Level),
            CapUse = false, Cap = 0, MoveUse = false, AllowStatus = true, SupportsMega = false, SupportsZMoves = false, SupportsEVs = false,
        });
        return [.. _rules.Values.OrderBy(r => r.Id)];
    }

    public void UpdateRuleRows(IReadOnlyList<TrainerRuleTableRow> rows, bool levelCaps)
    {
        GetRuleRows();
        foreach (var row in rows)
        {
            if (!_rules!.TryGetValue(row.Id, out var rule))
                continue;
            if (row.Cap is < 0 or > 100)
                throw new ArgumentException($"El cap del entrenador #{row.Id} debe estar entre 0 y 100.");
            if (levelCaps) { rule.CapUse = row.CapUse; rule.Cap = row.Cap; rule.Max3 = row.Max3; }
            else { rule.MoveUse = row.MoveUse; rule.AllowStatus = row.AllowStatus; }
        }
    }

    internal NdsRecordEditor(NdsProject project, NdsSection section)
    {
        _project = project;
        _section = section;
        Refresh();
    }

    private void Refresh()
    {
        if (_revision == _project.Revision)
            return;
        _cells = UsesTrainerLayout ? _project.ReadTrainerFields() : _project.Read(_section);
        _revision = _project.Revision;
        Entries = _cells.GroupBy(c => c.Entry).Select(g => new RecordEntry(g.Key, g.First().EntryName)).ToArray();
    }

    private IReadOnlyList<MoveChoice>? Choices(NdsCell cell) => cell.Kind switch
    {
        NdsValueKind.Species => _species ??= Names(_project.SpeciesNames, 0, _project.MaxSpecies),
        NdsValueKind.Item => _items ??= Names(_project.ItemNames, 0, _project.ItemNames.Count - 1),
        NdsValueKind.Move => _moves ??= Names(_project.MoveNames, 0, _project.MoveNames.Count - 1),
        NdsValueKind.Type => _types ??= _project.TypeNames.Select((n, i) => new MoveChoice(i, n)).ToArray(),
        NdsValueKind.Ability => _abilities ??= Names(_project.AbilityNames, 0, _project.AbilityNames.Count - 1),
        NdsValueKind.Option => cell.Options?.Select((n, i) => new MoveChoice(i, n)).ToArray(),
        _ => null,
    };

    private static IReadOnlyList<MoveChoice> Names(IReadOnlyList<string> names, int first, int last) =>
        Enumerable.Range(first, Math.Max(0, last - first + 1)).Select(i => new MoveChoice(i, i == 0 ? "—" : i < names.Count ? names[i] : "#" + i)).ToArray();

    public IReadOnlyList<EditorGroup> Read(int id)
    {
        Refresh();
        var rows = _cells.Where(c => c.Entry == id).ToArray();
        if (rows.Length == 0)
            throw new ArgumentException("Entrada desconocida: " + id);
        return rows.GroupBy(c => c.Group).Select(g => new EditorGroup(g.Key, g.Select(c =>
        {
            var choices = Choices(c);
            // Lists only offer what the cell's range allows (for example, no species 0 in a trainer team).
            return new EditorField(c.Local, c.Field, c.Value, c.Min, c.Max, choices?.Where(m => m.Id >= c.Min && m.Id <= c.Max).ToArray());
        }).ToArray())).ToArray();
    }

    public void Save(int id, IReadOnlyDictionary<string, int> values)
    {
        if (_revision != _project.Revision)
            throw new InvalidOperationException("La ROM cambió desde que se abrió esta entrada. Vuelve a seleccionarla antes de guardar.");
        var rows = _cells.Where(c => c.Entry == id).ToArray();
        EditorValues.Validate(Read(id), values);
        bool includeMoves = !UsesTrainerLayout || values.GetValueOrDefault("HasMoves", rows.First(r=>r.Local=="HasMoves").Value)==1;
        bool includeItems = !UsesTrainerLayout || values.GetValueOrDefault("HasItems", rows.First(r=>r.Local=="HasItems").Value)==1;
        var edits = rows.Where(c => values.TryGetValue(c.Local, out int v) && v != c.Value
            && (c.Kind != NdsValueKind.Move || includeMoves)
            && (!c.Local.EndsWith(".Item", StringComparison.Ordinal) || includeItems))
            .ToDictionary(c => c.Key, c => values[c.Local]);
        if (edits.Count == 0)
            return;
        _project.Apply(UsesTrainerLayout ? _project.PreviewTrainerEdits(edits) : _project.PreviewEdits(_section, edits));
        Refresh();
        Changed?.Invoke();
    }

    // The options, their keys, texts, defaults and groups are those of the 3DS editors (trainer randomizer of Sun/Moon, wild and static
    // encounters, Level Up, Egg Moves, evolutions and Pokémon), so a 3DS user finds the same screen; the DS backend does the work.
    // Options the DS games cannot have (Mega forms, shinies, SOS, Z moves) are left out; DS-only options follow in their own groups.
    // Values loaded from an options template (or kept while navigating) over the defaults below.
    private Dictionary<string, int>? _optionValues;

    public IReadOnlyList<EditorGroup> RandomOptions => _optionValues is null ? BaseOptions
        : BaseOptions.Select(g => g with { Fields = g.Fields.Select(f => _optionValues.TryGetValue(f.Key, out int v) && v >= f.Min && v <= f.Max ? f with { Value = v } : f).ToArray() }).ToArray();

    /// <summary>Options templates as in 3DS: trainers (with the Level Caps and Move Rules rows) and wild encounters.</summary>
    internal bool SupportsOptionTemplates => _section is NdsSection.Trainers or NdsSection.Encounters;
    public string ImportOptionsLabel => _section == NdsSection.Trainers ? "Cargar template de entrenadores" : "Cargar template Wild";
    public string ExportOptionsLabel => _section == NdsSection.Trainers ? "Exportar template de entrenadores" : "Exportar template Wild";

    public string CaptureOptions(IReadOnlyDictionary<string, int> options)
    {
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["Format"] = "DS-Options-1", ["Game"] = _project.Identity.Profile.GameCode, ["Section"] = _section.ToString(),
            ["Options"] = System.Text.Json.JsonSerializer.SerializeToNode(options.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)),
        };
        if (_section == NdsSection.Trainers && _rules is not null)
            json["Rules"] = System.Text.Json.JsonSerializer.SerializeToNode(_rules.Values.Where(r => r.CapUse || r.MoveUse || r.Cap != 0 || r.Max3 || !r.AllowStatus)
                .Select(r => new { r.Id, r.CapUse, r.Cap, r.Max3, r.MoveUse, r.AllowStatus }));
        return json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public void RestoreOptions(string text)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("El template no es un objeto JSON.");
        if (json["Format"]?.GetValue<string>() != "DS-Options-1" || json["Section"]?.GetValue<string>() != _section.ToString())
            throw new InvalidDataException("El template no es de " + Title.ToLowerInvariant() + " de Nintendo DS.");
        _optionValues = json["Options"].Deserialize<Dictionary<string, int>>() ?? [];
        // The rows name trainers of one game: only that game takes them.
        if (_section == NdsSection.Trainers && json["Game"]?.GetValue<string>() == _project.Identity.Profile.GameCode && json["Rules"] is System.Text.Json.Nodes.JsonArray rules)
        {
            GetRuleRows();
            foreach (var r in rules.OfType<System.Text.Json.Nodes.JsonObject>())
                if (_rules!.TryGetValue(r["Id"]!.GetValue<int>(), out var row))
                {
                    row.CapUse = r["CapUse"]!.GetValue<bool>(); row.Cap = Math.Clamp(r["Cap"]!.GetValue<int>(), 0, 100); row.Max3 = r["Max3"]!.GetValue<bool>();
                    row.MoveUse = r["MoveUse"]!.GetValue<bool>(); row.AllowStatus = r["AllowStatus"]!.GetValue<bool>();
                }
        }
    }

    public void ImportOptions(string path) => RestoreOptions(File.ReadAllText(path));
    public void ExportOptions(string path, IReadOnlyDictionary<string, int> options) => File.WriteAllText(path, CaptureOptions(options));

    private IReadOnlyList<EditorGroup> BaseOptions => _section switch
    {
        NdsSection.Trainers =>
        [
            new EditorGroup("Main", [Flag("CHK_Level", "Multiplicar nivel (%) · 100 conserva, 120 aumenta 20 %", 0), new EditorField("NUD_LevelBoost", "Nivel (%) · 120 = nivel 20 → 24", 100, 10, 300),
                Flag("CHK_ProgressiveBST", "BST progresivo", 0), Flag("CHK_E", "Legendarios de evento", 1), Flag("CHK_L", "Legendarios del juego", 1), .. Generations("CHK_G", true),
                Flag("CHK_BST", "Randomizar por BST", 0), Flag("CHK_RandomPKM", "Randomizar especies", 1)]),
            new EditorGroup("Estadísticas y movimientos", [new EditorField("CB_Moves", "Movimientos", 0, 0, 3, Choices("Conservar", "Randomizar todos", "Solo por nivel", "Modo Metrónomo")),
                Flag("CHK_ForceHighPower", "Forzar ataques potentes desde el nivel", 0), new EditorField("NUD_ForceHighPower", "Nivel para los ataques potentes", 50, 1, 100),
                Flag("CHK_NoFixedDamage", "Sin movimientos de daño fijo (Furia Dragón, Bomba Sónica)", 0), new EditorField("NUD_Damage", "Mínimo de movimientos ofensivos", 2, 0, 4),
                .. _project.Generation == 5 ? [Flag("CHK_RandomAbilities", "Randomizar habilidades (1 o 2)", 1)] : Array.Empty<EditorField>(),
                new EditorField("NUD_STAB", "Mínimo de movimientos STAB", 2, 0, 4), Flag("CHK_Damage", "Asegurar un mínimo de movimientos ofensivos", 0), Flag("CHK_STAB", "Asegurar un mínimo de movimientos STAB", 0)]),
            new EditorGroup("Entrenadores", [Flag("CHK_6PKM", "6 Pokémon para entrenadores importantes", 0), new EditorField("NUD_ForceFullyEvolved", "Nivel para la evolución final", 50, 1, 100),
                new EditorField("NUD_RMin", "Pokémon mínimos por equipo", 1, 1, 6), new EditorField("NUD_RMax", "Pokémon máximos por equipo", 6, 1, 6), Flag("CHK_TypeTheme", "Un tipo por entrenador", 0),
                Flag("CHK_RandomClass", "Randomizar clases de entrenador (nombres de clase)", 0), Flag("CHK_ForceFullyEvolved", "Forzar evolución final desde el nivel", 0),
                Flag("Names", "Barajar nombres de entrenador", 0)]),
            new EditorGroup("Reglas", [Flag("CHK_MaxAI", "IA máxima de entrenador", 1), Flag("CHK_MaxDiffPKM", "IV al máximo", 1),
                new EditorField("NUD_DoubleBattleChance", _project.DoublesProblem is { } problem ? "Probabilidad de combate doble (%) · no disponible: " + problem
                    : _project.Generation == 5 ? "Probabilidad de combate doble (%) · experimental en Gen5" : "Probabilidad de combate doble (%)", _project.DoublesProblem is null ? 15 : 0, 0, _project.DoublesProblem is null ? 100 : 0),
                Flag("CHK_RandomDoubleBattles", "Combates dobles", 0), Flag("CHK_LevelCaps", "Límites de nivel", 0)]),
            new EditorGroup("Priorizar mejor estadística", [Flag("CHK_StrongStatBosses", "Jefes", 1), Flag("CHK_StrongStatImportantTrainers", "Entrenadores importantes", 1),
                Flag("CHK_StrongStatNormalTrainers", "Entrenadores normales", 1), new EditorField("NUD_StrongStatTolerance", "Tolerancia de atacante mixto", 15, 0, 255), Flag("CHK_StrongStat", "Priorizar mejor estadística", 0)]),
            new EditorGroup("Movimientos mejorados", [Flag("CHK_BetterMovesetsBosses", "Jefes", 1), Flag("CHK_BetterMovesetsImportantTrainers", "Entrenadores importantes", 1),
                Flag("CHK_BetterMovesetsNormalTrainers", "Entrenadores normales", 1), Flag("CHK_BetterMovesetsIncludeTMs", "Incluir MT", 0), Flag("CHK_BetterMovesets", "Movimientos mejorados", 0)]),
            .. BstRanges(),
            // The per-trainer caps are in «Tabla de Level Caps»; they apply with «Límites de nivel».
            new EditorGroup("Política global de Level Caps", [Flag("GlobalCaps.ApplyPrevious", "Escalado progresivo de entrenadores sin cap", 1),
                Flag("GlobalCaps.Story", "Escalado según la cronología de la historia (WikiDex)", 1),
                new EditorField("GlobalCaps.PreviousGap", "Separación de nivel con entrenadores anteriores", 2, 0, 20)], false),
            new EditorGroup("Objetos equipados", [Flag("CHK_ItemClause", "No repetir objetos en el equipo", 0), Flag("CHK_BanBadItems", "Excluir objetos poco útiles", 1),
                new EditorField("CB_SmartHeldItemModeBoss", "Calidad Smart Items · jefes", 1, 0, 2, Quality), new EditorField("CB_SmartHeldItemModeImportant", "Calidad Smart Items · importantes", 1, 0, 2, Quality),
                new EditorField("CB_SmartHeldItemModeNormal", "Calidad Smart Items · normales", 1, 0, 2, Quality),
                Flag("CHK_SmartItemsBosses", "Jefes", 1), Flag("CHK_SmartItemsImportantTrainers", "Entrenadores importantes", 1), Flag("CHK_SmartItemsNormalTrainers", "Entrenadores normales", 1),
                Flag("CHK_SmartHeldItems", "Smart Items", 0), Flag("CHK_RandomItems", "Objetos equipados aleatorios", 1)]),
        ],
        NdsSection.Encounters =>
        [
            new EditorGroup("Especies permitidas", [Flag("Species", "Randomizar especies", 1), .. Generations("G", false), Flag("Legends", "Incluir legendarios", 0),
                Flag("Events", "Incluir legendarios de evento", 0), Flag("SimilarBST", "Mantener BST similar", 0), Flag("UniquePerTable", "Priorizar especies distintas en cada fila", 0),
                Flag("RelaxBST", "Usar BST más cercano si la tolerancia queda vacía", 0), new EditorField("BST", "Tolerancia BST (%) · 0 = libre", 30, 0, 100), Flag("Progressive", "BST progresivo por nivel", 0)]),
            new EditorGroup("Niveles", [Flag("ModifyLevel", "Modificar niveles al randomizar", 1), new EditorField("LevelScale", "Multiplicador de niveles (%)", 100, 1, 500),
                new EditorField("LevelFlat", "Ajuste plano (-100 a 100)", 0, -100, 100)]),
            .. BstRanges(),
            new EditorGroup("Al estilo de Universal Randomizer", [Flag("Global", "Mapeo global de especies (la misma especie cambia igual en todas las rutas)", 0)], false),
        ],
        NdsSection.Statics =>
        [
            new EditorGroup("Especies permitidas", [Flag("Species", "Randomizar especies", 1), .. Generations("G", false), Flag("Legends", "Permitir Pokémon legendarios", 1),
                Flag("Events", "Permitir Pokémon de evento", 1), Flag("SimilarBST", "Limitar especies por BST similar", 0), new EditorField("BST", "Tolerancia BST (%) · 0 = libre", 30, 0, 100),
                Flag("LegendToLegend", "Reemplazar legendarios por legendarios", 0)]),
        ],
        NdsSection.Trades => [new EditorGroup(Title, [Flag("Given", "Randomizar el Pokémon que entregan", 1), Flag("Requested", "Randomizar el Pokémon que piden", 1)])],
        // Three distinct random starters; the options and their keys follow the 3DS starters (BasicStarter, Legends).
        NdsSection.Starters => [new EditorGroup(Title, [Flag("BasicStarter", "Iniciales básicos con dos evoluciones (línea de 3 etapas)", 0),
            Flag("Legends", "Permitir Pokémon legendarios", 1),
            // Diamond and Platinum give the starter with an item; the section then has an "Item" cell.
            .. _cells.Any(c => c.Local == "Item") ? [Flag("Item", "Objeto aleatorio para el inicial", 0)] : Array.Empty<EditorField>()])],
        // 3DS Egg Moves; the DS lists have a fixed length per species, so "Ampliar lista" has no equivalent.
        NdsSection.EggMoves =>
        [
            new EditorGroup("Opciones originales", [Flag("STAB", "Priorizar movimientos STAB", 1), new EditorField("STABPercent", "Porcentaje STAB", 33, 0, 100),
                Flag("Hms", "Incluir MO entre movimientos huevo", 0)]),
            new EditorGroup("Más opciones", [Flag("Metronome", "Solo Metrónomo", 0)], false),
        ],
        NdsSection.Tutors => [new EditorGroup(Title, [new EditorField("Mode", "Compatibilidad", 0, 0, 2, Choices("Aleatoria", "Por tipo (mismo tipo siempre)", "Completa")),
            new EditorField("Percent", "Probabilidad (%)", 50, 0, 100)])],
        NdsSection.MoveLists => [new EditorGroup(Title,
        [
            .. _cells.Any(c => c.Entry == 0) ? [Flag("TMs", "Movimientos de las MT aleatorios (las MO no cambian)", 1)] : Array.Empty<EditorField>(),
            .. _cells.Any(c => c.Entry == 1) ? [Flag("Tutors", "Movimientos de los tutores aleatorios", 1)] : Array.Empty<EditorField>(),
            Flag("KeepField", "Conservar movimientos de campo (Destello, Golpe Cabeza…)", 1),
        ])],
        // 3DS «Randomizar Pokémon» (BatchToolsView).
        NdsSection.Pokemon =>
        [
            new EditorGroup("Estadísticas", [Flag("Stats", "Variar estadísticas base", 0), new EditorField("Deviation", "Variación de estadísticas (%)", 25, 0, 100),
                Flag("Shuffle", "Barajar estadísticas (preservar BST)", 0)]),
            new EditorGroup("Tipos y habilidades", [Flag("Types", "Randomizar tipos", 0), new EditorField("SingleTypeChance", "Probabilidad de un solo tipo (%)", 50, 0, 100),
                Flag("Abilities", "Randomizar habilidades", 1), Flag("WonderGuard", "Permitir Wonder Guard", 0), Flag("BanTraps", "Excluir Shadow Tag y Arena Trap", 1),
                Flag("Follow", "Las evoluciones heredan tipos y habilidades", 1)]),
            new EditorGroup("Objetos y captura", [Flag("Items", "Randomizar objetos equipados", 1), Flag("SetCatch", "Fijar ratio de captura", 0), new EditorField("CatchValue", "Ratio de captura (0–255)", 255, 0, 255)]),
            new EditorGroup("Más opciones", [new EditorField("MinCatch", "Ratio de captura mínimo (0 = sin cambios)", 0, 0, 255), Flag("Growth", "Crecimiento medio rápido (legendarios lento)", 0)], false),
        ],
        // 3DS «Randomizar movimientos»: types and categories; the DS editor also randomizes power, accuracy and PP.
        NdsSection.Moves =>
        [
            new EditorGroup("Movimientos", [Flag("MoveTypes", "Tipos de movimientos", 1)]),
            new EditorGroup("Más opciones", [Flag("Power", "Poder aleatorio (20–150)", 0), Flag("Accuracy", "Precisión aleatoria (50–100)", 0), Flag("PP", "PP aleatorios (5–40)", 0)], false),
        ],
        // 3DS Level Up · «Randomizar todos los Pokémon». The lists keep their length (no "Expandir cantidad").
        NdsSection.Learnsets =>
        [
            new EditorGroup("Randomizar todos los Pokémon", [Flag("AllowHms", "Permitir MO / HM", 0), Flag("Stab", "Sesgo por tipo (STAB)", 0), new EditorField("StabPercent", "Porcentaje STAB (0–100)", 15, 0, 100),
                Flag("StartFour", "Comenzar con 4 movimientos", 0), Flag("Spread", "Distribuir niveles uniformemente", 0), new EditorField("SpreadTo", "Último nivel de aprendizaje (1–100)", 79, 1, 100),
                Flag("ExcludeFixed", "Excluir movimientos de daño fijo", 1), Flag("Metronome", "Metrónomo · todos", 0)]),
        ],
        // 3DS evolutions · «Ajustes personalizados».
        NdsSection.Evolutions =>
        [
            new EditorGroup("Ajustes personalizados", [Flag("Species", "Randomizar destinos de evolución", 1), new EditorField("BST", "Tolerancia BST del destino (%) · 0 = libre", 30, 0, 100),
                Flag("Trade", "Cambiar evoluciones por intercambio a nivel", 0), new EditorField("TradeLevel", "Nivel para evoluciones por intercambio", 37, 1, 100)]),
            new EditorGroup("Más opciones", [Flag("Time", "Quitar evoluciones por hora del día", 0), new EditorField("MaxLevel", "Nivel máximo de evolución (0 = sin cambios)", 0, 0, 100)], false),
        ],
        NdsSection.Machines => [new EditorGroup(Title, [new EditorField("Mode", "Compatibilidad", 0, 0, 2, Choices("Aleatoria", "Por tipo (mismo tipo siempre)", "Completa")), new EditorField("Percent", "Probabilidad (%)", 50, 0, 100), Flag("KeepHms", "Conservar la compatibilidad de las MO", 1)])],
        _ => [new EditorGroup(Title, [new EditorField("Shuffle", "Barajar (solo objetos normales; MT, MO y objetos clave no se mueven)", 1, 1, 1)])],
    };

    private static readonly IReadOnlyList<MoveChoice> Quality = Choices("Normal", "Fuerte", "Competitivo");

    // Generation boxes as in 3DS (newest first in the trainer randomizer), up to this game's generation.
    private IEnumerable<EditorField> Generations(string prefix, bool trainerStyle) =>
        (trainerStyle ? Enumerable.Range(1, _project.Generation).Reverse() : Enumerable.Range(1, _project.Generation))
        .Select(g => Flag(prefix + g, (trainerStyle ? "Generación " : "Generación ") + g, 1));

    // Allowed generations as a bit mask (0 = all of them).
    private int GenerationMask(IReadOnlyDictionary<string, int> options, string prefix)
    {
        int mask = 0;
        for (int g = 1; g <= _project.Generation; g++)
            if (options.GetValueOrDefault(prefix + g, 1) == 1)
                mask |= 1 << (g - 1);
        if (mask == 0)
            throw new ArgumentException("Elige al menos una generación.");
        return mask == (1 << _project.Generation) - 1 ? 0 : mask;
    }

    private static NdsTrainerCategories Categories(IReadOnlyDictionary<string, int> options, string prefix) => new()
    {
        Normal = options.GetValueOrDefault(prefix + "NormalTrainers", 1) == 1, Important = options.GetValueOrDefault(prefix + "ImportantTrainers", 1) == 1,
        Boss = options.GetValueOrDefault(prefix + "Bosses", 1) == 1,
    };

    private static EditorField Flag(string key, string label, int value) => new(key, label, value, 0, 1);

    // The editable BST table of 3DS and Switch, read back by key prefix "BST.n.".
    private static IEnumerable<EditorGroup> BstRanges() => Gen5BstRange.Defaults.Select((r, i) => new EditorGroup("Rango " + (i + 1),
    [
        new EditorField($"BST.{i}.MinLevel", "Nivel mínimo", r.MinLevel, 1, 100), new EditorField($"BST.{i}.MaxLevel", "Nivel máximo", r.MaxLevel, 1, 100),
        new EditorField($"BST.{i}.MinBST", "BST mínimo", r.MinBst, 0, 1530), new EditorField($"BST.{i}.MaxBST", "BST máximo", r.MaxBst, 0, 1530), Flag($"BST.{i}.FullRandom", "Full Random", 0),
    ]));

    private static IReadOnlyList<Gen5BstRange> ReadRanges(IReadOnlyDictionary<string, int> options)
    {
        var ranges = options.Where(p => p.Key.StartsWith("BST.", StringComparison.Ordinal)).GroupBy(p => int.Parse(p.Key.Split('.')[1])).OrderBy(g => g.Key)
            .Select(g => { int V(string name) => options[$"BST.{g.Key}.{name}"]; return new Gen5BstRange(V("MinLevel"), V("MaxLevel"), V("MinBST"), V("MaxBST"), V("FullRandom") == 1); }).ToArray();
        return ranges.Length > 0 ? ranges : Gen5BstRange.Defaults;
    }
    private static IReadOnlyList<MoveChoice> Choices(params string[] names) => names.Select((n, i) => new MoveChoice(i, n)).ToArray();

    public EditorProposal Preview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope)
    {
        // The BST table may have more or fewer rows than the defaults; ProgressiveBstTable checks it.
        EditorValues.Validate(RandomOptions.Select(g => g with { Fields = g.Fields.Where(f => !f.Key.StartsWith("BST.", StringComparison.Ordinal)).ToArray() }), options);
        Refresh();
        if (scope.Count == 0)
            throw new ArgumentException("No hay entradas en el alcance elegido.");
        uint seed = SwitchSeed.New32();
        if (IsData(_section))
            return Propose(DataPreview(options, scope, seed), seed);
        int V(string key, int fallback = 0) => options.GetValueOrDefault(key, fallback);
        bool trainers = _section == NdsSection.Trainers;
        // Species mode from the 3DS boxes: randomize, progressive BST table, similar BST.
        bool randomize = V(trainers ? "CHK_RandomPKM" : "Species", 1) == 1;
        bool progressive = V(trainers ? "CHK_ProgressiveBST" : "Progressive") == 1;
        bool similar = V(trainers ? "CHK_BST" : "SimilarBST") == 1 && (trainers || V("BST", 30) > 0);
        var species = _section is not (NdsSection.Trainers or NdsSection.Encounters) ? Gen5SpeciesMode.Random
            : !randomize ? Gen5SpeciesMode.Keep : progressive ? Gen5SpeciesMode.ProgressiveBst : similar ? Gen5SpeciesMode.SimilarBst : Gen5SpeciesMode.Random;
        var ranges = ReadRanges(options);
        // The whole section is one scope value: null keeps the randomizers' original behaviour.
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Trainers => new Gen5RandomizerOptions(seed) { Trainers = species, Scope = chosen, TypeThemedTrainers = V("CHK_TypeTheme") == 1,
                ForceFullyEvolvedLevel = V("CHK_ForceFullyEvolved") == 1 ? V("NUD_ForceFullyEvolved", 50) : 0, TrainerLevelPercent = V("CHK_Level") == 1 ? V("NUD_LevelBoost", 100) - 100 : 0,
                BstRanges = ranges, BanGameLegends = V("CHK_L", 1) == 0, BanMythicals = V("CHK_E", 1) == 0, Generations = GenerationMask(options, "CHK_G") },
            NdsSection.Encounters => new Gen5RandomizerOptions(seed) { Wild = species, Scope = chosen, WildGlobalMapping = V("Global") == 1, UniqueWildPerTable = V("UniquePerTable") == 1,
                WildLevelPercent = V("ModifyLevel", 1) == 1 ? V("LevelScale", 100) - 100 : 0, WildLevelFlat = V("ModifyLevel", 1) == 1 ? V("LevelFlat") : 0, BstRanges = ranges,
                BanGameLegends = V("Legends") == 0, BanMythicals = V("Events") == 0, Generations = GenerationMask(options, "G"), BstTolerance = V("BST", 30), ClosestBst = V("RelaxBST") == 1 },
            NdsSection.Starters => new Gen5RandomizerOptions(seed) { Starters = Gen5StarterMode.Random, RandomStarterItem = V("Item") == 1,
                ThreeStageStarters = V("BasicStarter") == 1, BanLegendaries = V("Legends", 1) == 0 },
            NdsSection.FieldItems => new Gen5RandomizerOptions(seed) { FieldItems = Gen5ItemMode.Shuffle, Scope = chosen },
            _ => new Gen5RandomizerOptions(seed) { Shops = Gen5ItemMode.Shuffle, Scope = chosen },
        };
        string action = "Randomizar " + Title.ToLowerInvariant() + (_section is NdsSection.Trainers or NdsSection.Encounters && species != Gen5SpeciesMode.Keep ? " · " + SpeciesModes[(int)species - 1] : "");
        if (_section == NdsSection.Trainers)
        {
            bool caps = V("CHK_LevelCaps") == 1;
            var tools = new NdsTrainerOptions(seed ^ 0x5EED)
            {
                MinTeamSize = V("NUD_RMin", 1) > 1 ? V("NUD_RMin", 1) : 0, MaxTeamSize = V("NUD_RMax", 6), SixForImportant = V("CHK_6PKM") == 1,
                Generations = GenerationMask(options, "CHK_G"), BanGameLegends = V("CHK_L", 1) == 0, BanMythicals = V("CHK_E", 1) == 0,
                Moves = V("CB_Moves") switch { 1 => NdsTrainerMoveMode.Random, 2 => NdsTrainerMoveMode.LevelUp, 3 => NdsTrainerMoveMode.Metronome, _ => NdsTrainerMoveMode.Keep },
                HighPowerLevel = V("CHK_ForceHighPower") == 1 ? V("NUD_ForceHighPower", 50) : 0, NoFixedDamage = V("CHK_NoFixedDamage") == 1,
                MinDamagingMoves = V("CHK_Damage") == 1 ? V("NUD_Damage", 2) : 0, MinStabMoves = V("CHK_STAB") == 1 ? V("NUD_STAB", 2) : 0,
                RandomAbilities = _project.Generation == 5 && V("CHK_RandomAbilities", 1) == 1,
                BetterMovesets = V("CHK_BetterMovesets") == 1, BetterFor = Categories(options, "CHK_BetterMovesets"), BetterIncludeMachines = V("CHK_BetterMovesetsIncludeTMs") == 1,
                StrongestStat = V("CHK_StrongStat") == 1, StrongStatFor = Categories(options, "CHK_StrongStat"), StrongStatTolerance = V("NUD_StrongStatTolerance", 15),
                Items = V("CHK_RandomItems", 1) == 1 ? NdsTrainerItemMode.Random : NdsTrainerItemMode.Keep, SmartItems = V("CHK_SmartHeldItems") == 1,
                SmartItemsFor = Categories(options, "CHK_SmartItems"), SmartQualityNormal = V("CB_SmartHeldItemModeNormal", 1), SmartQualityImportant = V("CB_SmartHeldItemModeImportant", 1),
                SmartQualityBoss = V("CB_SmartHeldItemModeBoss", 1), BanBadItems = V("CHK_BanBadItems", 1) == 1, ItemClause = V("CHK_ItemClause") == 1,
                MaxIvs = V("CHK_MaxDiffPKM", 1) == 1, MaxAi = V("CHK_MaxAI", 1) == 1, DoubleChance = V("CHK_RandomDoubleBattles") == 1 ? V("NUD_DoubleBattleChance") : 0, Scope = chosen,
                ShuffleNames = V("Names") == 1, ShuffleClassNames = V("CHK_RandomClass") == 1,
                LevelCaps = caps ? _rules?.Values.Where(r => r.CapUse).Select(r => new NdsTrainerLevelCap(r.Id, r.Cap, r.Max3)).ToArray() ?? [] : [],
                MoveRules = _rules?.Values.Where(r => r.MoveUse).Select(r => new NdsTrainerMoveRule(r.Id, r.AllowStatus)).ToArray() ?? [],
                ScaleBetweenCaps = caps && V("GlobalCaps.ApplyPrevious", 1) == 1, CapGap = V("GlobalCaps.PreviousGap", 2),
                // As USUM: the scaling follows the game's story chronology (WikiDex); off, the caps by level as before.
                StoryScaling = caps && V("GlobalCaps.ApplyPrevious", 1) == 1 && V("GlobalCaps.Story", 1) == 1,
            };
            return Propose(_project.PreviewTrainers(action, (Gen5RandomizerOptions)config, tools), seed);
        }
        return Propose(_project.PreviewRandom(action, config), seed);
    }

    private NdsPreview DataPreview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope, uint seed)
    {
        int V(string key, int fallback = 0) => options.GetValueOrDefault(key, fallback);
        IReadOnlySet<int>? chosen = scope.Count == Entries.Count ? null : scope.ToHashSet();
        var config = _section switch
        {
            NdsSection.Pokemon => new NdsDataOptions(seed) { Stats = V("Stats") == 1 ? NdsStatsMode.Vary : V("Shuffle") == 1 ? NdsStatsMode.Shuffle : NdsStatsMode.Keep, StatDeviation = V("Deviation", 25),
                Types = V("Types") == 1, SingleTypePercent = V("SingleTypeChance", 50), Abilities = V("Abilities", 1) == 1, AllowWonderGuard = V("WonderGuard") == 1, BanTrapAbilities = V("BanTraps", 1) == 1,
                FollowEvolutions = V("Follow", 1) == 1, MinCatchRate = V("MinCatch"), SetCatchRate = V("SetCatch") == 1 ? Math.Max(1, V("CatchValue", 255)) : 0,
                StandardizeGrowth = V("Growth") == 1, WildHeldItems = V("Items", 1) == 1, Scope = chosen },
            NdsSection.Moves => new NdsDataOptions(seed) { MovePower = V("Power") == 1, MoveAccuracy = V("Accuracy") == 1, MovePp = V("PP") == 1, MoveTypes = V("MoveTypes", 1) == 1, Scope = chosen },
            NdsSection.Learnsets => new NdsDataOptions(seed) { Learnsets = V("Metronome") == 1 ? NdsLearnsetMode.Metronome : NdsLearnsetMode.Random, SameTypePercent = V("Stab") == 1 ? V("StabPercent", 15) : 0,
                AllowHmMoves = V("AllowHms") == 1, StartWithFour = V("StartFour") == 1, SpreadLevelsTo = V("Spread") == 1 ? V("SpreadTo", 79) : 0, ExcludeFixedDamage = V("ExcludeFixed", 1) == 1, Scope = chosen },
            NdsSection.Evolutions => new NdsDataOptions(seed) { RemoveTradeEvolutions = V("Trade") == 1, TradeEvolutionLevel = V("TradeLevel", 37), RandomEvolutions = V("Species", 1) == 1,
                EvolutionBstTolerance = V("BST", 30) == 0 ? 100 : V("BST", 30), RemoveTimeEvolutions = V("Time") == 1, MaxEvolutionLevel = V("MaxLevel"), Scope = chosen },
            NdsSection.Statics => new NdsDataOptions(seed) { Statics = V("Species", 1) == 1 ? NdsStaticMode.Random : NdsStaticMode.Keep, StaticGenerations = GenerationMask(options, "G"),
                StaticBanGameLegends = V("Legends", 1) == 0, StaticBanMythicals = V("Events", 1) == 0, StaticSimilarBst = V("SimilarBST") == 1 && V("BST", 30) > 0, StaticBstTolerance = V("BST", 30),
                StaticLegendToLegend = V("LegendToLegend") == 1, Scope = chosen },
            NdsSection.Trades => new NdsDataOptions(seed) { TradeGiven = V("Given") == 1, TradeRequested = V("Requested") == 1, Scope = chosen },
            NdsSection.EggMoves => new NdsDataOptions(seed) { EggMoves = V("Metronome") == 1 ? NdsLearnsetMode.Metronome : NdsLearnsetMode.Random, SameTypePercent = V("STAB", 1) == 1 ? V("STABPercent", 33) : 0,
                AllowHmMoves = V("Hms") == 1, Scope = chosen },
            NdsSection.Tutors => new NdsDataOptions(seed) { Tutors = (NdsMachineMode)(V("Mode") + 1), TutorPercent = V("Percent", 50), Scope = chosen },
            NdsSection.MoveLists => new NdsDataOptions(seed) { RandomTmMoves = V("TMs") == 1, RandomTutorMoves = V("Tutors") == 1, KeepFieldMoves = V("KeepField", 1) == 1, Scope = chosen },
            _ => new NdsDataOptions(seed) { Machines = (NdsMachineMode)(V("Mode") + 1), MachinePercent = V("Percent", 50), KeepHms = V("KeepHms", 1) == 1, Scope = chosen },
        };
        return _project.PreviewData("Randomizar " + Title, _section, config);
    }

    private EditorProposal Propose(NdsPreview preview, uint seed)
    {
        string action = preview.Action;
        var changes = preview.Result.Changes.Select(c => new BatchChange(0, c.Location, c.Field, TrainerBeforeLabel(c) ?? Label(c, c.Before), Label(c, c.After))).ToArray();
        var proposal = new EditorProposal(_owner, action, unchecked((int)seed), [], changes,
            new Dictionary<string, string> { ["Platform"] = "NDS", ["Game"] = _project.Identity.Profile.GameCode, ["Seed"] = seed.ToString() });
        _proposals.Add(proposal, preview);
        return proposal;
    }

    // Change values shown with their names, as in the export log.
    private string Label(Gen5Change change, int value)
    {
        string text = _project.Describe(change with { Before = value, After = value });
        int arrow = text.LastIndexOf(" → ", StringComparison.Ordinal);
        return arrow >= 0 ? text[(arrow + 3)..] : value.ToString();
    }

    public void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner || !_proposals.TryGetValue(proposal, out var preview))
            throw new InvalidOperationException("Propuesta ajena a este editor.");
        _project.Apply(preview);
        _proposals.Remove(proposal);
        Refresh();
        Changed?.Invoke();
    }
}
