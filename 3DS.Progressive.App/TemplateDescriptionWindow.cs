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
    private readonly System.Windows.Controls.Image _previewImage=new(){Width=320,Height=240,Stretch=Stretch.Uniform};
    private readonly TextBlock _previewStatus=new(){TextWrapping=TextWrapping.Wrap};
    private readonly System.Windows.Controls.Grid _previewPanel=new(){Visibility=Visibility.Collapsed};
    private readonly RowDefinition _previewRow=new(){Height=new GridLength(0)};
    private readonly System.Windows.Controls.Button _previewButton;
    internal string Description=>_editor.Text;
    internal TemplateDescriptionWindow(string text,GameProjectSession? project=null,MovePreviewExample? move=null)
    {
        _project=project;_move=move;
        Title="InGameDescription · español";MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        Width=Math.Min(820,Math.Max(320,MaxWidth-32));Height=Math.Min(480,Math.Max(300,MaxHeight-32));MinWidth=Math.Min(480,Width);MinHeight=Math.Min(340,Height);
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=(Brush)FindResource("Bg");Foreground=(Brush)FindResource("TextMain");
        var root=new System.Windows.Controls.Grid{Margin=new Thickness(20)};Content=root;
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star),MinHeight=90});root.RowDefinitions.Add(_previewRow);root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var title=new TextBlock{Text=Title,Style=(Style)FindResource("PageHeading"),Margin=new Thickness(0,0,0,12)};root.Children.Add(title);
        var footer=new WrapPanel{HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};System.Windows.Controls.Grid.SetRow(footer,3);root.Children.Add(footer);
        _previewButton=new System.Windows.Controls.Button{Content="Previsualizar",IsEnabled=project is not null,Margin=new Thickness(0,0,8,0)};footer.Children.Add(_previewButton);_previewButton.Click+=async (_,_)=>await PreviewAsync();
        _previewPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});_previewPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});_previewPanel.Margin=new Thickness(0,12,0,0);System.Windows.Controls.Grid.SetRow(_previewPanel,2);root.Children.Add(_previewPanel);
        var picture=new Viewbox{Child=_previewImage,Stretch=Stretch.Uniform,MaxWidth=640,MaxHeight=360,HorizontalAlignment=System.Windows.HorizontalAlignment.Left};_previewPanel.Children.Add(picture);
        _previewStatus.Foreground=(Brush)FindResource("TextSoft");_previewStatus.FontSize=12;_previewStatus.Margin=new Thickness(0,8,0,0);System.Windows.Controls.Grid.SetRow(_previewStatus,1);_previewPanel.Children.Add(_previewStatus);
        var cancel=new System.Windows.Controls.Button{Content="Cancelar",IsCancel=true,Margin=new Thickness(0,0,8,0)};EditorPresentation.StyleAction(cancel,EditorPresentation.ActionRole.Tertiary);footer.Children.Add(cancel);
        var save=new System.Windows.Controls.Button{Content="Guardar descripción"};EditorPresentation.StyleAction(save,EditorPresentation.ActionRole.Primary);save.Click+=(_,_)=>DialogResult=true;footer.Children.Add(save);
        _editor=new System.Windows.Controls.TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};System.Windows.Controls.Grid.SetRow(_editor,1);System.Windows.Automation.AutomationProperties.SetName(_editor,"Descripción del movimiento");root.Children.Add(_editor);
        _editor.TextChanged+=(_,_)=>{if(_font is not null&&_previewPanel.Visibility==Visibility.Visible)RefreshPreview();};
        Loaded+=(_,_)=>{_editor.Focus();_editor.CaretIndex=_editor.Text.Length;};
    }
    private async Task PreviewAsync()
    {
        if(_project is null)return;
        _previewButton.IsEnabled=false;_previewPanel.Visibility=Visibility.Visible;_previewRow.Height=new GridLength(1.5,GridUnitType.Star);Height=Math.Min(MaxHeight,Math.Max(Height,720));_previewStatus.Text="Cargando la fuente de tu ROM…";
        try
        {
            _font??=await Task.Run(()=>GameDescriptionFont.Load(_project.GameConfiguration.RomFS,_project.Generation,_project.GameConfiguration.ORAS));
            if(_move is not null)
            {
                if(_project.Generation==6)_scene6??=await Task.Run(()=>GameMoveDescription6Scene.Load(_project.GameConfiguration.RomFS));
                else _scene??=await Task.Run(()=>GameMoveDescriptionScene.Load(_project.GameConfiguration.RomFS,_project.GameConfiguration.USUM));

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
        var rendered=_scene6 is not null&&_move is not null?_scene6.Render(_font,Description,_move):_scene is not null&&_move is not null?_scene.Render(_font,Description,_move):_font.Render(Description,_project.Generation);_previewImage.Source=rendered.Image;_previewImage.Width=rendered.Image.PixelWidth;_previewImage.Height=rendered.Image.PixelHeight;
        if(_scene6 is not null){_previewStatus.Text="Ficha de movimientos Gen6 · "+_move!.Name+". Fondo del resumen y paneles de detalle de tu ROM; composición estática.\n"+rendered.Status;return;}
        if(_scene is not null){_previewStatus.Text="Ficha de movimientos de tu ROM · "+_move!.Name+". Fondo y cuadro originales; vista estática, sin ejecutar comandos ni variables.\n"+rendered.Status;return;}
        _previewStatus.Text="Fuente de tu ROM · "+_font.Resource+". Vista de referencia: el fondo, tamaño y posición del menú son aproximados; no se ejecutan comandos ni variables.\n"+rendered.Status;
    }

}
