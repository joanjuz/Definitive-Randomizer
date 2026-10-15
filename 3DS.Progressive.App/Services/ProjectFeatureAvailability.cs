namespace ThreeDS.Progressive.Services;

/// <summary>Navigation and visibility share the same game compatibility rules.</summary>
internal static class ProjectFeatureAvailability
{
    internal static void ValidateActions(GameProjectSession project,IEnumerable<string> actions)
    {if(!Supports(project,"MoveBalance")&&actions.Contains("moves.balance",StringComparer.OrdinalIgnoreCase))throw new System.IO.InvalidDataException("La receta contiene balance Progressive para un juego no compatible (Sol/Luna).");}
    internal static bool Supports(GameProjectSession project,string section)=>section switch
    {
        "MoveBalance"=>project.Generation==6||project.GameConfiguration.USUM,
        "Tutor6"=>project.GameConfiguration.ORAS,
        "TitleScreen6"=>project.Generation==6,
        "TitleScreen7"=>project.Generation==7,
        "GarcShuffle"=>project.Generation==6,
        "PersistentItems" or "PlayerCaps" or "UsumRelearner" or "CroRecipes"=>project.GameConfiguration.USUM,
        "Trade6" or "CatchZones" or "OPower"=>project.Generation==6,
        "OWSE"=>project.Generation==7,
        "Marts"=>project.Generation is 6 or 7,
        _=>true
    };
}
