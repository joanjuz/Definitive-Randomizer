using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Services.Switch;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using UserControl=System.Windows.Controls.UserControl;
namespace ThreeDS.Progressive;
internal sealed class SwitchAutomationView:UserControl
{
    private readonly SwordShieldProject _project;private readonly Action _changed;private readonly bool _batch;
    private readonly TextBox _summary=new(){IsReadOnly=true,AcceptsReturn=true,VerticalContentAlignment=VerticalAlignment.Top,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly TextBox _count=new(){Text="1",Width=90},_name=new(){Text="Definitive-SWSH",MinWidth=200};
    private readonly ComboBox _layout=new(){ItemsSource=new[]{"Atmosphère · ROMFS","Ryujinx · ROMFS y runtime"},SelectedIndex=1,MinWidth=210};
    private readonly StackPanel _actions=new();private readonly Button _apply=new(){Content="Aplicar cambios",IsEnabled=false};
    private string? _loadedPath;private JsonObject? _document;private SwitchGlobalPreview? _preview;private CancellationTokenSource? _cancel;
    internal bool Busy{get;private set;}
    internal SwitchAutomationView(SwordShieldProject project,bool batch,Action changed)
    {
        _project=project;_batch=batch;_changed=changed;SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);top.Children.Add(new TextBlock{Text=batch?"Batch ROM Builder":"Global Template",FontSize=23,FontWeight=FontWeights.SemiBold});top.Children.Add(new TextBlock{Text=batch?"Genera mods independientes desde la extracción original y la receta. Cada ejecución usa semillas nuevas. Los cambios de sesión solo se incluyen si forman parte de la receta.":"Carga una receta de Switch o créala desde el historial. Previsualizar no cambia el proyecto; aplicar añade cada operación al historial. Los cambios manuales prevalecen y el runtime se prepara al final.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,14)});
        top.Children.Add(_actions);var buttons=new WrapPanel();_actions.Children.Add(buttons);
        void Add(string label,Action action,EditorPresentation.ActionRole role=EditorPresentation.ActionRole.Secondary){var b=new Button{Content=label,Margin=new Thickness(0,0,8,8)};EditorPresentation.StyleAction(b,role);b.Click+=(_,_)=>{try{action();}catch(Exception ex){Error(ex);}};buttons.Children.Add(b);}
        Add("Cargar Global Template…",Load);Add("Crear desde historial",()=>{var captured=project.CaptureGlobal();if(_document is null)_document=captured;else{var updated=(JsonObject)_document.DeepClone();foreach(string key in new[]{"Format","Version","TitleId","BuildId","RuntimeProvider","Actions"})updated[key]=captured[key]?.DeepClone();_document=updated;}Invalidate();ShowDocument();});Add("Exportar template…",Save);
        if(batch){var fields=new WrapPanel{Margin=new Thickness(0,6,0,12)};_actions.Children.Add(fields);void Field(string name,FrameworkElement control){var panel=new StackPanel{Margin=new Thickness(0,0,12,0)};panel.Children.Add(new TextBlock{Text=name,Margin=new Thickness(0,0,0,5)});System.Windows.Automation.AutomationProperties.SetName(control,name);panel.Children.Add(control);fields.Children.Add(panel);}Field("Cantidad · 1–50",_count);Field("Nombre base",_name);Field("Formato de exportación",_layout);Add("Generar mods…",()=>_ = BuildAsync(),EditorPresentation.ActionRole.Primary);}
        else{Add("Previsualizar cambios",()=>_ = PreviewAsync(),EditorPresentation.ActionRole.Primary);EditorPresentation.StyleAction(_apply,EditorPresentation.ActionRole.Primary);buttons.Children.Add(_apply);_apply.Click+=(_,_)=>{try{if(_preview is null)return;project.ApplyGlobal(_preview);Invalidate();_changed();_status.Text="Receta aplicada al proyecto. Cada operación está en su historial; usa Exportar mod… cuando quieras.";}catch(Exception ex){Error(ex);}};}
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(_status);var cancel=new Button{Content="Cancelar trabajo",HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};EditorPresentation.StyleAction(cancel,EditorPresentation.ActionRole.Tertiary);cancel.Click+=(_,_)=>_cancel?.Cancel();bottom.Children.Add(cancel);root.Children.Add(_summary);System.Windows.Automation.AutomationProperties.SetName(_summary,"Resumen de Global Template de Switch");ShowDocument();
    }
    private void Error(Exception ex){_status.Text=ex.Message;StudioDialog.Error(Window.GetWindow(this),_batch?"Batch ROM Builder":"Global Template",ex.Message);}
    private void Invalidate(){_preview=null;_apply.IsEnabled=false;}
    private void ShowDocument(){if(_document is null){_summary.Text="Carga una receta o créala desde el historial.";return;}string Label(string? kind)=>kind switch{"personal.random"=>"Randomizar Pokémon","personal.manual"=>"Cambios manuales de Pokémon","data.random"=>"Randomizar datos","data.manual"=>"Ediciones manuales de tablas","world.manual"=>"Ediciones manuales de encuentros u objetos","gameplay.random"=>"Randomizar entrenadores, encuentros u objetos de campo","runtime.options"=>"Level caps, EXP y herramientas","runtime.remove"=>"Retirar configuración del runtime",_=>"Acción no reconocida"};_summary.Text=(_document["Name"]?.GetValue<string>()??"Global Template")+"\n\n"+string.Join("\n",(_document["Actions"] as JsonArray??new JsonArray()).Select((a,i)=>(i+1)+". "+Label(a?["Kind"]?.GetValue<string>()))); }
    private void Load(){var picker=new Microsoft.Win32.OpenFileDialog{Filter="Global Template de Switch (*.json)|*.json"};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;var loaded=SwordShieldProject.ReadGlobal(picker.FileName);if(loaded["Format"]?.GetValue<string>()!="DefinitiveRandomizer.SwitchGlobal"||loaded["Version"]?.GetValue<int>()!=1)throw new InvalidDataException("Selecciona una receta Global de Switch; no se convierten templates de 3DS.");_loadedPath=Path.GetFullPath(picker.FileName);_document=(JsonObject)loaded.DeepClone();Invalidate();ShowDocument();_status.Text="Template cargado. No se han aplicado cambios.";}
    private void Save(){if(_document is null)throw new InvalidOperationException("Carga o crea un template.");var picker=new Microsoft.Win32.SaveFileDialog{Filter="Global Template de Switch (*.json)|*.json",FileName="global-switch.json"};if(picker.ShowDialog(Window.GetWindow(this))==true){if(string.Equals(_loadedPath,Path.GetFullPath(picker.FileName),StringComparison.OrdinalIgnoreCase))throw new IOException("Exporta una copia con otro nombre; el template importado se conserva.");File.WriteAllText(picker.FileName,_document.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));}}
    private void SetBusy(bool value){Busy=value;_actions.IsEnabled=!value;}
    private async Task PreviewAsync()
    {
        if(Busy||_document is null)return;Invalidate();_cancel=new();SetBusy(true);try{var document=(JsonObject)_document.DeepClone();_status.Text="Preparando operaciones en una sesión aislada…";_preview=await Task.Run(()=>_project.PreviewGlobal(document,_cancel.Token));_apply.IsEnabled=_preview.Changes>0;_summary.Text=$"{_preview.Actions} operaciones · {_preview.Changes} cambios. No se han aplicado.\n\n"+_preview.Staged.GlobalHistorySummary(_project);int warnings=_preview.Staged.GameplayHistory.Skip(_project.GameplayHistory.Count).Sum(h=>h.Warnings?.Count??0);_status.Text="Vista previa preparada · "+warnings+" avisos. Revisa el detalle antes de aplicar.";}catch(Exception ex){Invalidate();Error(ex);}finally{SetBusy(false);_cancel.Dispose();_cancel=null;}
    }
    private async Task BuildAsync()
    {
        if(Busy||_document is null)return;if(!int.TryParse(_count.Text,out int count)||count is <1 or >50){Error(new ArgumentException("Cantidad entre 1 y 50."));return;}var folder=new Microsoft.Win32.OpenFolderDialog{Title="Carpeta de salida para los mods"};if(folder.ShowDialog(Window.GetWindow(this))!=true)return;var document=(JsonObject)_document.DeepClone();string name=_name.Text;var layout=_layout.SelectedIndex==0?SwitchModLayout.Atmosphere:SwitchModLayout.Ryujinx;_cancel=new();SetBusy(true);try{var progress=new Progress<string>(text=>_status.Text=text);var outputs=await Task.Run(()=>_project.BatchGlobal(document,count,folder.FolderName,name,layout,progress,_cancel.Token));_summary.Text=string.Join("\n",outputs.Select(o=>o.Folder+" · SHA256 manifiesto "+o.ManifestSha256));_status.Text="Mods terminados. La extracción y los cambios de sesión se conservan.";}catch(Exception ex){Error(ex);}finally{SetBusy(false);_cancel.Dispose();_cancel=null;}
    }
}
