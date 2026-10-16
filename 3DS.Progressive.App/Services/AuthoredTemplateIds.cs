using System.Globalization;
using System.IO;
namespace ThreeDS.Progressive.Services;
internal sealed partial class AuthoredTemplate
{
    private static readonly string[] MoveIdFields=["Move","Type","Category","Quality","Targeting","Inflict","UserStat","TargetStat","Stat1","Stat2","Stat3","ZEffect"];
    private IEnumerable<string> IdFields()=>Kind switch
    {
        "Moves"=>MoveIdFields,"Evolutions"=>["Source","Target","Method","Argument","Form"],"Stats"=>["Entry"],"Shops"=>ShopMode=="Replace"?["Shop","Item"]:["Stage","Item"],_=>[]
    };
    internal void CanonicalizeIds(GameProjectSession p)
    {
        if(Rows is null)throw new InvalidDataException("Faltan las filas del template.");
        foreach(var row in Rows)foreach(string field in IdFields())
        {
            if(!row.TryGetValue(field,out string? token)||string.IsNullOrWhiteSpace(token))continue;
            if(int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out int id)){row[field]=id.ToString(CultureInfo.InvariantCulture);continue;}
            if(Kind=="Moves"&&field!="Move")id=new MoveTemplateApplier(p.Generation,p.Types.ToArray()).ResolveTemplateChoice(field,token);
            else if(field=="Method")id=token.ToLowerInvariant() switch{"level" or "levelup" or "l"=>4,"friendship" or "friend" or "f"=>1,"malelevel" or "levelmale" or "male" or "m"=>23,"femalelevel" or "levelfemale" or "female" or "h"=>24,"useditem" or "item" or "stone" or "u"=>8,_=>-1};
            else
            {
                IReadOnlyList<string>? names=field switch{"Move"=>p.MoveNames,"Item" or "Argument"=>p.ItemNames,"Entry"=>Names(p),"Source" or "Target"=>p.GameConfiguration.GetText(pk3DS.Core.TextName.SpeciesNames),_=>null};
                id=names is null?-1:Enumerable.Range(0,names.Count).FirstOrDefault(i=>string.Equals(names[i],token,StringComparison.OrdinalIgnoreCase),-1);
            }
            if(id<0)throw new InvalidDataException($"No se pudo resolver {field}: {token}. Importá su ID numérico.");
            row[field]=id.ToString(CultureInfo.InvariantCulture);
        }
        RequireNumericIds();
    }
    private void RequireNumericIds()
    {
        foreach(var row in Rows)foreach(string field in IdFields())if(row.TryGetValue(field,out string? value)&&!string.IsNullOrWhiteSpace(value)&&!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out _))throw new InvalidDataException($"{field} debe guardarse como ID numérico, no como nombre traducido.");
    }
}
