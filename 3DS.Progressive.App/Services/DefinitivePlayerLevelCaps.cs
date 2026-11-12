using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;

namespace ThreeDS.Progressive.Services;

/// <summary>Optional <c>playerLevelCaps</c> block of Definitive Game Data v1: the state of the Player Level Caps patch
/// in the saved Battle.cro and code.bin. Read-only; never a view table, a preview, a template or a history replay.</summary>
internal static class DefinitivePlayerLevelCaps
{
    internal const int MaxBlockBytes = 64 * 1024;
    private const long MaxFileBytes = 128L * 1024 * 1024;
    private sealed record Copy(string Hook, string? Sha256, LevelCapTable? Table, string? Detail);

    /// <summary>Never throws: a failure to read the patch is reported in <c>status</c> and the rest of the document is kept.</summary>
    internal static JsonObject Build(GameConfig config, string titleId, List<FileStream> retained)
    {
        try
        {
            var battle = Read(GamePlayerLevelCapService.Find(config.RomFS, "Battle.cro", true), "Battle.cro", true, retained);
            var code = Read(GamePlayerLevelCapService.Find(config.ExeFS, "code.bin", false), "code.bin", false, retained);
            return Describe(titleId, battle, code);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new JsonObject
            {
                ["version"] = 1, ["status"] = "unsupported",
                ["hooks"] = new JsonObject { ["battleCro"] = "unrecognized", ["codeBin"] = "unrecognized" },
                ["detail"] = "No se pudo leer el estado de Player Level Caps (" + ex.GetType().Name + ")."
            };
        }
    }

    private static Copy Read(string path, string name, bool cro, List<FileStream> retained)
    {
        if (!File.Exists(path)) return new("missing", null, null, name + " no está en el proyecto guardado.");
        byte[] data;
        try
        {
            // Same policy as the other DGD resources: held without write sharing for the whole export.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            retained.Add(stream);
            if (stream.Length > MaxFileBytes) return new("unrecognized", null, null, name + " excede el límite de lectura.");
            data = new byte[checked((int)stream.Length)];
            stream.ReadExactly(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("unrecognized", null, null, name + " no se pudo leer.");
        }
        string hash = Convert.ToHexString(SHA256.HashData(data));
        if (cro && (data.Length < 0x180 || !data.AsSpan(0x80, 4).SequenceEqual("CRO0"u8)))
            return new("unrecognized", hash, null, "Battle.cro no tiene la cabecera CRO0 esperada.");
        if (!cro && (data.Length == 0 || (data.Length & 0x1FF) != 0))
            return new("unrecognized", hash, null, "code.bin no está descomprimido.");
        var state = cro ? LevelCapPatch.InspectBattle(data, out var table) : LevelCapPatch.InspectCandy(data, out table);
        return state switch
        {
            LevelCapCopyState.Stock => new("stock", hash, null, null),
            LevelCapCopyState.Applied when table is not null => new("applied", hash, table, null),
            LevelCapCopyState.AppliedLegacy => new("applied-legacy", hash, null, name + " tiene una rutina de un formato anterior."),
            _ => new("unrecognized", hash, null, name + " no tiene el código original ni la rutina de este parche.")
        };
    }

    private static JsonObject Describe(string titleId, Copy battle, Copy code)
    {
        var block = new JsonObject
        {
            ["version"] = 1, ["status"] = "",
            ["hooks"] = new JsonObject { ["battleCro"] = battle.Hook, ["codeBin"] = code.Hook }
        };
        JsonObject? source = battle.Sha256 is null || code.Sha256 is null ? null : new JsonObject
        {
            ["fingerprintAlgorithm"] = "SHA-256", ["scope"] = "battle.cro,code.bin",
            ["fingerprint"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                titleId + "\nbattle.cro:" + battle.Sha256 + "\ncode.bin:" + code.Sha256)))
        };
        string status; string? detail = null;
        string[] hooks = [battle.Hook, code.Hook];
        if (hooks.Any(h => h is "unrecognized" or "missing"))
        {
            status = "unsupported";
            detail = string.Join(" ", new[] { battle, code }.Where(c => c.Hook is "unrecognized" or "missing").Select(c => c.Detail));
        }
        else if (hooks.Contains("applied-legacy"))
        {
            status = "legacy-format";
            detail = "La rutina instalada usa un formato de tabla anterior. Vuelve a instalar Player Level Caps para exportar los puntos.";
        }
        else if (battle.Table is not null && code.Table is not null)
        {
            byte[] bytes = battle.Table.ToBytes();
            // Byte-for-byte: the runtime table is the only thing the two routines share.
            bool same = bytes.AsSpan().SequenceEqual(code.Table.ToBytes());
            if (same && battle.Table.Entries.Count is >= 1 and <= LevelCapTable.MaxEntries && battle.Table.Validate().Count == 0)
            {
                status = "installed";
                block["evaluation"] = "ordered-first-incomplete";
                block["enforcement"] = new JsonArray("exp-gain", "rare-candy");
                block["ceiling"] = (int)LevelCapTable.HardCeiling;
                source!["tableSha256"] = Convert.ToHexString(SHA256.HashData(bytes));
                block["source"] = source;
                block["checkpoints"] = Checkpoints(battle.Table);
                block["status"] = status;
                return block;
            }
            status = "inconsistent";
            detail = "Battle.cro y code.bin tienen tablas de límites distintas. Vuelve a instalar Player Level Caps.";
        }
        else if (battle.Table is null && code.Table is null) status = "not-detected";
        else
        {
            status = "inconsistent";
            detail = (battle.Table is not null ? "Battle.cro tiene el parche y code.bin conserva el código original."
                : "code.bin tiene el parche y Battle.cro conserva el código original.") + " Vuelve a instalar Player Level Caps.";
        }
        block["status"] = status;
        if (source is not null) block["source"] = source;
        if (!string.IsNullOrWhiteSpace(detail)) block["detail"] = detail.Length > 500 ? detail[..500] : detail;
        return block;
    }

    /// <summary>Labels come from the condition that was read, only when it is exactly one of the researched catalog.</summary>
    internal static JsonArray Checkpoints(LevelCapTable table)
    {
        var catalog = LevelCapTable.KnownFlags.GroupBy(k => (k.Kind, k.Offset, k.Bit)).ToDictionary(g => g.Key, g => g.First().Label);
        var result = new JsonArray();
        foreach (var entry in table.Entries)
        {
            bool flag = entry.Kind == LevelCapConditionKind.EventFlagSet;
            var point = new JsonObject
            {
                ["order"] = result.Count + 1, ["cap"] = (int)entry.Cap,
                ["condition"] = flag
                    ? new JsonObject { ["kind"] = "event-flag-set", ["byte"] = (int)entry.FlagOffset, ["mask"] = (int)entry.FlagBit }
                    : new JsonObject { ["kind"] = "event-work-at-least", ["index"] = (int)entry.FlagOffset, ["minimum"] = (int)entry.FlagBit }
            };
            if (catalog.TryGetValue((entry.Kind, entry.FlagOffset, entry.FlagBit), out string? label))
            { point["label"] = label; point["labelSource"] = "randomizer-catalog"; }
            result.Add(point);
        }
        return result;
    }
}
