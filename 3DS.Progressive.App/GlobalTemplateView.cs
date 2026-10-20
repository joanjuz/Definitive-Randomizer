using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;
using Panel=System.Windows.Controls.Panel;
using DataGrid=System.Windows.Controls.DataGrid;
using Binding=System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

/// <summary>Dark in-app v1/coverage8 global template with lossless passthrough.</summary>
internal sealed class GlobalTemplateView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly ObservableCollection<ActionRow> _actions=[];
    private readonly TextBlock _status,_meta;
    private readonly TextBox _name;
    private readonly Button _save,_previewButton,_applyButton;
    private readonly StackPanel _previewPanel=new();
    private GlobalTemplatePreview? _preview;
    private bool _busy;
    private readonly Func<Task>? _afterApply;
    internal bool ConfirmLeave()=>!_busy&&!_loading;
    private void InvalidatePreview(){_preview?.Dispose();_preview=null;_applyButton.IsEnabled=false;_previewPanel.Children.Clear();}
    private GlobalTemplateDocument? _loaded;
    private readonly Func<GlobalTemplateDocument,Task>? _restoreAsync;
    private bool _loading;
    private readonly Action<GlobalTemplateDocument>? _restoreOptions,_captureOptions;
    internal GlobalTemplateDocument? LoadedTemplate=>_loaded?.Clone();
    internal string? LoadedSourcePath=>_loaded?.SourcePath;
    private string Game=>_project.GameConfiguration.USUM?"USUM":
        _project.GameConfiguration.SM?"SM":_project.GameConfiguration.ORAS?"ORAS":"XY";
    internal GlobalTemplateView(GameProjectSession project,Action<GlobalTemplateDocument>? restoreOptions=null,
        Action<GlobalTemplateDocument>? captureOptions=null,Func<GlobalTemplateDocument,Task>? restoreAsync=null,Func<Task>? afterApply=null)
    {
        _restoreAsync=restoreAsync;_afterApply=afterApply;
        _project=project;_restoreOptions=restoreOptions;_captureOptions=captureOptions;Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(20)};
        Content=root;
        var header=new StackPanel{Margin=new Thickness(0,0,0,13)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Global Template · Definitive Randomizer",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock
        {
            Text="Cargá una receta y recuperá las opciones guardadas de los editores compatibles. " +
                "Previsualizar ejecuta la receta en una copia; Aplicar traslada el mismo resultado al proyecto y registra cada acción. No reconstruye la ROM. Guardar incorpora tus opciones y las modificaciones manuales guardadas en el historial. Batch aplica primero la randomización y después los valores manuales. Cargar no modifica la ROM; conservá junto al JSON su carpeta .assets si contiene recursos adjuntos.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,5,0,12)
        });
        var controls=new WrapPanel();
        header.Children.Add(controls);
        Add(controls,"Cargar template global…",Load);
        _save=Add(controls,"Guardar template global…",Save,EditorPresentation.ActionRole.Primary);
        Add(controls,"Crear desde historial",FromHistory);
        if(project.Generation is 6 or 7)Add(controls,"Nueva receta nativa Gen"+project.Generation,()=>Use(NativeRecipeFactory.Create(_project,_captureOptions),"Receta creada sin aplicar cambios. Revisá las opciones en los editores y desmarcá las acciones que no quieras incluir antes de guardar."));
        _previewButton=Add(controls,"Previsualizar cambios",()=>_ = PreviewChangesAsync());
        _applyButton=Add(controls,"Aplicar cambios",()=>_ = ApplyChangesAsync(),EditorPresentation.ActionRole.Primary);
        _previewButton.IsEnabled=_applyButton.IsEnabled=false;
        _name=new TextBox{MinWidth=260,Margin=new Thickness(0,7,0,9),Text="Definitive Randomizer"};
        header.Children.Add(_name);
        _meta=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap};
        header.Children.Add(_meta);
        _status=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,6)};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        var content=new DockPanel();root.Children.Add(content);
        DockPanel.SetDock(_previewPanel,Dock.Bottom);content.Children.Add(_previewPanel);
        var grid=new DataGrid
        {
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,RowHeight=34,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            ItemsSource=_actions,
            GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),
            ColumnHeaderHeight=36,
            ColumnHeaderStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
            {
                Setters={
                    new Setter(System.Windows.Controls.Control.BackgroundProperty,(Brush)FindResource("Surface")),
                    new Setter(System.Windows.Controls.Control.ForegroundProperty,(Brush)FindResource("TextMain")),
                    new Setter(System.Windows.Controls.Control.BorderBrushProperty,(Brush)FindResource("Stroke")),
                    new Setter(System.Windows.Controls.Control.BorderThicknessProperty,new Thickness(0,0,1,1)),
                    new Setter(System.Windows.Controls.Control.PaddingProperty,new Thickness(9,7,9,7)),
                    new Setter(System.Windows.Controls.Control.FontWeightProperty,FontWeights.SemiBold)
                }
            }
        };
        grid.Columns.Add(new DataGridCheckBoxColumn{Header="Incluir en copia",Width=120,
            Binding=new Binding(nameof(ActionRow.Selected)){Mode=BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
        grid.Columns.Add(new DataGridTextColumn{Header="Acción original",Width=new DataGridLength(1,DataGridLengthUnitType.Star),
            IsReadOnly=true,Binding=new Binding(nameof(ActionRow.Display))});
        DarkGridTheme.Apply(grid);content.Children.Add(EditorPresentation.EmptyState(grid,"Sin receta cargada","Cargá un template global o crealo desde el historial."));
        Unloaded+=(_,_)=>{if(!_busy)InvalidatePreview();};
        _save.IsEnabled=false;
        _status.Text="Ninguna receta cargada. Seleccioná DEFINITIVO.json o creá un snapshot parcial desde el historial.";
    }
    private static Button Add(Panel panel,string title,Action action,EditorPresentation.ActionRole role=EditorPresentation.ActionRole.Secondary)
    {
        var b=new Button{Content=title,Margin=new Thickness(0,0,9,7)};
        b.Click+=(_,_)=>{try{action();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(b),"Global Template",ex.Message);}};
        EditorPresentation.StyleAction(b,role);panel.Children.Add(b);return b;
    }
    private void Use(GlobalTemplateDocument document,string? warning=null)
    {
        ProjectFeatureAvailability.ValidateActions(_project,document.Actions);
        InvalidatePreview();_loaded=document;_previewButton.IsEnabled=true;
        _actions.Clear();
        foreach(var id in document.Actions)_actions.Add(new ActionRow {Id=id,Selected=true,Changed=InvalidatePreview});
        _name.Text=document.Name;
        _meta.Text=$"Versión {document.Version} · Coverage {document.Coverage} · " +
            $"{document.SettingsCount} líneas RandSettings · {document.TrainerCapCount} Trainer Caps · " +
            $"{document.TrainerMoveRuleCount} Trainer Move Rules · {document.PlayerCapCount} Player Caps · " +
            $"{document.AssetCount} assets · {(document.Section("ProgressiveApp")?["ManualEdits"]?["Entries"]?.AsArray().Count??0)+ManualOperations.Read(document).Length} modificaciones exactas · {_actions.Count} acciones · SHA {document.Sha256[..12]}…";
        _save.IsEnabled=true;
        _status.Text=warning??"Receta validada y cargada en memoria. Nada se escribió en la ROM.";
    }
    internal void OpenPath(string path)
    {
        var doc=GlobalTemplateDocument.Load(path,Game,_project.Generation);
        ProjectFeatureAvailability.ValidateActions(_project,doc.Actions);
        var folders=new[]{AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"custom_balance_templates"),
            Path.GetDirectoryName(path)??"",Path.Combine(_project.ProjectRoot,"custom_balance_templates")};
        var warnings=doc.CheckAssets(folders);
        _restoreOptions?.Invoke(doc);
        Use(doc,warnings.Count==0?"Template importado. Opciones disponibles restauradas; ROM sin cambios.":
            "Avisos de assets (no se reemplazaron): "+string.Join(" | ",warnings));
    }
    internal async Task OpenPathAsync(string path)
    {
        if(_loading)return;
        _loading=true;IsEnabled=false;UiProgress.Begin("Cargando y comprobando Global Template…");
        try
        {
            string game=Game;int generation=_project.Generation;
            var prepared=await Task.Run(()=>
            {
                var document=GlobalTemplateDocument.Load(path,game,generation);
                ProjectFeatureAvailability.ValidateActions(_project,document.Actions);
                var folders=new[]{AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"custom_balance_templates"),Path.GetDirectoryName(path)??"",Path.Combine(_project.ProjectRoot,"custom_balance_templates")};
                return(Document:document,Warnings:document.CheckAssets(folders));
            });
            if(_restoreAsync is not null)await _restoreAsync(prepared.Document);else _restoreOptions?.Invoke(prepared.Document);
            Use(prepared.Document,prepared.Warnings.Count==0?"Template importado. Opciones restauradas; ROM sin cambios.":"Avisos de assets: "+string.Join(" | ",prepared.Warnings));
        }
        finally{_loading=false;IsEnabled=true;UiProgress.End();}
    }
    private async void Load()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Global ROM Template JSON|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{await OpenPathAsync(dialog.FileName);}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Global Template",ex.Message);}
    }
    private void FromHistory()
    {
        // Capture only visited editors; do not invent the other legacy modules.
        var applied=_project.History.Read().Entries.LastOrDefault(entry=>entry.Parameters.ContainsKey("GlobalTemplateSnapshot"));
        var template=_loaded?.Clone()??(applied is null?GlobalTemplateDocument.Empty(Game,_project.Generation):
            GlobalTemplateDocument.Parse(applied.Parameters["GlobalTemplateSnapshot"],Game,_project.Generation,
                string.IsNullOrEmpty(applied.Parameters.GetValueOrDefault("GlobalTemplateSource"))?null:applied.Parameters["GlobalTemplateSource"]));
        if(_loaded is null&&applied is null)
        {
            var extension=template.Section("ProgressiveApp") as System.Text.Json.Nodes.JsonObject??new();
            extension["PartialSnapshot"]=true;template.SetSection("ProgressiveApp",extension);
        }
        template.MergeHistoryActions(_project.History);
        _captureOptions?.Invoke(template);
        int missing=template.Section("ProgressiveApp")?["ManualEdits"]?["MissingHistoryPayloads"]?.AsArray().Count??0;
        Use(template,missing>0?$"Se detectaron {missing} modificaciones antiguas sin datos suficientes. Al guardar se indicarán las entradas que necesitás editar y guardar de nuevo.":"Se incorporaron las opciones disponibles y las modificaciones manuales. Estas se aplicarán al final de la randomización. Los módulos sin configuración no se inventan.");
    }
    private void Save()
    {
        if(_loaded is null)throw new InvalidOperationException("Primero cargá o creá un template.");
        var dialog=new Microsoft.Win32.SaveFileDialog
        {Filter="Global ROM Template JSON|*.json",
            FileName="rom_template_"+Game.ToLowerInvariant()+".json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var export=ExportSnapshot();
        export.Validate(Game,_project.Generation);
        export.Save(dialog.FileName);
        Use(GlobalTemplateDocument.Load(dialog.FileName,Game,_project.Generation));
        _status.Text=$"Guardado compatible v1: {export.Actions.Count} acciones; " +
            "opciones actualizadas, parámetros y assets conservados. Archivo fuente intacto.";
    }
    internal GlobalTemplateDocument ExportSnapshot()
    {
        if(_loaded is null)throw new InvalidOperationException("Primero cargá o creá un template.");
        var export=_loaded.Clone();
        ManualEditTemplate.Capture(export,_project.History.Read());
        _captureOptions?.Invoke(export);
        // Newly enabled editor actions were not present in the previously loaded list.
        // Keep explicit deselections for existing rows and include new settings.
        foreach(string id in export.Actions.Where(id=>!_actions.Any(row=>row.Id.Equals(id,StringComparison.OrdinalIgnoreCase))).ToArray())_actions.Add(new ActionRow{Id=id,Changed=InvalidatePreview,Selected=!id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase)||!_actions.Any(row=>row.Id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase))||_actions.Any(row=>row.Id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase)&&row.Selected)});
        export.Rename(_name.Text);
        export.SelectActions(_actions.Where(a=>a.Selected).Select(a=>a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        NativeRecipeCompletion.ReconcileSelection(export);
        NativeRecipeCompletion.TryComplete(_project,export);
        return export;
    }
    internal GlobalTemplateDocument ExecutionSnapshot()
    {
        if(_loaded is null)throw new InvalidOperationException("Cargá primero un Global Template.");
        var snapshot=_loaded.Clone();snapshot.Rename(_name.Text);
        snapshot.SelectActions(_actions.Where(row=>row.Selected).Select(row=>row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        NativeRecipeCompletion.ReconcileSelection(snapshot);return snapshot;
    }
    internal async Task PreviewChangesAsync()
    {
        if(_busy||_loading)return;
        _busy=true;IsEnabled=false;using var interaction=UiProgress.BlockInteraction();UiProgress.Begin("Global Template · preparando vista previa…");
        try
        {
            InvalidatePreview();var snapshot=ExecutionSnapshot();
            _preview=await new GlobalTemplateApplication(_project).PreviewAsync(snapshot,message=>Dispatcher.BeginInvoke(()=>_status.Text=message));
            _previewPanel.Children.Add(new TextBlock{Text=$"Vista previa: {_preview.Template.Actions.Count} acciones · {_preview.Files.Count} archivos modificados · Semilla {_preview.Seed}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
            var text=string.Join(Environment.NewLine,_preview.Files.Select(file=>$"{file.RelativePath} · {file.Bytes:N0} bytes · SHA {file.AfterSha256[..12]}…"));
            _previewPanel.Children.Add(new ScrollViewer{Content=new TextBlock{Text=text.Length==0?"Las acciones no producen diferencias binarias en el proyecto actual.":text,TextWrapping=TextWrapping.Wrap},MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            _applyButton.IsEnabled=true;_status.Text="Vista previa lista. El proyecto sigue intacto. Aplicar guardará exactamente este resultado, sin reconstruir la ROM.";
        }
        catch(Exception ex){_status.Text="No se preparó la vista previa: "+ex.Message;}
        finally{_busy=false;IsEnabled=true;UiProgress.End();}
    }
    internal async Task ApplyChangesAsync()
    {
        if(_busy||_preview is null)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar Global Template",$"{_preview.Template.Actions.Count} acciones · {_preview.Files.Count} archivos", "Se aplicará el resultado previsualizado al proyecto abierto. Cada acción quedará en el historial. La ROM se reconstruye después con Rebuild."))return;
        bool applied=false;_busy=true;IsEnabled=false;
        using(var interaction=UiProgress.BlockInteraction())
        {
            UiProgress.Begin("Global Template · aplicando cambios…");
            try
            {
                var current=ExecutionSnapshot();if(current.Sha256!=_preview.Template.Sha256)throw new InvalidOperationException("La receta cambió. Genera otra vista previa.");
                await Task.Run(()=>new GlobalTemplateApplication(_project).Apply(_preview));
                applied=true;_status.Text="Global Template aplicado. Acciones registradas en el historial. Ya puedes usar Rebuild.";InvalidatePreview();
            }
            catch(Exception ex){_status.Text="No se aplicó el template: "+ex.Message;InvalidatePreview();}
            finally{_busy=false;IsEnabled=true;UiProgress.End();}
        }
        if(applied&&_afterApply is not null)
        {
            try{await _afterApply();}
            catch(Exception ex){StudioDialog.Notice(Window.GetWindow(this),"Cambios aplicados","Los archivos y el historial se guardaron, pero no se pudo recargar la interfaz. Vuelve a abrir el proyecto antes de seguir editando.",ex.Message);}
        }
    }
    private sealed class ActionRow
    {
        public string Id{get;init;}="";
        public string Display=>Id==ManualEditTemplate.ActionId?"Modificaciones manuales · aplicar entradas y recursos al final":Id;
        public Action? Changed{get;init;}
        private bool _selected;
        public bool Selected{get=>_selected;set{if(value==_selected)return;_selected=value;Changed?.Invoke();}}
    }
}
