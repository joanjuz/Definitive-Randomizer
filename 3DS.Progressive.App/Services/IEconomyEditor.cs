using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services;
public interface IEconomyEditor
{
    string EconomyPreset {get;}
    string EconomyHelp=>"Compra en múltiplos de 10; venta equivale a la mitad. Nada se guarda hasta aplicar una vista previa.";
    IReadOnlyList<ItemPriceRow> PriceRows();
    EditorProposal PreviewEconomy(IReadOnlyList<ItemPriceRow> rows);
    void Apply(EditorProposal proposal);
}
