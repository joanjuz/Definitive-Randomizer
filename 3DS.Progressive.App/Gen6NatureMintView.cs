using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal sealed class Gen6NatureMintView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly Gen6NatureMintService _service;
    private readonly Gen6MintIconService _icons;
    private readonly TextBlock _report;
    private readonly Button _inspect,_install,_installIcons,_shops;
    private Gen6MintPreview? _preview;
    private bool _busy;
    internal bool ConfirmLeave()=>!_busy;
    internal Gen6NatureMintView(GameProjectSession project,Action<string> navigate)
    {
        _project=project;_service=new(project,true);_icons=new(project);
        Background=(Brush)FindResource("Bg");
        var root=new StackPanel{Margin=new Thickness(20)};
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        root.Children.Add(new TextBlock{Text="Mentas Gen6 · Pokémon X/Y",FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain"),TextWrapping=TextWrapping.Wrap});
        root.Children.Add(new TextBlock{Text="21 mentas consumibles que cambian la naturaleza real y recalculan las estadísticas. Compatible con datos ya randomizados cuando el código nativo del parche permanece reconocido. X está aceptado; esta preview permite comprobar Y manualmente.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,10,0,12)});
        root.Children.Add(new TextBlock{Text="Instalar no randomiza ni reparte objetos. Los iconos son una operación independiente. Cada operación se guarda en Historial y se deshace en orden inverso. Rebuild debe generar una ROM nueva; utiliza una copia de tu guardado.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,12)});
        var buttons=new WrapPanel();root.Children.Add(buttons);
        _inspect=Add("Comprobar y preparar",()=>_ = InspectAsync());
        _install=Add("Instalar las 21 mentas",()=>_ = InstallAsync());
        _installIcons=Add("Instalar iconos corregidos",()=>_ = IconsAsync());
        _shops=Add("Añadir las 21 mentas a tiendas",()=>_ = ShopsAsync());
        Add("Abrir Historial",()=>navigate("History"));Add("Abrir Rebuild",()=>navigate("Rebuild"));
        root.Children.Add(new TextBlock{Text="Iconos: descarga local de PokéAPI; © The Pokémon Company. Se verifican sus huellas antes de convertirlos al formato de X/Y.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,8)});
        _report=new TextBlock{Text="Pulsa Comprobar y preparar para leer el estado y los archivos afectados.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,12,0,0)};root.Children.Add(_report);
        SetBusy(false);
        Loaded+=async(_,_)=>{if(_preview is null)await InspectAsync();};
        Button Add(string text,Action run){var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>run();buttons.Children.Add(b);return b;}
    }
    private void SetBusy(bool value)
    {
        _busy=value;_inspect.IsEnabled=!value;_install.IsEnabled=!value&&_preview?.Proposal is not null;
        _installIcons.IsEnabled=_shops.IsEnabled=!value&&_preview?.State==Gen6MintNativeState.Installed;
    }
    private async Task InspectAsync()
    {
        if(_busy)return;SetBusy(true);
        try{_preview=await Task.Run(_service.Inspect);_report.Text=_preview.Report;}
        catch(Exception ex){_preview=null;_report.Text=ex.Message;}
        finally{SetBusy(false);}
    }
    private async Task RunAsync(string title,Func<Task> operation)
    {
        if(_busy)return;SetBusy(true);using var interaction=UiProgress.BlockInteraction();UiProgress.Begin(title);
        try{int before=_project.History.Read().Entries.Count;await operation();_preview=await Task.Run(_service.Inspect);_report.Text=(_project.History.Read().Entries.Count>before?"Operación guardada en Historial.":"No se guardaron cambios.")+"\n"+_preview.Report;}
        catch(Exception ex){_report.Text=ex.Message;StudioDialog.Error(Window.GetWindow(this),"Mentas X/Y",ex.Message);}
        finally{UiProgress.End();SetBusy(false);}
    }
    private Task InstallAsync()
    {
        if(_preview?.Proposal is null)return Task.CompletedTask;
        var frozen=_preview;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Mentas X/Y","Instalar las 21 mentas",frozen.Report+"\nSe conservan los demás datos del proyecto. Deshacer está disponible en Historial."))return Task.CompletedTask;
        return RunAsync("Instalando mentas X/Y…",()=>Task.Run(()=>_service.Apply(frozen)));
    }
    private Task IconsAsync()=>RunAsync("Preparando los 21 iconos X/Y…",async()=>
    {
        var proposal=await Task.Run(()=>_icons.PrepareAsync());
        if(proposal.State==Gen6MintNativeState.Installed)return;
        if(proposal.Proposal is null)throw new InvalidOperationException(proposal.Report);
        if(StudioDialog.Confirm(Window.GetWindow(this),"Iconos X/Y","Instalar los 21 iconos corregidos",proposal.Report))await Task.Run(()=>_icons.Apply(proposal));
    });
    private Task ShopsAsync()=>RunAsync("Preparando tiendas con mentas X/Y…",async()=>
    {
        var service=new GameMart6Service(_project){Gen6MintInclusion=true};
        var proposal=await Task.Run(()=>
        {
            var rows=service.ReadAllItems();var ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
            for(int i=0;i<9;i++)rows[i]=rows[i].Concat(ids.Where(id=>!rows[i].Contains(id))).ToArray();
            var options=EditorValues.Flatten(service.RandomOptions);options["Randomize"]=0;options["Candies"]=0;options["EVItems"]=0;options["BanCapsule"]=0;
            return service.PrepareDraft(rows,options,Enumerable.Range(1,9).ToArray(),0);
        });
        if(StudioDialog.Confirm(Window.GetWindow(this),"Distribuir mentas","Añadir las 21 a las tiendas generales","Conserva los objetos existentes y añade cada menta que falte una sola vez. Solo se aplicará si el inventario cabe en el espacio verificado."))await Task.Run(()=>service.Apply(proposal));
    });
}
