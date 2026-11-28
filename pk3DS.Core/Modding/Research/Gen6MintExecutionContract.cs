using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

public sealed record Gen6MintRecipeOptions(bool InstallMints,bool InstallIcons,bool IncludeMarts,bool IncludeFieldItems);

/// <summary>Structural contract; native admission still requires title-specific dependency validation.</summary>
public static class Gen6MintExecutionContract
{
    public const string Node = "Gen6MintItems";
    public const string InstallAction="xy.nature-mints.install",IconsAction="xy.nature-mints.icons.install";
    public const string ReplaySeedEnvironment="DEFINITIVE_XY_REPLAY_SEED";
    public static int TrainerSeed(int romSeed)=>romSeed>=0?romSeed^0x58594D54:throw new ArgumentOutOfRangeException(nameof(romSeed));
    public const string BlockedReason = "Se requiere una instalación nativa de mentas X/Y acreditada.";

    public static bool Read(string json)
    {
        var o=ReadOptions(json);return o.IncludeMarts||o.IncludeFieldItems;
    }
    public static bool Read(JsonElement document){var o=ReadOptions(document);return o.IncludeMarts||o.IncludeFieldItems;}
    public static Gen6MintRecipeOptions ReadOptions(string json)
    {
        try { using var document=JsonDocument.Parse(json);return ReadOptions(document.RootElement); }
        catch(JsonException ex){throw new InvalidDataException("Contrato de mentas Gen6 con JSON inválido.",ex);}
    }
    public static Gen6MintRecipeOptions ReadOptions(JsonElement document)
    {
        if(document.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("La receta debe ser un objeto JSON.");
        Unique(document,"ProgressiveApp");Unique(document,"Actions");bool marts=false,field=false,install=false,icons=false;
        if(document.TryGetProperty("ProgressiveApp",out var native)&&native.ValueKind!=JsonValueKind.Null)
        {
            if(native.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("ProgressiveApp debe ser un objeto.");Unique(native,Node);
            if(native.TryGetProperty(Node,out var node))
            {
                if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Gen6MintItems debe ser un objeto de opciones.");
                foreach(string name in new[]{"Include","IncludeMarts","IncludeFieldItems"})Unique(node,name);
                bool hasLegacy=node.TryGetProperty("Include",out var legacy),hasMarts=node.TryGetProperty("IncludeMarts",out var m),hasField=node.TryGetProperty("IncludeFieldItems",out var f);
                if(!hasLegacy&&!hasMarts&&!hasField)throw new InvalidDataException("Gen6MintItems requiere opciones booleanas.");
                bool Boolean(JsonElement v)=>v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():throw new InvalidDataException("Inclusión de mentas no booleana.");
                bool old=hasLegacy&&Boolean(legacy);marts=hasMarts?Boolean(m):old;field=hasField?Boolean(f):old;
                if(hasLegacy&&(hasMarts&&marts!=old||hasField&&field!=old))throw new InvalidDataException("Include antiguo contradice las opciones independientes.");
            }
        }
        if(document.TryGetProperty("Actions",out var actions)&&actions.ValueKind!=JsonValueKind.Null)
        {
            if(actions.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Actions debe ser una lista.");int mintCount=0,iconCount=0;
            foreach(var action in actions.EnumerateArray())
            {
                if(action.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Acción no válida.");Unique(action,"Id");
                if(!action.TryGetProperty("Id",out var id)||id.ValueKind!=JsonValueKind.String)throw new InvalidDataException("La acción requiere Id.");
                if(string.Equals(id.GetString(),InstallAction,StringComparison.OrdinalIgnoreCase))mintCount++;
                if(string.Equals(id.GetString(),IconsAction,StringComparison.OrdinalIgnoreCase))iconCount++;
            }
            if(mintCount>1||iconCount>1)throw new InvalidDataException("Acción de mentas duplicada.");install=mintCount==1;icons=iconCount==1;
        }
        if(marts||field||install||icons)
        {
            Unique(document,"Game");Unique(document,"Generation");
            if(!document.TryGetProperty("Game",out var game)||game.ValueKind!=JsonValueKind.String||!string.Equals(game.GetString(),"XY",StringComparison.OrdinalIgnoreCase)||
                !document.TryGetProperty("Generation",out var generation)||generation.ValueKind!=JsonValueKind.Number||!generation.TryGetInt32(out int value)||value!=6)
                throw new InvalidDataException("La receta de mentas Gen6 requiere Pokémon X/Y.");
        }
        return new(install,icons,marts,field);
    }
    public static void ValidateForExecution(string json)
    {
        _=ReadOptions(json);
    }
    /// <summary>Existing rng.seed action, scoped to XY. Index zero is the exact
    /// saved seed; subsequent ROMs use an odd-stride sequence modulo 2^31.</summary>
    public static int SeedForRom(string json,int index,int generated)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(!root.TryGetProperty("Game",out var game)||!string.Equals(game.GetString(),"XY",StringComparison.OrdinalIgnoreCase)||
            !root.TryGetProperty("Actions",out var actions)||actions.ValueKind!=JsonValueKind.Array)return generated;
        var rows=actions.EnumerateArray().Where(a=>a.TryGetProperty("Id",out var id)&&string.Equals(id.GetString(),"rng.seed",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(rows.Length==0)return generated;
        if(rows.Length!=1||index<0||!rows[0].TryGetProperty("Parameters",out var parameters)||!parameters.TryGetProperty("value",out var value)||
            !int.TryParse(value.ToString(),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out int seed)||seed<0)
            throw new InvalidDataException("rng.seed requiere Parameters.value entero entre 0 y 2147483647.");
        return (int)(unchecked((uint)seed+(uint)index*0x9E3779B9u)&0x7fffffffu);
    }
    private static void Unique(JsonElement obj,string name)
    {
        if(obj.EnumerateObject().Count(p=>p.NameEquals(name))>1)
            throw new InvalidDataException("Campo duplicado en el contrato de mentas Gen6: "+name);
    }
}
