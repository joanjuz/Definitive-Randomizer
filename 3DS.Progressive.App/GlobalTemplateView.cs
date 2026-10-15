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
    private readonly Button _save;
    private GlobalTemplateDocument? _loaded;
    private readonly Func<GlobalTemplateDocument,Task>? _restoreAsync;
    private bool _loading;
    private readonly Action<GlobalTemplateDocument>? _restoreOptions,_captureOptions;
    internal GlobalTemplateDocument? LoadedTemplate=>_loaded?.Clone();
    internal string? LoadedSourcePath=>_loaded?.SourcePath;
    private string Game=>_project.GameConfiguration.USUM?"USUM":
        _project.GameConfiguration.SM?"SM":_project.GameConfiguration.ORAS?"ORAS":"XY";
    internal GlobalTemplateView(GameProjectSession project,Action<GlobalTemplateDocument>? restoreOptions=null,
        Action<GlobalTemplateDocument>? captureOptions=null,Func<GlobalTemplateDocument,Task>? restoreAsync=null)
    {
        _restoreAsync=restoreAsync;
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
                "Guardar incorpora tus ajustes de entrenadores, estáticos, Pokémon, movimientos, MT, tutores, tiendas, objetos, evoluciones y movimientos huevo. Cargar no modifica la ROM.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,5,0,12)
        });
        var controls=new WrapPanel();
        header.Children.Add(controls);
        Add(controls,"Cargar template global…",Load);
        _save=Add(controls,"Guardar template global…",Save);
        Add(controls,"Crear desde historial",FromHistory);
        if(project.Generation is 6 or 7)Add(controls,"Nueva receta nativa Gen"+project.Generation,()=>Use(NativeRecipeFactory.Create(_project,_captureOptions),"Receta creada sin aplicar cambios. Revisá las opciones en los editores y desmarcá las acciones que no quieras incluir antes de guardar."));
        _name=new TextBox{MinWidth=260,Margin=new Thickness(0,7,0,9),Text="Definitive Randomizer"};
        header.Children.Add(_name);
        _meta=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap};
        header.Children.Add(_meta);
        _status=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,6)};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
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
            IsReadOnly=true,Binding=new Binding(nameof(ActionRow.Id))});
        root.Children.Add(grid);
        _save.IsEnabled=false;
        _status.Text="Ninguna receta cargada. Seleccioná DEFINITIVO.json o creá un snapshot parcial desde el historial.";
    }
    private static Button Add(Panel panel,string title,Action action)
    {
        var b=new Button{Content=title,Margin=new Thickness(0,0,9,7)};
        b.Click+=(_,_)=>{try{action();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(b),"Global Template",ex.Message);}};
        panel.Children.Add(b);return b;
    }
    private void Use(GlobalTemplateDocument document,string? warning=null)
    {
        ProjectFeatureAvailability.ValidateActions(_project,document.Actions);
        _loaded=document;
        _actions.Clear();
        foreach(var id in document.Actions)_actions.Add(new ActionRow {Id=id,Selected=true});
        _name.Text=document.Name;
        _meta.Text=$"Versión {document.Version} · Coverage {document.Coverage} · " +
            $"{document.SettingsCount} líneas RandSettings · {document.TrainerCapCount} Trainer Caps · " +
            $"{document.TrainerMoveRuleCount} Trainer Move Rules · {document.PlayerCapCount} Player Caps · " +
            $"{document.AssetCount} assets · {_actions.Count} acciones · SHA {document.Sha256[..12]}…";
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
        var template=_loaded?.Clone()??GlobalTemplateDocument.Empty(Game,_project.Generation);
        if(_loaded is null)
        {
            var extension=template.Section("ProgressiveApp") as System.Text.Json.Nodes.JsonObject??new();
            extension["PartialSnapshot"]=true;template.SetSection("ProgressiveApp",extension);
        }
        template.MergeHistoryActions(_project.History);
        _captureOptions?.Invoke(template);
        Use(template,"Se agregaron las acciones del historial y las opciones recordadas de los editores compatibles. " +
            "Los demás módulos ausentes siguen pendientes. Para una receta completa, empezá con un template original.");
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
        _captureOptions?.Invoke(export);
        // Newly enabled editor actions were not present in the previously loaded list.
        // Keep explicit deselections for existing rows and include new settings.
        foreach(string id in export.Actions.Where(id=>!_actions.Any(row=>row.Id.Equals(id,StringComparison.OrdinalIgnoreCase))).ToArray())_actions.Add(new ActionRow{Id=id,Selected=!id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase)||!_actions.Any(row=>row.Id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase))||_actions.Any(row=>row.Id.StartsWith("wild-encounters.",StringComparison.OrdinalIgnoreCase)&&row.Selected)});
        export.Rename(_name.Text);
        export.SelectActions(_actions.Where(a=>a.Selected).Select(a=>a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        NativeRecipeCompletion.ReconcileSelection(export);
        NativeRecipeCompletion.TryComplete(_project,export);
        return export;
    }
    private sealed class ActionRow
    {
        public string Id{get;init;}="";
        public bool Selected{get;set;}
    }
}
