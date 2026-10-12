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
    internal GlobalTemplateDocument? LoadedTemplate=>_loaded?.Clone();
    private string Game=>_project.GameConfiguration.USUM?"USUM":
        _project.GameConfiguration.SM?"SM":_project.GameConfiguration.ORAS?"ORAS":"XY";
    internal GlobalTemplateView(GameProjectSession project)
    {
        _project=project;Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(20)};
        Content=root;
        var header=new StackPanel{Margin=new Thickness(0,0,0,13)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Global Template · Compatible con pk3DS Progressive",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock
        {
            Text="Importá recetas v1 completas, incluidos rangos BST, Level Caps, parámetros y 35+ acciones. " +
                "El JSON original se conserva; no se aplica ninguna randomización al cargar.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,5,0,12)
        });
        var controls=new WrapPanel();
        header.Children.Add(controls);
        Add(controls,"Cargar template global…",Load);
        _save=Add(controls,"Guardar template global…",Save);
        Add(controls,"Crear desde historial",FromHistory);
        _name=new TextBox{MinWidth=260,Margin=new Thickness(0,7,0,9),Text="3DS Progressive"};
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
            ItemsSource=_actions
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
        var folders=new[]{AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"custom_balance_templates"),
            Path.GetDirectoryName(path)??"",Path.Combine(_project.ProjectRoot,"custom_balance_templates")};
        var warnings=doc.CheckAssets(folders);
        Use(doc,warnings.Count==0?"Template importado, sin modificar la ROM.":
            "Avisos de assets (no se reemplazaron): "+string.Join(" | ",warnings));
    }
    private void Load()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Global ROM Template JSON|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))==true)OpenPath(dialog.FileName);
    }
    private void FromHistory()
    {
        // Never invent the 9 legacy module settings. Users can use this as a
        // deliberately partial v1 recipe or begin with a complete original.
        var template=_loaded?.Clone()??GlobalTemplateDocument.Empty(Game,_project.Generation);
        template.MergeHistoryActions(_project.History);
        Use(template,"Configuración desde historial: se agregaron IDs registrados. " +
            "Los módulos RandSettings ausentes NO se reconstruyen ni se inventan. " +
            "Para paridad total, empezá importando un template global original.");
    }
    private void Save()
    {
        if(_loaded is null)throw new InvalidOperationException("Primero cargá o creá un template.");
        var dialog=new Microsoft.Win32.SaveFileDialog
        {Filter="Global ROM Template JSON|*.json",
            FileName="rom_template_"+Game.ToLowerInvariant()+".json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var export=_loaded.Clone();
        export.Rename(_name.Text);
        export.SelectActions(_actions.Where(a=>a.Selected).Select(a=>a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        export.Validate(Game,_project.Generation);
        export.Save(dialog.FileName);
        _status.Text=$"Guardado compatible v1: {export.Actions.Count} acciones; " +
            "parámetros, rangos, flags y assets no eliminados. Archivo fuente intacto.";
    }
    private sealed class ActionRow
    {
        public string Id{get;init;}="";
        public bool Selected{get;set;}
    }
}
