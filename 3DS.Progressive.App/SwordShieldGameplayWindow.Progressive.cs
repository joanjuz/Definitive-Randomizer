using Binding=System.Windows.Data.Binding;
using Control=System.Windows.Controls.Control;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using pk3DS.Core.NX;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using DataGrid=System.Windows.Controls.DataGrid;
namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldGameplayWindow
{
    private readonly TextBlock _sectionTitle=new(){Text="Entrenadores, encuentros y Field Items",FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel _trainerSettings=new(),_bstSettings=new();
    private readonly CheckBox _progressiveBst=new(){Content="Usar rangos Progressive BST por nivel"},_trainerSpecies=new(){Content="Randomizar especies",IsChecked=true},_betterMoves=new(){Content="Better Movesets"},_strongStat=new(){Content="Usar el ataque más fuerte"},_allowStatus=new(){Content="Permitir movimientos de estado",IsChecked=true},_preferStab=new(){Content="Priorizar STAB",IsChecked=true},_smartItems=new(){Content="Smart Items"};
    private readonly TextBox _minimumPower=new(){Text="0"},_mixedTolerance=new(){Text="15"},_levelPercent=new(){Text="100"},_bannedMoves=new(),_doubleChance=new(){Text="0"};
    private readonly ComboBox _smartMode=new(){ItemsSource=new[]{"Normal","Relevantes","Competitivos"},SelectedIndex=2};
    private readonly ObservableCollection<BstRow> _ranges=new(SwordShieldBstRange.Defaults.Select(r=>new BstRow{MinLevel=r.MinLevel,MaxLevel=r.MaxLevel,MinBst=r.MinBst,MaxBst=r.MaxBst}));
    private readonly ObservableCollection<TrainerLevelRow> _trainerLevels=new();
    private sealed class BstRow { public int MinLevel{get;set;} public int MaxLevel{get;set;} public int MinBst{get;set;} public int MaxBst{get;set;} public bool FullRandom{get;set;} }
    private sealed class TrainerLevelRow { public int TrainerId{get;set;} public string Name{get;set;}=""; public int Level{get;set;} }
    private DataGrid OptionTable(string name)
    {
        var table=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,MinHeight=180,MaxHeight=300,FrozenColumnCount=1,Margin=new Thickness(0,8,0,12)};DarkGridTheme.Apply(table);AutomationProperties.SetName(table,name);table.CellEditEnding+=(_,_)=>Invalidate();return table;
    }
    private static void TextColumn(DataGrid grid,string heading,string property,bool readOnly=false)
        =>grid.Columns.Add(new DataGridTextColumn{Header=heading,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true},IsReadOnly=readOnly,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=60});
    private void BuildProgressiveOptions(StackPanel top)
    {
        top.Children.Add(_bstSettings);_bstSettings.Children.Add(_progressiveBst);var grid=OptionTable("Rangos BST por nivel");grid.ItemsSource=_ranges;
        TextColumn(grid,"Nivel mínimo",nameof(BstRow.MinLevel));TextColumn(grid,"Nivel máximo",nameof(BstRow.MaxLevel));TextColumn(grid,"BST mínimo",nameof(BstRow.MinBst));TextColumn(grid,"BST máximo",nameof(BstRow.MaxBst));grid.Columns.Add(new DataGridCheckBoxColumn{Header="Full Random",Binding=new Binding(nameof(BstRow.FullRandom)){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
        var ranges=new Expander{Header="Rangos Progressive BST",Content=grid,IsExpanded=false};_bstSettings.Children.Add(ranges);_progressiveBst.Checked+=(_,_)=>{ranges.IsExpanded=true;_options[5].IsEnabled=false;Invalidate();};_progressiveBst.Unchecked+=(_,_)=>{_options[5].IsEnabled=true;Invalidate();};
        top.Children.Add(_trainerSettings);var trainerPanel=new StackPanel();_trainerSettings.Children.Add(new Expander{Header="Opciones de entrenadores",Content=trainerPanel,IsExpanded=true});
        foreach(var option in new[]{_trainerSpecies,_betterMoves,_strongStat,_allowStatus,_preferStab,_smartItems}){trainerPanel.Children.Add(option);option.Checked+=(_,_)=>Invalidate();option.Unchecked+=(_,_)=>Invalidate();}
        void Field(string label,Control control){var caption=new System.Windows.Controls.Label{Content=label,Target=control};AutomationProperties.SetLabeledBy(control,caption);trainerPanel.Children.Add(caption);trainerPanel.Children.Add(control);if(control is TextBox box)box.TextChanged+=(_,_)=>Invalidate();else if(control is ComboBox combo)combo.SelectionChanged+=(_,_)=>Invalidate();}
        Field("Probabilidad de combate doble (0–100 %; equipos de al menos dos Pokémon)",_doubleChance);Field("Potencia mínima (0–255; 0 = sin mínimo)",_minimumPower);Field("Tolerancia mixto (0–255)",_mixedTolerance);Field("Smart Items · modo",_smartMode);Field("Nivel de entrenadores (%)",_levelPercent);Field("Movimientos prohibidos · IDs separados por comas",_bannedMoves);
        trainerPanel.Children.Add(new TextBlock{Text="Los movimientos se eligen entre los aprendizajes de la especie final al nivel final, sin repeticiones. Las restricciones incompatibles se informan antes de aplicar. Smart Items evalúa esos movimientos; no asigna objetos de elección o Chaleco Asalto a equipos con movimientos de estado.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});
        var levels=OptionTable("Niveles exactos por entrenador importante");levels.ItemsSource=_trainerLevels;TextColumn(levels,"ID",nameof(TrainerLevelRow.TrainerId));TextColumn(levels,"Entrenador",nameof(TrainerLevelRow.Name),true);TextColumn(levels,"Nivel exacto",nameof(TrainerLevelRow.Level));levels.CanUserAddRows=true;levels.CanUserDeleteRows=true;
        var levelPanel=new StackPanel();var load=new Button{Content="Leer entrenadores importantes"};load.Click+=async(_,_)=>await ReadImportantTrainers();levelPanel.Children.Add(load);levelPanel.Children.Add(levels);var caps=new Button{Content="Level caps y EXP · a partir de entrenadores"};caps.Click+=(_,_)=>{if(EmbeddedNavigate is not null)EmbeddedNavigate("Caps");else{var runtime=new SwordPokeFeurWindow(_project){Owner=DialogOwner};runtime.SelectTab(2);runtime.ShowDialog();}};levelPanel.Children.Add(caps);trainerPanel.Children.Add(new Expander{Header="Entrenadores importantes y level caps",Content=levelPanel});
        AutomationProperties.SetName(_progressiveBst,"Usar rangos Progressive BST por nivel");
    }
    private async Task ReadImportantTrainers()
    {
        if(_busy)return;Busy(true);
        try{var rows=await Task.Run(()=>_project.PokeFeurTrainerCaps());_trainerLevels.Clear();foreach(var row in rows)_trainerLevels.Add(new(){TrainerId=row.Trainer,Name=_project.TrainerName(row.Trainer),Level=row.Level});Invalidate();}
        catch(Exception e){StudioDialog.Error(DialogOwner,"Entrenadores importantes",e.Message);}finally{Busy(false);}
    }
    private static int Number(TextBox box,string name)=>int.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)?value:throw new ArgumentException(name+": introduce un entero.");
    private IReadOnlyList<SwordShieldBstRange> ReadBstRanges()=>_ranges.Select(r=>new SwordShieldBstRange(r.MinLevel,r.MaxLevel,r.MinBst,r.MaxBst,r.FullRandom)).ToArray();
    private SwordShieldTrainerOptions ReadTrainerRules()=>new(){DoubleBattleChance=Number(_doubleChance,"Probabilidad de combate doble"),BetterMovesets=_betterMoves.IsChecked==true,StrongestAttackStat=_strongStat.IsChecked==true,AllowStatus=_allowStatus.IsChecked==true,PreferStab=_preferStab.IsChecked==true,SmartItems=_smartItems.IsChecked==true,SmartItemMode=_smartMode.SelectedIndex,MinMovePower=Number(_minimumPower,"Potencia"),MixedTolerance=Number(_mixedTolerance,"Tolerancia"),LevelPercent=Number(_levelPercent,"Nivel"),BannedMoves=_bannedMoves.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(s=>int.TryParse(s,out int m)?m:throw new ArgumentException("ID de movimiento inválido.")).ToArray(),Levels=_trainerLevels.Select(t=>new SwordShieldTrainerLevel(t.TrainerId,t.Level)).ToArray()};
}
