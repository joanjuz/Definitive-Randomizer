using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
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
        var capability=InspectCapability(source);
        if(!capability.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(capability));
        _=Decode(source);
    }
    internal const string CapabilityId="3ds.shiny-rate";
    /// <summary>
    /// Read-only capability report. Shiny Rate has no fixed offsets: the PID routine is located by its signature and must
    /// be the only candidate, so its best level is «compatible by adaptation». «Everything Shiny» is optional: when its
    /// signature is absent the rerolls remain available and the report says so. The input is never modified.
    /// </summary>
    internal static PatchCapabilityReport InspectCapability(byte[]? code)
    {
        const string pidName="code.bin · rerolls del PID",shinyName="code.bin · Everything Shiny";
        PatchCapabilityReport Report(PatchCapabilityLevel level,string summary,PatchResourceReport[] resources,IReadOnlyList<PatchChangedRange>? changes=null)=>new(CapabilityId,level,resources,changes??[],[],summary);
        PatchResourceReport[] Both(PatchResourceState state,string evidence)=>[new(pidName,"Cantidad de rerolls",state,null,evidence),new(shinyName,"Todos shiny",state,null,evidence)];
        if(code is null)return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: falta code.bin.",Both(PatchResourceState.Missing,"No se encontró el archivo."));
        if(code.Length==0||code.Length%0x200!=0)return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: code.bin no está descomprimido o está incompleto.",Both(PatchResourceState.Truncated,"El tamaño no es múltiplo de 0x200."));
        var candidates=Candidates(code);
        PatchResourceReport pid=candidates.Count switch
        {
            1 when code.AsSpan(candidates[0],4).SequenceEqual(VanillaReroll)=>new(pidName,"Cantidad de rerolls",PatchResourceState.Original,(uint)candidates[0],$"Rutina PID original localizada por su firma en 0x{candidates[0]:X6}."),
            1=>new(pidName,"Cantidad de rerolls",PatchResourceState.OwnInstall,(uint)candidates[0],$"Rutina PID con rerolls modificados localizada por su firma en 0x{candidates[0]:X6}."),
            0=>new(pidName,"Cantidad de rerolls",PatchResourceState.ExternalModification,null,"No se encuentra la rutina PID con una instrucción de rerolls reconocida: otra revisión o una modificación la cambió."),
            _=>new(pidName,"Cantidad de rerolls",PatchResourceState.ExternalModification,null,$"Hay {candidates.Count} rutinas candidatas; debe ser única para saber dónde escribir."),
        };
        int shinyAt=code.AsSpan().IndexOf(ShinySignature);
        bool option=shinyAt>=0&&shinyAt+ShinySignature.Length<code.Length&&code[shinyAt+ShinySignature.Length] is 0x0A or 0xEA;
        PatchResourceReport shiny=option
            ?new(shinyName,"Todos shiny",code[shinyAt+ShinySignature.Length]==0xEA?PatchResourceState.OwnInstall:PatchResourceState.Original,(uint)(shinyAt+ShinySignature.Length),$"Condición localizada por su firma en 0x{shinyAt+ShinySignature.Length:X6}.")
            :new(shinyName,"Todos shiny",PatchResourceState.ExternalModification,null,"Firma no encontrada: la opción «Everything Shiny» no está disponible; los rerolls no dependen de ella.");
        PatchResourceReport[] resources=[pid,shiny];
        if(candidates.Count!=1)return Report(PatchCapabilityLevel.NotAccredited,"No se puede localizar de forma única la rutina de rerolls.",resources);
        byte[] trial=(byte[])code.Clone();
        try
        {
            _=Decode(code);
            var row=Instructions.First(x=>x.Count>=125);
            BitConverter.GetBytes(row.Instr).CopyTo(trial,candidates[0]);trial[candidates[0]+2]=0xA0;trial[candidates[0]+3]=0xE3;
            if(Decode(trial).State is not {Vanilla:false} read||read.Rerolls!=row.Count)return Report(PatchCapabilityLevel.NotAccredited,"La escritura simulada de los rerolls no se pudo leer de vuelta.",resources);
        }
        catch(Exception e) when(e is InvalidDataException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {return Report(PatchCapabilityLevel.NotAccredited,"No se pudo simular el cambio de rerolls: "+e.Message,resources);}
        var changes=new List<PatchChangedRange>();
        if(!trial.AsSpan(candidates[0],4).SequenceEqual(code.AsSpan(candidates[0],4)))changes.Add(new("code.bin",(uint)candidates[0],4));
        return Report(PatchCapabilityLevel.CompatibleByAdaptation,
            (pid.State==PatchResourceState.OwnInstall?"Los rerolls ya están modificados.":"Los rerolls tienen el valor original.")+
            " La rutina se localizó por su firma única, no por una posición fija; la revisión del archivo no se comprueba."+(option?"":" «Everything Shiny» no está disponible en este archivo."),resources,changes);
    }
    /// <summary>Offsets of the reroll instruction of every routine that looks like the PID loop.</summary>
    private static List<int> Candidates(byte[] raw)
    {
        var candidates=new List<int>();int search=0;
        while(search<=raw.Length-PidSignature.Length)
        {
            int found=raw.AsSpan(search).IndexOf(PidSignature);if(found<0)break;
            int sig=search+found;search=sig+1;if(sig<4)continue;
            int at=sig-4;
            if(raw.AsSpan(at,4).SequenceEqual(VanillaReroll)||
               raw[at+2]==0xA0&&raw[at+3]==0xE3&&Instructions.Any(row=>row.Instr==BitConverter.ToUInt16(raw,at)))candidates.Add(at);
        }
        return candidates;
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
        // ORAS has other loops sharing this suffix. Identify the PID loop by
        // its original LDRB instruction or a supported MOV reroll replacement.
        var candidates=Candidates(raw);
        if(candidates.Count!=1)throw new InvalidDataException("No se encontró una rutina PID única con instrucción de rerolls compatible.");
        int offset=candidates[0];
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
        var desired=Decode(file.Proposed()).State;
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:ManualOperationScope.Begin("shiny","shiny",new{desired.Rerolls,Everything=desired.AlwaysShiny,Restore=restore});
        GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit([file],"Mecánicas y CRO",restore?"Restaurar Shiny Rate":"Editar Shiny Rate",
            changes,new Dictionary<string,string>{{"ActionId",restore?"shiny-rate.restore":"shiny-rate.apply"}});
        _sha=SHA256.HashData(File.ReadAllBytes(_codePath));
    }
}
