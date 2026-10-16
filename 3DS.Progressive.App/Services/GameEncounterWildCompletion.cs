using System.IO;
using System.Text;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Non-writing audit and dynamic BST-range editing for Gen7 Wild.</summary>
public sealed partial class GameEncounterService
{
    internal bool SplitLongestWildRange()
    {
        int index=-1,span=0;
        for(int i=0;i<_bstRanges.Count;i++)
        {
            var range=_bstRanges[i];
            int width=range.MaxLevel-range.MinLevel;
            if(width>span){span=width;index=i;}
        }
        if(index<0)return false;
        var original=_bstRanges[index];
        int pivot=original.MinLevel+(original.MaxLevel-original.MinLevel)/2;
        _bstRanges[index]=(original.MinLevel,pivot,original.MinBST,
            original.MaxBST,original.FullRandom);
        _bstRanges.Insert(index+1,(pivot+1,original.MaxLevel,original.MinBST,
            original.MaxBST,original.FullRandom));
        return true;
    }

    internal bool MergeLastWildRange()
    {
        if(_bstRanges.Count<2)return false;
        int end=_bstRanges.Count-1;
        var a=_bstRanges[end-1];var b=_bstRanges[end];
        _bstRanges[end-1]=(a.MinLevel,b.MaxLevel,
            Math.Min(a.MinBST,b.MinBST),Math.Max(a.MaxBST,b.MaxBST),
            a.FullRandom||b.FullRandom);
        _bstRanges.RemoveAt(end);
        return true;
    }

    internal string DiagnoseWild(IReadOnlyDictionary<string,int> options)
    {
        if(!SupportsWildTemplate)throw new NotSupportedException("Diagnóstico solo Gen7.");
        EditorValues.Validate(RandomOptions,options);
        if(options["SimilarBST"]==1&&options["Progressive"]==1)
            throw new ArgumentException("BST similar y progresivo no pueden activarse a la vez.");
        var picker=new SpeciesRandomizer(Config)
        {
            G1=options["G1"]==1,G2=options["G2"]==1,G3=options["G3"]==1,
            G4=options["G4"]==1,G5=options["G5"]==1,G6=options["G6"]==1,
            G7=options["G7"]==1,L=options["Legends"]==1,E=options["Events"]==1,
            rBST=false
        };
        int[] allowed=picker.GetAllowedSpeciesPool().Where(id=>id>=1 &&
            id<=Config.MaxSpeciesID && id<Config.Personal.Table.Length).ToArray();
        if(allowed.Length==0)throw new InvalidDataException("Los filtros no permiten ninguna especie.");
        var sb=new StringBuilder();
        sb.AppendLine($"Juego: {(Config.USUM?"USUM":"SM")}");
        sb.AppendLine($"Especies disponibles: {allowed.Length}");
        sb.AppendLine($"Tablas: {Entries.Count}");
        int inaccessible=_locations.Values.Count(l=>!AccessibleForProgression(l));
        sb.AppendLine($"Tablas inaccesibles según ZoneData: {inaccessible}");
        sb.AppendLine($"Modo: {options["SlotMode"]} · SOS: {options["SOS"]} · Clima: {options["Weather"]}");
        sb.AppendLine($"Alola: {options["AlolanForms"]} · Mega: {options["MegaForms"]}");
        sb.AppendLine($"Niveles: {(options["ModifyLevel"]==1?"modificar":"conservar")}");
        if(options["Progressive"]==1)
        {
            sb.AppendLine("Rangos BST progresivos:");
            for(int i=0;i<_bstRanges.Count;i++)
            {
                string k=$"BST.{i}.";
                int lo=options[k+"MinLevel"],hi=options[k+"MaxLevel"];
                int min=options[k+"MinBST"],max=options[k+"MaxBST"];
                bool full=options[k+"FullRandom"]==1;
                int count=full?allowed.Length:allowed.Count(id=>
                    Config.Personal[id].BST>=min&&Config.Personal[id].BST<=max);
                sb.AppendLine($"  Nv. {lo}–{hi}: BST {min}–{max}"+
                    $" · {(full?"libre":"limitado")} · {count} especies");
                if(count==0)sb.AppendLine("  ADVERTENCIA: rango sin especies válidas.");
            }
        }
        sb.AppendLine("Solo se analiza el proyecto; este diagnóstico no guarda ningún archivo.");
        return sb.ToString();
    }
}
