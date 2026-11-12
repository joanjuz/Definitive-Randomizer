using System.IO;
using System.Security.Cryptography;
using System.Text;
using ThreeDS.Progressive.Models;
using pk3DS.Core.Modding.Research;
namespace ThreeDS.Progressive.Services;
/// <summary>Native transaction for the previously validated USUM persistent-consumables patch.</summary>
public sealed class GamePersistentItemsService:IProjectEditor
{
    private readonly GameProjectSession _project;private readonly string _path;private byte[] _hash;private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Persistent Items · USUM";
    public IReadOnlyList<RecordEntry> Entries=>[new(1,"Restaurar consumibles equipados después de combates salvajes y contra entrenadores")];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Consumibles persistentes",[new("Enable","Restaurar objetos tras el combate · no afecta instalaciones ni PvP",1,0,1)])];
    public GamePersistentItemsService(GameProjectSession project)
    {if(!project.GameConfiguration.USUM)throw new NotSupportedException("Persistent Items solo está validado para Ultra Sol/Ultra Luna.");_project=project;_path=new[]{Path.Combine(project.GameConfiguration.RomFS,"Battle.cro"),Path.Combine(project.GameConfiguration.RomFS,"dll","Battle.cro")}.FirstOrDefault(File.Exists)??throw new FileNotFoundException("Battle.cro ausente.");_hash=SHA256.HashData(File.ReadAllBytes(_path));var capability=InspectCapability(File.ReadAllBytes(_path));if(!capability.CanApply)throw new InvalidDataException("No se modificó ningún archivo. "+GamePlayerLevelCapService.Describe(capability));_=Patch(File.ReadAllBytes(_path));}
    internal const string CapabilityId="usum.persistent-items";
    /// <summary>
    /// Read-only capability report. This patch has no fixed offsets: each of its two points is located by a masked
    /// signature that must appear exactly once, so the best level it reports is «compatible by adaptation». The patch is
    /// simulated on a copy; the input is never modified.
    /// </summary>
    internal static PatchCapabilityReport InspectCapability(byte[]? cro)
    {
        const string battleName="Battle.cro · tipo de combate",itemName="Battle.cro · restauración del objeto";
        PatchCapabilityReport Report(PatchCapabilityLevel level,string summary,PatchResourceReport[] resources,IReadOnlyList<PatchChangedRange>? changes=null)=>new(CapabilityId,level,resources,changes??[],[],summary);
        if(cro is null)return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: falta Battle.cro.",[new(battleName,"Comprobación del tipo de combate",PatchResourceState.Missing,null,"No se encontró el archivo."),new(itemName,"Restauración del objeto equipado",PatchResourceState.Missing,null,"No se encontró el archivo.")]);
        if(!IsExpectedCro(cro))return Report(PatchCapabilityLevel.NotAccredited,"Datos insuficientes: Battle.cro no tiene la cabecera CRO0 esperada.",[new(battleName,"Comprobación del tipo de combate",PatchResourceState.Truncated,null,"Sin cabecera CRO0."),new(itemName,"Restauración del objeto equipado",PatchResourceState.Truncated,null,"Sin cabecera CRO0.")]);
        static PatchResourceReport Gate(byte[] data,string name,string role,byte[] signature,byte[] mask,int conditionOffset,byte stockCondition,byte patchedCondition)
        {
            int[] stock=FindMatches(data,signature,mask,conditionOffset,stockCondition),patched=FindMatches(data,signature,mask,conditionOffset,patchedCondition);
            if(stock.Length==1&&patched.Length==0)return new(name,role,PatchResourceState.Original,(uint)stock[0],$"Código original localizado por su firma en 0x{stock[0]:X6}.");
            if(stock.Length==0&&patched.Length==1)return new(name,role,PatchResourceState.OwnInstall,(uint)patched[0],$"Parche localizado por su firma en 0x{patched[0]:X6}.");
            return stock.Length+patched.Length==0
                ?new(name,role,PatchResourceState.ExternalModification,null,"No se encuentra el código que este parche modifica: otra revisión o una modificación lo cambió.")
                :new(name,role,PatchResourceState.ExternalModification,null,$"La firma aparece {stock.Length+patched.Length} veces; debe ser única para saber dónde escribir.");
        }
        PatchResourceReport[] gates=[Gate(cro,battleName,"Comprobación del tipo de combate",BattleTypeGateSignature,BattleTypeGateMask,BattleTypeConditionOffset,BattleTypeStockCondition,BattleTypePatchedCondition),
            Gate(cro,itemName,"Restauración del objeto equipado",ItemRestoreSignature,ItemRestoreMask,ItemRestoreConditionOffset,ItemRestoreStockCondition,ItemRestorePatchedCondition)];
        if(gates.Any(g=>g.State==PatchResourceState.ExternalModification))return Report(PatchCapabilityLevel.NotAccredited,"No se pueden localizar de forma única los dos puntos del parche.",gates);
        byte[] output;
        try{output=Patch(cro).Bytes;if(Patch(output).Changes!=0)return Report(PatchCapabilityLevel.NotAccredited,"La instalación simulada no se pudo leer de vuelta.",gates);}
        catch(Exception e) when(e is InvalidDataException or ArgumentException or IndexOutOfRangeException){return Report(PatchCapabilityLevel.NotAccredited,"No se pudo simular la instalación: "+e.Message,gates);}
        var changes=new List<PatchChangedRange>();
        for(int i=0;i<cro.Length&&i<output.Length;i++){if(cro[i]==output[i])continue;int start=i;while(i<cro.Length&&cro[i]!=output[i])i++;changes.Add(new("Battle.cro",(uint)start,i-start));}
        int installed=gates.Count(g=>g.State==PatchResourceState.OwnInstall);
        string state=installed==2?"El parche está instalado.":installed==1?"El parche está en uno solo de sus dos puntos; instalarlo completa el otro.":"Los dos puntos tienen el código original.";
        return Report(PatchCapabilityLevel.CompatibleByAdaptation,state+" Se localizaron por su firma única, no por una posición fija; la revisión del archivo no se comprueba.",gates,changes);
    }

    internal static (byte[] Bytes,int Changes) Patch(byte[] data)
    {
        if(!IsExpectedCro(data)||!TryGetGateState(data,BattleTypeGateSignature,BattleTypeGateMask,BattleTypeConditionOffset,BattleTypeStockCondition,BattleTypePatchedCondition,out int battle,out bool b)||!TryGetGateState(data,ItemRestoreSignature,ItemRestoreMask,ItemRestoreConditionOffset,ItemRestoreStockCondition,ItemRestorePatchedCondition,out int item,out bool i))throw new InvalidDataException("Battle.cro no contiene las dos firmas USUM únicas compatibles. No se modificó ningún archivo.");
        byte[] output=(byte[])data.Clone();output[battle+BattleTypeConditionOffset]=BattleTypePatchedCondition;output[item+ItemRestoreConditionOffset]=ItemRestorePatchedCondition;
        return(b&&i?output:CroIntegrity.Rebuild(output),(b?0:1)+(i?0:1));
    }
    public IReadOnlyList<EditorGroup> Read(int id){if(id!=1)throw new ArgumentException("Registro inválido.");var data=GarcTransaction.ReadUnchanged(_path,_hash);return[new("Estado",[new("Enable","Persistent Items activado",Patch(data).Changes==0?1:0,0,1)])];}
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {EditorValues.Validate(RandomOptions,options);if(options["Enable"]!=1||!scope.Contains(1)||scope.Any(i=>i!=1))throw new ArgumentException("Selecciona habilitar Persistent Items.");var source=GarcTransaction.ReadUnchanged(_path,_hash);var patch=Patch(source);return new(_owner,"Habilitar Persistent Items",0,[new(_path,source,patch.Bytes)],patch.Changes==0?[]:[new(1,Entries[0].Name,"Instrucciones ARM",patch.Changes.ToString(),"0 pendientes")],new Dictionary<string,string>{{"Game","USUM"}});}
    public void Save(int id,IReadOnlyDictionary<string,int> values){using var manual=ManualOperationScope.Record(this,id,values);Apply(Preview(values,[id]));}
    public void Apply(EditorProposal proposal){if(proposal.Owner!=_owner)throw new InvalidOperationException("Propuesta de otra sesión.");_=GarcTransaction.ReadUnchanged(_path,_hash);if(proposal.Changes.Count==0)return;_project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);_hash=SHA256.HashData(File.ReadAllBytes(_path));if(Patch(File.ReadAllBytes(_path)).Changes!=0)throw new InvalidDataException("No coincide la relectura de Persistent Items.");}
    // ------------------------------------------------------------
    // Gate #1: post-battle held-item reconciliation.
    //
    // Stock:
    //   ldrb r0, [r11]
    //   cmp  r0, #1
    //   bne  skip_item_reconciliation
    //   cmp  r4, #0
    //   mov  r5, #0
    //   bls  ...
    //
    // Research identifies battle type values as:
    //   0 = Wild
    //   1 = NPC
    //   2 = Battle Facility
    //   3 = PvP
    //
    // BNE -> BHI after cmp #1 means Wild and NPC both enter the
    // reconciliation loop, while BF/PvP still skip it.
    // ------------------------------------------------------------
    private static readonly byte[] BattleTypeGateSignature =
    [
        0x00, 0x00, 0xDB, 0xE5,
        0x01, 0x00, 0x50, 0xE3,
        0x00, 0x00, 0x00, 0x1A,
        0x00, 0x00, 0x54, 0xE3,
        0x00, 0x50, 0xA0, 0xE3,
        0x00, 0x00, 0x00, 0x9A,
    ];

    private static readonly byte[] BattleTypeGateMask =
    [
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0xFF,
    ];

    private const int BattleTypeConditionOffset = 11;
    private const byte BattleTypeStockCondition = 0x1A;   // BNE
    private const byte BattleTypePatchedCondition = 0x8A; // BHI

    // ------------------------------------------------------------
    // Gate #2: original held-item restoration.
    //
    // Stock:
    //   mov r0, r6
    //   bl  CoreParam::GetItem
    //   mov r6, r0
    //   mov r1, #17       ; PRM_ID_SPEND
    //   bl  ITEM_MANAGER::GetParam
    //   cmp r0, #0
    //   nop
    //   beq restore_original_item
    //
    // BEQ -> unconditional B makes consumable originals use the
    // same restoration path as non-consumables.
    // ------------------------------------------------------------
    private static readonly byte[] ItemRestoreSignature =
    [
        0x06, 0x00, 0xA0, 0xE1,
        0x00, 0x00, 0x00, 0xEB,
        0x00, 0x60, 0xA0, 0xE1,
        0x11, 0x10, 0xA0, 0xE3,
        0x00, 0x00, 0x00, 0xEB,
        0x00, 0x00, 0x50, 0xE3,
        0x00, 0xF0, 0x20, 0xE3,
        0x00, 0x00, 0x00, 0x0A,
    ];

    private static readonly byte[] ItemRestoreMask =
    [
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x00, 0xFF,
    ];

    private const int ItemRestoreConditionOffset = 31;
    private const byte ItemRestoreStockCondition = 0x0A;   // BEQ
    private const byte ItemRestorePatchedCondition = 0xEA; // B

    private static bool TryGetGateState(
        byte[] data,
        byte[] signature,
        byte[] mask,
        int conditionOffset,
        byte stockCondition,
        byte patchedCondition,
        out int offset,
        out bool patched)
    {
        int[] stock =
            FindMatches(
                data,
                signature,
                mask,
                conditionOffset,
                stockCondition);

        int[] modified =
            FindMatches(
                data,
                signature,
                mask,
                conditionOffset,
                patchedCondition);

        if (stock.Length == 1 &&
            modified.Length == 0)
        {
            offset = stock[0];
            patched = false;
            return true;
        }

        if (stock.Length == 0 &&
            modified.Length == 1)
        {
            offset = modified[0];
            patched = true;
            return true;
        }

        offset = -1;
        patched = false;
        return false;
    }

    private static int[] FindMatches(
        byte[] data,
        byte[] signature,
        byte[] mask,
        int conditionOffset,
        byte condition)
    {
        if (data is null ||
            signature is null ||
            mask is null ||
            signature.Length != mask.Length ||
            data.Length < signature.Length)
        {
            return [];
        }

        var result = new List<int>();

        for (int offset = 0;
             offset <= data.Length - signature.Length;
             offset += 4)
        {
            bool match = true;

            for (int i = 0;
                 i < signature.Length;
                 i++)
            {
                if (i == conditionOffset)
                {
                    if (data[offset + i] != condition)
                    {
                        match = false;
                        break;
                    }

                    continue;
                }

                byte m = mask[i];

                if (m == 0)
                    continue;

                if ((data[offset + i] & m) !=
                    (signature[i] & m))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                result.Add(offset);
        }

        return [.. result];
    }

    private static bool IsExpectedCro(byte[] data)
    {
        return data is not null &&
               data.Length >= 0x180 &&
               Encoding.ASCII.GetString(
                   data,
                   0x80,
                   4) == "CRO0";
    }

}
