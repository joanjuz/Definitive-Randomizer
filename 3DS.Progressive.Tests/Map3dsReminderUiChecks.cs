using System.IO;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen6;
using pk3DS.Core.Maps.Gen7;
using ThreeDS.Progressive;

/// <summary>Synthetic WPF placement of the accredited X/Y reminder; delegates stand in for persistence.</summary>
internal static class Map3dsReminderUiChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var zones = Map3dsReminderChecks.Zones();
        var calls = new List<(int Zone, int X, int Y, double Facing)>();
        int reloads = 0, nurseCalls = 0;
        bool reject = false;
        var reloaded = Map3dsReminderChecks.Backend(zones);
        var mover = new Map3dsMover((b, z, n, x, y) => "Mover", () => { reloads++; return reloaded; },
            addNurse: (b, z, x, y, f) => { nurseCalls++; return "Enfermera"; },
            addReminder: (b, z, x, y, f) =>
            {
                if (reject) throw new InvalidDataException("El recurso cambió desde la vista previa.");
                calls.Add((z, x, y, f));
                var g = (Gen6MapBackend)b;
                var next = (byte[][])zones.Clone();
                next[z] = Gen6ZoneEdit.AddNpc(g.ZoneBytes(z), g.Reminder!.Record, x, y, Gen6MapBackend.FacingValue(f)!.Value);
                reloaded = Map3dsReminderChecks.Backend(next);
                return "Añadir recordador";
            });
        var view = new Map3dsEditorView(Map3dsReminderChecks.Backend(zones), mover);
        Layout(view);
        view.ShowZone(1);
        check(view.ReminderButton is { Content: "Añadir recordador" } && view.NurseButton is not null,
            "ReminderUi_AccreditedXyShowsReminderBesideNurse");
        var unaccredited = new Map3dsEditorView(Map3dsReminderChecks.Backend(zones, accredited: false), mover);
        check(new Map3dsEditorView(Map3dsReminderChecks.Backend(zones)).ReminderButton is null
            && unaccredited.ReminderButton is null && !unaccredited.ShowReminderPanel(),
            "ReminderUi_NoWriterOrAccreditationHidesButton");
        var oras = new Map3dsEditorView(Map3dsReminderChecks.Backend(zones, game: Gen6Title.ORAS), mover);
        check(oras.ReminderButton is null && !oras.ShowReminderPanel(), "ReminderUi_OrasCannotOpenReminderPanel");
        check(new[] { "Sol", "Luna", "Ultra Sol", "Ultra Luna" }.All(title =>
        {
            var gen7 = new Map3dsEditorView(new Gen7MapBackend(title, 0, _ => [], 0, _ => []), mover);
            return gen7.ReminderButton is null && !gen7.ShowReminderPanel();
        }), "ReminderUi_Gen7NeverOffersReminder");
        check(view.ShowReminderPanel() && view.Canvas.PickingCell && view.ReminderX is { Text: "" }
            && view.ReminderDirection is { SelectedValue: 180.0 } && view.ReminderSave is { IsEnabled: false }
            && view.Details.Children.OfType<TextBlock>().Any(t => t.Text.Contains("2261") && t.Text.Contains("54")),
            "ReminderUi_PanelIdentifiesOriginalNpcModelAndScript");
        view.Canvas.PickCell(45, 12);
        check(view.ReminderX!.Text == "45" && view.PendingReminder is null && view.ReminderSave!.IsEnabled == false
            && view.ReminderResult!.Text.Contains("ocupa") && view.Canvas.PendingReminder is null,
            "ReminderUi_CollisionRejectsPreviewAndSave");
        view.Canvas.PickCell(31, 15);
        check(view.PendingReminder == (31, 15, 180.0) && view.Canvas.PendingReminder == (31, 15, 180.0)
            && view.Canvas.PendingNurse is null && view.ReminderSave!.IsEnabled && calls.Count == 0 && nurseCalls == 0,
            "ReminderUi_ClickPreviewsDistinctReminderWithoutWriting");
        view.ReminderDirection!.SelectedValue = 90.0;
        UiPump.Idle();
        check(view.PendingReminder == (31, 15, 90.0) && view.Canvas.PendingReminder is { Facing: 90 },
            "ReminderUi_DirectionUpdatesReminderPreview");
        view.ReminderX!.Text = "32";
        check(view.PendingReminder is null && view.Canvas.PendingReminder is null && !view.ReminderSave!.IsEnabled && !view.SaveReminder(),
            "ReminderUi_CoordinateChangeInvalidatesOldPreview");
        view.ReminderX.Text = "texto";
        check(!view.PreviewReminder() && view.PendingReminder is null && view.ReminderResult!.Text.Contains("enteros") && !view.ReminderSave!.IsEnabled,
            "ReminderUi_InvalidManualCoordinateCannotSave");
        view.ReminderX.Text = "31"; view.ReminderY!.Text = "15";
        check(view.PreviewReminder() && view.PendingReminder == (31, 15, 90.0) && !view.SaveNurse() && nurseCalls == 0,
            "ReminderUi_ManualCoordinatesUseIndependentReminderAction");
        reject = true;
        check(!view.SaveReminder() && calls.Count == 0 && reloads == 0 && view.ReminderResult!.Text.Contains("No se guardó")
            && view.ReminderResult.Text.Contains("cambió"), "ReminderUi_ChangedResourcesReportFailureWithoutReload");
        reject = false;
        check(view.SaveReminder() && calls.SequenceEqual([(1, 31, 15, 90.0)]) && nurseCalls == 0 && reloads == 1
            && view.Status.Text.Contains("historial") && !view.Canvas.PickingCell && view.Canvas.PendingReminder is null
            && view.Canvas.Selected is { Kind: MapEventKind.Npc, Index: 3, X: 31, Y: 15 },
            "ReminderUi_SaveDelegatesReloadsAndSelectsAddedNpc");
        view.ShowReminderPanel();
        view.Canvas.PickCell(32, 15);
        check(view.ShowNursePanel() && view.PendingReminder is null && view.ReminderX is null && view.Canvas.PendingReminder is null
            && view.NurseX is { Text: "" } && view.NurseSave is { IsEnabled: false },
            "ReminderUi_NursePanelClearsReminderState");
        view.ShowReminderPanel();
        view.EventList.SelectedIndex = 0;
        UiPump.Idle();
        check(!view.Canvas.PickingCell && view.ReminderX is null && view.Canvas.PendingReminder is null,
            "ReminderUi_SelectingEventExitsReminderPanel");
    }

    private static void Layout(FrameworkElement content)
    {
        content.Measure(new Size(1400, 860)); content.Arrange(new Rect(0, 0, 1400, 860));
        content.UpdateLayout(); UiPump.Idle(); content.UpdateLayout();
    }
}
