using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using ProgressBar=System.Windows.Controls.ProgressBar;
using Application=System.Windows.Application;
using CheckBox=System.Windows.Controls.CheckBox;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

internal sealed class GuidedRandomizationWindow:Window
{
    private readonly GameProjectSession _project;
    internal readonly GuidedRandomizationPlan Plan;
    private readonly DockPanel _root=new(){Margin=new Thickness(22)};
    private readonly StackPanel _page=new();
    private readonly TextBlock _heading=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _position=new(){Margin=new Thickness(0,7,0,12),TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly ProgressBar _progress=new(){Height=10,Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,0)};
    private readonly Button _back=new(){Content="Anterior",Margin=new Thickness(0,0,8,0)};
    private readonly Button _next=new(){Content="Configurar selección",Style=(Style)Application.Current.FindResource("PrimaryButton")};
    private readonly Button _export=new(){Content="Exportar configuración…",Margin=new Thickness(0,0,8,0),Visibility=Visibility.Collapsed};
    private readonly Dictionary<string,FrameworkElement> _inputs=[];
    private GuidedChoice[] _steps=[];
    private SpotlightTour? _helpTour;
    private bool _rangePage;
    private ProgressiveBstTable? _bstTable;
    internal static bool UsesBstRanges(GuidedChoice choice)=>choice.Groups is not null&&choice.Groups.SelectMany(g=>g.Fields).Any(f=>f.Key.StartsWith("BST."))&&(choice.Values.GetValueOrDefault("Progressive")==1||choice.Values.GetValueOrDefault("CHK_ProgressiveBST")==1);
    private int _step=-1;
    private bool _busy,_loading,_closed,_finished;
    private CancellationTokenSource? _loadCancellation;
    private GlobalTemplateDocument? _document;
    private TextBox? _output,_name;
    private string _folder="",_baseName="Randomizado";
    private CheckBox? _trim;
    private bool _trimmed=true;
    internal GuidedRandomizationWindow(GameProjectSession project)
    {
        Style=(Style)Application.Current.FindResource(typeof(Window));
        _project=project;Plan=new(project);Title="Randomizado guiado";Width=860;Height=700;MinWidth=360;MinHeight=340;
        MaxWidth=Math.Max(360,SystemParameters.WorkArea.Width-32);MaxHeight=Math.Max(340,SystemParameters.WorkArea.Height-32);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        _root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Bg");var host=new Grid();host.Children.Add(_root);Content=host;
        var footer=new DockPanel{Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);_root.Children.Add(footer);
        var buttons=new WrapPanel{HorizontalAlignment=System.Windows.HorizontalAlignment.Right};footer.Children.Add(buttons);
        var cancel=new Button{Content="Cancelar",Margin=new Thickness(0,0,8,0)};cancel.Click+=(_,_)=>Close();buttons.Children.Add(cancel);buttons.Children.Add(_export);buttons.Children.Add(_back);buttons.Children.Add(_next);
        var status=new StackPanel();DockPanel.SetDock(status,Dock.Bottom);_root.Children.Add(status);status.Children.Add(_status);status.Children.Add(_progress);
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);_root.Children.Add(top);top.Children.Add(_heading);top.Children.Add(_position);
        _root.Children.Add(new ScrollViewer{Content=_page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var help=new Button{Content="Ayuda de este paso",Margin=new Thickness(0,0,8,0)};buttons.Children.Insert(0,help);help.Click+=(_,_)=>ShowHelp();
        _back.Click+=async(_,_)=>await BackAsync();_next.Click+=async(_,_)=>await NextAsync();_export.Click+=(_,_)=>Export();
        Closed+=(_,_)=>{_closed=true;_loadCancellation?.Cancel();};
        PreviewKeyDown+=(_,e)=>{if(_helpTour is not null){if(e.Key==Key.Escape){e.Handled=true;_helpTour.Finish();}return;}if(e.Key==Key.Escape&&!_busy&&!_loading){e.Handled=true;Close();}else if(e.Key==Key.F1&&!_busy&&!_loading){e.Handled=true;ShowHelp();}};
        ShowSelection();
    }
    private void ShowHelp()
    {
        string route=_step>=0&&_step<_steps.Length?_steps[_step].Module.Key switch{"Personal"=>"Pokemon","Wild"=>"Encounters","Machines" or "Tutors"=>"MachineTutor","Pickup"=>"Pickup",var key=>key}:"Home";
        if(Content is not Grid host||_helpTour is not null)return;
        var topic=GuidanceCatalog.Find(route);
        SpotlightTour? tour=null;
        tour=new SpotlightTour(host,SpotlightTour.ForContent(_page,topic.Title,topic.Steps[0].Text),()=>{host.Children.Remove(tour!);_helpTour=null;});
        _helpTour=tour;host.Children.Add(tour);
    }
    private void Note(string text)=>_page.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
    internal void ShowSelection()
    {
        _step=-1;_page.Children.Clear();_inputs.Clear();_heading.Text="Elige qué quieres randomizar";
        _position.Text="Solo aparecerán los pasos que selecciones · "+_project.GameName;
        Note("Las opciones se guardan durante este recorrido. Puedes volver atrás; los archivos se procesan únicamente al confirmar el resumen final.");
        foreach(var choice in Plan.Choices)
        {
            var check=new CheckBox{Content=new TextBlock{Text=choice.Module.Title,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap},IsChecked=choice.Selected,Margin=new Thickness(0,8,0,4)};
            check.Checked+=(_,_)=>choice.Selected=true;check.Unchecked+=(_,_)=>choice.Selected=false;
            System.Windows.Automation.AutomationProperties.SetName(check,choice.Module.Title);_page.Children.Add(check);
            _page.Children.Add(new TextBlock{Text=choice.Module.Description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(23,0,0,8)});
        }
        _back.IsEnabled=false;_next.Content="Configurar selección";_export.Visibility=Visibility.Collapsed;
    }
    internal async Task NextAsync()
    {
        if(_busy||_loading)return;
        try
        {
            _status.Text="";
            if(_finished){Close();return;}
            if(_step==_steps.Length&&_step>=0){if(_document?.Actions.Count==0){Close();return;}await RunAsync();return;}
            if(_step<0)
            {
                _steps=Plan.Choices.Where(choice=>choice.Selected).ToArray();
                if(_steps.Length==0)throw new InvalidDataException("Elegí al menos una sección.");
                _step=0;
            }
            else
            {
                if(_steps[_step].Groups is null){await ShowStepAsync();return;}
                SaveFields();
                if(!_rangePage&&UsesBstRanges(_steps[_step])){ShowBstStep();return;}
                _rangePage=false;_step++;
            }
            if(_step==_steps.Length)
            {
                try{ShowSummary();}
                catch{_step=_steps.Length-1;_next.Content="Ver resumen";throw;}
            }
            else await ShowStepAsync();
        }
        catch(Exception ex){_status.Text=ex.Message;}
    }
    internal async Task BackAsync()
    {
        if(_busy||_loading||_step<0)return;
        try
        {
            _status.Text="";
            if(_step<_steps.Length){if(_steps[_step].Groups is not null)SaveFields();}else RememberOutput();
            if(_rangePage){_rangePage=false;await ShowStepAsync();return;}
            _step--;_document=null;
            if(_step<0)ShowSelection();else if(UsesBstRanges(_steps[_step]))ShowBstStep();else await ShowStepAsync();
        }
        catch(Exception ex){_status.Text=ex.Message;}
    }
    private async Task ShowStepAsync()
    {
        _rangePage=false;_bstTable=null;
        var choice=_steps[_step];_heading.Text=choice.Module.Title;_position.Text=$"Paso {_step+1} de {_steps.Length} · No se han aplicado cambios";
        _page.Children.Clear();_inputs.Clear();_export.Visibility=Visibility.Collapsed;_back.IsEnabled=true;
        _next.Content=_step==_steps.Length-1?"Ver resumen":"Siguiente";
        if(choice.Groups is null)
        {
            _loading=true;_root.IsEnabled=false;_status.Text="Cargando las opciones de esta sección…";
            var cancellation=new CancellationTokenSource();_loadCancellation=cancellation;var token=cancellation.Token;
            try
            {
                var editor=StudioDialog.AutomatedChecks?Plan.CreateEditor(choice.Module.Key):await Task.Run(()=>Plan.CreateEditor(choice.Module.Key,token),token);
                if(_closed||token.IsCancellationRequested)return;
                Plan.Initialize(choice.Module.Key,editor);
            }
            finally{if(ReferenceEquals(_loadCancellation,cancellation))_loadCancellation=null;cancellation.Dispose();_loading=false;if(!_closed)_root.IsEnabled=true;}
        }
        if(_closed)return;
        _status.Text="";Note(choice.Module.Description);
        if(choice.Module.Key=="Moves")Note("Elige tipos, categorías o ambos. El aprendizaje se configura en Level Up Moves; los templates de balance siguen disponibles en el modo completo.");
        if(choice.Module.Key=="Evolutions")Note("El asistente usa los motores originales. Los ajustes personalizados y los CSV siguen disponibles en el modo completo.");
        int index=0;
        foreach(var group in choice.Groups!)
        {
            if(choice.Module.Key=="Evolutions"&&group.Name=="Ajustes personalizados")continue;
            var body=new StackPanel{Margin=new Thickness(8,10,8,0)};
            foreach(var field in group.Fields)
            {
                if(field.Key.StartsWith("BST.",StringComparison.Ordinal))continue;
                if(choice.Module.Key=="LevelUp"&&_project.Generation==7&&field.Key=="CHK_HMs")continue;
                AddField(body,choice,field);
            }
            if(body.Children.Count>0)_page.Children.Add(new Expander{Header=group.Name,Content=body,IsExpanded=index++==0,Margin=new Thickness(0,0,0,10)});
        }
        if(choice.Module.Key=="Pickup")Note("Se usará el randomizador de Recogida de tu juego, conservando sus restricciones y probabilidades compatibles.");
        Dependencies(choice.Module.Key);
    }
    private void ShowBstStep()
    {
        var choice=_steps[_step];_rangePage=true;_page.Children.Clear();_inputs.Clear();_heading.Text=choice.Module.Title+" · rangos BST";
        _position.Text="Configura los intervalos antes de continuar · No se han aplicado cambios";
        Note("La tabla se usa solo mientras esté activado BST progresivo. Puedes añadir o quitar rangos y volver a las opciones sin perderlos.");
        var fields=choice.Groups!.SelectMany(g=>g.Fields).Where(f=>f.Key.StartsWith("BST.")).Select(f=>f with {Value=choice.Values.GetValueOrDefault(f.Key,f.Value)});
        _bstTable=new ProgressiveBstTable(fields,choice.Module.Key=="Wild"?999:1530);_page.Children.Add(_bstTable);
        _back.IsEnabled=true;_next.Content=_step==_steps.Length-1?"Ver resumen":"Siguiente";_export.Visibility=Visibility.Collapsed;
    }
    private void AddField(StackPanel body,GuidedChoice choice,EditorField field)
    {
        var cell=new StackPanel{Margin=new Thickness(0,0,0,13)};FrameworkElement input;
        int value=choice.Values[field.Key];
        var label=new TextBlock{Text=field.Label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5)};
        if(field.Choices is {} choices)
        {
            if(choice.Module.Key=="Evolutions"&&field.Key=="Operation")choices=choices.Where(item=>item.Id!=0).ToArray();
            input=new ComboBox{ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=value,MaxDropDownHeight=280};cell.Children.Add(label);
        }
        else if(field.Min==0&&field.Max==1)input=new CheckBox{Content=new TextBlock{Text=field.Label,TextWrapping=TextWrapping.Wrap},IsChecked=value==1};
        else
        {
            input=new TextBox{Text=(value/(decimal)field.Scale).ToString(CultureInfo.InvariantCulture)};cell.Children.Add(label);
        }
        System.Windows.Automation.AutomationProperties.SetName(input,field.Label);
        if(cell.Children.Contains(label))System.Windows.Automation.AutomationProperties.SetLabeledBy(input,label);
        if(input is TextBox)
        {
            string range=$"Rango: {field.Min/(decimal)field.Scale} – {field.Max/(decimal)field.Scale}";
            System.Windows.Automation.AutomationProperties.SetHelpText(input,range);input.ToolTip=range;
        }
        _inputs[field.Key]=input;cell.Children.Add(input);
        if(input is TextBox){var range=new TextBlock{Text=$"Rango: {field.Min/(decimal)field.Scale} – {field.Max/(decimal)field.Scale}",FontSize=12,Margin=new Thickness(0,4,0,0)};range.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");cell.Children.Add(range);}
        body.Children.Add(cell);
    }
    private void Dependencies(string module)
    {
        var pairs=module switch
        {
            "Personal"=>new[]{("Stats","Shuffle"),("Stats","Deviation"),("Types","SingleTypeChance"),("Abilities","WonderGuard"),("Abilities","BanTraps")},
            "LevelUp"=>new[]{("CHK_STAB","NUD_STAB"),("CHK_Expand","NUD_Moves"),("CHK_Spread","NUD_Level")},_=>Array.Empty<(string,string)>()
        };
        foreach(var (parent,child) in pairs)
            if(_inputs.GetValueOrDefault(parent) is CheckBox check&&_inputs.TryGetValue(child,out var dependent))
            {void Update()=>dependent.IsEnabled=check.IsChecked==true;check.Checked+=(_,_)=>Update();check.Unchecked+=(_,_)=>Update();Update();}
    }
    internal void SaveFields()
    {
        var choice=_steps[_step];var groups=choice.Groups??throw new InvalidOperationException("Todavía no se han cargado las opciones.");var values=new Dictionary<string,int>(choice.Values);
        foreach(var field in groups.SelectMany(group=>group.Fields))
        {
            if(!_inputs.TryGetValue(field.Key,out var control))continue;
            int value;
            if(control is CheckBox check)value=check.IsChecked==true?1:0;
            else if(control is ComboBox combo&&combo.SelectedValue is int selected)value=selected;
            else if(control is TextBox box&&decimal.TryParse(box.Text,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal number)&&number*field.Scale is var scaled&&scaled==decimal.Truncate(scaled)&&scaled>=int.MinValue&&scaled<=int.MaxValue)value=(int)scaled;
            else throw new ArgumentException(field.Label+": introduce un valor válido.");
            values[field.Key]=value;
        }
        if(_rangePage&&_bstTable is not null)
        {
            foreach(string key in values.Keys.Where(k=>k.StartsWith("BST.")).ToArray())values.Remove(key);
            foreach(var pair in _bstTable.Capture())values[pair.Key]=pair.Value;
            if(choice.Editor is GameEncounterService wild)wild.RestoreRangeOptions(values);
            // Refresh the existing metadata to include added/removed intervals before replay validation.
            if(choice.Editor is not null)choice.Groups=choice.Editor.RandomOptions;
            if(choice.Module.Key=="Trainers")
            {
                var basic=groups.Select(g=>g with {Fields=g.Fields.Where(f=>!f.Key.StartsWith("BST.")).ToArray()}).ToList();
                basic.Add(new("Rangos Progressive BST",values.Where(p=>p.Key.StartsWith("BST.")).Select(p=>new EditorField(p.Key,p.Key,p.Value,p.Key.EndsWith("FullRandom")?0:0,p.Key.EndsWith("FullRandom")?1:p.Key.EndsWith("Level")?100:1530)).ToArray()));choice.Groups=basic;
            }
        }
        var previous=choice.Values;choice.Values=values;
        try{if(GuidedRandomizationPlan.HasActions(choice))EditorValues.Validate(choice.Groups??groups,values);}catch{choice.Values=previous;throw;}
    }
    private void ShowSummary()
    {
        _document=Plan.BuildDocument();_page.Children.Clear();_inputs.Clear();_heading.Text="Revisa tu randomizado";_position.Text=$"{_steps.Length} secciones elegidas · {_document.Actions.Count} acciones · {_project.GameName}";
        Note("Se generará una ROM y se conservará su proyecto editable usando el constructor actual. La receta y el manifiesto quedarán junto al resultado.");
        _output=new TextBox{Text=_folder};_name=new TextBox{Text=_baseName};
        void Label(string text,FrameworkElement input){var caption=new TextBlock{Text=text,Margin=new Thickness(0,8,0,5)};_page.Children.Add(caption);_page.Children.Add(input);System.Windows.Automation.AutomationProperties.SetLabeledBy(input,caption);}
        Label("Carpeta de salida",_output);
        var browse=new Button{Content="Elegir carpeta…",HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,6,0,6)};_page.Children.Add(browse);
        browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="Carpeta para el randomizado guiado"};if(picker.ShowDialog(this)==true)_output.Text=picker.FolderName;};
        Label("Nombre del randomizado",_name);_trim=new CheckBox{Content="Trimmed .3DS",IsChecked=_trimmed,Margin=new Thickness(0,10,0,14)};_page.Children.Add(_trim);
        foreach(var choice in _steps.Where(GuidedRandomizationPlan.HasActions))
        {
            var text=new System.Text.StringBuilder();
            foreach(var group in choice.Groups!)
            {
                if(choice.Module.Key=="Evolutions"&&group.Name=="Ajustes personalizados")continue;
                foreach(var field in group.Fields)
                {
                    if(choice.Module.Key=="LevelUp"&&_project.Generation==7&&field.Key=="CHK_HMs")continue;
                    int value=choice.Values[field.Key];string display=field.Choices?.FirstOrDefault(item=>item.Id==value)?.Name??(field.Min==0&&field.Max==1?(value==1?"Sí":"No"):(value/(decimal)field.Scale).ToString(CultureInfo.InvariantCulture));
                    text.AppendLine(field.Label+": "+display);
                }
            }
            _page.Children.Add(new Expander{Header=choice.Module.Title,IsExpanded=false,Content=new TextBlock{Text=text.ToString(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12)},Margin=new Thickness(0,0,0,8)});
        }
        var skipped=_steps.Where(c=>!GuidedRandomizationPlan.HasActions(c)).ToArray();
        if(skipped.Length>0)Note("Sin cambios (se omiten): "+string.Join(", ",skipped.Select(c=>c.Module.Title)));
        _back.IsEnabled=true;_next.Content=_document.Actions.Count==0?"Cerrar sin cambios":"Generar ROM";_export.Visibility=Visibility.Visible;
    }
    private void RememberOutput(){if(_output is not null)_folder=_output.Text.Trim();if(_name is not null)_baseName=_name.Text.Trim();if(_trim is not null)_trimmed=_trim.IsChecked==true;}
    private void Export()
    {
        if(_document is null||_busy)return;
        var picker=new Microsoft.Win32.SaveFileDialog{FileName="randomizado-guiado.json",Filter="Global Template JSON|*.json"};
        if(picker.ShowDialog(this)!=true)return;
        try{File.WriteAllText(picker.FileName,_document.ToJson());_status.Text="Configuración exportada. Se puede reutilizar desde Global Template o Batch.";}catch(Exception ex){_status.Text=ex.Message;}
    }
    private async Task RunAsync()
    {
        if(_document is null)return;RememberOutput();
        if(!Directory.Exists(_folder))throw new DirectoryNotFoundException("Elegí una carpeta de salida existente.");
        if(string.IsNullOrWhiteSpace(_baseName)||_baseName.Contains("..")||_baseName.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Nombre de salida no válido.");
        string folder=Path.GetFullPath(_folder),root=Path.GetFullPath(_project.ProjectRoot);
        if(folder.Equals(root,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La salida debe estar fuera del proyecto de origen.");
        if(!StudioDialog.Confirm(this,"Generar randomizado guiado",$"{_steps.Length} secciones · {_document.Actions.Count} acciones", "Se creará una subcarpeta nueva en: "+folder))return;
        string output=Path.Combine(folder,_baseName+"_guiado_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]);
        using var interaction=UiProgress.BlockInteraction();_busy=true;_root.IsEnabled=false;_progress.Visibility=Visibility.Visible;_progress.IsIndeterminate=true;UiProgress.Begin("Randomizado guiado · comprobando configuración…");
        try
        {
            Directory.CreateDirectory(output);string template=Path.Combine(output,"randomizado-guiado.json");await File.WriteAllTextAsync(template,_document.ToJson());
            var execution=new BatchRomExecution(_project);
            var result=await execution.RunAsync(new(template,output,_baseName,1,_trimmed,false,true),snapshot=>
            {
                void Update(){if(_closed)return;_status.Text=snapshot.Stage+"\n"+snapshot.Detail;_progress.IsIndeterminate=false;_progress.Value=snapshot.OverallPercent;UiProgress.Report(snapshot.Stage,snapshot.OverallPercent);}
                if(Dispatcher.CheckAccess())Update();else Dispatcher.BeginInvoke((Action)Update);
            });
            var rom=result.Roms.Single();_status.Text="Randomizado terminado.\n"+rom.File+"\nSHA-256: "+rom.Sha256;_finished=true;_next.Content="Cerrar";_back.IsEnabled=false;_export.IsEnabled=false;
            StudioDialog.Notice(this,"Randomizado completado","ROM generada e integridad verificada.",rom.File);
        }
        catch(Exception ex){_status.Text="No se completó el randomizado: "+ex.Message+"\nLos detalles disponibles quedan en: "+output;}
        finally{_busy=false;_root.IsEnabled=true;_progress.Visibility=Visibility.Collapsed;UiProgress.End();}
    }
    internal bool ConfirmLeave()=>!_busy;
    protected override void OnClosing(CancelEventArgs e)
    {
        if(_busy){e.Cancel=true;_status.Text="La construcción sigue en curso. Espera a que termine antes de cerrar.";}
        else _loadCancellation?.Cancel();
        base.OnClosing(e);
    }
}
