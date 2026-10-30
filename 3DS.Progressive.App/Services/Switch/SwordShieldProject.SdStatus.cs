using System.IO;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldProject
{
    internal static string DefaultRyujinxSd=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Ryujinx","sdcard");
    internal string RuntimeSdStatus(string sdRoot)
    {
        if(!PokeFeurEnabled)return "No hay runtime añadido a la sesión.";
        if(!Directory.Exists(sdRoot))return "SD no localizada. Instala la configuración en la SD usada por tu emulador.";
        var plan=PreviewPokeSd(sdRoot);string pools=$"Pool de combate: {PokeFeurCurrent!.BattlePool.Count} especies · semilla {PokeFeurCurrent.Seed}. ";
        return plan.ExistingHash==plan.NewHash?pools+"La SD coincide con esta sesión.":pools+"ATENCIÓN: la SD tiene otra configuración o está vacía. El juego no usará estas opciones hasta instalarla.";
    }
}
