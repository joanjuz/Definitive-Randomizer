using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldRecordEditor
{
    internal bool HasEconomy=>_section==SwordShieldDataSection.Items;
    public string EconomyPreset=>"Precios heredados Progressive · objetos compartidos";
    public string EconomyHelp=>"Solo precios de compra en dinero. Watts y BP se editan por separado en la ficha del objeto. Los objetos nuevos sin preset conservan su precio. Revisa la vista previa antes de aplicar.";
    public IReadOnlyList<ItemPriceRow> PriceRows()=>_cells.Where(c=>c.Field=="Precio").Select(c=>new ItemPriceRow {Id=c.Entry,Name=_project.ItemName(c.Entry),CurrentBuy=c.Value,TargetBuy=GameEconomyDefaults.TargetPrice(c.Entry)??c.Value,Included=GameEconomyDefaults.TargetPrice(c.Entry).HasValue&&SwordShieldGameplay.IsAllowedItem(c.Entry),Source=GameEconomyDefaults.TargetPrice(c.Entry).HasValue?"Progressive · compartido":"Actual"}).ToArray();
    public EditorProposal PreviewEconomy(IReadOnlyList<ItemPriceRow> rows)
    {
        if(!HasEconomy)throw new InvalidOperationException("Esta sección no es de economía.");
        if(rows.Select(r=>r.Id).Distinct().Count()!=rows.Count)throw new ArgumentException("Objeto duplicado.");
        var changes=rows.Where(r=>r.Included).Select(r=>{var cell=_cells.Single(c=>c.Entry==r.Id&&c.Field=="Precio");return new SwordShieldDataEdit(cell.Key,r.TargetBuy);}).ToArray();
        var data=_project.PreviewData(_section,changes);var proposal=new EditorProposal(_owner,"Fix Economy",0,Array.Empty<FileChange>(),data.Result.Changes.Select(c=>new BatchChange(0,c.Location,c.Field,c.Before.ToString(),c.After.ToString())),new Dictionary<string,string>{{"Platform","SWSH"}});_proposals.Add(proposal,data);return proposal;
    }
}
