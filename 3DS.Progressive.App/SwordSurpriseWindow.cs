using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThreeDS.Progressive.Services.Switch;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using Label=System.Windows.Controls.Label;
namespace ThreeDS.Progressive;
internal sealed class SwordSurpriseWindow:Window
{
    private readonly SwordShieldProject _project;
    private readonly TextBox _seed=new(){Text="20261021"};
    private readonly TextBox _details=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly Button _preview=new(){Content="Previsualizar"},_apply=new(){Content="Añadir al mod",IsEnabled=false},_close=new(){Content="Cerrar",IsCancel=true};
    private SwordSurprisePreview? _pending;
    private bool _busy;
    internal SwordSurpriseWindow(SwordShieldProject project)
    {
        _project=project;Title="Combate sorpresa · Espada · experimental";Width=780;Height=590;MinWidth=450;MinHeight=350;MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");
        var root=new Grid{Margin=new Thickness(16)};Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();root.Children.Add(header);header.Children.Add(new TextBlock{Text=Title,FontSize=22,TextWrapping=TextWrapping.Wrap});
        header.Children.Add(new TextBlock{Text="La especie visible se conserva y la copia para batalla recibe otra especie base presente. La elección se mantiene para ese Pokémon visible mientras permanezca generado. Prototipo para Espada 1.3.2 en Ryujinx; requiere probar combate y captura. No se aplican cambios al abrir esta ventana.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        var label=new Label{Content="Semilla (0–4294967295)",Target=_seed};AutomationProperties.SetLabeledBy(_seed,label);header.Children.Add(label);header.Children.Add(_seed);
        _details.Text="Previsualiza para comprobar la versión y preparar los dos recursos ExeFS. La ROM original permanece en lectura.";AutomationProperties.SetName(_details,"Vista previa del módulo de combate sorpresa");Grid.SetRow(_details,1);root.Children.Add(_details);
        var footer=new WrapPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(footer,2);root.Children.Add(footer);
        foreach(var b in new[]{_preview,_apply,_close}){b.Margin=new Thickness(0,0,8,0);EditorPresentation.StyleAction(b,b==_apply?EditorPresentation.ActionRole.Primary:EditorPresentation.ActionRole.Secondary);footer.Children.Add(b);}
        _seed.TextChanged+=(_,_)=>{_pending=null;_apply.IsEnabled=false;};_preview.Click+=async(_,_)=>await PreviewAsync();_apply.Click+=(_,_)=>Apply();_close.Click+=(_,_)=>Close();Closing+=(_,e)=>{if(_busy)e.Cancel=true;};Loaded+=(_,_)=>_seed.Focus();
        NameScope.SetNameScope(this,new NameScope());RegisterName("SurpriseSeed",_seed);RegisterName("SurprisePreview",_preview);RegisterName("SurpriseApply",_apply);RegisterName("SurpriseDetails",_details);
    }
    internal async Task PreviewAsync()
    {
        if(_busy)return;
        try
        {
            if(!uint.TryParse(_seed.Text,NumberStyles.None,CultureInfo.InvariantCulture,out uint seed))throw new ArgumentException("La semilla debe estar entre 0 y 4294967295.");
            _busy=true;_pending=null;_preview.IsEnabled=_apply.IsEnabled=_seed.IsEnabled=_close.IsEnabled=false;_details.Text="Verificando el ejecutable y preparando el módulo…";
            _pending=await Task.Run(()=>_project.PreviewSurprise(seed));
            _details.Text=$"Espada 1.3.2 · semilla {seed} · {_pending.Result.Pool.Count} especies base presentes.\n\nSe añadirán exefs/main y exefs/subsdk9 al mod, junto con historial y hashes. El original queda intacto. No cambia main.npdm.\n\nSe preserva el modelo visible y se prepara una especie distinta para batalla; forma base y nivel conservado. Los movimientos se solicitan al aprendizaje por nivel del nuevo Pokémon.\n\nEs un prototipo nativo pendiente de validación jugable: probar captura, huida, menús y eventos. Desactiva otros mods ExeFS al probar. Exportación solo para Ryujinx en esta fase.";
        }
        catch(Exception ex){_details.Text="No se pudo preparar el módulo.";StudioDialog.Error(this,"Combate sorpresa",ex.Message);}
        finally{_busy=false;_preview.IsEnabled=_seed.IsEnabled=_close.IsEnabled=true;_apply.IsEnabled=_pending is not null;}
    }
    private void Apply()
    {
        if(_pending is null||_busy)return;
        try{_project.ApplySurprise(_pending);_pending=null;_apply.IsEnabled=false;_details.Text="Combate sorpresa añadido al mod de la sesión. Cierra esta ventana y exporta el mod para Ryujinx desde la ventana principal del prototipo. Todavía no se ha modificado la extracción.";}
        catch(Exception ex){_pending=null;_apply.IsEnabled=false;StudioDialog.Error(this,"No se pudo añadir el módulo",ex.Message);}
    }
}
