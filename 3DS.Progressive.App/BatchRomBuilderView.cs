using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using UserControl=System.Windows.Controls.UserControl;
using UniformGrid=System.Windows.Controls.Primitives.UniformGrid;

namespace ThreeDS.Progressive;

/// <summary>Batch ROM Builder panel: native dark UI; original Gen7 engine in a separate hidden process.</summary>
internal sealed class BatchRomBuilderView : UserControl
{
    private readonly BatchRomExecution _engine;
    private readonly GameProjectSession _project;
    private readonly TextBox _template,_destination,_baseName,_count;
    private readonly CheckBox _trimmed,_restore,_keepProjects,_allowMismatch;
    private readonly TextBlock _summary,_progress;
    private readonly Button _build;
    private bool _busy;
    private RomBatchOptions? _ready;
    internal BatchRomBuilderView(GameProjectSession project,string? suggestedTemplate=null)
    {
        _project=project;_engine=new BatchRomExecution(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};
        Content=root;
        var title=new StackPanel{Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        title.Children.Add(new TextBlock{Text="Batch ROM Builder · Constructor por lotes",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        title.Children.Add(new TextBlock
        {
            Text="Genera ROMs .3ds a partir de copias independientes. Cada una utiliza una semilla distinta " +
                "y reproduce el Global Template con el motor completo de pk3DS Progressive.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,5,0,3)
        });
        var buttons=new StackPanel();
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        _summary=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,9)};
        buttons.Children.Add(_summary);
        _progress=new TextBlock{Foreground=(Brush)FindResource("TextMain"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,7)};
        buttons.Children.Add(_progress);
        _allowMismatch=new CheckBox
        {
            Content="Comprendo que los SHA-256 de algunos assets no coinciden; continuar con los archivos actuales",
            Visibility=Visibility.Collapsed,Foreground=(Brush)FindResource("TextMain"),
            Margin=new Thickness(0,5,0,10)
        };
        _allowMismatch.Checked+=(_,_)=>_build.IsEnabled=_ready is not null;
        _allowMismatch.Unchecked+=(_,_)=>_build.IsEnabled=false;
        buttons.Children.Add(_allowMismatch);
        var bar=new WrapPanel();
        buttons.Children.Add(bar);
        Add(bar,"Comprobar y previsualizar",Preview);
        _build=Add(bar,"Construir ROMs",()=>_ = BuildAsync());_build.IsEnabled=false;
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(scroll);
        var content=new StackPanel{Margin=new Thickness(0,0,0,8)};
        scroll.Content=content;
        var settings=new Border{BorderBrush=(Brush)FindResource("Stroke"),
            Background=(Brush)FindResource("Panel"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(15)};
        content.Children.Add(settings);
        var fields=new StackPanel();settings.Child=fields;
        void Label(string text)=>fields.Children.Add(new TextBlock{Text=text,
            Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,5)});
        Label("Global Template JSON · compatible con DEFINITIVO.json");
        var fileRow=new DockPanel();
        fields.Children.Add(fileRow);
        var browse=Add(fileRow,"Elegir…",()=>
        {
            var chooser=new Microsoft.Win32.OpenFileDialog{Filter="Global ROM Template|*.json"};
            if(chooser.ShowDialog(Window.GetWindow(this))==true)_template.Text=chooser.FileName;
        });
        DockPanel.SetDock(browse,Dock.Right);
        _template=new TextBox{Text=suggestedTemplate??"",MinHeight=34};
        fileRow.Children.Add(_template);
        _template.TextChanged+=(_,_)=>Invalidate();
        Label("Carpeta de salida · fuera del proyecto de origen");
        var outputRow=new DockPanel();
        fields.Children.Add(outputRow);
        var outputButton=Add(outputRow,"Elegir…",()=>
        {
            using var browser=new System.Windows.Forms.FolderBrowserDialog
            {Description="Elegí la carpeta destino de los .3ds."};
            if(browser.ShowDialog()==System.Windows.Forms.DialogResult.OK)
                _destination.Text=browser.SelectedPath;
        });
        DockPanel.SetDock(outputButton,Dock.Right);
        _destination=new TextBox{Text=Path.Combine(Directory.GetParent(project.ProjectRoot)?.FullName
            ??project.ProjectRoot,Path.GetFileName(project.ProjectRoot)+"_BatchROMs"),MinHeight=34};
        outputRow.Children.Add(_destination);
        _destination.TextChanged+=(_,_)=>Invalidate();
        var two=new UniformGrid{Columns=2,Margin=new Thickness(0,9,0,6)};
        fields.Children.Add(two);
        var nameGroup=new StackPanel{Margin=new Thickness(0,0,12,0)};
        two.Children.Add(nameGroup);
        nameGroup.Children.Add(new TextBlock{Text="Nombre base",Foreground=(Brush)FindResource("TextSoft")});
        _baseName=new TextBox{Text="Progressive_Randomized",MinHeight=34,Margin=new Thickness(0,5,0,0)};
        nameGroup.Children.Add(_baseName);_baseName.TextChanged+=(_,_)=>Invalidate();
        var countGroup=new StackPanel();two.Children.Add(countGroup);
        countGroup.Children.Add(new TextBlock{Text="Número de ROMs (1–50)",
            Foreground=(Brush)FindResource("TextSoft")});
        _count=new TextBox{Text="4",MinHeight=34,Margin=new Thickness(0,5,0,0)};
        countGroup.Children.Add(_count);_count.TextChanged+=(_,_)=>Invalidate();
        _trimmed=new CheckBox{Content="Trimmed .3DS (reducir tamaño)",Margin=new Thickness(0,9,0,7)};
        _trimmed.Click+=(_,_)=>Invalidate();fields.Children.Add(_trimmed);
        _restore=new CheckBox{Content="Restaurar los backups originales en cada copia limpia",
            IsChecked=true,Margin=new Thickness(0,0,0,7)};
        _restore.Click+=(_,_)=>Invalidate();fields.Children.Add(_restore);
        _keepProjects=new CheckBox{Content="Conservar también los proyectos extraídos editables",
            Margin=new Thickness(0,0,0,7)};
        _keepProjects.Click+=(_,_)=>Invalidate();fields.Children.Add(_keepProjects);
        var warning=new TextBlock
        {
            Text="La ROM original permanece intacta. Se rechazan destinos existentes y assets con hash diferente. " +
                "Si falla una ROM, las ya completadas permanecen junto a su informe.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,13,0,0)
        };
        fields.Children.Add(warning);
        _summary.Text="Revisá la ruta del template y generá una vista previa antes de construir.";
        if(!_engine.BackendAvailable)_progress.Text=
            "El ejecutor original no está empaquetado. El botón Construir solo se habilitará al incluir LegacyBatch.";
    }
    private Button Add(System.Windows.Controls.Panel target,string label,Action run)
    {
        var b=new Button{Content=label,Margin=new Thickness(0,0,9,7)};
        b.Click+=(_,_)=>{try{run();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(this),"Batch ROM Builder",ex.Message);}};
        target.Children.Add(b);return b;
    }
    private void Invalidate()
    {
        _ready=null;
        if(_allowMismatch is not null)
        {
            _allowMismatch.IsChecked=false;
            _allowMismatch.Visibility=Visibility.Collapsed;
        }
        if(_build is not null)_build.IsEnabled=false;
    }
    private RomBatchOptions Settings()
    {
        if(!int.TryParse(_count.Text,out int n)||n is <1 or >50)
            throw new ArgumentException("El número de ROMs debe estar entre 1 y 50.");
        return new(_template.Text.Trim(),_destination.Text.Trim(),_baseName.Text.Trim(),n,
            _trimmed.IsChecked==true,_restore.IsChecked==true,_keepProjects.IsChecked==true);
    }
    internal void SelectTemplate(string path){_template.Text=path;Invalidate();}
    private void Preview()
    {
        var cfg=Settings();
        var (document,warnings)=_engine.Preflight(cfg);
        _summary.Text=$"Template: {document.Name} · formato {document.Version} · " +
            $"cobertura {document.Coverage} · {document.Actions.Count} acciones originales · " +
            $"{cfg.Count} ROMs · {(cfg.Trimmed?"Trimmed":"Full")} · " +
            $"SHA receta {document.Sha256[..14]}…";
        var blocking=warnings.Where(w=>!BatchRomExecution.IsHashMismatch(w)).ToArray();
        if(blocking.Length>0)
        {
            _progress.Text="Preflight detenido: "+string.Join(" | ",blocking);
            _ready=null;_build.IsEnabled=false;_allowMismatch.Visibility=Visibility.Collapsed;
            return;
        }
        if(warnings.Count>0)
        {
            _progress.Text="Advertencia SHA-256: "+string.Join(" | ",warnings)+
                " · Para construir, marcá la autorización explícita de usar los archivos actuales.";
            _allowMismatch.IsChecked=false;
            _allowMismatch.Visibility=Visibility.Visible;
            _ready=cfg with{AllowAssetMismatch=true};
            _build.IsEnabled=false;return;
        }
        _allowMismatch.Visibility=Visibility.Collapsed;
        _ready=cfg;_build.IsEnabled=true;
        _progress.Text="Validación previa correcta. La creación solo comienza al pulsar Construir ROMs.";
    }
    private async Task BuildAsync()
    {
        if(_busy||_ready is not {} options)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Construir ROMs",
            $"Se crearán {options.Count} ROMs con semillas distintas en {options.OutputDirectory}. "+
            "El proyecto de origen no se sobrescribe. ¿Continuar?"))return;
        _busy=true;IsEnabled=false;
        UiProgress.Begin("Batch ROM Builder · creando copias aisladas…");
        try
        {
            var manifest=await _engine.RunAsync(options,message=>
                Dispatcher.BeginInvoke(()=>_progress.Text=message));
            _summary.Text=$"Batch completado · {manifest.Roms.Count} ROMs · " +
                $"informe SHA-256: {options.BaseName}_batch-manifest.json";
            _progress.Text="ROMs generadas y verificadas. Semillas e historial en la carpeta de salida.";
            StudioDialog.Notice(Window.GetWindow(this),"Batch ROM Builder",
                $"Construidas y verificadas {manifest.Roms.Count} ROMs.",options.OutputDirectory);
        }
        catch(Exception ex)
        {
            StudioDialog.Error(Window.GetWindow(this),"Falló el Batch ROM Builder",ex.Message);
            _progress.Text="La ejecución se detuvo; revisá el log y el informe de errores de la carpeta de salida.";
        }
        finally{_busy=false;IsEnabled=true;Invalidate();UiProgress.End();}
    }
}
