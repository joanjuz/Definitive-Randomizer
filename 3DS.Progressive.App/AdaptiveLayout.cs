using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using DataGridCell=System.Windows.Controls.DataGridCell;
using Grid=System.Windows.Controls.Grid;
using Control=System.Windows.Controls.Control;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
namespace ThreeDS.Progressive;

// Presentation only: controls stay in their original namescope and retain their bindings.
public static class AdaptiveLayout
{
    public static readonly DependencyProperty InspectorProperty=DependencyProperty.RegisterAttached("Inspector",typeof(string),typeof(AdaptiveLayout),new PropertyMetadata(null,Configure));
    public static readonly DependencyProperty CompactWidthProperty=DependencyProperty.RegisterAttached("CompactWidth",typeof(double),typeof(AdaptiveLayout),new PropertyMetadata(880d));
    public static readonly DependencyProperty HeaderProperty=DependencyProperty.RegisterAttached("Header",typeof(bool),typeof(AdaptiveLayout),new PropertyMetadata(false,Configure));
    public static void SetInspector(DependencyObject o,string value)=>o.SetValue(InspectorProperty,value);
    public static string GetInspector(DependencyObject o)=>(string)o.GetValue(InspectorProperty);
    public static void SetCompactWidth(DependencyObject o,double value)=>o.SetValue(CompactWidthProperty,value);
    public static double GetCompactWidth(DependencyObject o)=>(double)o.GetValue(CompactWidthProperty);
    public static void SetHeader(DependencyObject o,bool value)=>o.SetValue(HeaderProperty,value);
    public static bool GetHeader(DependencyObject o)=>(bool)o.GetValue(HeaderProperty);
    private static void Configure(DependencyObject o,DependencyPropertyChangedEventArgs e)
    {
        if(o is not Grid grid)return;
        grid.Loaded-=Loaded;grid.Loaded+=Loaded;
    }
    private static void Loaded(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource))return;
        var grid=(Grid)sender;
        if(GetHeader(grid)){ConfigureHeader(grid);return;}
        var scope=Window.GetWindow(grid) as FrameworkElement;
        FrameworkElement? owner=grid;
        Expander? inspector=null;
        while(owner is not null&&inspector is null){inspector=owner.FindName(GetInspector(grid)) as Expander;owner=LogicalTreeHelper.GetParent(owner) as FrameworkElement;}
        inspector??=scope?.FindName(GetInspector(grid)) as Expander;
        if(inspector is null)return;
        ConfigureInspector(grid,inspector);
    }
    internal static void ConfigureInspector(Grid grid,Expander inspector)
    {
        if((bool)grid.GetValue(ConfiguredProperty))return;grid.SetValue(ConfiguredProperty,true);
        int column=Grid.GetColumn(inspector);var definition=grid.ColumnDefinitions[column];
        var width=definition.Width;double min=definition.MinWidth,max=definition.MaxWidth;
        if(grid.RowDefinitions.Count==0)grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        foreach(UIElement child in grid.Children)if(!ReferenceEquals(child,inspector))Grid.SetRowSpan(child,2);
        bool? previous=null;
        void Update()
        {
            bool compact=grid.ActualWidth<GetCompactWidth(grid);
            if(previous==compact)return;previous=compact;
            definition.MinWidth=compact?0:min;definition.MaxWidth=compact?0:max;definition.Width=compact?new GridLength(0):width;
            Grid.SetColumn(inspector,compact?column-1:column);Grid.SetRow(inspector,compact?1:0);Grid.SetRowSpan(inspector,compact?1:2);
            inspector.MaxHeight=compact?Math.Max(140,grid.ActualHeight*.42):double.PositiveInfinity;
            foreach(UIElement child in grid.Children)
                if(!ReferenceEquals(child,inspector)&&Grid.GetColumn(child)==column-1&&Grid.GetColumnSpan(child)==1)Grid.SetRowSpan(child,compact?1:2);
            inspector.IsExpanded=!compact;
        }
        grid.SizeChanged+=(_,_)=>{Update();if(previous==true)inspector.MaxHeight=Math.Max(140,grid.ActualHeight*.42);};Update();
    }
    private static readonly DependencyProperty ConfiguredProperty=DependencyProperty.RegisterAttached("Configured",typeof(bool),typeof(AdaptiveLayout),new PropertyMetadata(false));
    internal static void ConfigureHeader(Grid grid)
    {
        if((bool)grid.GetValue(ConfiguredProperty))return;grid.SetValue(ConfiguredProperty,true);
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var actions=grid.Children.OfType<WrapPanel>().Single();
        void Update(){bool compact=grid.ActualWidth<1100;Grid.SetRow(actions,compact?1:0);Grid.SetColumn(actions,compact?0:2);Grid.SetColumnSpan(actions,compact?3:1);actions.MaxWidth=compact?double.PositiveInfinity:530;actions.Margin=compact?new Thickness(0,12,0,0):new Thickness(0);}
        grid.SizeChanged+=(_,_)=>Update();Update();
    }
}
internal static class PresentationAccessibility
{
    private static readonly DependencyProperty WiredProperty=DependencyProperty.RegisterAttached("Wired",typeof(bool),typeof(PresentationAccessibility),new PropertyMetadata(false));
    internal static void Install()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnElementLoaded));
        EventManager.RegisterClassHandler(typeof(Control),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnControlLoaded));
        EventManager.RegisterClassHandler(typeof(UniformGrid),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnFormLoaded));
    }
    private static void OnElementLoaded(object sender,RoutedEventArgs e)
    {
        if(ReferenceEquals(sender,e.OriginalSource)&&sender is FrameworkElement element)AccessiblePalette.BindPalette(element);
    }
    private static void OnControlLoaded(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource)||sender is not Control control)return;
        if(control is not (TextBox or ComboBox or System.Windows.Controls.Button))return;
        if(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))||AutomationProperties.GetLabeledBy(control) is not null)return;
        if(control.Parent is System.Windows.Controls.Panel parent)
        {
            int index=parent.Children.IndexOf(control);
            if(index>0&&parent.Children[index-1] is TextBlock label&&!string.IsNullOrWhiteSpace(label.Text))
            {AutomationProperties.SetLabeledBy(control,label);AutomationProperties.SetHelpText(control,label.Text);return;}
        }
        DependencyObject? current=control;
        while(current is not null)
        {
            if(current is DataGridCell cell){AutomationProperties.SetName(control,cell.Column.Header?.ToString()??"Valor");return;}
            current=VisualTreeHelper.GetParent(current);
        }
        if(control is TextBox or ComboBox)
        {
            string hint=control.ToolTip as string??control.Name;
            if(!string.IsNullOrWhiteSpace(hint))AutomationProperties.SetName(control,hint);
        }
    }
    private static void OnFormLoaded(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.OriginalSource)||sender is not UniformGrid form||form.Columns!=2||form.Rows!=0||(bool)form.GetValue(WiredProperty))return;
        form.SetValue(WiredProperty,true);
        void Update()=>form.Columns=form.ActualWidth<460?1:2;
        form.SizeChanged+=(_,_)=>Update();Update();
    }
}
