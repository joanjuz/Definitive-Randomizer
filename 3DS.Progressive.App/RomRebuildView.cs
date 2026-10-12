using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

internal sealed class RomRebuildView : UserControl
{
    private readonly GameRomRebuildService _service;
    private readonly ComboBox _kind;
    private readonly TextBlock _detail;
    private readonly Button _run;
    private bool _busy;
    internal RomRebuildView(GameProjectSession project)
    {
        _service=new GameRomRebuildService(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};
        Content=root;
        var panel=new StackPanel();
        root.Children.Add(panel);
        panel.Children.Add(new TextBlock
        {
            Text="Rebuild · Reconstrucción de archivos ROM",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")
        });
        panel.Children.Add(new TextBlock
        {
            Text="Reutiliza los empaquetadores originales de pk3DS. Prepara la salida en una carpeta temporal " +
                "y verifica su SHA-256 antes de moverla al destino. Nunca reemplaza archivos existentes ni escribe el proyecto fuente.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,8,0,16)
        });
        _kind=new ComboBox{MinWidth=310,HorizontalAlignment=System.Windows.HorizontalAlignment.Left,
            ItemsSource=new[]{"RomFS (.romfs)", "ExeFS (.exefs)", ".3DS completo", "Trimmed .3DS"},
            SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
        panel.Children.Add(_kind);
        _run=new Button{Content="Seleccionar salida y reconstruir…",
            HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};
        _run.Click+=async(_,_)=>await BuildAsync();
        panel.Children.Add(_run);
        var card=new Border{Background=(Brush)FindResource("Panel"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(14)};
        panel.Children.Add(card);
        _detail=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Text="No se ha iniciado ninguna reconstrucción. Para .3DS se necesita ExHeader en la raíz extraída."};
        card.Child=_detail;
    }
    private async Task BuildAsync()
    {
        if(_busy)return;
        RomRebuildKind kind=(RomRebuildKind)_kind.SelectedIndex;
        var picker=new Microsoft.Win32.SaveFileDialog
        {
            Title="Guardar resultado de Rebuild",
            Filter=kind switch
            {
                RomRebuildKind.RomFS=>"RomFS|*.romfs",
                RomRebuildKind.ExeFS=>"ExeFS|*.exefs",
                _=>"ROM Nintendo 3DS|*.3ds"
            },
            FileName=kind switch
            {
                RomRebuildKind.RomFS=>"romfs.bin",
                RomRebuildKind.ExeFS=>"exefs.bin",
                RomRebuildKind.Trimmed3DS=>"Progressive_trimmed.3ds",
                _=>"Progressive.3ds"
            }
        };
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        if(File.Exists(picker.FileName))
        {
            StudioDialog.Error(Window.GetWindow(this),"Rebuild",
                "Seleccioná otro nombre: no se sobrescriben archivos existentes.");
            return;
        }
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Reconstruir ROM",
            $"Se reconstruirá {kind} con el motor pk3DS original en una ruta temporal. "+
            "Puede tardar varios minutos. ¿Continuar?"))return;
        _busy=true;_run.IsEnabled=false;_kind.IsEnabled=false;
        UiProgress.Begin("Rebuild · "+kind);
        try
        {
            var result=await _service.BuildAsync(kind,picker.FileName);
            _detail.Text=$"Reconstrucción terminada y verificada.\n"+
                $"Archivo: {result.File}\nTamaño: {result.Bytes:N0} bytes\n"+
                $"SHA-256: {result.Hash}\nLa carpeta fuente no fue modificada.";
            StudioDialog.Notice(Window.GetWindow(this),"Rebuild completado",
                "Archivo reconstruido con el empaquetador original y SHA-256 verificado.",result.File);
        }
        catch(Exception ex)
        {
            _detail.Text="La reconstrucción se detuvo. El archivo original sigue intacto: "+ex.Message;
            StudioDialog.Error(Window.GetWindow(this),"Falló Rebuild",ex.Message);
        }
        finally{_busy=false;_run.IsEnabled=true;_kind.IsEnabled=true;UiProgress.End();}
    }
}
