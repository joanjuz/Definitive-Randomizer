namespace ThreeDS.Progressive.Services;

/// <summary>Shared safe rebuild path for the packaged Gen6/Gen7 original batch host.</summary>
public static class NativeBatchRomBridge
{
    public static void ApplyPickup(string root,int seed)=>ApplyPickupSession(GameProjectSession.Open(root,recoverInterruptedSave:false),seed);
    internal static void ApplyPickupSession(GameProjectSession project,int seed)
    {
        if(project.Generation!=7)throw new System.IO.InvalidDataException("El replay de Pickup requiere Gen7.");var editor=new GamePickupService(project);var proposal=editor.Preview(editor.Randomize(editor.Read(),seed));if(proposal.Changes.Count>0)editor.Apply(proposal);
    }
    public static void ApplyShiny(string root,int rerolls,bool everything)
    {
        var service=new GameShinyRateService(GameProjectSession.Open(root,recoverInterruptedSave:false));
        var preview=service.Preview(rerolls,everything);if(preview.Changes.Count>0)service.Apply(preview.File,preview.Changes,false);
    }
    private static readonly ExeFsBuildCache BatchExeFsCache=new();
    public static void Rebuild(string root,string target,bool trimmed)=>RebuildWithProgress(root,target,trimmed,null);
    public static void RebuildWithProgress(string root,string target,bool trimmed,Action<string,int,int>? progress)
    {
        var project=GameProjectSession.Open(root,recoverInterruptedSave:false);
        new GameRomRebuildService(project,BatchExeFsCache).BuildAsync(trimmed?RomRebuildKind.Trimmed3DS:RomRebuildKind.Full3DS,target,progress).GetAwaiter().GetResult();
    }
}
