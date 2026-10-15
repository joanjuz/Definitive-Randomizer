using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;
public sealed partial class GameMachineTutorService
{
    private string? _machineTextPath;
    private byte[]? _machineTextOriginal;
    private GARCReference? _machineTextReference;
    private void CaptureMachineText()
    {
        var config=_project.GameConfiguration;
        _machineTextPath=Path.Combine(config.RomFS,config.GetGARCFileName("gametext"));
        if(!File.Exists(_machineTextPath))return;
        _machineTextOriginal=File.ReadAllBytes(_machineTextPath);
        var reference=config.GetGARCReference("gametext");_machineTextReference=reference.LanguageVariant?reference.GetRelativeGARC(config.Language):reference;
    }
    private FileChange SynchronizeMachineText(List<BatchChange> changes)
    {
        if(_machineTextPath is null||_machineTextOriginal is null||_machineTextReference is null)throw new InvalidDataException("Falta el banco de descripciones de MT.");
        var config=_project.GameConfiguration;byte[] source=GarcTransaction.ReadUnchanged(_machineTextPath,SHA256.HashData(_machineTextOriginal));
        byte[][] records=new GARC.MemGARC(source).Files;
        int item=config.GameText.First(row=>row.Name==TextName.ItemFlavor).Index,move=config.GameText.First(row=>row.Name==TextName.MoveFlavor).Index;
        if(item>=records.Length||move>=records.Length)throw new InvalidDataException("Banco de descripciones incompleto.");
        var items=new TextFile(config,records[item],config.RemapCharacters).Lines;
        var moves=new TextFile(config,records[move],config.RemapCharacters).Lines;
        int[] ids=[..Enumerable.Range(328,92),618,619,620,690,691,692,693,694];
        if(ids.Any(id=>id>=items.Length))throw new InvalidDataException("Faltan descripciones de objetos MT.");
        foreach(var row in Machines.Where(row=>row.MoveId!=_originalTm[row.Number-1]))
        {
            if(row.MoveId>=moves.Length)throw new InvalidDataException("Falta la descripción del movimiento elegido.");
            int id=ids[row.Number-1];string before=items[id];items[id]=moves[row.MoveId].Trim();
            if(before!=items[id])changes.Add(new(id,"Objeto MT","Descripción",before,items[id]));
        }
        byte[] encoded=TextFile.GetBytes(config,items,config.RemapCharacters);
        if(!new TextFile(config,encoded,config.RemapCharacters).Lines.SequenceEqual(items))throw new InvalidDataException("Las descripciones no conservan los caracteres del idioma activo.");
        records[item]=encoded;
        return new(_machineTextPath,source,GarcTransaction.Build(source,_machineTextReference,records,preserveRecordSize:false));
    }
}
