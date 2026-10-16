using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>Native WPF UI for the original USUM Relearner patch engine.</summary>
internal sealed class UsumRelearnerView : UserControl
{
    private readonly GameUsumRelearnerService _service;
    private readonly TextBlock _report;
    private readonly Button _prepare,_apply;
    private RelearnerPreview? _preview;
    private bool _running;
    internal bool ConfirmLeave()=>_running?false:_preview is null||StudioDialog.Confirm(
        Window.GetWindow(this),"Relearner sin aplicar",
        "La vista previa se descartará al salir. ¿Continuar?");
    internal UsumRelearnerView(GameProjectSession project)
    {
        _service=new GameUsumRelearnerService(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};
        Content=root;
        var stack=new StackPanel();root.Children.Add(stack);
        stack.Children.Add(new TextBlock{Text="USUM Relearner · Recordador de movimientos",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        stack.Children.Add(new TextBlock
        {
            Text="Integra el parche original CAFE.asm + Learn.asm de pk3DS Progressive en Ultra Sun y Ultra Moon. " +
                "Habilita el recordador en las cafeterías y actualiza el tutorial de Lylia para los idiomas admitidos. " +
                "Solo se escribirá tras revisar una copia de trabajo y confirmar.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,8,0,14)
        });
        var bar=new WrapPanel();stack.Children.Add(bar);
        _prepare=Add(bar,"Comprobar y generar vista previa",()=>_ = PreviewAsync());
        _apply=Add(bar,"Instalar USUM Relearner",Apply);_apply.IsEnabled=false;
        var card=new Border{Background=(Brush)FindResource("Panel"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(15),
            Margin=new Thickness(0,10,0,0)};
        stack.Children.Add(card);
        _report=new TextBlock{Foreground=(Brush)FindResource("TextMain"),
            TextWrapping=TextWrapping.Wrap,
            Text="Listo. No se modificó ningún archivo."};
        card.Child=_report;
    }
    private static Button Add(System.Windows.Controls.Panel panel,string text,Action action)
    {
        var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};
        b.Click+=(_,_)=>{try{action();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(b),"USUM Relearner",ex.Message);}};
        panel.Children.Add(b);return b;
    }
    private async Task PreviewAsync()
    {
        if(_running)return;
        _running=true;_prepare.IsEnabled=false;_apply.IsEnabled=false;_preview=null;
        UiProgress.Begin("USUM Relearner · validando el parche en una copia de trabajo…");
        try
        {
            var candidate=await _service.PrepareAsync(message=>Dispatcher.BeginInvoke(()=>
                _report.Text=message));
            _preview=candidate;
            _report.Text=candidate.Report+Environment.NewLine+
                $"Archivos que cambiarían: {candidate.Files.Count}"+
                Environment.NewLine+string.Join(Environment.NewLine,candidate.Files.Select(p=>
                    $"• {System.IO.Path.GetFileName(p.Path)} · "+
                    $"SHA origen {ProjectHistory.Hash(p.Before)[..12]}… → "+
                    $"SHA propuesto {ProjectHistory.Hash(p.After)[..12]}…"))+
                Environment.NewLine+"Ningún cambio fue escrito en el proyecto original.";
            _apply.IsEnabled=candidate.Files.Count>0;
        }
        catch(Exception ex)
        {
            _report.Text="La vista previa falló sin modificar la ROM: "+ex.Message;
            StudioDialog.Error(Window.GetWindow(this),"No se pudo preparar USUM Relearner",ex.Message);
        }
        finally{_running=false;_prepare.IsEnabled=true;UiProgress.End();}
    }
    private void Apply()
    {
        if(_preview is null||_running)throw new InvalidOperationException("Generá la vista previa primero.");
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar USUM Relearner",
            $"Se modificarán {_preview.Files.Count} archivos con transacción y registro histórico. ¿Continuar?"))
            return;
        UiProgress.Begin("Guardando USUM Relearner con historial…");
        try
        {
            _service.Apply(_preview);
            _report.Text="USUM Relearner aplicado. code.bin y StoryText quedaron registrados en el historial.";
            _preview=null;_apply.IsEnabled=false;
        }
        finally{UiProgress.End();}
    }
}
