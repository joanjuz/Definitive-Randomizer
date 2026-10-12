using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Properties;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Exact original shiny reroll instruction table and PID hook, safely staged.</summary>
internal sealed class GameShinyRateService
{
    private readonly GameProjectSession _project;
    private readonly string _codePath;
    private byte[] _sha;
    private static readonly byte[] PidSignature=[0x01,0x50,0x85,0xE2,0x05,0x00,0x50,0xE1,0xDE,0xFF,0xFF,0xCA];
    private static readonly byte[] ShinySignature=[0x00,0x20,0x22,0xE0,0x02,0x30,0x21,0xE2,
        0x03,0x20,0x92,0xE1,0x1C,0x00,0x00];
    private static readonly byte[] VanillaReroll=[0x23,0x00,0xD4,0xE5];
    private static readonly (int Count,ushort Instr)[] Instructions=ParseInstructions();
    internal GameShinyRateService(GameProjectSession project)
    {
        _project=project;
        string exefs=project.GameConfiguration.ExeFS;
        _codePath=new[]{Path.Combine(exefs,".code.bin"),Path.Combine(exefs,"code.bin")}
            .FirstOrDefault(File.Exists)??throw new FileNotFoundException("Falta code.bin sin comprimir.");
        byte[] source=File.ReadAllBytes(_codePath);
        if(source.Length%0x200!=0)throw new InvalidDataException("code.bin no está descomprimido.");
        _sha=SHA256.HashData(source);
        _=Decode(source);
    }
    private static (int Count,ushort Instr)[] ParseInstructions()
    {
        byte[] data=Resources.asm_mov;
        if(data.Length%4!=0||data.Length==0)throw new InvalidDataException("Tabla ARM asm_mov incompleta.");
        return Enumerable.Range(0,data.Length/4).Select(i=>
            (Count:(int)BitConverter.ToUInt16(data,i*4),
             Instr:BitConverter.ToUInt16(data,i*4+2))).ToArray();
    }
    internal sealed record ShinyState(bool Vanilla,int Rerolls,int? AlwaysShinyOffset,bool AlwaysShiny);
    private static (ShinyState State,int Offset) Decode(byte[] raw)
    {
        int sig=raw.AsSpan().IndexOf(PidSignature);
        if(sig<4||raw.AsSpan(sig+1).IndexOf(PidSignature)>=0)
            throw new InvalidDataException("No se encontró una firma PID única y compatible.");
        int offset=sig-4;
        bool vanilla=raw.AsSpan(offset,4).SequenceEqual(VanillaReroll);
        int current=125;
        if(!vanilla)
        {
            if(raw[offset+2]!=0xA0||raw[offset+3]!=0xE3)
                throw new InvalidDataException("Instrucción ARM de shiny desconocida; no se sobrescribirá.");
            ushort instr=BitConverter.ToUInt16(raw,offset);
            var match=Instructions.FirstOrDefault(x=>x.Instr==instr);
            if(match.Count<1)throw new InvalidDataException("Rerolls originales no reconocidos.");
            current=match.Count;
        }
        int shinyAt=raw.AsSpan().IndexOf(ShinySignature);
        int? always=null;bool shiny=false;
        if(shinyAt>=0&&shinyAt+ShinySignature.Length<raw.Length)
        {
            int at=shinyAt+ShinySignature.Length;
            if(raw[at] is 0x0A or 0xEA){always=at;shiny=raw[at]==0xEA;}
        }
        return(new ShinyState(vanilla,current,always,shiny),offset);
    }
    internal ShinyState Read()=>Decode(GarcTransaction.ReadUnchanged(_codePath,_sha)).State;
    internal (FileChange File,IReadOnlyList<BatchChange> Changes,int AppliedRerolls) Preview(int requested,bool everything,bool restore=false)
    {
        if(requested is <1 or >9000)throw new ArgumentOutOfRangeException(nameof(requested),"Rerolls permitidos: 1–9000.");
        byte[] raw=GarcTransaction.ReadUnchanged(_codePath,_sha);
        var (state,offset)=Decode(raw);
        if(everything&&state.AlwaysShinyOffset is null)
            throw new NotSupportedException("No hay firma válida del parche Everything Shiny.");
        byte[] next=(byte[])raw.Clone();
        var row=Instructions.FirstOrDefault(x=>x.Count>=requested);
        if(row.Count==0)row=Instructions[^1];
        if(restore)VanillaReroll.CopyTo(next,offset);
        else
        {
            BitConverter.GetBytes(row.Instr).CopyTo(next,offset);
            next[offset+2]=0xA0;next[offset+3]=0xE3;
        }
        if(state.AlwaysShinyOffset is int at)
            next[at]=everything&&!restore?(byte)0xEA:(byte)0x0A;
        var changes=new List<BatchChange>();
        if(!raw.AsSpan(offset,4).SequenceEqual(next.AsSpan(offset,4)))
            changes.Add(new BatchChange(0,"Shiny Rate","PID rerolls",
                state.Vanilla?"Vanilla":state.Rerolls.ToString(),restore?"Vanilla":row.Count.ToString()));
        if(state.AlwaysShinyOffset is int index&&raw[index]!=next[index])
            changes.Add(new BatchChange(0,"Shiny Rate","Everything Shiny",
                state.AlwaysShiny.ToString(),(next[index]==0xEA).ToString()));
        return(new FileChange(_codePath,raw,next),changes,restore?0:row.Count);
    }
    internal void Apply(FileChange file,IReadOnlyList<BatchChange> changes,bool restore)
    {
        GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit([file],"Mecánicas y CRO",restore?"Restaurar Shiny Rate":"Editar Shiny Rate",
            changes,new Dictionary<string,string>{{"ActionId",restore?"shiny-rate.restore":"shiny-rate.apply"}});
        _sha=SHA256.HashData(File.ReadAllBytes(_codePath));
    }
}
