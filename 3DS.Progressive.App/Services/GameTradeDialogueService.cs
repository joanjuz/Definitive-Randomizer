using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Curated original USUM Spanish trade dialogue cleanup, staged safely.</summary>
internal sealed class GameTradeDialogueService
{
    private readonly GameProjectSession _project;
    private readonly GameConfig _config;
    private readonly string _path;
    private readonly GARCReference _reference;
    private readonly byte[] _fingerprint;
    private static readonly Guid Owner=Guid.NewGuid();
    internal GameTradeDialogueService(GameProjectSession project)
    {
        _project=project;_config=project.GameConfiguration;
        if(!_config.USUM)throw new NotSupportedException("Solo USUM.");
        const int spanishBank=6;
        var origin=_config.GetGARCReference("storytext");
        _reference=origin.LanguageVariant?origin.GetRelativeGARC(spanishBank):origin;
        _path=Path.Combine(_config.RomFS,_reference.Reference);
        if(!File.Exists(_path))throw new FileNotFoundException("Falta el banco de historia español de USUM.",_path);
        _fingerprint=SHA256.HashData(File.ReadAllBytes(_path));
    }
    private static Dictionary<int, string> GetSpanishTradeDialogueOverrides() =>
        new()
        {
            [14] =
                "Hola. \u00BFTe apetece hacer un intercambio?\n" +
                "Si tienes un Pok\u00E9mon para cambiar,\\c\n" +
                "yo te doy uno de los m\u00EDos.",

            [15] =
                "\u00A1Gracias! Cuidar\u00E9 muy bien del Pok\u00E9mon\n" +
                "que me has dado.\\c\n" +
                "\u00A1T\u00FA tambi\u00E9n cuida mucho del m\u00EDo!",

            [16] =
                "\u00A1Un momento! Ese no es el Pok\u00E9mon\n" +
                "que estoy buscando.\\c\n" +
                "Si no tienes el que busco, no pasa nada.",

            [20] =
                "\u00A1Eh, t\u00FA! \u00BFEres Entrenador Pok\u00E9mon?\\c\n" +
                "\u00BFTe apetece hacer un intercambio?\n" +
                "Yo te doy uno de mis Pok\u00E9mon.\\c\n" +
                "\u00BFQu\u00E9 me dices?",

            [21] =
                "\u00A1Muchas gracias!\\c\n" +
                "\u00A1Cuida bien del Pok\u00E9mon que te he dado,\n" +
                "que yo har\u00E9 lo mismo con el tuyo!",

            [22] =
                "Bueno, no s\u00E9 t\u00FA... Pero ese no es el Pok\u00E9mon\n" +
                "que estoy buscando.\\c\n" +
                "Si no tienes el que necesito, d\u00EDmelo y ya est\u00E1.",

            [26] =
                "Llevo un Pok\u00E9mon que me viene de maravilla\n" +
                "cuando paso alg\u00FAn apuro en la Colina Saltagua...\\c\n" +
                "Pero me apetece cambiarlo por otro Pok\u00E9mon.\n" +
                "\u00BFQu\u00E9 dices?",

            [28] =
                "Oye, ese no es el Pok\u00E9mon que te hab\u00EDa pedido...\n" +
                "Si no quieres hacer el intercambio, d\u00EDmelo.",

            [30] =
                "\u00A1Viva! \u00A1Qu\u00E9 Pok\u00E9mon tan mono!\\c\n" +
                "El que te he dado tambi\u00E9n me encanta.\n" +
                "Cu\u00EDdalo muy bien, \u00BFvale?",

            [32] =
                "\u00BFNo te gustar\u00EDa hacerte con mi Pok\u00E9mon?\n" +
                "Se ha portado genial conmigo.\\c\n" +
                "Si quieres, podemos hacer un intercambio.",

            [33] =
                "\u00A1Guay! No te preocupes, que tu Pok\u00E9mon\n" +
                "estar\u00E1 en buenas manos conmigo.\\c\n" +
                "T\u00FA cuida bien del m\u00EDo, \u00BFeh?",

            [34] =
                "\u00A1No, ese no! Ese no es el Pok\u00E9mon que busco.\n" +
                "\u00BFQu\u00E9 parte no has entendido?",

            [36] =
                "Cada Pok\u00E9mon tiene sus puntos fuertes y d\u00E9biles.\n" +
                "\u00A1Conocerlos bien viene genial en combate!",

            [38] =
                "Oye, \u00BFte apetece intercambiar Pok\u00E9mon?\n" +
                "Yo te doy uno de los m\u00EDos a cambio del tuyo.",

            [39] =
                "Adi\u00F3s, compa\u00F1ero.\n" +
                "\u00A1Y bienvenido, nuevo amigo!",

            [40] =
                "\u00A1Venga ya!\n" +
                "\u00A1Ese no es el Pok\u00E9mon que te he pedido!",

            [42] =
                "Cu\u00EDdame bien al Pok\u00E9mon que te he dado, \u00BFeh?\\c\n" +
                "\u00A1Espero que el tuyo tambi\u00E9n sea feliz conmigo!",

            [44] =
                "Oye, \u00BFte apetece intercambiar un Pok\u00E9mon conmigo?\\c\n" +
                "Quiero comprobar una cosa que dicen sobre\n" +
                "los intercambios. \u00BFMe echas una mano?",

            [45] =
                "Los Pok\u00E9mon esconden un mont\u00F3n de misterios...\n" +
                "\u00A1Por eso me encanta investigar sobre ellos!",

            [46] =
                "\u00A1No, no! Necesito hacer un intercambio concreto\n" +
                "para comprobar mi teor\u00EDa.",

            [48] =
                "\u00A1Toma ya! \u00A1El intercambio ha funcionado!\\c\n" +
                "\u00A1Ya puedo comprobar si era verdad lo que dec\u00EDan!",

            [50] =
                "\u00BFQu\u00E9, te gusta mi Pok\u00E9mon?\n" +
                "Pues te lo cambio por uno de los tuyos.",

            [51] =
                "Gracias. Cuida de mi Pok\u00E9mon,\n" +
                "que yo har\u00E9 lo mismo con el tuyo.",

            [52] =
                "Vaya, hombre.\n" +
                "Ese no es el Pok\u00E9mon que hab\u00EDa pedido...",

            [54] =
                "Hay Pok\u00E9mon cuyo aspecto cambia seg\u00FAn d\u00F3nde vivan.\\c\n" +
                "\u00A1La naturaleza nunca deja de sorprenderme!",

            [56] =
                "\u00A1Mi Pok\u00E9mon es incre\u00EDble y no lo cambio\n" +
                "por nada del mundo!\\c\n" +
                "Bueno... quiz\u00E1 s\u00ED por otro que me guste.\n" +
                "\u00BFQu\u00E9 me dices?",

            [57] =
                "\u00A1Comienza una nueva etapa para nuestros Pok\u00E9mon!",

            [58] =
                "\u00A1Eh! \u00A1Ese no es el Pok\u00E9mon que quer\u00EDa!",

            [60] =
                "\u00A1Con este Pok\u00E9mon llegar\u00E9 a lo m\u00E1s alto del\n" +
                "\u00C1rbol de Combate, ya ver\u00E1s!\\c\n" +
                "\u00A1Espero que t\u00FA tambi\u00E9n tengas grandes combates\n" +
                "con el que te he dado!",
        };
    internal EditorProposal Preview()
    {
        byte[] original=GarcTransaction.ReadUnchanged(_path,_fingerprint);
        byte[][] files=new GARC.MemGARC(original).Files;
        const int storyId=14;
        if(storyId>=files.Length)throw new InvalidDataException("No existe storytext[14] en español.");
        int oldLanguage=_config.Language;
        string[] before,after;
        byte[] encoded;
        try
        {
            _config.Language=6;
            before=TextFile.GetStrings(_config,files[storyId]);
            after=(string[])before.Clone();
            foreach(var p in GetSpanishTradeDialogueOverrides())
            {
                if(p.Key<0||p.Key>=after.Length)
                    throw new InvalidDataException($"Falta diálogo de intercambio #{p.Key}.");
                after[p.Key]=p.Value;
            }
            encoded=TextFile.GetBytes(_config,after);
            string[] verified=TextFile.GetStrings(_config,encoded);
            if(!verified.SequenceEqual(after))
                throw new InvalidDataException("La codificación del diálogo no pasó el roundtrip; no se guardará.");
        }
        finally{_config.Language=oldLanguage;}
        files[storyId]=encoded;
        byte[] output=GarcTransaction.Build(original,_reference,files,preserveRecordSize:false);
        var changes=GetSpanishTradeDialogueOverrides().Where(x=>before[x.Key]!=x.Value)
            .Select(x=>new BatchChange(storyId,"Diálogos de intercambios ES",
                "Línea "+x.Key,before[x.Key],x.Value)).ToArray();
        return new EditorProposal(Owner,"Ocultar nombres en intercambios USUM",0,
            [new FileChange(_path,original,output)],changes,
            new Dictionary<string,string>
            {
                ["Scope"]="storytext español / archivo 14",
                ["Source"]="Curated USUM Spanish dialogue overrides (pk3DS Progressive)"
            });
    }
    internal void Apply(EditorProposal preview)
    {
        if(preview.Owner!=Owner)throw new InvalidOperationException("Vista previa ajena al editor de diálogos.");
        foreach(var item in preview.Files)GarcTransaction.ReadUnchanged(item.Path,item.Fingerprint);
        _project.History.Commit(preview.Files,"Diálogos de intercambios",preview.Action,preview.Changes,
            preview.Parameters);
    }
}
