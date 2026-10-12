using System.IO;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Immutable Gen7 table snapshot, tied to the opened extraction.</summary>
internal sealed record WildTableClipboard(
    string ProjectRoot, string Game, int SourceId, int SourceAreaFile,
    int OccupiedNormalSlots, IReadOnlyDictionary<string,int> Values);

public sealed partial class GameEncounterService
{
    private void EnsureGen7Wild()
    {
        if(Config.Generation!=7)
            throw new NotSupportedException("Copiar/pegar tablas de día, noche y SOS se implementa para Gen7.");
    }

    internal WildTableClipboard CopyWildTable(int id)
    {
        EnsureGen7Wild();
        if(!_locations.TryGetValue(id,out var location))
            throw new ArgumentOutOfRangeException(nameof(id),"Tabla de encuentro inexistente.");
        var values=EditorValues.Flatten(Read(id));
        return new WildTableClipboard(Path.GetFullPath(Project.ProjectRoot),
            Config.USUM?"USUM":"SM",id,location.File,
            NormalOccupied(values),new Dictionary<string,int>(values));
    }

    internal IReadOnlyList<int> CompatibleWildTables(WildTableClipboard copy,int destinationId)
    {
        EnsureClipboard(copy);
        if(!_locations.TryGetValue(destinationId,out var destination))
            throw new ArgumentOutOfRangeException(nameof(destinationId));
        var records=Records();
        return _locations.Where(p=>p.Value.File==destination.File)
            .Where(p=>NormalOccupied(EditorValues.Flatten(ReadRecord(p.Key,records)))
                ==copy.OccupiedNormalSlots)
            .Select(p=>p.Key).Order().ToArray();
    }

    private void EnsureClipboard(WildTableClipboard copy)
    {
        EnsureGen7Wild();
        if(copy is null ||
            !string.Equals(Path.GetFullPath(Project.ProjectRoot),copy.ProjectRoot,
                StringComparison.OrdinalIgnoreCase) ||
            (Config.USUM?"USUM":"SM")!=copy.Game)
            throw new InvalidOperationException(
                "La tabla copiada pertenece a otro proyecto o a un juego diferente.");
    }

    private static int NormalOccupied(IReadOnlyDictionary<string,int> values) =>
        Enumerable.Range(0,10).Count(slot=>
            values.TryGetValue($"S0.{slot}.Species",out int species) && species!=0);

    /// <summary>
    /// Single paste accepts any Gen7 table. Paste All only accepts compatible
    /// occupied-normal-slot counts in the CURRENT area (the vanilla behavior).
    /// All writes remain detached until the inherited EditorProposal is applied.
    /// </summary>
    internal EditorProposal PreviewPasteWild(WildTableClipboard copy,IReadOnlyList<int> targetIds)
    {
        EnsureClipboard(copy);
        var targets=targetIds.Distinct().Order().ToArray();
        if(targets.Length==0||targets.Any(id=>!_locations.ContainsKey(id)))
            throw new ArgumentException("No hay tablas de destino válidas.");
        var records=Records();
        var changes=new List<BatchChange>();
        foreach(int id in targets)
        {
            var before=ReadRecord(id,records);
            // Edición estructurada, no sobrescritura ciega de los bytes
            // reservados del formato EA ni de mapas ajenos.
            EditorValues.Validate(before,copy.Values);
            WriteRecord(id,copy.Values,records);
            var after=ReadRecord(id,records);
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,after));
        }
        return new EditorProposal(ProposalOwner,"Pegar tabla de encuentros",0,Build(records),
            changes,new Dictionary<string,string>
            {
                ["ActionId"]="wild-encounters.paste",
                ["SourceTable"]=copy.SourceId.ToString(),
                ["SourceArea"]=copy.SourceAreaFile.ToString(),
                ["Scope"]=string.Join(",",targets),
                ["SameNormalSlotCount"]=copy.OccupiedNormalSlots.ToString(),
                ["Version"]=copy.Game
            });
    }
}
