using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;

/// <summary>
/// Reads the table of a recorded "playercaps" manual operation ({"Table":{"Entries":[{Label,Kind,FlagOffset,FlagBit,Cap}]}}),
/// exactly as <see cref="GamePlayerLevelCapService"/> has always written it. LevelCapEntry has two constructors, so the
/// serializer cannot build it on its own; this reader is explicit and refuses anything it does not fully understand
/// before a single byte is prepared. It never changes what is written to the history or to a template.
/// </summary>
internal static class PlayerLevelCapRecipe
{
    internal static LevelCapTable Read(JsonObject payload)
    {
        if (payload["Table"] is not JsonObject table || table["Entries"] is not JsonArray rows)
            throw new InvalidDataException("Player Level Caps: la receta no incluye la tabla de puntos.");
        if (rows.Count == 0 || rows.Count > LevelCapTable.MaxEntries)
            throw new InvalidDataException($"Player Level Caps: la receta debe tener entre 1 y {LevelCapTable.MaxEntries} puntos.");
        var entries = new List<LevelCapEntry>(rows.Count);
        foreach (var row in rows)
        {
            if (row is not JsonObject point) throw new InvalidDataException("Player Level Caps: punto de la receta no válido.");
            int kind = Number(point, "Kind", 255);
            if (!Enum.IsDefined((LevelCapConditionKind)kind))
                throw new InvalidDataException("Player Level Caps: tipo de condición desconocido en la receta: " + kind);
            // The name is not part of the installed table: a point saved without one keeps an empty name.
            string label = point["Label"] switch
            {
                null => "",
                JsonValue text when text.GetValueKind() == JsonValueKind.String => text.GetValue<string>(),
                _ => throw new InvalidDataException("Player Level Caps: nombre de punto no válido en la receta."),
            };
            entries.Add(new LevelCapEntry(label, (LevelCapConditionKind)kind,
                (ushort)Number(point, "FlagOffset", ushort.MaxValue), (ushort)Number(point, "FlagBit", ushort.MaxValue), (byte)Number(point, "Cap", byte.MaxValue)));
        }
        var result = new LevelCapTable { Entries = entries };
        if (result.Validate() is { Count: > 0 } problems) throw new InvalidDataException("Player Level Caps: " + problems[0]);
        return result;
    }

    /// <summary>A plain non-negative integer within range: no fractions, exponents, signs or numbers written as text.</summary>
    private static int Number(JsonObject point, string field, int max)
    {
        if (point[field] is JsonValue value && value.GetValueKind() == JsonValueKind.Number &&
            int.TryParse(value.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number <= max)
            return number;
        throw new InvalidDataException("Player Level Caps: valor no válido en la receta: " + field);
    }
}
