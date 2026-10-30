using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using TabControl=System.Windows.Controls.TabControl;
using Binding=System.Windows.Data.Binding;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Label=System.Windows.Controls.Label;
using DataGrid=System.Windows.Controls.DataGrid;
namespace ThreeDS.Progressive;
internal sealed class PokeFeurCapRow
{
    public string Stage {get;init;}="";
    public string Level {get;set;}="100";
}
internal sealed class SwordPokeFeurWindow:Window
{
    internal event Action? Changed;
    internal Action? EmbeddedClose {get;set;}
    internal Action<string>? EmbeddedNavigate {get;set;}
    internal bool IsWorking=>_busy;
    private Window DialogOwner=>IsVisible?this:Owner??this;
    private void CloseEditor(){if(EmbeddedClose is not null)EmbeddedClose();else Close();}
    internal FrameworkElement TakePage(){var page=(FrameworkElement)Content;void ClearCancel(DependencyObject node){if(node is Button b)b.IsCancel=false;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())ClearCancel(child);}ClearCancel(page);Content=null;return page;}
    internal void SelectTab(int index){_tabs.SelectedIndex=index;}
    private readonly SwordShieldProject _project;
    private PokeFeurPackage? _package;
    private PokeFeurPreview? _pending;
    private bool _busy;
    private readonly TabControl _tabs=new();
    private readonly TextBox _packagePath=new(){IsReadOnly=true},_seed=new(){IsReadOnly=true},_visiblePool=new(),_battlePool=new(),_multiplier=new(),_shiny=new(),_spawns=new();
    private readonly CheckBox _random=new(){Content="Activar randomizador de encuentros del runtime"},_caps=new(){Content="Activar level caps de EXP"},_autoCaps=new(){Content="Derivar los 9 caps de los jefes de esta sesión"},_exp=new(){Content="Modificar ganancia de EXP"},_share=new(){Content="Repartir Exp. activado"},_fully=new(){Content="Retirar completamente el límite del Área Silvestre"},_chain=new(){Content="Conservar cadena de pesca después de un brillante"};
    internal void UseTrainerCaps(){_caps.IsChecked=true;_autoCaps.IsChecked=true;_capMode.SelectedIndex=0;}
    private readonly ComboBox _visible=new(){IsEditable=false,ItemsSource=new[]{"Vanilla · conservar modelos","Random · sorteo","Zone · por zona","Global · global","Descubrimiento · vanilla hasta revelar","Descubrimiento · señuelo aleatorio"}},_battle=new(){IsEditable=false,ItemsSource=new[]{"Vanilla","Random · sorteo","Zone · por zona","Global · global"}},_capMode=new(){IsEditable=false,ItemsSource=new[]{"Por medallas · gimnasios y Liga","Comparar receptor y derrotado"}};
    private readonly Dictionary<PokeFeurFeature,CheckBox> _features=new();
    private readonly DataGrid _capGrid=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,EnableRowVirtualization=true,FrozenColumnCount=1};
    private readonly List<PokeFeurCapRow> _rows=Enumerable.Range(0,9).Select(i=>new PokeFeurCapRow{Stage=i<8?$"{i} medallas · Gimnasio {i+1}":"8 medallas · Campeón"}).ToList();
    private readonly TextBox _details=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=170};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button _prepare=new(){Content="Previsualizar"},_apply=new(){Content="Añadir al mod",IsEnabled=false},_remove=new(){Content="Retirar runtime"},_install=new(){Content="Instalar configuración SD…"},_close=new(){Content="Cerrar",IsCancel=true};
    internal SwordPokeFeurWindow(SwordShieldProject project)
    {
        _project=project;string game=project.Identity.TitleId=="01008DB008C2C000"?"Escudo":"Espada";Title=game+" · encuentros, EXP y herramientas";Width=1000;Height=790;MinWidth=560;MinHeight=420;MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"TextMain");NameScope.SetNameScope(this,new NameScope());
        var root=new Grid{Margin=new Thickness(16)};Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();root.Children.Add(header);header.Children.Add(new TextBlock{Text=game+" · encuentros, level caps y herramientas",FontSize=22,TextWrapping=TextWrapping.Wrap});
        header.Children.Add(new TextBlock{Text=(game=="Escudo"?"Escudo: validaciones automáticas; combate sorpresa, captura, menús y herramientas pendientes de prueba jugable. ":"Combate sorpresa, captura y menús validados por el usuario en Espada. Level caps y demás funciones: pendientes de prueba jugable. ")+"Previsualizar o añadir no escribe en la extracción ni en la SD.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});
        Grid.SetRow(_tabs,1);root.Children.Add(_tabs);var package=Tab("Runtime y recetas");Field(package,"Runtime integrado y verificado (GPL-3.0)",_packagePath,"PokePackagePath");
        package.Children.Add(new TextBlock{Text="El runtime y sus scripts están integrados en la aplicación: no necesitas descargar ni importar un paquete. Se verifica su integridad antes de usarlos. La configuración SD es compartida entre mods PokeFeur; cierra el juego antes de instalarla.",TextWrapping=TextWrapping.Wrap});
        Action(package,"Importar receta runtime…",LoadRecipe);Action(package,"Guardar receta runtime…",SaveRecipe);package.Children.Add(new TextBlock{Text="Las recetas contienen opciones, pools y caps; no contienen ROM, ejecutables ni rutas personales. No son un Global Template de 3DS.",TextWrapping=TextWrapping.Wrap});
        var enc=Tab("Encuentros");Check(enc,_random,"PokeRandomizer");Field(enc,"Semilla aleatoria",_seed,"PokeSeed");Action(enc,"Nueva semilla aleatoria",()=>{_seed.Text=SwitchSeed.New64().ToString(CultureInfo.InvariantCulture);Invalidate();});Field(enc,"Pokémon visible",_visible,"PokeVisibleMode");Field(enc,"Pokémon de combate",_battle,"PokeBattleMode");Field(enc,"Pool visible · IDs separados por comas; vacío = presentes",_visiblePool,"PokeVisiblePool");Field(enc,"Pool de combate · IDs separados por comas; vacío = presentes",_battlePool,"PokeBattlePool");
        enc.Children.Add(new TextBlock{Text="Los pools usan especies base presentes en la extracción. Para combate sorpresa: visible Vanilla y combate Random. Descubrimiento guarda progreso en SD; no se reinicia automáticamente. Los modos distintos al validado requieren pruebas de alcance, captura y eventos.",TextWrapping=TextWrapping.Wrap});
        var exp=Tab("Level caps y EXP");Check(exp,_caps,"PokeCaps");Field(exp,"Modo de level cap",_capMode,"PokeCapMode");Check(exp,_autoCaps,"PokeAutoCaps");Action(exp,"Leer caps de jefes actuales",()=>_ = DeriveCapsAsync());
        exp.Children.Add(new TextBlock{Text="Nueve límites de 1 a 100: uno antes de cada gimnasio y otro para el Campeón. El modo automático toma el nivel máximo de cada equipo actual; si cambian después, hay que actualizar la vista previa.",TextWrapping=TextWrapping.Wrap});
        _capGrid.Columns.Add(new DataGridTextColumn{Header="Progreso",Binding=new Binding("Stage"),IsReadOnly=true,Width=new DataGridLength(2,DataGridLengthUnitType.Star)});_capGrid.Columns.Add(new DataGridTextColumn{Header="Level cap (1–100)",Binding=new Binding("Level"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});_capGrid.ItemsSource=_rows;_capGrid.MinHeight=180;_capGrid.MaxHeight=320;DarkGridTheme.Apply(_capGrid);AutomationProperties.SetName(_capGrid,"Nueve level caps por medallas");RegisterName("PokeCapGrid",_capGrid);exp.Children.Add(_capGrid);
        Check(exp,_exp,"PokeExpRuntime");Field(exp,"Multiplicador EXP (0,01–100)",_multiplier,"PokeExpMultiplier");Check(exp,_share,"PokeExpShare");
        var tools=Tab("Herramientas");foreach(var feature in PokeFeurOptions.AvailableCatalog){var box=new CheckBox{Content=feature.Label,ToolTip=feature.Description,Margin=new Thickness(0,5,0,5)};_features.Add(feature.Feature,box);Check(tools,box,"PokeFeature"+feature.Feature);tools.Children.Add(new TextBlock{Text=feature.Description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22,0,0,3)});}
        Field(tools,"Bonificación shiny (0–100)",_shiny,"PokeShinyBoost");Field(tools,"Spawns de Bosque Lumirinto (1–20)",_spawns,"PokeSpawns");Check(tools,_fully,"PokeFullyUncap");Check(tools,_chain,"PokeFishingChain");
        var preview=Tab("Vista previa");preview.Children.Add(_details);AutomationProperties.SetName(_details,"Configuración y recursos preparados, de solo lectura");RegisterName("PokeDetails",_details);
        var footer=new WrapPanel{Margin=new Thickness(0,10,0,6)};Grid.SetRow(footer,2);root.Children.Add(footer);foreach(var b in new[]{_prepare,_apply,_remove,_install,_close}){b.Margin=new Thickness(0,0,8,6);EditorPresentation.StyleAction(b,b==_apply?EditorPresentation.ActionRole.Primary:EditorPresentation.ActionRole.Secondary);footer.Children.Add(b);}
        Grid.SetRow(_status,3);root.Children.Add(_status);AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);
        RegisterName("PokePreview",_prepare);RegisterName("PokeApply",_apply);RegisterName("PokeInstallSd",_install);RegisterName("PokeRemove",_remove);
        _prepare.Click+=async(_,_)=>await PreviewAsync();_apply.Click+=(_,_)=>Apply();_remove.Click+=(_,_)=>{try{_project.RemovePokeFeur();Changed?.Invoke();Invalidate();_status.Text="Runtime retirado de la sesión; los cambios de Pokémon y ROMFS se conservan.";}catch(Exception e){Error(e);}};_install.Click+=(_,_)=>InstallSd();_close.Click+=(_,_)=>CloseEditor();Closing+=(_,e)=>{if(_busy)e.Cancel=true;};
        _capGrid.CellEditEnding+=(_,_)=>Invalidate();_autoCaps.Checked+=(_,_)=>_capGrid.IsReadOnly=true;_autoCaps.Unchecked+=(_,_)=>_capGrid.IsReadOnly=false;
        SetOptions(project.PokeFeurCurrent??new PokeFeurOptions());
        SetPackage(PokeFeurPackage.BuiltIn(_project.Identity.TitleId));
        Refresh();Loaded+=(_,_)=>_tabs.Focus();
    }
    private StackPanel Tab(string title){var p=new StackPanel{Margin=new Thickness(12)};_tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}});return p;}
    private void Field(StackPanel p,string label,System.Windows.Controls.Control c,string name){var l=new Label{Content=label,Target=c};AutomationProperties.SetLabeledBy(c,l);p.Children.Add(l);p.Children.Add(c);c.Margin=new Thickness(0,0,0,8);RegisterName(name,c);if(c is TextBox t&&!t.IsReadOnly)t.TextChanged+=(_,_)=>Invalidate();if(c is ComboBox combo)combo.SelectionChanged+=(_,_)=>Invalidate();}
    private void Check(StackPanel p,CheckBox c,string name){p.Children.Add(c);RegisterName(name,c);AutomationProperties.SetName(c,c.Content?.ToString()??name);c.Checked+=(_,_)=>Invalidate();c.Unchecked+=(_,_)=>Invalidate();}
    private void Action(StackPanel p,string label,Action action){var b=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,8)};EditorPresentation.StyleAction(b,EditorPresentation.ActionRole.Secondary);b.Click+=(_,_)=>{try{action();}catch(Exception e){Error(e);}};p.Children.Add(b);}
    private void Error(Exception e)=>StudioDialog.Error(DialogOwner,"Runtime PokeFeur",e.Message);
    private void Invalidate(){_pending=null;_details.Text="Opciones modificadas. Genera una nueva vista previa.";Refresh();}
    private void Refresh(){_tabs.IsEnabled=!_busy;_prepare.IsEnabled=_close.IsEnabled=!_busy;_apply.IsEnabled=!_busy&&_pending is not null;_install.IsEnabled=_remove.IsEnabled=!_busy&&_project.PokeFeurEnabled;}
    internal void SetPackage(PokeFeurPackage package){_package=package;_packagePath.Text=package.Folder;Invalidate();_status.Text="Runtime integrado verificado · "+PokeFeurPackage.Commit[..12];}
    internal void SetOptions(PokeFeurOptions o)
    {
        o.ValidateFeatureAvailability();
        _seed.Text=o.Seed.ToString(CultureInfo.InvariantCulture);_random.IsChecked=o.Randomizer;_visible.SelectedIndex=(int)o.Visible;_battle.SelectedIndex=(int)o.Battle;_visiblePool.Text=string.Join(",",o.VisiblePool);_battlePool.Text=string.Join(",",o.BattlePool);_caps.IsChecked=o.Caps;_capMode.SelectedIndex=(int)o.CapMode;_autoCaps.IsChecked=o.CapsFromTrainers;for(int i=0;i<9&&i<o.StoryCaps.Count;i++)_rows[i].Level=o.StoryCaps[i].ToString(CultureInfo.InvariantCulture);_capGrid.Items.Refresh();_exp.IsChecked=o.ExpRuntime;_multiplier.Text=o.ExpMultiplier.ToString(CultureInfo.InvariantCulture);_share.IsChecked=o.ExpShare;_shiny.Text=o.ShinyBoost.ToString(CultureInfo.InvariantCulture);_spawns.Text=o.GlimwoodSpawns.ToString(CultureInfo.InvariantCulture);_fully.IsChecked=o.FullyUncap;_chain.IsChecked=o.KeepFishingChain;foreach(var pair in _features)pair.Value.IsChecked=o.Features.Contains(pair.Key);Invalidate();
    }
    internal PokeFeurOptions Options()
    {
        _capGrid.CommitEdit(DataGridEditingUnit.Cell,true);_capGrid.CommitEdit(DataGridEditingUnit.Row,true);
        int Number(string s,string label){if(!int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))throw new ArgumentException("Valor inválido: "+label);return value;}
        int[] Pool(string s)=>s.Split(new[]{',',';',' ','\n','\r','\t'},StringSplitOptions.RemoveEmptyEntries).Select(v=>Number(v,"ID de especie")).ToArray();
        if(!ulong.TryParse(_seed.Text,NumberStyles.None,CultureInfo.InvariantCulture,out ulong seed))throw new ArgumentException("Semilla inválida.");if(!double.TryParse(_multiplier.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double multiplier))throw new ArgumentException("Multiplicador inválido.");
        return new(){Seed=seed,Randomizer=_random.IsChecked==true,Visible=(PokeFeurVisibleMode)_visible.SelectedIndex,Battle=(PokeFeurBattleMode)_battle.SelectedIndex,VisiblePool=Pool(_visiblePool.Text),BattlePool=Pool(_battlePool.Text),Caps=_caps.IsChecked==true,CapMode=(PokeFeurCapMode)_capMode.SelectedIndex,CapsFromTrainers=_autoCaps.IsChecked==true,StoryCaps=_rows.Select(r=>Number(r.Level,"level cap")).ToArray(),ExpRuntime=_exp.IsChecked==true,ExpMultiplier=multiplier,ExpShare=_share.IsChecked==true,ShinyBoost=Number(_shiny.Text,"shiny"),GlimwoodSpawns=Number(_spawns.Text,"spawns"),FullyUncap=_fully.IsChecked==true,KeepFishingChain=_chain.IsChecked==true,Features=_features.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToArray()};
    }
    internal async Task PreviewAsync()
    {
        if(_busy)return;
        try{var options=Options();if(_project.PokeFeurCurrent?.Seed==options.Seed){_seed.Text=SwitchSeed.New64().ToString(CultureInfo.InvariantCulture);options=Options();}_pending=null;_busy=true;Refresh();_status.Text="Verificando origen, opciones y recursos…";var package=_package;_pending=await Task.Run(()=>_project.PreviewPokeFeur(package,options));
            if(_pending.Caps.Count>0){for(int i=0;i<9;i++)_rows[i].Level=_pending.Caps[i].Level.ToString(CultureInfo.InvariantCulture);_capGrid.Items.Refresh();}
            _details.Text=(_pending.Options.Enabled?"Se añadirá el runtime externo al mod. No se sustituye el main del juego.\n":"No se activará el runtime. Se retirará su configuración de la sesión si existe.\n")+"\nRecursos:\n"+string.Join("\n",_pending.Hashes.Select(p=>p.Key+" · "+p.Value))+"\n\n"+_pending.Options.Config();_tabs.SelectedIndex=4;_status.Text="Vista previa preparada. Añadir al mod registra las opciones; instalar en SD es una acción separada.";}
        catch(Exception e){_pending=null;Error(e);}finally{_busy=false;Refresh();}
    }
    private void Apply(){if(_pending is null||_busy)return;try{_project.ApplyPokeFeur(_pending);Changed?.Invoke();_pending=null;_status.Text="Runtime añadido a la sesión. "+_project.RuntimeSdStatus(SwordShieldProject.DefaultRyujinxSd);Refresh();}catch(Exception e){_pending=null;Refresh();Error(e);}}
    private async Task DeriveCapsAsync(){if(_busy)return;try{_busy=true;Refresh();var rows=await Task.Run(()=>_project.PokeFeurTrainerCaps());for(int i=0;i<9;i++)_rows[i].Level=rows[i].Level.ToString(CultureInfo.InvariantCulture);_capGrid.Items.Refresh();Invalidate();_status.Text=string.Join(" · ",rows.Select(r=>$"Jefe {r.Trainer}: {r.Level}"));}catch(Exception e){Error(e);}finally{_busy=false;Refresh();}}
    private void LoadRecipe(){var d=new Microsoft.Win32.OpenFileDialog{Filter="Receta runtime (*.json)|*.json"};if(d.ShowDialog(DialogOwner)!=true)return;if(new FileInfo(d.FileName).Length>200000)throw new InvalidDataException("Receta demasiado grande.");var o=PokeFeurOptions.ReadRecipe(File.ReadAllText(d.FileName)).Normalize(_project.PokeFeurPresentPool());SetOptions(o with{Seed=SwitchSeed.New64()});_status.Text="Receta cargada con una nueva semilla aleatoria. No se ha aplicado; genera una vista previa.";}
    private void SaveRecipe(){var options=Options().Normalize(_project.PokeFeurPresentPool());var d=new Microsoft.Win32.SaveFileDialog{Filter="Receta runtime (*.json)|*.json",FileName="runtime-espada.json"};if(d.ShowDialog(DialogOwner)==true)File.WriteAllText(d.FileName,options.RecipeJson());}
    private void InstallSd()
    {
        try{if(Options().Normalize(_project.PokeFeurPresentPool()).RecipeJson()!=_project.PokeFeurCurrent?.RecipeJson())throw new InvalidOperationException("Hay opciones sin añadir al mod. Previsualízalas y añádelas antes de instalar en SD.");using var d=new System.Windows.Forms.FolderBrowserDialog{Description="Selecciona la raíz SD de Ryujinx (sdcard)",ShowNewFolderButton=false,SelectedPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Ryujinx","sdcard")};if(d.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;var plan=_project.PreviewPokeSd(d.SelectedPath);bool replace=false;if(plan.Backup is not null){replace=StudioDialog.Confirm(DialogOwner,"Configuración SD existente","La configuración actual es distinta. ¿Instalar la de esta sesión conservando una copia de seguridad?",$"Destino: {plan.Destination}\nAnterior: {plan.ExistingHash}\nNueva: {plan.NewHash}\nCopia: {plan.Backup}");if(!replace)return;}_project.InstallPokeSd(plan,replace);_status.Text="Configuración SD verificada: "+plan.Destination+(plan.Backup is not null?" · copia: "+plan.Backup:"");}catch(Exception e){Error(e);}
    }
}
