using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Binding=System.Windows.Data.Binding;
using Brush=System.Windows.Media.Brush;
using ThreeDS.Progressive.Services;
namespace ThreeDS.Progressive;
internal sealed class OptionalDescriptionConverter : IValueConverter
{
    internal static readonly OptionalDescriptionConverter Instance=new();
    public object? Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is string s&&!string.IsNullOrWhiteSpace(s)?s:null;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
internal sealed class DescriptionPreviewConverter : IValueConverter
{
    internal static readonly DescriptionPreviewConverter Instance=new();
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        string text=value as string??"";if(string.IsNullOrWhiteSpace(text))return "Añadir descripción…";
        text=text.Replace("\r","").Replace("\n"," · ");return text.Length>75?text[..75]+"…":text;
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
internal sealed class TemplateDescriptionWindow : Window
{
    private readonly System.Windows.Controls.TextBox _editor;
    private readonly GameProjectSession? _project;
    private GameDescriptionFont? _font;
    private GameMoveDescriptionScene? _scene;
    private GameMoveDescription6Scene? _scene6;
    private readonly MovePreviewExample? _move;
    private readonly System.Windows.Controls.Image _previewImage=new(){Width=640,Height=224,Stretch=Stretch.Uniform};
    private readonly TextBlock _previewStatus=new(){TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel _previewPanel=new(){Visibility=Visibility.Collapsed};
    private readonly System.Windows.Controls.Button _previewButton;
    internal string Description=>_editor.Text;
    internal TemplateDescriptionWindow(string text,GameProjectSession? project=null,MovePreviewExample? move=null)
    {
        _project=project;_move=move;
        Title="InGameDescription · español";Width=760;Height=380;MinWidth=480;MinHeight=280;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=(Brush)FindResource("Bg");Foreground=(Brush)FindResource("TextMain");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var title=new TextBlock{Text=Title,FontSize=21,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var footer=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        _previewButton=new System.Windows.Controls.Button{Content="Previsualizar",IsEnabled=project is not null,Margin=new Thickness(0,0,8,0)};footer.Children.Add(_previewButton);_previewButton.Click+=async (_,_)=>await PreviewAsync();
        DockPanel.SetDock(_previewPanel,Dock.Bottom);root.Children.Add(_previewPanel);_previewPanel.Children.Add(_previewImage);_previewPanel.Children.Add(_previewStatus);
        var cancel=new System.Windows.Controls.Button{Content="Cancelar",IsCancel=true,Margin=new Thickness(0,0,8,0)};footer.Children.Add(cancel);
        var save=new System.Windows.Controls.Button{Content="Guardar descripción"};save.Click+=(_,_)=>DialogResult=true;footer.Children.Add(save);
        _editor=new System.Windows.Controls.TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};root.Children.Add(_editor);
        _editor.TextChanged+=(_,_)=>{if(_font is not null&&_previewPanel.Visibility==Visibility.Visible)RefreshPreview();};
        Loaded+=(_,_)=>{_editor.Focus();_editor.CaretIndex=_editor.Text.Length;};
    }
    private async Task PreviewAsync()
    {
        if(_project is null)return;
        _previewButton.IsEnabled=false;_previewPanel.Visibility=Visibility.Visible;Height=Math.Max(Height,850);_previewStatus.Text="Cargando la fuente de tu ROM…";
        try
        {
            _font??=await Task.Run(()=>GameDescriptionFont.Load(_project.GameConfiguration.RomFS,_project.Generation,_project.GameConfiguration.ORAS));
            if(_move is not null)
            {
                if(_project.Generation==6)_scene6??=await Task.Run(()=>GameMoveDescription6Scene.Load(_project.GameConfiguration.RomFS));
                else _scene??=await Task.Run(()=>GameMoveDescriptionScene.Load(_project.GameConfiguration.RomFS,_project.GameConfiguration.USUM));
                _previewImage.Height=480;
            }
            RefreshPreview();
        }
        catch(Exception ex) when(ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException or NotSupportedException)
        {
            _previewImage.Source=null;_previewStatus.Text="No se pudo cargar la vista previa: "+ex.Message+" El texto se puede seguir editando y guardando.";
        }
        finally{_previewButton.IsEnabled=true;}
    }
    private void RefreshPreview()
    {
        if(_font is null||_project is null||(_move is not null&&_scene is null&&_scene6 is null))return;
        var rendered=_scene6 is not null&&_move is not null?_scene6.Render(_font,Description,_move):_scene is not null&&_move is not null?_scene.Render(_font,Description,_move):_font.Render(Description,_project.Generation);_previewImage.Source=rendered.Image;
        if(_scene6 is not null){_previewStatus.Text="Ficha de movimientos Gen6 · "+_move!.Name+". Fondo del resumen y paneles de detalle de tu ROM; composición estática.\n"+rendered.Status;return;}
        if(_scene is not null){_previewStatus.Text="Ficha de movimientos de tu ROM · "+_move!.Name+". Fondo y cuadro originales; vista estática, sin ejecutar comandos ni variables.\n"+rendered.Status;return;}
        _previewStatus.Text="Fuente de tu ROM · "+_font.Resource+". Vista de referencia: el fondo, tamaño y posición del menú son aproximados; no se ejecutan comandos ni variables.\n"+rendered.Status;
    }

}
