using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>The existing Persistent Items section, backed by the independent XY installer.</summary>
internal sealed class Gen6PersistentItemsView : UserControl
{
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    internal Button PreviewInstall { get; } = new() { Content = "Vista previa de instalación", Margin = new Thickness(0, 0, 8, 8) };
    internal Button PreviewRemove { get; } = new() { Content = "Vista previa de retirada", Margin = new Thickness(0, 0, 8, 8) };
    internal Button ApplyButton { get; } = new() { Content = "Aplicar propuesta", IsEnabled = false, Margin = new Thickness(0, 0, 8, 8) };
    private readonly GameProjectSession _project;
    private readonly Gen6PersistentItemsProfile? _profile;
    private GameGen6PersistentItemsService? _service;
    private EditorProposal? _proposal;
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };

    internal Gen6PersistentItemsView(GameProjectSession project, Gen6PersistentItemsProfile? profile = null)
    {
        _project = project; _profile = profile;
        SetResourceReference(BackgroundProperty, "Bg");
        var content = new StackPanel { Margin = new Thickness(20), MaxWidth = 1100, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        content.Children.Add(new TextBlock { Text = "Persistent Items · Pokémon X/Y", FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Conserva el objeto inicialmente equipado al terminar el combate, también tras consumo, Desarme, Lanzamiento o Truco. Durante el combate se mantiene el funcionamiento normal del objeto.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { Text = "Alcance: combates salvajes y contra entrenadores normales. Instalaciones, PvP y combates en línea quedan fuera de alcance. La carga está comprobada en emulador HLE; no se acredita consola ni una firma CRR nueva.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(Status);
        var actions = new WrapPanel(); actions.Children.Add(PreviewInstall); actions.Children.Add(PreviewRemove); actions.Children.Add(ApplyButton); content.Children.Add(actions);
        content.Children.Add(_preview);
        content.Children.Add(new TextBlock { Text = "Archivo afectado: DllBattle.cro. Se conservan la CRR, el tamaño, los segmentos y las relocaciones. La propuesta muestra los cambios ARM y las huellas antes de escribir. Historial conserva la operación y permite Deshacer en orden; la retirada revierte solo las dos condiciones y sus hashes.", TextWrapping = TextWrapping.Wrap });
        foreach (var text in content.Children.OfType<TextBlock>()) text.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PreviewInstall.Click += (_, _) => ShowPreview(true); PreviewRemove.Click += (_, _) => ShowPreview(false); ApplyButton.Click += Apply;
        Refresh();
    }
    private void Refresh()
    {
        _proposal = null; ApplyButton.IsEnabled = false;
        var inspection = _profile is null ? GameGen6PersistentItemsService.Inspect(_project) : GameGen6PersistentItemsService.Inspect(_project, _profile);
        Status.Text = $"{inspection.Game} · {inspection.State}\n{inspection.Reason}";
        PreviewInstall.IsEnabled = PreviewRemove.IsEnabled = inspection.Allowed;
        _service = null;
        if (inspection.Allowed)
        {
            try { _service = _profile is null ? new(_project) : new(_project, _profile); }
            catch (Exception error) { Status.Text = error.Message; PreviewInstall.IsEnabled = PreviewRemove.IsEnabled = false; }
        }
    }
    private void ShowPreview(bool enable)
    {
        _proposal = null; ApplyButton.IsEnabled = false;
        try
        {
            if (_service is null) return;
            _proposal = _service.Preview(new Dictionary<string, int> { ["Enable"] = enable ? 1 : 0 }, [1]);
            _preview.Text = _proposal.Action + "\n" + (_proposal.Changes.Count == 0 ? "El archivo ya está en el estado solicitado. No hay cambios pendientes." :
                string.Join("\n", _proposal.Changes.Select(c => c.Field + ": " + c.Before + " → " + c.After))) +
                "\nSHA-256 original: " + _proposal.Parameters["SourceSHA256"] + "\nSHA-256 propuesto: " + _proposal.Parameters["ResultSHA256"];
            ApplyButton.IsEnabled = _proposal.Changes.Count > 0;
        }
        catch (Exception error) { _preview.Text = error.Message; }
    }
    private async void Apply(object sender, RoutedEventArgs args)
    {
        var proposal = _proposal;
        if (proposal is null || _service is null || !ApplyButton.IsEnabled) return;
        if (!StudioDialog.Confirm(Window.GetWindow(this), "Persistent Items", proposal.Action + ". Se modificará únicamente DllBattle.cro. ¿Aplicar la propuesta revisada?")) return;
        ApplyButton.IsEnabled = PreviewInstall.IsEnabled = PreviewRemove.IsEnabled = false;
        UiProgress.Begin("Guardando Persistent Items X/Y…");
        try
        {
            if (!StudioDialog.AutomatedChecks) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            _service.Apply(proposal); Refresh(); _preview.Text = "Operación guardada y verificada. Puedes consultarla y deshacerla desde Historial.";
        }
        catch (Exception error) { Refresh(); _preview.Text = "No se pudo aplicar la propuesta. " + error.Message; }
        finally { UiProgress.End(); }
    }
}
