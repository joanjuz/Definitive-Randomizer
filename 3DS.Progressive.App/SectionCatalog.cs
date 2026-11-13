namespace ThreeDS.Progressive;

/// <summary>One destination of the 3DS editor: sidebar section, hub or hub tool.</summary>
internal sealed record SectionInfo(string Route,string Label,string? Hub,string Keywords,bool Simple=false);

/// <summary>
/// Single catalog of the 3DS sections: name, hub that contains the tool, search keywords and whether it is shown in
/// simplified mode. The sidebar, the search palette, the breadcrumbs, the guided tour, the change marks and the history
/// filters read it, so adding a section is done here once. Availability per game stays in ProjectFeatureAvailability.
/// </summary>
internal static class SectionCatalog
{
    internal static readonly IReadOnlyList<SectionInfo> All=
    [
        new("Pokemon","Pokémon",null,"estadisticas stats bst tipos habilidades especies personal base captura ev",true),
        new("Moves","Movimientos",null,"ataques poder precision pp categoria descripcion nombre",true),
        new("LevelUp","Level Up Moves",null,"aprendizaje learnset movimientos por nivel",true),
        new("MachineTutor","MT y tutores",null,"mt mo tm hm tutores maquinas compatibilidad",true),
        new("Evolutions","Evoluciones",null,"evolucionar evo metodo intercambio",true),
        new("PokemonTools","Herramientas Pokémon",null,"hub huevo mega tipos shiny pickup instalaciones"),
        new("Trainers","Entrenadores",null,"rivales lideres gimnasio alto mando jefes caps niveles equipos ia dobles",true),
        new("Encounters","Encuentros",null,"salvajes hierba wild sos zonas",true),
        new("StaticEncounters","Encuentros estáticos",null,"regalos iniciales starters legendarios totem gift",true),
        new("FieldItems","Field Items",null,"objetos del mapa suelo ocultos recoger",true),
        new("Marts","Tiendas",null,"tienda comprar bp precios inventario",true),
        new("Items","Objetos y economía",null,"precios economia dinero fix economy"),
        new("GameText","Game Text",null,"textos idioma nombres frases"),
        new("StoryText","Story Text",null,"dialogos escenas historia"),
        new("Mechanics","Mecánicas y CRO",null,"parches codigo obediencia megas tutores intercambios"),
        new("RomTools","Herramientas ROM",null,"rom archivos extraer rebuild"),
        new("Automation","Automatización",null,"flujos randomizar todo"),
        new("Configuration","Opciones e idioma",null,"idioma carpetas preferencias about garc",true),
        new("TemplateStudio","Templates",null,"plantillas csv json crear"),
        new("GlobalTemplate","Global Template",null,"receta definitivo json repetir"),
        new("BatchRom","Batch ROM Builder",null,"lote varias roms semillas"),
        new("History","Historial de cambios",null,"registro cambios sha deshacer",true),
        new("FacilityNormal","Instalaciones · Normal","PokemonTools","instalaciones batalla"),
        new("FacilityAlternate","Instalaciones · Alternativa","PokemonTools","instalaciones batalla"),
        new("EggMoves","Egg Moves","PokemonTools","huevo crianza"),
        new("MegaEvolutions","Mega Evolutions","PokemonTools","mega piedras megaevolucion"),
        new("TypeChart","Tabla de tipos","PokemonTools","tipos efectividad tabla debilidades"),
        new("ShinyRate","Shiny Rate","PokemonTools","variocolor shiny brillante probabilidad"),
        new("Pickup","Pickup","PokemonTools","recogida habilidad"),
        new("OWSE","OWSE","PokemonTools","mapas scripts dev"),
        new("Tutor6","Tutores BP · ORAS","Mechanics","tutores bp"),
        new("PlayerCaps","Player Level Caps","Mechanics","limite nivel jugador level cap"),
        new("UsumRelearner","USUM Relearner","Mechanics","recordar movimientos cafeteria"),
        new("CroRecipes","CRO Recipes","Mechanics","mecanicas recetas battle cro"),
        new("Trade6","Intercambios · Gen6","Mechanics","intercambios ofertas"),
        new("CatchZones","Catch Zones","Mechanics","zonas captura"),
        new("OPower","O-Powers","Mechanics","poderes"),
        new("PersistentItems","Persistent Items","Mechanics","consumibles objetos tras el combate"),
        new("MegaAbilitySync","Mega Ability Sync","Mechanics","megaevoluciones habilidades combates entre dos juegos multijugador"),
        new("Extraction","Extraer ROM","RomTools","extraer descifrar cxi 3ds"),
        new("Rebuild","Rebuild","RomTools","generar rom exportar reconstruir 3ds romfs exefs"),
        new("CroIntegrity","Integridad CRO/CRR","RomTools","hashes crr integridad"),
        new("RestoreSelected","Restaurar archivos","RomTools","restaurar archivos backup"),
        new("TitleScreen6","Pantalla de título","RomTools","logo titulo portada"),
        new("TitleScreen7","Pantalla de título","RomTools","logo titulo portada animacion"),
        new("GarcShuffle","GARC Shuffle","RomTools","mezclar"),
        new("Graphics","Gráficos","RomTools","imagenes bclim bflim png"),
        new("SMDH","Icono SMDH","RomTools","icono nombre del juego metadatos"),
    ];
    private static readonly IReadOnlyDictionary<string,SectionInfo> ByRoute=All.ToDictionary(s=>s.Route);
    internal static SectionInfo? Find(string route)=>ByRoute.GetValueOrDefault(route);
    internal static string Label(string route)=>ByRoute.TryGetValue(route,out var info)?info.Label:route;
    internal static string Keywords(string route)=>ByRoute.TryGetValue(route,out var info)?info.Keywords:"";
    /// <summary>Tools opened from a hub card, and the hub that contains them.</summary>
    internal static readonly IReadOnlyDictionary<string,string> HubOf=All.Where(s=>s.Hub is not null).ToDictionary(s=>s.Route,s=>s.Hub!);
    /// <summary>Routes shown in simplified mode (the tour switches to full mode for the others).</summary>
    internal static readonly IReadOnlySet<string> SimpleRoutes=All.Where(s=>s.Simple).Select(s=>s.Route).ToHashSet();
}
