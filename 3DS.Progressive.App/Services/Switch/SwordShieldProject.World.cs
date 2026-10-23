using pk3DS.Core.NX;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldProject
{
    private Dictionary<string,byte[]> WorldFiles(bool items)
    {
        RequireEditable();VerifySource();var paths=items?new[]{SwordShieldGameplay.PlacementPath,SwordShieldGameplay.ItemHashesPath}:new[]{SwordShieldGameplay.EncountersPath};return paths.ToDictionary(p=>p,p=>LoadGameplay(p));
    }
    internal IReadOnlyList<int> WorldItemIds()=>SwordShieldWorldData.AvailableItems(WorldFiles(true));
    internal IReadOnlyList<SwordShieldWorldCell> ReadWorld(bool items)
    {var rows=SwordShieldWorldData.Read(WorldFiles(items),items,Identity.TitleId=="01008DB008C2C000");VerifySource();return rows;}
    internal SwordShieldGameplayPreview PreviewWorld(bool items,IReadOnlyList<SwordShieldDataEdit> edits)
    {var result=SwordShieldWorldData.Edit(WorldFiles(items),_draft,items,Identity.TitleId=="01008DB008C2C000",edits);VerifySource();return new(_id,RevisionHash,new SwordShieldGameplayOptions(0),result);}
}
