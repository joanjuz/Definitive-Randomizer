using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using UserControl=System.Windows.Controls.UserControl;
using TabControl=System.Windows.Controls.TabControl;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using DataGrid=System.Windows.Controls.DataGrid;
using DataGridColumnHeader=System.Windows.Controls.Primitives.DataGridColumnHeader;
using DataGridRow=System.Windows.Controls.DataGridRow;
using Binding=System.Windows.Data.Binding;
using Control=System.Windows.Controls.Control;

namespace ThreeDS.Progressive;

/// <summary>Native WPF Gen7 TM table and actual tutor prices, without running old WinForms dialogs.</summary>
internal sealed class MachineTutorView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly CheckBox _recipeTutors=new(){Content="Randomizar tutores en Global/Batch"},_recipeFree=new(){Content="Tutores gratis en Global/Batch"};
    private CheckBox _tmSanity=null!,_tmFollow=null!,_tutorSanity=null!,_tutorFollow=null!;
    internal Gen7MachineState RememberRecipe()=>new(true,_tmSanity.IsChecked==true,_tmFollow.IsChecked==true,_recipeTutors.IsChecked==true,_recipeFree.IsChecked==true,_tutorSanity.IsChecked==true,_tutorFollow.IsChecked==true);
    internal void RestoreRecipe(Gen7MachineState s){_tmSanity.IsChecked=s.MachineSanity;_tmFollow.IsChecked=s.MachineFollow;_recipeTutors.IsChecked=s.RandomTutors;_recipeFree.IsChecked=s.FreeTutors;_tutorSanity.IsChecked=s.TutorSanity;_tutorFollow.IsChecked=s.TutorFollow;}
    private GameMachineTutorService _service;
    private readonly DataGrid _machineGrid,_tutorGrid;
    private readonly List<DataGrid> _machineColumns=[];
    private readonly List<DataGrid> _tutorColumns=[];
    private readonly Button _saveMachines,_savePrices;
    private readonly Button _applyTutorChanges;
    private TutorEditPreview? _tutorPreview;
    private bool _dirtyMachines,_dirtyPrices;
    private readonly TextBlock _status;
    public bool HasPendingChanges=>_dirtyMachines||_dirtyPrices;
    public bool ConfirmLeave()=>!HasPendingChanges||StudioDialog.Confirm(Window.GetWindow(this),
        "Cambios sin guardar","Hay modificaciones de MT o precios pendientes.","Si continuás, se descartarán los cambios.");

    internal MachineTutorView(GameProjectSession project)
    {
        _project=project;
        _service=new(project);
        var bg=(Brush)FindResource("Bg");
        var panel=(Brush)FindResource("Panel");
        var text=(Brush)FindResource("TextMain");
        var soft=(Brush)FindResource("TextSoft");
        var border=(Brush)FindResource("Stroke");
        Background=bg;
        var root=new DockPanel {Margin=new Thickness(22,20,22,20)};
        Content=root;
        var header=new StackPanel {Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock {Text="MT y tutores",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=text});
        header.Children.Add(new TextBlock {Text="Tablas reales del proyecto · code.bin y Shop.cro. Los cambios se verifican antes de guardarse.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),Foreground=soft});
        var footer=new StackPanel {Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        _status=new TextBlock {Text="Seleccioná un movimiento o cambiá precios y confirmá el guardado.",
            Foreground=soft,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        footer.Children.Add(_status);
        var tabs=new TabControl();
        root.Children.Add(tabs);
        var machines=new DockPanel {Margin=new Thickness(4)};
        var commands=new WrapPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(commands,Dock.Top);machines.Children.Add(commands);
        var random=new Button {Content="Randomizar MT",Margin=new Thickness(0,0,8,0)};
        random.Click+=(_,_)=>TryAction(()=>
        {
            _service.RandomizeMachines();
            foreach(var table in _machineColumns)table.Items.Refresh();
            _dirtyMachines=true;UpdateState();
            _status.Text="Asignaciones de MT aleatorias preparadas en memoria. Revisá antes de guardar.";
        });
        _saveMachines=new Button {Content="Guardar MT",IsEnabled=false};
        _saveMachines.Click+=(_,_)=>TryAction(()=>
        {
            if(_dirtyPrices)
                throw new InvalidOperationException("Primero guardá o descartá los cambios pendientes de tutores.");
            foreach(var table in _machineColumns)
            {
                table.CommitEdit(DataGridEditingUnit.Cell,true);
                table.CommitEdit(DataGridEditingUnit.Row,true);
            }
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar MT",
                "Se actualizarán code.bin y las descripciones de las MT en el idioma activo, con verificación e historial."))return;
            _service.SaveMachines(synchronizeDescriptions:true);_service=new GameMachineTutorService(_project);
            Rebind();_dirtyMachines=false;UpdateState();
            _status.Text="MT y descripciones guardadas con verificación conjunta e historial.";
        });
        commands.Children.Add(random);commands.Children.Add(_saveMachines);
        // Native Gen7 TM compatibility: independent from tutor Sanity flags.
        var tmCompatibilityControls=new StackPanel {Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(tmCompatibilityControls,Dock.Top);
        machines.Children.Add(tmCompatibilityControls);
        var tmFlags=new WrapPanel {Margin=new Thickness(0,0,0,8)};
        var tmSanity=new CheckBox
        {
            Content="TM Sanity · movimientos aprendidos por nivel",
            Foreground=text,Margin=new Thickness(0,3,18,3)
        };
        var tmFollow=new CheckBox
        {
            Content="TM Follow Evolutions · heredar compatibilidad",
            Foreground=text,Margin=new Thickness(0,3,18,3)
        };
        _tmSanity=tmSanity;_tmFollow=tmFollow;
        tmFlags.Children.Add(tmSanity);
        tmFlags.Children.Add(tmFollow);
        tmCompatibilityControls.Children.Add(tmFlags);
        GameTmCompatibilityService? tmCompatibilityService=null;
        TmCompatibilityPreview? tmCompatibilityPreview=null;
        var tmCompatibilityActions=new WrapPanel();
        tmCompatibilityControls.Children.Add(tmCompatibilityActions);
        var tmApplyCompatibility=new Button
        {
            Content="Aplicar compatibilidad MT",
            IsEnabled=false,Margin=new Thickness(0,0,8,0)
        };
        void ClearTmProposal()
        {
            tmCompatibilityService=null;tmCompatibilityPreview=null;
            tmApplyCompatibility.IsEnabled=false;
        }
        tmSanity.Checked+=(_,_)=>ClearTmProposal();
        tmSanity.Unchecked+=(_,_)=>ClearTmProposal();
        tmFollow.Checked+=(_,_)=>ClearTmProposal();
        tmFollow.Unchecked+=(_,_)=>ClearTmProposal();
        var tmPreviewCompatibility=new Button
        {
            Content="Vista previa compatibilidad MT",
            Margin=new Thickness(0,0,8,0)
        };
        tmPreviewCompatibility.Click+=(_,_)=>TryAction(()=>
        {
            foreach(var table in _machineColumns)
            {
                table.CommitEdit(DataGridEditingUnit.Cell,true);
                table.CommitEdit(DataGridEditingUnit.Row,true);
            }
            if(_dirtyMachines||_dirtyPrices)
                throw new InvalidOperationException(
                    "Guardá o descartá primero las ediciones de MT o tutores para usar sus datos activos.");
            tmCompatibilityService=new GameTmCompatibilityService(_project);
            tmCompatibilityPreview=tmCompatibilityService.Preview(
                tmSanity.IsChecked==true,tmFollow.IsChecked==true);
            tmApplyCompatibility.IsEnabled=tmCompatibilityPreview.Changes.Count>0;
            var examples=string.Join(Environment.NewLine,tmCompatibilityPreview.Changes.Take(16)
                .Select(d=>$"{d.Name}: {d.Before} → {d.After} compatibilidades MT"));
            _status.Text=$"TM Sanity: +{tmCompatibilityPreview.AddedSanityFlags} · " +
                $"Follow Evolutions: +{tmCompatibilityPreview.AddedInheritedFlags} · " +
                $"{tmCompatibilityPreview.Changes.Count} entradas afectadas. Sin cambios de archivos.";
            StudioDialog.Notice(Window.GetWindow(this),"Vista previa de compatibilidad MT",
                $"{tmCompatibilityPreview.Changes.Count} registros del GARC Personal cambiarán.",
                examples+(tmCompatibilityPreview.Changes.Count>16?Environment.NewLine+"…":""));
        });
        tmApplyCompatibility.Click+=(_,_)=>TryAction(()=>
        {
            if(tmCompatibilityService is null||tmCompatibilityPreview is null)return;
            if(_dirtyMachines||_dirtyPrices)
                throw new InvalidOperationException("Existen ediciones de MT/tutores pendientes.");
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar compatibilidad MT",
                $"¿Añadir compatibilidades a {tmCompatibilityPreview.Changes.Count} Pokémon/formas? " +
                "Solo se modifican los bits TMHM en el GARC Personal."))return;
            tmCompatibilityService.Apply(tmCompatibilityPreview);
            ClearTmProposal();
            _status.Text="TM Sanity/Follow Evolutions guardado y verificado en Personal.";
        });
        tmCompatibilityActions.Children.Add(tmPreviewCompatibility);
        tmCompatibilityActions.Children.Add(tmApplyCompatibility);
        tmCompatibilityControls.Children.Add(new TextBlock
        {
            Text="Sanity compara las MT actuales de code.bin con Level Up. " +
                "Follow Evolutions hereda compatibilidad por cadenas, formas y ramas.",
            Foreground=soft,TextWrapping=TextWrapping.Wrap
        });
        machines.Children.Add(new TextBlock
        {
            Text="Solo se cambia la asignación de MT. La sincronización automática del texto descriptivo de objetos y las opciones Sanity/Follow Evolutions aún están pendientes.",
            Foreground=soft,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,43,0,0),
            IsHitTestVisible=false,Visibility=Visibility.Collapsed
        });
        _machineGrid=BuildGrid();
        _machineGrid.Columns.Add(new DataGridTextColumn {Header="MT",Binding=new Binding("Number"),IsReadOnly=true,Width=72});
        _machineGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header="Movimiento",ItemsSource=_service.MoveChoices,DisplayMemberPath="Display",
            SelectedValuePath="Id",SelectedValueBinding=new Binding("MoveId")
            {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
            Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=300
        });
        _machineGrid.CellEditEnding+=(_,_)=>{_dirtyMachines=true;UpdateState();};
        var compact=new UniformGrid {Columns=3};
        _machineColumns.Add(_machineGrid);
        compact.Children.Add(_machineGrid);
        for(int n=1;n<3;n++)
        {
            var extra=BuildGrid();
            extra.Margin=new Thickness(7,0,0,0);
            extra.Columns.Add(new DataGridTextColumn
                {Header="MT",Binding=new Binding("Number"),IsReadOnly=true,Width=48});
            extra.Columns.Add(new DataGridComboBoxColumn
            {
                Header="Movimiento",ItemsSource=_service.MoveChoices,DisplayMemberPath="Display",
                SelectedValuePath="Id",SelectedValueBinding=new Binding("MoveId")
                {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
                Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=185
            });
            extra.CellEditEnding+=(_,_)=>{_dirtyMachines=true;UpdateState();};
            _machineColumns.Add(extra);
            compact.Children.Add(extra);
        }
        _machineGrid.Columns[0].Width=48;
        _machineGrid.Columns[1].MinWidth=185;
        machines.Children.Add(compact);
        tabs.Items.Add(new TabItem {Header="MT",Content=machines});

        var tutoring=new DockPanel {Margin=new Thickness(4)};
        var tutorHeader=new StackPanel {Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(tutorHeader,Dock.Top);tutoring.Children.Add(tutorHeader);
        tutorHeader.Children.Add(new TextBlock
        {
            Text="Tutores USUM: cambia los 67 movimientos y sus precios. Se verifican Shop.cro y la tabla de compatibilidad nativa de code.bin antes de aplicar los cambios.",
            Foreground=soft,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        var tutorActions=new WrapPanel();
        tutorHeader.Children.Add(tutorActions);
        var free=new Button {Content="Todos gratis (0 BP)",Margin=new Thickness(0,0,8,0)};
        free.Click+=(_,_)=>{foreach(var row in _service.Tutors)row.Price=0;_dirtyPrices=true;_tutorPreview=null;UpdateState();foreach(var t in _tutorColumns)t.Items.Refresh();};
        var randomTutors=new Button {Content="Randomizar tutores",Margin=new Thickness(0,0,8,0),IsEnabled=_service.CanEditTutorMoves};
        randomTutors.Click+=(_,_)=>TryAction(()=>
        {
            int changed=_service.RandomizeTutorMoves();
            _dirtyPrices=true;_tutorPreview=null;UpdateState();
            foreach(var t in _tutorColumns)t.Items.Refresh();
            _status.Text=$"{changed} movimientos de tutores preparados. Generá vista previa antes de aplicar.";
        });
        _savePrices=new Button {Content="Vista previa tutores",IsEnabled=false};
        _savePrices.Click+=(_,_)=>TryAction(()=>
        {
            CommitTutorGrids();
            if(_dirtyMachines)
                throw new InvalidOperationException("Primero guardá o descartá los cambios pendientes de MT.");
            if(!_service.CanEditTutorMoves)
            {
                if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar precios de tutores",
                    "Se actualizarán únicamente los precios de Shop.cro. ¿Continuar?"))return;
                _service.SaveTutorPrices();
                _service=new GameMachineTutorService(_project);
                _dirtyPrices=false;_tutorPreview=null;Rebind();
                _status.Text="Precios de tutores guardados y verificados.";
                return;
            }
            _tutorPreview=_service.PreviewTutorEdits();
            _applyTutorChanges.IsEnabled=_tutorPreview.Changes.Count>0;
            string details=string.Join(Environment.NewLine,_tutorPreview.Changes.Take(12)
                .Select(d=>$"{d.Name}: {d.Field} {d.Before} → {d.After}"));
            _status.Text=$"{_tutorPreview.Changes.Count} cambios · " +
                $"{_tutorPreview.CodeSlotsChanged} posiciones nativas en code.bin. " +
                "Revisá la vista previa antes de aplicar.";
            StudioDialog.Notice(Window.GetWindow(this),"Vista previa tutores",
                $"{_tutorPreview.Changes.Count} diferencias y {_tutorPreview.CodeSlotsChanged} sincronizaciones en code.bin.",
                details+(_tutorPreview.Changes.Count>12?Environment.NewLine+"…":""));
        });
        _applyTutorChanges=new Button {Content="Aplicar tutores y verificar",IsEnabled=false};
        _applyTutorChanges.Click+=(_,_)=>TryAction(()=>
        {
            CommitTutorGrids();
            if(_tutorPreview is null)return;
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar tutores USUM",
                "Se comprobarán y guardarán de forma conjunta Shop.cro y code.bin, " +
                "sin alterar las otras tiendas ni las MT. ¿Continuar?"))return;
            _service.ApplyTutorEdits(_tutorPreview);
            _service=new GameMachineTutorService(_project);
            _tutorPreview=null;_dirtyPrices=false;
            Rebind();UpdateState();
            _status.Text="Tutores USUM guardados; Shop.cro y code.bin sincronizados y verificados.";
        });
        foreach(var recipeOption in new[]{_recipeTutors,_recipeFree}){recipeOption.Foreground=text;recipeOption.Margin=new Thickness(8,4,0,0);recipeOption.Visibility=project.GameConfiguration.USUM?Visibility.Visible:Visibility.Collapsed;tutorActions.Children.Add(recipeOption);}
        tutorActions.Children.Add(free);
        tutorActions.Children.Add(randomTutors);
        tutorActions.Children.Add(_savePrices);
        tutorActions.Children.Add(_applyTutorChanges);
        // These commands modify Personal GARC compatibility, not Shop.cro or code.bin.
        // Keep the proposal isolated from unsaved TM/Tutor edits.
        var compatibilityOptions=new WrapPanel {Margin=new Thickness(0,10,0,8)};
        var sanityOption=new CheckBox
        {
            Content="Tutor Sanity · compatibilidad por nivel",
            Margin=new Thickness(0,4,18,4),Foreground=text,
            IsEnabled=_service.CanEditTutorMoves
        };
        var followOption=new CheckBox
        {
            Content="Follow Evolutions · heredar compatibilidad",
            Margin=new Thickness(0,4,18,4),Foreground=text,
            IsEnabled=_service.CanEditTutorMoves
        };
        _tutorSanity=sanityOption;_tutorFollow=followOption;
        compatibilityOptions.Children.Add(sanityOption);
        compatibilityOptions.Children.Add(followOption);
        compatibilityOptions.Visibility=project.GameConfiguration.USUM?Visibility.Visible:Visibility.Collapsed;
        tutorHeader.Children.Add(compatibilityOptions);
        var compatibilityActions=new WrapPanel {Margin=new Thickness(0,0,0,8)};
        tutorHeader.Children.Add(compatibilityActions);
        GameTutorCompatibilityService? compatibilityService=null;
        TutorCompatibilityPreview? compatibilityPreview=null;
        var applyCompatibility=new Button
        {
            Content="Aplicar compatibilidad",
            IsEnabled=false,Margin=new Thickness(0,0,8,0)
        };
        void InvalidateCompatibility()
        {
            compatibilityService=null;compatibilityPreview=null;
            applyCompatibility.IsEnabled=false;
        }
        sanityOption.Checked+=(_,_)=>InvalidateCompatibility();
        sanityOption.Unchecked+=(_,_)=>InvalidateCompatibility();
        followOption.Checked+=(_,_)=>InvalidateCompatibility();
        followOption.Unchecked+=(_,_)=>InvalidateCompatibility();
        var previewCompatibility=new Button
        {
            Content="Vista previa compatibilidad",
            IsEnabled=_service.CanEditTutorMoves,Margin=new Thickness(0,0,8,0)
        };
        previewCompatibility.Click+=(_,_)=>TryAction(()=>
        {
            CommitTutorGrids();
            if(_dirtyPrices||_dirtyMachines)
                throw new InvalidOperationException(
                    "Guardá o descartá primero los cambios pendientes en MT o movimientos/precios de tutores.");
            compatibilityService=new GameTutorCompatibilityService(_project);
            compatibilityPreview=compatibilityService.Preview(sanityOption.IsChecked==true,
                followOption.IsChecked==true);
            applyCompatibility.IsEnabled=compatibilityPreview.Changes.Count>0;
            string examples=string.Join(Environment.NewLine,compatibilityPreview.Changes.Take(16)
                .Select(d=>$"{d.Name}: {d.Before} → {d.After} compatibilidades"));
            _status.Text=$"Tutor Sanity: +{compatibilityPreview.AddedSanityFlags} · " +
                $"Follow Evolutions: +{compatibilityPreview.AddedInheritedFlags} · " +
                $"{compatibilityPreview.Changes.Count} Pokémon/formas afectados. " +
                "No hay archivos modificados todavía.";
            StudioDialog.Notice(Window.GetWindow(this),"Vista previa de compatibilidad",
                $"Cambios preparados para {compatibilityPreview.Changes.Count} entradas de Personal.",
                examples+(compatibilityPreview.Changes.Count>16?Environment.NewLine+"…":""));
        });
        applyCompatibility.Click+=(_,_)=>TryAction(()=>
        {
            if(compatibilityService is null||compatibilityPreview is null)return;
            if(_dirtyPrices||_dirtyMachines)
                throw new InvalidOperationException("Hay cambios de MT/tutores pendientes.");
            if(!StudioDialog.Confirm(Window.GetWindow(this),"Aplicar compatibilidad",
                $"¿Actualizar {compatibilityPreview.Changes.Count} entradas del GARC Personal? " +
                "Los archivos Shop.cro, code.bin, Level Up y Evoluciones permanecerán intactos."))
                return;
            compatibilityService.Apply(compatibilityPreview);
            InvalidateCompatibility();
            _status.Text="Compatibilidad de tutores guardada, verificada y actualizada en Pokémon.";
        });
        compatibilityActions.Children.Add(previewCompatibility);
        compatibilityActions.Children.Add(applyCompatibility);
        tutorHeader.Children.Add(new TextBlock
        {
            Text="Sanity activa tutores para movimientos que el Pokémon aprende por nivel; " +
                 "Follow Evolutions hereda la compatibilidad de preevoluciones. " +
                 "Ambas acciones afectan únicamente compatibilidades del GARC Personal.",
            Foreground=soft,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,8)
        });
        var tutorColumns=new UniformGrid {Columns=2};
        for(int i=0;i<2;i++)
        {
            var table=BuildGrid();
            table.Margin=new Thickness(i==0?0:9,0,0,0);
            table.Columns.Add(new DataGridTextColumn
                {Header="N.º",Binding=new Binding("Number"),IsReadOnly=true,Width=46});
            table.Columns.Add(new DataGridTextColumn
                {Header="Sede",Binding=new Binding("Group"),IsReadOnly=true,Width=80});
            table.Columns.Add(new DataGridComboBoxColumn
                {
                    Header="Movimiento",ItemsSource=_service.MoveChoices,DisplayMemberPath="Display",
                    SelectedValuePath="Id",SelectedValueBinding=new Binding("MoveId")
                    {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},
                    Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=170
                });
            table.Columns.Add(new DataGridTextColumn
                {
                    Header="BP",Binding=new Binding("Price")
                    {Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,
                        ValidatesOnExceptions=true},Width=72
                });
            table.CellEditEnding+=(_,_)=>{_dirtyPrices=true;_tutorPreview=null;UpdateState();};
            _tutorColumns.Add(table);tutorColumns.Children.Add(table);
        }
        _tutorGrid=_tutorColumns[0];
        tutoring.Children.Add(tutorColumns);
        tabs.Items.Add(new TabItem {Header="Tutores · movimientos y BP",Content=tutoring});
        Rebind();
    }
    private void CommitTutorGrids()
    {
        foreach(var grid in _tutorColumns)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell,true);
            grid.CommitEdit(DataGridEditingUnit.Row,true);
        }
    }
    private DataGrid BuildGrid()
    {
        var grid=new DataGrid {AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            CanUserResizeColumns=true,CanUserSortColumns=true,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            RowBackground=(Brush)FindResource("Panel"),AlternatingRowBackground=(Brush)FindResource("Surface"),
            HorizontalGridLinesBrush=(Brush)FindResource("Stroke"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            RowHeight=34,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var style=new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)FindResource("Surface")));
        style.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        grid.ColumnHeaderStyle=style;
        var rowStyle=new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)FindResource("TextMain")));
        grid.RowStyle=rowStyle;
        return grid;
    }
    private void Rebind()
    {
        int chunk=(_service.Machines.Count+_machineColumns.Count-1)/_machineColumns.Count;
        for(int i=0;i<_machineColumns.Count;i++)
        {
            var table=_machineColumns[i];
            table.ItemsSource=_service.Machines.Skip(i*chunk).Take(chunk).ToArray();
            if(table.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault() is { } selector)
                selector.ItemsSource=_service.MoveChoices;
        }
        int count=(_service.Tutors.Count+_tutorColumns.Count-1)/_tutorColumns.Count;
        for(int i=0;i<_tutorColumns.Count;i++)
        {
            _tutorColumns[i].ItemsSource=_service.Tutors.Skip(i*count).Take(count).ToArray();
            if(_tutorColumns[i].Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault() is { } combo)
                combo.ItemsSource=_service.MoveChoices;
        }
        UpdateState();
    }
    private void UpdateState()
    {
        _saveMachines.IsEnabled=_dirtyMachines&&_service.CanEditTms;
        _savePrices.IsEnabled=_dirtyPrices&&_service.CanEditTutorPrices;
        _applyTutorChanges.IsEnabled=_tutorPreview is { Changes.Count: >0 } && _dirtyPrices;
    }
    private void TryAction(Action action)
    {
        try{action();}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo completar la acción",ex.Message);}
    }
}
