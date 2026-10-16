using System.Text.RegularExpressions;
using pk3DS.Core;

namespace ThreeDS.Progressive.Services;

public sealed record TextCommandEntry(string Name,string Syntax,string Function,string Notes,string Example,bool Observed)
{
    public bool CanCopy=>Example.Length>0;
    public string Display=>Name+" · "+Function+(Observed?" · presente en este archivo":"");
}

internal static class TextCommandCatalogue
{
    private static readonly Dictionary<string,string> Functions=new()
    {
        ["TRNAME"]="Nombre de un Entrenador preparado por el evento.", ["PKNAME"]="Nombre de una especie Pokémon preparado por el evento.",
        ["PKNICK"]="Mote de un Pokémon preparado por el evento.", ["TYPE"]="Nombre de un tipo Pokémon.", ["LOCATION"]="Nombre de un lugar.",
        ["ABILITY"]="Nombre de una habilidad.", ["MOVE"]="Nombre de un movimiento.", ["ITEM1"]="Nombre de objeto, variante de formato 1.",
        ["ITEM2"]="Nombre de objeto, variante de formato 2.", ["sTRBAG"]="Nombre de un saco de Superentrenamiento; identificador heredado del formato.",
        ["BOX"]="Nombre de una caja del PC.", ["EVSTAT"]="Nombre de una característica para puntos de base.", ["OPOWER"]="Nombre de un Poder O; identificador heredado del formato.",
        ["RIBBON"]="Nombre de una cinta.", ["MIINAME"]="Nombre de un Mii.", ["WEATHER"]="Nombre de un clima.", ["TRNICK"]="Apodo de un Entrenador.",
        ["1stchrTR"]="Primer carácter del nombre de un Entrenador.", ["SHOUTOUT"]="Mensaje de saludo de un Entrenador.", ["BERRY"]="Nombre de una baya.",
        ["REMFEEL"]="Expresión relacionada con un recuerdo del Pokémon.", ["REMQUAL"]="Calificador relacionado con un recuerdo del Pokémon.",
        ["WEBSITE"]="Texto relacionado con un sitio web.", ["CHOICECOS"]="Texto relacionado con una elección cosmética.", ["GSYNCID"]="Identificador de Sincronizar Juego.",
        ["PRVIDSAY"]="Mensaje relacionado con un vídeo de presentación.", ["BTLTEST"]="Texto relacionado con un Test de Combate.", ["GENLOC"]="Referencia genérica de lugar.",
        ["CHOICEFOOD"]="Texto relacionado con una elección de comida.", ["HOTELITEM"]="Objeto de una actividad del hotel.", ["TAXISTOP"]="Parada de taxi.",
        ["MAISTITLE"]="Título relacionado con la Mansión Batalla.", ["ITEMPLUR0"]="Variante gramatical de objeto 0.", ["ITEMPLUR1"]="Variante gramatical de objeto 1.",
        ["GENDBR"]="Alternativas gramaticales según género.", ["NUMBRNCH"]="Alternativas gramaticales según número.",
        ["COLOR"]="Cambio de color del texto.", ["iCOLOR2"]="Control de color de variante 2.", ["iCOLOR3"]="Control de color de variante 3."
    };
    private static readonly HashSet<string> SimpleTemplates=["TRNAME","PKNAME","PKNICK","TYPE","LOCATION","ABILITY","MOVE","ITEM1","ITEM2","BOX","COLOR"];
    internal static IReadOnlyList<TextCommandEntry> Build(GameConfig config,IEnumerable<string> fileLines)
    {
        string[] lines=fileLines.ToArray();
        var observed=lines.SelectMany(line=>Regex.Matches(line,@"\[VAR ([^\s\](]+)(?:\([^\]]*\))?\]").Cast<Match>().Where(m=>IsUnescaped(line,m.Index)))
            .GroupBy(m=>m.Groups[1].Value,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Value,StringComparer.Ordinal);
        var rows=new List<TextCommandEntry>
        {
            new(@"\n",@"\n","Salto de línea dentro del mensaje.","Se escribe una barra y la letra n, no un salto real en la tabla.",@"\n",Seen(lines,@"\n")),
            new(@"\r",@"\r","Espera una pulsación y desplaza el texto.","Permite continuar un mensaje desplazando su contenido.",@"\r",Seen(lines,@"\r")),
            new(@"\c",@"\c","Espera una pulsación y limpia el cuadro de texto.","La continuación se muestra después de limpiar el cuadro.",@"\c",Seen(lines,@"\c")),
            new(@"\\",@"\\","Muestra una barra invertida literal.","Dos barras representan una barra visible.",@"\\",Seen(lines,@"\\")),
            new(@"\[",@"\[","Muestra un corchete de apertura literal.","Evita interpretarlo como comienzo de un comando.",@"\[",Seen(lines,@"\[")),
            new("WAIT","[WAIT valor]","Pausa del mensaje.","Valor decimal de 0 a 65535. La unidad de tiempo no está documentada en el lector; copiá un valor existente.","[WAIT 30]",Seen(lines,"[WAIT ")),
            new("~","[~ número]","Marcador de una entrada de texto reservada.","No es texto visible ni un comando para añadir diálogos. Conservá el marcador original.","",Seen(lines,"[~ "))
        };
        foreach(var variable in config.Variables)
        {
            string name=variable.Name;
            bool found=observed.TryGetValue(name,out string? example);
            bool number=name.StartsWith("NUM",StringComparison.Ordinal)&&int.TryParse(name.AsSpan(3),out _);
            string function=Functions.GetValueOrDefault(name,number?"Número con formato "+name+"; el evento aporta su valor.":"Identificador reconocido por el lector; función detallada no documentada.");
            string notes="Los parámetros de VAR son hexadecimales de cuatro dígitos (000A = 10). Los índices suelen señalar valores preparados por el evento: 0000 no significa la especie o el Entrenador nº 0. Conservá cantidad, orden y valores del original.";
            if(name is "GENDBR" or "NUMBRNCH" or "ITEMPLUR0" or "ITEMPLUR1")notes+=" Usa parámetros y texto de alternativas empaquetados; no se deben reconstruir solo cambiando los números.";
            if(!found && (SimpleTemplates.Contains(name)||number))example=$"[VAR {name}(0000)]";
            rows.Add(new(name,$"[VAR {name}(parámetros)] · código {variable.Code:X4}",function,notes+(found?" Ejemplo real del archivo guardado seleccionado.":" Plantilla de sintaxis; no garantiza que el evento tenga ese valor preparado."),example??"",found));
        }
        foreach(var pair in observed.Where(p=>!config.Variables.Any(v=>v.Name==p.Key)))
            rows.Add(new(pair.Key,pair.Value,"Código presente en este archivo cuya función no está documentada.","El lector admite identificadores hexadecimales. No supongas que controlan un personaje, una pausa o un efecto: conservá el original.",pair.Value,true));
        return rows;
    }
    private static bool IsUnescaped(string line,int index)
    {
        int slashes=0;for(int i=index-1;i>=0&&line[i]=='\\';i--)slashes++;
        return slashes%2==0;
    }
    private static bool Seen(IEnumerable<string> lines,string token)
    {
        foreach(string line in lines)
            for(int i=line.IndexOf(token,StringComparison.Ordinal);i>=0;i=line.IndexOf(token,i+1,StringComparison.Ordinal))
                if(IsUnescaped(line,i))return true;
        return false;
    }

}
