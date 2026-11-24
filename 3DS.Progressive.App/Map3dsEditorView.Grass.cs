using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.Maps;
using pk3DS.Core.Maps.Gen7;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace ThreeDS.Progressive;

/// <summary>
/// «Añadir hierba visible» (Ultra Sun / Ultra Moon): copies a vanilla grass patch onto the map through
/// <see cref="Map3dsMover.AddGrass"/>. Visible grass is scenery only (part 1 of a map block); encounters come from the
/// catch zone, a separate operation, and the panel says so.
/// </summary>
internal sealed partial class Map3dsEditorView
{
    internal const string GrassScopeNotice = "La hierba visible solo cambia el escenario (el modelo de hierba del bloque): no crea encuentros. Los encuentros los crea la «Zona de captura», que es otra operación.";
    internal const string GrassGameplayNotice = "Prueba jugable aprobada en Pueblo Lilii (Ultra Luna); otros sitios no se han probado en el juego.";
    internal const string GrassLegend = "Vista previa: azul discontinuo, el bloque que cambia; rectángulo ámbar, la superficie que ocupará la hierba (rojo si no es válida); " +
        "dentro, el modelo de hierba real visto desde arriba con las texturas del área (aproximado: sin iluminación ni animación); " +
        "borde punteado verde, casillas con suelo admitido para una zona de captura; punteado turquesa, hierba visible que ya existe.";

    private Button? _grassButton, _grassSave, _grassUndo;
    private ComboBox? _grassTemplate;
    private TextBox? _grassX, _grassY;
    private TextBlock? _grassResult;
    private bool _grassActive;
    private (Gen7GrassPlan Plan, (int X, int Y) Centre)? _pendingGrass;

    internal Button? GrassButton => _grassButton;
    internal ComboBox? GrassTemplate => _grassTemplate;
    internal TextBlock? GrassResult => _grassResult;
    internal Button? GrassSave => _grassSave;
    internal Button? GrassUndo => _grassUndo;
    internal Gen7GrassPlan? PendingGrass => _pendingGrass?.Plan;

    private void AddGrassButton(System.Windows.Controls.Panel bar, IMapEditorBackend backend)
    {
        if (_mover is not { CanAddGrass: true } || backend is not Gen7MapBackend) return;
        _grassButton = Small("Añadir hierba visible", () => ShowGrassPanel(), "Copiar una mancha de hierba alta del propio juego en el mapa (solo escenario: no crea encuentros)");
        _grassButton.Margin = new Thickness(12, 0, 6, 0);
        bar.Children.Add(_grassButton);
    }

    private void ResetGrass()
    {
        _grassActive = false; _pendingGrass = null; _grassTemplate = null; _grassX = _grassY = null; _grassSave = _grassUndo = null; _grassResult = null;
        _canvas.SetGrass(null);
    }

    /// <summary>
    /// Visible grass and encounters of an area, side by side: cells with visible grass, how many of them produce
    /// encounters, and encounter cells without visible grass (sand, water and caves have none in the game either).
    /// </summary>
    internal static string GrassEncounterSummary(Gen7MapBackend backend, int zone)
    {
        var visible = backend.VisibleGrassCells(zone);
        var encounters = backend.EncounterCells(zone).ToHashSet();
        // Grass on terrain the area loads from a neighbour (same block, or the same ground with encounters there) belongs
        // to that area in the game: its encounters, if any, come from it.
        var own = visible.Where(c => backend.OtherOwner(zone, c) is null).ToArray();
        int neighbour = visible.Count - own.Length, both = own.Count(encounters.Contains);
        string text = visible.Count == 0 ? "Esta área no tiene hierba visible."
            : own.Length == 0 ? $"Hierba visible: {visible.Count} casillas, todas en terreno de áreas vecinas que esta área carga como escenario."
            : $"Hierba visible propia (aproximada por la caja de cada mancha): {own.Length} casillas, {both} con encuentros y {own.Length - both} sin encuentros."
              + (neighbour > 0 ? $" Otras {neighbour} están en terreno de áreas vecinas (sus encuentros dependen de esas áreas)." : "");
        var visibleSet = visible.ToHashSet();
        int bare = encounters.Count(c => !visibleSet.Contains(c));
        if (bare > 0) text += $" {bare} casillas con encuentros no tienen hierba visible (la arena, el agua y las cuevas tampoco la tienen en el juego).";
        return text;
    }

    /// <summary>Panel to place visible grass: template, centre (typed or clicked), preview, save and undo.</summary>
    internal bool ShowGrassPanel()
    {
        if (_mover is not { CanAddGrass: true } || _backend is not Gen7MapBackend grass || CurrentZone is not int zone) return false;
        _canvas.Select(null, false); SyncList(null);
        ShowDetails(null);
        _details.Children.Clear();
        _grassActive = true;
        _details.Children.Add(new TextBlock { Text = "Añadir hierba visible", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Res("TextMain") });
        _details.Children.Add(Note("Copia una mancha de hierba alta del propio juego en este mapa. " + GrassScopeNotice));
        string summary;
        try { summary = GrassEncounterSummary(grass, zone); }
        catch (Exception ex) when (ex is MapFormatException or InvalidOperationException) { summary = "No se pudo leer la hierba visible del área: " + ex.Message; }
        _details.Children.Add(Note(summary, main: true));

        var choices = grass.GrassChoices(zone);
        if (choices.Count == 0)
        {
            _details.Children.Add(Note("Esta área no tiene las texturas de ninguna hierba del juego. Esta versión no añade texturas, así que aquí no se puede añadir hierba visible.", main: true, bold: true));
        }
        else
        {
            _details.Children.Add(Note("Plantilla (manchas originales del juego cuyas texturas tiene esta área):"));
            _grassTemplate = new ComboBox { Margin = new Thickness(0, 4, 0, 0), DisplayMemberPath = "Name", SelectedValuePath = "Block",
                ItemsSource = choices.Select(c => new GrassChoice(c.Name, c.Template.Block)).ToArray(), SelectedIndex = 0 };
            System.Windows.Automation.AutomationProperties.SetName(_grassTemplate, "Plantilla de hierba");
            _grassTemplate.SelectionChanged += (_, _) => { if (_grassX is { Text.Length: > 0 }) PreviewGrass(); };
            _details.Children.Add(_grassTemplate);
            _details.Children.Add(Note("Haz clic en el mapa para elegir el centro de la mancha, o escribe la casilla."));
            var coords = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            TextBox Number(string name)
            {
                var t = new TextBox { Width = 64, Margin = new Thickness(4, 0, 10, 0) };
                System.Windows.Automation.AutomationProperties.SetName(t, name);
                return t;
            }
            coords.Children.Add(new TextBlock { Text = "X", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
            coords.Children.Add(_grassX = Number("Casilla X del centro de la hierba"));
            coords.Children.Add(new TextBlock { Text = "Y", Foreground = Res("TextSoft"), VerticalAlignment = VerticalAlignment.Center });
            coords.Children.Add(_grassY = Number("Casilla Y del centro de la hierba"));
            _details.Children.Add(coords);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (choices.Count > 0)
        {
            buttons.Children.Add(Small("Previsualizar", () => PreviewGrass(), "Comprobar la posición y dibujar la hierba sin escribir nada"));
            _grassSave = Small("Guardar", () => SaveGrass(), "Guardar la hierba visible en el proyecto");
            _grassSave.IsEnabled = false;
            buttons.Children.Add(_grassSave);
        }
        buttons.Children.Add(Small("Cancelar", () => ShowDetails(null), "Cancelar la hierba visible"));
        _details.Children.Add(buttons);
        if (_mover.CanUndoGrass(zone))
        {
            _grassUndo = Small("Deshacer la última hierba de esta área", () => UndoGrass(), "Devolver el archivo de bloques, byte a byte, a su estado antes de la última hierba visible de esta área");
            _grassUndo.Margin = new Thickness(0, 8, 0, 0);
            _details.Children.Add(_grassUndo);
        }
        _grassResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("TextSoft"), Margin = new Thickness(0, 6, 0, 0) };
        _details.Children.Add(_grassResult);
        _details.Children.Add(Note(GrassLegend, small: true));
        _details.Children.Add(Note(GrassGameplayNotice, small: true));
        _canvas.PickingCell = choices.Count > 0;
        ShowGrassOverlay(grass, zone, null, null);
        _status.Text = $"Zona {zone:000}: elige dónde va la hierba visible.";
        return true;
    }

    private TextBlock Note(string text, bool main = false, bool bold = false, bool small = false) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res(main ? "TextMain" : "TextSoft"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        FontSize = small ? 11 : 12.5, Margin = new Thickness(0, small ? 6 : 4, 0, 0),
    };

    private void GrassCellPicked(int x, int y)
    {
        if (_grassX is null || _grassY is null) return;
        _grassX.Text = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _grassY.Text = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PreviewGrass();
    }

    /// <summary>Checks the template and centre and draws the patch on the map; nothing is written.</summary>
    internal bool PreviewGrass()
    {
        if (!_grassActive || _grassX is null || _grassY is null || _grassResult is null || _grassSave is null || CurrentZone is not int zone || _backend is not Gen7MapBackend grass) return false;
        _pendingGrass = null; _grassSave.IsEnabled = false;
        if (!int.TryParse(_grassX.Text, out int x) || !int.TryParse(_grassY.Text, out int y)) { _grassResult.Text = "Escribe números enteros para X e Y."; return false; }
        if (_grassTemplate?.SelectedValue is not int template) { _grassResult.Text = "Elige una plantilla."; return false; }
        Gen7GrassPlan? plan; string reason;
        try { (plan, reason) = grass.PlanGrass(zone, (x, y), template); }
        catch (Exception ex) when (ex is MapFormatException or InvalidOperationException) { (plan, reason) = (null, ex.Message); }
        if (plan is null)
        {
            _grassResult.Text = "No se puede: " + reason;
            ShowGrassOverlay(grass, zone, null, (x, y, template));
            return false;
        }
        var soil = grass.PaintableCells(zone).ToHashSet();
        var encounters = grass.EncounterCells(zone).ToHashSet();
        int admitted = plan.Covered.Count(soil.Contains), already = plan.Covered.Count(encounters.Contains);
        string name = (_grassTemplate.SelectedItem as GrassChoice)?.Name ?? $"plantilla {template}";
        string text = $"Se copiará «{name}» en el bloque {plan.TargetBlock}: ocupará {plan.Covered.Count} casillas (X {plan.Covered.Min(c => c.X)}–{plan.Covered.Max(c => c.X)}, Y {plan.Covered.Min(c => c.Y)}–{plan.Covered.Max(c => c.Y)}), sobre suelo casi plano (desnivel {plan.GroundSpread:0}).";
        if (plan.Sharing.Count > 0)
            text += $" Ese bloque también lo cargan {string.Join(", ", plan.Sharing.Select(a => grass.Zones.FirstOrDefault(z => z.Id == a)?.Name ?? $"{a:000}"))}: la hierba se verá también desde allí.";
        text += already > 0 ? $" {already} de esas casillas ya tienen encuentros." : "";
        text += admitted > 0
            ? $" {admitted} casillas tienen un suelo admitido: después puedes crear allí una «Zona de captura» para que salgan Pokémon."
            : " Bajo la hierba no hay suelo admitido para encuentros: será solo decorativa y la «Zona de captura» no podrá crear encuentros aquí.";
        _grassResult.Text = text + " No se ha escrito nada.";
        _pendingGrass = (plan, (x, y));
        ShowGrassOverlay(grass, zone, plan, null);
        _grassSave.IsEnabled = true;
        return true;
    }

    /// <summary>Block bounds, patch area (valid or not), the real model from above and the admitted soil under it.</summary>
    private void ShowGrassOverlay(Gen7MapBackend grass, int zone, Gen7GrassPlan? plan, (int X, int Y, int Template)? refused)
    {
        IReadOnlyList<(int X, int Y)> visible;
        try { visible = grass.VisibleGrassCells(zone); } catch (Exception ex) when (ex is MapFormatException or InvalidOperationException) { visible = []; }
        const double cell = Gen7MapBackend.UnitsPerCell;
        var w = grass.World(zone);
        double blockCells = w.BlockSize / cell;
        if (plan is not null)
        {
            var (fx, fz, fw, fd) = grass.GrassFootprint(plan);
            ImageSource? image = null; Rect? at = null;
            try
            {
                var (img, ix, iz, span) = grass.GrassPreviewImage(plan, 256);
                var bmp = BitmapSource.Create(img.Size, img.Size, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Size * 4);
                bmp.Freeze(); image = bmp; at = new Rect(ix / cell, iz / cell, span / cell, span / cell);
            }
            catch (Exception ex) when (ex is MapFormatException or InvalidOperationException) { image = null; }
            var soil = grass.PaintableCells(zone).ToHashSet();
            _canvas.SetGrass(new Map3dsGrassOverlay(new Rect(plan.Col * blockCells, plan.Row * blockCells, blockCells, blockCells),
                new Rect(fx / cell, fz / cell, fw / cell, fd / cell), image, at, plan.Covered.Where(soil.Contains).ToArray(), visible, true));
            return;
        }
        if (refused is { } r && grass.GrassTemplates.FirstOrDefault(t => t.Block == r.Template) is { } t)
        {
            int col = (int)Math.Floor(r.X / blockCells), row = (int)Math.Floor(r.Y / blockCells);
            double fw = t.Size.X / cell, fd = t.Size.Z / cell;
            _canvas.SetGrass(new Map3dsGrassOverlay(new Rect(col * blockCells, row * blockCells, blockCells, blockCells),
                new Rect(r.X + 0.5 - (fw / 2), r.Y + 0.5 - (fd / 2), fw, fd), null, null, [], visible, false));
            return;
        }
        _canvas.SetGrass(new Map3dsGrassOverlay(null, null, null, null, [], visible, true));
    }

    /// <summary>Writes the previewed grass through the project history and reloads the zone from disk.</summary>
    internal bool SaveGrass()
    {
        if (_mover is null || _pendingGrass is not { } pending || CurrentZone is not int zone || _grassResult is null) return false;
        try
        {
            string summary = _mover.AddGrass(_backend, zone, pending.Centre, pending.Plan.TemplateBlock);
            _backend = _mover.Reload();
            _encounterCells = null;
            ShowZone(zone);
            ShowGrassPanel();
            _status.Text = summary + " · Registrado en el historial: puedes deshacerlo aquí o desde el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException or MapFormatException)
        {
            _grassResult.Text = "No se guardó: " + ex.Message;
            return false;
        }
    }

    /// <summary>Undoes the area's last visible grass from its restore point (a new history entry) and reloads the zone.</summary>
    internal bool UndoGrass()
    {
        if (_mover is null || CurrentZone is not int zone) return false;
        try
        {
            string summary = _mover.UndoGrass(zone);
            _backend = _mover.Reload();
            _encounterCells = null;
            ShowZone(zone);
            ShowGrassPanel();
            _status.Text = summary + " · Registrado en el historial.";
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        {
            if (_grassResult is not null) _grassResult.Text = "No se deshizo: " + ex.Message;
            _status.Text = "No se deshizo: " + ex.Message;
            return false;
        }
    }

    private sealed record GrassChoice(string Name, int Block);
}
