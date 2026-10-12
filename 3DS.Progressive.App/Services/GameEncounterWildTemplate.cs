using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Mirrors the v1 format used by WinForms WildRandomizerTemplateFile.
/// Unknown JSON properties survive a load/edit/save roundtrip.
/// </summary>
internal sealed class WildTemplateV1
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Wild Encounter randomizer";
    public string Game { get; set; } = "ANY";
    public bool G1 { get; set; } = true;
    public bool G2 { get; set; } = true;
    public bool G3 { get; set; } = true;
    public bool G4 { get; set; } = true;
    public bool G5 { get; set; } = true;
    public bool G6 { get; set; } = true;
    public bool G7 { get; set; } = true;
    public bool Legendaries { get; set; }
    public bool Events { get; set; }
    public bool MegaForms { get; set; }
    public bool SimilarBST { get; set; }
    public bool AllCanCallAllies { get; set; }
    public int SlotRandomizationOption { get; set; }
    public bool ModifyLevel { get; set; }
    public decimal LevelAmplifier { get; set; } = 1m;
    public int AdvancedLevelFlat { get; set; }
    public int AdvancedLevelMultiplier { get; set; } = 100;
    public bool AdvancedKeepRange { get; set; } = true;
    public WildProgressiveV1 ProgressiveBST { get; set; } = new();
    [JsonExtensionData] public Dictionary<string,JsonElement>? Extra { get; set; }
}
internal sealed class WildProgressiveV1
{
    public bool Enabled { get; set; }
    public List<WildRangeV1> Ranges { get; set; } = [];
    [JsonExtensionData] public Dictionary<string,JsonElement>? Extra { get; set; }
}
internal sealed class WildRangeV1
{
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MinBST { get; set; }
    public int MaxBST { get; set; }
    public bool FullRandom { get; set; }
    [JsonExtensionData] public Dictionary<string,JsonElement>? Extra { get; set; }
}

public sealed partial class GameEncounterService
{
    private static readonly JsonSerializerOptions WildJson = new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive=true,
        ReadCommentHandling=JsonCommentHandling.Skip,
        AllowTrailingCommas=true,
        WriteIndented=true
    };
    private WildTemplateV1? _wildTemplate;
    private IReadOnlyDictionary<string,int>? _wildTemplateDefaults;
    internal bool SupportsWildTemplate => Config.Generation == 7;

    private string WildGame => Config.USUM?"USUM":"SM";
    private static string NormalizeGame(string? game) =>
        new string((game??"").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public void ImportOptions(string path) => RestoreOptions(File.ReadAllText(path));
    public void ExportOptions(string path,IReadOnlyDictionary<string,int> options) =>
        File.WriteAllText(path,CaptureOptions(options),System.Text.Encoding.UTF8);

    public void RestoreOptions(string json)
    {
        if(!SupportsWildTemplate)
            throw new NotSupportedException("Templates Wild Progressive v1 requieren Sol/Luna o Ultra Sol/Ultra Luna.");
        WildTemplateV1 candidate;
        try { candidate=JsonSerializer.Deserialize<WildTemplateV1>(json,WildJson)
                ??throw new InvalidDataException("El template Wild está vacío."); }
        catch(JsonException ex)
        { throw new InvalidDataException("El template Wild no contiene un JSON válido.",ex); }
        ValidateWildTemplate(candidate,WildGame);
        var defaults=new Dictionary<string,int>
        {
            ["G1"]=candidate.G1?1:0,["G2"]=candidate.G2?1:0,
            ["G3"]=candidate.G3?1:0,["G4"]=candidate.G4?1:0,
            ["G5"]=candidate.G5?1:0,["G6"]=candidate.G6?1:0,
            ["G7"]=candidate.G7?1:0,
            ["Legends"]=candidate.Legendaries?1:0,["Events"]=candidate.Events?1:0,
            ["MegaForms"]=candidate.MegaForms?1:0,
            ["SimilarBST"]=candidate.SimilarBST?1:0,
            ["AllCanCallAllies"]=candidate.AllCanCallAllies?1:0,
            ["SlotMode"]=candidate.SlotRandomizationOption,
            ["SOS"]=1,
            ["Weather"]=candidate.SlotRandomizationOption is 0 or 2 ?1:0,
            ["Progressive"]=candidate.ProgressiveBST.Enabled?1:0,
            ["LevelFlat"]=candidate.AdvancedLevelFlat,
            ["LevelScale"]=candidate.ModifyLevel
                ?Math.Clamp((int)Math.Round(candidate.LevelAmplifier*100m),10,300)
                :candidate.AdvancedLevelMultiplier,
            ["KeepRange"]=candidate.AdvancedKeepRange?1:0
        };
        // Only replace the active options after validating the entire document.
        if(candidate.ProgressiveBST.Ranges.Count>0)
        {
            _bstRanges=candidate.ProgressiveBST.Ranges
                .OrderBy(r=>r.MinLevel)
                .Select(r=>(r.MinLevel,r.MaxLevel,r.MinBST,r.MaxBST,r.FullRandom)).ToList();
        }
        _wildTemplate=candidate;
        _wildTemplateDefaults=defaults;
    }

    public string CaptureOptions(IReadOnlyDictionary<string,int> options)
    {
        if(!SupportsWildTemplate)
            throw new NotSupportedException("Templates Wild solo soportan Gen7.");
        bool On(string key,bool fallback=false)=>options.TryGetValue(key,out int v)?v==1:fallback;
        int Value(string key,int fallback)=>options.TryGetValue(key,out int v)?v:fallback;
        var template=_wildTemplate is null
            ?new WildTemplateV1 {Game=WildGame,Name=WildGame+" · Progressive Wild"}
            :JsonSerializer.Deserialize<WildTemplateV1>(
                JsonSerializer.Serialize(_wildTemplate,WildJson),WildJson)!;
        template.Version=1;
        template.G1=On("G1");template.G2=On("G2");template.G3=On("G3");
        template.G4=On("G4");template.G5=On("G5");template.G6=On("G6");
        template.G7=On("G7");
        template.Legendaries=On("Legends");
        template.Events=On("Events");
        template.MegaForms=On("MegaForms");
        template.SimilarBST=On("SimilarBST");
        template.AllCanCallAllies=On("AllCanCallAllies");
        template.SlotRandomizationOption=Value("SlotMode",0);
        template.AdvancedLevelFlat=Value("LevelFlat",0);
        template.AdvancedLevelMultiplier=Value("LevelScale",100);
        template.AdvancedKeepRange=On("KeepRange",true);
        template.ModifyLevel=template.AdvancedLevelMultiplier!=100||template.AdvancedLevelFlat!=0;
        template.LevelAmplifier=template.AdvancedLevelMultiplier/100m;
        template.ProgressiveBST??=new();
        template.ProgressiveBST.Enabled=On("Progressive");
        var indexes=options.Keys.Where(k=>k.StartsWith("BST.",StringComparison.Ordinal))
            .Select(k=>k.Split('.')).Where(p=>p.Length==3&&int.TryParse(p[1],out _))
            .Select(p=>int.Parse(p[1],CultureInfo.InvariantCulture))
            .Distinct().Order().ToArray();
        if(indexes.Length>0)
        {
            template.ProgressiveBST.Ranges=indexes.Select(index=>
            {
                string k=$"BST.{index}.";
                return new WildRangeV1
                {
                    MinLevel=Value(k+"MinLevel",0),MaxLevel=Value(k+"MaxLevel",0),
                    MinBST=Value(k+"MinBST",0),MaxBST=Value(k+"MaxBST",0),
                    FullRandom=On(k+"FullRandom")
                };
            }).ToList();
        }
        ValidateWildTemplate(template,WildGame);
        return JsonSerializer.Serialize(template,WildJson);
    }

    // Preserves the level-BST editor's normal row rendering while loading v1.
    private int WildDefault(string key,int fallback) =>
        _wildTemplateDefaults is not null&&_wildTemplateDefaults.TryGetValue(key,out int value)
            ?value:fallback;

    private static void ValidateWildTemplate(WildTemplateV1 t,string game)
    {
        if(t.Version!=1)throw new InvalidDataException("Wild template: solo se admite versión 1.");
        string claimed=NormalizeGame(t.Game);
        if(claimed is not ("" or "ANY") && claimed!=NormalizeGame(game))
            throw new InvalidDataException($"El template Wild corresponde a {t.Game}, no a {game}.");
        if(t.SlotRandomizationOption is <0 or >3)
            throw new InvalidDataException("SlotRandomizationOption debe estar entre 0 y 3.");
        if(t.LevelAmplifier is <0.10m or >3.00m)
            throw new InvalidDataException("LevelAmplifier debe estar entre 0.10 y 3.00.");
        if(t.AdvancedLevelFlat is <-100 or >100 ||
           t.AdvancedLevelMultiplier is <10 or >300)
            throw new InvalidDataException("Los parámetros avanzados de nivel superan los límites del editor.");
        if(t.ProgressiveBST is null)
            throw new InvalidDataException("Falta ProgressiveBST.");
        var ranges=t.ProgressiveBST.Ranges??[];
        if(t.ProgressiveBST.Enabled && ranges.Count==0)
            throw new InvalidDataException("ProgressiveBST está habilitado sin rangos.");
        if(ranges.Count==0)return;
        var sorted=ranges.OrderBy(x=>x.MinLevel).ToArray();
        if(sorted[0].MinLevel!=1||sorted[^1].MaxLevel!=100)
            throw new InvalidDataException("Los rangos Progressive Wild deben abarcar niveles 1–100.");
        for(int i=0;i<sorted.Length;i++)
        {
            var r=sorted[i];
            if(r.MinLevel<1||r.MaxLevel>100||r.MinLevel>r.MaxLevel||
                (!r.FullRandom&&(r.MinBST<1||r.MaxBST>999||r.MinBST>r.MaxBST)))
                throw new InvalidDataException($"Rango Progressive Wild inválido: {i+1}.");
            if(i>0&&r.MinLevel!=sorted[i-1].MaxLevel+1)
                throw new InvalidDataException("Los rangos BST tienen huecos o solapamientos.");
        }
    }

    /// <summary>Deterministic equivalent to Gen7 FormRandomizer's wild rules.</summary>
    private int PickWildForm(int species,Random random,bool allowMega)
    {
        var stats=Config.Personal.Table;
        if(species<1||species>=stats.Length)return 0;
        int forms=stats[species].FormeCount;
        if(forms<=1)return 0;
        if(species==658&&!allowMega)return 0;
        if(species is 664 or 665 or 666)return 30;
        if(species==774)return random.Next(7);
        if(Legal.EvolveToAlolanForms.Contains(species))return random.Next(2);
        if(Legal.BattleExclusiveForms.Contains(species)&&!allowMega)return 0;
        return random.Next(Math.Min(forms,32));
    }
}
