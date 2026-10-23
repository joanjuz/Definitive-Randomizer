using Control=System.Windows.Controls.Control;
using HorizontalAlignment=System.Windows.HorizontalAlignment;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using Panel=System.Windows.Controls.Panel;
using Image=System.Windows.Controls.Image;
namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldPrototypeWindow
{
    private readonly Control[] _personalFields=new Control[SwordShieldPersonalTable.FieldCount];
    private readonly TextBlock _personalName=new(){FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly WrapPanel _personalTypes=new(){Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock _personalState=new(){Text="Guardado"},_inspectorBst=new(){FontSize=32,FontWeight=FontWeights.Bold},_inspectorSession=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _personalIdentity=new(),_personalBst=new(){FontSize=16,FontWeight=FontWeights.Bold},_spriteFallback=new(){Text="◉",FontSize=45,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Image _personalSprite=new(){Width=96,Height=96,Stretch=System.Windows.Media.Stretch.Uniform};
    private readonly TextBox _personalSearch=new();
    private readonly SpriteService _spriteService=new();
    private bool _refreshingForm,_formDirty;
    private Button? _personalModifyButton,_personalRandomButton;
    private int _spriteRequest;
    private SwordShieldPersonalEntry? _formSelection;
    private bool _restoringSelection;
    internal bool CanChangeSection()
    {
        if(!_formDirty)return true;if(!StudioDialog.Confirm(DialogOwner,"Cambios sin guardar","¿Descartar los campos sin guardar antes de cambiar de sección?"))return false;RefreshValue();return true;
    }
    private void OnPersonalSelection()
    {
        if(_restoringSelection)return;
        if(_formDirty&&!StudioDialog.Confirm(DialogOwner,"Cambios sin guardar","¿Descartar los cambios de los campos antes de cambiar de Pokémon?"))
        {_restoringSelection=true;_entries.SelectedItem=_formSelection;_restoringSelection=false;return;}
        _formSelection=_entries.SelectedItem as SwordShieldPersonalEntry;RefreshValue();
    }
    private void FilterPersonalEntries()
    {
        if(_project is null)return;
        if(_formDirty)return;
        int? selected=(_entries.SelectedItem as SwordShieldPersonalEntry)?.Index;string text=_personalSearch.Text.Trim();
        _entries.ItemsSource=_project.Entries.Where(e=>e.Label.Contains(text,StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _entries.SelectedItem=_entries.Items.OfType<SwordShieldPersonalEntry>().FirstOrDefault(e=>e.Index==selected)??_entries.Items.OfType<SwordShieldPersonalEntry>().FirstOrDefault();
    }
    private static readonly string[] TypeNames={"Normal","Lucha","Volador","Veneno","Tierra","Roca","Bicho","Fantasma","Acero","Fuego","Agua","Planta","Eléctrico","Psíquico","Hielo","Dragón","Siniestro","Hada"};
    private Border PersonalCard(FrameworkElement content,bool summary=false)
    {var card=new Border{Child=content};card.SetResourceReference(StyleProperty,"Card");if(summary)card.SetResourceReference(BackgroundProperty,"SummarySurface");return card;}
    private void BuildPersonalForm(Panel parent)
    {
        var summary=new Grid();summary.ColumnDefinitions.Add(new(){Width=new GridLength(112)});summary.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var sprite=new Grid();sprite.Children.Add(_spriteFallback);sprite.Children.Add(_personalSprite);var frame=new Border{Width=104,Height=112,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Child=sprite,HorizontalAlignment=HorizontalAlignment.Left};frame.SetResourceReference(BackgroundProperty,"SpriteSurface");frame.SetResourceReference(BorderBrushProperty,"Stroke");summary.Children.Add(frame);
        var identity=new StackPanel{Margin=new Thickness(5,0,0,0)};Grid.SetColumn(identity,1);summary.Children.Add(identity);identity.Children.Add(_personalName);identity.Children.Add(_personalIdentity);identity.Children.Add(_personalTypes);var stateRow=new WrapPanel();stateRow.Children.Add(_personalBst);var stateBadge=new Border{Child=_personalState,Padding=new Thickness(9,5,9,5),CornerRadius=new CornerRadius(7),Margin=new Thickness(10,7,0,0)};stateBadge.SetResourceReference(BackgroundProperty,"BadgeFill");_personalState.SetResourceReference(TextBlock.ForegroundProperty,"BadgeText");stateRow.Children.Add(stateBadge);identity.Children.Add(stateRow);_personalIdentity.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");_personalBst.Margin=new Thickness(0,14,0,0);parent.Children.Add(PersonalCard(summary,true));
        var stats=new StackPanel();stats.Children.Add(new TextBlock{Text="Estadísticas base",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,13)});var statGrid=new UniformGrid{Columns=2};stats.Children.Add(statGrid);
        var names=new[]{"PS","Ataque","Defensa","Velocidad","Ataque Especial","Defensa Especial","Primer tipo","Segundo tipo","Ratio de captura (0–255)","Habilidad 1","Habilidad 2","Habilidad oculta","Objeto 1","Objeto 2","Objeto 3","Género (0–255)","Ciclos de eclosión (0–255)","Amistad base (0–255)","Crecimiento (0–5)","Grupo huevo 1 (0–15)","Grupo huevo 2 (0–15)","Color (0–9)","Experiencia base (0–65535)","Altura (décimas de metro)","Peso (décimas de kg)","EV PS","EV Ataque","EV Defensa","EV Velocidad","EV Ataque Especial","EV Defensa Especial"};
        void Changed(){if(_refreshingForm)return;_formDirty=true;_preview=null;RefreshPersonalSummary();RefreshButtons();}
        FrameworkElement Field(int f)
        {
            var wrap=new StackPanel{Margin=new Thickness(0,0,10,12)};Control control;
            if(f is 6 or 7){var combo=new ComboBox{ItemsSource=TypeNames};combo.SelectionChanged+=(_,_)=>Changed();control=combo;}
            else if(f is >=9 and <=14){var combo=new ComboBox{IsTextSearchEnabled=true,ItemsSource=Enumerable.Range(1,267).Select(id=>new AbilityChoice(id,id.ToString("000",CultureInfo.InvariantCulture))).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Id"};combo.SelectionChanged+=(_,_)=>Changed();control=combo;}
            else{var box=new TextBox();box.TextChanged+=(_,_)=>Changed();control=box;}
            _personalFields[f]=control;AutomationProperties.SetName(control,names[f]);AddLabel(wrap,names[f],control);wrap.Children.Add(control);return wrap;
        }
        foreach(int f in new[]{0,1,2,4,5,3})statGrid.Children.Add(Field(f));parent.Children.Add(PersonalCard(stats));
        var types=new StackPanel();types.Children.Add(new TextBlock{Text="TIPOS Y CAPTURA",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,13)});var typeGrid=new UniformGrid{Columns=2};typeGrid.Children.Add(Field(6));typeGrid.Children.Add(Field(7));types.Children.Add(typeGrid);types.Children.Add(Field(8));parent.Children.Add(PersonalCard(types));
        var abilities=new StackPanel();foreach(int f in new[]{9,10,11,12,13,14})abilities.Children.Add(Field(f));parent.Children.Add(PersonalCard(new Expander{Header="HABILIDADES Y OBJETOS",Content=abilities}));
        var breeding=new StackPanel();foreach(int f in new[]{15,16,17,18,19,20})breeding.Children.Add(Field(f));parent.Children.Add(PersonalCard(new Expander{Header="CRÍA Y CRECIMIENTO",Content=breeding}));
        var rewards=new StackPanel();foreach(int f in new[]{22,25,26,27,28,29,30})rewards.Children.Add(Field(f));parent.Children.Add(PersonalCard(new Expander{Header="EXPERIENCIA Y EV",Content=rewards}));
        var extra=new StackPanel();foreach(int f in new[]{21,23,24})extra.Children.Add(Field(f));parent.Children.Add(PersonalCard(new Expander{Header="DATOS ADICIONALES",Content=extra}));
        parent.Loaded+=(_,_)=>UpdatePersonalSprite();
    }
    private sealed record AbilityChoice(int Id,string Name);
    private void RefreshPersonalForm()
    {
        if(_personalFields[0] is null)return;_refreshingForm=true;
        try
        {
            if(_project is null||_entries.SelectedItem is not SwordShieldPersonalEntry selected){_personalName.Text="Selecciona un Pokémon";_formDirty=false;return;}
            var entry=_project.Read(selected.Index);_personalName.Text=_project.SpeciesName(entry.Species);_personalIdentity.Text=$"Entrada #{entry.Index:000} · Forma {entry.Form}";_personalBst.Text=$"BST  {entry.BST}";
            for(int f=0;f<SwordShieldPersonalTable.FieldCount;f++)if(_personalFields[f] is TextBox box)box.Text=entry.Values[f].ToString(CultureInfo.InvariantCulture);else if(_personalFields[f] is ComboBox combo){if(f>=9){combo.ItemsSource=(f<=11?Enumerable.Range(1,267).Select(id=>new AbilityChoice(id,$"{id:000} · {_project.AbilityName(id)}")):Enumerable.Range(-1,1609).Select(id=>new AbilityChoice(id,$"{id:000} · {_project.ItemName(id)}"))).ToArray();combo.SelectedValue=entry.Values[f];}else combo.SelectedIndex=entry.Values[f];}
            _formDirty=false;RefreshPersonalSummary();UpdatePersonalSprite();
        }finally{_refreshingForm=false;}
    }
    private void RefreshPersonalSummary()
    {
        var stats=_personalFields.Take(6).OfType<TextBox>().Select(b=>int.TryParse(b.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)&&value is >=1 and <=255?value:-1).ToArray();string total=stats.Length==6&&stats.All(v=>v>=0)?stats.Sum().ToString(CultureInfo.InvariantCulture):"—";_personalBst.Text="BST  "+total;_inspectorBst.Text=total;_personalState.Text=_formDirty?"Cambios sin guardar":"Guardado";
        _personalTypes.Children.Clear();foreach(int type in _personalFields.Skip(6).Take(2).OfType<ComboBox>().Select(c=>c.SelectedIndex).Where(i=>i>=0).Distinct())
        {
            var text=new TextBlock{Text=TypeNames[type],FontWeight=FontWeights.SemiBold};text.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");var badge=new Border{Child=text,Padding=new Thickness(11,6,11,6),CornerRadius=new CornerRadius(7),Margin=new Thickness(0,0,7,0)};badge.SetResourceReference(BackgroundProperty,"TypeFallbackFill");_personalTypes.Children.Add(badge);
        }
        _inspectorSession.Text=(_project?.Identity.Game??"")+"\n"+_personalName.Text;
    }
    private void BuildPersonalInspector(Panel panel)
    {
        var stats=new StackPanel();stats.Children.Add(new TextBlock{Text="Total de estadísticas",Margin=new Thickness(0,0,0,8)});_inspectorBst.SetResourceReference(TextBlock.ForegroundProperty,"Accent");stats.Children.Add(_inspectorBst);stats.Children.Add(new TextBlock{Text="BST (Base Stat Total)",FontSize=11,TextWrapping=TextWrapping.Wrap});panel.Children.Add(PersonalCard(stats));
        var session=new StackPanel();session.Children.Add(new TextBlock{Text="Sesión actual",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});session.Children.Add(_inspectorSession);panel.Children.Add(PersonalCard(session));panel.Children.Add(PersonalCard(new TextBlock{Text="Los cambios se guardan en el proyecto. Exportar mod… genera los archivos; la extracción permanece en lectura.",TextWrapping=TextWrapping.Wrap,FontSize=11}));
    }
    private int[] ReadPersonalForm()=>_personalFields.Select((c,f)=>c is TextBox box?int.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int v)?v:throw new ArgumentException("Introduce estadísticas enteras."):f<9?((ComboBox)c).SelectedIndex:(int)(((ComboBox)c).SelectedValue??0)).ToArray();
    private async void UpdatePersonalSprite()
    {
        int request=++_spriteRequest;_personalSprite.Source=null;_spriteFallback.Visibility=Visibility.Visible;
        if(_project is null||_entries.SelectedItem is not SwordShieldPersonalEntry entry||!_personalSprite.IsLoaded)return;
        var result=await _spriteService.GetSpriteAsync(entry.Index,entry.Species,entry.Form);
        if(request!=_spriteRequest)return;_personalSprite.Source=result.Image;_spriteFallback.Visibility=result.Image is null?Visibility.Visible:Visibility.Collapsed;
        _personalSprite.ToolTip=result.Image is null?"Sprite no disponible sin conexión":result.Description;
    }
    private void ShowRandomOptions(StackPanel options)
    {
        var body=new Grid{Margin=new Thickness(20)};body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});var contents=new StackPanel();contents.Children.Add(options);var previewText=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};previewText.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding("Text"){Source=_changes,Mode=System.Windows.Data.BindingMode.OneWay});contents.Children.Add(new TextBlock{Text="Vista previa",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8)});contents.Children.Add(previewText);var scroll=new ScrollViewer{Content=contents,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(scroll);
        var dialog=new Window{Title="Randomizar Pokémon",Width=720,Height=700,MinWidth=430,MinHeight=340,MaxHeight=SystemParameters.WorkArea.Height,MaxWidth=SystemParameters.WorkArea.Width,Content=body,Owner=DialogOwner,WindowStartupLocation=WindowStartupLocation.CenterOwner};dialog.SetResourceReference(BackgroundProperty,"Bg");dialog.SetResourceReference(ForegroundProperty,"TextMain");var labels=new Style(typeof(System.Windows.Controls.Label));labels.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,new DynamicResourceExtension("TextSoft")));dialog.Resources.Add(typeof(System.Windows.Controls.Label),labels);
        if(options.Children.OfType<Expander>().FirstOrDefault() is {} expander)expander.IsExpanded=true;
        var close=new Button{Content="Volver al editor",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>dialog.Close();var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(actions,1);body.Children.Add(actions);var apply=new Button{Content="Aplicar cambios",Margin=new Thickness(0,12,10,0)};EditorPresentation.StyleAction(apply,EditorPresentation.ActionRole.Primary);apply.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding("IsEnabled"){Source=_apply});apply.Click+=(_,_)=>{Apply();if(_preview is null&&!_formDirty)dialog.Close();};actions.Children.Add(apply);actions.Children.Add(close);try{dialog.ShowDialog();}finally{contents.Children.Remove(options);}
    }
}
