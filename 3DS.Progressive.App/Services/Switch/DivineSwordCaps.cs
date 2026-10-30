namespace ThreeDS.Progressive.Services.Switch;
// Selected DivineSword caps, with version-exclusive gym IDs corrected for Sword.
internal static class DivineSwordCaps
{
    internal static IReadOnlyDictionary<int,int> ForTitle(string titleId)=>titleId=="01008DB008C2C000"?new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(Levels.ToDictionary(p=>p.Key==77?78:p.Key==135?136:p.Key,p=>p.Value)):Levels;
    internal static IReadOnlyDictionary<int,int> Levels {get;} = new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(new Dictionary<int,int>
    {
        [7]=11,
        [8]=11,
        [9]=11,
        [32]=27,
        [36]=34,
        [37]=43,
        [77]=50,
        [107]=75,
        [108]=58,
        [121]=29,
        [122]=29,
        [123]=29,
        [124]=47,
        [125]=47,
        [126]=47,
        [127]=59,
        [128]=59,
        [129]=59,
        [130]=83,
        [131]=83,
        [132]=83,
        [133]=52,
        [134]=85,
        [135]=68,
        [138]=73,
        [144]=78,
        [145]=84,
        [149]=92,
        [191]=17,
        [192]=17,
        [193]=17,
        [195]=21,
        [196]=42,
        [202]=70,
        [203]=70,
        [204]=70,
        [210]=87,
        [212]=88,
        [213]=87,
        [240]=36,
    });
}
