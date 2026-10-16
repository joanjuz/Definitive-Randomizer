using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class MachineMoveRow : INotifyPropertyChanged
{
    public int Number {get;init;}
    private int _moveId;
    public int MoveId {get=>_moveId;set{if(_moveId==value)return;_moveId=value;Changed();}}
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class TutorPriceRow : INotifyPropertyChanged
{
    public int Number {get;init;}
    public string Group {get;init;}="";
    private int _moveId;
    public int MoveId {get=>_moveId;set{if(_moveId==value)return;_moveId=value;Changed();}}
    public string MoveName {get;init;}="";
    private int _price;
    public int Price {get=>_price;set{if(_price==value)return;_price=value;Changed();}}
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}

/// <summary>Reads exact active Gen7 TM code and Shop.cro tutor layout; writes only the verified table spans.</summary>
public sealed partial class GameMachineTutorService
{
    private readonly GameProjectSession _project;
    private readonly string _codePath,_shopPath;
    private readonly byte[] _codeOriginal;
    private byte[]? _shopOriginal;
    private readonly int _tmOffset,_tutorOffset;
    private readonly ushort[] _originalTm;
    private readonly int[] _originalPrices;
    private readonly int[] _originalTutorMoves;
    private int[]? _generatedMachines,_generatedTutorMoves;
    private object[] ManualMachines()=>Machines.Where((row,i)=>row.MoveId!=(_generatedMachines is null?_originalTm[i]:_generatedMachines[i])).Select(r=>(object)new{r.Number,r.MoveId}).ToArray();
    private object[] ManualTutors(bool full)=>Tutors.Select((r,i)=>new{r.Number,r.MoveId,r.Price,MoveChanged=full&&r.MoveId!=(_generatedTutorMoves is null?_originalTutorMoves[i]:_generatedTutorMoves[i]),PriceChanged=r.Price!=_originalPrices[i]}).Where(r=>r.MoveChanged||r.PriceChanged).Cast<object>().ToArray();
    public IReadOnlyList<MachineMoveRow> Machines {get;}
    public IReadOnlyList<TutorPriceRow> Tutors {get;}
    public IReadOnlyList<MoveChoice> MoveChoices {get;}
    public bool CanEditTms=>Machines.Count==100;
    public bool CanEditTutorPrices=>_shopOriginal is not null && Tutors.Count>0;
    public bool CanEditTutorMoves=>_project.GameConfiguration.USUM&&_shopOriginal is not null&&Tutors.Count==67;
    public GameMachineTutorService(GameProjectSession project)
    {
        _project=project;CaptureMachineText();
        if(project.Generation!=7)throw new NotSupportedException("La edición de MT y tutores Gen 6 requiere el formato de esa generación.");
        var config=project.GameConfiguration;
        _codePath=Directory.EnumerateFiles(config.ExeFS).FirstOrDefault(p=>
            Path.GetFileName(p).Contains("code",StringComparison.OrdinalIgnoreCase))
            ??throw new FileNotFoundException("No se encontró code.bin.");
        _codeOriginal=File.ReadAllBytes(_codePath);
        byte[] signature=[0x03,0x40,0x03,0x41,0x03,0x42,0x03,0x43,0x03];
        if(_codeOriginal.Length<0x400000+signature.Length+0x22+200||_codeOriginal.Length%0x200!=0)
            throw new InvalidDataException("code.bin no está descomprimido o tiene un formato incompatible.");
        int found=_codeOriginal.AsSpan(0x400000).IndexOf(signature);
        if(found<0)throw new InvalidDataException("No se encontró la firma original de MT dentro de code.bin.");
        _tmOffset=checked(0x400000+found+signature.Length+(config.USUM?0x22:0));
        if(_tmOffset+200>_codeOriginal.Length)throw new InvalidDataException("La tabla de MT supera code.bin.");
        _originalTm=Enumerable.Range(0,100).Select(i=>BitConverter.ToUInt16(_codeOriginal,_tmOffset+2*i)).ToArray();
        Machines=_originalTm.Select((move,i)=>new MachineMoveRow {Number=i+1,MoveId=move}).ToArray();
        MoveChoices=project.MoveNames.Take(Math.Min(project.MoveNames.Count,config.Moves.Length)).Select((name,i)=>new MoveChoice(i,
            string.IsNullOrWhiteSpace(name)?$"Movimiento ID {i}":name)).ToArray();
        _shopPath=Path.Combine(config.RomFS,"Shop.cro");
        var tutors=new List<TutorPriceRow>();
        var prices=new List<int>();
        if(File.Exists(_shopPath))
        {
            byte[] data=File.ReadAllBytes(_shopPath);
            byte[] counts=[];int offset=-1;
            if(config.USUM)
            {
                if(Gen7ShopCroLayout.TryRead(data,out var layout,out _))
                {counts=layout.BPTutors.Counts.ToArray();offset=checked((int)layout.BPTutors.DataStart);}
            }
            else if(data.Length>=0x54DE)
            {counts=data.Skip(0x52D2).Take(4).ToArray();offset=0x54DE;}
            int total=counts.Sum(n=>(int)n);
            if(total is >0 and <=256 && offset>=0 && (long)offset+total*4<=data.Length)
            {
                _shopOriginal=data;_tutorOffset=offset;int current=0;
                for(int group=0;group<counts.Length;group++)
                    for(int n=0;n<counts[group];n++,current++)
                    {
                        int at=offset+current*4;
                        ushort move=BitConverter.ToUInt16(data,at);
                        ushort price=BitConverter.ToUInt16(data,at+2);
                        tutors.Add(new TutorPriceRow {Number=current+1,Group=$"Sede {group+1}",
                            MoveId=move,MoveName=project.GetMoveLabel(move),Price=price});
                        prices.Add(price);
                    }
            }
        }
        Tutors=tutors;_originalPrices=prices.ToArray();
        _originalTutorMoves=tutors.Select(t=>t.MoveId).ToArray();
    }
    public void RandomizeMachines()=>RandomizeMachines(RandomNumberGenerator.GetInt32(int.MaxValue));
    public void RandomizeMachines(int seed)
    {
        // Equivalent safe move pool: do not assign Z-moves, Struggle or banned vanilla exceptions.
        int[] excluded=[165,621,166,226];
        var pool=Enumerable.Range(1,Math.Max(0,Math.Min(_project.MoveNames.Count,_project.GameConfiguration.Moves.Length)-1))
            .Where(id=>!Legal.Z_Moves.Contains(id)&&!excluded.Contains(id)).ToArray();
        if(pool.Length<100)throw new InvalidDataException("No hay suficientes movimientos válidos para randomizar MT.");
        var random=new Random(seed);
        for(int i=pool.Length-1;i>0;i--){int j=random.Next(i+1);(pool[i],pool[j])=(pool[j],pool[i]);}
        int cursor=0;
        foreach(var row in Machines)
            if(!excluded.Contains(row.MoveId)&&!Legal.Z_Moves.Contains(row.MoveId))
                row.MoveId=pool[cursor++];
        _generatedMachines=Machines.Select(r=>r.MoveId).ToArray();
    }
    internal (IReadOnlyList<FileChange> Files,IReadOnlyList<BatchChange> Changes) PrepareMachines(bool synchronizeDescriptions)
    {
        int[] changed=Machines.Select(r=>r.MoveId).ToArray();
        if(changed.Length!=100||Machines.Select(row=>row.Number).Where((number,index)=>number!=index+1).Any()||changed.Any(id=>id<0||id>=Math.Min(_project.MoveNames.Count,_project.GameConfiguration.Moves.Length)))
            throw new ArgumentException("Existe un movimiento de MT inválido.");
        byte[] current=GarcTransaction.ReadUnchanged(_codePath,SHA256.HashData(_codeOriginal));
        byte[] output=(byte[])current.Clone();
        foreach(var row in Machines)BitConverter.GetBytes((ushort)row.MoveId).CopyTo(output,_tmOffset+(row.Number-1)*2);
        var differences=Machines.Where(r=>_originalTm[r.Number-1]!=r.MoveId).Select(r=>
            new BatchChange(r.Number,$"MT {r.Number:000}","Movimiento",
                _project.GetMoveLabel(_originalTm[r.Number-1]),_project.GetMoveLabel(r.MoveId))).ToList();
        var files=new List<FileChange>{new(_codePath,current,output)};if(synchronizeDescriptions)files.Add(SynchronizeMachineText(differences));
        return (files,differences);
    }
    public void SaveMachines(bool synchronizeDescriptions=false)
    {
        var explicitRows=ManualMachines();
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:explicitRows.Length>0?ManualOperationScope.Begin("machines7","machines7",new{Rows=explicitRows,SynchronizeDescriptions=synchronizeDescriptions}):_generatedMachines is not null?ManualOperationScope.Begin("random-only","",new{ActionId="tms.randomize"}):null;
        var preview=PrepareMachines(synchronizeDescriptions);
        _project.History.Commit(preview.Files,"MT y tutores","Editar MT",preview.Changes);
        if(synchronizeDescriptions)_project.GameConfiguration.InitializeGameText();
        _project.RefreshMoveAssignments();
        // The shell refreshes assignment names only after project reload; do not trust cached IDs.
    }
    public void SaveTutorPrices()
    {
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:ManualOperationScope.Begin("tutors7","tutors7",new{Rows=ManualTutors(false)});
        if(_shopOriginal is null)throw new NotSupportedException("No se encontró una tabla de tutores editable.");
        if(Tutors.Any(t=>t.Price is <0 or >65535))throw new ArgumentException("Los precios deben estar entre 0 y 65535.");
        byte[] current=GarcTransaction.ReadUnchanged(_shopPath,SHA256.HashData(_shopOriginal));
        byte[] output=(byte[])current.Clone();
        foreach(var row in Tutors)BitConverter.GetBytes((ushort)row.Price).CopyTo(output,_tutorOffset+(row.Number-1)*4+2);
        var differences=Tutors.Where(r=>_originalPrices[r.Number-1]!=r.Price).Select(r=>
            new BatchChange(r.Number,r.MoveName,"Precio BP",_originalPrices[r.Number-1].ToString(),r.Price.ToString())).ToArray();
        _project.History.Commit([new FileChange(_shopPath,current,output)],"MT y tutores","Editar precios de tutores",differences);
    }
}
