using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridRow=System.Windows.Controls.DataGridRow;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using ComboBox=System.Windows.Controls.ComboBox;
using ListBox=System.Windows.Controls.ListBox;
using TextBox=System.Windows.Controls.TextBox;
using Control=System.Windows.Controls.Control;
using Binding=System.Windows.Data.Binding;

namespace ThreeDS.Progressive;

/// <summary>In-app level-up learnsets: add, delete, edit, and export without WinForms.</summary>
internal sealed class LevelUpView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly GameLevelUpService _service;
    private ObservableCollection<LevelUpMoveEntry> _rows=[];
    private readonly DataGrid _grid;
    private readonly UniformGrid _compact=new() {Columns=3};
    private LevelUpMoveEntry? _selectedMove;
    private int _compactColumns=3;
    private readonly ListBox _species;
    private readonly TextBox _search;
    private readonly Button _save;
    private readonly TextBlock _status;
    private LevelUpBatchPreview? _bulkPreview;
    private readonly CheckBox _allowHms=new() {Content="Permitir MO / HM"};
    private readonly CheckBox _biasByType=new() {Content="Sesgo por tipo (STAB)"};
    private readonly CheckBox _expand=new() {Content="Expandir cantidad de movimientos"};
    private readonly CheckBox _startFour=new() {Content="Comenzar con 4 movimientos"};
    private readonly CheckBox _spread=new() {Content="Distribuir niveles uniformemente"};
    private readonly CheckBox _excludeFixed=new() {Content="Excluir movimientos de daño fijo",IsChecked=true};
    private readonly TextBox _stabPercent=new() {Text="15"};
    private readonly TextBox _expandTo=new() {Text="25"};
    private readonly TextBox _spreadTo=new() {Text="79"};
    private Button? _applyBulk;
    private int _current;
    private bool _dirty,_loading;
    internal bool HasPendingChanges=>_dirty;
    internal bool ConfirmLeave()=>!_dirty||StudioDialog.Confirm(Window.GetWindow(this),
        "Movimientos pendientes","La especie tiene movimientos sin guardar.","Si continuás, se descartarán estos cambios.");
    internal LevelUpView(GameProjectSession project)
    {
        _project=project;_service=new(project);Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(18)};
        Content=root;
        var header=new StackPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock {Text="Level Up Moves · Aprendizaje por nivel",
            FontSize=21,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock {Text="Editá la lista real de movimientos de cada Pokémon y forma. Las filas se guardan ordenadas por nivel.",
            Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,6,0,0)});
        var footer=new DockPanel {Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        _status=new TextBlock {Text="Seleccioná una especie.",Foreground=(Brush)FindResource("TextSoft"),
            TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        footer.Children.Add(_status);
        _save=new Button {Content="Guardar movimientos",Padding=new Thickness(18,9,18,9),IsEnabled=false};
        DockPanel.SetDock(_save,Dock.Right);footer.Children.Add(_save);
        _save.Click+=(_,_)=>Save();
        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(242)});
        columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(columns);
        var left=new DockPanel {Margin=new Thickness(0,0,14,0)};
        Grid.SetColumn(left,0);columns.Children.Add(left);
        _search=new TextBox {MinHeight=36,Margin=new Thickness(0,0,0,8)};
        _search.TextChanged+=(_,_)=>Filter();
        DockPanel.SetDock(_search,Dock.Top);left.Children.Add(_search);
        _species=new ListBox {ItemsSource=_service.Entries,DisplayMemberPath="Name"};
        _species.SelectionChanged+=(_,_)=>SelectSpecies();
        left.Children.Add(_species);
        var right=new DockPanel();
        Grid.SetColumn(right,1);columns.Children.Add(right);
        var actions=new WrapPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(actions,Dock.Top);right.Children.Add(actions);
        Button Action(string text,Action callback)
        {
            var button=new Button {Content=text,Margin=new Thickness(0,0,8,6)};
            button.Click+=(_,_)=>callback();actions.Children.Add(button);
            return button;
        }
        Action("+ Añadir",Add);
        Action("− Quitar",Remove);
        Action("Metrónomo",()=>{if(_rows.Count==0)return;foreach(var row in _rows)row.Move=118;_grid.Items.Refresh();Dirty();});
        Action("Randomizar especie",Randomize);
        Action("Exportar CSV",Export);
        var randomSettings=new Expander {Header="Opciones del randomizador original (todos los Pokémon)",
            IsExpanded=false,Foreground=(Brush)FindResource("TextMain"),
            Margin=new Thickness(0,0,0,12)};
        var settingsBody=new StackPanel {Margin=new Thickness(10)};
        randomSettings.Content=settingsBody;
        var settingsGrid=new UniformGrid {Columns=3};
        settingsBody.Children.Add(settingsGrid);
        void AddChoice(CheckBox choice,TextBox? value=null,string? label=null)
        {
            var box=new StackPanel {Margin=new Thickness(0,0,12,10)};
            box.Children.Add(choice);
            if(value is not null)
            {
                box.Children.Add(new TextBlock {Text=label??"",FontSize=11,
                    Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,5,0,3)});
                value.MinHeight=30;
                box.Children.Add(value);
                value.IsEnabled=choice.IsChecked==true;
                choice.Checked+=(_,_)=>value.IsEnabled=true;
                choice.Unchecked+=(_,_)=>value.IsEnabled=false;
                value.TextChanged+=(_,_)=>InvalidateBulk();
            }
            choice.Checked+=(_,_)=>InvalidateBulk();
            choice.Unchecked+=(_,_)=>InvalidateBulk();
            settingsGrid.Children.Add(box);
        }
        _allowHms.IsChecked=_project.Generation==6;
        AddChoice(_allowHms);AddChoice(_biasByType,_stabPercent,"Porcentaje STAB (0–100)");
        AddChoice(_expand,_expandTo,"Movimientos por Pokémon (1–100)");
        AddChoice(_startFour);AddChoice(_spread,_spreadTo,"Último nivel de aprendizaje (1–100)");
        AddChoice(_excludeFixed);
        var bulkButtons=new WrapPanel {Margin=new Thickness(0,4,0,4)};
        settingsBody.Children.Add(bulkButtons);
        var randomAll=new Button {Content="Generar vista previa · Randomizar TODOS",Margin=new Thickness(0,0,8,0)};
        randomAll.Click+=(_,_)=>PrepareBulk(false);
        var metronomeAll=new Button {Content="Metrónomo · Todos",Margin=new Thickness(0,0,8,0)};
        metronomeAll.Click+=(_,_)=>PrepareBulk(true);
        _applyBulk=new Button {Content="Aplicar propuesta global",IsEnabled=false};
        _applyBulk.Click+=(_,_)=>ApplyBulk();
        bulkButtons.Children.Add(randomAll);bulkButtons.Children.Add(metronomeAll);
        bulkButtons.Children.Add(_applyBulk);
        DockPanel.SetDock(randomSettings,Dock.Top);right.Children.Add(randomSettings);
        _grid=new DataGrid
        {
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            ItemsSource=_rows,RowHeight=37,CanUserSortColumns=false,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        var style=new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        style.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,9,12,9)));
        _grid.ColumnHeaderStyle=style;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        _grid.RowStyle=rowStyle;
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header="Nivel",Binding=new Binding("Level"){Mode=BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true},Width=95
        });
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header="Movimiento",ItemsSource=_service.Moves,DisplayMemberPath="Display",
            SelectedValuePath="Id",SelectedValueBinding=new Binding("Move")
                {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
            Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=220
        });
        _grid.CellEditEnding+=(_,_)=>Dirty();
        // Dense multi-column cards replace the long one-record-per-full-width table.
        // The original grid remains a binding source for the row-edit commands.
        var compactScroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=_compact};
        right.Children.Add(compactScroll);
        right.SizeChanged+=(_,_)=>
        {
            int columns=Math.Clamp((int)(right.ActualWidth/295),1,4);
            if(columns!=_compactColumns){_compactColumns=columns;_compact.Columns=columns;}
        };
        _loading=true;_species.SelectedIndex=0;_loading=false;
        if(_species.SelectedItem is PokemonListEntry p)Load(p.Index);
    }
    private void InvalidateBulk()
    {
        _bulkPreview=null;
        if(_applyBulk is not null)_applyBulk.IsEnabled=false;
    }
    private LevelUpRandomSettings ReadRandomOptions()
    {
        int Number(TextBox input,int min,int max,string label)
        {
            if(!int.TryParse(input.Text,out int number)||number<min||number>max)
                throw new ArgumentException($"{label}: el valor debe estar entre {min} y {max}.");
            return number;
        }
        return new LevelUpRandomSettings(
            _allowHms.IsChecked==true,_biasByType.IsChecked==true,
            Number(_stabPercent,0,100,"STAB"),_expand.IsChecked==true,
            Number(_expandTo,1,100,"Movimientos"),_startFour.IsChecked==true,
            _spread.IsChecked==true,Number(_spreadTo,1,100,"Nivel final"),
            _excludeFixed.IsChecked==true);
    }
    private void PrepareBulk(bool metronome)
    {
        if(_dirty)
        {
            if(!ConfirmLeave())return;
            Load(_current);
        }
        try
        {
            _bulkPreview=_service.PrepareRandomization(ReadRandomOptions(),metronome);
            _applyBulk!.IsEnabled=_bulkPreview.Changes.Count>0;
            _status.Text=$"Vista previa congelada: {_bulkPreview.Changes.Count} Pokémon con cambios. Confirmá para guardar.";
        }
        catch(Exception ex){InvalidateBulk();StudioDialog.Error(Window.GetWindow(this),
            "Randomización Level Up",ex.Message);}
    }
    private void ApplyBulk()
    {
        if(_bulkPreview is not { } preview)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar Level Up global",
            $"Se escribirán los learnsets de {preview.Changes.Count} Pokémon. Esta operación queda registrada en el historial."))return;
        try
        {
            _service.ApplyRandomization(preview);
            InvalidateBulk();Load(_current);
            _status.Text="Todos los learnsets aplicados, verificados y registrados en el historial.";
        }
        catch(Exception ex){InvalidateBulk();StudioDialog.Error(Window.GetWindow(this),
            "Falló el guardado de Level Up",ex.Message);}
    }
    private void RebuildCards()
    {
        _compact.Children.Clear();
        foreach(var row in _rows)
        {
            var border=new Border {Margin=new Thickness(0,0,9,9),Padding=new Thickness(9,7,9,7),
                CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),
                BorderBrush=(Brush)FindResource("Stroke"),Background=(Brush)FindResource("Panel")};
            void Select()
            {
                _selectedMove=row;
                foreach(Border card in _compact.Children)card.BorderBrush=(Brush)FindResource("Stroke");
                border.BorderBrush=(Brush)FindResource("Accent");
            }
            border.MouseLeftButtonDown+=(_,_)=>Select();
            var slot=new Grid();
            slot.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(58)});
            slot.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            var level=new TextBox {Text=row.Level.ToString(),MinHeight=31,Margin=new Thickness(0,0,8,0),
                ToolTip="Nivel al que se aprende el movimiento"};
            level.LostFocus+=(_,_)=>{if(int.TryParse(level.Text,out int n)&&n is >=1 and <=100)
            {if(row.Level!=n){row.Level=n;Dirty();}}else level.Text=row.Level.ToString();};
            var moves=new ComboBox {ItemsSource=_service.Moves,DisplayMemberPath="Display",
                SelectedValuePath="Id",SelectedValue=row.Move,MinHeight=31,FontSize=11};
            moves.SelectionChanged+=(_,_)=>{if(moves.SelectedValue is int move&&row.Move!=move)
            {row.Move=move;Dirty();}};
            Grid.SetColumn(moves,1);slot.Children.Add(level);slot.Children.Add(moves);
            border.Child=slot;
            _compact.Children.Add(border);
        }
    }
    private void Filter()
    {
        var view=CollectionViewSource.GetDefaultView(_species.ItemsSource);
        string q=_search.Text.Trim();
        view.Filter=o=>o is PokemonListEntry p&&(p.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||
            p.Index.ToString().Contains(q,StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }
    private void SelectSpecies()
    {
        if(_loading||_species.SelectedItem is not PokemonListEntry p||p.Index==_current)return;
        if(!ConfirmLeave())
        {
            _loading=true;_species.SelectedItem=_service.Entries.FirstOrDefault(x=>x.Index==_current);
            _loading=false;return;
        }
        Load(p.Index);
    }
    private void Load(int id)
    {
        _current=id;
        _rows=new ObservableCollection<LevelUpMoveEntry>(_service.Read(id));
        _grid.ItemsSource=_rows;RebuildCards();_dirty=false;_save.IsEnabled=false;
        _status.Text=$"Especie #{id:000} · {_rows.Count} movimientos de nivel.";
    }
    private void Commit()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
    }
    private void Dirty(){_dirty=true;_save.IsEnabled=true;_status.Text="Cambios pendientes. Guardá antes de cambiar de especie.";}
    private void Add()
    {
        Commit();if(_rows.Count>=100)return;
        int level=_selectedMove?.Level??1;
        var row=new LevelUpMoveEntry {Level=level,Move=1};_rows.Add(row);_selectedMove=row;RebuildCards();Dirty();
    }
    private void Remove()
    {
        Commit();var r=_selectedMove??_rows.LastOrDefault();if(r is not null){_rows.Remove(r);_selectedMove=null;RebuildCards();Dirty();}
    }
    private void Randomize()
    {
        Commit();
        if(_rows.Count==0)return;
        var valid=_service.Moves.Where(m=>m.Id>0&&m.Id<622&&m.Id is not (165 or 621 or 166 or 226))
            .Select(m=>m.Id).ToArray();
        if(valid.Length==0)return;
        var rng=new Random(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
        foreach(var row in _rows)row.Move=valid[rng.Next(valid.Length)];
        _grid.Items.Refresh();RebuildCards();Dirty();
    }
    private void Save()
    {
        try
        {
            Commit();
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar Level Up Moves",
                $"Se actualizará el aprendizaje por nivel del Pokémon #{_current:000}."))return;
            _service.Save(_current,_rows.ToArray());
            Load(_current);_status.Text="Aprendizaje guardado y registrado en el historial.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudieron guardar los movimientos",ex.Message);}
    }
    private void Export()
    {
        try
        {
            var dialog=new Microsoft.Win32.SaveFileDialog {Filter="CSV|*.csv",FileName="level-up-moves.csv"};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            File.WriteAllText(dialog.FileName,_service.ExportCsv(),System.Text.Encoding.UTF8);
            _status.Text="CSV exportado.";
        }
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Error al exportar",ex.Message);}
    }
}
