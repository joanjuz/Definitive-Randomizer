using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
using DataGrid=System.Windows.Controls.DataGrid;
using Label=System.Windows.Controls.Label;
namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldGameplayWindow:Window
{
    internal event Action? Changed;
    internal Action? EmbeddedClose {get;set;}
    internal Action<string>? EmbeddedNavigate {get;set;}
    internal bool IsWorking=>_busy;
    private Window DialogOwner=>IsVisible?this:Owner??this;
    private void CloseEditor(){if(EmbeddedClose is not null)EmbeddedClose();else Close();}
    internal FrameworkElement TakePage(){var page=(FrameworkElement)Content;void ClearCancel(DependencyObject node){if(node is Button b)b.IsCancel=false;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())ClearCancel(child);}ClearCancel(page);Content=null;return page;}
    internal void FocusSection(int option)
    {
        bool trainers=option==0,items=option==3;_sectionTitle.Text=trainers?"Entrenadores":items?"Field Items":"Encuentros";
        for(int i=0;i<5;i++)_options[i].Visibility=(trainers?i==0:items?i is 3 or 4:i is 1 or 2)?Visibility.Visible:Visibility.Collapsed;
        _trainerSettings.Visibility=trainers?Visibility.Visible:Visibility.Collapsed;_bstSettings.Visibility=items?Visibility.Collapsed:Visibility.Visible;_options[5].Visibility=items?Visibility.Collapsed:Visibility.Visible;
    }
    private readonly SwordShieldProject _project;
    private readonly CheckBox[] _options = { new(){Content="Entrenadores"},new(){Content="Encuentros visibles"},new(){Content="Encuentros ocultos"},new(){Content="Field Items visibles"},new(){Content="Field Items ocultos"},new(){Content="Especies con BST similar (±20 %; más cercana si no hay candidatos)",IsChecked=true} };
    private readonly TextBox _seed=new(){Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture),IsReadOnly=true};
    private readonly TextBlock _status=new(){Text="Selecciona las secciones y genera una vista previa. La extracción permanece en lectura.",TextWrapping=TextWrapping.Wrap};
    private readonly DataGrid _grid=new(){IsReadOnly=true,AutoGenerateColumns=false,FrozenColumnCount=1,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=true};
    private readonly Button _prepare=new(){Content="Previsualizar"},_apply=new(){Content="Añadir al mod",IsEnabled=false},_close=new(){Content="Cerrar",IsCancel=true};
    private SwordShieldGameplayPreview? _preview;
    private bool _busy;
    private readonly Button _surprise=new(){Content="Runtime · encuentros, level caps y herramientas…",Margin=new Thickness(0,0,0,8)};
    internal SwordShieldGameplayWindow(SwordShieldProject project)
    {
        _project=project;Title="Espada/Escudo · entrenadores, encuentros y Field Items";Width=1050;Height=760;MinWidth=550;MinHeight=400;
        MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");
        var root=new Grid{Margin=new Thickness(16)};Content=root;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var top=new StackPanel();var scroll=new ScrollViewer{Content=top,MaxHeight=310,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(scroll);
        top.Children.Add(_sectionTitle);
        var seedLabel=new Label{Content="Semilla aleatoria",Target=_seed};AutomationProperties.SetLabeledBy(_seed,seedLabel);top.Children.Add(seedLabel);top.Children.Add(_seed);var reseed=new Button{Content="Nueva semilla aleatoria"};reseed.Click+=(_,_)=>_seed.Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture);top.Children.Add(reseed);
        foreach(var option in _options){top.Children.Add(option);option.Checked+=(_,_)=>Invalidate();option.Unchecked+=(_,_)=>Invalidate();}
        BuildProgressiveOptions(top);
        _seed.TextChanged+=(_,_)=>Invalidate();
        top.Children.Add(new TextBlock{Text="Las reglas de entrenadores se aplican sobre el equipo final. Los campos no seleccionados se conservan; se retira Gigantamax al sustituir especies. Los encuentros conservan niveles y probabilidades. Field Items: objetos comunes, equipables, de combate, Poké Balls, medicinas, bayas, tesoros e ingredientes; cada reemplazo difiere del original. Las MT, objetos clave y hashes desconocidos se conservan. No incluye incursiones, regalos, estáticos ni árboles de bayas.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        _surprise.IsEnabled=project.Identity.CanEdit;EditorPresentation.StyleAction(_surprise,EditorPresentation.ActionRole.Secondary);
        _surprise.Click+=(_,_)=>{if(EmbeddedNavigate is not null){EmbeddedNavigate("Runtime");return;}new SwordPokeFeurWindow(_project){Owner=DialogOwner}.ShowDialog();Invalidate();};top.Children.Add(_surprise);
        top.Children.Add(_status);AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);
        foreach(var column in new[]{("Ubicación","Location",3),("Campo","Field",1),("Antes","BeforeText",2),("Después","AfterText",2)})_grid.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new System.Windows.Data.Binding(column.Item2),Width=new DataGridLength(column.Item3,DataGridLengthUnitType.Star),MinWidth=75});
        DarkGridTheme.Apply(_grid);_grid.FrozenColumnCount=1;AutomationProperties.SetName(_grid,"Cambios pendientes de entrenadores, encuentros y Field Items");Grid.SetRow(_grid,1);root.Children.Add(_grid);
        var footer=new WrapPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(footer,2);root.Children.Add(footer);
        foreach(var button in new[]{_prepare,_apply,_close}){button.Margin=new Thickness(0,0,8,0);EditorPresentation.StyleAction(button,button==_apply?EditorPresentation.ActionRole.Primary:EditorPresentation.ActionRole.Secondary);footer.Children.Add(button);}
        _prepare.Click+=async(_,_)=>await PreviewAsync();_apply.Click+=(_,_)=>Apply();_close.Click+=(_,_)=>CloseEditor();Closing+=(_,e)=>{if(_busy)e.Cancel=true;};
        NameScope.SetNameScope(this,new NameScope());RegisterName("SwitchGameplayPreview",_prepare);RegisterName("SwitchGameplayApply",_apply);RegisterName("SwitchGameplayGrid",_grid);RegisterName("SwitchGameplaySeed",_seed);RegisterName("SwitchGameplayDoubleChance",_doubleChance);RegisterName("SwitchGameplayRuntime",_surprise);
        for(int i=0;i<_options.Length;i++)RegisterName("SwitchGameplayOption"+i,_options[i]);
        Loaded+=(_,_)=>_seed.Focus();
    }
    private void Invalidate(){_preview=null;_apply.IsEnabled=false;_grid.ItemsSource=null;}
    private void Busy(bool busy){_busy=busy;_surprise.IsEnabled=!busy&&_project.Identity.CanEdit;_prepare.IsEnabled=_close.IsEnabled=_seed.IsEnabled=_trainerSettings.IsEnabled=_bstSettings.IsEnabled=!busy;foreach(var option in _options)option.IsEnabled=!busy;_apply.IsEnabled=!busy&&_preview?.Result.Changes.Count>0;}
    internal async Task PreviewAsync()
    {
        if(_busy)return;
        try
        {
            if(!uint.TryParse(_seed.Text,NumberStyles.None,CultureInfo.InvariantCulture,out uint seed))throw new ArgumentException("Semilla inválida.");
            if(_project.GameplayHistory.LastOrDefault()?.Options.Seed==seed){seed=SwitchSeed.New32();_seed.Text=seed.ToString(CultureInfo.InvariantCulture);}
            bool On(int i)=>_options[i].Visibility==Visibility.Visible&&_options[i].IsChecked==true;
            var options=new SwordShieldGameplayOptions(seed,On(0),On(1),On(2),On(3),On(4),_options[5].IsChecked==true){ProgressiveBst=_bstSettings.Visibility==Visibility.Visible&&_progressiveBst.IsChecked==true,BstRanges=ReadBstRanges(),RandomizeTrainerSpecies=_trainerSpecies.IsChecked==true,TrainerRules=ReadTrainerRules()};
            Invalidate();Busy(true);_status.Text="Leyendo y comprobando recursos…";
            _preview=await Task.Run(()=>_project.PreviewGameplay(options));_grid.ItemsSource=_preview.Result.Changes.Select(c=>new{Location=Location(c),c.Field,BeforeText=Describe(c,c.Before),AfterText=Describe(c,c.After)}).ToArray();
            _status.Text=$"{_preview.Result.EligibleSlots} ranuras elegibles · {_preview.Result.Changes.Count} cambios · {_preview.Result.SkippedItems} objetos/modelos conservados por seguridad. Todavía no se han añadido al mod.";
        }
        catch(Exception ex){_status.Text="No se pudo preparar la vista previa.";StudioDialog.Error(DialogOwner,"Vista previa",ex.Message);}
        finally{Busy(false);}
    }
    private string Describe(SwordShieldGameplayChange change,int id)=>change.Field switch { "Especie"=>$"{id:000} {_project.SpeciesName(id)}", "Objeto"=>$"{id:000} {_project.ItemName(id)}", _=>id.ToString(CultureInfo.InvariantCulture)};
    private string Location(SwordShieldGameplayChange change)
    {
        if(change.Resource.StartsWith("bin/trainer/",StringComparison.Ordinal)&&int.TryParse(System.IO.Path.GetFileNameWithoutExtension(change.Resource).Split('_').Last(),out int id))return _project.TrainerName(id)+" · "+change.Location;
        return change.Location;
    }
    private void Apply()
    {
        if(_busy||_preview is null)return;
        try{_project.ApplyGameplay(_preview);Changed?.Invoke();_status.Text="Cambios añadidos al mod de esta sesión. Exporta desde la ventana anterior para generar el mod completo.";Invalidate();}
        catch(Exception ex){Invalidate();StudioDialog.Error(DialogOwner,"No se pudo añadir al mod",ex.Message);}
    }
}
