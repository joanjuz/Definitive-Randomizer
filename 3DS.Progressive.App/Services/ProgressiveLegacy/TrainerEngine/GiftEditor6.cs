using pk3DS.Core;
namespace ThreeDS.Progressive.Services.ProgressiveLegacy;
internal static class GiftEditor6 {
public static Dictionary<int,int[]> GetMegaDictionary(GameConfig config)=>config.XY?MegaDictionaryXY:MegaDictionaryAO.Concat(MegaDictionaryXY).ToDictionary(p=>p.Key,p=>p.Value);
private static readonly Dictionary<int, int[]> MegaDictionaryXY = new()
    {
        {003, [659] }, // Venusaur @ Venusaurite
        {006, [660, 678] }, // Charizard @ Charizardite X/Y
        {009, [661] }, // Blastoise @ Blastoisinite
        {065, [679] }, // Alakazam @ Alakazite
        {094, [656] }, // Gengar @ Gengarite
        {115, [675] }, // Kangaskhan @ Kangaskhanite
        {127, [671] }, // Pinsir @ Pinsirite
        {130, [676] }, // Gyarados @ Gyaradosite
        {142, [672] }, // Aerodactyl @ Aerodactylite
        {150, [662, 663] }, // Mewtwo @ Mewtwonite X/Y
        {181, [658] }, // Ampharos @ Ampharosite
        {212, [670] }, // Scizor @ Scizorite
        {214, [680] }, // Heracross @ Heracronite
        {229, [666] }, // Houndoom @ Houndoominite
        {248, [669] }, // Tyranitar @ Tyranitarite
        {257, [664] }, // Blaziken @ Blazikenite
        {282, [657] }, // Gardevoir @ Gardevoirite
        {303, [681] }, // Mawile @ Mawilite
        {306, [667] }, // Aggron @ Aggronite
        {308, [665] }, // Medicham @ Medichamite
        {310, [682] }, // Manectric @ Manectite
        {354, [668] }, // Banette @ Banettite
        {359, [677] }, // Absol @ Absolite
        {380, [684] }, // Latias @ Latiasite
        {381, [685] }, // Latios @ Latiosite
        {445, [683] }, // Garchomp @ Garchompite
        {448, [673] }, // Lucario @ Lucarionite
        {460, [674] }, // Abomasnow @ Abomasite
    };
private static readonly Dictionary<int, int[]> MegaDictionaryAO = new()
    {
        {015, [770] }, // Beedrill @ Beedrillite
        {018, [762] }, // Pidgeot @ Pidgeotite
        {080, [760] }, // Slowbro @ Slowbronite
        {208, [761] }, // Steelix @ Steelixite
        {254, [753] }, // Sceptile @ Sceptilite
        {260, [752] }, // Swampert @ Swampertite
        {302, [754] }, // Sableye @ Sablenite
        {319, [759] }, // Sharpedo @ Sharpedonite
        {323, [767] }, // Camerupt @ Cameruptite
        {334, [755] }, // Altaria @ Altarianite
        {362, [763] }, // Glalie @ Glalitite
        {373, [769] }, // Salamence @ Salamencite
        {376, [758] }, // Metagross @ Metagrossite
        {428, [768] }, // Lopunny @ Lopunnite
        {475, [756] }, // Gallade @ Galladite
        {531, [757] }, // Audino @ Audinite
        {719, [764] }, // Diancie @ Diancite
    };
}