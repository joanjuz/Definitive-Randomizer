using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

internal static class SinglePurchaseShopText
{
    internal const int Bank=95;
    private static readonly string[] Messages=["この道具はもう買いました。","このどうぐはもうかいました。","You already bought this item.","Vous avez déjà acheté cet objet.","Hai già comprato questo strumento.","Du hast dieses Item bereits gekauft.","Ya compraste este objeto.","이미 구입한 도구입니다.","你已经购买过这个道具了。","你已經購買過這個道具了。"];
    internal static (ushort Id,IReadOnlyList<FileChange> Files) Prepare(GameProjectSession project,ushort? existing)
    {
        if(!project.GameConfiguration.USUM)throw new NotSupportedException("Las compras únicas de tiendas especiales requieren USUM.");
        var config=project.GameConfiguration;var reference=config.GetGARCReference("gametext");
        var banks=new List<(string Path,GARCReference Reference,byte[] Source,byte[][] Records,string[] Lines,int Language)>();
        for(int language=0;language<Messages.Length;language++)
        {
            var relative=reference.GetRelativeGARC(language);string path=Path.Combine(config.RomFS,relative.Reference);
            byte[] source=File.ReadAllBytes(path);var records=new GARC.MemGARC(source).Files;
            if(records.Length<=Bank)throw new InvalidDataException("Falta el banco de mensajes de tiendas USUM en el idioma "+language+".");
            var lines=new TextFile(config,records[Bank],config.RemapCharacters).Lines;
            if(lines.Length<=38)throw new InvalidDataException("Banco de mensajes de tiendas incompleto.");
            banks.Add((path,relative,source,records,lines,language));
        }
        int message=existing??banks.Max(b=>b.Lines.Length);
        if(message>ushort.MaxValue)throw new InvalidDataException("El banco de mensajes excede el rango admitido.");
        var changes=new List<FileChange>();
        foreach(var bank in banks)
        {
            if(existing is not null)
            {
                if(message>=bank.Lines.Length)throw new InvalidDataException("El mensaje de compras únicas registrado no existe en uno de los idiomas.");
                // Preserve any later user text customization. Also freeze unchanged message dependencies.
                changes.Add(new(bank.Path,bank.Source,bank.Source));continue;
            }
            var lines=bank.Lines.Concat(Enumerable.Repeat("",message-bank.Lines.Length)).Append(Messages[bank.Language]).ToArray();
            var encoded=TextFile.GetBytes(config,lines,config.RemapCharacters);
            if(!new TextFile(config,encoded,config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("El mensaje de compra única no se pudo codificar sin pérdidas.");
            bank.Records[Bank]=encoded;
            byte[] output=GarcTransaction.Build(bank.Source,bank.Reference,bank.Records,preserveRecordSize:false);
            var readback=new GARC.MemGARC(output).Files;
            if(!new TextFile(config,readback[Bank],config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("No coincide la relectura del mensaje de tienda.");
            changes.Add(new(bank.Path,bank.Source,output));
        }
        return ((ushort)message,changes);
    }
}
