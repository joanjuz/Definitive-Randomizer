using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using pk3DS.Core.NX;

namespace ThreeDS.Progressive.Services.Switch;

internal sealed record SwordShieldChange(int Entry, string Field, int Before, int After);
internal sealed class SwordShieldEditPreview
{
    internal Guid Owner { get; }
    internal string DraftHash { get; }
    private readonly byte[] _result;
    internal byte[] Result => (byte[])_result.Clone();
    internal IReadOnlyList<SwordShieldChange> Changes { get; }
    internal SwordShieldRandomOptions? RandomOptions { get; }
    internal int EligibleEntries { get; }
    internal IReadOnlyList<int> ProtectedSpecies { get; } = Array.Empty<int>();
    internal SwordShieldEditPreview(Guid owner,string hash,byte[] result,IReadOnlyList<SwordShieldChange> changes)
    {Owner=owner;DraftHash=hash;_result=(byte[])result.Clone();Changes=Array.AsReadOnly(changes.ToArray());}
    internal SwordShieldEditPreview(Guid owner, string hash, byte[] result, SwordShieldChange change)
    { Owner = owner; DraftHash = hash; _result = (byte[])result.Clone(); Changes = Array.AsReadOnly(new[] { change }); }
    internal SwordShieldEditPreview(Guid owner, string hash, SwordShieldRandomResult result, SwordShieldRandomOptions options)
    {
        Owner = owner; DraftHash = hash; _result = result.ToArray(); RandomOptions = options;
        Changes = Array.AsReadOnly(result.Changes.Select(c => new SwordShieldChange(c.Entry,c.Field.ToString(),c.Before,c.After)).ToArray());
        EligibleEntries = result.EligibleEntries; ProtectedSpecies = result.ProtectedSpecies;
    }

}
internal sealed record SwordShieldRandomOperation(string Algorithm, SwordShieldRandomOptions Options, int ChangedValues, int EligibleEntries, IReadOnlyList<int> ProtectedSpecies, string InputSha256, string ResultSha256);
internal enum SwitchModLayout { Atmosphere, Ryujinx }
internal sealed record SwitchExportResult(string Folder, string ResourceSha256, int ChangedValues);

/// <summary>Session-local overlay. Opening, previewing and applying never write into the source extraction.</summary>
internal sealed partial class SwordShieldProject
{
    internal const string PersonalRelativePath = "bin/pml/personal/personal_total.bin";
    private readonly Guid _id = Guid.NewGuid();
    private readonly byte[] _original;
    private byte[] _draft;
    private readonly Dictionary<string, string> _dependencies;
    private readonly List<SwordShieldChange> _history = new();
    private readonly List<SwordShieldRandomOperation> _randomizations = new();
    internal string Root { get; }
    internal SwordShieldIdentity Identity { get; }
    internal string SourceHash => Hash(_original);
    internal string DraftHash => Hash(_draft);
    internal bool HasChanges => !_draft.AsSpan().SequenceEqual(_original) || _gameplayDraft.Count != 0 || SurpriseEnabled || PokeFeurEnabled;
    internal IReadOnlyList<SwordShieldChange> History => _history.AsReadOnly();
    internal IReadOnlyList<SwordShieldRandomOperation> Randomizations => _randomizations.AsReadOnly();
    internal IReadOnlyList<SwordShieldPersonalEntry> Entries => new SwordShieldPersonalTable(_draft).Entries().Select(e=>e with {Name=SpeciesName(e.Species)}).ToArray();
    internal SwordShieldPersonalEntry Read(int index) => new SwordShieldPersonalTable(_draft).Read(index);
    private SwordShieldProject(string root, SwordShieldIdentity identity, byte[] personal, Dictionary<string, string> dependencies)
    { Root = root; Identity = identity; _original = personal; _draft = (byte[])personal.Clone(); _dependencies = dependencies; }
    internal static SwordShieldProject Open(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Selecciona una carpeta extraída con romfs y exefs; los archivos NSP/XCI no se abren en este prototipo.");
        var paths = new[] { "exefs/main.npdm", "exefs/main", "romfs/" + PersonalRelativePath };
        foreach (string relative in paths)
        {
            string path = Path.Combine(root, relative); CheckNoLinks(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Falta " + relative + ". Selecciona la extracción preparada del juego y su actualización, no la carpeta que contiene los NSP.", path);
        }
        var data = paths.ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(root, p)), StringComparer.Ordinal);
        var identity = SwordShieldIdentity.Read(data[paths[0]], data[paths[1]]);
        _ = new SwordShieldPersonalTable(data[paths[2]]);
        return new(root, identity, data[paths[2]], data.ToDictionary(p => p.Key, p => Hash(p.Value), StringComparer.Ordinal));
    }
    internal SwordShieldEditPreview Preview(int index, SwordShieldField field, int value)
    {
        RequireEditable(); VerifySource();
        var table = new SwordShieldPersonalTable(_draft);
        int before = table.Read(index).Values[(int)field is >= 0 and < SwordShieldPersonalTable.FieldCount ? (int)field : throw new ArgumentOutOfRangeException(nameof(field))];
        return new(_id, Hash(_draft), table.WithValue(index, field, value), new SwordShieldChange(index, field.ToString(), before, value));
    }
    internal SwordShieldEditPreview PreviewPersonalBulk(IReadOnlyList<int> scope,SwordShieldField field,int value)
    {
        RequireEditable();VerifySource();var table=new SwordShieldPersonalTable(_draft);var output=table.WithValues(scope,field,value);var changes=scope.Where(id=>table.Read(id).Values[(int)field]!=value).Select(id=>new SwordShieldChange(id,field.ToString(),table.Read(id).Values[(int)field],value)).ToArray();return new(_id,DraftHash,output,changes);
    }
    internal SwordShieldEditPreview PreviewPersonal(int index,IReadOnlyList<int> values)
    {
        RequireEditable();VerifySource();if(values.Count!=SwordShieldPersonalTable.FieldCount)throw new ArgumentException("Faltan campos de Pokémon.");
        var data=(byte[])_draft.Clone();var changes=new List<SwordShieldChange>();
        for(int f=0;f<SwordShieldPersonalTable.FieldCount;f++){var table=new SwordShieldPersonalTable(data);int before=table.Read(index).Values[f];data=table.WithValue(index,(SwordShieldField)f,values[f]);if(before!=values[f])changes.Add(new(index,((SwordShieldField)f).ToString(),before,values[f]));}
        return new(_id,DraftHash,data,changes);
    }
    internal SwordShieldEditPreview PreviewRandom(SwordShieldRandomOptions options)
    {
        RequireEditable(); VerifySource();
        if(options.HeldItems){var available=ReadData(SwordShieldDataSection.Items).Select(c=>c.Entry).Distinct().Where(SwordShieldGameplay.IsAllowedItem).Order().ToArray();options=options with{HeldItemPool=Array.AsReadOnly(available)};}
        var result = SwordShieldPersonalRandomizer.Generate(_draft, _original, options);
        return new(_id, Hash(_draft), result, options);
    }
    internal void Apply(SwordShieldEditPreview preview)
    {
        RequireEditable(); VerifySource();
        if (preview.Owner != _id || preview.DraftHash != Hash(_draft))
            throw new InvalidOperationException("La vista previa pertenece a otro proyecto o quedó desactualizada. Genérala de nuevo.");
        _ = new SwordShieldPersonalTable(preview.Result);
        if (_draft.AsSpan().SequenceEqual(preview.Result)) return;
        RecordPersonal(preview);_draft = (byte[])preview.Result.Clone(); _history.AddRange(preview.Changes);
        if(preview.RandomOptions is {} options) _randomizations.Add(new(SwordShieldPersonalRandomizer.Algorithm,options,preview.Changes.Count,preview.EligibleEntries,preview.ProtectedSpecies,preview.DraftHash,Hash(_draft)));
    }
    internal void Reset() { _draft = (byte[])_original.Clone(); _history.Clear(); _randomizations.Clear(); _gameplayDraft.Clear(); _gameplayHistory.Clear(); _surpriseDraft.Clear(); _surpriseHistory.Clear(); ResetPokeFeur(); _dataHistory.Clear(); _recipeJournal.Clear(); _mutationEpoch++; }
    internal SwitchExportResult Export(string destination, SwitchModLayout layout = SwitchModLayout.Atmosphere)
    {
        RequireEditable(); VerifySource();
        if (layout is not SwitchModLayout.Atmosphere and not SwitchModLayout.Ryujinx) throw new ArgumentOutOfRangeException(nameof(layout));
        if (!HasChanges) throw new InvalidOperationException("No hay cambios que exportar.");
        string target = Path.GetFullPath(destination);
        CheckNoLinks(target);
        if (Contains(Root, target) || Contains(target, Root)) throw new IOException("El mod debe exportarse fuera de la extracción original, en una carpeta nueva.");
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("La carpeta de salida ya existe. Elige un nombre nuevo; no se sobrescriben mods.");
        string parent = Path.GetDirectoryName(target) ?? throw new IOException("Destino inválido.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("La carpeta padre de salida no existe.");
        string stage = Path.Combine(parent, ".definitive-swsh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            string resource = layout == SwitchModLayout.Ryujinx
                ? Path.Combine(stage,"romfs",PersonalRelativePath)
                : Path.Combine(stage, "atmosphere", "contents", Identity.TitleId, "romfs", PersonalRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(resource)!);
            if (!_draft.AsSpan().SequenceEqual(_original)) File.WriteAllBytes(resource, _draft);
            if (File.Exists(resource))
            {
                byte[] reopened = File.ReadAllBytes(resource); _ = new SwordShieldPersonalTable(reopened);
                if (!reopened.AsSpan().SequenceEqual(_draft)) throw new IOException("La verificación del recurso exportado falló.");
            }
            ExportGameplay(stage, layout);
            ExportSurprise(stage, layout);
            ExportPokeFeur(stage, layout);
            var net = NetChanges();
            var manifest = new
            {
                Format = "DefinitiveRandomizer.SwitchMod", Version = 1, Prototype = true, Layout = layout.ToString(),
                Game = Identity.Game, Identity.TitleId, Identity.BuildId,
                ProfileVersion = Identity.ProfileVersion,
                VersionEvidence = "NSO Build ID reconocido; la mezcla base/actualización de RomFS y las licencias DLC no se verifican automáticamente.",
                SourcePersonalSha256 = SourceHash, ResourceSha256 = Hash(_draft),
                Resource = "romfs/" + PersonalRelativePath,
                Dependencies = _dependencies, ChangedValues = net.Count + GameplayNetChanges().Count + DataNetChanges().Count + (SurpriseEnabled?1:0) + (PokeFeurEnabled?1:0),
                Surprise = _surpriseHistory.ToArray(), NativeRuntimeExperimental = SurpriseEnabled,
                PokeFeur = _pokeHistory.ToArray(), PokeFeurProvider = PokeFeurEnabled?PokeFeurPackage.Commit:null,
                DataEditors = _dataHistory.ToArray(), DataChanges = DataNetChanges(), Gameplay = _gameplayHistory.ToArray(), Resources = ExportResources().ToArray(), GameplayChanges = GameplayNetChanges(),
                Changes = net, Operations = _history.ToArray(), Randomizations = _randomizations.ToArray(),
            };
            File.WriteAllBytes(Path.Combine(stage, "definitive-mod.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(stage, "LEEME.txt"),
                $"Prototipo Espada/Escudo · {Identity.Game} · perfil {Identity.ProfileVersion}\n" +
                "Contiene recursos modificados del mod y metadatos. No modifica tu ROM original.\n" +
                (layout == SwitchModLayout.Ryujinx ? "Copia esta carpeta completa a la carpeta de mods de tu título en Ryujinx; romfs debe quedar dentro de la carpeta del mod.\n" : "Copia la carpeta atmosphere a la SD de tu entorno con LayeredFS.\n") +
                "Comprueba juego, actualización y ausencia de otros mods sobre el mismo recurso. Retira el mod para volver a los datos originales.\n" +
                "No se ha certificado la jugabilidad de este prototipo. El manifiesto enumera cada cambio y sus hashes.\n");
            VerifySource(); CheckNoLinks(target);
            Directory.Move(stage, target);
            return new(target, RevisionHash, net.Count + GameplayNetChanges().Count + DataNetChanges().Count + (SurpriseEnabled?1:0) + (PokeFeurEnabled?1:0));
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    internal IReadOnlyList<SwordShieldChange> NetChanges()
    {
        var changes = new List<SwordShieldChange>();
        var beforeTable = new SwordShieldPersonalTable(_original); var afterTable = new SwordShieldPersonalTable(_draft);
        foreach(var e in afterTable.Entries())
        {
            var before=beforeTable.Read(e.Index);
            for(int f=0;f<SwordShieldPersonalTable.FieldCount;f++)
                if(before.Values[f]!=e.Values[f])changes.Add(new(e.Index,((SwordShieldField)f).ToString(),before.Values[f],e.Values[f]));
        }

        return changes.AsReadOnly();
    }
    private void RequireEditable()
    { if (!Identity.CanEdit) throw new InvalidOperationException("Build ID desconocido: solo lectura. Este prototipo admite el perfil 1.3.2 de Espada/Escudo."); }
    private void VerifySource()
    {
        VerifyDataListings();
        if(_surpriseSourceChecked||_pokeProfileChecked)VerifyNoSubsdk9();
        if(_trainerPaths is not null)
        {
            string folder=Path.Combine(Root,"romfs/bin/trainer/trainer_poke");CheckNoLinks(folder);
            if(!Directory.EnumerateFiles(folder,"*.bin").OrderBy(p=>p,StringComparer.Ordinal).SequenceEqual(_trainerPaths))throw new IOException("El listado de entrenadores cambió desde que se abrió.");
        }
        foreach (var pair in _dependencies)
        {
            string path = Path.Combine(Root, pair.Key); CheckNoLinks(path);
            if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != pair.Value)
                throw new IOException("La extracción cambió desde que se abrió: " + pair.Key + ". Vuelve a abrir el proyecto.");
        }
    }
    private static bool Contains(string parent, string child) =>
        string.Equals(Path.TrimEndingDirectorySeparator(parent), Path.TrimEndingDirectorySeparator(child), StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void CheckNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se admiten enlaces o junctions en las rutas de este prototipo: " + current);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
