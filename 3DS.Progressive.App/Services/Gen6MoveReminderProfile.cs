using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;

namespace ThreeDS.Progressive.Services;

/// <summary>Admission of the exact X resources whose Route 2 experiment the owner validated in game.</summary>
internal static class Gen6MoveReminderProfile
{
    internal const ulong XTitleId = 0x0004000000055D00;
    internal const ulong YTitleId = 0x0004000000055E00;
    private const string CodeHash = "0F6D3E1D66CBCD55D46FA453DB4B6AA489E03C3537C6B24026F2A53AF142797A";
    private const string CommonHash = "4068FC8704B1DD9529B2351E0173EF5216A2F4B0225B1282B193498704744D6F";
    private const string RecordHash = "C7AB122F70C27E674959DF808E5B595C86AADABE64036B590BB4B993B0E13393";
    private const string ModelHash = "1A7FC7BF2E1E93ED1093D166EFC2A823D5459039D28126E2B3139915A25361E4";
    private static readonly (string Path, string Hash)[] NativePins =
    [
        ("DllField.cro", "ED043EB6C5D4057D64CEEFDA99B59B0978412B982427551F6C99BF6084BE6489"),
        ("static.crs", "30D10ADA3F829AE5AE2080C15D27DDF809B3D852CBAF5928C44E609764BFFDAA"),
        ("DllWazaOmoidashi.cro", "B76C2045E0D7988AEB1B82F132F9F5C38C7A622FEBDCEDD144C1538A1BB4E5DD"),
        ("DllPokeList.cro", "D673A3B9A38BF301C8337672EB7E67AECABB83DB5B8D2A989F26CDB754E22D38"),
    ];
    private static readonly string[] StoryPins =
    [
        "BD4D1C509737E10D77EA7E387A3FB774FAB9A076C7F05F89FD5675580B9AA329",
        "237B0BB54419DE448A67ABE4C907821181943F22A75AC9A6B1CCC568E849D556",
        "C2E03639A6986DF5D67213A1E121051B3FB5EA8A0699A6447F204B923759E815",
        "5AF8301CA250BF6724E06FB5B9102B29B7AD82A6D17F6D439100EF897C99CE5D",
        "4AE040D6DF42F3426AD7D711127EDF0D10850BC995DB7FADFAF517CFDD8F3F2F",
        "C0BA403E14B23B6A623269E299E6855609AD90E42325F808D5ACD2A1A5BC0F21",
        "14812B46DFE158FD5159C93643793F194BD699051A2B4E7007F2860EFA5793FA",
        "597D3C2DAFB8E785A267AFABA3A480A5364C3F749B972261DE624E9D1FD0A9D3",
    ];

    internal sealed record Inspection(bool Allowed, string Reason, string Title,
        IReadOnlyList<(string Path, byte[] Fingerprint)> Dependencies);

    private static readonly ConditionalWeakTable<Gen6MapBackend, Inspection> Snapshots = new();

    internal static Gen6MapBackend CreateBackend(GameConfig config)
    {
        var evidence = Inspect(config);
        var backend = Gen6MapBackend.FromConfig(config, reminderAccredited: evidence.Allowed);
        if (evidence.Allowed)
        {
            foreach (var dependency in evidence.Dependencies) GarcTransaction.ReadUnchanged(dependency.Path, dependency.Fingerprint);
            Snapshots.Add(backend, evidence);
        }
        return backend;
    }

    internal static Inspection Revalidate(Gen6MapBackend backend, GameConfig config)
    {
        if (!Snapshots.TryGetValue(backend, out var snapshot))
            throw new InvalidOperationException("El editor no tiene una acreditación de recursos del proyecto. Vuelve a abrirlo.");
        foreach (var dependency in snapshot.Dependencies) GarcTransaction.ReadUnchanged(dependency.Path, dependency.Fingerprint);
        var current = Inspect(config);
        if (!current.Allowed) throw new InvalidOperationException(current.Reason);
        if (current.Dependencies.Count != snapshot.Dependencies.Count || current.Dependencies.Any(d =>
            !snapshot.Dependencies.Any(s => s.Path == d.Path && s.Fingerprint.AsSpan().SequenceEqual(d.Fingerprint))))
            throw new InvalidDataException("Los recursos cambiaron desde que se abrió el editor. Vuelve a abrirlo.");
        return snapshot;
    }

    internal static string? IdentityFailure(ReadOnlySpan<byte> exheader)
    {
        if (exheader.Length < 0x208) return "La cabecera de identidad está truncada.";
        ulong title = BinaryPrimitives.ReadUInt64LittleEndian(exheader[0x200..]);
        return title switch
        {
            XTitleId => null,
            YTitleId => "Pokémon Y permanece bloqueado: falta su validación jugable independiente.",
            _ => "El Title ID no corresponde al perfil acreditado de Pokémon X.",
        };
    }

    internal static void RequireHash(ReadOnlySpan<byte> bytes, string expected, string resource)
    {
        if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(Convert.FromHexString(expected)))
            throw new InvalidDataException($"{resource}: la huella no coincide con el recurso acreditado de Pokémon X.");
    }

    internal static void RequireOriginalTemplate(Gen6MapBackend backend)
    {
        RequireHash(backend.Reminder?.Record ?? [], RecordHash, "Plantilla del editor");
        RequireHash(backend.ReminderModelEntry ?? [], ModelHash, "Modelo del editor");
    }

    internal static void RequireModelLists(byte[][] lists, int originalArea)
    {
        if (lists.Any(list => Gen6AreaModels.Models(list).Count(model => model == 54) > 1))
            throw new InvalidDataException("Hay una entrada duplicada del modelo 54 en un área.");
        if (originalArea < 0 || originalArea >= lists.Length || !Gen6AreaModels.Models(lists[originalArea]).Contains(54))
            throw new InvalidDataException("El área original no conserva el modelo 54 del recordador.");
        byte[] entry = Gen6AreaModels.Entry(lists, 54) ?? throw new InvalidDataException("El modelo 54 está ausente, es ambiguo o tiene listas ilegibles.");
        RequireHash(entry, ModelHash, "Entrada del modelo 54");
    }

    internal static void RequireArchiveHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 0x1C || !bytes[..4].SequenceEqual("CRAG"u8))
            throw new InvalidDataException("Cabecera GARC ausente o ilegible.");
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint data = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        if (header < 0x1C || data < header || data > bytes.Length || BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) != bytes.Length)
            throw new InvalidDataException("Los tamaños de la cabecera GARC no coinciden con el archivo.");
    }

    internal static Inspection Inspect(GameConfig config)
    {
        Inspection Deny(string reason) => new(false, reason, config.XY ? "XY" : config.Version.ToString(), []);
        if (!config.XY) return Deny("Añadir recordador está disponible exclusivamente para Pokémon X acreditado; este juego no es compatible.");
        if (string.IsNullOrWhiteSpace(config.RomFS) || string.IsNullOrWhiteSpace(config.ExeFS))
            return Deny("Faltan las rutas del proyecto para comprobar la identidad de Pokémon X.");
        try
        {
            string root = Directory.GetParent(Path.GetFullPath(config.RomFS))?.FullName
                ?? throw new InvalidDataException("La ruta RomFS no tiene una carpeta raíz de proyecto.");
            var dependencies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            byte[] Read(string path)
            {
                path = Path.GetFullPath(path);
                byte[] bytes = File.ReadAllBytes(path);
                byte[] hash = SHA256.HashData(bytes);
                if (dependencies.TryGetValue(path, out var prior) && !prior.AsSpan().SequenceEqual(hash))
                    throw new InvalidDataException("Un recurso cambió durante la comprobación del perfil.");
                dependencies[path] = hash;
                return bytes;
            }
            byte[] Archive(string path)
            {
                byte[] bytes = Read(path);
                RequireArchiveHeader(bytes);
                return bytes;
            }
            string[] headers = new[] { "DecryptedExHeader.bin", "exheader.bin" }.Select(n => Path.Combine(root, n)).Where(File.Exists).ToArray();
            if (headers.Length == 0) return Deny("Falta la cabecera de identidad (DecryptedExHeader.bin o exheader.bin). No se puede acreditar Pokémon X.");
            foreach (string header in headers)
                if (IdentityFailure(Read(header)) is { } reason) return Deny(reason);
            string[] code = new[] { ".code.bin", "code.bin" }.Select(n => Path.Combine(config.ExeFS, n)).Where(File.Exists).ToArray();
            if (code.Length != 1) return Deny("Se necesita un único ejecutable code.bin identificable para acreditar Pokémon X.");
            RequireHash(Read(code[0]), CodeHash, "code.bin");
            foreach (var pin in NativePins) RequireHash(Read(Path.Combine(config.RomFS, pin.Path)), pin.Hash, pin.Path);
            byte[] common = Archive(Path.Combine(config.RomFS, "a/0/3/1"));
            RequireHash(new GARC.MemGARC(common).GetFile(42), CommonHash, "Script común 42");
            for (int i = 0; i < StoryPins.Length; i++)
            {
                byte[] texts = Archive(Path.Combine(config.RomFS, $"a/0/8/{i}"));
                RequireHash(new GARC.MemGARC(texts).GetFile(96), StoryPins[i], $"Story96, idioma {i}");
            }
            byte[] zones = Archive(Path.Combine(config.RomFS, config.GetGARCFileName("encdata")));
            var zone = new Gen6Zone(new GARC.LazyGARC(zones)[34]);
            var template = Gen6MoveReminder.Find(zone) ?? throw new InvalidDataException("La zona 034 no conserva el NPC original del recordador.");
            RequireHash(template.Record, RecordHash, "Registro original NPC 2");
            byte[] models = Archive(Path.Combine(config.RomFS, Gen6MapBackend.ModelListArchive(true)));
            var lists = new GARC.MemGARC(models).Files;
            RequireModelLists(lists, zone.Area);
            // Whole-file fingerprints also guard the already-present destination model until Apply.
            foreach (var dependency in dependencies) GarcTransaction.ReadUnchanged(dependency.Key, dependency.Value);
            return new(true, "Pokémon X acreditado: servicio original confirmado por el propietario en Ruta 2; cada nueva colocación debe ser accesible.", "X",
                dependencies.Select(d => (d.Key, d.Value)).ToArray());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or IndexOutOfRangeException or OverflowException or MapFormatException or FormatException)
        {
            return Deny("No se puede acreditar el recordador de Pokémon X: " + ex.Message);
        }
    }
}
