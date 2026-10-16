using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Orientation=System.Windows.Controls.Orientation;
using Window=System.Windows.Window;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
using CheckBox=System.Windows.Controls.CheckBox;

namespace ThreeDS.Progressive;

internal sealed class TextCommandsWindow : Window
{
    internal TextCommandsWindow(IReadOnlyList<TextCommandEntry> entries,string game)
    {
        Title="Variables y comandos · "+game;MaxWidth=SystemParameters.WorkArea.Width-24;MaxHeight=SystemParameters.WorkArea.Height-24;Width=Math.Min(1000,MaxWidth);Height=Math.Min(720,MaxHeight);MinWidth=Math.Min(560,Width);MinHeight=Math.Min(320,Height);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResizeWithGrip;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=(Brush)FindResource("Bg");Foreground=(Brush)FindResource("TextMain");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var title=new TextBlock{Text=Title,FontSize=22,FontWeight=FontWeights.SemiBold};
        title.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==1)DragMove();};heading.Children.Add(title);
        heading.Children.Add(new TextBlock{Text="Referencia del formato de texto del juego abierto. Los códigos reconocidos pueden ser heredados y no implican que la mecánica esté disponible. Copiar no modifica la ROM.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)});
        var search=new TextBox{ToolTip="Buscar por nombre o función: jugador, color, movimiento, salto…",Margin=new Thickness(0,0,0,8)};heading.Children.Add(search);
        var onlyObserved=new CheckBox{Content="Solo códigos presentes en el archivo guardado seleccionado",Margin=new Thickness(0,0,0,10)};heading.Children.Add(onlyObserved);
        var footer=new WrapPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var copy=new Button{Content="Copiar código",IsEnabled=false,Margin=new Thickness(0,0,8,0)};footer.Children.Add(copy);
        var close=new Button{Content="Cerrar",IsCancel=true};close.Click+=(_,_)=>Close();footer.Children.Add(close);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=150});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.6,GridUnitType.Star),MinWidth=180});root.Children.Add(grid);
        var list=new ListBox{DisplayMemberPath="Display",Margin=new Thickness(0,0,16,0)};grid.Children.Add(list);
        var label=new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Display"));
        label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);label.SetValue(TextBlock.MaxWidthProperty,285d);
        list.DisplayMemberPath="";list.ItemTemplate=new DataTemplate{VisualTree=label};
        var details=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),Padding=new Thickness(14)};
        Grid.SetColumn(details,1);grid.Children.Add(details);
        System.Windows.Automation.AutomationProperties.SetName(search,"Buscar variables y comandos");
        System.Windows.Automation.AutomationProperties.SetName(list,"Variables y comandos disponibles");
        System.Windows.Automation.AutomationProperties.SetName(details,"Función, sintaxis y ejemplo del comando");
        EditorPresentation.StyleAction(copy);EditorPresentation.StyleAction(close,EditorPresentation.ActionRole.Tertiary);
        grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(0)});
        grid.SizeChanged+=(_,_)=>{bool compact=grid.ActualWidth<600;grid.ColumnDefinitions[1].MinWidth=compact?0:180;grid.ColumnDefinitions[1].Width=compact?new GridLength(0):new GridLength(1.6,GridUnitType.Star);grid.RowDefinitions[1].Height=compact?new GridLength(1,GridUnitType.Star):new GridLength(0);Grid.SetColumn(details,compact?0:1);Grid.SetRow(details,compact?1:0);};
        void Filter()
        {
            string term=TextContentClassifier.Normalize(search.Text);
            list.ItemsSource=entries.Where(e=>(onlyObserved.IsChecked!=true||e.Observed)&&TextContentClassifier.Normalize(e.Name+" "+e.Function+" "+e.Notes+" "+e.Syntax).Contains(term,StringComparison.Ordinal)).ToArray();
            list.SelectedIndex=0;
        }
        list.SelectionChanged+=(_,_)=>
        {
            copy.Content="Copiar código";
            var entry=list.SelectedItem as TextCommandEntry;copy.IsEnabled=entry?.CanCopy==true;
            details.Text=entry is null?"No hay coincidencias.":$"{entry.Name}\n\nFunción\n{entry.Function}\n\nSintaxis\n{entry.Syntax}\n\nUso\n{entry.Notes}\n\n{(entry.Observed?"Ejemplo del archivo":"Ejemplo de sintaxis")}\n{(entry.CanCopy?entry.Example:"Sin ejemplo copiable; consultá un uso original.")}\n\nLos parámetros dependen del evento. No añadas variables que su script no prepara. No cambies la cantidad ni el orden de las líneas del banco.";
        };
        copy.Click+=(_,_)=>
        {
            if(list.SelectedItem is not TextCommandEntry{CanCopy:true} entry)return;
            try{System.Windows.Clipboard.SetText(entry.Example);copy.Content="Copiado";}
            catch(System.Runtime.InteropServices.COMException){details.AppendText("\n\nNo se pudo acceder al portapapeles. Podés seleccionar el código y copiarlo manualmente.");}
        };
        search.TextChanged+=(_,_)=>Filter();onlyObserved.Checked+=(_,_)=>Filter();onlyObserved.Unchecked+=(_,_)=>Filter();Filter();
    }
}
