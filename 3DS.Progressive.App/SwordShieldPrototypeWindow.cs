using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using Label = System.Windows.Controls.Label;

namespace ThreeDS.Progressive;

/// <summary>Deliberately separate from the 3DS session until the Switch provider is validated in-game.</summary>
internal sealed partial class SwordShieldPrototypeWindow : Window
{
    internal Action? EmbeddedClose {get;set;}
    internal Action<string>? EmbeddedNavigate {get;set;}
    internal bool IsWorking=>_busy;
    private Window DialogOwner=>IsVisible?this:Owner??this;
    private void CloseEditor(){if(EmbeddedClose is not null)EmbeddedClose();else Close();}
    internal FrameworkElement TakePage(){var page=(FrameworkElement)Content;void ClearCancel(DependencyObject node){if(node is Button b)b.IsCancel=false;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())ClearCancel(child);}ClearCancel(page);_open.Visibility=_export.Visibility=_gameplayButton.Visibility=_pokeRuntimeButton.Visibility=_closeWindow.Visibility=Visibility.Collapsed;Content=null;return page;}
    internal event Action? ProjectChanged;
    internal event Action? SessionChanged;
    internal SwordShieldProject? Project=>_project;
    internal Task OpenEmbeddedAsync()=>OpenAsync();
    internal Task ExportEmbeddedAsync()=>ExportAsync();
    internal bool CanLeave()=>!_busy&&ConfirmDiscard();
    internal void RefreshSession(){RefreshEntries();RefreshValue();}
    private SwordShieldProject? _project;
    private SwordShieldEditPreview? _preview;
    private bool _busy;
    private string? _lastExportHash;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _entries = new() { DisplayMemberPath = "Label", MinHeight = 120 };
    private readonly ComboBox _field = new() { IsEditable = false, MinWidth = 160 };
    private readonly TextBox _value = new() { MinWidth = 100 };
    private readonly TextBlock _range = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _changes = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly Button _open, _prepare, _apply, _reset, _export;
    private readonly Button _closeWindow;
    internal SwordShieldPrototypeWindow()
    {
        Title = "Espada/Escudo · Editor Switch"; Width = 950; Height = 700; MinWidth = 580; MinHeight = 400;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "TextMain");
        var root = new Grid { Margin = new Thickness(18),ClipToBounds=true }; Content = root;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Datos de Pokémon · Espada/Escudo", FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = "Edita la entrada seleccionada o configura la randomización. Los cambios se añaden al mod; tu extracción permanece en lectura.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        _status.Text = "Abre una carpeta extraída con romfs y exefs. Los paquetes NSP/XCI requieren extracción previa.";
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite); header.Children.Add(_status);
        var body = new Grid { Margin = new Thickness(0, 15, 0, 10) }; Grid.SetRow(body, 1); root.Children.Add(body);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(240), MinWidth = 180, MaxWidth=280 });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        body.ColumnDefinitions.Add(new(){Width=new GridLength(210),MinWidth=180,MaxWidth=245});
        var listPanel=new DockPanel();var listHeader=new StackPanel{Margin=new Thickness(0,0,0,10)};listHeader.Children.Add(new TextBlock{Text="Especies y formas",Margin=new Thickness(0,0,0,8)});listHeader.Children.Add(_personalSearch);DockPanel.SetDock(listHeader,Dock.Top);listPanel.Children.Add(listHeader);listPanel.Children.Add(_entries);body.Children.Add(listPanel);AutomationProperties.SetName(_personalSearch,"Buscar especie o forma");_personalSearch.TextChanged+=(_,_)=>FilterPersonalEntries(); AutomationProperties.SetName(_entries, "Entradas presentes en Espada/Escudo; identificadas por especie, forma y entrada");
        VirtualizingStackPanel.SetIsVirtualizing(_entries, true); ScrollViewer.SetHorizontalScrollBarVisibility(_entries, ScrollBarVisibility.Disabled);
        _entries.DisplayMemberPath="";var entryPanel=new FrameworkElementFactory(typeof(StackPanel));var entryTitle=new FrameworkElementFactory(typeof(TextBlock));var titleBinding=new System.Windows.Data.MultiBinding{StringFormat="{0:000} {1}"};titleBinding.Bindings.Add(new System.Windows.Data.Binding("Species"));titleBinding.Bindings.Add(new System.Windows.Data.Binding("Name"));entryTitle.SetBinding(TextBlock.TextProperty,titleBinding);entryTitle.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);entryTitle.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);entryPanel.AppendChild(entryTitle);
        var entryDetails=new FrameworkElementFactory(typeof(TextBlock));var detailsBinding=new System.Windows.Data.MultiBinding{StringFormat="Forma {0} · BST {1} · #{2:0000}"};detailsBinding.Bindings.Add(new System.Windows.Data.Binding("Form"));detailsBinding.Bindings.Add(new System.Windows.Data.Binding("BST"));detailsBinding.Bindings.Add(new System.Windows.Data.Binding("Index"));entryDetails.SetBinding(TextBlock.TextProperty,detailsBinding);entryDetails.SetValue(TextBlock.FontSizeProperty,11.0);entryDetails.SetValue(FrameworkElement.MarginProperty,new Thickness(0,3,0,0));entryDetails.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");entryPanel.AppendChild(entryDetails);_entries.ItemTemplate=new DataTemplate{VisualTree=entryPanel};
        var entryStyle=new Style(typeof(ListBoxItem),(Style)FindResource(typeof(ListBoxItem)));entryStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty,new Thickness(9,7,9,7)));entryStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding("Label")));entryStyle.Setters.Add(new Setter(AutomationProperties.NameProperty,new System.Windows.Data.Binding("Label")));_entries.ItemContainerStyle=entryStyle;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(16, 0, 0, 0) }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var editor = new StackPanel(); scroll.Content = editor;
        BuildPersonalForm(editor);
        var advanced=new StackPanel();editor.Children.Add(new Expander{Header="Edición por campo",Content=advanced,Margin=new Thickness(0,0,0,12)});
        _field.ItemsSource = new[] { "PS", "Ataque", "Defensa", "Velocidad", "Ataque Especial", "Defensa Especial", "Primer tipo", "Segundo tipo", "Ratio de captura", "Habilidad 1", "Habilidad 2", "Habilidad oculta", "Objeto 1", "Objeto 2", "Objeto 3", "Género", "Ciclos de eclosión", "Amistad base", "Crecimiento", "Grupo huevo 1", "Grupo huevo 2", "Color", "Experiencia base", "Altura", "Peso", "EV PS", "EV Ataque", "EV Defensa", "EV Velocidad", "EV Ataque Especial", "EV Defensa Especial" }; _field.SelectedIndex = 0;
        AddLabel(advanced, "Campo", _field); advanced.Children.Add(_field);
        AddLabel(advanced, "Nuevo valor", _value); advanced.Children.Add(_value); advanced.Children.Add(_range);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) }; editor.Children.Add(actions);
        _prepare = AddButton(actions, "Previsualizar cambio", Prepare);
        _apply = AddButton(actions, "Añadir al mod", Apply, true);
        var randomOptions=new StackPanel();BuildRandomization(randomOptions);BuildGameplay(randomOptions);
        var randomDialog=new Button{Content="Randomizar Pokémon",Margin=new Thickness(0,0,0,12)};EditorPresentation.StyleAction(randomDialog,EditorPresentation.ActionRole.Secondary);_personalRandomButton=randomDialog;var inspectorPanel=new StackPanel{Margin=new Thickness(14)};inspectorPanel.Children.Add(randomDialog);var modify=new Button{Content="Modificar Pokémon",Margin=new Thickness(0,0,0,10)};_personalModifyButton=modify;modify.Click+=(_,_)=>ShowModifyPersonal();inspectorPanel.Children.Add(modify);BuildPersonalInspector(inspectorPanel);var inspector=new Expander{Header="Inspector",IsExpanded=true,Content=new ScrollViewer{Content=inspectorPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Margin=new Thickness(14,0,0,0)};Grid.SetColumn(inspector,2);body.Children.Add(inspector);AdaptiveLayout.SetCompactWidth(body,880);AdaptiveLayout.ConfigureInspector(body,inspector);
        randomDialog.Click+=(_,_)=>ShowRandomOptions(randomOptions);
        AutomationProperties.SetName(_changes, "Vista previa e historial del mod; no modifica la extracción"); editor.Children.Add(new Expander{Header="Vista previa e historial del mod",IsExpanded=false,Content=_changes,Margin=new Thickness(0,12,0,0)});
        AddButton(editor,"Ver historial completo",()=>{if(_project is null)return;if(EmbeddedNavigate is not null){EmbeddedNavigate("History");return;}var window=new Window{Title="Historial de cambios",Width=1000,Height=700,Content=new SwitchHistoryView(_project)};window.Owner=DialogOwner;window.ShowDialog();});
        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0),HorizontalAlignment=System.Windows.HorizontalAlignment.Right }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        _open = AddButton(footer, "Abrir extracción…", () => _ = OpenAsync());
        actions.Children.Remove(_apply); footer.Children.Add(_apply);
        _reset = AddButton(advanced, "Descartar cambios del mod", Reset);
        _export = AddButton(footer, "Exportar mod…", () => _ = ExportAsync(), true);
        AddButton(footer,"Restablecer campos",RefreshValue);
        _closeWindow = AddButton(footer, "Cerrar", CloseEditor); _closeWindow.IsCancel = true;
        _entries.SelectionChanged += (_, _) => OnPersonalSelection(); _field.SelectionChanged += (_, _) => RefreshValue();
        _value.TextChanged += (_, _) => { _preview = null; RefreshButtons(); };
        Closing += (_, e) => { if (EmbeddedClose is null&&(_busy || !ConfirmDiscard())) e.Cancel = true; };
        NameScope.SetNameScope(this, new NameScope());
        RegisterRandomNames();
        for(int f=0;f<SwordShieldPersonalTable.FieldCount;f++)RegisterName("SwitchPersonalField"+f,_personalFields[f]);RegisterName("SwitchPersonalSprite",_personalSprite);RegisterName("SwitchPersonalBst",_personalBst);RegisterName("SwitchPersonalState",_personalState);
        RegisterName("SwitchEntries", _entries); RegisterName("SwitchField", _field); RegisterName("SwitchValue", _value);
        RegisterName("SwitchPreview", _prepare); RegisterName("SwitchApply", _apply); RegisterName("SwitchExport", _export);
        RegisterName("SwitchChanges", _changes); RegisterName("SwitchStatus", _status);
        Loaded += (_, _) => _open.Focus(); RefreshButtons();
    }
    private static void AddLabel(Panel panel, string text, UIElement target)
    { var label = new Label { Content = text, Target = target, Padding = new Thickness(0, 6, 0, 4) }; AutomationProperties.SetLabeledBy(target, label); panel.Children.Add(label); }
    private static Button AddButton(Panel panel, string text, Action action, bool primary = false)
    { var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) }; EditorPresentation.StyleAction(button, primary ? EditorPresentation.ActionRole.Primary : EditorPresentation.ActionRole.Secondary); button.Click += (_, _) => action(); panel.Children.Add(button); return button; }
    private bool ConfirmDiscard() => (!_formDirty&&(_project?.HasChanges != true || _project.RevisionHash == _lastExportHash)) || StudioDialog.Confirm(DialogOwner, "Cambios del mod", "Hay cambios sin exportar en esta sesión. ¿Quieres descartarlos?");
    private async Task OpenAsync()
    {
        if (_busy || !ConfirmDiscard()) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Selecciona la extracción de Espada/Escudo con romfs y exefs.", ShowNewFolderButton = false };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        await OpenPathAsync(dialog.SelectedPath);
    }
    internal async Task OpenPathAsync(string root)
    {
        if (_busy) return;
        _busy = true; RefreshButtons();
        try
        {
            var candidate = await Task.Run(() => SwordShieldProject.Open(root));
            PresentProject(candidate);
        }
        catch (Exception ex) { StudioDialog.Error(DialogOwner, "No se pudo abrir Espada/Escudo", ex.Message); }
        finally { _busy = false; RefreshButtons(); }
    }
    internal void PresentProject(SwordShieldProject candidate)
    {
        _formDirty=false;_project = candidate; _lastExportHash = candidate.SourceHash; _preview = null; _entries.ItemsSource = candidate.Entries; _entries.SelectedIndex = 0;
        var identity = candidate.Identity;
        _status.Text=$"{identity.Game} · {candidate.Entries.Count} entradas · "+(identity.CanEdit?"Perfil 1.3.2":"Build ID desconocido · solo lectura");
        _status.ToolTip=$"{identity.TitleId}\nBuild ID: {identity.BuildId}\nComprueba que RomFS incluya la misma actualización. Las licencias DLC y la jugabilidad no están verificadas.";
        _status.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        _seed.Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture);RefreshValue();ProjectChanged?.Invoke();
    }
    private void RefreshValue()
    {
        _preview = null;
        RefreshPersonalForm();
        if (_project is not null && _entries.SelectedItem is SwordShieldPersonalEntry selected && _field.SelectedIndex >= 0)
            _value.Text = _project.Read(selected.Index).Values[_field.SelectedIndex].ToString(CultureInfo.InvariantCulture);
        _range.Text = _field.SelectedIndex is 6 or 7 ? "Rango: 0–17 · Normal, Lucha, Volador, Veneno, Tierra, Roca, Bicho, Fantasma, Acero, Fuego, Agua, Planta, Eléctrico, Psíquico, Hielo, Dragón, Siniestro, Hada." : _field.SelectedIndex is >=9 and <=11 ? "Rango: 1–267 · ID de habilidad Gen8" : $"Rango: {SwordShieldPersonalTable.Range((SwordShieldField)Math.Max(0,_field.SelectedIndex)).Min}–{SwordShieldPersonalTable.Range((SwordShieldField)Math.Max(0,_field.SelectedIndex)).Max}";
        RefreshJournal(); RefreshButtons();
    }
    private void Prepare()
    {
        try
        {
            if (_project is null || _entries.SelectedItem is not SwordShieldPersonalEntry selected) return;
            if(_formDirty){_preview=_project.PreviewPersonal(selected.Index,ReadPersonalForm());RefreshJournal();RefreshButtons();return;}
            if (!int.TryParse(_value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw new InvalidDataException("Introduce un número entero en el rango indicado.");
            _preview = _project.Preview(selected.Index, (SwordShieldField)_field.SelectedIndex, value);
            RefreshJournal(); RefreshButtons();
        }
        catch (Exception ex) { StudioDialog.Error(DialogOwner, "Vista previa", ex.Message); }
    }
    private void Apply()
    {
        try { if(_formDirty)Prepare(); if (_project is null || _preview is null) return; _project.Apply(_preview); _formDirty=false;_preview = null; RefreshEntries(); RefreshValue(); }
        catch (Exception ex) { StudioDialog.Error(DialogOwner, "No se pudo añadir el cambio", ex.Message); }
    }
    private void RefreshEntries()
    { if(_project is null)return;int selected=_entries.SelectedIndex;_entries.ItemsSource=_project.Entries;_entries.SelectedIndex=selected; }
    private void Reset()
    { if (_busy || !ConfirmDiscard()) return; _formDirty=false;_project?.Reset(); _preview = null; RefreshEntries(); RefreshValue(); }
    private void RefreshJournal()
    {
        int count=_project is null?0:_project.History.Count+_project.DataHistory.Sum(h=>h.Changes.Count)+_project.GameplayHistory.Sum(h=>h.Changes.Count)+_project.PokeFeurHistory.Sum(h=>h.Changes.Count);
        _changes.Text=$"{count} cambios registrados. Usa Ver historial completo para consultar o exportar el detalle.";
        if(_project is not null)_changes.Text+="\n"+string.Join("\n",_project.Randomizations.TakeLast(3).Select(h=>$"Semilla {h.Options.Seed} · {h.ChangedValues} valores"));
        if(_preview?.RandomOptions is {} random)_changes.Text+=$"\nRandomización: semilla {random.Seed} · {_preview.EligibleEntries} entradas elegibles · {_preview.ProtectedSpecies.Count} especies protegidas.";
        if(_preview is not null)_changes.Text+="\nPendiente de añadir: "+_preview.Changes.Count+" cambios.\n"+string.Join("\n",_preview.Changes.Take(50).Select(c=>$"Entrada {c.Entry} · {FieldLabel(c.Field)}: {c.Before} → {c.After}"))+(_preview.Changes.Count>50?"\n… Vista previa resumida.":"");
    }

    internal string FieldLabel(string field) => Enum.TryParse<SwordShieldField>(field, out var f) ? ((string[])_field.ItemsSource)[(int)f] : field;
    private async Task ExportAsync()
    {
        if (_busy || _project is null || !_project.HasChanges) return;
        if(_formDirty){StudioDialog.Error(DialogOwner,"Campos pendientes","Guarda o restablece los campos de Pokémon antes de exportar.");return;}
        if (_preview?.Changes.Any(c=>c.Before!=c.After)==true) { StudioDialog.Error(DialogOwner, "Cambio pendiente", "Añade el cambio de la vista previa al mod o descártalo antes de exportar."); return; }
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Selecciona la carpeta donde crear una carpeta NUEVA para el mod.", ShowNewFolderButton = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string target = Path.Combine(dialog.SelectedPath, $"Definitive-SWSH-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        _busy = true; RefreshButtons();
        try
        {
            var project = _project;
            var layout = _exportLayout.SelectedIndex == 1 ? Services.Switch.SwitchModLayout.Ryujinx : Services.Switch.SwitchModLayout.Atmosphere;
            var result = await Task.Run(() => project.Export(target,layout));
            _lastExportHash = result.ResourceSha256;
            string sdStatus="";
            if(project.PokeFeurEnabled)
            {
                sdStatus=project.RuntimeSdStatus(SwordShieldProject.DefaultRyujinxSd);
                if(Directory.Exists(SwordShieldProject.DefaultRyujinxSd))
                {
                    var plan=project.PreviewPokeSd(SwordShieldProject.DefaultRyujinxSd);
                    if(plan.ExistingHash!=plan.NewHash&&StudioDialog.Confirm(DialogOwner,"Instalar configuración del mod",sdStatus+"\n\n¿Instalar ahora la configuración que acabas de exportar? Se conservará una copia de la anterior. Cierra el juego antes de continuar.","Destino: "+plan.Destination+"\nCopia: "+plan.Backup))
                    {project.InstallPokeSd(plan,plan.ExistingHash is not null);sdStatus=project.RuntimeSdStatus(SwordShieldProject.DefaultRyujinxSd);}
                }
            }
            StudioDialog.Notice(DialogOwner, "Mod exportado", $"{result.ChangedValues} valores modificados.\n\n{result.Folder}\n\n{sdStatus}\n\nEl manifiesto incluye el historial. Si añadiste PokeFeur, instala también sdcard/config/swsh-mods-exl/config.toml desde la ventana del runtime o siguiendo INSTALAR-CONFIGURACION-SD.txt. Las funciones nuevas requieren prueba jugable.");
        }
        catch (Exception ex) { StudioDialog.Error(DialogOwner, "No se pudo exportar el mod", ex.Message); }
        finally { _busy = false; RefreshButtons(); }
    }
    private void RefreshButtons()
    {
        if (_open is null) return;
        bool edit = !_busy && _project?.Identity.CanEdit == true;
        if(_personalModifyButton is not null)_personalModifyButton.IsEnabled=edit;if(_personalRandomButton is not null)_personalRandomButton.IsEnabled=edit;
        _open.IsEnabled = !_busy; _entries.IsEnabled = !_busy; _field.IsEnabled = edit; _value.IsEnabled = edit;
        RefreshRandomButtons(edit);
        if(_project?.SurpriseEnabled==true||_project?.PokeFeurEnabled==true){_exportLayout.SelectedIndex=1;_exportLayout.IsEnabled=false;}
        _gameplayButton.IsEnabled = edit;
        _pokeRuntimeButton.IsEnabled=edit;
        _prepare.IsEnabled = edit && _entries.SelectedItem is not null;
        _apply.Content="Guardar en el proyecto";
        _apply.IsEnabled = edit && (_formDirty || _preview is not null && _preview.Changes.Any(c => c.Before != c.After));
        foreach(var control in _personalFields)control.IsEnabled=edit;
        _reset.IsEnabled = edit && _project!.HasChanges; _export.IsEnabled = edit && _project!.HasChanges && _preview?.Changes.Any(c=>c.Before!=c.After)!=true;SessionChanged?.Invoke();
    }
}
