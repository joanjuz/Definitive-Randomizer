using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThreeDS.Progressive.Services;

public sealed record TextContentEvidence(string Category, int Line, string Match, string Source="")
{
    public string Display => Category + " · " + Match + (Line>=0?$" · línea {Line}":"") + (Source.Length>0?" · fuente: "+Source:"");
}
public sealed record TextContentClassification(IReadOnlyList<TextContentEvidence> Evidence)
{
    public IReadOnlyList<string> Tags => Evidence.Select(e => e.Category).Distinct().ToArray();
    public string Summary => string.Join(" · ", Evidence.Select(e => e.Category + ": " + e.Match).Distinct().Take(4));
}

/// <summary>Content hints, never a claim that a bank belongs to a map or speaker.</summary>
public static class TextContentClassifier
{
    public static readonly string[] Categories = ["Intercambios", "Servicios y tiendas", "Tutoriales", "Menús", "Cajas del PC", "Lugares mencionados", "Personajes mencionados", "Vacíos o reservados", "Solo códigos de control", "Curación y Centro Pokémon", "Compras y servicios", "Regalos y recogida de objetos", "Crianza y huevos", "Pesca y captura", "Combates y desafíos", "Instalaciones de combate", "Concursos", "Bases Secretas", "Festi Plaza", "Poké Recreo", "Superentrenamiento", "Pokémonturas", "Minijuegos y actividades", "Conexión e intercambios", "Fósiles y formas", "Exploración y obstáculos", "Movimientos y habilidades", "Relatos y leyendas"];
    private static readonly (string Category, string[] Terms)[] Common =
    [
        ("Intercambios", ["te cambio", "me cambias", "quieres cambiarme", "intercambiar conmigo", "would you trade", "trade with me"]),
        ("Servicios y tiendas", ["centro pokemon", "tienda pokemon", "que deseas comprar", "que quieres comprar", "restaurar la salud", "pokemon center", "would you like to buy"]),
        ("Tutoriales", ["para capturar", "para atrapar", "como capturar", "te ensenare", "te voy a ensenar", "how to catch"]),
        ("Menús", ["guardar la partida", "continuar la partida", "nueva partida", "save the game", "new game"]),
        ("Curación y Centro Pokémon", ["curar a tus pokemon", "curar a los pokemon", "restaurar la salud", "centro pokemon", "recuperado todas sus fuerzas"]),
        ("Compras y servicios", ["que deseas comprar", "que quieres comprar", "que deseas vender", "tienda pokemon", "peluqueria", "boutique", "corte de pelo", "recordar un movimiento", "olvidar un movimiento", "ponerte un mote"]),
        ("Regalos y recogida de objetos", ["has obtenido", "has encontrado", "te regalo", "arbol de bayas", "regalo misterioso"]),
        ("Crianza y huevos", ["guarderia pokemon", "cuidados pokemon", "ha salido del huevo", "hemos encontrado un huevo", "recoger el huevo"]),
        ("Fósiles y formas", ["fosil", "fosiles", "meteorito", "rotom", "deoxys", "cambiar de forma"]),
        ("Exploración y obstáculos", ["golpe roca", "ascensor", "a que planta", "cortar este arbol", "usar surf", "usar fuerza", "desnivel"]),
        ("Movimientos y habilidades", ["ensenar movimientos", "ensene a algun pokemon", "ensenar a tus pokemon", "inspector de motes", "juzgar el potencial", "poder oculto"]),
        ("Relatos y leyendas", ["hace mucho tiempo", "hace mucho pero que mucho tiempo", "leyenda", "leyendas"]),
        ("Pesca y captura", ["quieres probar a pescar", "sedal", "anzuelo", "para capturar", "lanzar la poke ball", "capturar pokemon"]),
        ("Combates y desafíos", ["echemos un combate", "combate pokemon", "combatir contigo", "lider del gimnasio", "gimnasio pokemon", "alto mando", "gran prueba", "pokemon dominante"]),
        ("Instalaciones de combate", ["arbol de combate", "estadio royale", "agencia de combate", "mansion batalla", "bastion batalla"]),
        ("Concursos", ["concurso pokemon", "concursos pokemon", "concurso de belleza", "concurso de dulzura", "concurso de dureza", "licuabayas", "pokecubos"]),
        ("Bases Secretas", ["base secreta", "bases secretas", "superbase secreta", "superbases secretas"]),
        ("Festi Plaza", ["festi plaza", "plaza festival", "festimonedas", "festifan"]),
        ("Poké Recreo", ["poke recreo", "pokelitos"]),
        ("Superentrenamiento", ["superentrenamiento", "super entrenamiento"]),
        ("Pokémonturas", ["pokemontura", "buscamontura", "carga tauros", "olfateo stoutland"]),
        ("Minijuegos y actividades", ["surfeo mantine", "pokevisor", "fotoclub", "loteria id"]),
        ("Conexión e intercambios", ["intercambio prodigioso", "gts", "conexion a internet", "comunicacion local", "sincronizar juego"])
    ];
    // Names cross-checked against the four WikiDex guides; no dialogue is distributed.
    private static readonly Dictionary<string, string[]> Places = new()
    {
        ["XY"] = ["Pueblo Boceto", "Pueblo Acuarela", "Bosque de Novarte", "Ciudad Novarte", "Ciudad Luminalia", "Pueblo Vánitas", "Palacio Cénit", "Gruta Tierraunida", "Pueblo Petroglifo", "Cueva Brillante", "Ciudad Relieve", "Pueblo Crómlech", "Cueva Reflejos", "Ciudad Yantra", "Torre Maestra", "Ciudad Témpera", "Central de Kalos", "Ciudad Romantis", "Fábrica de Poké Balls", "Pueblo Fresco", "Gruta Helada", "Ciudad Fluxus", "Pueblo Mosaico", "Ciudad Fractal", "Villa Pokémon", "Ciudad Batik", "Mansión Batalla"],
        ["ORAS"] = ["Villa Raíz", "Pueblo Escaso", "Ciudad Petalia", "Ciudad Férrica", "Pueblo Azuliza", "Ciudad Portual", "Ciudad Malvalona", "Pueblo Pardal", "Cascada Meteoro", "Desfiladero", "Pueblo Lavacalda", "Instituto Meteorológico", "Ciudad Arborada", "Monte Pírico", "Guarida Magma", "Guarida Aqua", "Ciudad Algaria", "Caverna Abisal", "Arrecípolis", "Calle Victoria", "Liga Pokémon", "Episodio Delta"],
        ["SM"] = ["Melemele", "Akala", "Ula-Ula", "Poni", "Liga Pokémon", "Árbol de Combate"],
        ["USUM"] = ["Melemele", "Akala", "Ula-Ula", "Poni", "Liga Pokémon", "Árbol de Combate", "Equipo Rainbow Rocket"]
    };
    private static readonly Dictionary<string, string[]> People = new()
    {
        ["XY"] = ["Profesor Ciprés", "Serena", "Xana", "Lysson", "Equipo Flare"],
        ["ORAS"] = ["Profesor Abedul", "Máximo", "Blasco", "Magno", "Aquiles", "Equipo Magma", "Equipo Aqua"],
        ["SM"] = ["Profesor Kukui", "Lylia", "Tilo", "Gladio", "Samina", "Fundación Æther", "Equipo Skull"],
        ["USUM"] = ["Profesor Kukui", "Lylia", "Tilo", "Gladio", "Samina", "Fundación Æther", "Equipo Skull", "Unidad Ultra", "Darius", "Miria"]
    };
    public static string Normalize(string text)
    {
        text = Regex.Replace(text, @"\[VAR[^\]]*\]", " ", RegexOptions.IgnoreCase);
        text = text.Replace("\\n", " ").Replace("\\r", " ");
        var output = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                output.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return Regex.Replace(output.ToString(), @"\s+", " ").Trim();
    }
    private static bool Contains(string line, string normalizedTerm) => (" " + line + " ").Contains(normalizedTerm, StringComparison.Ordinal);
    public static TextContentClassification Classify(string game, IReadOnlyList<string> lines)
    {
        var evidence = new List<TextContentEvidence>();
        var normalized = lines.Select(Normalize).ToArray();
        string[] readable=lines.Select(t=>Regex.Replace(t,@"\[VAR[^\]]*\]|\[~[^\]]*\]|\\[nrc]", " ")).ToArray();
        if(readable.All(t=>string.IsNullOrWhiteSpace(t)||t.Trim().Equals("n/a",StringComparison.OrdinalIgnoreCase)))
        {
            string category=lines.Any(t=>t.Contains("[VAR",StringComparison.Ordinal))?"Solo códigos de control":"Vacíos o reservados";
            return new([new(category,-1,"Sin mensajes legibles")]);
        }
        foreach (var rule in Common) Add(rule.Category, rule.Terms);
        if (Places.TryGetValue(game, out var places)) Add("Lugares mencionados", places);
        if (People.TryGetValue(game, out var people)) Add("Personajes mencionados", people);
        var visible = normalized.Select((text, line) => (text, line)).Where(x => x.text.Length > 0).ToArray();
        if (visible.Length >= 3 && visible.All(x => Regex.IsMatch(x.text, @"^(caja|box) \d+$")))
            evidence.Add(new("Cajas del PC", visible[0].line, lines[visible[0].line]));
        return new(evidence);
        void Add(string category, IEnumerable<string> terms)
        {
            foreach (string term in terms)
            {
                string needle = " " + Normalize(term) + " ";
                int line = Array.FindIndex(normalized, text => Contains(text, needle));
                if (line >= 0) evidence.Add(new(category, line, term));
            }
        }
    }
}
