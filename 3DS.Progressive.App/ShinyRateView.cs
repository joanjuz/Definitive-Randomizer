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

namespace ThreeDS.Progressive;

internal sealed class ShinyRateView : UserControl
{
    private readonly GameShinyRateService _service;
    private readonly TextBox _rerolls;
    private readonly CheckBox _everything;
    private readonly TextBlock _status;
    private readonly Button _apply;
    private (FileChange File,IReadOnlyList<ThreeDS.Progressive.Models.BatchChange> Changes,int AppliedRerolls)? _proposal;
    private bool _restoring;
    internal ShinyRateView(GameProjectSession project)
    {
        _service=new GameShinyRateService(project);
        var current=_service.Read();
        Background=(Brush)FindResource("Bg");
        var stack=new StackPanel{Margin=new Thickness(22)};Content=stack;
        stack.Children.Add(new TextBlock{Text="Shiny Rate · Frecuencia shiny",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        stack.Children.Add(new TextBlock
        {
            Text="Modificar los rerolls PID no cambia la determinación shiny del juego. " +
                "Se aplica el patrón ARM y la tabla de instrucciones originales de pk3DS.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,7,0,16)
        });
        var card=new Border{Background=(Brush)FindResource("Panel"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(16)};
        stack.Children.Add(card);
        var panel=new StackPanel();card.Child=panel;
        panel.Children.Add(new TextBlock{Text="Rerolls PID (1 a 9000)",
            Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,8)});
        _rerolls=new TextBox{Text=current.Rerolls.ToString(),Width=140,
            HorizontalAlignment=System.Windows.HorizontalAlignment.Left,MinHeight=36};
        _rerolls.TextChanged+=(_,_)=>Invalidate();
        panel.Children.Add(_rerolls);
        _everything=new CheckBox{Content="Everything Shiny (parche universal)",
            IsChecked=current.AlwaysShiny,IsEnabled=current.AlwaysShinyOffset.HasValue,
            Margin=new Thickness(0,14,0,11)};
        _everything.Checked+=(_,_)=>Invalidate();_everything.Unchecked+=(_,_)=>Invalidate();
        panel.Children.Add(_everything);
        panel.Children.Add(new TextBlock
        {
            Text=current.AlwaysShinyOffset.HasValue?
                "Firma Everything Shiny compatible.":"Esta ROM no presenta una firma compatible para Everything Shiny.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft")
        });
        var options=new WrapPanel{Margin=new Thickness(0,14,0,8)};
        stack.Children.Add(options);
        var preview=new Button{Content="Generar vista previa",Margin=new Thickness(0,0,8,8)};
        preview.Click+=(_,_)=>Preview(false);options.Children.Add(preview);
        _apply=new Button{Content="Guardar cambios",IsEnabled=false,Margin=new Thickness(0,0,8,8)};
        _apply.Click+=(_,_)=>Apply();options.Children.Add(_apply);
        var restore=new Button{Content="Restaurar tasa original",Margin=new Thickness(0,0,8,8)};
        restore.Click+=(_,_)=>Preview(true);options.Children.Add(restore);
        _status=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,0),
            Text=current.Vanilla?"Tasa original detectada.":"Rerolls personalizados detectados."};
        stack.Children.Add(_status);
    }
    internal bool ConfirmLeave()=>_proposal is null||StudioDialog.Confirm(Window.GetWindow(this),
        "Shiny Rate · cambios pendientes","Hay una vista previa sin aplicar. ¿Descartarla?");
    private void Invalidate(){_proposal=null;if(_apply is not null)_apply.IsEnabled=false;}
    private void Preview(bool restore)
    {
        try
        {
            if(!int.TryParse(_rerolls.Text,out var rerolls))
                throw new InvalidDataException("Introducí un número de rerolls válido.");
            var proposal=_service.Preview(rerolls,_everything.IsChecked==true,restore);
            _proposal=proposal;_restoring=restore;
            _apply.IsEnabled=proposal.Changes.Count>0;
            var pct=100*(1-Math.Pow(4095d/4096,proposal.AppliedRerolls));
            _status.Text=$"Vista previa · {proposal.Changes.Count} cambios sin guardar"+
                (restore?" · restaurar valores originales":
                    $" · {proposal.AppliedRerolls} rerolls · shiny aproximado: {pct:0.00}%");
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Shiny Rate",ex.Message);}
    }
    private void Apply()
    {
        if(_proposal is not { } preview)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar Shiny Rate",
            $"Se aplicarán {preview.Changes.Count} cambios a code.bin con historial. ¿Continuar?"))return;
        try
        {
            _service.Apply(preview.File,preview.Changes,_restoring);
            _proposal=null;_apply.IsEnabled=false;
            _status.Text="Shiny Rate guardado y registrado en el historial de cambios.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Shiny Rate",ex.Message);}
    }
}
