using System.IO;
using System.Security.Cryptography;
using System.Text;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Original USUM Battle.cro Mega event flag hook, including all CRO segment hashes.</summary>
internal sealed class GameMegaUnlockService(GameProjectSession project)
{
    private const int Offset=0x9CE24;
    private static readonly byte[] Stock=[0x01,0x00,0x00,0x0A];
    private static readonly byte[] Patched=[0x00,0xF0,0x20,0xE3];
    private string PathForCro()=>new GameCroRecipeService(project).BattlePath
        ??throw new FileNotFoundException("Falta Battle.cro en el RomFS de USUM.");
    internal (FileChange File,IReadOnlyList<BatchChange> Changes) Preview()
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Mega Evolution from Start solo en USUM.");
        string path=PathForCro();
        byte[] original=File.ReadAllBytes(path);
        if(original.Length<Offset+4||original.Length<0x180||
            Encoding.ASCII.GetString(original,0x80,4)!="CRO0")
            throw new InvalidDataException("Battle.cro no presenta la cabecera USUM esperada.");
        var bytes=original.AsSpan(Offset,4);
        if(bytes.SequenceEqual(Patched))
            return(new FileChange(path,original,original),[]);
        if(!bytes.SequenceEqual(Stock))
            throw new InvalidDataException("Battle.cro tiene una instrucción desconocida en 0x9CE24; no se aplicó nada.");
        byte[] output=(byte[])original.Clone();
        Patched.CopyTo(output,Offset);
        RecalculateCroHashes(output);
        return(new FileChange(path,original,output),
            [new BatchChange(0,"USUM Mega","Desbloqueo desde el inicio",
                "Bloqueo por progreso de historia","Mega desde el inicio")]);
    }
    private static void RecalculateCroHashes(byte[] cro)
    {
        int codeStart=BitConverter.ToInt32(cro,0xB0),codeLength=BitConverter.ToInt32(cro,0xB4);
        int dataStart=BitConverter.ToInt32(cro,0xB8),dataLength=BitConverter.ToInt32(cro,0xBC);
        int roStart=BitConverter.ToInt32(cro,0xC0),roLength=dataStart-roStart;
        static byte[] Segment(byte[] source,int at,int length)
        {
            if(at<0||length<0||at>source.Length||length>source.Length-at)
                throw new InvalidDataException("Segmento CRO fuera de rango: se rechazó la modificación.");
            return SHA256.HashData(source.AsSpan(at,length));
        }
        byte[][] hashes=[
            Segment(cro,0x80,0x100),
            Segment(cro,codeStart,codeLength),
            Segment(cro,roStart,roLength),
            Segment(cro,dataStart,dataLength)
        ];
        for(int i=0;i<4;i++)hashes[i].CopyTo(cro,i*0x20);
    }
    internal void Apply(FileChange file,IReadOnlyList<BatchChange> changes)
    {
        if(!string.Equals(file.Path,PathForCro(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El archivo Battle.cro cambió después de preparar la vista previa.");
        GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        project.History.Commit([file],"Mega Evolutions","Habilitar Mega desde el inicio",
            changes,new Dictionary<string,string>
            {
                ["ActionId"]="mega-evolution.unlock-from-start",
                ["PatchOffset"]="0x9CE24",["Engine"]="pk3DS Progressive · Gen7MegaEventFlagPatcher"
            });
    }
}
