using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class LevelUpMoveEntry
{
    public int Level {get;set;}
    public int Move {get;set;}
}
/// <summary>Direct Gen6/Gen7 Learnset6 GARC editor with immutable disk fingerprints.</summary>
public sealed class GameLevelUpService
{
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private byte[] _sha;
    public IReadOnlyList<PokemonListEntry> Entries {get;}
    public IReadOnlyList<MoveChoice> Moves {get;}
    public GameLevelUpService(GameProjectSession project)
    {
        _project=project;_config=project.GameConfiguration;
        _path=Path.Combine(_config.RomFS,_config.GetGARCFileName("levelup"));
        if(!File.Exists(_path))throw new FileNotFoundException("Falta el archivo levelup GARC.",_path);
        _sha=SHA256.HashData(File.ReadAllBytes(_path));
        var count=new GARC.MemGARC(File.ReadAllBytes(_path)).Files.Length;
        Entries=project.Entries.Where(p=>p.Index<count).ToArray();
        Moves=project.MoveNames.Select((name,id)=>new MoveChoice(id,
            string.IsNullOrWhiteSpace(name)?$"Movimiento {id:000}":name)).ToArray();
    }
    private byte[][] ReadRecords()
    {
        byte[] source=GarcTransaction.ReadUnchanged(_path,_sha);
        return new GARC.MemGARC(source).Files;
    }
    public IReadOnlyList<LevelUpMoveEntry> Read(int species)
    {
        byte[][] records=ReadRecords();
        if(species<=0||species>=records.Length)throw new ArgumentOutOfRangeException(nameof(species));
        var set=new Learnset6(records[species]);
        return set.Moves.Select((move,i)=>new LevelUpMoveEntry{Move=move,Level=set.Levels[i]}).ToArray();
    }
    public void Save(int species,IReadOnlyList<LevelUpMoveEntry> rows)
    {
        if(species<=0||!Entries.Any(e=>e.Index==species))throw new ArgumentOutOfRangeException(nameof(species));
        if(rows.Count>100)throw new ArgumentException("No se admiten más de 100 movimientos por especie.");
        if(rows.Any(row=>row.Level is <1 or >100||row.Move<1||row.Move>=Moves.Count))
            throw new ArgumentException("Cada movimiento debe tener un nivel 1–100 y un ID válido.");
        var ordered=rows.OrderBy(r=>r.Level).ToArray();
        byte[][] files=ReadRecords();
        var original=new Learnset6(files[species]);
        var replacement=new Learnset6([255,255,255,255])
        {
            Moves=ordered.Select(r=>r.Move).ToArray(),
            Levels=ordered.Select(r=>r.Level).ToArray()
        };
        files[species]=replacement.Write();
        string name=Entries.First(e=>e.Index==species).Name;
        var before=string.Join("; ",original.Moves.Select((m,i)=>$"{original.Levels[i]}:{m}"));
        var after=string.Join("; ",ordered.Select(r=>$"{r.Level}:{r.Move}"));
        _sha=_project.History.CommitGarc(_path,_config.GetGARCReference("levelup"),_sha,files,
            "Level Up Moves","Editar movimientos por nivel",
            before==after?[]:[new BatchChange(species,name,"Aprendizaje por nivel",before,after)]);
        _config.InitializeLearnset();
    }
    public string ExportCsv()
    {
        var lines=new List<string>{"PokemonID,Pokemon,Level,MoveID,Move"};
        foreach(var entry in Entries)
            foreach(var row in Read(entry.Index))
            {
                string species="\"" + entry.Name.Replace("\"","\"\"") + "\"";
                string moveName="\"" + _project.GetMoveLabel(row.Move).Replace("\"","\"\"") + "\"";
                lines.Add($"{entry.Index},{species},{row.Level},{row.Move},{moveName}");
            }
        return string.Join(Environment.NewLine,lines);
    }
}
