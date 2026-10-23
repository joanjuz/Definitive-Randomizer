using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldRecordEditor
{
    private IReadOnlyList<MoveChoice>? ContextChoices(SwordShieldDataCell cell,IReadOnlyDictionary<string,int> values)
    {
        if(_section!=SwordShieldDataSection.Evolutions||cell.Field!="Argumento")return Choices(cell);
        var method=_cells.Single(c=>c.Entry==cell.Entry&&c.Slot==cell.Slot&&c.Field=="Método");int id=values.GetValueOrDefault(UiKey(method),method.Value);IReadOnlyList<MoveChoice>? choices=id switch
        {
            6 or 8 or 17 or 18 or 19 or 20 or 42=>Enumerable.Range(0,1608).Select(i=>new MoveChoice(i,_project.ItemName(i))).ToArray(),
            21=>Enumerable.Range(0,827).Select(i=>new MoveChoice(i,_project.MoveName(i))).ToArray(),
            22=>_project.Entries.Where(e=>e.Form==0).Select(e=>new MoveChoice(e.Species,e.Name)).ToArray(),
            29 or 30=>Choices(cell with{Field="Tipo"}),
            _=>null
        };
        int current=values.GetValueOrDefault(UiKey(cell),cell.Value);if(choices is not null&&!choices.Any(c=>c.Id==current))choices=choices.Append(new MoveChoice(current,"ID "+current+" · conservar")).ToArray();return choices;
    }
}
