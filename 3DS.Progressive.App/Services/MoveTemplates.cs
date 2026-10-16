// CSV schema and token normalization ported from CustomBalanceTemplates (f459e40).
#nullable disable
using System.Globalization;
using System.IO;
using System.Text;
namespace ThreeDS.Progressive.Services;
internal static class MoveTemplates
{
    internal sealed class MovePatchRow
    {
        public int Move { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Quality { get; init; } = string.Empty;
        public int? Power { get; init; }
        public int? Accuracy { get; init; }
        public int? PP { get; init; }
        public int? Priority { get; init; }
        public int? HitMin { get; init; }
        public int? HitMax { get; init; }
        public int? CriticalStage { get; init; }
        public int? Flinch { get; init; }
        public int? Effect { get; init; }
        public int? Param0x0B { get; init; }
        public int? Heal { get; init; }
        public int? Recoil { get; init; }
        public int? TurnMin { get; init; }
        public int? TurnMax { get; init; }
        public string Targeting { get; init; } = string.Empty;
        public int? Inflict { get; init; }
        public string InflictToken { get; init; } = string.Empty;
        public int? InflictChance { get; init; }
        public bool ClearStatEffects { get; init; }
        public string UserStat { get; init; } = string.Empty;
        public int? UserStatChange { get; init; }
        public int? UserStatChance { get; init; }
        public string TargetStat { get; init; } = string.Empty;
        public int? TargetStatChange { get; init; }
        public int? TargetStatChance { get; init; }
        public string Stat1 { get; init; } = string.Empty;
        public int? Stat1Change { get; init; }
        public int? Stat1Chance { get; init; }
        public string Stat2 { get; init; } = string.Empty;
        public int? Stat2Change { get; init; }
        public int? Stat2Chance { get; init; }
        public string Stat3 { get; init; } = string.Empty;
        public int? Stat3Change { get; init; }
        public int? Stat3Chance { get; init; }
        public bool ClearFlags { get; init; }
        public string SetFlags { get; init; } = string.Empty;
        public string UnsetFlags { get; init; } = string.Empty;
        public bool KingShieldAttackMinusOne { get; init; }

        public int? ZEffect { get; init; }
        public string BattlePatch { get; init; } = string.Empty;
        public string InGameDescription { get; init; } = string.Empty;
    }

    internal static MovePatchRow[] LoadMovePatches(string path, string[] moveNames)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No se encontró el template.", path);

        var rows = new List<MovePatchRow>();
        Dictionary<string, int> header = null;
        int lineNumber = 0;

        foreach (var fields in ReadCsv(path))
        {
            lineNumber++;
            if (fields.Length == 0)
                continue;

            string first = Get(fields, 0);
            if (first.Length == 0 || first.StartsWith('#'))
                continue;

            if (IsHeader(first, "Move"))
            {
                header = BuildHeaderMap(fields);
                continue;
            }

            string moveToken = GetField(fields, header, 0, "Move");
            int move = ResolveId(moveToken, moveNames);
            if (move <= 0) throw new InvalidDataException($"Template, fila {lineNumber}: movimiento desconocido {moveToken}.");

            string inflict = GetField(fields, header, 6, "Inflict", "InflictStatus", "Status");

            try { rows.Add(new MovePatchRow
            {
                Move = move,
                Type = GetField(fields, header, -1, "Type"),
                Category = GetField(fields, header, -1, "Category", "DamageCategory"),
                Quality = GetField(fields, header, -1, "Quality", "MoveQuality"),
                Power = ParseNullableInt(GetField(fields, header, 1, "Power", "BasePower")),
                Accuracy = ParseNullableInt(GetField(fields, header, 2, "Accuracy", "Acc")),
                PP = ParseNullableInt(GetField(fields, header, 3, "PP")),
                Priority = ParseNullableInt(GetField(fields, header, -1, "Priority")),
                HitMin = ParseNullableInt(GetField(fields, header, -1, "HitMin", "MinHits")),
                HitMax = ParseNullableInt(GetField(fields, header, -1, "HitMax", "MaxHits")),
                CriticalStage = ParseNullableInt(GetField(fields, header, 4, "CriticalStage", "CritStage", "Critical", "Crit")),
                Flinch = ParseNullableInt(GetField(fields, header, -1, "Flinch", "FlinchChance")),
                Effect = ParseNullableInt(GetField(fields, header, -1, "Effect", "EffectID")),
                Param0x0B = ParseNullableInt(GetField(fields, header, -1, "Param0x0B", "0x0B", "Byte0B", "Extra0x0B", "Raw0x0B")),
                Heal = ParseNullableInt(GetField(fields, header, 5, "Heal", "Drain")),
                Recoil = ParseNullableInt(GetField(fields, header, -1, "Recoil")),
                TurnMin = ParseNullableInt(GetField(fields, header, -1, "TurnMin", "MinTurns")),
                TurnMax = ParseNullableInt(GetField(fields, header, -1, "TurnMax", "MaxTurns")),
                Targeting = GetField(fields, header, -1, "Targeting", "Target", "Targets"),
                Inflict = TryParseIntegerToken(inflict, out int inflictId) ? inflictId : null,
                InflictToken = TryParseIntegerToken(inflict, out _) ? string.Empty : inflict,
                InflictChance = ParseNullableInt(GetField(fields, header, 7, "InflictChance", "StatusChance", "EffectChance", "SecondaryChance")),
                ClearStatEffects = ParseBool(GetField(fields, header, 8, "ClearStatEffects", "ClearStats")),
                UserStat = GetField(fields, header, -1, "UserStat", "SelfStat"),
                UserStatChange = ParseNullableInt(GetField(fields, header, -1, "UserStatChange", "SelfStatChange")),
                UserStatChance = ParseNullableInt(GetField(fields, header, -1, "UserStatChance", "SelfStatChance")),
                TargetStat = GetField(fields, header, -1, "TargetStat", "FoeStat"),
                TargetStatChange = ParseNullableInt(GetField(fields, header, -1, "TargetStatChange", "FoeStatChange")),
                TargetStatChance = ParseNullableInt(GetField(fields, header, -1, "TargetStatChance", "FoeStatChance")),
                Stat1 = GetField(fields, header, -1, "Stat1"),
                Stat1Change = ParseNullableInt(GetField(fields, header, -1, "Stat1Change")),
                Stat1Chance = ParseNullableInt(GetField(fields, header, -1, "Stat1Chance")),
                Stat2 = GetField(fields, header, -1, "Stat2"),
                Stat2Change = ParseNullableInt(GetField(fields, header, -1, "Stat2Change")),
                Stat2Chance = ParseNullableInt(GetField(fields, header, -1, "Stat2Chance")),
                Stat3 = GetField(fields, header, -1, "Stat3"),
                Stat3Change = ParseNullableInt(GetField(fields, header, -1, "Stat3Change")),
                Stat3Chance = ParseNullableInt(GetField(fields, header, -1, "Stat3Chance")),
                ClearFlags = ParseBool(GetField(fields, header, -1, "ClearFlags")),
                SetFlags = GetField(fields, header, -1, "SetFlags", "Flags", "AddFlags"),
                UnsetFlags = GetField(fields, header, -1, "UnsetFlags", "RemoveFlags"),
                KingShieldAttackMinusOne = ParseBool(GetField(fields, header, 9, "KingShieldAttackMinusOne")),

                ZEffect = ParseNullableInt(GetField(fields, header, -1, "ZEffect", "ZMoveEffect", "ZStatusEffect")),
                BattlePatch = GetField(fields, header, -1, "BattlePatch", "SpecialPatch", "BattleEffectPatch"),
                InGameDescription = GetField(fields, header, -1, "InGameDescription", "MoveDescription", "MoveFlavor", "FlavorText"),
            }); } catch (InvalidDataException ex) { throw new InvalidDataException($"Template, movimiento {move}: {ex.Message}", ex); }
        }

        return [.. rows];
    }

    internal static IEnumerable<string[]> ReadCsv(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            yield return SplitCsvLine(line);
        }
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quote && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quote = !quote;
                }
                continue;
            }

            if (c == ',' && !quote)
            {
                if (quote) throw new InvalidDataException("CSV con comillas sin cerrar.");
        result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (quote) throw new InvalidDataException("CSV con comillas sin cerrar.");
        result.Add(current.ToString().Trim());
        return [.. result];
    }

    internal static Dictionary<string, int> BuildHeaderMap(string[] fields)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fields.Length; i++)
        {
            string key = NormalizeToken(fields[i]);
            if (key.Length != 0 && !result.ContainsKey(key))
                result.Add(key, i);
        }
        return result;
    }

    internal static string GetField(string[] fields, Dictionary<string, int> header, int fallbackIndex, params string[] names)
    {
        if (header is not null)
        {
            foreach (string name in names)
            {
                if (header.TryGetValue(NormalizeToken(name), out int index))
                    return Get(fields, index);
            }
            return string.Empty;
        }

        return fallbackIndex >= 0 ? Get(fields, fallbackIndex) : string.Empty;
    }

    internal static string Get(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index]?.Trim() ?? string.Empty : string.Empty;

    internal static bool IsHeader(string value, string header)
        => string.Equals(value, header, StringComparison.OrdinalIgnoreCase);

    private static int ResolveId(string value, string[] names)
    {
        value = value.Trim();
        if (value.Length == 0)
            return -1;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            return id;

        return Array.FindIndex(names, z => string.Equals(z, value, StringComparison.OrdinalIgnoreCase));
    }

    internal static int ResolveToken(string value, string[] names, Dictionary<string, int> aliases = null)
    {
        value = value.Trim();
        if (value.Length == 0)
            return -1;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            return id;

        string normalized = NormalizeToken(value);
        if (aliases is not null && aliases.TryGetValue(normalized, out int alias))
            return alias;

        return Array.FindIndex(names, z => NormalizeToken(z) == normalized);
    }

    internal static IEnumerable<string> SplitTokens(string value)
        => value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string NormalizeToken(string value)
    {
        if (value is null)
            return string.Empty;

        string lower = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lower.Length);
        foreach (char c in lower)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int ParseInt(string value, int fallback = 0)
        => TryParseIntegerToken(value, out int result) ? result : fallback;

    internal static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TryParseIntegerToken(value, out int result)) throw new InvalidDataException($"Número inválido: {value}.");
        return result;
    }

    private static bool TryParseIntegerToken(string value, out int result)
    {
        result = 0;

        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        int sign = 1;
        if (value.StartsWith("+", StringComparison.Ordinal))
        {
            value = value[1..].TrimStart();
        }
        else if (value.StartsWith("-", StringComparison.Ordinal))
        {
            sign = -1;
            value = value[1..].TrimStart();
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
                return false;

            result = sign * hex;
            return true;
        }

        if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            if (!int.TryParse(value[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
                return false;

            result = sign * hex;
            return true;
        }

        if (!int.TryParse((sign < 0 ? "-" : string.Empty) + value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            return false;

        return true;
    }
    private static bool ParseBool(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Equals("0") || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("n", StringComparison.OrdinalIgnoreCase)) return false;
        bool result = value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("si", StringComparison.OrdinalIgnoreCase)
            || value.Equals("sí", StringComparison.OrdinalIgnoreCase);
        if (!result) throw new InvalidDataException($"Check inválido: {value}.");
        return true;
    }
}
