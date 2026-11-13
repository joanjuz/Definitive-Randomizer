using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;

internal enum ProfileSectionStatus { Compatible, Conflict, Incompatible, Unknown }

/// <summary>One selectable part of a profile: a group of recipe actions, the manual operations of one kind or an option.</summary>
internal sealed record ProfileSection(string Id, string Kind, string Title, string Summary, IReadOnlyList<string> Requires);

/// <summary>What a section means for the open project. Only compatible sections and conflicts can be chosen; a conflict is never chosen by default.</summary>
internal sealed record ProfileSectionReport(ProfileSection Section, ProfileSectionStatus Status, string Detail)
{
    internal bool Selectable => Status is ProfileSectionStatus.Compatible or ProfileSectionStatus.Conflict;
}

internal sealed record ProfileOmission(string What, int Count, string Reason);

internal sealed record ProfileCompatibility(bool SameGame, string Verdict, IReadOnlyList<ProfileSectionReport> Sections, IReadOnlyList<string> DependencyWarnings);

/// <summary>
/// Shareable profile, format 1 (docs/contracts/SHAREABLE_PROFILE_V1.md): a versioned envelope around a Global Template
/// recipe. It carries configuration only: no ROM, no extracted resource, no game text and no local path. It is not an
/// installer: importing it only loads the chosen sections into Global Template, whose preview, confirmation, history
/// and undo apply them. Format 1 covers the four 3DS families (XY, ORAS, SM, USUM); a profile belongs to exactly one.
/// </summary>
internal sealed class ShareableProfile
{
    internal const string Format = "definitive-randomizer-profile", Extension = ".drprofile.json";
    internal const int Version = 1, MaxBytes = 2 * 1024 * 1024, MaxSections = 64;
    private const string ManualPrefix = "manual/", ActionPrefix = "actions/", PersonalFields = "manual/personal-fields", Mints = "options/nature-mints";
    private const string GamePrefix = "game:", CapsPatch = "patch:player-level-caps", MintPatch = "patch:nature-mints";
    /// <summary>The games a profile can target, each with its generation. Recipes of different games are never mixed.</summary>
    internal static readonly IReadOnlyDictionary<string, int> Games = new Dictionary<string, int> { ["XY"] = 6, ["ORAS"] = 6, ["SM"] = 7, ["USUM"] = 7 };
    internal static string GameTitle(string game) => game switch
    { "USUM" => "Ultra Sol / Ultra Luna", "SM" => "Sol / Luna", "ORAS" => "Rubí Omega / Zafiro Alfa", "XY" => "X / Y", _ => game };
    private static string GameRequirement(string game) => GamePrefix + game.ToLowerInvariant();

    /// <summary>Manual operations a profile may carry: final values chosen by the author, never bytes or text of the game.</summary>
    internal static readonly IReadOnlySet<string> ShareableOperations = new HashSet<string>
        { "record", "move", "learnset", "personal-flags", "typechart", "shiny", "playercaps", "crointegrity", "shops6", "shops7", "pickup", "machines7", "tutors7" };

    private static readonly (string Prefix, string Title)[] Titles =
    [
        ("rng", "Semilla"), ("personal", "Pokémon: estadísticas, tipos y habilidades"), ("pokemon-stats", "Estadísticas Pokémon"), ("moves", "Movimientos"),
        ("evolutions", "Evoluciones"), ("egg-moves", "Movimientos huevo"), ("starters", "Iniciales"), ("trainers", "Entrenadores"),
        ("wild-encounters", "Encuentros salvajes"), ("static-encounters", "Encuentros estáticos"), ("trades", "Intercambios"), ("tms", "MT"),
        ("move-tutors", "Tutores"), ("marts", "Tiendas"), ("items", "Objetos"), ("field-items", "Objetos de campo"), ("pickup", "Recogida"),
        ("shiny-rate", "Probabilidad shiny"),
    ];
    private static readonly Dictionary<string, string> OperationTitles = new()
    {
        ["record"] = "Entradas editadas a mano", ["move"] = "Movimientos editados a mano", ["learnset"] = "Movimientos por nivel editados", ["personal-flags"] = "Compatibilidades MT/tutores",
        ["typechart"] = "Tabla de tipos", ["shiny"] = "Probabilidad shiny (parche)", ["playercaps"] = "Player Level Caps", ["crointegrity"] = "Integridad CRO/CRR",
["shops6"] = "Tiendas editadas a mano", ["shops7"] = "Tiendas editadas a mano", ["pickup"] = "Recogida editada a mano", ["machines7"] = "MT editadas a mano", ["tutors7"] = "Tutores editados a mano",
    };

    private readonly JsonObject _root;
    private readonly GlobalTemplateDocument _recipe;
    internal string Name => _root["name"]!.GetValue<string>();
    internal string Author => Text(_root, "author", 80, false);
    internal string Description => Text(_root, "description", 2000, false);
    internal string Game => _root["target"]!["game"]!.GetValue<string>();
    internal int Generation => _root["target"]!["generation"]!.GetValue<int>();
    internal string Generator => _root["generator"] is JsonObject g ? (Text(g, "name", 80, false) + " " + Text(g, "version", 40, false)).Trim() : "";
    internal string CreatedUtc => Text(_root, "createdUtc", 40, false);
    internal string RecipeSha256 => _root["recipeSha256"]!.GetValue<string>();
    internal IReadOnlyList<ProfileSection> Sections { get; }
    internal IReadOnlyList<ProfileOmission> Omitted { get; }
    private ShareableProfile(JsonObject root, GlobalTemplateDocument recipe, IReadOnlyList<ProfileSection> sections, IReadOnlyList<ProfileOmission> omitted)
    { _root = root; _recipe = recipe; Sections = sections; Omitted = omitted; }

    internal string ToJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    internal static string GameOf(GameProjectSession project) =>
        project.GameConfiguration.USUM ? "USUM" : project.GameConfiguration.SM ? "SM" : project.GameConfiguration.ORAS ? "ORAS" : "XY";
    private static string Sha(JsonNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));

    // ---- export -------------------------------------------------------------------------------------------------

    /// <summary>Builds a profile from the recipe that «Guardar template global» would write. Nothing is written here.</summary>
    internal static ShareableProfile Create(GameProjectSession project, GlobalTemplateDocument export, string name, string author, string description)
    {
        string game = GameOf(project); int generation = Games[game];
        if (project.Generation != generation) throw new InvalidOperationException("Los perfiles compartibles admiten proyectos de X/Y, Rubí Omega/Zafiro Alfa, Sol/Luna y Ultra Sol/Ultra Luna.");
        name = Clean(name, 120); author = Clean(author, 80); description = Clean(description, 2000);
        if (name.Length == 0) throw new ArgumentException("El perfil necesita un nombre.");
        var recipe = export.Clone();
        if (recipe.Game.Equals("ANY", StringComparison.OrdinalIgnoreCase)) recipe.SetSection("Game", JsonValue.Create(game));
        recipe.Validate(game, generation);
        var omitted = Strip(recipe);
        recipe.Validate(game, generation);
        var node = JsonNode.Parse(recipe.ToJson())!.AsObject();
        RejectLocalPaths(node, "recipe", project.ProjectRoot);
        var sections = SectionsOf(recipe);
        if (sections.Count == 0) throw new InvalidDataException("La receta no contiene nada que se pueda compartir en un perfil.");
        if (sections.Count > MaxSections) throw new InvalidDataException("El perfil tendría demasiadas secciones.");
        var root = new JsonObject
        {
            ["format"] = Format, ["version"] = Version, ["name"] = name, ["author"] = author, ["description"] = description,
            ["createdUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["generator"] = new JsonObject { ["name"] = "Definitive Randomizer", ["version"] = typeof(ShareableProfile).Assembly.GetName().Version?.ToString() ?? "" },
            ["target"] = new JsonObject { ["platform"] = "3ds", ["game"] = game, ["generation"] = generation },
            ["sections"] = new JsonArray(sections.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id, ["kind"] = s.Kind, ["title"] = s.Title, ["summary"] = s.Summary, ["requires"] = new JsonArray(s.Requires.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            }).ToArray()),
            ["dependencies"] = recipe.Section("Assets") ?? new JsonArray(),
            ["omitted"] = new JsonArray(omitted.Select(o => (JsonNode)new JsonObject { ["what"] = o.What, ["count"] = o.Count, ["reason"] = o.Reason }).ToArray()),
            ["recipe"] = node, ["recipeSha256"] = Sha(node),
        };
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("El perfil supera el límite de 2 MB.");
        return Parse(json);
    }

    /// <summary>Writes the profile to a new file. An existing file is never replaced here: the dialog asks first.</summary>
    internal void Save(string path)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null) Directory.CreateDirectory(folder);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson(), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static string Clean(string? text, int max)
    {
        text = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>Removes what a profile must not carry and says what was left out.</summary>
    private static List<ProfileOmission> Strip(GlobalTemplateDocument recipe)
    {
        var omitted = new List<ProfileOmission>();
        if (recipe.Section("ProgressiveApp") is not JsonObject native) return omitted;
        native.Remove("Profile");
        if (native["ManualEdits"] is JsonObject manual)
        {
            if (manual["MissingHistoryPayloads"] is JsonArray { Count: > 0 })
                throw new InvalidDataException("El historial tiene modificaciones antiguas sin datos completos. Guárdalas de nuevo antes de exportar un perfil.");
            var operations = manual["Operations"] as JsonArray ?? [];
            var kept = new JsonArray();
            foreach (var group in operations.Select(o => o!.AsObject()).GroupBy(o => o["Kind"]?.GetValue<string>() ?? ""))
            {
                bool shareable = ShareableOperations.Contains(group.Key);
                if (shareable) foreach (var operation in group) kept.Add(operation.DeepClone());
                else omitted.Add(new(group.Key, group.Count(), group.Key switch
                {
                    "resources" => "Recursos reemplazados: contienen datos del juego o archivos propios y no se comparten.",
                    "authored-template" => "Templates creados: dependen de un archivo local.",
                    "exact-overrides" => "Valores exactos de textos: incluyen texto del juego.",
                    _ => "Operación no compartible en el formato 1.",
                }));
            }
            var entries = manual["Entries"] as JsonArray ?? [];
            var personal = new JsonArray(entries.Where(e => e?["Kind"]?.GetValue<string>() == "personal").Select(e => e!.DeepClone()).ToArray());
            if (personal.Count != entries.Count) omitted.Add(new("text", entries.Count - personal.Count, "Textos editados: incluyen texto del juego."));
            manual["Operations"] = kept; manual["Entries"] = personal;
            if (kept.Count == 0 && personal.Count == 0) native.Remove("ManualEdits");
        }
        recipe.SetSection("ProgressiveApp", native);
        if (native["ManualEdits"] is null)
            recipe.SelectActions(recipe.Actions.Where(id => !id.Equals(ManualEditTemplate.ActionId, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return omitted;
    }

    private static readonly Regex LocalPath = new(@"(^|[\s""'=(])([A-Za-z]:[\\/]|\\\\[^\\]|/home/|/Users/)", RegexOptions.Compiled);
    private static void RejectLocalPaths(JsonNode? node, string at, string projectRoot)
    {
        switch (node)
        {
            case JsonObject o: foreach (var p in o) RejectLocalPaths(p.Value, at + "." + p.Key, projectRoot); break;
            case JsonArray a: for (int i = 0; i < a.Count; i++) RejectLocalPaths(a[i], at + "[" + i + "]", projectRoot); break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                string text = v.GetValue<string>();
                if (LocalPath.IsMatch(text) || text.Contains(projectRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("La receta contiene una ruta local en " + at + ". No se exporta un perfil con rutas de este equipo.");
                break;
        }
    }

    // ---- sections -----------------------------------------------------------------------------------------------

    private static JsonObject[] Operations(GlobalTemplateDocument recipe) =>
        recipe.Section("ProgressiveApp")?["ManualEdits"]?["Operations"] is JsonArray a ? a.Select(o => o!.AsObject()).ToArray() : [];
    private static int PersonalEntries(GlobalTemplateDocument recipe) => recipe.Section("ProgressiveApp")?["ManualEdits"]?["Entries"] is JsonArray a ? a.Count : 0;

    /// <summary>The sections are derived from the recipe itself; the list written in the file must agree with them.</summary>
    internal static IReadOnlyList<string> AppliedSectionIds(GlobalTemplateDocument recipe) => SectionsOf(recipe).Select(s => s.Id).ToArray();
    private static List<ProfileSection> SectionsOf(GlobalTemplateDocument recipe)
    {
        var sections = new List<ProfileSection>();
        string only = GameRequirement(recipe.Game);
        foreach (var group in recipe.Actions.Where(id => !id.Equals(ManualEditTemplate.ActionId, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(id => id.Contains('.') ? id[..id.IndexOf('.')] : id, StringComparer.OrdinalIgnoreCase))
        {
            string prefix = group.Key.ToLowerInvariant();
            string title = Titles.FirstOrDefault(t => t.Prefix == prefix).Title ?? prefix;
            sections.Add(new(ActionPrefix + prefix, "actions", title, string.Join(", ", group), [only]));
        }
        foreach (var group in Operations(recipe).GroupBy(o => o["Kind"]!.GetValue<string>()))
        {
            string summary = group.Count() == 1 ? "1 operación" : group.Count() + " operaciones";
            if (group.Key == "playercaps")
            {
                var table = PlayerLevelCapRecipe.Read(group.Last()["Payload"]!.AsObject());
                summary = $"{table.Entries.Count} puntos; límites de Nv. {table.Entries.Min(e => e.Cap)} a Nv. {table.Entries.Max(e => e.Cap)}";
            }
            sections.Add(new(ManualPrefix + group.Key, "manual-operations", OperationTitles.GetValueOrDefault(group.Key, group.Key), summary,
                group.Key == "playercaps" ? [only, CapsPatch] : [only]));
        }
        if (PersonalEntries(recipe) is > 0 and var fields)
            sections.Add(new(PersonalFields, "manual-values", "Valores exactos de Pokémon", fields == 1 ? "1 valor" : fields + " valores", [only]));
        if (UsumMintItemPolicy.Read(recipe))
            sections.Add(new(Mints, "option", "Mentas Gen7 en tiendas, objetos y Recogida", "Añade las mentas a los objetos disponibles", [only, MintPatch]));
        return sections;
    }

    // ---- import -------------------------------------------------------------------------------------------------

    internal static ShareableProfile Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No existe el perfil.", path);
        if (info.Length > MaxBytes) throw new InvalidDataException("El perfil supera el límite de 2 MB.");
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    private static string Text(JsonObject o, string key, int max, bool required)
    {
        if (o[key] is null) { if (required) throw new InvalidDataException("Al perfil le falta «" + key + "»."); return ""; }
        if (o[key] is not JsonValue v || v.GetValueKind() != JsonValueKind.String) throw new InvalidDataException("«" + key + "» del perfil no es texto.");
        string text = v.GetValue<string>();
        if (text.Length > max || required && text.Trim().Length == 0) throw new InvalidDataException("«" + key + "» del perfil está vacío o es demasiado largo.");
        return text;
    }
    private static int Whole(JsonObject o, string key)
    {
        if (o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && int.TryParse(v.ToJsonString(), out int number)) return number;
        throw new InvalidDataException("«" + key + "» del perfil no es un número entero.");
    }

    internal static ShareableProfile Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("El perfil supera el límite de 2 MB.");
        JsonObject root;
        try { root = JsonNode.Parse(json, null, new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject ?? throw new InvalidDataException("El perfil no es un objeto JSON."); }
        catch (JsonException) { throw new InvalidDataException("El archivo no es un perfil: no contiene JSON válido."); }
        if (root["format"] is not JsonValue format || format.GetValueKind() != JsonValueKind.String || format.GetValue<string>() != Format)
            throw new InvalidDataException("El archivo no es un perfil compartible de Definitive Randomizer.");
        int version = Whole(root, "version");
        if (version > Version) throw new InvalidDataException($"El perfil usa el formato {version}, más reciente que esta versión de la aplicación (formato {Version}). Actualiza la aplicación para importarlo.");
        if (version < 1) throw new InvalidDataException("Versión de perfil no válida.");
        Text(root, "name", 120, true); Text(root, "author", 80, false); Text(root, "description", 2000, false); Text(root, "createdUtc", 40, false);
        if (root["generator"] is not (null or JsonObject)) throw new InvalidDataException("«generator» del perfil no es válido.");
        if (root["target"] is not JsonObject target) throw new InvalidDataException("Al perfil le falta el juego de destino.");
        if (Text(target, "platform", 16, true) != "3ds") throw new InvalidDataException("El perfil es de otra plataforma; las recetas de DS, 3DS y Switch no se mezclan.");
        string game = Text(target, "game", 16, true); int generation = Whole(target, "generation");
        if (!Games.TryGetValue(game, out int expected) || generation != expected)
            throw new InvalidDataException($"El perfil es de {game} (generación {generation}). Esta versión importa perfiles de X/Y, Rubí Omega/Zafiro Alfa, Sol/Luna y Ultra Sol/Ultra Luna.");
        if (root["recipe"] is not JsonObject recipeNode) throw new InvalidDataException("Al perfil le falta la receta.");
        if (!Text(root, "recipeSha256", 64, true).Equals(Sha(recipeNode), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El perfil está dañado o se modificó: la receta no coincide con su huella.");
        GlobalTemplateDocument recipe;
        try { recipe = GlobalTemplateDocument.Parse(recipeNode.ToJsonString(), game, generation); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or JsonException or FormatException or KeyNotFoundException or NullReferenceException)
        { throw new InvalidDataException("La receta del perfil no es válida: " + e.Message); }
        if (Operations(recipe).FirstOrDefault(o => !ShareableOperations.Contains(o["Kind"]?.GetValue<string>() ?? "")) is { } foreign)
            throw new InvalidDataException("El perfil contiene una operación que un perfil no puede llevar: " + (foreign["Kind"]?.GetValue<string>() ?? "?"));
        if (recipe.Section("ProgressiveApp")?["ManualEdits"]?["Entries"] is JsonArray entries && entries.Any(e => e?["Kind"]?.GetValue<string>() != "personal"))
            throw new InvalidDataException("El perfil contiene textos del juego, que un perfil no puede llevar.");
        if (recipe.Section("ProgressiveApp")?["ManualEdits"]?["MissingHistoryPayloads"] is JsonArray { Count: > 0 })
            throw new InvalidDataException("El perfil declara modificaciones sin datos.");
        List<ProfileSection> computed;
        try { computed = SectionsOf(recipe); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or NullReferenceException or KeyNotFoundException)
        { throw new InvalidDataException("La receta del perfil no es válida: " + e.Message); }
        if (root["sections"] is not JsonArray listed || listed.Count > MaxSections) throw new InvalidDataException("La lista de secciones del perfil no es válida.");
        var extra = new Dictionary<string, string[]>();
        foreach (var item in listed)
        {
            if (item is not JsonObject section) throw new InvalidDataException("Sección de perfil no válida.");
            string id = Text(section, "id", 80, true);
            if (section["requires"] is not (null or JsonArray)) throw new InvalidDataException("Requisitos de sección no válidos.");
            var requires = (section["requires"] as JsonArray ?? []).Select(r => r is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : throw new InvalidDataException("Requisito de sección no válido.")).ToArray();
            if (!extra.TryAdd(id, requires)) throw new InvalidDataException("Sección de perfil repetida: " + id);
        }
        if (!extra.Keys.ToHashSet().SetEquals(computed.Select(s => s.Id)))
            throw new InvalidDataException("Las secciones que declara el perfil no coinciden con el contenido de su receta.");
        // A requirement written in the file is honoured even if this version does not derive it: an unknown one blocks the section.
        var sections = computed.Select(s => s with { Requires = s.Requires.Union(extra[s.Id]).ToArray() }).ToList();
        var omitted = new List<ProfileOmission>();
        if (root["omitted"] is JsonArray left)
            foreach (var item in left.OfType<JsonObject>().Take(MaxSections))
                omitted.Add(new(Text(item, "what", 80, false), item["count"] is null ? 0 : Whole(item, "count"), Text(item, "reason", 300, false)));
        return new(root, recipe, sections, omitted);
    }

    // ---- compatibility ------------------------------------------------------------------------------------------

    /// <summary>Read-only: what each section would mean for this project. Writes nothing.</summary>
    internal ProfileCompatibility Evaluate(GameProjectSession project)
    {
        string game = GameOf(project);
        if (game != Game || project.Generation != Generation)
        {
            string why = $"El perfil es de {Game} (generación {Generation}) y el proyecto abierto es {game} (generación {project.Generation}).";
            return new(false, why, Sections.Select(s => new ProfileSectionReport(s, ProfileSectionStatus.Incompatible, why)).ToArray(), []);
        }
        var history = EffectiveHistory.Of(project.History.Read());
        var applied = history.Entries.Select(e => e.Parameters.GetValueOrDefault("ActionId") ?? "").Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = Operations(GlobalTemplateDocument.FromHistory(history, Game, Generation, null)).GroupBy(o => o["Key"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.Last()["Payload"]!.ToJsonString());
        var reports = new List<ProfileSectionReport>();
        foreach (var section in Sections)
        {
            var (status, detail) = (ProfileSectionStatus.Compatible, "Compatible con este proyecto.");
            foreach (string requirement in section.Requires)
            {
                var (ok, known, why) = Requirement(project, requirement);
                if (!known) { (status, detail) = (ProfileSectionStatus.Unknown, "Requiere una capacidad que esta versión de la aplicación no conoce: " + requirement); break; }
                if (!ok) { (status, detail) = (ProfileSectionStatus.Incompatible, why); break; }
            }
            if (status == ProfileSectionStatus.Compatible) (status, detail) = Conflict(project, section, applied, current) ?? (status, detail);
            reports.Add(new(section, status, detail));
        }
        var folders = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "custom_balance_templates"), Path.Combine(project.ProjectRoot, "custom_balance_templates") };
        int usable = reports.Count(r => r.Selectable);
        string verdict = usable == reports.Count ? "Todas las secciones se pueden usar en este proyecto." :
            usable == 0 ? "Ninguna sección se puede usar en este proyecto." : $"{usable} de {reports.Count} secciones se pueden usar en este proyecto.";
        return new(true, verdict, reports, _recipe.CheckAssets(folders));
    }

    private (bool Ok, bool Known, string Why) Requirement(GameProjectSession project, string requirement)
    {
        if (requirement.StartsWith(GamePrefix, StringComparison.Ordinal))
        {
            string wanted = Games.Keys.FirstOrDefault(g => GameRequirement(g) == requirement) ?? "";
            return (wanted == GameOf(project), wanted.Length > 0, "Solo " + GameTitle(wanted) + ".");
        }
        switch (requirement)
        {
            case MintPatch:
                var (available, report) = new UsumMintItemPolicy(project).Inspect();
                return (available, true, "Requiere el parche de mentas instalado. " + report);
            case CapsPatch:
                var capability = new GamePlayerLevelCapService(project).Capability();
                return (capability.CanApply, true, GamePlayerLevelCapService.Describe(capability));
            default: return (false, false, "");
        }
    }
    /// <summary>The table installed in both files, when both carry the current patch with the same table.</summary>
    private static LevelCapTable? InstalledCaps(GameProjectSession project)
    {
        try
        {
            string battlePath = GamePlayerLevelCapService.Find(project.GameConfiguration.RomFS, "Battle.cro", true), codePath = GamePlayerLevelCapService.Find(project.GameConfiguration.ExeFS, "code.bin", false);
            if (!File.Exists(battlePath) || !File.Exists(codePath)) return null;
            return LevelCapPatch.InspectBattle(File.ReadAllBytes(battlePath), out var first) == LevelCapCopyState.Applied &&
                LevelCapPatch.InspectCandy(File.ReadAllBytes(codePath), out var second) == LevelCapCopyState.Applied && first.ToBytes().SequenceEqual(second.ToBytes()) ? first : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private (ProfileSectionStatus, string)? Conflict(GameProjectSession project, ProfileSection section, HashSet<string> applied, Dictionary<string, string> current)
    {
        if (section.Kind == "actions")
        {
            var actions = ActionsOf(section);
            try { ProjectFeatureAvailability.ValidateActions(project, actions); }
            catch (InvalidDataException e) { return (ProfileSectionStatus.Incompatible, e.Message); }
            var again = actions.Where(applied.Contains).ToArray();
            return again.Length == 0 ? null : (ProfileSectionStatus.Conflict, "Ya se aplicó en este proyecto (" + string.Join(", ", again) + "): se volvería a ejecutar sobre el resultado actual.");
        }
        if (section.Kind != "manual-operations") return null;
        string kind = section.Id[ManualPrefix.Length..];
        if (kind == "playercaps")
        {
            var installed = InstalledCaps(project);
            if (installed is null) return (ProfileSectionStatus.Compatible, "Se instalaría con la tabla del perfil. " + new GamePlayerLevelCapService(project).Capability().Summary);
            var table = PlayerLevelCapRecipe.Read(Operations(_recipe).Last(o => o["Kind"]!.GetValue<string>() == kind)["Payload"]!.AsObject());
            return installed.ToBytes().SequenceEqual(table.ToBytes()) ? (ProfileSectionStatus.Compatible, "El proyecto ya tiene instalada esta misma tabla.") :
                (ProfileSectionStatus.Conflict, $"Sustituiría la tabla instalada ({installed.Entries.Count} puntos) por la del perfil ({table.Entries.Count} puntos).");
        }
        int replaced = Operations(_recipe).Count(o => o["Kind"]!.GetValue<string>() == kind && current.TryGetValue(o["Key"]!.GetValue<string>(), out var payload) && payload != o["Payload"]!.ToJsonString());
        return replaced == 0 ? null : (ProfileSectionStatus.Conflict, replaced == 1 ? "Reemplazaría 1 modificación manual que ya tiene el proyecto." : $"Reemplazaría {replaced} modificaciones manuales que ya tiene el proyecto.");
    }
    private string[] ActionsOf(ProfileSection section)
    {
        string prefix = section.Id[ActionPrefix.Length..];
        return _recipe.Actions.Where(id => (id.Contains('.') ? id[..id.IndexOf('.')] : id).Equals(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // ---- selection ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The Global Template recipe with only the chosen sections, ready for the existing preview and apply. A section that
    /// the report does not allow is refused: there is no way to force it.
    /// </summary>
    internal GlobalTemplateDocument Select(ProfileCompatibility compatibility, IReadOnlyCollection<string> chosen)
    {
        if (chosen.Count == 0) throw new InvalidOperationException("Elige al menos una sección del perfil.");
        foreach (string id in chosen)
        {
            var report = compatibility.Sections.FirstOrDefault(r => r.Section.Id == id) ?? throw new InvalidOperationException("El perfil no tiene la sección " + id + ".");
            if (!report.Selectable) throw new InvalidOperationException("La sección «" + report.Section.Title + "» no se puede usar en este proyecto: " + report.Detail);
        }
        var recipe = _recipe.Clone();
        var kinds = chosen.Where(id => id.StartsWith(ManualPrefix, StringComparison.Ordinal) && id != PersonalFields).Select(id => id[ManualPrefix.Length..]).ToHashSet();
        bool personal = chosen.Contains(PersonalFields), manualKept = false;
        var native = recipe.Section("ProgressiveApp") as JsonObject ?? new();
        if (native["ManualEdits"] is JsonObject manual)
        {
            var operations = new JsonArray((manual["Operations"] as JsonArray ?? []).Where(o => kinds.Contains(o!["Kind"]!.GetValue<string>())).Select(o => o!.DeepClone()).ToArray());
            var entries = personal ? manual["Entries"]?.DeepClone() ?? new JsonArray() : new JsonArray();
            manual["Operations"] = operations; manual["Entries"] = entries;
            manualKept = operations.Count > 0 || entries.AsArray().Count > 0;
            if (!manualKept) native.Remove("ManualEdits");
        }
        native["Profile"] = new JsonObject { ["Format"] = Format, ["Version"] = Version, ["Name"] = Name, ["Author"] = Author, ["RecipeSha256"] = RecipeSha256, ["Sections"] = new JsonArray(chosen.Order(StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()) };
        recipe.SetSection("ProgressiveApp", native);
        var actions = Sections.Where(s => s.Kind == "actions" && chosen.Contains(s.Id)).SelectMany(ActionsOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (manualKept) actions.Add(ManualEditTemplate.ActionId);
        recipe.SelectActions(actions);
        if (!chosen.Contains(Mints)) UsumMintItemPolicy.Capture(recipe, false);
        NativeRecipeCompletion.ReconcileSelection(recipe);
        recipe.Rename(Name);
        recipe.Validate(Game, Generation);
        return recipe;
    }
}
