using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal sealed class RomExtractionView : UserControl
{
    private readonly GameRomExtractionService _service;
    private readonly TextBox _input,_name;
    private readonly Button _select,_run,_cancel;
    private readonly TextBlock _detail;
    private CancellationTokenSource? _cancellation;
    internal bool ConfirmLeave()=>_cancellation is null;
    internal RomExtractionView(string? protectedProject=null)
    {
        _service=new(protectedProject);Background=(Brush)FindResource("Bg");
        var panel=new StackPanel{Margin=new Thickness(20)};
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="Extraer ROM · CXI / 3DS",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        panel.Children.Add(new TextBlock{Text="Seleccioná una ROM descifrada o reconstruida por pk3DS y una carpeta de destino. "+
            "RomFS, ExeFS y las cabeceras se preparan en una carpeta nueva y se verifican antes de publicarla. "+
            "El archivo original se conserva. Code.bin se descomprime cuando corresponde para poder editar el proyecto.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,15)});
        _input=new TextBox{IsReadOnly=true,Margin=new Thickness(0,0,0,10)};panel.Children.Add(_input);
        _select=new Button{Content="Seleccionar ROM…",HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,0,0,14)};
        _select.Click+=(_,_)=>Select();panel.Children.Add(_select);
        panel.Children.Add(new TextBlock{Text="Nombre de la nueva carpeta",Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,6)});
        _name=new TextBox{Text="Progressive_extraido",Margin=new Thickness(0,0,0,12)};panel.Children.Add(_name);
        var buttons=new WrapPanel();panel.Children.Add(buttons);
        _run=new Button{Content="Elegir destino y extraer…",IsEnabled=false,Margin=new Thickness(0,0,10,12)};
        _run.Click+=async(_,_)=>await ExtractAsync();buttons.Children.Add(_run);
        _cancel=new Button{Content="Cancelar",IsEnabled=false,Margin=new Thickness(0,0,0,12)};
        _cancel.Click+=(_,_)=>Cancel();buttons.Children.Add(_cancel);
        _detail=new TextBlock{Text="Todavía no se seleccionó una ROM.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft")};panel.Children.Add(_detail);
    }
    private void Cancel(){_cancellation?.Cancel();_cancel.IsEnabled=false;_detail.Text="Cancelando y retirando la salida temporal…";}
    private void Select()
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Title="ROM descifrada o reconstruida",Filter="CXI / 3DS|*.cxi;*.3ds|Todos los archivos|*.*"};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        _input.Text=picker.FileName;_name.Text=Path.GetFileNameWithoutExtension(picker.FileName)+"_extraido";_run.IsEnabled=true;
    }
    private async Task ExtractAsync()
    {
        if(_cancellation is not null)return;
        string name=_name.Text.Trim();
        if(name.Length==0||name is "." or ".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
        {StudioDialog.Error(Window.GetWindow(this),"Extracción","Escribí un nombre de carpeta válido.");return;}
        var picker=new Microsoft.Win32.OpenFolderDialog{Title="Carpeta donde se creará el proyecto extraído"};
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        _cancellation=new();_select.IsEnabled=_run.IsEnabled=_name.IsEnabled=false;_cancel.IsEnabled=true;
        UiProgress.Begin("Extrayendo ROM");
        try
        {
            var result=await _service.ExtractAsync(_input.Text,Path.Combine(picker.FolderName,name),_cancellation.Token);
            _detail.Text=$"Extracción completada y verificada.\n{result.Files:N0} archivos · {result.Folder}\nSHA-256 del original: {result.SourceSha256}\nUsá «Abrir proyecto» y seleccioná esta carpeta para comenzar a editar.";
        }
        catch(OperationCanceledException){_detail.Text="Extracción cancelada. El archivo original se conserva y no se publicó una carpeta de destino.";}
        catch(Exception ex){_detail.Text="La extracción se detuvo: "+ex.Message;StudioDialog.Error(Window.GetWindow(this),"Extracción",ex.Message);}
        finally{_cancellation.Dispose();_cancellation=null;_select.IsEnabled=_run.IsEnabled=_name.IsEnabled=true;_cancel.IsEnabled=false;UiProgress.End();}
    }
}
