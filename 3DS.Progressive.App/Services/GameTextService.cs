using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Live game text GARC backed by pk3DS's original encrypted TextFile codec.</summary>
public sealed record TextSearchHit(int FileId,int Line,string Text)
{
    public string Display=>$"Archivo #{FileId:000} · línea {Line}: {Text.Replace("\r"," ").Replace("\n"," ")}";
}

public sealed class GameTextService
{
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private readonly GARCReference _reference;
    private byte[] _sha;
    private readonly bool _story;
    private readonly Guid _owner=Guid.NewGuid();
    public string BankName=>_story?"Story Text":"Game Text";
    public IReadOnlyList<RecordEntry> Entries {get;private set;} = [];
    public GameTextService(GameProjectSession project,bool story=false)
    {
        _project=project;_config=project.GameConfiguration;_story=story;
        string bank=story?"storytext":"gametext";
        var original=_config.GetGARCReference(bank);
        _reference=original.LanguageVariant?original.GetRelativeGARC(_config.Language):original;
        _path=Path.Combine(_config.RomFS,_config.GetGARCFileName(bank));
        if(!File.Exists(_path))throw new FileNotFoundException("No se encontró el GARC de "+BankName+" en el idioma seleccionado.",_path);
        _sha=SHA256.HashData(File.ReadAllBytes(_path));
        RefreshCatalogue();
    }
    private void RefreshCatalogue()
    {
        var records=ReadRecords();
        Entries=Enumerable.Range(0,records.Length).Select(i=>
        {
            var lines=new TextFile(_config,records[i],_config.RemapCharacters).Lines;
            string sample=lines.FirstOrDefault(t=>!string.IsNullOrWhiteSpace(t))??"Sin mensajes visibles";
            sample=sample.Replace("\r"," ").Replace("\n"," ");if(sample.Length>55)sample=sample[..55]+"…";
            var tags=Tags(i);return new RecordEntry(i,$"{Describe(i)} · [{(tags.Count==0?"Sin clasificar":string.Join(" · ",tags))}] · {lines.Length} líneas");
        }).ToArray();
    }
    public static readonly string[] TagChoices=["Intercambios","NPCs","Historia principal","Diálogos importantes","Combate","Menús","Pokédex","Nombres y descripciones"];
    private string TagsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DefinitiveRandomizer","TextTags",Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(_project.ProjectRoot).ToUpperInvariant())))[..24]+$"-{(_story?"story":"game")}-{_config.Language}.json");
    private Dictionary<int,string[]>? _tags;
    public IReadOnlyList<string> Tags(int id)
    {
        if(_tags is null)_tags=File.Exists(TagsPath)?System.Text.Json.JsonSerializer.Deserialize<Dictionary<int,string[]>>(File.ReadAllText(TagsPath))??[]:[];
        if(_tags.TryGetValue(id,out var tags))return tags;
        if(!_story)
        {
            var reference=_config.GameText.FirstOrDefault(t=>t.Index==id);
            if(reference is not null)return reference.Name switch
            {
                TextName.TrainerText=>["Combate"],TextName.PokedexEntry1 or TextName.PokedexEntry2 or TextName.SpeciesClassifications=>["Pokédex"],
                TextName.TrainerNames or TextName.TrainerClasses or TextName.AbilityNames or TextName.ItemNames or TextName.ItemFlavor or TextName.MoveNames or TextName.MoveFlavor or TextName.SpeciesNames or TextName.Types or TextName.Natures or TextName.Forms=>["Nombres y descripciones"],_=>[]
            };
        }
        return [];
    }
    public void SetTags(int id,IEnumerable<string> tags)
    {
        if(!Entries.Any(e=>e.Id==id))throw new ArgumentOutOfRangeException(nameof(id));
        var next=tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(next.Any(t=>!TagChoices.Contains(t)))throw new ArgumentException("Etiqueta no admitida.");
        _=Tags(id);var updated=new Dictionary<int,string[]>(_tags!){[id]=next};
        Directory.CreateDirectory(Path.GetDirectoryName(TagsPath)!);
        string temp=TagsPath+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(updated));File.Move(temp,TagsPath,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        _tags=updated;RefreshCatalogue();
    }
    public string Location=>$"{_project.GameName} · idioma {_config.Language} · {_config.GetGARCFileName(_story?"storytext":"gametext")}";
    private string Describe(int id)
    {
        if(_story)return $"Diálogos y eventos · archivo {id:000}";
        var reference=_config.GameText.FirstOrDefault(t=>t.Index==id);
        return reference is null?$"Otros textos · archivo {id:000}":reference.Name switch
        {
            TextName.AbilityNames=>"Nombres de habilidades",TextName.MoveNames=>"Nombres de movimientos",
            TextName.MoveFlavor=>"Descripciones de movimientos",TextName.ItemNames=>"Nombres de objetos",
            TextName.ItemFlavor=>"Descripciones de objetos",TextName.SpeciesNames=>"Nombres de Pokémon",
            TextName.Types=>"Tipos de Pokémon",TextName.Natures=>"Naturalezas",TextName.Forms=>"Nombres de formas",
            TextName.TrainerNames=>"Nombres de entrenadores",TextName.TrainerClasses=>"Clases de entrenadores",
            TextName.TrainerText=>"Mensajes de entrenadores",TextName.metlist_000000=>"Nombres de lugares",
            TextName.OPowerFlavor=>"Descripciones de Poderes O",TextName.MaisonTrainerNames=>"Entrenadores de Mansión Batalla",
            TextName.SuperTrainerNames=>"Entrenadores especiales",TextName.BattleRoyalNames=>"Entrenadores de Battle Royal",
            TextName.BattleTreeNames=>"Entrenadores de Árbol de Combate",TextName.SpeciesClassifications=>"Categorías de Pokémon",
            TextName.PokedexEntry1=>"Descripciones Pokédex · versión 1",TextName.PokedexEntry2=>"Descripciones Pokédex · versión 2",
            _=>reference.Name.ToString()
        };
    }
    public IReadOnlyList<TextSearchHit> Search(string query,int limit=500)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        if(limit<1||limit>5000)throw new ArgumentOutOfRangeException(nameof(limit));
        query=query.Trim();var results=new List<TextSearchHit>();var records=ReadRecords();
        for(int file=0;file<records.Length;file++)
        {
            var lines=new TextFile(_config,records[file],_config.RemapCharacters).Lines;
            for(int line=0;line<lines.Length;line++)
                if(lines[line].Contains(query,StringComparison.OrdinalIgnoreCase))
                {results.Add(new(file,line,lines[line]));if(results.Count==limit)return results;}
        }
        return results;
    }
    private byte[][] ReadRecords()=>new GARC.MemGARC(GarcTransaction.ReadUnchanged(_path,_sha)).Files;
    public IReadOnlyList<string> Read(int id)
    {
        var data=ReadRecords();
        if(id<0||id>=data.Length)throw new ArgumentOutOfRangeException(nameof(id));
        return new TextFile(_config,data[id],_config.RemapCharacters).Lines;
    }
    public void Save(int id,IReadOnlyList<string> text)
    {
        var records=ReadRecords();
        if(id<0||id>=records.Length)throw new ArgumentOutOfRangeException(nameof(id));
        if(text.Count>ushort.MaxValue)throw new ArgumentException("Demasiadas líneas.");
        string[] before=new TextFile(_config,records[id],_config.RemapCharacters).Lines;
        string[] after=text.Select(line=>line??string.Empty).ToArray();
        if(before.SequenceEqual(after))return;
        byte[] bytes=TextFile.GetBytes(_config,after,_config.RemapCharacters);
        string[] confirmed=new TextFile(_config,bytes,_config.RemapCharacters).Lines;
        if(!after.SequenceEqual(confirmed))
            throw new InvalidDataException("La codificación del texto no es reversible. No se guardará.");
        records[id]=bytes;
        if(before.Length!=after.Length)
            throw new ArgumentException("No se admite cambiar la cantidad de líneas del archivo.");
        var changes=Enumerable.Range(0,before.Length)
            .Where(i=>before[i]!=after[i])
            .Select(i=>new BatchChange(id,$"Archivo {id:000}","Línea "+i,before[i],after[i])).ToArray();
        byte[] source=GarcTransaction.ReadUnchanged(_path,_sha);
        byte[] output=GarcTransaction.Build(source,_reference,records,preserveRecordSize:false);
        _project.History.Commit([new FileChange(_path,source,output)],"Historia y textos",
            _story?"Editar Story Text":"Editar Game Text",changes,
            new Dictionary<string,string>{{"Bank",_story?"storytext":"gametext"},
                {"Language",_config.Language.ToString()}});
        _sha=SHA256.HashData(output);
        RefreshCatalogue();
        // Update only the modified loaded section; never discard the full localized text cache.
        if(!_story && id<_config.GameTextStrings.Length)_config.GameTextStrings[id]=after;
    }
    // Vanilla TextEditor-compatible full-bank dump. Keep escaped in-game
    // control sequences untouched; visually stripped dumps cannot be imported.
    public string ExportAll()
    {
        var files=ReadRecords();
        var sb=new System.Text.StringBuilder();
        for(int i=0;i<files.Length;i++)
        {
            sb.AppendLine("~~~~~~~~~~~~~~~");
            sb.AppendLine("Text File : "+i);
            sb.AppendLine("~~~~~~~~~~~~~~~");
            foreach(string line in new TextFile(_config,files[i],_config.RemapCharacters).Lines)
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    public TextBankPreview PreviewImportAll(string dump)
    {
        if(string.IsNullOrWhiteSpace(dump))
            throw new InvalidDataException("El archivo de importación está vacío.");
        byte[] original=GarcTransaction.ReadUnchanged(_path,_sha);
        var files=new GARC.MemGARC(original).Files;
        using var reader=new StringReader(dump);
        var changes=new List<BatchChange>();
        string? separator=null;
        for(int i=0;i<files.Length;i++)
        {
            separator ??=reader.ReadLine();
            if(separator!="~~~~~~~~~~~~~~~")
                throw new InvalidDataException($"Falta el separador del archivo {i}.");
            string header=reader.ReadLine()??"";
            if(header!="Text File : "+i)
                throw new InvalidDataException($"Se esperaba 'Text File : {i}', encontrado '{header}'.");
            if(reader.ReadLine()!="~~~~~~~~~~~~~~~")
                throw new InvalidDataException($"Falta el encabezado completo de archivo {i}.");
            var lines=new List<string>();
            string? next;
            while((next=reader.ReadLine()) is not null && next!="~~~~~~~~~~~~~~~")
                lines.Add(next);
            separator=next;
            string[] before=new TextFile(_config,files[i],_config.RemapCharacters).Lines;
            if(lines.Count!=before.Length)
                throw new InvalidDataException($"Archivo {i}: {lines.Count} líneas recibidas; " +
                    $"{before.Length} esperadas. No se moverán referencias de scripts.");
            if(before.SequenceEqual(lines))continue;
            byte[] encoded=TextFile.GetBytes(_config,lines.ToArray(),_config.RemapCharacters);
            var verified=new TextFile(_config,encoded,_config.RemapCharacters).Lines;
            if(!verified.SequenceEqual(lines))
                throw new InvalidDataException($"Archivo {i}: códigos de control o texto no reversibles.");
            for(int line=0;line<lines.Count;line++)
                if(before[line]!=lines[line])
                    changes.Add(new BatchChange(i,$"Archivo {i:000}",$"Línea {line}",before[line],lines[line]));
            files[i]=encoded;
        }
        if(separator is not null||reader.ReadLine() is not null)
            throw new InvalidDataException("El archivo importado contiene bancos de texto adicionales.");
        byte[] output=changes.Count==0?(byte[])original.Clone():
            GarcTransaction.Build(original,_reference,files,preserveRecordSize:false);
        if(changes.Count>0)
        {
            var rebuilt=new GARC.MemGARC(output).Files;
            if(rebuilt.Length!=files.Length||
               rebuilt.Where((bytes,i)=>!bytes.AsSpan().SequenceEqual(files[i])).Any())
                throw new InvalidDataException("No se pudo verificar el GARC de texto reconstruido.");
        }
        return new TextBankPreview(original,output,changes,_story);
    }

    public void ApplyImportAll(TextBankPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if(preview.Story!=_story||preview.Changes.Count==0)
            throw new ArgumentException("No hay diferencias válidas para este banco de texto.");
        byte[] current=GarcTransaction.ReadUnchanged(_path,_sha);
        if(!current.AsSpan().SequenceEqual(preview.Original))
            throw new IOException("El GARC cambió desde la vista previa de importación.");
        _project.History.Commit([new FileChange(_path,current,preview.Output)],
            "Historia y textos",_story?"Importar Story Text completo":"Importar Game Text completo",
            preview.Changes,new Dictionary<string,string>
            {["Bank"]=_story?"storytext":"gametext",["Language"]=_config.Language.ToString(),
             ["Scope"]="All"});
        _sha=SHA256.HashData(preview.Output);
        RefreshCatalogue();
        if(!_story)_config.InitializeGameText();
    }

    public StoryRandomPreview PreviewRandomize(int? fileId,int seed)
    {
        if(!_story)throw new InvalidOperationException("La randomización protegida solo está disponible en Story Text.");
        byte[] source=GarcTransaction.ReadUnchanged(_path,_sha);var records=new GARC.MemGARC(source).Files;
        int[] ids=fileId is int selectedId?new[]{selectedId}:Enumerable.Range(0,records.Length).ToArray();
        if(ids.Any(id=>id<0||id>=records.Length))throw new ArgumentOutOfRangeException(nameof(fileId));
        var lines=ids.ToDictionary(id=>id,id=>new TextFile(_config,records[id],_config.RemapCharacters).Lines);
        // Preserve empty placeholders, variables and every escaped/control line
        // at its original index. Shuffle only literal dialogue messages.
        bool Eligible(string line)=>line.Length>0&&!line.Contains('[')&&!line.Contains(']')&&!line.Contains('\\')&&!line.Any(char.IsControl);
        var slots=ids.SelectMany(id=>lines[id].Select((text,index)=>(Id:id,Index:index,Text:text))).Where(slot=>Eligible(slot.Text)).ToArray();
        string[] pool=slots.Select(slot=>slot.Text).ToArray();var random=new Random(seed);
        for(int i=pool.Length-1;i>0;i--){int other=random.Next(i+1);(pool[i],pool[other])=(pool[other],pool[i]);}
        var changes=new List<BatchChange>();
        for(int i=0;i<slots.Length;i++)
        {
            var slot=slots[i];if(slot.Text==pool[i])continue;
            lines[slot.Id][slot.Index]=pool[i];changes.Add(new(slot.Id,$"Archivo {slot.Id:000}","Línea "+slot.Index,slot.Text,pool[i]));
        }
        foreach(int id in changes.Select(change=>change.Id).Distinct())
        {
            byte[] next=TextFile.GetBytes(_config,lines[id],_config.RemapCharacters);
            if(!new TextFile(_config,next,_config.RemapCharacters).Lines.SequenceEqual(lines[id]))throw new InvalidDataException("La codificación del diálogo no es reversible.");
            records[id]=next;
        }
        byte[] output=changes.Count==0?source:GarcTransaction.Build(source,_reference,records,preserveRecordSize:false);
        return new(_owner,new FileChange(_path,source,output),changes.AsReadOnly(),seed,fileId);
    }
    public void ApplyRandomize(StoryRandomPreview preview)
    {
        if(!_story||preview.Owner!=_owner||preview.Changes.Count==0)throw new InvalidOperationException("La propuesta no pertenece a este editor o no tiene cambios.");
        GarcTransaction.ReadUnchanged(_path,_sha);GarcTransaction.ReadUnchanged(_path,preview.File.Fingerprint);
        _project.History.Commit([preview.File],"Historia y textos","Randomizar diálogos protegidos",preview.Changes,
            new Dictionary<string,string>{{"Bank","storytext"},{"Language",_config.Language.ToString()},{"PreviewSeed",preview.Seed.ToString()},
                {"Scope",preview.FileId?.ToString()??"All"},{"ProtectControlLines","True"}});
        _sha=SHA256.HashData(preview.File.Proposed());
        RefreshCatalogue();
    }
    public string Export(int id)=>string.Join(Environment.NewLine,Read(id).Select((line,i)=>$"[{i:0000}] {line}"));
}

public sealed record TextBankPreview(byte[] Original,byte[] Output,
    IReadOnlyList<BatchChange> Changes,bool Story);

public sealed class StoryRandomPreview
{
    internal Guid Owner {get;}
    internal FileChange File {get;}
    public IReadOnlyList<BatchChange> Changes {get;}
    public int Seed {get;}
    public int? FileId {get;}
    internal StoryRandomPreview(Guid owner,FileChange file,IReadOnlyList<BatchChange> changes,int seed,int? fileId)
    {Owner=owner;File=file;Changes=changes;Seed=seed;FileId=fileId;}
}
