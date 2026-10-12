using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>USUM only: faithfully invokes the researched dual-hook installer in memory,
/// then commits Battle.cro + code.bin + history in one validated file transaction.</summary>
internal sealed class GamePlayerLevelCapService
{
    private readonly GameProjectSession _project;
    private readonly string _battlePath,_codePath;
    private readonly Guid _owner=Guid.NewGuid();
    private byte[] _battleHash,_codeHash;
    internal bool Supported {get;}
    internal string Status {get;private set;}="";
    internal GamePlayerLevelCapService(GameProjectSession project)
    {
        _project=project;Supported=project.GameConfiguration.USUM;
        var config=project.GameConfiguration;
        _battlePath=Find(config.RomFS,"Battle.cro",true);
        _codePath=Find(config.ExeFS,"code.bin",false);
        _battleHash=File.Exists(_battlePath)?SHA256.HashData(File.ReadAllBytes(_battlePath)):[];
        _codeHash=File.Exists(_codePath)?SHA256.HashData(File.ReadAllBytes(_codePath)):[];
        RefreshState();
    }
    private static string Find(string root,string filename,bool nested)
    {
        if(!Directory.Exists(root))return Path.Combine(root,filename);
        var direct=nested?new[]{Path.Combine(root,filename),Path.Combine(root,"dll",filename)}:
            new[]{Path.Combine(root,".code.bin"),Path.Combine(root,filename)};
        foreach(var path in direct)if(File.Exists(path))return path;
        return Directory.EnumerateFiles(root,nested?filename:"*code*.bin",
            nested?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly).FirstOrDefault()
            ??Path.Combine(root,filename);
    }
    private (byte[] Battle,byte[] Code) Read()
    {
        if(!Supported)throw new NotSupportedException("Player Level Caps solo admite Ultra Sun / Ultra Moon.");
        if(!File.Exists(_battlePath)||!File.Exists(_codePath))
            throw new FileNotFoundException("Se necesitan Battle.cro y code.bin descomprimido.");
        byte[] battle=GarcTransaction.ReadUnchanged(_battlePath,_battleHash);
        byte[] code=GarcTransaction.ReadUnchanged(_codePath,_codeHash);
        if(battle.Length<0x180||Encoding.ASCII.GetString(battle,0x80,4)!="CRO0")
            throw new InvalidDataException("Battle.cro no tiene la cabecera CRO0 esperada.");
        if(code.Length==0||(code.Length&0x1FF)!=0)
            throw new InvalidDataException("code.bin debe estar descomprimido y alineado a 0x200.");
        return(battle,code);
    }
    internal void RefreshState()
    {
        if(!Supported){Status="Solo disponible para USUM.";return;}
        try
        {
            var (battle,code)=Read();
            var a=LevelCapPatch.GetBattleState(battle,out _);
            var b=LevelCapPatch.GetCandyState(code,out _);
            Status=$"Battle.cro: {a} · Rare Candy/code.bin: {b}";
        }
        catch(Exception ex){Status="Sin parche disponible: "+ex.Message;}
    }
    internal LevelCapTable StartingTable()
    {
        if(Supported)
            try
            {
                var (battle,_)=Read();
                if(LevelCapPatch.TryReadInstalledBattleTable(battle,out var installed)&&installed is not null)
                    return installed;
            }
            catch(IOException){}
            catch(InvalidDataException){}
        return LevelCapTable.Default();
    }
    internal LevelCapInstallPreview Preview(LevelCapTable table)
    {
        if(table.Validate() is {Count:>0} problems)
            throw new InvalidDataException("Player Level Caps: "+problems[0]);
        var (battle,code)=Read();
        var install=LevelCapPatch.InstallManaged(battle,code,table);
        if(!install.Success)
            throw new InvalidDataException("Firma/hook incompatible: no se escribirá nada. "+
                string.Join(" | ",install.Sites.Select(s=>s.ToString())));
        return new(_owner,table,[new FileChange(_battlePath,battle,install.BattleCro),
            new FileChange(_codePath,code,install.CodeBin)],
            string.Join(Environment.NewLine,install.Sites.Select(x=>x.ToString())));
    }
    internal void Apply(LevelCapInstallPreview proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("Vista previa de otro proyecto.");
        foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,"Mecánicas y CRO","Instalar Player Level Caps",
            proposal.Table.Entries.Select((entry,i)=>new BatchChange(i,entry.Label,
                "Cap / "+entry.Kind,"",entry.Cap.ToString())),
            new Dictionary<string,string>{{"Scope","Player Level Caps USUM · Battle.cro + code.bin"},
                {"Flags",string.Join(";",proposal.Table.Entries.Select(e=>
                    $"{e.Kind}/{e.FlagOffset:X4}/{e.FlagBit:X4}/Lv{e.Cap}"))}});
        _battleHash=SHA256.HashData(File.ReadAllBytes(_battlePath));
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));
        RefreshState();
    }
    // Only trainer fights whose save-derived checkpoint is researched in Progressive.
    private static readonly (int Trainer,string Label)[] ProvenTrainers=
    [
        (23,"Defeat Kahuna Hala"),(90,"Defeat Olivia - Mayla"),
        (235,"Defeat Guzma"),(154,"Defeat Nanu - Denio"),(131,"Defeat Lusamine"),
        (497,"Defeat Hapu - Hela"),(416,"Defeat Gladion - Mount Lanakila"),
        (156,"Elite Four - Kahili"),(489,"Elite Four - Molayne (Lario)"),
        (153,"Elite Four - Olivia (Mayla"),(149,"Elite Four - Acerola (Zarala)"),
        (496,"Defeat Champion Hau (Tilo)")
    ];
    internal LevelCapTable SyncFromTrainerRules(LevelCapTable current,out int updated)
    {
        var trainers=new GameTrainerService(_project).GetRuleRows()
            .Where(x=>x.CapUse).ToDictionary(x=>x.Id);
        var result=current.Entries.ToList();
        updated=0;
        foreach(var (trainer,label) in ProvenTrainers)
        {
            if(!trainers.TryGetValue(trainer,out var row))continue;
            int i=result.FindIndex(x=>x.Label==label);
            if(i<0)continue;
            int level=row.Cap>0?row.Cap:row.Ace;
            if(level is <1 or >100)continue;
            if(result[i].Cap==level)continue;
            result[i]=result[i] with {Cap=(byte)level};updated++;
        }
        byte previous=0;
        for(int i=0;i<result.Count;i++)
        {
            byte level=(byte)Math.Max(previous,result[i].Cap);
            result[i]=result[i] with {Cap=level};previous=level;
        }
        var table=new LevelCapTable {Entries=result};
        if(table.Validate().Count>0)
            throw new InvalidDataException("Los niveles sincronizados no superan la validación de Player Level Caps.");
        return table;
    }
}
internal sealed record LevelCapInstallPreview(Guid Owner,LevelCapTable Table,
    IReadOnlyList<FileChange> Files,string Report);
