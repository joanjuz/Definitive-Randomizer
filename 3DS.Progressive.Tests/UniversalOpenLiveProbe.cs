using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreeDS.Progressive;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;
using Visual = System.Windows.Media.Visual;

/// <summary>
/// LOCAL, INTERACTIVE: opens the real application in a visible window and drives the universal "Abrir proyecto" with real Windows mouse and keyboard input
/// (SendInput): click the header button, click the path box, type the path, Enter, answer the confirmation, Esc. Each path given is opened in order, so
/// passing a DS ROM, a 3DS folder and a Switch folder proves that one button opens all three. Needs an unlocked desktop; never runs in CI. Originals are
/// only read: every entry is compared (SHA-256 for files, size and time for folders) before and after. It writes only a new report folder.
/// </summary>
internal static class UniversalOpenLiveProbe
{
    private sealed record ProjectDetectionView(bool? Candidate, bool? Warning, string Platform, string Game);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public UNION u; }
    private const uint LeftDown = 0x2, LeftUp = 0x4, KeyUp = 0x2, Unicode = 0x4;

    private static void Wait(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false); Dispatcher.PushFrame(frame); Thread.Sleep(5); }
    }
    private static void Mouse(uint flags) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; SendInput(1, [i], Marshal.SizeOf<INPUT>()); }
    private static void Key(ushort vk, bool up) { var i = new INPUT { type = 1 }; i.u.ki.vk = vk; i.u.ki.flags = up ? KeyUp : 0; SendInput(1, [i], Marshal.SizeOf<INPUT>()); }
    private static void Tap(ushort vk) { Key(vk, false); Wait(40); Key(vk, true); Wait(200); }
    private static void MoveTo(Visual v, Point local) { var s = v.PointToScreen(local); SetCursorPos((int)Math.Round(s.X), (int)Math.Round(s.Y)); Wait(150); }
    private static void ClickCentre(FrameworkElement e) { MoveTo(e, new Point(e.ActualWidth / 2, e.ActualHeight / 2)); Mouse(LeftDown); Wait(60); Mouse(LeftUp); Wait(300); }
    private static void SelectAll() { Key(0x11, false); Key(0x41, false); Wait(40); Key(0x41, true); Key(0x11, true); Wait(150); }
    /// <summary>Real keystrokes, one UTF-16 unit each (Unicode key events), so any path is typed as the keyboard would.</summary>
    private static void TypeText(string text)
    {
        foreach (char c in text)
        {
            var down = new INPUT { type = 1 }; down.u.ki.scan = c; down.u.ki.flags = Unicode;
            var up = new INPUT { type = 1 }; up.u.ki.scan = c; up.u.ki.flags = Unicode | KeyUp;
            SendInput(2, [down, up], Marshal.SizeOf<INPUT>()); Thread.Sleep(8);
        }
        Wait(150);
    }
    private static FrameworkElement Find(DependencyObject root, string id)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is FrameworkElement e && AutomationProperties.GetAutomationId(e) == id) return e;
                var nested = FindOrNull(d, id); if (nested is not null) return nested;
            }
        throw new InvalidOperationException("No se encontró " + id);
    }
    private static FrameworkElement? FindOrNull(DependencyObject root, string id) { try { return Find(root, id); } catch (InvalidOperationException) { return null; } }

    private static string Snapshot(string path)
    {
        var sb = new StringBuilder();
        if (File.Exists(path)) { using var stream = File.OpenRead(path); var f = new FileInfo(path); return $"{f.Length}|{f.LastWriteTimeUtc.Ticks}|{Convert.ToHexString(SHA256.HashData(stream))}"; }
        foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            sb.Append(Path.GetRelativePath(path, entry)).Append('|').Append(File.Exists(entry) ? new FileInfo(entry).Length : -1).Append('|').Append(File.GetLastWriteTimeUtc(entry).Ticks).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    internal static void Run(string output, IReadOnlyList<string> paths)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Usa una carpeta nueva de salida."); Directory.CreateDirectory(output);
        var rows = new List<object>(); var log = new List<string>(); void Log(string s) { log.Add(s); Console.WriteLine(s); }
        var before = paths.Distinct().ToDictionary(p => p, Snapshot);
        var main = new MainWindow { Width = 1280, Height = 820, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        main.Show(); Wait(1000); SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(main).Handle); main.Activate(); Wait(400);
        void AnswerPrompts() { foreach (var prompt in Application.Current.Windows.OfType<StudioDialog>().Where(w => w.IsVisible)) { Log("  confirmación en pantalla: «" + prompt.Title + "» → Enter"); Tap(0x0D); } }
        bool failed = false;
        foreach (string path in paths)
        {
            Log($"== {path}");
            string screenBefore = main.HostDescription(); var started = System.Diagnostics.Stopwatch.StartNew();
            // Real mouse: the single header button, whatever platform is on screen. The dialog is modal: its steps run from a timer inside its own loop.
            var open = (Button)main.FindName("OpenProjectButton");
            ProjectDetectionView? shown = null; string outcome = "no abierto (no candidato)"; bool opened = false; Exception? stepFailure = null; bool finished = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var dialog = main.OpenDialog ?? throw new InvalidOperationException("el botón no abrió el diálogo");
                    SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(dialog).Handle); Wait(300);
                    // Real mouse + keyboard: click the box, select all, type, wait for the analysis.
                    ClickCentre((TextBox)Find(dialog, "OpenProjectPath")); SelectAll(); TypeText(path);
                    for (int i = 0; i < 300 && dialog.Current is null; i++) Wait(100);
                    var detected = dialog.Current;
                    shown = new(detected?.IsCandidate, detected?.HasWarning, dialog.PlatformText, dialog.GameText);
                    Log($"  detectado: {dialog.PlatformText} · {dialog.GameText} · candidato={detected?.IsCandidate} · botón Abrir={dialog.IsOpenEnabled}");
                    Log("  mensaje: " + dialog.MessageText); if (dialog.WarningText.Length > 0) Log("  advertencia: " + dialog.WarningText);
                    if (dialog.IsOpenEnabled)
                    {
                        Tap(0x0D);   // Enter: the default button
                        for (int i = 0; i < 1800 && main.OpenDialog is not null && !(dialog.LastResult is not null && !dialog.Busy); i++) { AnswerPrompts(); Wait(100); }
                        var result = dialog.LastResult; opened = result?.Opened == true; outcome = result is null ? "sin resultado" : $"{result.Outcome}: {result.Message}";
                        for (int i = 0; i < 50 && opened && main.OpenDialog is not null; i++) Wait(100);
                    }
                    if (main.OpenDialog is not null) { Tap(0x1B); for (int i = 0; i < 30 && main.OpenDialog is not null; i++) Wait(100); }   // Esc closes a dialog kept open after a rejection
                }
                catch (Exception ex) { stepFailure = ex; main.OpenDialog?.Close(); }
                finished = true;
            };
            { var sp = open.PointToScreen(new Point(open.ActualWidth / 2, open.ActualHeight / 2)); Log($"  botón: visible={open.IsVisible} habilitado={open.IsEnabled} pantalla=({sp.X:0},{sp.Y:0}) ventana activa={main.IsActive}"); }
            timer.Start(); ClickCentre(open);
            for (int i = 0; i < 3000 && !finished; i++) Wait(100);
            if (stepFailure is not null) { Log("  FALLO: " + stepFailure.Message); failed = true; rows.Add(new { path, error = stepFailure.Message }); continue; }
            var detected2 = shown;
            Wait(500);
            Log($"  resultado: {outcome} ({started.Elapsed.TotalSeconds:0.0} s)");
            string screenAfter = main.HostDescription();
            Log($"  pantalla: {screenAfter}");
            Log($"  proyectos: DS={(main.NdsWorkspace?.Project?.Game ?? "—")} · 3DS={(main.Session?.GameName ?? "—")} · Switch={(main.SwitchWorkspace?.Project?.Identity.Game ?? "—")}");
            rows.Add(new { path, platform = shown?.Platform, game = shown?.Game, candidate = shown?.Candidate, hasWarning = shown?.Warning, opened, outcome, seconds = Math.Round(started.Elapsed.TotalSeconds, 1), screenBefore, screenAfter, ds = main.NdsWorkspace?.Project?.Game, threeDs = main.Session?.GameName, @switch = main.SwitchWorkspace?.Project?.Identity.Game });
            if (!opened && shown?.Candidate == true) failed = true;
        }
        string[] unchanged = paths.Distinct().Where(p => Snapshot(p) != before[p]).ToArray();
        Log(unchanged.Length == 0 ? "Entradas originales sin cambios (SHA-256 / tamaño y hora)." : "CAMBIARON: " + string.Join(", ", unchanged));
        if (unchanged.Length > 0) failed = true;
        Log($"estado final: DS={(main.NdsWorkspace?.Project?.Game ?? "—")} · 3DS={(main.Session?.GameName ?? "—")} · Switch={(main.SwitchWorkspace?.Project?.Identity.Game ?? "—")}");
        File.WriteAllText(Path.Combine(output, "informe.json"), JsonSerializer.Serialize(new { rows, unchanged }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(output, "informe.txt"), log);
        main.Topmost = false; main.Close();
        if (failed) throw new InvalidOperationException("La sonda tuvo fallos: revisa " + Path.Combine(output, "informe.txt"));
    }
}
