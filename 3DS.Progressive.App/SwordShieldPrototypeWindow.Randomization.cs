using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using pk3DS.Core.NX;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using Control = System.Windows.Controls.Control;

namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldPrototypeWindow
{
    private readonly CheckBox _randomStats = new() { Content = "Mezclar estadísticas (conservar BST)" };
    private readonly CheckBox _redistributeStats=new(){Content="Redistribuir estadísticas (conservar BST)"},_banTrapping=new(){Content="Prohibir Sombratrampa y Trampa Arena"},_randomCatch=new(){Content="Randomizar ratio de captura"},_randomItems=new(){Content="Randomizar objetos equipados"},_randomEgg=new(){Content="Randomizar grupos huevo"},_randomGrowth=new(){Content="Randomizar crecimiento"};
    private readonly TextBox _minimumCatch=new(){Text="45"};
    private readonly CheckBox _randomTypes = new() { Content = "Randomizar tipos" };
    private readonly CheckBox _randomAbilities = new() { Content = "Randomizar habilidades" };
    private readonly CheckBox _includeForms = new() { Content = "Incluir formas alternas", IsChecked = true };
    private readonly CheckBox _preserveTypeCount = new() { Content = "Conservar uno o dos tipos", IsChecked = true };
    private readonly CheckBox _distinctAbilities = new() { Content = "Habilidades normales distintas", IsChecked = true };
    private readonly CheckBox _allowWonderGuard = new() { Content = "Permitir Superguarda" };
    private readonly TextBox _seed = new() { Text = SwitchSeed.New32().ToString(CultureInfo.InvariantCulture), IsReadOnly=true, MinWidth = 100 };
    private readonly ComboBox _randomScope = new() { IsEditable = false, ItemsSource = new[] { "Todas las entradas presentes", "Solo la entrada seleccionada" }, SelectedIndex = 0 };
    private readonly ComboBox _exportLayout = new() { IsEditable = false, ItemsSource = new[] { "Atmosphère · SD", "Ryujinx · carpeta de mod" }, SelectedIndex = 0 };
    private Button _randomPrepare = null!;
    private void BuildRandomization(Panel parent)
    {
        var panel = new StackPanel(); var expander = new Expander { Header = "Randomizar Pokémon · Espada/Escudo", IsExpanded = false, Content = panel, Margin = new Thickness(0, 0, 0, 12) }; parent.Children.Add(expander);
        panel.Children.Add(_randomStats);panel.Children.Add(_redistributeStats); panel.Children.Add(_randomTypes); panel.Children.Add(_randomAbilities);panel.Children.Add(_banTrapping);panel.Children.Add(_randomCatch);AddLabel(panel,"Ratio de captura mínimo (1–255)",_minimumCatch);panel.Children.Add(_minimumCatch);panel.Children.Add(_randomItems);panel.Children.Add(_randomEgg);panel.Children.Add(_randomGrowth);
        AddLabel(panel, "Semilla aleatoria", _seed); panel.Children.Add(_seed);AddButton(panel,"Nueva semilla aleatoria",()=>_seed.Text=SwitchSeed.New32().ToString(CultureInfo.InvariantCulture));
        AddLabel(panel, "Alcance", _randomScope); panel.Children.Add(_randomScope);
        panel.Children.Add(_includeForms); panel.Children.Add(_preserveTypeCount); panel.Children.Add(_distinctAbilities); panel.Children.Add(_allowWonderGuard);
        panel.Children.Add(new TextBlock { Text = "Se usan habilidades presentes en la tabla original. Las especies con mecánicas de cambio de forma se conservan. No se heredan tipos o habilidades por evolución en esta fase. No marcar opciones produce cero cambios.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 8) });
        AddLabel(panel, "Formato de exportación", _exportLayout); panel.Children.Add(_exportLayout);
        _randomPrepare = AddButton(panel, "Previsualizar randomización", () => _ = PrepareRandomizationAsync());
        foreach(var box in new[] { _randomStats,_redistributeStats,_banTrapping,_randomCatch,_randomItems,_randomEgg,_randomGrowth,_randomTypes,_randomAbilities,_includeForms,_preserveTypeCount,_distinctAbilities,_allowWonderGuard })
        { box.Checked += (_,_)=>InvalidateRandomPreview(); box.Unchecked += (_,_)=>InvalidateRandomPreview(); }
        _minimumCatch.TextChanged+=(_,_)=>InvalidateRandomPreview();
        _seed.TextChanged += (_,_)=>InvalidateRandomPreview(); _randomScope.SelectionChanged += (_,_)=>InvalidateRandomPreview();
    }
    private void InvalidateRandomPreview()
    { _preview = null; if(_open is not null){RefreshJournal();RefreshButtons();} }
    private void RefreshRandomButtons(bool edit)
    {
        foreach(var control in new Control[] {_randomStats,_randomTypes,_randomAbilities,_includeForms,_seed,_randomScope})control.IsEnabled=edit;
        _redistributeStats.IsEnabled=edit&&_randomStats.IsChecked==true;_banTrapping.IsEnabled=edit&&_randomAbilities.IsChecked==true;_randomCatch.IsEnabled=_randomItems.IsEnabled=_randomEgg.IsEnabled=_randomGrowth.IsEnabled=edit;_minimumCatch.IsEnabled=edit&&_randomCatch.IsChecked==true;
        _preserveTypeCount.IsEnabled=edit&&_randomTypes.IsChecked==true;
        _distinctAbilities.IsEnabled=_allowWonderGuard.IsEnabled=edit&&_randomAbilities.IsChecked==true;
        _randomPrepare.IsEnabled=edit; _exportLayout.IsEnabled = !_busy;
    }
    internal async Task PrepareRandomizationAsync()
    {
        if(_busy||_project is null)return;
        if(_formDirty){StudioDialog.Error(DialogOwner,"Campos pendientes","Guarda o restablece los campos antes de randomizar.");return;}
        try
        {
            if(!uint.TryParse(_seed.Text,NumberStyles.None,CultureInfo.InvariantCulture,out uint seed))
                throw new ArgumentException("La semilla debe ser un entero entre 0 y 4294967295.");
            if(_project.Randomizations.LastOrDefault()?.Options.Seed==seed){seed=SwitchSeed.New32();_seed.Text=seed.ToString(CultureInfo.InvariantCulture);}
            int? selected = _randomScope.SelectedIndex==1?(_entries.SelectedItem as SwordShieldPersonalEntry)?.Index:null;
            if(_randomScope.SelectedIndex==1&&selected is null)throw new ArgumentException("Selecciona una entrada para ese alcance.");
            var options=new SwordShieldRandomOptions(seed,_randomTypes.IsChecked==true,_randomAbilities.IsChecked==true,
                _includeForms.IsChecked==true,_preserveTypeCount.IsChecked==true,_distinctAbilities.IsChecked==true,_allowWonderGuard.IsChecked==true,selected,_randomStats.IsChecked==true){RedistributeStats=_redistributeStats.IsChecked==true,BanTrappingAbilities=_banTrapping.IsChecked==true,CatchRate=_randomCatch.IsChecked==true,MinimumCatchRate=_randomCatch.IsChecked!=true?45:int.TryParse(_minimumCatch.Text,out int minimum)?minimum:throw new ArgumentException("Ratio mínimo inválido."),HeldItems=_randomItems.IsChecked==true,EggGroups=_randomEgg.IsChecked==true,Growth=_randomGrowth.IsChecked==true};
            _busy=true;RefreshButtons();
            var project=_project;_preview=await Task.Run(()=>project.PreviewRandom(options));
            RefreshJournal();
            if(IsVisible)await Dispatcher.InvokeAsync(()=>_changes.BringIntoView());
        }
        catch(Exception ex){StudioDialog.Error(DialogOwner,"No se pudo previsualizar la randomización",ex.Message);}
        finally{_busy=false;RefreshButtons();}
    }
    private void RegisterRandomNames()
    {
        RegisterName("SwitchRandomStats",_randomStats);RegisterName("SwitchRandomTypes",_randomTypes);RegisterName("SwitchRandomAbilities",_randomAbilities);
        RegisterName("SwitchSeed",_seed);RegisterName("SwitchRandomScope",_randomScope);
        RegisterName("SwitchIncludeForms",_includeForms);RegisterName("SwitchPreserveTypeCount",_preserveTypeCount);
        RegisterName("SwitchDistinctAbilities",_distinctAbilities);RegisterName("SwitchAllowWonderGuard",_allowWonderGuard);
        RegisterName("SwitchRandomPreview",_randomPrepare);RegisterName("SwitchExportLayout",_exportLayout);
    }
}
