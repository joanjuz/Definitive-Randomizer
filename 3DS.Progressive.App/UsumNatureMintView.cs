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
    private readonly UsumMintIconService _iconService;
    private readonly TextBlock _report;
    private readonly TextBlock _diagnostic;
    private readonly Button _inspect,_install,_shops,_prepareIcons,_installIcons;
    private UsumMintPreview? _preview;
    private UsumMintIconPreview? _iconPreview;
    private bool _running;
    internal bool ConfirmLeave()=>!_running;
    internal UsumNatureMintView(GameProjectSession project,Action openShops)
    {
        _service=new(project);_iconService=new(project);Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="Mentas Gen7 — Ultra Sol / Ultra Luna",FontSize=23,
            FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain"),TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=UsumNatureMintService.Warning+".",TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,10,0,12),Foreground=(Brush)FindResource("TextMain")});
        heading.Children.Add(new TextBlock{Text="21 mentas consumibles. No se añaden automáticamente a tiendas ni pools de randomización. " +
            "Guarda un respaldo del proyecto. Deshacer restaura archivos del juego, no lo que hayas guardado en una partida.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,12)});
        heading.Children.Add(new UsumMintPoolControl(project));
        var buttons=new WrapPanel();heading.Children.Add(buttons);
        _inspect=Add("Comprobar estado y preparar",()=>_ = InspectAsync());
        _install=Add("Aplicar y guardar",()=>_ = InstallAsync());_install.IsEnabled=false;
        _shops=Add("Abrir Tiendas y BP",openShops);
        _prepareIcons=Add("Descargar y preparar iconos (PokéAPI)",()=>_ = PrepareIconsAsync());_prepareIcons.IsEnabled=false;
        _installIcons=Add("Instalar iconos preparados",()=>_ = InstallIconsAsync());_installIcons.IsEnabled=false;
        heading.Children.Add(new TextBlock{Text="Iconos opcionales: se descargan localmente de PokéAPI; © The Pokémon Company. No se incluyen en la aplicación. " +
            "La actualización conserva tiendas, precios y comportamiento de las mentas.",TextWrapping=TextWrapping.Wrap,
            Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,8)});
        _report=new TextBlock{Text="Comprobando el proyecto…",TextWrapping=TextWrapping.Wrap,
            Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,12,0,0)};
        _diagnostic=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft")};
        var status=new StackPanel();status.Children.Add(_report);
        status.Children.Add(new Expander{Header="Diagnóstico técnico",Content=_diagnostic,Margin=new Thickness(0,12,0,0)});
        root.Children.Add(new ScrollViewer{Content=status,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Loaded+=async(_,_)=>{if(_preview is null)await InspectAsync();};
        Button Add(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(0,0,8,8)};
            button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;}
    }
    private void Busy(bool value)
    {
        _running=value;_inspect.IsEnabled=!value;_shops.IsEnabled=!value;
        _install.IsEnabled=!value&&_preview?.Proposal is not null;
        _prepareIcons.IsEnabled=!value&&_preview?.State==UsumMintState.Installed&&_iconPreview?.State==UsumMintState.NotInstalled;
        _installIcons.IsEnabled=!value&&_iconPreview?.Proposal is not null;
    }
    private async Task InspectAsync()
    {
        if(_running)return;Busy(true);_preview=null;_iconPreview=null;
        try{_preview=await Task.Run(_service.Inspect);if(_preview.State==UsumMintState.Installed)_iconPreview=await Task.Run(_iconService.Inspect);Show();}
        catch(Exception ex){_preview=new(UsumMintState.Incompatible,ex.Message,null);Show();}
        finally{Busy(false);}
    }
    internal static string Status(UsumMintPreview? preview)=>preview?.State switch{
        UsumMintState.NotInstalled=>"Listo para instalar\nLas 21 mentas superaron la instalación de prueba en una copia temporal. Pulsa Aplicar y guardar para conservar los cambios.",
        UsumMintState.Installed=>"Instalado correctamente\nLas 21 mentas están reconocidas. Puedes añadirlas desde Tiendas y BP.",
        _=>"Instalación no compatible\n"+preview?.Report};
    private void Show(){_report.Text=Status(_preview);_diagnostic.Text=_preview?.Report+
        (_iconPreview is null?"":"\n\nICONOS · "+_iconPreview.Report);}
    private async Task InstallAsync()
    {
        if(_running||_preview?.Proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Instalar mentas Gen7",UsumNatureMintService.Warning+".",
            _preview.Report+"\nLos cambios se registrarán juntos en el historial."))return;
        Busy(true);UiProgress.Begin("Instalando mentas Gen7 con transacción e historial…");
        try{await Task.Run(()=>_service.Apply(_preview));_preview=await Task.Run(_service.Inspect);_iconPreview=await Task.Run(_iconService.Inspect);Show();}
        catch(Exception ex){_preview=new(UsumMintState.Incompatible,ex.Message,null);Show();
            StudioDialog.Error(Window.GetWindow(this),"Mentas Gen7",ex.Message);}
        finally{UiProgress.End();Busy(false);}
    }
    private async Task PrepareIconsAsync()
    {
        if(_running)return;Busy(true);_iconPreview=null;UiProgress.Begin("Descargando y convirtiendo los 21 iconos de PokéAPI…");
        try{_iconPreview=await Task.Run(()=>_iconService.PrepareAsync());Show();}
        catch(Exception ex){_report.Text="No se modificó el proyecto. No se pudieron preparar los iconos: "+ex.Message;}
        finally{UiProgress.End();Busy(false);}
    }
    private async Task InstallIconsAsync()
    {
        if(_running||_iconPreview?.Proposal is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Instalar iconos de mentas", "Actualizar los 21 iconos de mentas Gen7",
            _iconPreview.Report+"\nLos dos archivos se registrarán juntos en el historial. Deshacer restaura sus bytes anteriores."))return;
        Busy(true);UiProgress.Begin("Instalando iconos de mentas con transacción e historial…");
        try{await Task.Run(()=>_iconService.Apply(_iconPreview));_preview=await Task.Run(_service.Inspect);_iconPreview=await Task.Run(_iconService.Inspect);Show();}
        catch(Exception ex){_iconPreview=null;_report.Text="Instalación de iconos no confirmada: "+ex.Message;StudioDialog.Error(Window.GetWindow(this),"Iconos de mentas",ex.Message);}
        finally{UiProgress.End();Busy(false);}
    }
}
