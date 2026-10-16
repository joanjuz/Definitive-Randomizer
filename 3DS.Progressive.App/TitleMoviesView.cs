using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive.Services;
using Panel=System.Windows.Controls.Panel;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using Image=System.Windows.Controls.Image;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;
internal sealed class TitleMoviesView : UserControl,IDisposable
{
    private readonly GameTitleMovieService _service;
    private readonly ComboBox _files=new(){DisplayMemberPath="Label",Margin=new Thickness(0,8,0,8)};
    private readonly ComboBox _track=new(){MinWidth=220,Margin=new Thickness(0,0,12,0)};
    private readonly TextBox _seconds=new(){Text="5",Width=80,Margin=new Thickness(0,0,12,0)};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    private readonly Image _preview=new(){Stretch=Stretch.Uniform,MaxHeight=420};
    private readonly List<Button> _actions=[];
    private readonly Button _cancel=new(){Content="Cancelar operación",IsEnabled=false,Margin=new Thickness(0,8,0,8)};
    private CancellationTokenSource? _cancellation;
    private bool _suppress;
    private TitleMovieFile? _selected;
    internal bool Busy=>_cancellation is not null;
    internal bool ConfirmLeave()=>!Busy&&(!_service.Dirty||StudioDialog.Confirm(Window.GetWindow(this),"Vídeo pendiente","¿Descartar el vídeo preparado sin aplicarlo?"));
    internal TitleMoviesView(GameProjectSession project)
    {
        _service=new(project);_service.Progress=new Progress<string>(text=>Dispatcher.InvokeAsync(()=>_status.Text=text));Background=(Brush)FindResource("Bg");
        var body=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        body.Children.Add(new TextBlock{Text="Animaciones de inicio · Gen"+project.Generation,FontSize=24,Foreground=(Brush)FindResource("TextMain")});
        body.Children.Add(new TextBlock{Text="Esta secuencia es un vídeo MOFLEX. Exportá la pista a MP4 para editarla y volvé a importarla, o importá un MOFLEX preparado. Al importar MP4 se sustituye solo la pista elegida y se conservan las demás pantallas. Se usa el audio del original, recodificado al mismo formato, frecuencia y canales; el audio del MP4 no se importa. Conservá dimensiones, FPS y número de fotogramas.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,8)});
        body.Children.Add(_files);body.Children.Add(_status);_status.Foreground=(Brush)FindResource("TextSoft");
        var frame=new WrapPanel{Margin=new Thickness(0,8,0,8)};body.Children.Add(frame);frame.Children.Add(_track);frame.Children.Add(new TextBlock{Text="Segundo:",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),Foreground=(Brush)FindResource("TextSoft")});frame.Children.Add(_seconds);
        Add(frame,"Ver fotograma",async token=>{if(!decimal.TryParse(_seconds.Text,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal second))throw new ArgumentException("Indicá un segundo válido, por ejemplo 10.5.");byte[] bmp=await _service.Preview(second,_track.SelectedIndex,token);using var stream=new MemoryStream(bmp);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();_preview.Source=image;});
        var buttons=new WrapPanel();body.Children.Add(buttons);
        Add(buttons,"Exportar MOFLEX…",_=>{var path=Save("MOFLEX|*.moflex","titulo-original.moflex");if(path is not null)_service.ExportMoflex(path);return Task.CompletedTask;});
        Add(buttons,"Exportar pista a MP4…",async token=>{var path=Save("MP4|*.mp4","titulo-pista-"+(_track.SelectedIndex+1)+".mp4");if(path is not null)await _service.ExportMp4(path,_track.SelectedIndex,token);});
        Add(buttons,"Importar MOFLEX…",async token=>{var path=Open("Vídeo MOFLEX|*.moflex");if(path is not null)await _service.ImportMoflex(path,token);});
        Add(buttons,"Importar vídeo editado…",async token=>{var path=Open("Vídeo editado|*.mp4;*.mkv;*.avi;*.mov");if(path is not null)await _service.ImportEditedVideo(path,_track.SelectedIndex,token);});
        Add(buttons,"Aplicar y verificar",_=>{_service.Apply();return Task.CompletedTask;});
        Add(buttons,"Descartar preparación",_=>{_service.Discard();return Task.CompletedTask;});
        body.Children.Add(_cancel);_cancel.Click+=(_,_)=>_cancellation?.Cancel();body.Children.Add(_preview);
        _files.ItemsSource=_service.Files;
        _files.SelectionChanged+=async(_,_)=>
        {
            if(_suppress||_files.SelectedItem is not TitleMovieFile movie)return;
            if(_service.Dirty&&!StudioDialog.Confirm(Window.GetWindow(this),"Vídeo pendiente","¿Descartar el vídeo preparado para abrir otro?")){_suppress=true;_files.SelectedItem=_selected;_suppress=false;return;}
            await Execute(async token=>{try{await _service.Select(movie,token);}catch{_suppress=true;_files.SelectedItem=_selected;_suppress=false;throw;}_selected=movie;_preview.Source=null;_track.ItemsSource=_service.Profile!.Videos.Select((v,i)=>$"Pista {i+1} · {v.Width} × {v.Height}").ToArray();_track.SelectedIndex=0;});
        };
        Loaded+=(_,_)=>{if(_files.SelectedIndex<0&&_service.Files.Count>0)_files.SelectedIndex=0;};
        if(_service.Files.Count==0)_status.Text="La extracción no contiene vídeos MOFLEX en la carpeta m.";
    }
    private static string? Save(string filter,string name){var dialog=new Microsoft.Win32.SaveFileDialog{Filter=filter,FileName=name};return dialog.ShowDialog()==true?dialog.FileName:null;}
    private static string? Open(string filter){var dialog=new Microsoft.Win32.OpenFileDialog{Filter=filter};return dialog.ShowDialog()==true?dialog.FileName:null;}
    private void Add(Panel panel,string title,Func<CancellationToken,Task> action)
    {var button=new Button{Content=title,Margin=new Thickness(0,0,8,8),Padding=new Thickness(12,7,12,7)};panel.Children.Add(button);_actions.Add(button);button.Click+=async(_,_)=>await Execute(action);}
    private async Task Execute(Func<CancellationToken,Task> action)
    {
        if(Busy)return;using var cancellation=new CancellationTokenSource();_cancellation=cancellation;
        foreach(var button in _actions)button.IsEnabled=false;_files.IsEnabled=false;_track.IsEnabled=false;_seconds.IsEnabled=false;_cancel.IsEnabled=true;_status.Text="Procesando vídeo…";
        try{await action(cancellation.Token);Describe();}
        catch(OperationCanceledException){_status.Text="Operación cancelada. No se aplicaron cambios al proyecto.";}
        catch(Exception ex){_status.Text=ex.Message;StudioDialog.Error(Window.GetWindow(this),"Animaciones de título",ex.Message);}
        finally{_cancellation=null;foreach(var button in _actions)button.IsEnabled=true;_files.IsEnabled=true;_track.IsEnabled=true;_seconds.IsEnabled=true;_cancel.IsEnabled=false;}
    }
    private void Describe()
    {
        if(_service.Profile is not {} profile)return;
        _status.Text=string.Join("\n",profile.Videos.Select((v,i)=>$"Pista {i+1}: {v.Width} × {v.Height} · {v.Rate} FPS · {v.Packets} fotogramas"))+"\n"+profile.Audio.Length+" pista(s) de audio · "+(_service.Dirty?"Vídeo preparado; pendiente de aplicar":"Sin cambios pendientes");
    }
    public void Dispose()=>_service.Dispose();
}

internal sealed class TitleResourcesView : UserControl
{
    private readonly TitleScreenView _images;
    private readonly TitleMoviesView _movies;
    private readonly ContentControl _page=new();
    internal bool ConfirmLeave()=>_images.ConfirmLeave()&&_movies.ConfirmLeave();
    internal TitleResourcesView(GameProjectSession project)
    {
        _images=new(project);_movies=new(project);var root=new DockPanel{Background=(Brush)FindResource("Bg")};Content=root;
        var navigation=new WrapPanel{Margin=new Thickness(22,12,22,0)};DockPanel.SetDock(navigation,Dock.Top);root.Children.Add(navigation);root.Children.Add(_page);
        foreach(var item in new[]{("Imágenes y logos 2D",(object)_images),("Animaciones y vídeos MOFLEX",(object)_movies)})
        {var button=new Button{Content=item.Item1,Margin=new Thickness(0,0,10,0),Padding=new Thickness(12,8,12,8)};navigation.Children.Add(button);button.Click+=(_,_)=>{if(!_images.IsBusy&&!_movies.Busy)_page.Content=item.Item2;};}
        _page.Content=_images;Unloaded+=(_,_)=>_movies.Dispose();
    }
}
