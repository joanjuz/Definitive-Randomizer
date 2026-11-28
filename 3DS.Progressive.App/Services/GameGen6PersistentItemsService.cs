using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record Gen6PersistentItemsProfile(ulong TitleId, string Name,
    IReadOnlyDictionary<string, string> Pins, string ShapeHash)
{
    internal static Gen6PersistentItemsProfile Known(bool y) => new(y ? 0x0004000000055E00UL : 0x0004000000055D00UL,
        y ? "Pokémon Y" : "Pokémon X", new Dictionary<string, string>
        {
            ["HeaderNCCH0.bin"] = y ? "C83D1FAF22DECCF1223F16ABF77219664C1E73F684922D146DA388D9A590B139" : "C35912A8ED8296DB4758D865C5F50BB17D75FD32B404F9872ECB2C3E5B646E7F",
            ["DecryptedExHeader.bin"] = y ? "F64B947A13E9B634665E5A4D7D7E7FB7410201871AD091510967BE72A7BEF47C" : "AE8BBDE2BABB4A0222CDF08D2E71DABA27EA4DEB3C4FA9D17600D2068901F4DB",
            ["ExtractedExeFS/.code.bin"] = y ? "62554AC3D494F6A333D92B156AD239017114ECF95367A97DB57E251456692FE7" : "0F6D3E1D66CBCD55D46FA453DB4B6AA489E03C3537C6B24026F2A53AF142797A",
            ["ExtractedRomFS/DllBattle.cro"] = "9C07A3342DFBA4ADFC3569271D65FE9FAD88F1E271D5BA650179377E60D0A5A1",
            ["ExtractedRomFS/DllField.cro"] = y ? "F4D77DD5FE18888A51513E13864F84AF0FDA2D0A3420334CD0E886BC9F080BD3" : "ED043EB6C5D4057D64CEEFDA99B59B0978412B982427551F6C99BF6084BE6489",
            ["ExtractedRomFS/static.crs"] = y ? "598EAA950FEC39DD4D7915257299B4BCECE7E143F3542A31EB5772E7F89FECBD" : "30D10ADA3F829AE5AE2080C15D27DDF809B3D852CBAF5928C44E609764BFFDAA",
            ["ExtractedRomFS/.crr/static.crr"] = y ? "0637C64185F94AD937044E73600248A0CAAE811E9CD2DDBBB3E90F88E0B06248" : "D9611CA325E149A15D0B97DE5DE6FF1175D0317269CA4D4B2A7EFB6A82C93CEB",
            ["ExtractedRomFS/a/2/2/0"] = "59F9A844495D55176E57CDFA1C7CF4E317CECB4A0FC6479A2578C1B9087D352E",
        }, Gen6PersistentItemsPatcher.KnownShape);
}

/// <summary>Independent XY installer. Native reconciliation, frozen proposal, exact delta removal.</summary>
internal sealed class GameGen6PersistentItemsService : IProjectEditor
{
    internal const string ActionId = "xy.persistent-items";
    internal sealed record Inspection(bool Allowed, string Reason, string Game, string State,
        Gen6PersistentItemsProfile? Profile, IReadOnlyList<(string Path, byte[] Fingerprint)> Dependencies);
    private readonly GameProjectSession _project;
    private readonly Gen6PersistentItemsProfile _profile;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly bool _composed;
    private readonly string _path;
    private IReadOnlyList<(string Path, byte[] Fingerprint)> _dependencies;
    private byte[] _hash;
    public string Title => "Persistent Items · " + _profile.Name;
    public IReadOnlyList<RecordEntry> Entries => [new(1, "Conservar el objeto inicialmente equipado después del combate")];
    public IReadOnlyList<EditorGroup> RandomOptions => [new("Instalación reversible", [new("Enable", "Persistent Items activado", 1, 0, 1)])];

    internal GameGen6PersistentItemsService(GameProjectSession project) : this(project, SelectProfile(project),true) { }
    internal GameGen6PersistentItemsService(GameProjectSession project, Gen6PersistentItemsProfile profile)
        :this(project,profile,false) { }
    private GameGen6PersistentItemsService(GameProjectSession project, Gen6PersistentItemsProfile profile,bool composed)
    {
        _project = project; _profile = profile;_composed=composed;
        var inspection = Inspect(project, composed?null:profile);
        if (!inspection.Allowed) throw new InvalidDataException(inspection.Reason);
        _path = Path.Combine(project.GameConfiguration.RomFS, "DllBattle.cro");
        _dependencies = inspection.Dependencies;
        _hash = _dependencies.Single(d => StringComparer.OrdinalIgnoreCase.Equals(d.Path, _path)).Fingerprint;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void NoLinks(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Falta el recurso requerido: " + Path.GetFileName(path), path);
        for (FileSystemInfo? part = new FileInfo(Path.GetFullPath(path)); part is not null;
             part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("El recurso no puede atravesar enlaces: " + part.Name);
    }
    private static Gen6PersistentItemsProfile SelectProfile(GameProjectSession project)
    {
        if (!project.GameConfiguration.XY) throw new NotSupportedException("Persistent Items Gen6 solo admite Pokémon X/Y acreditados.");
        string root = Path.GetDirectoryName(Path.GetFullPath(project.GameConfiguration.RomFS))!;
        string path = new[] { "DecryptedExHeader.bin", "exheader.bin" }.Select(n => Path.Combine(root, n)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Falta la cabecera de identidad de X/Y.");
        NoLinks(path); byte[] header = File.ReadAllBytes(path);
        if (header.Length < 0x208) throw new InvalidDataException("La cabecera de identidad está truncada.");
        return BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0x200)) switch
        {
            0x0004000000055D00 => Gen6PersistentItemsProfile.Known(false),
            0x0004000000055E00 => Gen6PersistentItemsProfile.Known(true),
            _ => throw new InvalidDataException("El Title ID no corresponde a Pokémon X/Y acreditados."),
        };
    }
    internal static Inspection Inspect(GameProjectSession project) => Inspect(project, null);
    internal static Inspection Inspect(GameProjectSession project, Gen6PersistentItemsProfile? profile)
    {
        try
        {
            if(profile is null)
            {
                profile=SelectProfile(project);
                var admission=Gen6MintRuntimeDependencies.Capture(project,requireInstalled:false);
                if(admission.TitleId!=profile.TitleId)throw new InvalidDataException("Perfil XY cruzado.");
                string battlePath=Path.Combine(project.GameConfiguration.RomFS,"DllBattle.cro");
                var native=Gen6PersistentItemsPatcher.Inspect(CroIntegrity.Rebuild(File.ReadAllBytes(battlePath)),profile.ShapeHash);
                if(native==Gen6PersistentItemsPatcher.State.Partial)throw new InvalidDataException("Instalación parcial de Persistent Items; usa el historial para deshacerla.");
                admission.VerifyUnchanged();
                return new(true,"Código nativo y datos randomizados acreditados. Se verifican CRO/CRR en la misma transacción; no se genera una firma criptográfica.",profile.Name,
                    native==Gen6PersistentItemsPatcher.State.Installed?"Instalado · patrón ARM acreditado":"Original compatible",profile,admission.Dependencies.Select(d=>(d.Key,Convert.FromHexString(d.Value))).ToArray());
            }
            profile ??= SelectProfile(project);
            if (!project.GameConfiguration.XY) throw new InvalidDataException("Este juego no pertenece a X/Y.");
            if (profile.TitleId is not (0x0004000000055D00UL or 0x0004000000055E00UL)
                || profile.Pins.Count != 8 || Gen6PersistentItemsProfile.Known(false).Pins.Keys.Any(key => !profile.Pins.ContainsKey(key)))
                throw new InvalidDataException("El perfil de dependencias X/Y está incompleto o tiene un título desconocido.");
            string root = Path.GetDirectoryName(Path.GetFullPath(project.GameConfiguration.RomFS))!;
            var dependencies = new List<(string Path, byte[] Fingerprint)>();
            byte[] Read(string path)
            {
                NoLinks(path); byte[] bytes = File.ReadAllBytes(path);
                dependencies.Add((path, SHA256.HashData(bytes))); return bytes;
            }
            string Resolve(string key) => key.StartsWith("ExtractedRomFS/", StringComparison.Ordinal) ? Path.Combine(project.GameConfiguration.RomFS, key[15..])
                : key.StartsWith("ExtractedExeFS/", StringComparison.Ordinal) ? Path.Combine(project.GameConfiguration.ExeFS, key[15..]) : Path.Combine(root, key);
            Gen6PersistentItemsPatcher.State state = Gen6PersistentItemsPatcher.State.Original;
            foreach (var (key, pin) in profile.Pins)
            {
                string path = Resolve(key);
                if (key is "HeaderNCCH0.bin" or "DecryptedExHeader.bin" or "ExtractedExeFS/.code.bin")
                {
                    string alias = key switch { "HeaderNCCH0.bin" => Path.Combine(root, "ncchheader.bin"), "DecryptedExHeader.bin" => Path.Combine(root, "exheader.bin"), _ => Path.Combine(project.GameConfiguration.ExeFS, "code.bin") };
                    string[] paths = new[] { path, alias }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (paths.Length == 0) throw new FileNotFoundException("Falta " + key);
                    foreach (string candidate in paths)
                    {
                        byte[] bytes = Read(candidate);
                        if (Hash(bytes) != pin) throw new InvalidDataException(key + ": huella de otro título, revisión o modificación externa.");
                        if (key == "HeaderNCCH0.bin" && (bytes.Length < 0x120 || !bytes.AsSpan(0x100, 4).SequenceEqual("NCCH"u8)
                            || BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0x118)) != profile.TitleId)) throw new InvalidDataException("Title ID NCCH incompatible.");
                        if (key == "DecryptedExHeader.bin" && (bytes.Length < 0x208 || BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0x200)) != profile.TitleId)) throw new InvalidDataException("Title ID exheader incompatible.");
                    }
                }
                else if (key == "ExtractedRomFS/DllBattle.cro")
                {
                    string[] matches = Directory.GetFiles(project.GameConfiguration.RomFS).Where(p => Path.GetFileName(p).Equals("DllBattle.cro", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1 || Path.GetFileName(matches[0]) != "DllBattle.cro") throw new InvalidDataException("Debe existir una sola copia física llamada DllBattle.cro.");
                    state = Gen6PersistentItemsPatcher.Inspect(Read(path), profile.ShapeHash);
                    if (state == Gen6PersistentItemsPatcher.State.Partial) throw new InvalidDataException("Instalación parcial de Persistent Items; usa el historial para deshacerla.");
                }
                else if (Hash(Read(path)) != pin) throw new InvalidDataException(key + ": dependencia no acreditada.");
            }
            foreach (var dependency in dependencies) GarcTransaction.ReadUnchanged(dependency.Path, dependency.Fingerprint);
            return new(true, "Perfil acreditado por título y huellas. Carga comprobada en emulador HLE; consola y firma CRR no acreditadas.", profile.Name,
                state == Gen6PersistentItemsPatcher.State.Installed ? "Instalado · patrón ARM acreditado" : "Original compatible", profile, dependencies);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(false, "No se modificó ningún archivo. " + e.Message, profile?.Name ?? "X/Y sin acreditar", "Incompatible", profile, []); }
    }
    private byte[] Current()
    {
        foreach (var dependency in _dependencies) GarcTransaction.ReadUnchanged(dependency.Path, dependency.Fingerprint);
        return GarcTransaction.ReadUnchanged(_path, _hash);
    }
    public IReadOnlyList<EditorGroup> Read(int id)
    {
        if (id != 1) throw new ArgumentException("Registro inválido.");
        byte[] current=Current();return [new("Estado", [new("Enable", "Persistent Items activado", Gen6PersistentItemsPatcher.Inspect(_composed?CroIntegrity.Rebuild(current):current, _profile.ShapeHash) == Gen6PersistentItemsPatcher.State.Installed ? 1 : 0, 0, 1)])];
    }
    public EditorProposal Preview(IReadOnlyDictionary<string, int> options, IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions, options);
        if (scope.Count != 1 || scope[0] != 1) throw new ArgumentException("Selecciona Persistent Items.");
        byte[] before = Current(), after = Gen6PersistentItemsPatcher.Patch(_composed?CroIntegrity.Rebuild(before):before, options["Enable"] == 1, _profile.ShapeHash);
        var files=new List<FileChange>{new(_path,before,after)};
        if(_composed)
        {
            string[] paths=Directory.GetFiles(_project.GameConfiguration.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
            var images=new List<byte[]>();
            foreach(string path in paths)
            {
                byte[] source=File.ReadAllBytes(path),proposed=path.Equals(_path,StringComparison.OrdinalIgnoreCase)?after:CroIntegrity.Rebuild(source);images.Add(proposed);
                if(!path.Equals(_path,StringComparison.OrdinalIgnoreCase)&&!source.AsSpan().SequenceEqual(proposed))files.Add(new(path,source,proposed));
            }
            string crrPath=Path.Combine(_project.GameConfiguration.RomFS,".crr/static.crr");byte[] crr=File.ReadAllBytes(crrPath),next= CroIntegrity.RebuildCrr(crr,images);
            if(!crr.AsSpan().SequenceEqual(next))files.Add(new(crrPath,crr,next));
            Current();
        }
        var changes = new List<BatchChange>();
        foreach (int at in new[] { Gen6PersistentItemsPatcher.Gate, Gen6PersistentItemsPatcher.Restore })
            if (!before.AsSpan(at, 4).SequenceEqual(after.AsSpan(at, 4))) changes.Add(new(1, "DllBattle.cro", $"ARM 0x{at:X}", Convert.ToHexString(before.AsSpan(at, 4)), Convert.ToHexString(after.AsSpan(at, 4))));
        return new(_owner, options["Enable"] == 1 ? "Instalar Persistent Items X/Y" : "Retirar Persistent Items X/Y", 0,
            files, changes, new Dictionary<string, string>
            { ["ActionId"] = ActionId, ["Enable"] = options["Enable"].ToString(), ["TitleId"] = _profile.TitleId.ToString("X16"), ["SourceSHA256"] = Hash(before), ["ResultSHA256"] = Hash(after) },
            _dependencies.Select(d => (d.Path, (byte[])d.Fingerprint.Clone())));
    }
    public void Save(int id, IReadOnlyDictionary<string, int> values) => Apply(Preview(values, [id]));
    public void Apply(EditorProposal proposal)
    {
        if (proposal.Owner != _owner) throw new InvalidOperationException("La propuesta pertenece a otra sesión.");
        byte[] current = Current();
        if (!proposal.Parameters.TryGetValue("Enable", out string? enable) || enable is not ("0" or "1")) throw new InvalidDataException("Propuesta inválida.");
        byte[] expected = Gen6PersistentItemsPatcher.Patch(_composed?CroIntegrity.Rebuild(current):current, enable == "1", _profile.ShapeHash);
        var canonical = Preview(new Dictionary<string, int> { ["Enable"] = enable == "1" ? 1 : 0 }, [1]);
        if (proposal.Action != canonical.Action || proposal.Seed != canonical.Seed || !proposal.Changes.SequenceEqual(canonical.Changes)
            || proposal.Parameters.Count != canonical.Parameters.Count || canonical.Parameters.Any(p => !proposal.Parameters.TryGetValue(p.Key, out string? value) || value != p.Value))
            throw new InvalidDataException("La descripción o las huellas de la propuesta fueron alteradas.");
        if (proposal.Dependencies.Count != canonical.Dependencies.Count || proposal.Dependencies.Where((d, i) =>
            !StringComparer.Ordinal.Equals(d.Path, canonical.Dependencies[i].Path) || !d.Fingerprint.AsSpan().SequenceEqual(canonical.Dependencies[i].Fingerprint)).Any())
            throw new InvalidDataException("Las dependencias de la propuesta fueron alteradas.");
        if (proposal.Files.Count != canonical.Files.Count || proposal.Files.Where((file,i)=>file.Path!=canonical.Files[i].Path
            || !file.Original().AsSpan().SequenceEqual(canonical.Files[i].Original()) || !file.Proposed().AsSpan().SequenceEqual(canonical.Files[i].Proposed())).Any())
            throw new InvalidDataException("La propuesta está obsoleta o fue alterada.");
        using var manual = ManualOperationScope.Record(this, 1, new Dictionary<string, int> { ["Enable"] = enable == "1" ? 1 : 0 });
        var held = new List<FileStream>();
        try
        {
            // Dependencies remain immutable for the complete history/file transaction.
            foreach (var dependency in _dependencies.Where(d => !canonical.Files.Any(f=>f.Changed&&StringComparer.OrdinalIgnoreCase.Equals(d.Path,f.Path))))
            {
                NoLinks(dependency.Path); var file = new FileStream(dependency.Path, FileMode.Open, FileAccess.Read, FileShare.Read); held.Add(file);
                if (!SHA256.HashData(file).AsSpan().SequenceEqual(dependency.Fingerprint)) throw new IOException("Una dependencia cambió después de la vista previa.");
            }
            _project.History.Commit(canonical.Files, Title, canonical.Action, canonical.Changes, canonical.Parameters, "Mechanics");
            if (!File.ReadAllBytes(_path).AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Falló la relectura del parche.");
            foreach(var file in canonical.Files)if(!File.ReadAllBytes(file.Path).AsSpan().SequenceEqual(file.Proposed()))throw new InvalidDataException("Falló la relectura de integridad: "+Path.GetFileName(file.Path));
            _hash = SHA256.HashData(expected);
            _dependencies = _dependencies.Select(d => (d.Path, canonical.Files.FirstOrDefault(f=>StringComparer.OrdinalIgnoreCase.Equals(d.Path,f.Path)) is {} file?SHA256.HashData(file.Proposed()):d.Fingerprint)).ToArray();
        }
        finally { foreach (var file in held) file.Dispose(); }
    }
}
