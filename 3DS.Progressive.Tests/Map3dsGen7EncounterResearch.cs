using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive.Services;

/// <summary>
/// Read-only research of USUM wild encounters: per area, the encounter tables (slot 9 of the area, «EA» pack of
/// day/night tables) and the surface attributes of the walkable ground (part 6 of its blocks). Nothing is written.
/// </summary>
internal static class Map3dsGen7EncounterResearch
{
    internal static void Run(string root, string output)
    {
        var session = GameProjectSession.Open(Path.GetFullPath(root), false);
        var config = session.GameConfiguration;
        var backend = Gen7MapBackend.FromConfig(config);
        var encdata = config.GetlzGARCData("encdata");
        using var w = new StreamWriter(output);
        int areas = encdata.FileCount / Gen7MapBackend.SlotsPerWorld;
        w.WriteLine($"areas\t{areas}");
        for (int a = 0; a < areas; a++)
        {
            byte[] slot = encdata[(a * Gen7MapBackend.SlotsPerWorld) + 9];
            int tables = 0, nonEmpty = 0;
            string tableSizes = "";
            if (slot.Length >= 4 && slot[0] == 'E' && slot[1] == 'A')
            {
                var parts = Mini.UnpackMini(slot, "EA") ?? [];
                tables = parts.Length; nonEmpty = parts.Count(p => p.Length > 0);
                tableSizes = string.Join(",", parts.Select(p => p.Length));
            }
            IReadOnlyList<Gen7CollisionTriangle> tris;
            try { tris = backend.GroundCollision(a); } catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidDataException) { tris = []; }
            var hist = tris.GroupBy(t => t.Attribute).OrderByDescending(g => g.Count()).Select(g => $"{g.Key:X8}:{g.Count()}");
            string name = backend.Zones.FirstOrDefault(z => z.Id == a)?.Name ?? "";
            // ED parts: per zone file, u32 count and the first u32 (type) of the first record, plus the file size.
            string ed = "";
            if (backend.EventPack(a) is { } pack)
                ed = string.Join(" ", Enumerable.Range(0, pack.Count).Select(p =>
                {
                    var part = pack.Part(p).ToArray();
                    var inner = Gen7Events.Container(part);
                    if (inner is null) return $"p{p}:raw{part.Length}";
                    var files = Enumerable.Range(0, inner.Count).Select(z => inner.Part(z).ToArray()).ToArray();
                    string Desc(byte[] f) => f.Length < 4 ? $"{f.Length}b" : $"{BitConverter.ToInt32(f, 0)}x" + (f.Length >= 8 ? $"t{BitConverter.ToUInt32(f, 4)}" : "") + $"/{f.Length}";
                    return $"p{p}:[{string.Join(",", files.Select(Desc))}]";
                }));
            w.WriteLine($"{a}\t{name}\tslot9={slot.Length}\ttables={tables}\tnonempty={nonEmpty}\tsizes={tableSizes}\ttris={tris.Count}\t{string.Join(" ", hist)}\t{ed}");
        }
        Console.WriteLine($"Investigación escrita en {output} (solo lectura)");
    }
}
