using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Live game text GARC backed by pk3DS's original encrypted TextFile codec.</summary>
public sealed class GameTextService
{
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private readonly GARCReference _reference;
    private byte[] _sha;
    private readonly bool _story;
    public string BankName=>_story?"Story Text":"Game Text";
    public IReadOnlyList<RecordEntry> Entries {get;}
    public GameTextService(GameProjectSession project,bool story=false)
    {
        _project=project;_config=project.GameConfiguration;_story=story;
        string bank=story?"storytext":"gametext";
        var original=_config.GetGARCReference(bank);
        _reference=original.LanguageVariant?original.GetRelativeGARC(_config.Language):original;
        _path=Path.Combine(_config.RomFS,_config.GetGARCFileName(bank));
        if(!File.Exists(_path))throw new FileNotFoundException("No se encontró el GARC de "+BankName+" en el idioma seleccionado.",_path);
        _sha=SHA256.HashData(File.ReadAllBytes(_path));
        int count=new GARC.MemGARC(File.ReadAllBytes(_path)).Files.Length;
        Entries=Enumerable.Range(0,count).Select(i=>new RecordEntry(i,
            _story?$"Diálogo / evento {i:000}":$"Archivo de texto {i:000}")).ToArray();
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
        if(!_story)_config.InitializeGameText();
    }

    public string Export(int id)=>string.Join(Environment.NewLine,Read(id).Select((line,i)=>$"[{i:0000}] {line}"));
}

public sealed record TextBankPreview(byte[] Original,byte[] Output,
    IReadOnlyList<BatchChange> Changes,bool Story);
