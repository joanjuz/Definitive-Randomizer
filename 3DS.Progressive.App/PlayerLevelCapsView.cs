using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using DataGrid=System.Windows.Controls.DataGrid;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using UserControl=System.Windows.Controls.UserControl;
using Binding=System.Windows.Data.Binding;
using Control=System.Windows.Controls.Control;
using Panel=System.Windows.Controls.Panel;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;

namespace ThreeDS.Progressive;

/// <summary>In-app original USUM Player Level Caps event flag / event work editor.</summary>
internal sealed class PlayerLevelCapsView : UserControl
{
    private readonly GamePlayerLevelCapService _service;
    private readonly ObservableCollection<CapRow> _rows=[];
    private readonly DataGrid _grid;
    private readonly TextBlock _summary,_status;
    private readonly ComboBox _preset;
    private readonly TextBox _finalCap;
    private readonly Button _apply;
    private readonly StackPanel _operations;
    private LevelCapInstallPreview? _preview;
    private bool _changed;
    internal bool ConfirmLeave()=>!_changed&&_preview is null||StudioDialog.Confirm(
        Window.GetWindow(this),"Player Level Caps pendiente",
        "Hay flags, caps o una vista previa sin aplicar.",
        "Si continuás, se descartan los cambios no guardados.");
    internal PlayerLevelCapsView(GameProjectSession project)
    {
        _service=new GamePlayerLevelCapService(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(18)};
        Content=root;
        var header=new StackPanel{Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock
        {
            Text="Player Level Caps · Event Flags y Event Work",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")
        });
        header.Children.Add(new TextBlock
        {
            Text="Control del nivel máximo del jugador por progreso de historia. Es independiente de Trainer Level Caps. El parche afecta Battle.cro y Rare Candy en code.bin.",
            Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,5,0,10)
        });
        _operations=new StackPanel();header.Children.Add(_operations);
        var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,8)};
        _operations.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock{Text="Preset",Margin=new Thickness(0,8,9,0),
            Foreground=(Brush)FindResource("TextSoft")});
        _preset=new ComboBox{Width=205,Margin=new Thickness(0,0,12,5)};
        _preset.ItemsSource=LevelCapShifts.All.Select(n=>new PresetChoice(n,LevelCapShifts.Describe(n))).ToArray();
        _preset.DisplayMemberPath="Label";_preset.SelectedValuePath="Shift";
        _preset.SelectedValue=0;toolbar.Children.Add(_preset);
        toolbar.Children.Add(new TextBlock{Text="Cap final",Margin=new Thickness(0,8,8,0),
            Foreground=(Brush)FindResource("TextSoft")});
        _finalCap=new TextBox{Text=LevelCapTable.ResearchFinalCap.ToString(),
            Width=65,Margin=new Thickness(0,0,10,5)};
        toolbar.Children.Add(_finalCap);
        ActionButton(toolbar,"Aplicar preset",()=>
        {
            if(_preset.SelectedValue is not int shift||!byte.TryParse(_finalCap.Text,out var cap))return;
            if(cap<5||cap>100)throw new ArgumentException("El cap final debe estar entre 5 y 100.");
            Reload(LevelCapPresets.Build(shift,cap));
            _summary.Text="Preset: "+LevelCapShifts.Describe(shift)+". "+LevelCapPresets.Summarise(GetTable());
        });
        var commands=new WrapPanel();_operations.Children.Add(commands);
        ActionButton(commands,"Sync Trainer Caps",SyncTrainers);
        ActionButton(commands,"Cargar template…",LoadTemplate);
        ActionButton(commands,"Guardar template…",SaveTemplate);
        ActionButton(commands,"Valores investigados",()=>
        {
            _preset.SelectedValue=0;_finalCap.Text=LevelCapTable.ResearchFinalCap.ToString();
            Reload(LevelCapTable.Default());
        });
        _summary=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,10,0,6)};
        header.Children.Add(_summary);
        _status=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Text=_service.Status,Margin=new Thickness(0,0,0,7)};
        header.Children.Add(_status);
        var bottom=new Grid{Margin=new Thickness(0,11,0,0)};
        bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var report=new TextBlock{Text="No se modifica ningún archivo hasta confirmar una vista previa.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            VerticalAlignment=VerticalAlignment.Center};
        bottom.Children.Add(report);
        var prepare=ActionButton(bottom,"Generar vista previa",Prepare);
        Grid.SetColumn(prepare,1);
        _apply=ActionButton(bottom,"Instalar / actualizar",Apply);
        _apply.IsEnabled=false;Grid.SetColumn(_apply,2);
        _grid=new DataGrid
        {
            AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            RowHeight=34,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke")
        };
        var headStyle=new Style(typeof(DataGridColumnHeader));
        headStyle.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        headStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        headStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,8,9,8)));
        _grid.ColumnHeaderStyle=headStyle;
        void Column(string title,string property,int width,bool editable=false)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header=title,IsReadOnly=!editable,
                Width=width<0?new DataGridLength(1,DataGridLengthUnitType.Star):
                    new DataGridLength(width),
                Binding=new Binding(property)
                {
                    Mode=editable?BindingMode.TwoWay:BindingMode.OneWay,
                    UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,
                    ValidatesOnExceptions=true
                }
            });
        }
        Column("Punto de historia","Label",-1);
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header="Condición",Width=135,
            ItemsSource=Enum.GetValues<LevelCapConditionKind>(),
            SelectedItemBinding=new Binding("Kind"){Mode=BindingMode.TwoWay,
                UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}
        });
        Column("Flag / Work ID","Offset",130,true);
        Column("Máscara / Mínimo", "Bit",140,true);
        Column("Nivel máximo","Cap",120,true);
        _grid.ItemsSource=_rows;
        _grid.CellEditEnding+=(_,_)=>Invalidate();
        root.Children.Add(_grid);
        Reload(_service.StartingTable());
        _changed=false;
        _operations.IsEnabled=_service.Supported;
        _grid.IsEnabled=_service.Supported;
        prepare.IsEnabled=_service.Supported;
        if(!_service.Supported)_status.Text="Esta mecánica investigada solo está disponible para Ultra Sun / Ultra Moon.";
    }
    private static Button ActionButton(Panel panel,string text,Action action)
    {
        var button=new Button{Content=text,Margin=new Thickness(0,0,9,6)};
        button.Click+=(_,_)=>{try{action();}
            catch(Exception ex){StudioDialog.Error(Window.GetWindow(button),"Player Level Caps",ex.Message);}};
        panel.Children.Add(button);return button;
    }
    private void Invalidate(){_changed=true;_preview=null;if(_apply is not null)_apply.IsEnabled=false;}
    private void Reload(LevelCapTable table)
    {
        _rows.Clear();
        foreach(var e in table.Entries)
        {
            var r=new CapRow(e);r.PropertyChanged+=(_,_)=>Invalidate();_rows.Add(r);
        }
        Invalidate();
        if(_summary is not null)_summary.Text=LevelCapPresets.Summarise(table);
    }
    private LevelCapTable GetTable()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);
        _grid.CommitEdit(DataGridEditingUnit.Row,true);
        var table=new LevelCapTable{Entries=_rows.Select(x=>new LevelCapEntry(x.Label,
            x.Kind,checked((ushort)x.Offset),checked((ushort)x.Bit),
            checked((byte)x.Cap))).ToList()};
        if(table.Validate() is {Count:>0} problems)
            throw new InvalidDataException(problems[0]);
        return table;
    }
    private void SyncTrainers()
    {
        var sync=_service.SyncFromTrainerRules(GetTable(),out int changes);
        Reload(sync);
        _status.Text=$"Sincronizados {changes} checkpoints con entrenadores investigados; " +
            "se conservan las condiciones especiales y los caps nunca retroceden.";
    }
    private void LoadTemplate()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Player Level Caps JSON|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var model=JsonSerializer.Deserialize<LevelCapTemplateDto>(
            File.ReadAllText(dialog.FileName),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException("Template vacío.");
        if(!string.Equals(model.Game,"USUM",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El template no corresponde a USUM.");
        var table=new LevelCapTable{Entries=model.Entries.Select(e=>
            new LevelCapEntry(e.Label,e.Kind,e.FlagOffset,e.FlagBit,e.Cap)).ToList()};
        if(table.Validate() is {Count:>0} problems)throw new InvalidDataException(problems[0]);
        _preset.SelectedValue=model.PresetShift;_finalCap.Text=model.FinalCap.ToString();
        Reload(table);_status.Text="Template cargado. Generá una vista previa antes de instalarlo.";
    }
    private void SaveTemplate()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog
        {Filter="Player Level Caps JSON|*.json",FileName="usum-player-level-caps.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var rows=GetTable().Entries;
        var dto=new LevelCapTemplateDto
        {
            Game="USUM",PresetShift=_preset.SelectedValue is int shift?shift:0,
            FinalCap=int.TryParse(_finalCap.Text,out var c)?c:LevelCapTable.ResearchFinalCap,
            Entries=rows.Select(e=>new LevelCapTemplateRowDto(e.Label,e.Kind,
                e.FlagOffset,e.FlagBit,e.Cap)).ToList()
        };
        File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(dto,new JsonSerializerOptions{WriteIndented=true}));
        _status.Text="Template exportado con todas las flags y condiciones.";
    }
    private void Prepare()
    {
        UiProgress.Begin("Verificando Battle.cro y los hooks de code.bin…");
        try
        {
            _preview=_service.Preview(GetTable());
            _status.Text="Vista previa congelada (sin escribir): "+_preview.Report;
            _apply.IsEnabled=_preview.Files.Any(x=>x.Changed);
        }
        finally{UiProgress.End();}
    }
    private void Apply()
    {
        if(_preview is not { } proposed)return;
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Instalar Player Level Caps",
            "Se escribirán Battle.cro y code.bin de forma transaccional con sus checkpoints, " +
            "flags y niveles. Revisá la vista previa antes de confirmar."))return;
        UiProgress.Begin("Instalando Player Level Caps y verificando ambos ejecutables…");
        try
        {
            _service.Apply(proposed);Invalidate();_changed=false;
            _status.Text="Player Level Caps instalado y verificado. "+_service.Status;
        }
        finally{UiProgress.End();}
    }
    private sealed record PresetChoice(int Shift,string Label);
    private sealed class CapRow : System.ComponentModel.INotifyPropertyChanged
    {
        internal CapRow(LevelCapEntry e)
        {Label=e.Label;Kind=e.Kind;Offset=e.FlagOffset;Bit=e.FlagBit;Cap=e.Cap;}
        public string Label{get;}
        private LevelCapConditionKind _kind;
        public LevelCapConditionKind Kind{get=>_kind;set{_kind=value;Changed(nameof(Kind));}}
        private int _offset,_bit,_cap;
        public int Offset{get=>_offset;set{_offset=value;Changed(nameof(Offset));}}
        public int Bit{get=>_bit;set{_bit=value;Changed(nameof(Bit));}}
        public int Cap{get=>_cap;set{_cap=value;Changed(nameof(Cap));}}
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string p)=>PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(p));
    }
    private sealed class LevelCapTemplateDto
    {
        public int Version{get;set;}=1;
        public string Name{get;set;}="Player Level Caps";
        public string Game{get;set;}="USUM";
        public int PresetShift{get;set;}
        public int FinalCap{get;set;}=LevelCapTable.ResearchFinalCap;
        public List<LevelCapTemplateRowDto> Entries{get;set;}=[];
    }
    private sealed record LevelCapTemplateRowDto(string Label,LevelCapConditionKind Kind,
        ushort FlagOffset,ushort FlagBit,byte Cap);
}
