using System.IO;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

// Legacy patchers sync their example rows only inside the temporary workspace.
internal static class CustomBalanceTemplates
{
    internal static string NormalizeToken(string value) => MoveTemplates.NormalizeToken(value);
    internal static string GetMoveTemplatePath(int generation) => Path.Combine(Main.Workspace, "custom_balance_templates", $"moves_gen{generation}.csv");
}
