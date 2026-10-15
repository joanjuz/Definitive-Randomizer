using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;
internal sealed class TitleScreenView : UserControl
{
    private readonly ComboBox _archives = new() { DisplayMemberPath = "Label" }, _images = new() { DisplayMemberPath = "Name" };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new();
    private readonly StackPanel _controls = new();
    private readonly string _projectRoot;
    private GameTitleScreenService? _service;
    private bool _busy;
    internal bool IsBusy=>_busy;
    internal bool ConfirmLeave() => !_busy && (_service?.Dirty != true || StudioDialog.Confirm(Window.GetWindow(this), "Título pendiente", "¿Descartar las imágenes sin guardar?"));
    internal TitleScreenView(GameProjectSession project)
    {
        _projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.ProjectRoot)) + Path.DirectorySeparatorChar;
        Background = (Brush)FindResource("Bg"); var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        DockPanel.SetDock(_controls, Dock.Top); root.Children.Add(_controls);
        _controls.Children.Add(new TextBlock { Text = "Pantallas de título · Gen"+project.Generation, FontSize = 24, Foreground = (Brush)FindResource("TextMain") });
        _controls.Children.Add(new TextBlock { Text = project.GameConfiguration.USUM?"Edita los logos 2D y efectos del inicio de una partida nueva. Para la secuencia animada de acceso a la partida guardada, abrí Animaciones y vídeos MOFLEX. Se conservan formato, orientación y tamaño originales.":project.GameConfiguration.ORAS?"Edita el logo moderno de Pulsa Start y sus efectos 2D, y la presentación Presented by / GAME FREAK del bosque. El catálogo muestra los diseños, animaciones y archivos de cada paquete. Los vídeos MOFLEX son recursos independientes.":"Edita los recursos de título del juego. Conserva las dimensiones y verifica los demás archivos antes de guardar en el historial.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSoft"), Margin = new Thickness(0,8,0,12) });
        _controls.Children.Add(_archives);
        var resourceDescription=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,6,0,6),MaxHeight=110};
        _controls.Children.Add(new ScrollViewer{Content=resourceDescription,MaxHeight=110,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        _archives.SelectionChanged+=(_,_)=>resourceDescription.Text=(_archives.SelectedItem as TitleArchive)?.Description??"";
        _controls.Children.Add(_images);
        var buttons = new WrapPanel(); _controls.Children.Add(buttons);
        Add(buttons, "Importar PNG / BCLIM / BFLIM…", Import); Add(buttons, "Exportar PNG…", () => Export(true)); Add(buttons, "Exportar imagen original…", () => Export(false)); Add(buttons, "Guardar y verificar", Save);
        _archives.SelectionChanged += (_, _) => Try(() => { if (_service is not null && _archives.SelectedItem is TitleArchive archive) { _images.ItemsSource = _service.Images(archive.Id); _images.SelectedIndex = 0; } });
        _images.SelectionChanged += (_, _) => Try(Show);
        _status.Foreground = (Brush)FindResource("TextSoft"); DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(_image);
        Loaded += async (_, _) =>
        {
            if (_service is not null || _busy) return; Busy(true); UiProgress.Begin("Leyendo pantallas de título…");
            try { _service = await Task.Run(() => new GameTitleScreenService(project)); _archives.ItemsSource = _service.Archives; _archives.SelectedIndex = _service.Archives.Count - 1; }
            catch (Exception ex) { _status.Text = ex.Message; StudioDialog.Error(Window.GetWindow(this), "Pantallas de título", ex.Message); }
            finally { Busy(false); UiProgress.End(); }
        };
    }
    private void Busy(bool value) { _busy = value; _controls.IsEnabled = !value; }
    private void Try(Action action) { try { action(); } catch (Exception ex) { StudioDialog.Error(Window.GetWindow(this), "Pantallas de título", ex.Message); } }
    private static void Add(WrapPanel panel, string text, Action action) { var button = new Button { Content = text, Margin = new Thickness(0,8,8,8) }; button.Click += (_, _) => action(); panel.Children.Add(button); }
    private (int Archive, int Image) Selection() => _archives.SelectedItem is TitleArchive archive && _images.SelectedItem is TitleDarcEntry image ? (archive.Id, image.Id) : throw new InvalidOperationException("Seleccioná una imagen de título.");
    private void Show()
    {
        if (_service is null || _images.SelectedItem is null) return; var selected = Selection();
        using var bitmap = GameGraphicsCodec.Decode(_service.Read(selected.Archive, selected.Image)); using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); _image.Source = image;
        _status.Text = $"{bitmap.Width} × {bitmap.Height}" + (_service.Dirty ? " · cambios pendientes" : " · sin cambios pendientes");
    }
    private void Import() => Try(() =>
    {
        if (_service is null) return; var selected = Selection(); var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.bclim;*.bflim" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (new FileInfo(dialog.FileName).Length > 64 * 1024 * 1024) throw new InvalidDataException("La imagen supera 64 MiB.");
        byte[] data = File.ReadAllBytes(dialog.FileName);
        if (Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (data.Length < 24 || !data.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)) is 0 or >4096 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)) is 0 or >4096) throw new InvalidDataException("Encabezado PNG incompatible.");
            using var stream = new MemoryStream(data); using var bitmap = new System.Drawing.Bitmap(stream); data = GameGraphicsCodec.EncodeLike(bitmap,_service.Read(selected.Archive,selected.Image));
        }
        _service.Replace(selected.Archive, selected.Image, data); Show();
    });
    private void Export(bool png) => Try(() =>
    {
        if (_service is null) return; var selected = Selection(); var dialog = new Microsoft.Win32.SaveFileDialog { Filter = png ? "PNG|*.png" : _service.Gen7?"BFLIM|*.bflim":"BCLIM|*.bclim", FileName = png ? "title.png" : _service.Gen7?"title.bflim":"title.bclim" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (Path.GetFullPath(dialog.FileName).StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Exportá la imagen fuera del proyecto para conservar sus recursos.");
        byte[] data = _service.Read(selected.Archive, selected.Image); if (png) { using var bitmap = GameGraphicsCodec.Decode(data); bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png); } else File.WriteAllBytes(dialog.FileName, data);
    });
    private async void Save()
    {
        if (_service?.Dirty != true || !StudioDialog.Confirm(Window.GetWindow(this), "Guardar títulos", "¿Guardar las imágenes y registrar el cambio en el historial?")) return;
        Busy(true); UiProgress.Begin("Reconstruyendo y verificando títulos…");
        try { await Task.Run(_service.Apply); Show(); _status.Text += " · guardado verificado"; }
        catch (Exception ex) { StudioDialog.Error(Window.GetWindow(this), "Pantallas de título", ex.Message); }
        finally { Busy(false); UiProgress.End(); }
    }
}
