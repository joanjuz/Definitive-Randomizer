using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class LevelUpMoveEntry : System.ComponentModel.INotifyPropertyChanged
{
    private int _level,_move;
    public int Level {get=>_level;set{if(_level==value)return;_level=value;PropertyChanged?.Invoke(this,new(nameof(Level)));}}
    // Notifies the change so the read-only move details (type, power…) of the level-up table follow the edit.
    public int Move {get=>_move;set{if(_move==value)return;_move=value;PropertyChanged?.Invoke(this,new(nameof(Move)));}}
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
/// <summary>Level-up moves behind <c>LevelUpView</c>: the 3DS GARC editor and the Nintendo DS learnsets.</summary>
public interface ILevelUpSource
{
    IReadOnlyList<PokemonListEntry> Entries {get;}
    IReadOnlyList<MoveChoice> Moves {get;}
    int Generation {get;}
    /// <summary>Most moves one species may have: 100 on 3DS; on DS the longest list of the game, which loads them into a fixed buffer.</summary>
    int MaxMoves {get;}
    /// <summary>Moves the per-species «Randomizar» may pick (moves the game can use).</summary>
    IReadOnlyList<int> RandomMovePool {get;}
    IReadOnlyList<LevelUpMoveEntry> Read(int species);
    void Save(int species,IReadOnlyList<LevelUpMoveEntry> rows);
    LevelUpBatchPreview PrepareRandomization(LevelUpRandomSettings settings,bool metronome=false);
    IReadOnlyDictionary<int,IReadOnlyList<LevelUpMoveEntry>> PreviewLearnsets(LevelUpBatchPreview preview);
    void ApplyRandomization(LevelUpBatchPreview preview);
    string ExportCsv();
}

/// <summary>Direct Gen6/Gen7 Learnset6 GARC editor with immutable disk fingerprints.</summary>
/// <summary>What the level-up table shows next to a move (3DS: read from the move data of the open game).</summary>
public sealed record LevelUpMoveDetails(string Type,string Category,int Power,int Accuracy,int PP);
public sealed class GameLevelUpService:ILevelUpSource
{
    internal Func<int,LevelUpMoveDetails?> MoveDetails {get;}
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private byte[] _sha;
    public IReadOnlyList<PokemonListEntry> Entries {get;}
    public IReadOnlyList<MoveChoice> Moves {get;}
    public int Generation=>_config.Generation;
    public int MaxMoves=>100;
    public IReadOnlyList<int> RandomMovePool=>Moves.Where(m=>m.Id>0&&m.Id<622&&m.Id is not (165 or 621 or 166 or 226)).Select(m=>m.Id).ToArray();
    public GameLevelUpService(GameProjectSession project)
    {
        _project=project;_config=project.GameConfiguration;
        MoveDetails=id=>id>0&&id<_config.Moves.Length&&_config.Moves[id] is {} move
            ?new LevelUpMoveDetails(move.Type>=0&&move.Type<project.Types.Count?project.Types[move.Type]:move.Type.ToString(),move.Category switch{0=>"Estado",1=>"Físico",2=>"Especial",_=>"—"},move.Power,move.Accuracy,move.PP):null;
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
        using var manual=ManualOperationScope.Begin("learnset","learnset:"+species,new{Id=species,Rows=rows});
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
        byte[] source=GarcTransaction.ReadUnchanged(_path,_sha);
        byte[] output=GarcTransaction.Build(source,_config.GetGARCReference("levelup"),files,
            preserveRecordSize:false);
        _project.History.Commit([new FileChange(_path,source,output)],"Level Up Moves",
            "Editar movimientos por nivel",
            before==after?[]:[new BatchChange(species,name,"Aprendizaje por nivel",before,after)]);
        _sha=SHA256.HashData(output);
        _config.InitializeLearnset();
    }
    public LevelUpBatchPreview PrepareRandomization(LevelUpRandomSettings settings,bool metronome=false)
    {
        if(settings.ExpandTo is <1 or >100||settings.SpreadTo is <1 or >100||
           settings.StabPercent is <0 or >100)
            throw new ArgumentException("Revisa la cantidad de movimientos, nivel final y porcentaje STAB.");
        byte[] before=GarcTransaction.ReadUnchanged(_path,_sha);
        byte[][] files=new GARC.MemGARC(before).Files;
        var sets=files.Select(data=>new Learnset6(data)).ToArray();
        if(metronome)
        {
            for(int i=1;i<sets.Length;i++)
            {
                sets[i].Moves=[118];sets[i].Levels=[1];
            }
        }
        else
        {
            var banned=new HashSet<int>([165,166,226,621]);
            banned.UnionWith(Legal.Z_Moves);
            if(settings.ExcludeFixedDamage)banned.UnionWith(MoveRandomizer.FixedDamageMoves);
            // Gen6 HM compatibility: the original game requires these machine moves.
            if(!settings.AllowHms&&_config.Generation==6)
                banned.UnionWith([15,19,57,70,127,249,291]);
            var randomizer=new LearnsetRandomizer(_config,sets)
            {
                Expand=settings.Expand,ExpandTo=settings.ExpandTo,
                Spread=settings.Spread,SpreadTo=settings.SpreadTo,
                STAB=settings.BiasByType,STABPercent=settings.StabPercent,
                STABFirst=settings.BiasByType,BannedMoves=banned.ToArray(),
                Learn4Level1=settings.StartWithFour
            };
            randomizer.Execute();
        }
        var changes=new List<BatchChange>();
        for(int i=1;i<files.Length;i++)
        {
            byte[] after=sets[i].Write();
            if(files[i].AsSpan().SequenceEqual(after))continue;
            files[i]=after;
            string name=Entries.FirstOrDefault(p=>p.Index==i)?.Name??$"Pokémon {i}";
            changes.Add(new BatchChange(i,name,"Aprendizaje por nivel",
                "Learnset original",metronome?"Metrónomo al nivel 1":"Learnset randomizado"));
        }
        byte[] result=GarcTransaction.Build(before,_config.GetGARCReference("levelup"),files,
            preserveRecordSize:false);
        return new LevelUpBatchPreview(before,result,changes,metronome?"Modo Metrónomo (todos)":"Randomizar todos",
            metronome?null:settings);
    }
    /// <summary>Read the exact frozen proposed GARC without touching disk or recomputing randomization.</summary>
    public IReadOnlyDictionary<int,IReadOnlyList<LevelUpMoveEntry>> PreviewLearnsets(LevelUpBatchPreview preview)
    {
        if(preview.Original.Length==0||preview.Output.Length==0)
            throw new ArgumentException("Vista previa vacía.");
        var files=new GARC.MemGARC(preview.Output).Files;
        return Entries.Where(e=>e.Index>0&&e.Index<files.Length).ToDictionary(e=>e.Index,
            e=>
            {
                var set=new Learnset6(files[e.Index]);
                return (IReadOnlyList<LevelUpMoveEntry>)set.Moves
                    .Select((move,index)=>new LevelUpMoveEntry{Move=move,Level=set.Levels[index]})
                    .ToArray();
            });
    }
    public void ApplyRandomization(LevelUpBatchPreview preview)
    {
        byte[] before=GarcTransaction.ReadUnchanged(_path,_sha);
        if(!before.AsSpan().SequenceEqual(preview.Original))
            throw new IOException("El GARC cambió desde la vista previa. Vuelve a generar la randomización.");
        _project.History.Commit([new FileChange(_path,before,preview.Output)],
            "Level Up Moves",preview.Action,preview.Changes);
        _sha=SHA256.HashData(preview.Output);
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

public sealed record LevelUpRandomSettings(bool AllowHms,bool BiasByType,int StabPercent,
    bool Expand,int ExpandTo,bool StartWithFour,bool Spread,int SpreadTo,bool ExcludeFixedDamage);
public sealed record LevelUpBatchPreview(byte[] Original,byte[] Output,
    IReadOnlyList<BatchChange> Changes,string Action,LevelUpRandomSettings? Settings)
{
    /// <summary>The source's own proposal (Nintendo DS); the 3DS one is the GARC above.</summary>
    public object? Token {get;init;}
}
