using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Binding=System.Windows.Data.Binding;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using DataGrid=System.Windows.Controls.DataGrid;
using Brush=System.Windows.Media.Brush;

namespace ThreeDS.Progressive;

internal sealed class PickupView : UserControl
{
    private readonly GamePickupService _service;
    private readonly DataGrid _grid;
    private readonly Button _apply;
    private readonly TextBlock _status;
    private ObservableCollection<PickupRow> _rows;
    private List<PickupRow> _original;
    private EditorProposal? _proposal;
    internal PickupView(GameProjectSession project)
    {
        _service=new(project);_original=_service.Read();_rows=new(_original.Select(r=>r.Copy()));
        Background=(Brush)FindResource("Bg");var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Recogida · Pickup",FontSize=23,Foreground=(Brush)FindResource("TextMain")});
        top.Children.Add(new TextBlock{Text=_service.Gen7?
            "Objetos y probabilidades por rango de nivel. Cada columna debe sumar 100 %. Podés añadir o quitar la última fila. La randomización conserva las probabilidades.":
            "18 objetos comunes y 11 raros en XY/ORAS. Las probabilidades originales se conservan. Revisá los cambios antes de guardar.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,12)});
        var buttons=new WrapPanel();top.Children.Add(buttons);
        Add(buttons,"Randomizar objetos",Randomize);
        if(_service.Gen7)
        {
            Add(buttons,"Añadir fila",()=>{if(!CommitEdits())return;_rows.Add(new(){Group=$"Fila {_rows.Count+1:00}"});Invalidate();});
            Add(buttons,"Quitar última fila",()=>{if(!CommitEdits()||_rows.Count<=1)return;_rows.RemoveAt(_rows.Count-1);Invalidate();});
        }
        Add(buttons,"Restaurar al abrir",()=>SetRows(_original.Select(r=>r.Copy())));
        Add(buttons,"Generar vista previa",Preview);
        _apply=Add(buttons,"Aplicar y verificar",Apply);_apply.IsEnabled=false;
        _status=new TextBlock{Text="Sin cambios guardados.",Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,10,0,0),TextWrapping=TextWrapping.Wrap};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        _grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=false,ItemsSource=_rows};
        DarkGridTheme.Apply(_grid);
        _grid.Columns.Add(new DataGridTextColumn{Header="Entrada",IsReadOnly=true,Binding=new Binding("Group"),Width=115});
        _grid.Columns.Add(new DataGridComboBoxColumn{Header="Objeto",ItemsSource=_service.Items,DisplayMemberPath="Display",SelectedValuePath="Id",
            SelectedValueBinding=new Binding("Item"){Mode=BindingMode.TwoWay},Width=260});
        if(_service.Gen7)for(int c=0;c<10;c++)_grid.Columns.Add(new DataGridTextColumn{Header=$"{c*10+1}–{(c+1)*10} %",
            Binding=new Binding($"Rates[{c}]"){Mode=BindingMode.TwoWay,ValidatesOnExceptions=true},Width=80});
        _grid.BeginningEdit+=(_,_)=>Invalidate();root.Children.Add(_grid);
    }
    private Button Add(System.Windows.Controls.Panel host,string label,Action action)
    {
        var b=new Button{Content=label,Margin=new Thickness(0,0,8,12)};host.Children.Add(b);
        b.Click+=(_,_)=>{try{action();}catch(Exception ex){_status.Text=ex.Message;StudioDialog.Error(Window.GetWindow(this),"Pickup",ex.Message);}};return b;
    }
    private bool CommitEdits()=>_grid.CommitEdit(DataGridEditingUnit.Cell,true)&&_grid.CommitEdit(DataGridEditingUnit.Row,true);
    private void Invalidate(){_proposal=null;_apply.IsEnabled=false;}
    private void SetRows(IEnumerable<PickupRow> rows){_rows=new(rows);_grid.ItemsSource=_rows;Invalidate();}
    private bool Dirty()=>_rows.Count!=_original.Count||_rows.Where((r,i)=>r.Item!=_original[i].Item||!r.Rates.SequenceEqual(_original[i].Rates)).Any();
    internal bool ConfirmLeave()
    {
        if(!CommitEdits())return false;
        return !Dirty()||StudioDialog.Confirm(Window.GetWindow(this),"Pickup sin guardar","Los cambios de Pickup se descartarán al salir. ¿Continuar?");
    }
    private void Randomize(){if(!CommitEdits())return;SetRows(_service.Randomize(_rows));_status.Text="Objetos randomizados en memoria. Generá una vista previa antes de guardar.";}
    private void Preview()
    {
        if(!CommitEdits())return;_proposal=_service.Preview(_rows);_apply.IsEnabled=_proposal.Changes.Count>0;
        _status.Text=_proposal.Changes.Count==0?"No hay cambios.":string.Join(Environment.NewLine,_proposal.Changes.Take(20).Select(c=>$"Fila {c.Id+1} · {c.Field}: {c.Before} → {c.After}"))+
            (_proposal.Changes.Count>20?$"\n… {_proposal.Changes.Count} cambios en total.":"");
    }
    private void Apply()
    {
        if(_proposal is null||!CommitEdits())return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar Pickup",$"Aplicar los {_proposal.Changes.Count} cambios de la vista previa?"))return;
        _service.Apply(_proposal);_original=_service.Read();SetRows(_original.Select(r=>r.Copy()));_status.Text="Pickup guardado, verificado y registrado en el historial.";
    }
}
