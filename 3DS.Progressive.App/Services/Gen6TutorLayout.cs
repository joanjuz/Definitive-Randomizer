using System.IO;
namespace ThreeDS.Progressive.Services;

internal sealed record Gen6TutorLayout(int List,int Shop)
{
    // Stable header from the original pk3DS locator. The shop prefix and need to
    // synchronize both tables are documented by UPR-ZX Gen6RomHandler.
    internal static readonly byte[] ListHeader=Convert.FromHexString("00466F726D617454797065000045646765494400FF");
    internal static readonly byte[] ShopHeader=Convert.FromHexString("8A02000030000000");
    // Index mapping between the 60 actual BP shop rows and the four legacy lists.
    // Three legacy entries already offered as TMs are absent from the BP shop.
    internal static readonly int[] ListSlots=[34,35,49,48,0,1,9,4,44,12,13,14,45,36,2,3,59,11,32,8,7,5,29,27,30,6,38,23,31,19,18,60,24,10,42,47,17,65,37,39,43,51,52,53,54,55,56,61,62,16,21,22,40,41,57,58,63,20,25,26];
    internal static Gen6TutorLayout Read(byte[] code,int moveCount)
    {
        if(code.Length%512!=0||code.Length<0x400000)throw new InvalidDataException("Se requiere code.bin ORAS descomprimido.");
        int Locate(byte[] signature)
        {int index=code.AsSpan(0x400000).IndexOf(signature);if(index<0||code.AsSpan(0x400000+index+1).IndexOf(signature)>=0)throw new InvalidDataException("Firma de tutores ORAS ausente o ambigua.");return 0x400000+index+signature.Length;}
        int list=Locate(ListHeader),shop=Locate(ShopHeader);
        if((long)list+136>code.Length||(long)shop+480>code.Length)throw new InvalidDataException("Tabla de tutores ORAS truncada.");
        foreach(int marker in new[]{15,33,50,66})if(BitConverter.ToUInt16(code,list+2*marker)!=622)throw new InvalidDataException("Marcadores de sedes de tutor incompatibles.");
        if(BitConverter.ToUInt16(code,list+134)!=ushort.MaxValue)throw new InvalidDataException("Terminador de tutores ORAS incompatible.");
        for(int i=0;i<60;i++)
        {
            int move=BitConverter.ToUInt16(code,shop+i*8);
            if(move<=0||move>=moveCount||move==622||BitConverter.ToUInt16(code,list+2*ListSlots[i])!=move)throw new InvalidDataException("Las tablas de tutores ORAS no están sincronizadas. Recuperá code.bin desde un backup compatible.");
            if(BitConverter.ToUInt32(code,shop+i*8+4)>65535)throw new InvalidDataException("Precio de tutor fuera de rango.");
        }
        return new(list,shop);
    }
    internal static (int Group,int Flag) Compatibility(int slot)
    {
        int raw=ListSlots[slot];return raw<15?(0,raw):raw<33?(1,raw-16):raw<50?(2,raw-34):(3,raw-51);
    }
}
