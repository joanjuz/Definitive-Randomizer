using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal sealed class UsumNatureMintView : UserControl
{
    private readonly UsumNatureMintService _service;
    private readonly TextBlock _report;
    private readonly Button _inspect,_install,_shops;
    private UsumMintPreview? _preview;
    private bool _running;
    internal bool ConfirmLeave()=>!_running;
    internal UsumNatureMintView(GameProjectSession project,Action openShops)
    {
        _service=new(project);Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="Mentas Gen7 — Ultra Sol / Ultra Luna",FontSize=23,
            FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain"),TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=UsumNatureMintService.Warning+".",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,10,0,12),Foreground=(Brush)FindResource("TextMain")});
        heading.Children.Add(new TextBlock{Text="21 mentas consumibles. No se añaden automáticamente a tiendas ni pools de randomización. " +
            "Guarda un respaldo del proyecto. Deshacer restaura archivos del juego, no lo que hayas guardado en una partida.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,12)});
        var buttons=new WrapPanel();heading.Children.Add(buttons);
        _inspect=Add("Comprobar estado y preparar",()=>_ = InspectAsync());
        _install=Add("Instalar 21 mentas",()=>_ = InstallAsync());_install.IsEnabled=false;
        _shops=Add("Abrir Tiendas y BP",openShops);
        _report=new TextBlock{Text="Comprobando el proyecto…",TextWrapping=TextWrapping.Wrap,
            Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,12,0,0)};
        root.Children.Add(new ScrollViewer{Content=_report,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Loaded+=async(_,_)=>{if(_preview is null)await InspectAsync();};
        Button Add(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(0,0,8,8)};
            button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;}
    }
    private void Busy(bool value)
    {
        _running=value;_inspect.IsEnabled=!value;_shops.IsEnabled=!value;
        _install.IsEnabled=!value&&_preview?.Proposal is not null;
    }
    private async Task InspectAsync()
    {
        if(_running)return;Busy(true);_preview=null;
        try{_preview=await Task.Run(_service.Inspect);Show();}
        catch(Exception ex){_report.Text="No se modificó el proyecto: "+ex.Message;}
        finally{Busy(false);}
    }
    private void Show(){_report.Text=(_preview?.State switch{
        UsumMintState.NotInstalled=>"No instalado",UsumMintState.Installed=>"Instalado y reconocido",
        UsumMintState.Modified=>"Parcialmente reconocido / modificado externamente",_=>"Incompatible"})+"\n\n"+_preview?.Report;}
    private async Task InstallAsync()
    {
        if(_running||_preview?.Proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Instalar mentas Gen7",UsumNatureMintService.Warning+".",
            _preview.Report+"\nLos cambios se registrarán juntos en el historial."))return;
        Busy(true);UiProgress.Begin("Instalando mentas Gen7 con transacción e historial…");
        try{await Task.Run(()=>_service.Apply(_preview));_preview=await Task.Run(_service.Inspect);Show();}
        catch(Exception ex){_preview=null;_report.Text="Instalación no confirmada: "+ex.Message;
            StudioDialog.Error(Window.GetWindow(this),"Mentas Gen7",ex.Message);}
        finally{UiProgress.End();Busy(false);}
    }
}
