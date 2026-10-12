using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>Same original recipe engine, WPF-native preview and atomic journal.</summary>
internal sealed class CroRecipesView : UserControl
{
    private readonly GameCroRecipeService _service;
    private readonly ListBox _files;
    private readonly TextBlock _summary;
    private readonly Button _apply;
    private CroRecipePreview? _preview;
    internal bool ConfirmLeave()=>_preview is null||StudioDialog.Confirm(Window.GetWindow(this),
        "Vista previa CRO pendiente","El resultado aún no se guardó. ¿Descartar la vista previa?");
    internal CroRecipesView(GameProjectSession project)
    {
        _service=new GameCroRecipeService(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var header=new StackPanel{Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="CRO Recipes · Recetas de mecánicas",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock{Text="Combina varios JSON de pk3DS Progressive en una sola propuesta " +
            "de Battle.cro. Revisa dependencias, relocaciones y tamaño antes de escribir; el historial " +
            "registra una única transacción verificable.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,6,0,9)});
        var actions=new WrapPanel();header.Children.Add(actions);
        Add(actions,"Añadir recetas JSON…",AddFiles);
        Add(actions,"Limpiar selección",()=>{_files.Items.Clear();Clear();});
        Add(actions,"Generar vista previa",Preview);
        _apply=Add(actions,"Aplicar recetas CRO",Apply);
        _apply.IsEnabled=false;
        _summary=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)};
        DockPanel.SetDock(_summary,Dock.Bottom);root.Children.Add(_summary);
        _files=new ListBox{Background=(Brush)FindResource("Panel"),
            Foreground=(Brush)FindResource("TextMain"),BorderBrush=(Brush)FindResource("Stroke"),
            BorderThickness=new Thickness(1)};
        root.Children.Add(_files);
        _summary.Text="Esperando archivos de recetas. No se ha modificado Battle.cro.";
    }
    private static Button Add(System.Windows.Controls.Panel panel,string text,Action action)
    {
        var button=new Button{Content=text,Margin=new Thickness(0,0,8,8)};
        button.Click+=(_,_)=>{try{action();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(button),"CRO Recipes",ex.Message);}};
        panel.Children.Add(button);return button;
    }
    private void Clear(){_preview=null;_apply.IsEnabled=false;}
    private void AddFiles()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog
        {Multiselect=true,Filter="CRO recipe JSON|*.json",Title="Seleccionar recetas CRO"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var paths=_files.Items.Cast<string>().Concat(dialog.FileNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        _files.Items.Clear();foreach(var p in paths)_files.Items.Add(p);
        Clear();
        _summary.Text=$"{paths.Length} recetas seleccionadas. Generá una vista previa.";
    }
    private void Preview()
    {
        UiProgress.Begin("Verificando recetas y calculando CRO propuesto…");
        try
        {
            _preview=_service.Prepare(_files.Items.Cast<string>());
            _apply.IsEnabled=!_preview.Original.AsSpan().SequenceEqual(_preview.Proposed);
            _summary.Text=_preview.Summary+Environment.NewLine+
                (_apply.IsEnabled?"Propuesta sin escribir. Confirmá para guardarla con historial.":
                    "Las recetas no producen diferencias; no hay nada que guardar.");
        }
        finally{UiProgress.End();}
    }
    private void Apply()
    {
        if(_preview is null)throw new InvalidOperationException("Generá primero una vista previa.");
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar recetas CRO",
            _preview.Summary+Environment.NewLine+"¿Guardar una sola transacción en Battle.cro?"))return;
        UiProgress.Begin("Aplicando recetas CRO y verificando historial…");
        try
        {
            _service.Apply(_preview);
            _summary.Text="Recetas CRO aplicadas y registradas en el historial.";
            _preview=null;_apply.IsEnabled=false;
        }
        finally{UiProgress.End();}
    }
}
