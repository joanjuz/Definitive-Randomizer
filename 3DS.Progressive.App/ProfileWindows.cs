using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

/// <summary>Shared layout of the two profile dialogs: heading, scrolling body and a row of buttons.</summary>
internal abstract class ProfileWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly WrapPanel Footer = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
    protected ProfileWindow(string title)
    {
        Title = title; MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
        Width = Math.Min(760, Math.Max(320, MaxWidth - 32)); Height = Math.Min(640, Math.Max(300, MaxHeight - 32));
        MinWidth = Math.Min(420, Width); MinHeight = Math.Min(320, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("TextMain");
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var heading = new TextBlock { Text = title, Style = (Style)FindResource("PageHeading"), Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(Footer, Dock.Bottom); root.Children.Add(Footer);
        root.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
    }
    protected TextBlock Line(string text, bool soft = false, bool strong = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, soft ? "TextSoft" : "TextMain");
        Body.Children.Add(block); return block;
    }
    protected Button Action(string text, EditorPresentation.ActionRole role, bool cancel = false)
    {
        var button = new Button { Content = text, IsCancel = cancel, Margin = new Thickness(8, 0, 0, 0) };
        EditorPresentation.StyleAction(button, role); Footer.Children.Add(button); return button;
    }
}

/// <summary>Export: what the profile will carry, what is left out and why, and the texts the author chooses to add.</summary>
internal sealed class ProfileExportWindow : ProfileWindow
{
    private readonly TextBox _name, _author, _description;
    internal string ProfileName => _name.Text;
    internal string Author => _author.Text;
    internal string Description => _description.Text;
    internal ProfileExportWindow(ShareableProfile draft) : base("Exportar perfil compartible")
    {
        Line("Un perfil guarda configuración para que otra persona la use en su propia copia del juego. No incluye la ROM, archivos del juego, textos del juego ni rutas de este equipo.", soft: true);
        _name = Field("Nombre del perfil", draft.Name, 120, false);
        _author = Field("Autor (opcional; se guarda en el archivo)", draft.Author, 80, false);
        _description = Field("Descripción (opcional)", draft.Description, 2000, true);
        Line($"Juego: {ShareableProfile.GameTitle(draft.Game)} · {draft.Sections.Count} secciones", strong: true);
        foreach (var section in draft.Sections) Line("• " + section.Title + " — " + section.Summary);
        if (draft.Omitted.Count > 0)
        {
            Line("No se incluye en el perfil:", strong: true);
            foreach (var omitted in draft.Omitted) Line($"• {omitted.Count} × {omitted.Reason}", soft: true);
        }
        Action("Cancelar", EditorPresentation.ActionRole.Tertiary, cancel: true);
        var save = Action("Guardar perfil…", EditorPresentation.ActionRole.Primary);
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) { StudioDialog.Notice(this, "Exportar perfil", "Escribe un nombre para el perfil."); _name.Focus(); return; }
            DialogResult = true;
        };
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
    private TextBox Field(string label, string value, int max, bool multiline)
    {
        Line(label, soft: true).Margin = new Thickness(0, 6, 0, 3);
        var box = new TextBox { Text = value, MaxLength = max, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 64 : 0, Margin = new Thickness(0, 0, 0, 6),
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
        AutomationProperties.SetName(box, label); Body.Children.Add(box); return box;
    }
}

/// <summary>
/// Import: who made the profile, whether it fits the open project and which sections to load. Nothing is written from
/// here: accepting only loads the chosen sections into Global Template. A section that is not compatible cannot be ticked.
/// </summary>
internal sealed class ProfileImportWindow : ProfileWindow
{
    private readonly List<(ProfileSectionReport Report, CheckBox Box)> _rows = [];
    private readonly Button _accept;
    internal IReadOnlyList<(ProfileSectionReport Report, CheckBox Box)> Rows => _rows;
    internal Button AcceptButton => _accept;
    internal IReadOnlyCollection<string> Chosen => _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Report.Section.Id).ToArray();
    internal ProfileImportWindow(ShareableProfile profile, ProfileCompatibility compatibility) : base("Importar perfil compartible")
    {
        Line(profile.Name, strong: true).FontSize = 17;
        string origin = string.Join(" · ", new[] { profile.Author.Length > 0 ? "Autor: " + profile.Author : "Autor no indicado",
            profile.Generator.Length > 0 ? "Creado con " + profile.Generator : "", profile.CreatedUtc.Length > 0 ? profile.CreatedUtc : "" }.Where(t => t.Length > 0));
        Line("Juego: " + ShareableProfile.GameTitle(profile.Game) + " · " + origin + " · Huella " + profile.RecipeSha256[..12] + "…", soft: true);
        Line("La procedencia la declara el archivo: comprueba que confías en quien te lo dio.", soft: true);
        if (profile.Description.Length > 0) Line(profile.Description);
        Line(compatibility.Verdict, strong: true).Margin = new Thickness(0, 8, 0, 8);
        foreach (var report in compatibility.Sections)
        {
            string state = report.Status switch
            {
                ProfileSectionStatus.Compatible => "Compatible", ProfileSectionStatus.Conflict => "Conflicto",
                ProfileSectionStatus.Incompatible => "No compatible", _ => "No reconocida",
            };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = report.Section.Title + " · " + state, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var summary = new TextBlock { Text = report.Section.Summary, TextWrapping = TextWrapping.Wrap }; summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft"); text.Children.Add(summary);
            var detail = new TextBlock { Text = report.Detail, TextWrapping = TextWrapping.Wrap }; detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft"); text.Children.Add(detail);
            // A conflict is offered but never ticked for the user; what cannot be used stays unticked and explains why.
            var box = new CheckBox { Content = text, IsEnabled = report.Selectable, IsChecked = report.Status == ProfileSectionStatus.Compatible, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetName(box, report.Section.Title + ". " + state + ". " + report.Detail);
            box.Checked += (_, _) => Refresh(); box.Unchecked += (_, _) => Refresh();
            Body.Children.Add(box); _rows.Add((report, box));
        }
        if (compatibility.DependencyWarnings.Count > 0)
        {
            Line("Dependencias que este equipo no tiene tal como las espera el perfil (la vista previa las comprobará):", strong: true);
            foreach (string warning in compatibility.DependencyWarnings) Line("• " + warning, soft: true);
        }
        if (profile.Omitted.Count > 0)
        {
            Line("Su autor no pudo incluir:", strong: true);
            foreach (var omitted in profile.Omitted) Line($"• {omitted.Count} × {omitted.Reason}", soft: true);
        }
        Line("Aceptar no modifica el proyecto: carga las secciones marcadas en Global Template, donde podrás previsualizar los cambios y aplicarlos.", soft: true).Margin = new Thickness(0, 8, 0, 0);
        Action("Cancelar", EditorPresentation.ActionRole.Tertiary, cancel: true);
        _accept = Action("Cargar secciones marcadas", EditorPresentation.ActionRole.Primary);
        _accept.Click += (_, _) => DialogResult = true;
        Refresh();
    }
    private void Refresh()
    {
        int chosen = _rows.Count(r => r.Box.IsChecked == true);
        _accept.IsEnabled = chosen > 0;
        _accept.Content = chosen == 0 ? "Marca al menos una sección" : chosen == 1 ? "Cargar 1 sección" : $"Cargar {chosen} secciones";
    }
}
