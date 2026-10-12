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
    public string Export(int id)=>string.Join(Environment.NewLine,Read(id).Select((line,i)=>$"[{i:0000}] {line}"));
}
