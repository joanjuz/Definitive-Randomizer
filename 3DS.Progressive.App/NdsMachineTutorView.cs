using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using pk3DS.Core.NDS;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services.Nds;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using Binding=System.Windows.Data.Binding;
using TabControl=System.Windows.Controls.TabControl;
using TabItem=System.Windows.Controls.TabItem;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>
/// «MT y tutores» for Nintendo DS with the layout of the 3DS view: a vertical TM table and a tutor table, each with the option cards
/// on the right (assignment, TM/Tutor Sanity and Follow Evolutions). Every save is one stacked operation of the DS project.
/// </summary>
internal sealed class NdsMachineTutorView : UserControl
{
    internal sealed class MoveRow
    {
        public string Number {get;init;}="";
        public string Key {get;init;}="";
        public int Move {get;set;}
        public int Original {get;init;}
    }

    private const int Struggle=165;
    private readonly NdsProject _project;
    private readonly ObservableCollection<MoveRow> _machines=[],_tutors=[];
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly IReadOnlyList<MoveChoice> _moves;
    private readonly bool _machinesEditable;
    internal event Action? Changed;
    internal DataGrid MachineGrid {get;}
    internal DataGrid? TutorGrid {get;}

    internal NdsMachineTutorView(NdsProject project)
    {
        _project=project;SetResourceReference(BackgroundProperty,"Bg");
        _moves=project.MoveNames.Select((name,id)=>new MoveChoice(id,string.IsNullOrWhiteSpace(name)?$"Movimiento {id:000}":name)).ToArray();
        var cells=project.SectionProblem(NdsSection.MoveLists) is null?project.Read(NdsSection.MoveLists):[];
        foreach(var cell in cells)
            (cell.Entry==0?_machines:_tutors).Add(new MoveRow{Number=cell.Entry==0?"MT"+(int.Parse(cell.Local[2..])+1).ToString("00"):(int.Parse(cell.Local[5..])+1).ToString(),Key=cell.Key,Move=cell.Value,Original=cell.Value});
        _machinesEditable=_machines.Count>0;
        // Black/White: the TM list is in the DSi ARM9 and is only shown, from the compatibility table's column names.
        if(!_machinesEditable)
            foreach(var cell in project.Read(NdsSection.Machines).Where(c=>c.Entry==1).Take(project.Source.TmCount))
                _machines.Add(new MoveRow{Number=cell.Field.Split(' ')[0],Key="",Move=MoveOf(cell.Field),Original=MoveOf(cell.Field)});

        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var header=new StackPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="MT y tutores",FontSize=22,FontWeight=FontWeights.SemiBold});
        var intro=new TextBlock{Text="Tablas reales del proyecto. Cada guardado se acumula y se escribe al exportar la ROM.",Margin=new Thickness(0,4,0,0),TextWrapping=TextWrapping.Wrap};
        intro.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");header.Children.Add(intro);
        DockPanel.SetDock(_status,Dock.Bottom);_status.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");root.Children.Add(_status);
        var tabs=new TabControl();root.Children.Add(tabs);

        MachineGrid=Table("MT",_machines,_machinesEditable);
        var randomMachines=Action("Randomizar MT",()=>Shuffle(_machines,HmMoves()));var saveMachines=Action("Guardar MT",()=>Save(_machines,"Guardar MT"));
        randomMachines.IsEnabled=saveMachines.IsEnabled=_machinesEditable;
        tabs.Items.Add(new TabItem{Header="MT",Content=Layout(MachineGrid,OptionCards.Column(
            OptionCards.Card("Asignación de MT",_machinesEditable?"Cambia el movimiento de cada MT. Las MO no cambian: la historia necesita sus movimientos de campo."
                :"Negro y Blanco guardan la lista de MT en el ARM9 DSi, que no se modifica; la tabla solo se muestra.",randomMachines,OptionCards.Primary(saveMachines)),
            CompatibilityCard("Compatibilidad de MT","TM",NdsSection.Machines)))});
        if(_tutors.Count>0)
        {
            TutorGrid=Table("N.º",_tutors,true);
            tabs.Items.Add(new TabItem{Header="Tutores · movimientos",Content=Layout(TutorGrid,OptionCards.Column(
                OptionCards.Card("Movimientos de los tutores","Cambia el movimiento que enseña cada tutor.",Action("Randomizar tutores",()=>Shuffle(_tutors,[])),OptionCards.Primary(Action("Guardar tutores",()=>Save(_tutors,"Guardar tutores")))),
                CompatibilityCard("Compatibilidad de tutores","Tutor",NdsSection.Tutors)))});
        }
        _status.Text="Selecciona un movimiento o cambia la compatibilidad y confirma el guardado.";
    }

    private int MoveOf(string field)=>field.Split(" · ",2) is [_,var name]?_moves.FirstOrDefault(m=>m.Name==name)?.Id??0:0;
    private HashSet<int> HmMoves()=>_project.Source.MachineMoves.Skip(_project.Source.TmCount).ToHashSet();

    private DataGrid Table(string first,ObservableCollection<MoveRow> rows,bool editable)
    {
        var grid=new DataGrid{ItemsSource=rows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,IsReadOnly=!editable};
        DarkGridTheme.Apply(grid);grid.RowHeight=34;grid.MinRowHeight=34;
        grid.Columns.Add(new DataGridTextColumn{Header=first,Binding=new Binding(nameof(MoveRow.Number)),IsReadOnly=true,Width=new DataGridLength(70)});
        grid.Columns.Add(new DataGridComboBoxColumn{Header="Movimiento",ItemsSource=_moves,SelectedValuePath=nameof(MoveChoice.Id),DisplayMemberPath=nameof(MoveChoice.Display),
            SelectedValueBinding=new Binding(nameof(MoveRow.Move)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=200});
        System.Windows.Automation.AutomationProperties.SetName(grid,first=="MT"?"Tabla de MT":"Tabla de tutores");
        return grid;
    }

    // Table in the middle and the option cards on the right, as MachineTutorView.
    private static System.Windows.Controls.Grid Layout(DataGrid table,FrameworkElement options)
    {
        var grid=new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.4,GridUnitType.Star),MinWidth=260});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=250,MaxWidth=340});
        table.Margin=new Thickness(0,0,14,0);grid.Children.Add(table);
        var scroll=new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,6,0)};
        System.Windows.Controls.Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
        return grid;
    }

    private static Button Action(string label,Action action){var b=new Button{Content=label};b.Click+=(_,_)=>action();return b;}

    private Border CompatibilityCard(string title,string prefix,NdsSection section)
    {
        var sanity=new CheckBox{Content=prefix+" Sanity · movimientos aprendidos por nivel"};
        var follow=new CheckBox{Content=prefix+" Follow Evolutions · heredar compatibilidad"};
        NdsPreview? proposal=null;Button? apply=null;
        var preview=Action($"Vista previa compatibilidad {(prefix=="TM"?"MT":"tutores")}",()=>
        {
            if(sanity.IsChecked!=true&&follow.IsChecked!=true){_status.Text="Elige "+prefix+" Sanity y/o "+prefix+" Follow Evolutions.";return;}
            var options=new NdsDataOptions(0){MachineSanity=prefix=="TM"&&sanity.IsChecked==true,MachineFollowEvolutions=prefix=="TM"&&follow.IsChecked==true,
                TutorSanity=prefix=="Tutor"&&sanity.IsChecked==true,TutorFollowEvolutions=prefix=="Tutor"&&follow.IsChecked==true};
            string action=string.Join(" y ",new[]{sanity.IsChecked==true?prefix+" Sanity":null,follow.IsChecked==true?prefix+" Follow Evolutions":null}.OfType<string>());
            proposal=_project.PreviewData(action,section,options);apply!.IsEnabled=proposal.Result.Changes.Count>0;
            _status.Text=$"{action}: {proposal.Result.Changes.Count} compatibilidades nuevas. Revisa y aplica.";
        });
        apply=Action($"Aplicar compatibilidad {(prefix=="TM"?"MT":"tutores")}",()=>
        {
            if(proposal is null)return;
            try{_project.Apply(proposal);proposal=null;apply!.IsEnabled=false;_status.Text="Compatibilidad añadida; se escribirá al exportar.";Changed?.Invoke();}
            catch(Exception ex) when (ex is InvalidOperationException or ArgumentException){_status.Text="No se pudo aplicar: "+ex.Message;}
        });
        apply.IsEnabled=false;
        sanity.Checked+=(_,_)=>Clear();sanity.Unchecked+=(_,_)=>Clear();follow.Checked+=(_,_)=>Clear();follow.Unchecked+=(_,_)=>Clear();
        void Clear(){proposal=null;apply!.IsEnabled=false;}
        var help=new TextBlock{Text="Sanity añade la compatibilidad de los movimientos que el Pokémon aprende por nivel. Follow Evolutions hereda la compatibilidad de la preevolución. Solo se añade, nunca se quita.",TextWrapping=TextWrapping.Wrap};
        return OptionCards.Card(title,null,sanity,follow,preview,OptionCards.Primary(apply),help);
    }

    // As the 3DS "Randomizar MT": distinct random moves, never Struggle or an HM move; the result is saved with «Guardar».
    private void Shuffle(ObservableCollection<MoveRow> rows,HashSet<int> excluded)
    {
        var pool=Enumerable.Range(1,_moves.Count-1).Where(id=>id!=Struggle&&!excluded.Contains(id)).ToArray();
        for(int i=pool.Length-1;i>0;i--){int j=RandomNumberGenerator.GetInt32(i+1);(pool[i],pool[j])=(pool[j],pool[i]);}
        var copy=rows.ToArray();rows.Clear();
        for(int i=0;i<copy.Length;i++)rows.Add(new MoveRow{Number=copy[i].Number,Key=copy[i].Key,Move=pool[i%pool.Length],Original=copy[i].Original});
        _status.Text="Movimientos nuevos en la tabla. Guarda para acumular el cambio.";
    }

    private void Save(ObservableCollection<MoveRow> rows,string action)
    {
        var edits=rows.Where(r=>r.Key.Length>0&&r.Move!=r.Original).ToDictionary(r=>r.Key,r=>r.Move);
        if(edits.Count==0){_status.Text="No hay cambios que guardar.";return;}
        try
        {
            _project.Apply(_project.PreviewEdits(NdsSection.MoveLists,edits));
            var saved=rows.Select(r=>new MoveRow{Number=r.Number,Key=r.Key,Move=r.Move,Original=r.Move}).ToArray();rows.Clear();foreach(var r in saved)rows.Add(r);
            _status.Text=$"{action}: {edits.Count} cambios; se escribirán al exportar.";Changed?.Invoke();
        }
        catch(Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.InvalidDataException){_status.Text="No se pudo guardar: "+ex.Message;}
    }
}
