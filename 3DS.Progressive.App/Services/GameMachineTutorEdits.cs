using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Frozen Shop.cro + code.bin update, both verified before any write.</summary>
public sealed record TutorEditPreview(
    byte[] ShopOriginal, byte[] ShopOutput, byte[] CodeOriginal, byte[] CodeOutput,
    IReadOnlyList<BatchChange> Changes, int CodeSlotsChanged);

public sealed partial class GameMachineTutorService
{
    private static readonly int[] TutorExceptions=[165,621,166,226];

    public int RandomizeTutorMoves()=>RandomizeTutorMoves(RandomNumberGenerator.GetInt32(int.MaxValue));
    public int RandomizeTutorMoves(int seed)
    {
        if(!CanEditTutorMoves)throw new NotSupportedException("Solo USUM con 67 ranuras de tutores verificadas.");
        var pool=Enumerable.Range(1,Math.Max(0,Math.Min(_project.MoveNames.Count,_project.GameConfiguration.Moves.Length)-1))
            .Where(id=>id<=ushort.MaxValue&&!Legal.Z_Moves.Contains(id)&&
                !TutorExceptions.Contains(id)).ToArray();
        if(pool.Length<Tutors.Count)throw new InvalidDataException("No hay suficientes movimientos válidos de tutor.");
        var rng=new Random(seed);
        for(int i=pool.Length-1;i>0;i--){int j=rng.Next(i+1);(pool[i],pool[j])=(pool[j],pool[i]);}
        int differences=0;
        for(int i=0;i<Tutors.Count;i++)
        {
            if(Tutors[i].MoveId!=pool[i])differences++;
            Tutors[i].MoveId=pool[i];
        }
        _generatedTutorMoves=Tutors.Select(r=>r.MoveId).ToArray();
        return differences;
    }

    public TutorEditPreview PreviewTutorEdits()
    {
        if(!CanEditTutorMoves||_shopOriginal is null)
            throw new NotSupportedException("La edición completa de tutores requiere una tabla USUM de 67 ranuras.");
        if(Tutors.Count!=_originalTutorMoves.Length || Tutors.Select(t=>t.Number).Where((n,i)=>n!=i+1).Any())
            throw new InvalidDataException("Orden o número de tutores alterado.");
        if(Tutors.Any(t=>t.MoveId<=0||t.MoveId>=Math.Min(_project.MoveNames.Count,_project.GameConfiguration.Moves.Length)||
                            t.MoveId>ushort.MaxValue||
                            (t.MoveId!=_originalTutorMoves[t.Number-1]&&
                             (Legal.Z_Moves.Contains(t.MoveId)||TutorExceptions.Contains(t.MoveId)))))
            throw new InvalidDataException("Hay movimientos de tutor inválidos o exclusivos de mecánicas especiales.");
        if(Tutors.Any(t=>t.Price is <0 or >65535))
            throw new InvalidDataException("Los precios de tutores deben estar entre 0 y 65535 BP.");

        byte[] shop=(byte[])_shopOriginal.Clone();
        if(!Gen7ShopCroLayout.TryRead(shop,out var layout,out string error))
            throw new InvalidDataException("Shop.cro tiene un layout incompatible: "+error);
        if(layout.BPTutors.TotalEntries!=Tutors.Count || checked((int)layout.BPTutors.DataStart)!=_tutorOffset)
            throw new InvalidDataException("El orden o la ubicación de tutores no coincide con la carga inicial.");
        var differences=new List<BatchChange>();
        bool moveChanged=false;
        for(int i=0;i<Tutors.Count;i++)
        {
            var row=Tutors[i];
            int at=_tutorOffset+i*4;
            ushort oldMove=BitConverter.ToUInt16(shop,at);
            ushort oldPrice=BitConverter.ToUInt16(shop,at+2);
            if(oldMove!=_originalTutorMoves[i]||oldPrice!=_originalPrices[i])
                throw new InvalidDataException("Los tutores cargados cambiaron inesperadamente.");
            if(row.MoveId!=oldMove)
            {
                BitConverter.GetBytes((ushort)row.MoveId).CopyTo(shop,at);
                differences.Add(new BatchChange(i+1,row.Group,"Movimiento",
                    _project.GetMoveLabel(oldMove),_project.GetMoveLabel(row.MoveId)));
                moveChanged=true;
            }
            if(row.Price!=oldPrice)
            {
                BitConverter.GetBytes((ushort)row.Price).CopyTo(shop,at+2);
                differences.Add(new BatchChange(i+1,row.Group,"Precio BP",
                    oldPrice.ToString(),row.Price.ToString()));
            }
        }
        if(!Gen7ShopCroLayout.TryRead(shop,out var verified,out error) ||
            verified.BPTutors.DataStart!=layout.BPTutors.DataStart ||
            verified.BPTutors.TotalEntries!=Tutors.Count)
            throw new InvalidDataException("La tabla de tutores modificada no superó la relectura CRO: "+error);
        for(int i=0;i<Tutors.Count;i++)
        {
            int at=_tutorOffset+i*4;
            if(BitConverter.ToUInt16(shop,at)!=Tutors[i].MoveId||
               BitConverter.ToUInt16(shop,at+2)!=Tutors[i].Price)
                throw new InvalidDataException("La verificación de los tutores no coincide.");
        }
        // If a move has changed, the native compatibility routine must be
        // found uniquely, otherwise reject BOTH changes without touching disk.
        UsumTutorCodePatch.Proposal code=moveChanged
            ?UsumTutorCodePatch.Prepare(_codeOriginal,Tutors.Select(t=>(ushort)t.MoveId).ToArray())
            :new((byte[])_codeOriginal.Clone(),0,0);
        return new TutorEditPreview((byte[])_shopOriginal.Clone(),shop,
            (byte[])_codeOriginal.Clone(),code.Output,differences,code.ChangedSlots);
    }

    public void ApplyTutorEdits(TutorEditPreview preview)
    {
        var explicitRows=ManualTutors(true);
        using var manual=Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"?null:explicitRows.Length>0?ManualOperationScope.Begin("tutors7","tutors7",new{Rows=explicitRows}):_generatedTutorMoves is not null?ManualOperationScope.Begin("random-only","",new{ActionId="move-tutors.randomize"}):null;
        ArgumentNullException.ThrowIfNull(preview);
        if(preview.Changes.Count==0)
            throw new InvalidOperationException("No existen cambios para guardar.");
        var fresh=PreviewTutorEdits();
        if(!fresh.ShopOutput.AsSpan().SequenceEqual(preview.ShopOutput)||
           !fresh.CodeOutput.AsSpan().SequenceEqual(preview.CodeOutput))
            throw new InvalidOperationException("La propuesta cambió desde la vista previa.");
        byte[] onDiskShop=GarcTransaction.ReadUnchanged(_shopPath,
            SHA256.HashData(preview.ShopOriginal));
        // Check code.bin too, even if only editing BP prices. This avoids
        // silently discarding changes to code while the editor is open.
        byte[] onDiskCode=GarcTransaction.ReadUnchanged(_codePath,
            SHA256.HashData(preview.CodeOriginal));
        var files=new List<FileChange>
        {
            new(_shopPath,onDiskShop,preview.ShopOutput),
            new(_codePath,onDiskCode,preview.CodeOutput)
        };
        _project.History.Commit(files,"MT y tutores","Editar movimientos y BP de tutores USUM",
            preview.Changes,new Dictionary<string,string>
            {
                ["CodeSyncSlots"]=preview.CodeSlotsChanged.ToString(),
                ["Formato"]="USUM Tutor native ARM table (67 slots)"
            });
        _project.RefreshMoveAssignments();
    }
}
