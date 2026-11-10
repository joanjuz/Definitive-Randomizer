using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using pk3DS.Core.NDS;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services.Nds;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;
using Visual = System.Windows.Media.Visual;

/// <summary>
/// LOCAL, INTERACTIVE diagnosis: hosts the real event editor in a visible window on a COPY of a private ROM and drives it with real Windows
/// mouse and keyboard input (SendInput). Needs an unlocked desktop; never runs in CI. Writes only to a new output folder.
/// </summary>
internal static class NdsLiveUiProbe
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public UNION u; }
    private const uint LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, KeyUp = 0x2;

    private static void Wait(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false); Dispatcher.PushFrame(frame); Thread.Sleep(10); }
    }
    private static void Mouse(uint flags) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; SendInput(1, [i], Marshal.SizeOf<INPUT>()); }
    private static void Key(ushort vk, bool up) { var i = new INPUT { type = 1 }; i.u.ki.vk = vk; i.u.ki.flags = up ? KeyUp : 0; SendInput(1, [i], Marshal.SizeOf<INPUT>()); }
    private static void MoveTo(Visual v, Point local) { var s = v.PointToScreen(local); SetCursorPos((int)Math.Round(s.X), (int)Math.Round(s.Y)); Wait(120); }
    private static void Click(Visual v, Point local, bool right = false) { MoveTo(v, local); Mouse(right ? RightDown : LeftDown); Wait(60); Mouse(right ? RightUp : LeftUp); Wait(250); }
    private static void ClickCentre(FrameworkElement e) => Click(e, new Point(e.ActualWidth / 2, e.ActualHeight / 2));
    private static void Chord(ushort vk) { Key(0x11, false); Key(vk, false); Wait(40); Key(vk, true); Key(0x11, true); Wait(250); }
    private static void Type(string text) { foreach (char c in text) { ushort vk = char.IsDigit(c) ? c : char.ToUpperInvariant(c); bool shift = char.IsUpper(c); if (shift) Key(0x10, false); Key(vk, false); Key(vk, true); if (shift) Key(0x10, true); Wait(30); } Wait(300); }
    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject => NdsMapCanvasChecks.Children<T>(root).Single(e => AutomationProperties.GetName(e) == name);

    internal static void Run(string romPath, string output, int width, int height)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        string copy = Path.Combine(output, "Platino-copia.nds"); File.Copy(romPath, copy);
        var log = new List<string>(); void Log(string s) { log.Add(s); Console.WriteLine(s); }
        using var rom = NdsRom.Open(copy); var data = PlatinumEvents.Open(rom); var terrain = PlatinumTerrain.Open(rom);
        var session = new NdsEventSession(rom, data); var view = new NdsEventEditorView(session, terrain, null);
        var window = new Window { Content = view, Width = width, Height = height, Title = "live probe", WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        window.Show(); Wait(800); SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle); window.Activate(); Wait(300);
        var canvas = NdsMapCanvasChecks.Children<NdsMapCanvasControl>(view).Single(); var maps = NdsMapCanvasChecks.Children<ListBox>(view).Single();
        void Shot(string name) { Wait(200); var b = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); b.Render(view); var e = new System.Windows.Media.Imaging.PngBitmapEncoder(); e.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(b)); using var f = File.Create(Path.Combine(output, name + ".png")); e.Save(f); }
        bool OnScreen(FrameworkElement e) { if (!e.IsVisible || e.ActualHeight <= 0) return false; var p = e.TranslatePoint(new Point(0, e.ActualHeight), view); var q = e.TranslatePoint(new Point(0, 0), view); return q.Y >= 0 && p.Y <= view.ActualHeight + 1; }
        Log($"window {width}x{height}, view {view.ActualWidth:0}x{view.ActualHeight:0}, canvas {canvas.ActualWidth:0}x{canvas.ActualHeight:0}");

        // 1. find Twinleaf by typing in the real search box
        var search = Find<TextBox>(view, "Eventos DS · buscar mapa por nombre o ID"); ClickCentre(search); Type("T01");
        Log($"search typed: '{search.Text}', maps listed {maps.Items.Count}, selected {(maps.SelectedItem as PlatinumMap)?.Display}");
        if ((maps.SelectedItem as PlatinumMap)?.Id != 411) { maps.SelectedItem = maps.Items.Cast<PlatinumMap>().First(m => m.Id == 411); Log("fallback: selected 411 programmatically"); }
        Wait(400); var m = canvas.Model!;
        Point Tile(double x, double y) { var p = m.TileToPixel(x, y); return new Point(p.X, p.Y); }
        // centre the view on NPC 0 at a usable zoom with real wheel-free calls (view setup only)
        m.SetZoom(14, m.ViewportWidth / 2, m.ViewportHeight / 2); var c = m.TileToPixel(119.5, 878.5); m.PanBy(c.X - m.ViewportWidth / 2, c.Y - m.ViewportHeight / 2); canvas.Invalidate(); Wait(300);
        Shot("01-twinleaf");

        // 2. real click on NPC 0
        Click(canvas, Tile(119.5, 878.5));
        Log($"click NPC0 -> canvas selected {m.Selected}, inspector X '{Find<TextBox>(view, "Eventos DS · Coordenada X").Text}'");
        var dup = Find<Button>(view, "Eventos DS · duplicar NPC"); var add = Find<Button>(view, "Eventos DS · añadir NPC"); var copyB = Find<Button>(view, "Eventos DS · copiar NPC"); var paste = Find<Button>(view, "Eventos DS · pegar NPC");
        Log($"buttons: add={add.IsEnabled} dup={dup.IsEnabled} copy={copyB.IsEnabled} paste={paste.IsEnabled}; dup on screen={OnScreen(dup)}");

        // 3. Duplicate with real clicks: ghost, click, pending NPC on the map
        ClickCentre(dup); MoveTo(canvas, Tile(120.5, 878.5)); Log($"Duplicar -> placing={canvas.Placing}, ghost {canvas.PlacementState}");
        Click(canvas, Tile(120.5, 878.5));
        var diff = Find<TextBox>(view, "Eventos DS · diferencias previstas"); var save = Find<Button>(view, "Eventos DS · Guardar cambios…"); var form = Find<Border>(view, "Eventos DS · NPC nuevo");
        Log($"after click: form visible={form.IsVisible}, pending on map={canvas.PendingAddition}, save enabled={save.IsEnabled} on screen={OnScreen(save)}, diff='{diff.Text.Replace("\n", " | ")}'");
        Shot("02-duplicate-pending");
        string cp = Path.Combine(output, "dup.ndsevents.json"); var t = view.SaveToAsync(cp); while (!t.IsCompleted) Wait(50); m = canvas.Model!;
        Log($"saved: revision {session.Revision}, NPC count {session.Data.NpcCount(411)}, new NPC {session.Data.Npc(411, 8)}");

        // 4. Real right-click on NPC 1 opens the menu with it selected; «Pegar aquí» at another tile
        Click(canvas, Tile(119.5, 878.5)); Chord(0x43); Log($"Ctrl+C on NPC0 -> paste enabled={paste.IsEnabled}");
        var npc1 = m.Find(PlatinumEventKind.Npc, 4)!; Click(canvas, Tile(npc1.CenterX, npc1.CenterY), right: true); Wait(400);
        Log($"right-click NPC1 -> selected {m.Selected}, menu open={canvas.ContextMenu?.IsOpen}");
        if (canvas.ContextMenu?.IsOpen == true) { Key(0x1B, false); Key(0x1B, true); Wait(300); }
        Click(canvas, Tile(117.5, 880.5), right: true); Wait(400);
        var here = canvas.ContextMenu!.Items.OfType<MenuItem>().First(i => AutomationProperties.GetName(i) == "Eventos DS · menú · pegar NPC aquí");
        Log($"right-click empty tile -> menu open={canvas.ContextMenu.IsOpen}, 'Pegar aquí' enabled={here.IsEnabled} visible={here.IsVisible}");
        if (here.IsVisible) ClickCentre(here); else { Key(0x1B, false); Key(0x1B, true); here.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Log("menu item clicked programmatically"); }
        Wait(300); Log($"after 'Pegar aquí': pending={canvas.PendingAddition}, diff='{diff.Text.Replace("\n", " | ")}'");
        Shot("03-paste-here");
        var t2 = view.SaveToAsync(Path.Combine(output, "paste.ndsevents.json")); while (!t2.IsCompleted) Wait(50); m = canvas.Model!;
        Log($"saved paste: count {session.Data.NpcCount(411)}");

        // 5. Copy from another map into Twinleaf (basic copy)
        var other = session.Data.Maps.First(x => x.Id != 411 && x.EventFile != 390 && session.Data.Events(x.Id).Any(e => e.Kind == PlatinumEventKind.Npc && e.Type == 0 && e.Graphics != 12));
        search.Text = ""; Wait(200); maps.SelectedItem = maps.Items.Cast<PlatinumMap>().First(x => x.Id == other.Id); Wait(500);
        var on = canvas.Model!; var src = on.Markers.First(k => k.Kind == PlatinumEventKind.Npc && k.Event.Type == 0 && k.Event.Graphics != 12); on.SetZoom(14, on.ViewportWidth / 2, on.ViewportHeight / 2); var cc = on.TileToPixel(src.CenterX, src.CenterY); on.PanBy(cc.X - on.ViewportWidth / 2, cc.Y - on.ViewportHeight / 2); canvas.Invalidate(); Wait(300);
        var p1 = on.TileToPixel(src.CenterX, src.CenterY); Click(canvas, new Point(p1.X, p1.Y)); Chord(0x43);
        Log($"copied map {other.Id} ({other.InternalName}) NPC {src.Index} gfx {src.Event.Graphics}");
        maps.SelectedItem = maps.Items.Cast<PlatinumMap>().First(x => x.Id == 411); Wait(500); m = canvas.Model!;
        m.SetZoom(14, m.ViewportWidth / 2, m.ViewportHeight / 2); c = m.TileToPixel(115.5, 876.5); m.PanBy(c.X - m.ViewportWidth / 2, c.Y - m.ViewportHeight / 2); canvas.Invalidate(); Wait(300);
        ClickCentre(paste); MoveTo(canvas, Tile(115.5, 876.5)); Log($"cross-map ghost {canvas.PlacementState}"); Click(canvas, Tile(115.5, 876.5));
        var previewNew = Find<Button>(view, "Eventos DS · previsualizar propuesta de NPC nuevo");
        Log($"cross-map form visible={form.IsVisible} on screen={OnScreen(form)}, preview on screen={OnScreen(previewNew)} enabled={previewNew.IsEnabled}");
        Log("form: " + Find<TextBlock>(view, "Eventos DS · validación del NPC nuevo").Text.Replace("\n", " | "));
        Shot("04-cross-map-form");
        if (!OnScreen(previewNew)) previewNew.BringIntoView(); Wait(300); ClickCentre(previewNew);
        Log($"cross-map preview: pending={canvas.PendingAddition}, canvas {canvas.ActualWidth:0}x{canvas.ActualHeight:0}");
        var t3 = view.SaveToAsync(Path.Combine(output, "cross.ndsevents.json")); while (!t3.IsCompleted) Wait(50); m = canvas.Model!;
        var cross = session.Data.Npc(411, session.Data.NpcCount(411) - 1); Log($"saved cross-map NPC: {cross}");

        // 6. Delete an added NPC that is not the last one
        var firstAdded = m.Find(PlatinumEventKind.Npc, 8)!; Click(canvas, Tile(firstAdded.CenterX, firstAdded.CenterY));
        var del = Find<Button>(view, "Eventos DS · eliminar NPC"); Log($"select added NPC 8 -> delete button '{del.Content}' enabled={del.IsEnabled}");
        Click(canvas, Tile(firstAdded.CenterX, firstAdded.CenterY)); Key(0x2E, false); Key(0x2E, true); Wait(300);
        Log($"Supr -> diff='{diff.Text.Replace("\n", " | ")}'");
        var t4 = view.SaveToAsync(Path.Combine(output, "delete.ndsevents.json")); while (!t4.IsCompleted) Wait(50);
        Log($"after delete: count {session.Data.NpcCount(411)}");
        Shot("05-after-delete");

        // 7. Export through the real NDS writer and reopen
        string built = Path.Combine(output, "Platino-editado.nds"); rom.Save(built, new Dictionary<int, byte[]> { [rom.File(PlatinumEvents.EventPath).Id] = session.Replacement()! }); session.VerifyExport(built);
        using (var rebuilt = NdsRom.Open(built)) Log($"exported: other files identical={rom.Files.Where(x => x.Path != PlatinumEvents.EventPath).All(x => rom.ReadFile(x).AsSpan().SequenceEqual(rebuilt.ReadFile(x.Path)))}");
        // 8. Restore all
        while (session.CanUndo) { Find<Button>(view, "Eventos DS · Restaurar último cambio…").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); var tu = view.SaveToAsync(Path.Combine(output, $"undo-{session.Revision}.ndsevents.json")); while (!tu.IsCompleted) Wait(50); }
        Log($"restored all: events equal original={session.Data.Bytes().AsSpan().SequenceEqual(data.Bytes())}");
        // 9. Nurse Joy with behaviour, with real input: copy in Jubilife PC, paste into Twinleaf, keep the exported ROM for the emulator test
        search.Text = ""; Wait(200); ClickCentre(search); Type("C01PC0101"); Wait(400);
        var pcMap = maps.Items.Cast<PlatinumMap>().FirstOrDefault(x => x.InternalName == "C01PC0101"); if (pcMap is not null && (maps.SelectedItem as PlatinumMap)?.Id != pcMap.Id) maps.SelectedItem = pcMap; Wait(400);
        var pm = canvas.Model!; var joy = pm.Markers.First(k => k.Kind == PlatinumEventKind.Npc && session.Data.ClassifyScript(pcMap!.Id, k.Event.Script)?.Pattern == PlatinumScriptPattern.PokecenterNurse);
        pm.SetZoom(14, pm.ViewportWidth / 2, pm.ViewportHeight / 2); var jc = pm.TileToPixel(joy.CenterX, joy.CenterY); pm.PanBy(jc.X - pm.ViewportWidth / 2, jc.Y - pm.ViewportHeight / 2); canvas.Invalidate(); Wait(300);
        jc = pm.TileToPixel(joy.CenterX, joy.CenterY); Click(canvas, new Point(jc.X, jc.Y)); Chord(0x43);
        Log($"nurse selected {pm.Selected}, inspector: " + Find<TextBox>(view, "Eventos DS · propiedades y referencias (copiable)").Text.Split('\n').FirstOrDefault(l => l.StartsWith("Comportamiento")));
        search.Text = ""; Wait(200); ClickCentre(search); Type("T01"); Wait(400); if ((maps.SelectedItem as PlatinumMap)?.Id != 411) maps.SelectedItem = maps.Items.Cast<PlatinumMap>().First(x => x.Id == 411); Wait(400);
        m = canvas.Model!; m.SetZoom(14, m.ViewportWidth / 2, m.ViewportHeight / 2); c = m.TileToPixel(115.5, 876.5); m.PanBy(c.X - m.ViewportWidth / 2, c.Y - m.ViewportHeight / 2); canvas.Invalidate(); Wait(300);
        ClickCentre(paste); MoveTo(canvas, Tile(115.5, 876.5)); Click(canvas, Tile(115.5, 876.5));
        var modeBox = Find<System.Windows.Controls.ComboBox>(view, "Eventos DS · nuevo NPC · comportamiento");
        Log($"nurse form visible={form.IsVisible}, mode='{(modeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content}', preview on screen={OnScreen(previewNew)}");
        Shot("06-nurse-form");
        ClickCentre(previewNew); Log($"nurse diff='{diff.Text.Replace("\n", " | ")}'");
        var tn = view.SaveToAsync(Path.Combine(output, "nurse.ndsevents.json")); while (!tn.IsCompleted) Wait(50); m = canvas.Model!;
        string nurseRom = Path.Combine(output, "Platino-enfermera-Hojaverde.nds");
        var replacements = new Dictionary<int, byte[]> { [rom.File(PlatinumEvents.EventPath).Id] = session.Replacement()! }; if (session.ScriptReplacement() is { } sr) replacements[rom.File(PlatinumEvents.ScriptPath).Id] = sr;
        rom.Save(nurseRom, replacements); session.VerifyExport(nurseRom);
        var placed = session.Data.Npc(411, session.Data.NpcCount(411) - 1);
        Log($"nurse saved and exported: NPC {placed.Index} id {placed.Id} at ({placed.X},{placed.Y}) script {placed.Script} -> {session.Data.ClassifyScript(411, placed.Script)?.Describe()}; ROM {nurseRom}");
        Shot("07-nurse-saved");
        Log($"FINAL: session revision {session.Revision}, source untouched={File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(romPath))}");
        File.WriteAllLines(Path.Combine(output, "live-log.txt"), log);
        window.Close();
    }
}
