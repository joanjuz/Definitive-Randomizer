using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Services.Nds;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using ListBox=System.Windows.Controls.ListBox;
using Orientation=System.Windows.Controls.Orientation;
using TextBox=System.Windows.Controls.TextBox;
using Window=System.Windows.Window;

namespace ThreeDS.Progressive;

/// <summary>
/// Nintendo DS randomizer (Gen4 and Gen5): choose an original ROM, pick options, preview the changes and export a new ROM.
/// The source ROM is never modified; a preview is discarded as soon as any option changes.
/// </summary>
internal sealed class NdsRandomizerWindow : Window
{
    private static readonly (Gen5SpeciesMode Mode,string Label)[] Modes=
        [(Gen5SpeciesMode.Keep,"Sin cambios"),(Gen5SpeciesMode.Random,"Aleatorio"),(Gen5SpeciesMode.SimilarBst,"BST similar"),(Gen5SpeciesMode.ProgressiveBst,"BST progresivo por nivel")];
    private readonly TextBox _path=new(){IsReadOnly=true,Margin=new Thickness(0,0,8,0)};
    private readonly Button _choose=new(){Content="Elegir ROM…"};
    private readonly TextBlock _identity=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly ComboBox _trainers=ModeCombo(Gen5SpeciesMode.ProgressiveBst);
    private readonly ComboBox _wild=ModeCombo(Gen5SpeciesMode.SimilarBst);
    private static readonly (Gen5StarterMode Mode,string Label)[] StarterModes=[(Gen5StarterMode.Keep,"Sin cambios"),(Gen5StarterMode.Random,"Aleatorios"),(Gen5StarterMode.Custom,"Personalizados")];
    private readonly ComboBox _starters=new(){ItemsSource=StarterModes.Select(m=>m.Label).ToArray(),SelectedIndex=0};
    private static readonly (Gen5ItemMode Mode,string Label)[] ItemModes=[(Gen5ItemMode.Keep,"Sin cambios"),(Gen5ItemMode.Shuffle,"Barajar")];
    private readonly ComboBox _fieldItems=new(){ItemsSource=ItemModes.Select(m=>m.Label).ToArray(),SelectedIndex=0,MinWidth=260};
    private readonly ComboBox _shops=new(){ItemsSource=ItemModes.Select(m=>m.Label).ToArray(),SelectedIndex=0,MinWidth=260};
    private readonly ComboBox[] _starterPicks=[new(){MinWidth=150,IsEnabled=false,Margin=new Thickness(0,0,8,4)},new(){MinWidth=150,IsEnabled=false,Margin=new Thickness(0,0,8,4)},new(){MinWidth=150,IsEnabled=false,Margin=new Thickness(0,0,8,4)}];
    private readonly TextBox _seed=new(){Width=140};
    private readonly Button _newSeed=new(){Content="Nueva semilla",Margin=new Thickness(8,0,0,0)};
    private readonly Button _preview=new(){Content="Vista previa",IsEnabled=false};
    private readonly Button _export=new(){Content="Exportar ROM…",IsEnabled=false,Margin=new Thickness(0,0,8,0)};
    private readonly Button _close=new(){Content="Cerrar",IsCancel=true};
    private readonly TextBlock _summary=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,6)};
    private readonly ListBox _changes=new();
    private NdsGen5Session? _session;
    private NdsGen5Preview? _current;
    private bool _busy;

    internal NdsGen5Session? Session=>_session;
    internal NdsGen5Preview? CurrentPreview=>_current;
    internal string Status=>_summary.Text;
    internal bool CanExport=>_export.IsEnabled;

    internal NdsRandomizerWindow()
    {
        Title="Randomizer DS · Gen4 y Gen5 (beta)";MaxWidth=SystemParameters.WorkArea.Width-24;MaxHeight=SystemParameters.WorkArea.Height-24;
        Width=Math.Min(920,MaxWidth);Height=Math.Min(760,MaxHeight);MinWidth=Math.Min(560,Width);MinHeight=Math.Min(520,Height);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResizeWithGrip;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=(Brush)FindResource("Bg");Foreground=(Brush)FindResource("TextMain");
        // Body scrolls and the footer keeps its own row, so Export and Close stay reachable at the minimum size.
        var root=new Grid{Margin=new Thickness(20)};Content=root;root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);
        var heading=new StackPanel();scroll.Content=heading;
        var title=new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold};title.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==1)DragMove();};heading.Children.Add(title);
        heading.Children.Add(new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,14),
            Text="Elige un volcado original de Pokémon Diamante, Platino o HeartGold (USA), Negro (USA/EUR), Blanco (España) o Blanco 2 (USA/EUR). La ROM original no se modifica: se exporta una ROM nueva junto con un registro de cambios. "+
                 "Negro, Blanco y Blanco 2 son DSi-enhanced: la ROM exportada funciona en modo DS y en emuladores, no en modo DSi."});

        var source=new DockPanel();heading.Children.Add(Section("ROM original"));DockPanel.SetDock(_choose,Dock.Right);source.Children.Add(_choose);source.Children.Add(_path);heading.Children.Add(source);heading.Children.Add(_identity);
        heading.Children.Add(Section("Opciones"));
        var options=new Grid{Margin=new Thickness(0,0,0,4)};options.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});options.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        void Row(string label,FrameworkElement control){int row=options.RowDefinitions.Count;options.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var text=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,12,4)};Grid.SetRow(text,row);options.Children.Add(text);
            control.Margin=new Thickness(0,4,0,4);control.HorizontalAlignment=System.Windows.HorizontalAlignment.Left;Grid.SetRow(control,row);Grid.SetColumn(control,1);options.Children.Add(control);}
        _trainers.MinWidth=_wild.MinWidth=_starters.MinWidth=260;
        Row("Entrenadores",_trainers);Row("Pokémon salvajes",_wild);Row("Iniciales",_starters);
        var picks=new WrapPanel();foreach(var pick in _starterPicks)picks.Children.Add(pick);Row("Planta · Fuego · Agua",picks);
        Row("Objetos del mapa",_fieldItems);Row("Tiendas",_shops);
        var seedRow=new StackPanel{Orientation=Orientation.Horizontal};seedRow.Children.Add(_seed);seedRow.Children.Add(_newSeed);Row("Semilla",seedRow);
        heading.Children.Add(options);
        heading.Children.Add(new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,2,0,10),
            Text="BST progresivo: cada nivel tiene un rango de BST (nv. 1–10: 180–320 … nv. 51–100: 480–680). Los entrenadores pierden sus movimientos personalizados y usan los de nivel de la nueva especie. En cada área, la misma especie original se cambia por la misma especie nueva. "+
                 "Los iniciales cambian en la entrega, la pantalla de elección y los combates del rival que dependen de ellos. "+
                 "Barajar objetos y tiendas reparte los mismos objetos en otro orden: las MT, MO y objetos clave no se mueven, y las tiendas de MT y las de progreso no cambian."});
        var previewRow=new WrapPanel();previewRow.Children.Add(_preview);heading.Children.Add(previewRow);heading.Children.Add(_summary);

        _changes.Height=280;heading.Children.Add(_changes);
        var footer=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};Grid.SetRow(footer,1);root.Children.Add(footer);
        footer.Children.Add(_export);footer.Children.Add(_close);

        AutomationProperties.SetName(_path,"Ruta de la ROM original");AutomationProperties.SetName(_choose,"Elegir la ROM original de Nintendo DS");
        AutomationProperties.SetName(_trainers,"Modo de randomización de entrenadores");AutomationProperties.SetName(_wild,"Modo de randomización de Pokémon salvajes");
        AutomationProperties.SetName(_starters,"Modo de iniciales");AutomationProperties.SetName(_starterPicks[0],"Inicial de la posición Planta");
        AutomationProperties.SetName(_starterPicks[1],"Inicial de la posición Fuego");AutomationProperties.SetName(_starterPicks[2],"Inicial de la posición Agua");
        AutomationProperties.SetName(_fieldItems,"Objetos del mapa");AutomationProperties.SetName(_shops,"Tiendas");
        AutomationProperties.SetName(_seed,"Semilla");AutomationProperties.SetName(_newSeed,"Generar una semilla nueva");AutomationProperties.SetName(_preview,"Generar vista previa de cambios");
        AutomationProperties.SetName(_changes,"Cambios de la vista previa");AutomationProperties.SetName(_export,"Exportar ROM randomizada");AutomationProperties.SetName(_close,"Cerrar randomizer DS");
        EditorPresentation.StyleAction(_choose);EditorPresentation.StyleAction(_newSeed,EditorPresentation.ActionRole.Tertiary);EditorPresentation.StyleAction(_preview);
        EditorPresentation.StyleAction(_export,EditorPresentation.ActionRole.Primary);EditorPresentation.StyleAction(_close,EditorPresentation.ActionRole.Tertiary);

        NewSeed();
        _choose.Click+=async(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="ROM de Nintendo DS (*.nds)|*.nds",Title="ROM original de Pokémon para Nintendo DS"};if(picker.ShowDialog(this)==true)await LoadAsync(picker.FileName);};
        _newSeed.Click+=(_,_)=>NewSeed();
        _preview.Click+=async(_,_)=>await PreviewAsync();
        _export.Click+=async(_,_)=>{if(_current is null||_session is null)return;var picker=new Microsoft.Win32.SaveFileDialog{Filter="ROM de Nintendo DS (*.nds)|*.nds",FileName=Path.GetFileNameWithoutExtension(_session.SourcePath)+"-random-"+_current.Options.Seed+".nds",Title="Guardar ROM randomizada"};if(picker.ShowDialog(this)==true)await ExportAsync(picker.FileName);};
        _close.Click+=(_,_)=>Close();
        _trainers.SelectionChanged+=(_,_)=>Invalidate();_wild.SelectionChanged+=(_,_)=>Invalidate();_seed.TextChanged+=(_,_)=>Invalidate();
        _starters.SelectionChanged+=(_,_)=>{UpdateStarterPicks();Invalidate();};_fieldItems.SelectionChanged+=(_,_)=>Invalidate();_shops.SelectionChanged+=(_,_)=>Invalidate();foreach(var pick in _starterPicks)pick.SelectionChanged+=(_,_)=>Invalidate();
        Closing+=(_,e)=>{if(_busy)e.Cancel=true;};Closed+=(_,_)=>_session?.Dispose();
    }

    internal async Task LoadAsync(string path)
    {
        if(_busy)return;SetBusy(true);_summary.Text="Leyendo y verificando la ROM…";
        try
        {
            var session=await NdsGen5Session.OpenAsync(path);
            _session?.Dispose();_session=session;_path.Text=session.SourcePath;
            _identity.Text=$"{session.Game} · {(session.KnownDump?"volcado original verificado":"código reconocido, volcado no verificado")} · {session.TrainerCount} entrenadores · {session.AreaCount} áreas de encuentro";
            var names=session.SpeciesNames.Skip(1).Take(session.MaxSpecies).ToArray();
            for(int i=0;i<3;i++){_starterPicks[i].ItemsSource=names;_starterPicks[i].SelectedIndex=session.OriginalStarters[i]-1;}
            _summary.Text="ROM lista. Revisa las opciones y genera la vista previa.";
        }
        catch(Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            _summary.Text="No se puede usar esta ROM: "+ex.Message;
            if(!StudioDialog.AutomatedChecks)StudioDialog.Error(this,"Randomizer DS",ex.Message);
        }
        finally{Invalidate(keepStatus:true);SetBusy(false);}
    }

    internal async Task PreviewAsync()
    {
        if(_busy||_session is null)return;
        if(!uint.TryParse(_seed.Text.Trim(),out uint seed)){_summary.Text="La semilla debe ser un número entero entre 0 y 4294967295.";return;}
        var starters=StarterModes[_starters.SelectedIndex].Mode;
        var options=new Gen5RandomizerOptions(seed){Trainers=Modes[_trainers.SelectedIndex].Mode,Wild=Modes[_wild.SelectedIndex].Mode,Starters=starters,
            CustomStarters=starters==Gen5StarterMode.Custom?_starterPicks.Select(p=>p.SelectedIndex+1).ToArray():null,
            FieldItems=ItemModes[_fieldItems.SelectedIndex].Mode,Shops=ItemModes[_shops.SelectedIndex].Mode};
        SetBusy(true);_summary.Text="Generando vista previa…";
        try
        {
            var preview=await _session.PreviewAsync(options);
            _current=preview;var changes=preview.Result.Changes;
            _changes.ItemsSource=changes.Select(_session.Describe).ToArray();
            int species=changes.Count(c=>c.Field=="Especie");
            _summary.Text=changes.Count==0?"Sin cambios con estas opciones.":$"{changes.Count} cambios ({species} especies) · semilla {seed}. Nada se ha escrito todavía.";
        }
        catch(Exception ex) when (ex is ArgumentException or InvalidDataException or IOException)
        {
            _summary.Text="No se pudo generar la vista previa: "+ex.Message;
        }
        finally{SetBusy(false);}
    }

    internal async Task ExportAsync(string output)
    {
        if(_busy||_session is null||_current is null||_current.Result.Changes.Count==0)return;
        using var interaction=UiProgress.BlockInteraction();
        SetBusy(true);UiProgress.Begin("Randomizer DS · exportando ROM…");_summary.Text="Exportando y verificando la ROM nueva…";
        try
        {
            string log=await _session.ExportAsync(output,_current);
            _summary.Text=$"ROM exportada: {output}. Registro: {Path.GetFileName(log)}.";
            if(!StudioDialog.AutomatedChecks)StudioDialog.Notice(this,"Randomizer DS","ROM exportada y verificada.",output);
        }
        catch(Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            _summary.Text="No se pudo exportar: "+ex.Message;
            if(!StudioDialog.AutomatedChecks)StudioDialog.Error(this,"Randomizer DS",ex.Message);
        }
        finally{UiProgress.End();SetBusy(false);}
    }

    // Any option change discards the preview, so Export never writes something the user has not seen.
    private void Invalidate(bool keepStatus=false)
    {
        _current=null;_changes.ItemsSource=null;
        if(!keepStatus&&_session is not null)_summary.Text="Las opciones cambiaron: genera la vista previa de nuevo.";
        UpdateButtons();
    }

    private void SetBusy(bool busy){_busy=busy;_choose.IsEnabled=_trainers.IsEnabled=_wild.IsEnabled=_seed.IsEnabled=_newSeed.IsEnabled=!busy;_starters.IsEnabled=_fieldItems.IsEnabled=_shops.IsEnabled=!busy;_close.IsEnabled=!busy;UpdateStarterPicks();UpdateButtons();}

    private void UpdateStarterPicks(){bool custom=!_busy&&_session is not null&&StarterModes[_starters.SelectedIndex].Mode==Gen5StarterMode.Custom;foreach(var pick in _starterPicks)pick.IsEnabled=custom;}

    private void UpdateButtons(){_preview.IsEnabled=!_busy&&_session is not null;_export.IsEnabled=!_busy&&_current is {Result.Changes.Count:>0};}

    private void NewSeed()=>_seed.Text=((uint)Random.Shared.NextInt64(0,uint.MaxValue)).ToString();

    private TextBlock Section(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,FontSize=15,Margin=new Thickness(0,8,0,6)};

    private static ComboBox ModeCombo(Gen5SpeciesMode selected)
    {
        var combo=new ComboBox();foreach(var (_,label) in Modes)combo.Items.Add(label);
        combo.SelectedIndex=Array.FindIndex(Modes,m=>m.Mode==selected);return combo;
    }
}
