using System.Buffers.Binary;
using pk3DS.Core;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using ThreeDS.Progressive.Services;

/// <summary>Synthetic records only; original commercial bytes are never fixtures.</summary>
internal static class Map3dsReminderChecks
{
    internal static byte[] SetNpc(byte[] zone, int npc, int field, int value)
    {
        byte[] copy = (byte[])zone.Clone();
        var r = new Gen6Zone(copy).Entities.Records.Single(r => r.Kind == MapEventKind.Npc && r.Index == npc);
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(r.Offset + field), (ushort)value);
        return copy;
    }

    internal static byte[][] Zones()
    {
        var zones = Enumerable.Range(0, 35).Select(_ => Map3dsGen6Checks.Zone(0, 3, [])).ToArray();
        Map3dsNurseChecks.Zones().CopyTo(zones, 0);
        byte[] source = Map3dsGen6Checks.Zone(157, 76,
            [new(9, 7, 40, Sight: 0, Angle: 0), new(6, 3, 55, Sight: 0, Angle: 0), new(14, 3, 54, Face: 2, Sight: 0, Angle: 0)]);
        source = SetNpc(SetNpc(source, 2, Gen6ZoneEdit.NpcId, 2), 2, Gen6ZoneEdit.NpcScript, Gen6MoveReminder.Script);
        var parsed = new Gen6Zone(source);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(parsed.Pack.Start(0) + 2), 108);
        zones[34] = source;
        return zones;
    }

    internal static byte[][] Models()
    {
        var models = Enumerable.Range(0, 109).Select(_ => Array.Empty<byte>()).ToArray();
        models[0] = Map3dsNurseChecks.ModelEntry(7);
        models[1] = [.. Map3dsNurseChecks.ModelEntry(7), .. Map3dsNurseChecks.ModelEntry(59), .. Map3dsNurseChecks.ModelEntry(54)];
        models[108] = Map3dsNurseChecks.ModelEntry(54);
        return models;
    }

    internal static Gen6MapBackend Backend(byte[][] zones, byte[][]? models = null, bool accredited = true,
        Gen6Title game = Gen6Title.XY, bool noModels = false)
    {
        var matrix = Map3dsGen6Checks.Matrix(2, 2, 0, 1, -1, 1);
        var block = Map3dsGen6Checks.Block(0x20);
        models ??= Models();
        return new(game, zones.Length, i => zones[i], 158, _ => matrix, 2, _ => block, null,
            modelListCount: noModels ? 0 : models.Length, modelList: noModels ? null : i => models[i], reminderAccredited: accredited);
    }

    internal static void Run(Action<bool, string> check)
    {
        Map3dsReminderProfileChecks.Run(check);
        byte[][] zones = Zones();
        var b = Backend(zones);
        var original = new Gen6Zone(zones[34]);
        check(b.Reminder is { Zone: 34, Index: 2, Model: 54 } template && template.Record.Length == 0x30
            && template.Record.AsSpan().SequenceEqual(original.Entities.Bytes(original.Entities.Records.Single(r => r.Kind == MapEventKind.Npc && r.Index == 2)))
            && b.ReminderSummary.Contains("2261"), "Reminder_TemplateComesFromZone34Npc2");

        foreach (var (field, value, name) in new[]
        {
            (Gen6ZoneEdit.NpcId, 1, "Id"), (Gen6ZoneEdit.NpcModel, 55, "Model"), (Gen6ZoneEdit.NpcScript, 2260, "DeleterScript"),
            (Gen6ZoneEdit.NpcScript, 2100, "NurseScript"), (Gen6ZoneEdit.NpcMovement, 1, "Movement"), (Gen6ZoneEdit.NpcFlag, 1, "SpawnFlag"),
            (Gen6ZoneEdit.NpcFacing, 1, "Facing"), (Gen6ZoneEdit.NpcX, 13, "X"), (Gen6ZoneEdit.NpcY, 4, "Y"), (Gen6ZoneEdit.NpcSight, 1, "Sight"),
        })
        {
            byte[][] changed = (byte[][])zones.Clone(); changed[34] = SetNpc(zones[34], 2, field, value);
            var rejected = Backend(changed);
            check(rejected.Reminder is null && !rejected.ValidateAddReminder(1, 31, 15, 180).Allowed,
                "Reminder_TemplateRejectsIncorrect" + name);
        }
        byte[][] missing = (byte[][])zones.Clone(); missing[34] = Map3dsGen6Checks.Zone(0, 76, []);
        check(Backend(missing).Reminder is null && !Backend(missing).ValidateAddReminder(1, 31, 15, 180).Allowed,
            "Reminder_MissingOriginalIsNotGuessedFromAnotherNpc");
        foreach (var (field, value, name) in new[] { (2, 109, "Area"), (4, 156, "Matrix"), (0x1C, 75, "Location") })
        {
            byte[][] changed = (byte[][])zones.Clone(); changed[34] = (byte[])zones[34].Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(changed[34].AsSpan(original.Pack.Start(0) + field), (ushort)value);
            check(Backend(changed).Reminder is null, "Reminder_TemplateRejectsIncorrect" + name);
        }
        var angled = (byte[][])zones.Clone(); angled[34] = (byte[])zones[34].Clone();
        var sourceRecord = original.Entities.Records.Single(r => r.Kind == MapEventKind.Npc && r.Index == 2);
        BinaryPrimitives.WriteSingleLittleEndian(angled[34].AsSpan(sourceRecord.Offset + 0x2C), 90);
        check(Backend(angled).Reminder is null, "Reminder_TemplateRejectsAdditionalAngle");
        check(!Backend(zones, accredited: false).CanAddReminder && Backend(zones, accredited: false).Reminder is null
            && !Backend(zones, game: Gen6Title.ORAS).CanAddReminder && Backend(zones, game: Gen6Title.ORAS).Reminder is null,
            "Reminder_UnaccreditedAndOrasVersionsCannotAdd");
        var xyGate = Gen6MoveReminderProfile.Inspect(new GameConfig(GameVersion.XY));
        check(!xyGate.Allowed && xyGate.Title == "XY" && xyGate.Dependencies.Count == 0
            && xyGate.Reason.Contains("identidad"),
            "Reminder_ProductionGateRejectsMissingProjectIdentity");
        foreach (var game in new[] { GameVersion.ORAS, GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM })
        {
            var denied = Gen6MoveReminderProfile.Inspect(new GameConfig(game));
            check(!denied.Allowed && denied.Dependencies.Count == 0 && denied.Reason.Contains("exclusivamente"),
                "Reminder_ProductionGateRejects" + game);
        }

        check(b.ValidateAddReminder(1, 31, 15, 180) is { Allowed: true } free && free.Reason.Contains("NPC 3")
            && free.Reason.Contains("modelo 54") && free.Reason.Contains("acceder al NPC"), "Reminder_FreeCellExplainsAdditionAndModel");
        check(!b.ValidateAddReminder(35, 31, 15, 180).Allowed && !b.ValidateAddReminder(-1, 31, 15, 180).Allowed,
            "Reminder_MissingAndNegativeZonesRejected");
        check(!b.ValidateAddReminder(1, -1, 15, 180).Allowed && !b.ValidateAddReminder(1, 80, 15, 180).Allowed
            && !b.ValidateAddReminder(1, 5, 50, 180).Allowed && !b.ValidateAddReminder(1, 31, 15, 45).Allowed,
            "Reminder_OutsideMissingBlockAndUnsupportedFacingRejected");
        foreach (var (x, y, kind) in new[] { (45, 12, "Npc"), (45, 10, "Prop"), (50, 20, "Warp"), (42, 30, "Trigger") })
            check(b.ValidateAddReminder(1, x, y, 180) is { Allowed: false } occupied && occupied.Reason.Contains("ocupa"),
                "Reminder_CollisionWith" + kind + "Rejected");
        check(b.ValidateAddReminder(2, 31, 15, 180) is { Allowed: false } full && full.Reason.Contains("máximo")
            && !b.ValidateAddReminder(3, 31, 15, 180).Allowed, "Reminder_NpcCeilingAndUnverifiedCoordinatesRejected");

        var loadedModels = Models(); loadedModels[0] = [.. loadedModels[0], .. Map3dsNurseChecks.ModelEntry(54)];
        var loaded = Backend(zones, loadedModels);
        check(loaded.AreaLoadsReminder(1) == true && loaded.ValidateAddReminder(1, 31, 15, 180) is { Allowed: true } present
            && !present.Reason.Contains("Se añadirá"), "Reminder_ExistingModelIsReused");
        var modelEntry = b.ReminderModelEntry!;
        var appendedModel = Gen6AreaModels.Add(b.ModelList(0)!, modelEntry);
        check(b.AreaLoadsReminder(1) == false && Gen6AreaModels.Models(appendedModel).SequenceEqual([7, 54])
            && appendedModel.AsSpan(0, 24).SequenceEqual(b.ModelList(0)) && appendedModel.AsSpan(24).SequenceEqual(modelEntry),
            "Reminder_MissingModelAppendsExactEntryPreservingOthers");
        var ambiguous = Models(); ambiguous[2] = Map3dsNurseChecks.ModelEntry(54, flag: 0);
        check(Backend(zones, ambiguous).ReminderModelEntry is null && !Backend(zones, ambiguous).ValidateAddReminder(1, 31, 15, 180).Allowed,
            "Reminder_AmbiguousModelRejected");
        var absent = Models(); absent[1] = Map3dsNurseChecks.ModelEntry(59); absent[108] = [];
        check(!Backend(zones, absent).ValidateAddReminder(1, 31, 15, 180).Allowed && !Backend(zones, noModels: true).ValidateAddReminder(1, 31, 15, 180).Allowed,
            "Reminder_AbsentAndUnreadableModelRejected");
        var fullModels = Models(); fullModels[0] = [.. Map3dsNurseChecks.ModelEntry(7), .. Map3dsNurseChecks.ModelEntry(8), .. Map3dsNurseChecks.ModelEntry(9)];
        check(Backend(zones, fullModels).ValidateAddReminder(1, 31, 15, 180) is { Allowed: false } tooMany && tooMany.Reason.Contains("máximo"),
            "Reminder_ModelCeilingRejected");
        var malformed = Models(); malformed[2] = new byte[25];
        check(!Backend(zones, malformed).ValidateAddReminder(1, 31, 15, 180).Allowed, "Reminder_MalformedModelListRejected");

        var snapshots = zones.Select(z => (byte[])z.Clone()).ToArray();
        var preview = b.ValidateAddReminder(1, 31, 15, 180);
        byte[] added = Gen6ZoneEdit.AddNpc(b.ZoneBytes(1), b.Reminder!.Record, 31, 15, 1);
        check(preview.Allowed && zones.Select((z, i) => z.AsSpan().SequenceEqual(snapshots[i])).All(v => v),
            "Reminder_PlanningAndAppendNeverMutateSources");
        var before = b.Zone(1); var after = new Gen6Zone(added);
        check(before.Entities.Records.All(r => before.Entities.Bytes(r).SequenceEqual(after.Entities.Bytes(after.Entities.Records.Single(n => n.Kind == r.Kind && n.Index == r.Index))))
            && Enumerable.Range(0, before.Pack.Count).Where(i => i != 1).All(i => before.Pack.Part(i).SequenceEqual(after.Pack.Part(i))),
            "Reminder_AppendPreservesOtherRecordsAndParts");
        int oldScript = before.Entities.Records.Max(r => r.Offset + r.Size);
        int newScript = after.Entities.Records.Max(r => r.Offset + r.Size);
        check(before.Pack.Raw.AsSpan(oldScript, before.Pack.Start(2) - oldScript)
            .SequenceEqual(after.Pack.Raw.AsSpan(newScript, after.Pack.Start(2) - newScript)),
            "Reminder_AppendPreservesEntityScriptByteForByte");
        var addedRecord = after.Entities.Records.Last(r => r.Kind == MapEventKind.Npc);
        check(after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcScript) == 2261 && after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcModel) == 54
            && after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcId) == 3 && after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcX) == 31
            && after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcY) == 15 && after.Entities.U16(addedRecord, Gen6ZoneEdit.NpcFacing) == 1,
            "Reminder_AppendHasAuthenticScriptModelAndRequestedPlacement");
        var appendedZones = (byte[][])zones.Clone(); appendedZones[1] = added;
        var reopened = Backend(appendedZones, loadedModels);
        check(reopened.ZoneEvents(1).Last(r => r.Kind == MapEventKind.Npc) is { Index: 3, X: 31, Y: 15, Placed: true } shown
            && shown.Properties.Any(p => p.Name == "Script" && p.Value == "2261") && reopened.Reminder is not null,
            "Reminder_ReopenedBackendRecognizesAddedNpcAndOriginalTemplate");

        // A nurse remains independently selectable and both records survive one another.
        byte[] both = Gen6ZoneEdit.AddNpc(added, b.Nurse!.Record, 32, 15, 2);
        var bothZone = new Gen6Zone(both);
        var npcs = bothZone.Entities.Records.Where(r => r.Kind == MapEventKind.Npc).ToArray();
        check(reopened.ValidateAddNurse(1, 32, 15, 270).Allowed && npcs.Length == 5 && bothZone.Entities.U16(npcs[3], Gen6ZoneEdit.NpcScript) == 2261
            && bothZone.Entities.U16(npcs[4], Gen6ZoneEdit.NpcScript) == 2100
            && bothZone.Entities.Bytes(npcs[3]).SequenceEqual(after.Entities.Bytes(addedRecord)),
            "Reminder_NurseAndReminderCoexistWithoutChangingOneAnother");
        var fullZone = (byte[][])appendedZones.Clone(); fullZone[1] = both;
        check(!Backend(fullZone, loadedModels).ValidateAddReminder(1, 33, 15, 180).Allowed,
            "Reminder_NpcLimitAllowsLastSlotAndRejectsNextAddition");
        var noNurse = (byte[][])zones.Clone(); noNurse[0] = SetNpc(noNurse[0], 0, Gen6ZoneEdit.NpcScript, 2000);
        var independent = Backend(noNurse);
        check(independent.Nurse is null && independent.ModelListCeiling == 3 && independent.ValidateAddReminder(1, 31, 15, 180).Allowed,
            "Reminder_ModelCapacityDoesNotDependOnFindingANurse");
        var clone = b.Reminder!.Record; clone[0] ^= 1;
        check(b.Reminder!.Record[0] == 2, "Reminder_CallerCannotMutateCachedTemplate");
    }
}
