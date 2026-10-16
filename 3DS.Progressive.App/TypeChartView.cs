using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using WpfApplication=System.Windows.Application;
using UniformGrid=System.Windows.Controls.Primitives.UniformGrid;

namespace ThreeDS.Progressive;

/// <summary>18x18 in-app type effectiveness matrix, Gen6 and Gen7 vanilla layouts.</summary>
internal sealed class TypeChartView : UserControl
{
    private readonly GameTypeChartService _service;
    private readonly TextBlock _state;
    private readonly Button[,] _buttons=new Button[18,18];
    private readonly Button _apply;
    private readonly byte[] _original,_draft;
    private byte[]? _normalTable;
    private readonly System.Windows.Controls.CheckBox _inverse=new(){Content="Combate inverso (incluye inmunidades)",Margin=new Thickness(0,4,10,8)};
    internal byte[] DraftTable=>(byte[])_draft.Clone();
    internal void SetInverse(bool enabled)=>_inverse.IsChecked=enabled;
    private (FileChange File,IReadOnlyList<ThreeDS.Progressive.Models.BatchChange> Changes)? _proposal;
    internal TypeChartView(GameProjectSession project)
    {
        _service=new GameTypeChartService(project);
        _original=_service.Read();_draft=(byte[])_original.Clone();
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var top=new StackPanel{Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Type Chart · Efectividad entre tipos",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        top.Children.Add(new TextBlock{Text="Filas: tipo atacante · Columnas: tipo defensor. " +
            "Pulsá una casilla para alternar inmunidad ×0, resistencia ×½, daño ×1 y supereficaz ×2. " +
            "Los cambios se guardan después de confirmar la vista previa.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,5,0,7)});
        _state=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,8),
            Text="Tabla original cargada, sin modificaciones."};
        top.Children.Add(_state);
        _inverse.Checked+=(_,_)=>{_normalTable=(byte[])_draft.Clone();GameTypeChartService.Inverse(_draft).CopyTo(_draft,0);InvalidateInverse();};
        _inverse.Unchecked+=(_,_)=>{if(_normalTable is not null)_normalTable.CopyTo(_draft,0);_normalTable=null;InvalidateInverse();};
        top.Children.Add(_inverse);
        var buttons=new WrapPanel();top.Children.Add(buttons);
        var preview=new Button{Content="Generar vista previa",Margin=new Thickness(0,0,8,5)};
        preview.Click+=(_,_)=>Preview();buttons.Children.Add(preview);
        _apply=new Button{Content="Aplicar cambios",Margin=new Thickness(0,0,8,5),IsEnabled=false};
        _apply.Click+=(_,_)=>Apply();buttons.Children.Add(_apply);
        var reset=new Button{Content="Restaurar valores al abrir",Margin=new Thickness(0,0,8,5)};
        reset.Click+=(_,_)=>{_inverse.IsChecked=false;Array.Copy(_original,_draft,_draft.Length);_proposal=null;_apply.IsEnabled=false;
            Refresh();_state.Text="Valores originales restaurados en memoria, sin escribir.";};
        buttons.Children.Add(reset);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(scroll);
        var grid=new UniformGrid{Rows=19,Columns=19,Margin=new Thickness(0,0,12,5)};
        scroll.Content=grid;
        grid.Children.Add(Header("ATQ ↓ / DEF →"));
        for(int c=0;c<18;c++)grid.Children.Add(Header(_service.Types[c]));
        for(int r=0;r<18;r++)
        {
            grid.Children.Add(Header(_service.Types[r]));
            for(int c=0;c<18;c++)
            {
                int rr=r,cc=c;
                var btn=new Button{MinWidth=58,MinHeight=31,Margin=new Thickness(1),
                    Padding=new Thickness(1),FontSize=11,ToolTip=$"{_service.Types[r]} ataca a {_service.Types[c]}"};
                btn.Click+=(_,_)=>
                {
                    int index=rr*18+cc;
                    _draft[index]=_draft[index] switch {0=>2,2=>4,4=>8,_=>0};
                    _proposal=null;_apply.IsEnabled=false;Refresh();
                    _state.Text=$"{_service.Types[rr]} → {_service.Types[cc]}: {Label(_draft[index])} " +
                        "· generá vista previa antes de aplicar.";
                };
                _buttons[r,c]=btn;
                grid.Children.Add(btn);
            }
        }
        Refresh();
    }
    internal int ChangedCount=>_draft.Where((v,i)=>v!=_original[i]).Count();
    internal bool ConfirmLeave()=>ChangedCount==0||StudioDialog.Confirm(Window.GetWindow(this),
        "Tabla de tipos con cambios sin guardar","Los cambios de efectividad se descartarán al salir. ¿Continuar?");
    private void InvalidateInverse(){_proposal=null;_apply.IsEnabled=false;Refresh();_state.Text="Modo de efectividad actualizado en memoria. Generá vista previa antes de aplicar.";}
    private static TextBlock Header(string label)=>new()
    {
        Text=label,Foreground=(Brush)WpfApplication.Current.FindResource("TextSoft"),
        TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,
        VerticalAlignment=VerticalAlignment.Center,
        Margin=new Thickness(1),MinWidth=59,FontSize=11,
        ToolTip=label
    };
    private static string Label(byte v)=>v switch {0=>"×0",2=>"×½",4=>"×1",8=>"×2",_=>"?"};
    private void Refresh()
    {
        for(int r=0;r<18;r++)
        for(int c=0;c<18;c++)
        {
            byte v=_draft[r*18+c];
            _buttons[r,c].Content=Label(v);
            _buttons[r,c].Background=(Brush)FindResource(v switch
            {
                0=>"Bg",2=>"Panel",4=>"Surface",_=>"Selected"
            });
        }
    }
    private void Preview()
    {
        try
        {
            var proposal=_service.Preview(_draft,repairCro:true);
            _proposal=proposal;
            _apply.IsEnabled=proposal.Changes.Count>0;
            _state.Text=$"{proposal.Changes.Count} relaciones modificadas · vista previa congelada, "+
                "sin guardar en code.bin.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Type Chart",ex.Message);}
    }
    private void Apply()
    {
        if(_proposal is not { } candidate)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar Type Chart",
            $"Se aplicarán {candidate.Changes.Count} cambios de efectividad en la tabla de tipos. ¿Continuar?"))return;
        try
        {
            _service.Apply(candidate.File,candidate.Changes);
            Array.Copy(_draft,_original,_draft.Length);
            _proposal=null;_apply.IsEnabled=false;
            _state.Text=$"Guardados {candidate.Changes.Count} cambios y registrados en el historial.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Type Chart",ex.Message);}
    }
}
