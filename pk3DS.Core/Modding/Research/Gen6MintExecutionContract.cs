using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace pk3DS.Core.Modding.Research;

/// <summary>Read-only execution admission shared by WPF and the legacy runner.
/// No native Gen6 installer has been accredited: an opt-in can be saved but cannot execute.</summary>
public static class Gen6MintExecutionContract
{
    public const string Node = "Gen6MintItems";
    public const string BlockedReason = "Contrato nativo y 21 IDs aún sin acreditar";

    public static bool Read(string json)
    {
        try { using var document=JsonDocument.Parse(json);return Read(document.RootElement); }
        catch(JsonException ex){throw new InvalidDataException("Contrato de mentas Gen6 con JSON inválido.",ex);}
    }
    public static bool Read(JsonElement document)
    {
        if(document.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("La receta debe ser un objeto JSON.");
        Unique(document,"ProgressiveApp");
        if(!document.TryGetProperty("ProgressiveApp",out var native))return false;
        if(native.ValueKind==JsonValueKind.Null)return false; // Legacy templates may serialize an absent extension as null.
        if(native.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("ProgressiveApp debe ser un objeto.");
        Unique(native,Node);
        if(!native.TryGetProperty(Node,out var node))return false;
        if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Gen6MintItems debe ser un objeto de opciones.");
        Unique(node,"Include");
        if(!node.TryGetProperty("Include",out var include)||include.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Gen6MintItems requiere una opción Include booleana.");
        if(!include.GetBoolean())return false;
        Unique(document,"Game");Unique(document,"Generation");
        if(!document.TryGetProperty("Game",out var game)||game.ValueKind!=JsonValueKind.String||
            !string.Equals(game.GetString(),"XY",StringComparison.OrdinalIgnoreCase)||
            !document.TryGetProperty("Generation",out var generation)||generation.ValueKind!=JsonValueKind.Number||
            !generation.TryGetInt32(out int value)||value!=6)
            throw new InvalidDataException("La receta de mentas Gen6 requiere Pokémon X/Y.");
        return true;
    }
    public static void ValidateForExecution(string json)
    {
        if(Read(json))throw new InvalidDataException(BlockedReason);
    }
    private static void Unique(JsonElement obj,string name)
    {
        if(obj.EnumerateObject().Count(p=>p.NameEquals(name))>1)
            throw new InvalidDataException("Campo duplicado en el contrato de mentas Gen6: "+name);
    }
}
