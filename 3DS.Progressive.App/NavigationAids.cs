using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Size=System.Windows.Size;
using TextBox=System.Windows.Controls.TextBox;
using Orientation=System.Windows.Controls.Orientation;
namespace ThreeDS.Progressive;

/// <summary>Placeholder text drawn by the TextBox template while the box is empty. Presentation only.</summary>
public static class InputHint
{
    public static readonly DependencyProperty TextProperty=DependencyProperty.RegisterAttached("Text",typeof(string),typeof(InputHint),new PropertyMetadata(null,Changed));
    public static void SetText(DependencyObject o,string? value)=>o.SetValue(TextProperty,value);
    public static string? GetText(DependencyObject o)=>(string?)o.GetValue(TextProperty);
    // The placeholder is also the help text read by screen readers, so the hint is not only visual.
    private static void Changed(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is TextBox box&&e.NewValue is string text&&string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(box)))AutomationProperties.SetHelpText(box,text);
    }
}

/// <summary>Name of the tool shown in the breadcrumb of the section host ("Herramientas Pokémon › Egg Moves").</summary>
public static class Breadcrumbs
{
    public static readonly DependencyProperty CurrentProperty=DependencyProperty.RegisterAttached("Current",typeof(string),typeof(Breadcrumbs),new PropertyMetadata(null));
    public static void SetCurrent(DependencyObject o,string? value)=>o.SetValue(CurrentProperty,value);
    public static string? GetCurrent(DependencyObject o)=>(string?)o.GetValue(CurrentProperty);
}
public sealed class HasTextConverter:System.Windows.Data.IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is string text&&!string.IsNullOrWhiteSpace(text);
    public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
}

/// <summary>Number of saved history operations shown next to a sidebar entry. Presentation only.</summary>
public static class NavBadge
{
    public static readonly DependencyProperty CountProperty=DependencyProperty.RegisterAttached("Count",typeof(int),typeof(NavBadge),new PropertyMetadata(0,CountChanged));
    private static readonly DependencyPropertyKey HasChangesKey=DependencyProperty.RegisterAttachedReadOnly("HasChanges",typeof(bool),typeof(NavBadge),new PropertyMetadata(false));
    public static readonly DependencyProperty HasChangesProperty=HasChangesKey.DependencyProperty;
    public static void SetCount(DependencyObject o,int value)=>o.SetValue(CountProperty,value);
    public static int GetCount(DependencyObject o)=>(int)o.GetValue(CountProperty);
    public static bool GetHasChanges(DependencyObject o)=>(bool)o.GetValue(HasChangesProperty);
    private static void CountChanged(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        int count=(int)e.NewValue;o.SetValue(HasChangesKey,count>0);
        // Screen readers hear the same information the badge shows.
        if(o is UIElement element)AutomationProperties.SetHelpText(element,count>0?$"{count} {(count==1?"cambio guardado":"cambios guardados")} en el historial":"");
        if(o is FrameworkElement framework&&framework.ToolTip is null or string)
        {
            string label=framework is System.Windows.Controls.ContentControl {Content:string text}?new NavigationLabelConverter().Convert(text,typeof(string),null!,System.Globalization.CultureInfo.CurrentCulture).ToString()!:"";
            if(count>0)framework.ToolTip=$"{label} · {count} {(count==1?"cambio guardado":"cambios guardados")}";
            else framework.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }
}

/// <summary>
/// Two-column form layout. Unlike <see cref="UniformGrid"/>, each row is as tall as its own content, so a row of
/// check boxes is not stretched to the height of the tallest field of the form. It still derives from UniformGrid
/// so the existing adaptive hook (one column below 460 px) and the checks that look for form grids keep working.
/// </summary>
public class FormGrid : UniformGrid
{
    private int ColumnCount=>Math.Max(1,Columns);
    private IEnumerable<UIElement> Visible=>InternalChildren.Cast<UIElement>().Where(c=>c.Visibility!=Visibility.Collapsed);
    protected override Size MeasureOverride(Size constraint)
    {
        int columns=ColumnCount;
        double cell=double.IsInfinity(constraint.Width)?double.PositiveInfinity:constraint.Width/columns;
        double height=0,row=0,width=0;int index=0;
        foreach(UIElement child in InternalChildren)
        {
            if(child.Visibility==Visibility.Collapsed){child.Measure(new Size(0,0));continue;}
            child.Measure(new Size(cell,double.PositiveInfinity));
            row=Math.Max(row,child.DesiredSize.Height);width=Math.Max(width,child.DesiredSize.Width);
            if(++index%columns==0){height+=row;row=0;}
        }
        return new Size(width*columns,height+row);
    }
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        int columns=ColumnCount;double cell=arrangeSize.Width/columns,y=0;
        var children=Visible.ToArray();
        for(int start=0;start<children.Length;start+=columns)
        {
            var row=children.Skip(start).Take(columns).ToArray();
            double height=row.Max(c=>c.DesiredSize.Height);
            for(int i=0;i<row.Length;i++)row[i].Arrange(new Rect(i*cell,y,cell,height));
            y+=height;
        }
        foreach(UIElement child in InternalChildren)if(child.Visibility==Visibility.Collapsed)child.Arrange(new Rect());
        return arrangeSize;
    }
}

/// <summary>Cards that flow in as many columns as fit (minimum width per card), each row as tall as its tallest card.</summary>
public sealed class TileGrid : System.Windows.Controls.Panel
{
    public double MinTileWidth {get;set;}=330;
    public double Gap {get;set;}=12;
    private int Columns(double width)=>double.IsInfinity(width)?1:Math.Max(1,(int)((width+Gap)/(MinTileWidth+Gap)));
    protected override Size MeasureOverride(Size available)
    {
        int columns=Columns(available.Width);
        double cell=double.IsInfinity(available.Width)?MinTileWidth:(available.Width-Gap*(columns-1))/columns;
        var visible=InternalChildren.Cast<UIElement>().Where(c=>c.Visibility!=Visibility.Collapsed).ToArray();
        double height=0;
        for(int start=0;start<visible.Length;start+=columns)
        {
            double row=0;
            foreach(var child in visible.Skip(start).Take(columns)){child.Measure(new Size(cell,double.PositiveInfinity));row=Math.Max(row,child.DesiredSize.Height);}
            height+=row+(start+columns<visible.Length?Gap:0);
        }
        return new Size(double.IsInfinity(available.Width)?cell:available.Width,height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        int columns=Columns(final.Width);
        double cell=(final.Width-Gap*(columns-1))/columns,y=0;
        var visible=InternalChildren.Cast<UIElement>().Where(c=>c.Visibility!=Visibility.Collapsed).ToArray();
        for(int start=0;start<visible.Length;start+=columns)
        {
            var row=visible.Skip(start).Take(columns).ToArray();
            double height=row.Max(c=>c.DesiredSize.Height);
            for(int i=0;i<row.Length;i++)row[i].Arrange(new Rect(i*(cell+Gap),y,cell,height));
            y+=height+Gap;
        }
        foreach(UIElement child in InternalChildren)if(child.Visibility==Visibility.Collapsed)child.Arrange(new Rect());
        return final;
    }
}

/// <summary>«1 Configurar → 2 Vista previa → 3 Aplicar». Shows where the user is in a randomization; it never runs anything.</summary>
public sealed class StepIndicator : Border
{
    private static readonly string[] Labels=["Configura las opciones","Genera la vista previa","Aplica los cambios"];
    private readonly (Border Badge,TextBlock Number,TextBlock Label)[] _steps;
    private readonly TextBlock _status=new(){Visibility=Visibility.Collapsed};
    internal int Current {get;private set;}
    public StepIndicator()
    {
        Margin=new Thickness(0,0,0,10);
        var row=new WrapPanel();
        _steps=new (Border,TextBlock,TextBlock)[Labels.Length];
        for(int i=0;i<Labels.Length;i++)
        {
            var number=new TextBlock{Text=(i+1).ToString(),FontSize=11,FontWeight=FontWeights.Bold,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var badge=new Border{Width=20,Height=20,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Child=number,VerticalAlignment=VerticalAlignment.Center};
            var label=new TextBlock{Text=Labels[i],FontSize=12,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            var item=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,12,4)};
            item.Children.Add(badge);item.Children.Add(label);
            if(i<Labels.Length-1)item.Children.Add(new TextBlock{Text="›",Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center,FontSize=14});
            row.Children.Add(item);_steps[i]=(badge,number,label);
        }
        var stack=new StackPanel();stack.Children.Add(row);stack.Children.Add(_status);
        Child=stack;
        AutomationProperties.SetLiveSetting(this,AutomationLiveSetting.Polite);
        Loaded+=(_,_)=>SetStep(Current);
    }
    /// <summary>0-2: current step; 3: every step done.</summary>
    internal void SetStep(int current)
    {
        Current=Math.Clamp(current,0,Labels.Length);
        for(int i=0;i<_steps.Length;i++)
        {
            var (badge,number,label)=_steps[i];
            bool done=i<Current,active=i==Current;
            number.Text=done?"✓":(i+1).ToString();
            badge.SetResourceReference(BackgroundProperty,done||active?"Accent":"Surface");
            badge.SetResourceReference(BorderBrushProperty,done||active?"Accent":"Stroke");
            number.SetResourceReference(TextBlock.ForegroundProperty,done||active?"Bg":"TextSoft");
            label.SetResourceReference(TextBlock.ForegroundProperty,active?"TextMain":"TextSoft");
            label.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
        }
        string state=Current>=Labels.Length?"Cambios aplicados":$"Paso {Current+1} de {Labels.Length}: {Labels[Current]}";
        AutomationProperties.SetName(this,state);
    }
}

/// <summary>One saved operation as the user reads it: what was done, when, and how much it changed.</summary>
internal sealed record SavedChange(DateTimeOffset Time,string Title,string Detail)
{
    public string When=>Time.ToLocalTime().ToString("dd/MM HH:mm",System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Which sidebar section a history entry belongs to, and a readable description of it. Read-only: it never changes the history.
/// Entries applied by a Global Template (module «Global Template») and bulk actions carry an action id (trainers.randomize…);
/// the id decides the section, so a project randomized with a recipe is marked in every section the recipe touched.
/// </summary>
internal static class SectionProgress
{
    private static readonly IReadOnlyDictionary<string,(string Route,string Label)> Actions=new Dictionary<string,(string,string)>
    {
        ["battle.persistent-consumables"]=("Mechanics","Persistent Items · consumibles tras el combate"),
        ["catchzones.enable"]=("Mechanics","Enable Catch Zones"),
        ["egg-moves.randomize"]=("PokemonTools","Randomizar movimientos huevo"),
        ["evolutions.randomize"]=("Evolutions","Randomizar evoluciones"),
        ["evolutions.every-level"]=("Evolutions","Todas las evoluciones por nivel"),
        ["evolutions.remove-trade"]=("Evolutions","Quitar evoluciones por intercambio"),
        ["evolutions.normalize"]=("Evolutions","Normalizar evoluciones"),
        ["field-items.randomize"]=("FieldItems","Randomizar objetos de campo"),
        ["items.fix-economy"]=("Items","Fix Economy · precios"),
        ["level-up-moves.randomize"]=("LevelUp","Randomizar movimientos por nivel"),
        ["level-up-moves.metronome"]=("LevelUp","Modo Metrónomo · movimientos por nivel"),
        ["machines6.edit"]=("MachineTutor","Editar MT y MO"),
        ["machines6.randomize"]=("MachineTutor","Randomizar MT y MO"),
        ["manual-edits.apply"]=("GlobalTemplate","Modificaciones manuales exactas"),
        ["marts.add-ev-items"]=("Marts","Añadir vitaminas a las tiendas"),
        ["marts.add-rare-candies"]=("Marts","Añadir caramelos raros a las tiendas"),
        ["marts.ban-ability-capsule"]=("Marts","Quitar la cápsula habilidad"),
        ["marts.expanded-layout"]=("Marts","Tiendas ampliadas"),
        ["marts.free-mega-stones"]=("Marts","Megapiedras gratis"),
        ["marts.randomize"]=("Marts","Randomizar tiendas"),
        ["marts.randomize-bp"]=("Marts","Randomizar tiendas BP"),
        ["marts.single-purchase-specials"]=("Marts","Compra única en tiendas especiales"),
        ["marts6.edit"]=("Marts","Editar tiendas"),
        ["mega-evolution.unlock-from-start"]=("PokemonTools","Megaevolución desde el inicio"),
        ["move-tutors.follow-evolutions"]=("MachineTutor","Tutores · seguir evoluciones"),
        ["move-tutors.free"]=("MachineTutor","Tutores gratis"),
        ["move-tutors.randomize"]=("MachineTutor","Randomizar tutores"),
        ["move-tutors.sanity"]=("MachineTutor","Tutores · compatibilidad coherente"),
        ["moves.balance"]=("Moves","Balance Progressive de movimientos"),
        ["moves.metronome"]=("Moves","Modo Metrónomo"),
        ["moves.randomize"]=("Moves","Randomizar movimientos"),
        ["opower.edit"]=("Mechanics","Editar O-Powers"),
        ["personal.ban-trapping-abilities"]=("Pokemon","Quitar habilidades que impiden huir"),
        ["personal.manual"]=("Pokemon","Ediciones manuales de Pokémon"),
        ["personal.modify-all"]=("Pokemon","Modificar Pokémon"),
        ["personal.random"]=("Pokemon","Randomizar Pokémon"),
        ["personal.randomize"]=("Pokemon","Randomizar Pokémon"),
        ["pickup.edit"]=("PokemonTools","Editar Pickup"),
        ["pickup.randomize"]=("PokemonTools","Randomizar Pickup"),
        ["player.level-caps"]=("Mechanics","Player Level Caps"),
        ["pokemon-stats.apply"]=("Pokemon","Estadísticas Pokémon · template"),
        ["pokemon.no-obtainable-megas"]=("Mechanics","Quitar formas Mega obtenibles"),
        ["player.traded-obedience"]=("Mechanics","Los Pokémon intercambiados siempre obedecen"),
        ["shiny-rate.apply"]=("PokemonTools","Shiny Rate"),
        ["shiny-rate.restore"]=("PokemonTools","Restaurar Shiny Rate"),
        ["starters.randomize"]=("StaticEncounters","Randomizar iniciales"),
        ["static-encounters.modify-levels"]=("StaticEncounters","Niveles de encuentros estáticos"),
        ["static-encounters.randomize"]=("StaticEncounters","Randomizar encuentros estáticos"),
        ["static-encounters.totem-bst"]=("StaticEncounters","Totem BST"),
        ["static-encounters.totem-level-caps"]=("StaticEncounters","Totem Level Caps"),
        ["tms.follow-evolutions"]=("MachineTutor","MT · seguir evoluciones"),
        ["tms.randomize"]=("MachineTutor","Randomizar MT"),
        ["tms.sanity"]=("MachineTutor","MT · compatibilidad coherente"),
        ["trades.accept-any"]=("StaticEncounters","Intercambios · aceptar cualquier Pokémon"),
        ["trades.hide-species-names"]=("StaticEncounters","Intercambios · ocultar nombres"),
        ["trades.randomize-offers"]=("StaticEncounters","Randomizar ofertas de intercambio"),
        ["trades6.acceptany"]=("Mechanics","Intercambios · aceptar cualquier Pokémon"),
        ["trainers.randomize"]=("Trainers","Randomizar entrenadores"),
        ["usum.move-relearner"]=("Mechanics","USUM Relearner"),
        ["usum.ultra-warp-hint"]=("GameText","Pista del Ultraviaje"),
        ["wild-encounters.copy-sos"]=("Encounters","Copiar SOS"),
        ["wild-encounters.modify-levels"]=("Encounters","Niveles de encuentros"),
        ["wild-encounters.paste"]=("Encounters","Pegar tablas de encuentros"),
        ["wild-encounters.progressive"]=("Encounters","Progressive Wild"),
        ["wild-encounters.randomize"]=("Encounters","Randomizar encuentros"),
        ["wild-encounters.scale-levels"]=("Encounters","Escalar niveles de encuentros"),
    };
    private const string GlobalModule="Global Template";
    /// <summary>The action id of an entry: its «ActionId» parameter, or the action of a Global Template entry when it is an id.</summary>
    internal static string? ActionIdOf(HistoryEntry entry)
    {
        if(entry.Parameters.TryGetValue("ActionId",out var id)&&!string.IsNullOrWhiteSpace(id))return id;
        return entry.Module==GlobalModule&&entry.Action.Contains('.')&&!entry.Action.Contains(' ')?entry.Action:null;
    }
    internal static string? RouteFor(HistoryEntry entry)
    {
        // Entries committed since 0.13 keep the section decided when they were saved.
        if(!string.IsNullOrEmpty(entry.Section))return entry.Section;
        if(ActionIdOf(entry) is string id&&Actions.TryGetValue(id,out var known))return known.Route;
        if(entry.Module==GlobalModule)return "GlobalTemplate";
        return RouteFor(entry.Module,entry.Action);
    }
    internal static string? RouteFor(string module,string action)
    {
        string m=module,a=action;
        if(Has(a,"tabla de tipos")||Has(a,"efectividad")||Has(a,"shiny"))return "PokemonTools";
        if(Has(a,"formas mega obtenibles"))return "Mechanics";
        if(Has(m,"entrenador"))return "Trainers";
        if(Has(m,"estático")||Has(m,"regalos"))return "StaticEncounters";
        if(Has(m,"encuentros"))return "Encounters";
        if(Has(m,"level up"))return "LevelUp";
        if(Has(m,"egg moves")||Has(m,"mega evolution")||Has(m,"battle ")||Has(m,"maison")||Has(m,"pickup"))return "PokemonTools";
        if(Key(m) is "pokmon" or "personal"||Has(m,"estadísticas pokémon"))return "Pokemon";
        if(Has(m,"oras")&&Has(m,"tutor"))return "Mechanics";
        if(Has(m,"mt y")||Has(m,"mt ")||Has(m,"tutores"))return "MachineTutor";
        if(Has(m,"movimientos")||Key(m)=="move")return "Moves";
        if(Has(m,"tiendas"))return "Marts";
        if(Has(m,"field items")||Has(m,"objetos de campo"))return "FieldItems";
        if(Has(m,"persistent"))return "Mechanics";
        if(Has(m,"objetos"))return "Items";
        if(Has(m,"evoluciones"))return "Evolutions";
        if(Has(m,"historia y textos"))return "GameText";
        if(Has(m,"mecánicas")||Has(m,"obediencia")||Has(m,"catch zones")||Has(m,"o-powers")||Has(m,"intercambios"))return "Mechanics";
        if(Has(m,"herramientas rom")||Has(m,"título")||Has(m,"smdh")||Has(m,"garc")||Has(m,"restauración"))return "RomTools";
        if(Has(m,"modificaciones manuales")||Key(m)=="templates"||Has(m,"global template"))return "GlobalTemplate";
        return null;
    }
    // Some old history files store module names with a damaged accent (the «á» of «estáticos» saved as U+FFFD). Comparing
    // without any non-ASCII character makes the correct and the damaged spelling equal, so those entries keep their section.
    private static string Key(string text)=>new string(text.ToLowerInvariant().Where(c=>c<128).ToArray());
    private static bool Has(string text,string pattern)=>Key(text).Contains(Key(pattern),StringComparison.Ordinal);
    /// <summary>«Randomizar entrenadores» · «Global Template · 120 valores» · «Lillie, Hau y 3 más».</summary>
    internal static SavedChange Describe(HistoryEntry entry)
    {
        string title=ActionIdOf(entry) is string id&&Actions.TryGetValue(id,out var known)?known.Label:entry.Action;
        var parts=new List<string>();
        if(entry.Module==GlobalModule)parts.Add("Global Template");
        int values=entry.Kind=="Action"?entry.ChangedValueCount:entry.Changes.Count;
        if(values>0)parts.Add(values==1?"1 valor":$"{values:N0} valores");
        var names=entry.Changes.Select(c=>c.Name).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToArray();
        if(names.Length>0)parts.Add(names.Length<=3?string.Join(", ",names):string.Join(", ",names.Take(3))+$" y {names.Length-3} más");
        else if(entry.Files.Count>0&&values==0)parts.Add(entry.Files.Count==1?"1 archivo":$"{entry.Files.Count} archivos");
        return new(entry.Time,title,string.Join(" · ",parts));
    }
    /// <summary>Saved operations per section, newest first.</summary>
    internal static IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> Changes(HistoryDocument document)
    {
        var result=new Dictionary<string,List<SavedChange>>();
        foreach(var entry in document.Entries)
            if(RouteFor(entry) is string route)
            {
                if(!result.TryGetValue(route,out var list))result[route]=list=[];
                list.Add(Describe(entry));
            }
        return result.ToDictionary(p=>p.Key,p=>(IReadOnlyList<SavedChange>)p.Value.OrderByDescending(c=>c.Time).ToArray());
    }
    /// <summary>Number of saved operations per section, read from the existing history file.</summary>
    internal static IReadOnlyDictionary<string,int> Count(HistoryDocument document)=>Changes(document).ToDictionary(p=>p.Key,p=>p.Value.Count);
    // The sidebar asks after every save: reading a large history again is skipped while the file did not change.
    private static readonly Dictionary<string,((long,DateTime) Stamp,IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> Changes)> Cache=new(StringComparer.OrdinalIgnoreCase);
    internal static IReadOnlyDictionary<string,IReadOnlyList<SavedChange>> TryChanges(GameProjectSession? session)
    {
        if(session is null)return new Dictionary<string,IReadOnlyList<SavedChange>>();
        try
        {
            var stamp=session.History.Stamp();
            lock(Cache)if(Cache.TryGetValue(session.ProjectRoot,out var cached)&&cached.Stamp==stamp&&stamp.Length>0)return cached.Changes;
            var changes=Changes(session.History.Read());
            lock(Cache)Cache[session.ProjectRoot]=(stamp,changes);
            return changes;
        }
        catch(Exception ex) when(ex is System.IO.IOException or System.Text.Json.JsonException or System.IO.InvalidDataException or UnauthorizedAccessException){return new Dictionary<string,IReadOnlyList<SavedChange>>();}
    }
    internal static IReadOnlyDictionary<string,int> TryCount(GameProjectSession? session)=>TryChanges(session).ToDictionary(p=>p.Key,p=>p.Value.Count);
}

/// <summary>Saved changes of the open section, shown from the section bar («3 cambios guardados»). Presentation only.</summary>
public static class SectionChanges
{
    public static readonly DependencyProperty SummaryProperty=DependencyProperty.RegisterAttached("Summary",typeof(string),typeof(SectionChanges),new PropertyMetadata(null));
    public static readonly DependencyProperty ItemsProperty=DependencyProperty.RegisterAttached("Items",typeof(System.Collections.IEnumerable),typeof(SectionChanges),new PropertyMetadata(null));
    public static void SetSummary(DependencyObject o,string? value)=>o.SetValue(SummaryProperty,value);
    public static string? GetSummary(DependencyObject o)=>(string?)o.GetValue(SummaryProperty);
    public static void SetItems(DependencyObject o,System.Collections.IEnumerable? value)=>o.SetValue(ItemsProperty,value);
    public static System.Collections.IEnumerable? GetItems(DependencyObject o)=>(System.Collections.IEnumerable?)o.GetValue(ItemsProperty);
}

/// <summary>Backups and rebuilds done in this session, so Inicio can say what is still pending. Kept in memory only.</summary>
internal static class SessionMilestones
{
    private sealed record State(DateTimeOffset? Backup,DateTimeOffset? Rebuild,int EntriesAtRebuild,int EntriesAtBackup);
    private static readonly Dictionary<string,State> States=new(StringComparer.OrdinalIgnoreCase);
    internal static event Action? Changed;
    private static State Get(string root)=>States.GetValueOrDefault(root)??new(null,null,0,0);
    internal static void MarkBackup(GameProjectSession project){States[project.ProjectRoot]=Get(project.ProjectRoot) with {Backup=DateTimeOffset.Now,EntriesAtBackup=Entries(project)};Changed?.Invoke();}
    internal static void MarkRebuild(GameProjectSession project){States[project.ProjectRoot]=Get(project.ProjectRoot) with {Rebuild=DateTimeOffset.Now,EntriesAtRebuild=Entries(project)};Changed?.Invoke();}
    internal static (DateTimeOffset? Backup,DateTimeOffset? Rebuild,int SinceBackup,int SinceRebuild) For(GameProjectSession project,int entries)
    {
        var state=Get(project.ProjectRoot);
        return (state.Backup,state.Rebuild,state.Backup is null?entries:Math.Max(0,entries-state.EntriesAtBackup),state.Rebuild is null?entries:Math.Max(0,entries-state.EntriesAtRebuild));
    }
    private static int Entries(GameProjectSession project){try{return project.History.Read().Entries.Count;}catch(Exception ex) when(ex is System.IO.IOException or System.Text.Json.JsonException or System.IO.InvalidDataException){return 0;}}
}
