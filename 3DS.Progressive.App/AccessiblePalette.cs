using System.ComponentModel;
using SystemColors=System.Windows.SystemColors;
using System.Windows;
using System.Windows.Media;
namespace ThreeDS.Progressive;
internal static class AccessiblePalette
{
    private static readonly Dictionary<string,object> Defaults=new();
    internal static void Install(System.Windows.Application app)
    {
        void Collect(ResourceDictionary dictionary)
        {
            foreach(var merged in dictionary.MergedDictionaries)Collect(merged);
            foreach(object key in dictionary.Keys)if(key is string name&&dictionary[key] is SolidColorBrush)Defaults[name]=dictionary[key];
        }
        Collect(app.Resources);
        Update(app);SystemParameters.StaticPropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(SystemParameters.HighContrast))app.Dispatcher.Invoke(()=>Update(app));};
    }
    internal static void Update(System.Windows.Application app,bool? contrastOverride=null)
    {
        foreach(string key in Defaults.Keys)
        {
            object value=Defaults[key];
            if(contrastOverride??SystemParameters.HighContrast)value=key switch
            {
                "SelectionText"=>SystemColors.HighlightTextBrush,
                "TextMain" or "TextSoft" or "ErrorText" or "BrandText" or "SuccessText" or "BadgeText" or "ScrollThumb" or "IllustrationStroke"=>SystemColors.WindowTextBrush,
                "InputStroke" or "Stroke" or "PrimaryStroke" or "Accent"=>SystemColors.HighlightBrush,
                "Selected" or "Hover" or "PrimaryFill" or "NavActiveFill"=>SystemColors.HighlightBrush,
                "DisabledText"=>SystemColors.GrayTextBrush,
                _ when key.StartsWith("DetailForeground")||key.StartsWith("DetailBorderBrush")||key.StartsWith("DetailStroke")||key.StartsWith("DetailFill")=>SystemColors.WindowTextBrush,
                _=>SystemColors.WindowBrush
            };
            app.Resources[key]=value is SolidColorBrush brush?brush.Clone():value;
        }
    }
    internal static void BindPalette(FrameworkElement control)
    {
        IEnumerable<DependencyProperty> properties=control switch
        {
            System.Windows.Controls.Control=>[System.Windows.Controls.Control.BackgroundProperty,System.Windows.Controls.Control.ForegroundProperty,System.Windows.Controls.Control.BorderBrushProperty],
            System.Windows.Controls.Border=>[System.Windows.Controls.Border.BackgroundProperty,System.Windows.Controls.Border.BorderBrushProperty],
            System.Windows.Controls.Panel=>[System.Windows.Controls.Panel.BackgroundProperty],
            System.Windows.Controls.TextBlock=>[System.Windows.Controls.TextBlock.BackgroundProperty,System.Windows.Controls.TextBlock.ForegroundProperty],
            System.Windows.Shapes.Shape=>[System.Windows.Shapes.Shape.FillProperty,System.Windows.Shapes.Shape.StrokeProperty],
            _=>[]
        };
        foreach(var property in properties)
        {
            if(System.Windows.Data.BindingOperations.IsDataBound(control,property))continue;
            if(control.ReadLocalValue(property) is not SolidColorBrush brush)continue;
            foreach(string key in Defaults.Keys)
                if(ReferenceEquals(brush,System.Windows.Application.Current.FindResource(key))){control.SetResourceReference(property,key);break;}
        }
    }
}
