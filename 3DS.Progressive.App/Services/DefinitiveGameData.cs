using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;

namespace ThreeDS.Progressive.Services;

/// <summary>Portable metadata from retained on-disk archives; never from UI values or history replay.</summary>
internal static class DefinitiveGameData
{
    internal const string Format = "PokeJuz.DefinitiveGameData";
    internal static bool Supports(GameProjectSession project) => project.GameConfiguration.USUM;

    internal static JsonObject Build(GameProjectSession project)
    {
        if (!Supports(project)) throw new InvalidOperationException("Definitive Game Data v1 admite solo Ultra Sol y Ultra Luna.");
        project.History.RejectPending();
        var config = project.GameConfiguration;
        // The editor identifies the edition through the non-empty encounter archive, not a folder name.
        bool moon = config.GetGARCReference("encdata").Reference == GARCReference.GARCReference_UM.Single(r => r.Name == "encdata").Reference;
        string titleId = moon ? "00040000001B5100" : "00040000001B5000";
        var streams = new List<FileStream>();
        try
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            byte[] Retain(string key, string path)
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(stream);
                if (stream.Length > 128 * 1024 * 1024) throw new InvalidDataException("El recurso " + key + " excede el límite de lectura DGD.");
                byte[] data = new byte[checked((int)stream.Length)];
                stream.ReadExactly(data);
                hashes.Add(key, Convert.ToHexString(SHA256.HashData(data)));
                return data;
            }
            var resources = new Dictionary<string, byte[]>();
            foreach (string key in new[] { "personal", "move", "evolution", "gametext" })
                resources.Add(key, Retain(key, Path.Combine(config.RomFS, config.GetGARCFileName(key))));
            // NCCH/ExHeader metadata is optional in manually extracted projects. Check it when available.
            foreach (var item in new[] { ("ncchheader.bin", 0x118, true), ("exheader.bin", 0x200, false) })
            {
                string path = Path.Combine(project.ProjectRoot, item.Item1);
                if (!File.Exists(path)) continue;
                byte[] header = Retain(item.Item1, path);
                if (header.Length < item.Item2 + 8 || item.Item3 && !header.AsSpan(0x100, 4).SequenceEqual("NCCH"u8) ||
                    BitConverter.ToUInt64(header, item.Item2).ToString("X16") != titleId)
                    throw new InvalidDataException("La identidad de " + item.Item1 + " no coincide con la edición USUM del proyecto.");
            }
            var personal = new GARC.MemGARC(resources["personal"]).Files;
            if (personal.Length < 809 || personal.Take(personal.Length - 1).Any(r => r.Length != PersonalInfoSM.SIZE) ||
                !personal.Take(personal.Length - 1).SelectMany(r => r).SequenceEqual(personal[^1]))
                throw new InvalidDataException("Los registros Personal y su tabla maestra no coinciden. Revisa el proyecto guardado.");
            var table = new PersonalTable(personal[^1], GameVersion.USUM);
            var outerMoves = new GARC.MemGARC(resources["move"]).Files;
            if (outerMoves.Length != 1) throw new InvalidDataException("Contenedor de movimientos USUM no reconocido.");
            var moves = Mini.UnpackMini(outerMoves[0], "WD");
            if (moves is null || moves.Length <= 728 || moves.Take(729).Any(r => r.Length < 0x28))
                throw new InvalidDataException("La tabla de movimientos USUM está incompleta.");
            var evolution = new GARC.MemGARC(resources["evolution"]).Files;
            var texts = new GARC.MemGARC(resources["gametext"]).Files;
            string[] Names(TextName name)
            {
                int index = config.GameText.Single(t => t.Name == name).Index;
                if (index < 0 || index >= texts.Length) throw new InvalidDataException("Falta el catálogo " + name + ".");
                return new TextFile(config, texts[index], true).Lines;
            }
            var speciesNames = Names(TextName.SpeciesNames);
            var moveNames = Names(TextName.MoveNames);
            var typeNames = Names(TextName.Types);
            var abilityNames = Names(TextName.AbilityNames);
            string Label(string[] labels, int id)
            {
                if (id >= labels.Length || string.IsNullOrWhiteSpace(labels[id]) || labels[id].Length > 200)
                    throw new InvalidDataException("Catálogo incompleto o nombre inválido en el registro " + id + ".");
                return labels[id];
            }
            var species = new JsonArray();
            var evolutions = new JsonArray();
            for (int id = 1; id <= 807; id++)
            {
                var baseEntry = table.Table[id];
                if (baseEntry.FormeCount > 32) throw new InvalidDataException("Cantidad de formas no compatible con DGD v1.");
                for (int form = 0; form < Math.Max(1, baseEntry.FormeCount); form++)
                {
                    int index = baseEntry.FormeIndex(id, form);
                    if (index < 1 || index >= table.Table.Length || index >= evolution.Length || evolution[index].Length != 64)
                        throw new InvalidDataException("Referencia de forma o evolución inválida: " + id + ":" + form);
                    var p = table.Table[index];
                    if (p.Types.Any(t => t < 0 || t > 17) || p.Abilities.Any(a => a < 0 || a > 233))
                        throw new InvalidDataException("Tipo o habilidad no compatible: " + id + ":" + form);
                    species.Add(new JsonObject
                    {
                        ["speciesId"] = id, ["formId"] = form, ["personalIndex"] = index, ["name"] = Label(speciesNames, id),
                        ["types"] = new JsonArray(p.Types.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
                        ["abilities"] = new JsonArray(p.Abilities.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                        ["baseStats"] = new JsonObject { ["hp"] = p.HP, ["attack"] = p.ATK, ["defense"] = p.DEF,
                            ["specialAttack"] = p.SPA, ["specialDefense"] = p.SPD, ["speed"] = p.SPE }
                    });
                    var routes = new JsonArray();
                    var data = evolution[index];
                    for (int slot = 0; slot < 8; slot++)
                    {
                        int at = slot * 8, method = BitConverter.ToUInt16(data, at);
                        if (method == 0) continue;
                        int target = BitConverter.ToUInt16(data, at + 4), targetForm = unchecked((sbyte)data[at + 6]);
                        if (method > 42 || target < 1 || target > 807 || targetForm < -1 || data[at + 7] > 100)
                            throw new InvalidDataException("Ruta evolutiva no compatible: " + id + ":" + form);
                        routes.Add(new JsonObject { ["method"] = method, ["argument"] = (int)BitConverter.ToUInt16(data, at + 2),
                            ["targetSpeciesId"] = target, ["targetFormId"] = targetForm, ["level"] = (int)data[at + 7] });
                    }
                    evolutions.Add(new JsonObject { ["speciesId"] = id, ["formId"] = form, ["routes"] = routes });
                }
            }
            var moveRows = new JsonArray();
            for (int id = 0; id <= 728; id++)
            {
                var m = new Move7(moves[id]);
                int priority = unchecked((sbyte)m.Priority);
                if (m.Type > 17 || m.Category > 2 || priority < -8 || priority > 8)
                    throw new InvalidDataException("Propiedades de movimiento fuera de rango: " + id);
                moveRows.Add(new JsonObject { ["id"] = id, ["name"] = Label(moveNames, id), ["typeId"] = m.Type,
                    ["category"] = m.Category, ["power"] = m.Power, ["accuracy"] = m.Accuracy, ["pp"] = m.PP, ["priority"] = priority });
            }
            JsonArray Catalog(string[] labels, int count) => new(Enumerable.Range(0, count)
                .Select(id => (JsonNode?)new JsonObject { ["id"] = id, ["name"] = Label(labels, id) }).ToArray());
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(titleId + "\n" +
                string.Join("\n", hashes.Select(p => p.Key + ":" + p.Value)))));
            // Optional block with its own fingerprint: neither the root fingerprint nor source.scope include these files.
            var playerLevelCaps = DefinitivePlayerLevelCaps.Build(config, titleId, streams);
            project.History.RejectPending();
            var document = new JsonObject
            {
                ["format"] = Format, ["schemaVersion"] = 1,
                ["producer"] = new JsonObject { ["id"] = "DefinitiveRandomizer",
                    ["version"] = typeof(GameProjectSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown" },
                // NCCH format version and ExHeader remaster version are not an installed game-update proof.
                ["game"] = new JsonObject { ["id"] = moon ? "ultra-moon" : "ultra-sun", ["generation"] = 7,
                    ["titleId"] = titleId, ["revision"] = null, ["revisionStatus"] = "unknown" },
                ["source"] = new JsonObject { ["kind"] = "saved-project", ["fingerprintAlgorithm"] = "SHA-256",
                    ["fingerprint"] = fingerprint, ["scope"] = "personal,moves,evolutions,gametext" },
                ["coverage"] = "exported-records", ["types"] = Catalog(typeNames, 18), ["abilities"] = Catalog(abilityNames, 234),
                ["species"] = species, ["moves"] = moveRows, ["evolutions"] = evolutions,
                ["playerLevelCaps"] = playerLevelCaps
            };
            var provenance = DefinitiveProfileProvenance.Build(project, moon ? "ultra-moon" : "ultra-sun");
            if (provenance is not null) document["profileProvenance"] = provenance;
            project.History.RejectPending();
            return document;
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }

    internal static void Export(GameProjectSession project, string destination)
    {
        string target = Path.GetFullPath(destination), root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.ProjectRoot));
        if (target.Equals(root, StringComparison.OrdinalIgnoreCase) || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Guarda el JSON fuera de la carpeta del proyecto.");
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(Build(project), new JsonSerializerOptions { WriteIndented = true });
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(data); stream.Flush(true); }
            File.Move(temp, target, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
