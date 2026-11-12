using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using Level = pk3DS.Core.Modding.Research.PatchCapabilityLevel;
using State = pk3DS.Core.Modding.Research.PatchResourceState;

/// <summary>
/// Player Level Caps and Persistent Items both write to Battle.cro. These checks install them in both orders on
/// synthetic modules (one with spare room, one that forces the code segment to grow) and verify that each patch
/// still recognises its own installation and the other one afterwards. Synthetic binaries only.
/// </summary>
internal static class PatchInteractionChecks
{
    private const int GateAt = 0x10000;
    /// <summary>The caps skeleton plus the two Persistent Items gates inside its code segment.</summary>
    private static byte[] Battle(bool tight)
    {
        byte[] cro = DefinitiveGameDataCapsChecks.StockBattle(), gates = Review73Checks.Cro();
        gates.AsSpan(0x180, 0x40).CopyTo(cro.AsSpan(GateAt));
        BitConverter.GetBytes(0x30000).CopyTo(cro, 0xC0); // read-only data starts after the code, as the integrity hashes expect
        if (tight)
        {
            // As in a real module, the segment table lives after the code; no spare room is left in the code segment.
            Array.Copy(cro, 0x140, cro, 0x30800, 0x40); Array.Clear(cro, 0x140, 0x40); BitConverter.GetBytes(0x30800).CopyTo(cro, 0xC8);
            for (int i = 0x16000; i < 0x30000; i++) cro[i] = 0xAA;
        }
        return cro;
    }
    private static bool CapsOwn(byte[] battle, byte[] code, LevelCapTable table) =>
        LevelCapPatch.InspectCapability(battle, code) is { CanApply: true } report && report.Resources.All(r => r.State == State.OwnInstall) &&
        LevelCapPatch.InspectBattle(battle, out var installed) == LevelCapCopyState.Applied && installed.ToBytes().SequenceEqual(table.ToBytes());
    private static bool ItemsOwn(byte[] battle) =>
        GamePersistentItemsService.InspectCapability(battle) is { CanApply: true } report && report.Resources.All(r => r.State == State.OwnInstall);
    private static bool ItemsStock(byte[] battle) =>
        GamePersistentItemsService.InspectCapability(battle) is { CanApply: true } report && report.Resources.All(r => r.State == State.Original);

    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var table = PlayerLevelCapRecipeChecks.Table();
        foreach (bool tight in new[] { false, true })
        {
            string name = tight ? "GrowingModule" : "RoomyModule";
            byte[] battle = Battle(tight), code = DefinitiveGameDataCapsChecks.StockCode();
            var capsBefore = LevelCapPatch.InspectCapability(battle, code);
            check(capsBefore.Level == (tight ? Level.CompatibleByAdaptation : Level.Verified) && ItemsStock(battle), "Interaction_BothPatchesAccreditedOnTheOriginal_" + name);

            // Caps first, then items.
            var caps = LevelCapPatch.InstallManaged(battle, code, table);
            check(caps.Success && CapsOwn(caps.BattleCro, caps.CodeBin, table) && ItemsStock(caps.BattleCro), "Interaction_CapsLeaveTheItemGatesLocatable_" + name);
            byte[] capsThenItems = GamePersistentItemsService.Patch(caps.BattleCro).Bytes;
            check(ItemsOwn(capsThenItems) && CapsOwn(capsThenItems, caps.CodeBin, table), "Interaction_ItemsAfterCapsKeepBothInstalled_" + name);

            // Items first, then caps.
            byte[] items = GamePersistentItemsService.Patch(battle).Bytes;
            check(ItemsOwn(items) && LevelCapPatch.InspectCapability(items, code).Level == capsBefore.Level, "Interaction_ItemsDoNotChangeTheLevelOfCaps_" + name);
            var itemsThenCaps = LevelCapPatch.InstallManaged(items, code, table);
            check(itemsThenCaps.Success && CapsOwn(itemsThenCaps.BattleCro, itemsThenCaps.CodeBin, table) && ItemsOwn(itemsThenCaps.BattleCro), "Interaction_CapsAfterItemsKeepBothInstalled_" + name);

            // The order does not matter: same module either way.
            check(capsThenItems.SequenceEqual(itemsThenCaps.BattleCro) && caps.CodeBin.SequenceEqual(itemsThenCaps.CodeBin), "Interaction_OrderDoesNotChangeTheResult_" + name);

            // Installing again in any order changes nothing, and replacing the table keeps the item patch.
            // Installing again. With spare room nothing changes. After the code segment had to grow, the installer moves
            // its routine once more (out of the grown segment into page padding) and only then reaches a fixed point:
            // the result stays valid and keeps the item patch, but the first reinstall is not a no-op. Recorded as it is.
            check(GamePersistentItemsService.Patch(capsThenItems).Changes == 0, "Interaction_ReinstallingItemsChangesNothing_" + name);
            var again = LevelCapPatch.InstallManaged(capsThenItems, caps.CodeBin, table);
            check(again.Success && again.ChangedCount == (tight ? 1 : 0) && again.BattleCro.Length == capsThenItems.Length &&
                CapsOwn(again.BattleCro, again.CodeBin, table) && ItemsOwn(again.BattleCro), "Interaction_ReinstallingCapsKeepsBothInstalled_" + name);
            var settled = LevelCapPatch.InstallManaged(again.BattleCro, again.CodeBin, table);
            check(settled.Success && settled.ChangedCount == 0 && settled.BattleCro.SequenceEqual(again.BattleCro), "Interaction_ReinstallingCapsReachesAFixedPoint_" + name);
            var replaced = LevelCapPatch.InstallManaged(capsThenItems, caps.CodeBin, LevelCapTable.Default());
            check(replaced.Success && CapsOwn(replaced.BattleCro, replaced.CodeBin, LevelCapTable.Default()) && ItemsOwn(replaced.BattleCro), "Interaction_ReplacingTheCapsTableKeepsTheItemPatch_" + name);
        }
    }
}
