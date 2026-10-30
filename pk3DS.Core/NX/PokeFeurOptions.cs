#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace pk3DS.Core.NX;
public enum PokeFeurVisibleMode { Vanilla, Random, Zone, Global, Discovery, RandomDiscovery }
public enum PokeFeurBattleMode { Vanilla, Random, Zone, Global }
public enum PokeFeurCapMode { StoryGymLeague, ReceiverVsDefeated }
public enum PokeFeurFeature { Underworld, OverworldShiny, Camera, UncapLevel, FreeCamera, Glimwood, Synchronization, Following, Fishing, DexAnimations, EarlyBike, PcMenu, RuntimeStatus }
public sealed record PokeFeurFeatureInfo(PokeFeurFeature Feature,string Section,string Label,string Description,string? Script=null);
public sealed record PokeFeurOptions
{
    public ulong Seed {get;init;}=SwitchSeed.New64();
    public bool Randomizer {get;init;}
    public PokeFeurVisibleMode Visible {get;init;}=PokeFeurVisibleMode.Vanilla;
    public PokeFeurBattleMode Battle {get;init;}=PokeFeurBattleMode.Random;
    public IReadOnlyList<int> VisiblePool {get;init;}=Array.Empty<int>();
    public IReadOnlyList<int> BattlePool {get;init;}=Array.Empty<int>();
    public bool Caps {get;init;}
    public PokeFeurCapMode CapMode {get;init;}
    public bool CapsFromTrainers {get;init;}
    public IReadOnlyList<int> StoryCaps {get;init;}=Array.AsReadOnly(new[]{20,24,27,36,38,42,46,48,65});
    public bool ExpRuntime {get;init;}
    public double ExpMultiplier {get;init;}=1.0;
    public bool ExpShare {get;init;}=true;
    public IReadOnlyList<PokeFeurFeature> Features {get;init;}=Array.Empty<PokeFeurFeature>();
    public int ShinyBoost {get;init;}
    public bool FullyUncap {get;init;}=true;
    public int GlimwoodSpawns {get;init;}=1;
    public bool KeepFishingChain {get;init;}=true;
    public bool Enabled=>Randomizer||Caps||ExpRuntime||!ExpShare||Features.Count>0;
    public static readonly IReadOnlyList<PokeFeurFeatureInfo> Catalog=Array.AsReadOnly(new[]{
        new PokeFeurFeatureInfo(PokeFeurFeature.Underworld,"underworld","Underworld · experimental","Activa un parche adicional del módulo original. El contrato disponible no documenta con suficiente precisión su efecto jugable: dejar desmarcado hasta investigarlo. No es necesario para los combates sorpresa."),
        new PokeFeurFeatureInfo(PokeFeurFeature.OverworldShiny,"overworld_shiny","Shinies visibles","Representación de shinies y auras; bonificación configurable, sin cuadro de mensaje."),
        new PokeFeurFeatureInfo(PokeFeurFeature.Camera,"camera_tweaks","Ajustes de cámara","Ajustes de la cámara del runtime con sus valores predeterminados."),
        new PokeFeurFeatureInfo(PokeFeurFeature.UncapLevel,"uncap_level","Retirar límite de nivel del Área Silvestre","No es el level cap de EXP. Permite configurar el parche propio del Área Silvestre."),
        new PokeFeurFeatureInfo(PokeFeurFeature.FreeCamera,"freecam","Cámara libre","Control de cámara libre del runtime; probar controles dentro del juego."),
        new PokeFeurFeatureInfo(PokeFeurFeature.Glimwood,"glimwood_overworld","Encuentros visibles en Bosque Lumirinto","Límite de spawns configurable de 1 a 20."),
        new PokeFeurFeatureInfo(PokeFeurFeature.Synchronization,"synchro_mode","Sincronización de naturaleza","Parche de sincronización de naturaleza de los encuentros. No controla el movimiento del Pokémon. Su efecto concreto debe validarse dentro del juego."),
        new PokeFeurFeatureInfo(PokeFeurFeature.Following,"extended_following","Pokémon acompañante ampliado","Amplía la función de Pokémon que te sigue por el escenario. Su alcance y controles dependen del módulo integrado; requiere validación jugable."),
        new PokeFeurFeatureInfo(PokeFeurFeature.Fishing,"fishing_tweaks","Mejoras de pesca","Permite conservar la cadena después de un encuentro brillante."),
        new PokeFeurFeatureInfo(PokeFeurFeature.DexAnimations,"dex_animations","Animaciones de Pokédex","Requiere script AMX y registro generado desde tu extracción.","bin/script/amx/dex_animations.amx"),
        new PokeFeurFeatureInfo(PokeFeurFeature.EarlyBike,"early_bike","Bicicleta temprana","Requiere script AMX y registro generado desde tu extracción.","bin/script/amx/early_bike.amx"),
        new PokeFeurFeatureInfo(PokeFeurFeature.PcMenu,"runtime_pc","Opciones del mod en el PC del juego","Añade al PC de los Centros Pokémon un menú para consultar y cambiar opciones como repartir EXP y el multiplicador de experiencia durante la partida. Modifica el registro de scripts.","bin/script/amx/randomizer_pc_menu.amx"),
        new PokeFeurFeatureInfo(PokeFeurFeature.RuntimeStatus,"runtime_status","Mostrar estado del mod en el juego","Activa la información de estado del randomizador dentro del juego. Es una ayuda de diagnóstico; no randomiza encuentros ni cambia la EXP por sí sola.")});
    // Preserve the serialized enum ID and TOML section, but quarantine the confirmed slowdown trigger.
    public static bool IsFeatureAvailable(PokeFeurFeature feature)=>feature!=PokeFeurFeature.DexAnimations;
    public static IReadOnlyList<PokeFeurFeatureInfo> AvailableCatalog {get;}=Array.AsReadOnly(Catalog.Where(f=>IsFeatureAvailable(f.Feature)).ToArray());
    public void ValidateFeatureAvailability()
    {
        if(Features is null)throw new InvalidDataException("Lista de funciones ausente.");
        if(Features.Contains(PokeFeurFeature.DexAnimations))throw new InvalidDataException("Animaciones de Pokédex está retirada temporalmente porque provoca lentitud extrema. Desactiva esa opción en una copia del template para continuar.");
    }
    public PokeFeurOptions Normalize(IEnumerable<int> present)
    {
        ValidateFeatureAvailability();
        if(Seed>long.MaxValue)throw new InvalidDataException("La semilla TOML debe estar entre 0 y 9223372036854775807.");
        if(!Enum.IsDefined(Visible)||!Enum.IsDefined(Battle)||!Enum.IsDefined(CapMode))throw new InvalidDataException("Modo de runtime desconocido.");
        if(VisiblePool is null||BattlePool is null||StoryCaps is null||Features is null)throw new InvalidDataException("Listas de configuración ausentes.");
        if(Features.Any(f=>!Enum.IsDefined(f))||Features.Distinct().Count()!=Features.Count)throw new InvalidDataException("Función desconocida o duplicada.");
        if(!double.IsFinite(ExpMultiplier)||ExpMultiplier<0.01||ExpMultiplier>100)throw new InvalidDataException("El multiplicador EXP debe estar entre 0,01 y 100.");
        if(ShinyBoost<0||ShinyBoost>100||GlimwoodSpawns<1||GlimwoodSpawns>20)throw new InvalidDataException("Bonificación shiny: 0–100; spawns: 1–20.");
        if(StoryCaps.Count!=9||StoryCaps.Any(c=>c<1||c>100))throw new InvalidDataException("Introduce nueve level caps entre 1 y 100.");
        int[] available=present.Distinct().Order().ToArray();if(available.Length==0||available.Any(i=>i<1||i>898))throw new InvalidDataException("No hay especies presentes válidas.");
        IReadOnlyList<int> Pool(IReadOnlyList<int> chosen){var a=(chosen.Count==0?available:chosen.ToArray());if(a.Length>1024||a.Distinct().Count()!=a.Length||a.Any(i=>Array.BinarySearch(available,i)<0))throw new InvalidDataException("El pool contiene duplicados o especies no presentes.");return Array.AsReadOnly(a.Order().ToArray());}
        return this with{VisiblePool=Pool(VisiblePool),BattlePool=Pool(BattlePool),StoryCaps=Array.AsReadOnly(StoryCaps.ToArray()),Features=Array.AsReadOnly(Features.Order().ToArray())};
    }
    public string Config()
    {
        ValidateFeatureAvailability();
        var b=new StringBuilder("# Definitive Randomizer · backend externo PokeFeur · Espada 1.3.2\n# Copiar a sdcard/config/swsh-mods-exl/config.toml; no se lee desde la carpeta del mod.\n");
        string Bool(bool v)=>v?"true":"false";
        b.Append($"\n[randomizer]\nactive = {Bool(Randomizer)}\nvisible_mode = \"{new[]{"vanilla","random","zone","global","decouverte","decouverte_random"}[(int)Visible]}\"\nbattle_mode = \"{Battle.ToString().ToLowerInvariant()}\"\nvisible_pool = [{string.Join(", ",VisiblePool)}]\nbattle_pool = [{string.Join(", ",BattlePool)}]\nseed = {Seed.ToString(CultureInfo.InvariantCulture)}\nreset_discoveries = false\n");
        b.Append($"\n[exp_level_cap]\nactive = {Bool(Caps)}\nmode = \"{(CapMode==PokeFeurCapMode.StoryGymLeague?"story_gym_league":"receiver_vs_defeated")}\"\nstory_caps = [{string.Join(", ",StoryCaps)}]\n\n[exp_runtime]\nactive = {Bool(ExpRuntime)}\nenabled = {Bool(ExpRuntime)}\nmultiplier = {ExpMultiplier.ToString("0.0######",CultureInfo.InvariantCulture)}\n\n[exp_share]\nenabled = {Bool(ExpShare)}\n");
        foreach(var f in Catalog){b.Append($"\n[{f.Section}]\nactive = {Bool(Features.Contains(f.Feature))}\n");switch(f.Feature){case PokeFeurFeature.OverworldShiny:b.Append($"boosted_percentage = {ShinyBoost}\nshow_message_box = false\n");break;case PokeFeurFeature.UncapLevel:b.Append($"fully = {Bool(FullyUncap)}\n");break;case PokeFeurFeature.Glimwood:b.Append($"maximum_spawns = {GlimwoodSpawns}\n");break;case PokeFeurFeature.Fishing:b.Append($"keep_chain_after_brilliant = {Bool(KeepFishingChain)}\n");break;}}
        return b.ToString();
    }
    private sealed record Recipe(int Version,string Provider,string TitleId,PokeFeurOptions Options);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public string RecipeJson(){ValidateFeatureAvailability();return JsonSerializer.Serialize(new Recipe(1,"PokeFeur","0100ABF008968000",this),Json);}
    public static PokeFeurOptions ReadRecipe(string json)
    {if(json.Length>200000)throw new InvalidDataException("Receta demasiado grande.");Recipe recipe;try{recipe=JsonSerializer.Deserialize<Recipe>(json,Json)??throw new InvalidDataException("Receta vacía.");}catch(JsonException e){throw new InvalidDataException("La receta contiene campos desconocidos o JSON inválido.",e);}if(recipe.Version!=1||recipe.Provider!="PokeFeur"||recipe.TitleId!="0100ABF008968000")throw new InvalidDataException("Receta incompatible con este backend de Espada.");var options=recipe.Options??throw new InvalidDataException("Faltan opciones.");options.ValidateFeatureAvailability();return options;}
}
