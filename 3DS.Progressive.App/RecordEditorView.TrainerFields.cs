using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

// Trainer editor details: the list says who each trainer is (class, team size, levels), gender and ability of Gen7 team
// members are chosen by name instead of a raw number, and IV/EV form one table per stat. Values and keys are unchanged.
public partial class RecordEditorView
{
    private sealed class FuncConverter(Func<object,object> convert):IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>convert(value);
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
    }
    private readonly Dictionary<int,string> _trainerSubtitles=[];
    private bool ListsTrainers=>Editor is GameTrainerService or ThreeDS.Progressive.Services.Nds.NdsRecordEditor {UsesTrainerLayout:true};
    /// <summary>«Joven · 3 Pokémon · Nv. 12–15», read from the trainer record (cached; cleared after a save or an applied proposal).</summary>
    internal string TrainerSubtitle(int id)
    {
        if(_trainerSubtitles.TryGetValue(id,out var cached))return cached;
        string text="";
        try
        {
            var record=Editor.Read(id);var fields=record.SelectMany(g=>g.Fields).GroupBy(f=>f.Key).ToDictionary(g=>g.Key,g=>g.First());
            var parts=new List<string>();
            if(fields.TryGetValue("Class",out var trainerClass)&&trainerClass.Choices?.FirstOrDefault(c=>c.Id==trainerClass.Value) is MoveChoice choice)parts.Add(Clean(choice.Display));
            int count=fields.TryGetValue("Count",out var size)?size.Value:0;
            parts.Add(count==1?"1 Pokémon":$"{count} Pokémon");
            var levels=Enumerable.Range(0,count).Select(s=>fields.TryGetValue($"P{s}.Level",out var level)?level.Value:(int?)null).OfType<int>().ToArray();
            if(levels.Length>0)parts.Add(levels.Min()==levels.Max()?$"Nv. {levels.Min()}":$"Nv. {levels.Min()}–{levels.Max()}");
            if(fields.TryGetValue("Mode",out var mode)&&mode.Value==1)parts.Add("doble");
            text=string.Join(" · ",parts);
        }
        catch(Exception ex) when(ex is System.IO.InvalidDataException or ArgumentException or InvalidOperationException){}
        return _trainerSubtitles[id]=text;
    }
    private void InvalidateTrainerSubtitles()
    {
        if(!ListsTrainers)return;
        _trainerSubtitles.Clear();
        if(RecordList.ItemsSource is not null)CollectionViewSource.GetDefaultView(RecordList.ItemsSource).Refresh();
    }
    private void UseTrainerListTemplate()
    {
        if(!ListsTrainers)return;
        var root=new FrameworkElementFactory(typeof(StackPanel));
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(RecordEntry.Display)));name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        var detail=new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding{Converter=new FuncConverter(o=>o is RecordEntry entry?TrainerSubtitle(entry.Id):"")});
        detail.SetValue(TextBlock.FontSizeProperty,11d);detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);detail.SetValue(UIElement.OpacityProperty,.78);
        detail.SetValue(FrameworkElement.MarginProperty,new Thickness(0,2,0,0));
        root.AppendChild(name);root.AppendChild(detail);
        RecordList.ItemTemplate=new DataTemplate(typeof(RecordEntry)){VisualTree=root};
    }
    /// <summary>Gen7 team members: gender and ability slot by name. The stored values (0–3) are the same.</summary>
    private static IReadOnlyList<EditorField> PresentTrainerFields(IReadOnlyList<EditorField> fields)
    {
        if(!fields.Any(f=>f.Key.EndsWith(".Shiny",StringComparison.Ordinal)))return fields;
        MoveChoice[] gender=[new(0,"Aleatorio"),new(1,"Macho"),new(2,"Hembra"),new(3,"Valor 3")];
        MoveChoice[] ability=[new(0,"Cualquiera (1 o 2)"),new(1,"Habilidad 1"),new(2,"Habilidad 2"),new(3,"Habilidad oculta")];
        return fields.Select(f=>f.Choices is not null||f.Max!=3?f
            :f.Key.EndsWith(".Gender",StringComparison.Ordinal)?f with {Choices=gender,Label="Género"}
            :f.Key.EndsWith(".Ability",StringComparison.Ordinal)?f with {Choices=ability,Label="Habilidad"}:f).ToArray();
    }
    /// <summary>IV and EV of a team member as one row per stat, with the EV total.</summary>
    private FrameworkElement BuildStatTable(IReadOnlyList<EditorField> fields,Dictionary<string,FrameworkElement> controls)
    {
        var byKey=fields.ToDictionary(f=>f.Key);
        string prefix=fields[0].Key[..(fields[0].Key.LastIndexOf('.')+1)];
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(110)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MaxWidth=160});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MaxWidth=160});
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        string[] headers=["Estadística","IV (0–31)","EV (0–255)"];
        for(int c=0;c<3;c++){var h=new TextBlock{Text=headers[c],FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,8,6)};h.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");Grid.SetColumn(h,c);grid.Children.Add(h);}
        var total=new TextBlock{FontSize=12,Margin=new Thickness(0,8,0,0)};total.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        void UpdateTotal()
        {
            int sum=Enumerable.Range(0,6).Sum(j=>controls.TryGetValue(prefix+"EV"+j,out var c)&&c is TextBox box&&int.TryParse(box.Text,out int v)?v:0);
            total.Text=$"Total EV: {sum} / 510"+(sum>510?" · supera el máximo del juego":"");
        }
        for(int j=0;j<6;j++)
        {
            int row=j+1;grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            // The stat name comes from the field label («IV PS» → «PS»), so the order of the service is respected.
            string stat=byKey.TryGetValue(prefix+"IV"+j,out var iv)&&iv.Label.StartsWith("IV ",StringComparison.Ordinal)?iv.Label[3..]:$"Stat {j+1}";
            var label=new TextBlock{Text=stat,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,6)};
            Grid.SetRow(label,row);grid.Children.Add(label);
            foreach(var (key,column) in new[]{(prefix+"IV"+j,1),(prefix+"EV"+j,2)})
            {
                if(!byKey.TryGetValue(key,out var field))continue;
                var input=CreateFieldInput(field,controls,true,false);input.Margin=new Thickness(0,0,8,6);
                if(input is TextBox box){box.SetResourceReference(StyleProperty,"GridTextBox");if(column==2)box.TextChanged+=(_,_)=>UpdateTotal();}
                Grid.SetRow(input,row);Grid.SetColumn(input,column);grid.Children.Add(input);
            }
        }
        var rest=fields.Where(f=>!controls.ContainsKey(f.Key)).ToArray();
        var stack=new StackPanel();stack.Children.Add(grid);stack.Children.Add(total);
        if(rest.Length>0)stack.Children.Add(BuildFieldsGrid(rest,controls,true));
        UpdateTotal();
        return stack;
    }
}
