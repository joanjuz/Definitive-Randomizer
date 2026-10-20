using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Button=System.Windows.Controls.Button;
using Panel=System.Windows.Controls.Panel;
using UserControl=System.Windows.Controls.UserControl;
using Brushes=System.Windows.Media.Brushes;
using Color=System.Windows.Media.Color;
using Size=System.Windows.Size;
using Path=System.Windows.Shapes.Path;
namespace ThreeDS.Progressive;

internal sealed record SpotlightStep(FrameworkElement Target,string Title,string Text,Action? Explore=null,Action? Prepare=null);

/// <summary>A voluntary walkthrough anchored to existing controls. Never invokes save/apply actions.</summary>
internal sealed class SpotlightTour:UserControl
{
    private readonly FrameworkElement _surface;
    private readonly IReadOnlyList<SpotlightStep> _steps;
    private readonly Action _close;
    private readonly Canvas _canvas=new(){Background=Brushes.Transparent};
    private readonly Path _shade=new(){Fill=new SolidColorBrush(Color.FromArgb(185,0,0,0)),IsHitTestVisible=false};
    private readonly Border _outline=new(){BorderThickness=new Thickness(3),CornerRadius=new CornerRadius(5),IsHitTestVisible=false};
    private readonly Border _card=new(){Padding=new Thickness(18),CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1)};
    private readonly TextBlock _title=new(){FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _text=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};
    private readonly TextBlock _count=new(){Margin=new Thickness(0,0,0,8)};
    private readonly Button _back=new(){Content="Anterior",Margin=new Thickness(0,0,6,6)};
    private readonly Button _next=new(){Content="Siguiente",Margin=new Thickness(0,0,6,6)};
    private readonly Button _skip=new(){Content="Saltar todo",Margin=new Thickness(0,0,0,6)};
    private readonly Button _explore=new(){Content="Ver esta sección",Margin=new Thickness(0,0,6,6)};
    private readonly IInputElement? _previousFocus;
    private bool _disposed;
    internal int Index{get;private set;}
    internal SpotlightStep Current=>_steps[Index];
    internal Rect HighlightBounds{get;private set;}
    internal SpotlightTour(FrameworkElement surface,IReadOnlyList<SpotlightStep> steps,Action close)
    {
        if(steps.Count==0)throw new ArgumentException("No hay controles visibles que explicar.");
        _surface=surface;_steps=steps;_close=close;_previousFocus=Keyboard.FocusedElement;Focusable=true;
        Content=_canvas;_canvas.Children.Add(_shade);_canvas.Children.Add(_outline);_canvas.Children.Add(_card);
        _outline.SetResourceReference(Border.BorderBrushProperty,"Accent");_card.SetResourceReference(Border.BackgroundProperty,"Panel");_card.SetResourceReference(Border.BorderBrushProperty,"Accent");
        _title.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");_text.SetResourceReference(TextBlock.ForegroundProperty,"TextMain");_count.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");
        var body=new StackPanel();_card.Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        body.Children.Add(_count);body.Children.Add(_title);body.Children.Add(_text);var actions=new WrapPanel();body.Children.Add(actions);
        foreach(var button in new[]{_explore,_back,_next,_skip})actions.Children.Add(button);
        _back.Click+=(_,_)=>Move(-1);_next.Click+=(_,_)=>Move(1);_skip.Click+=(_,_)=>Finish();
        _explore.Click+=(_,_)=>{var action=Current.Explore;Finish();action?.Invoke();};
        System.Windows.Automation.AutomationProperties.SetName(this,"Vista guiada · "+steps[0].Title);
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Finish();}else if(e.Key==Key.Right){e.Handled=true;Move(1);}else if(e.Key==Key.Left){e.Handled=true;Move(-1);}};
        Loaded+=(_,_)=>{Show();_next.Focus();};SizeChanged+=(_,_)=>Place();_surface.LayoutUpdated+=SurfaceLayout;
        Unloaded+=(_,_)=>Dispose();Show();
    }
    internal void Move(int delta)
    {
        if(_disposed)return;
        int next=Index+delta;if(next>=_steps.Count){Finish();return;}if(next<0)return;
        Index=next;Show();
    }
    private void Show()
    {
        Current.Prepare?.Invoke();
        _title.Text=Current.Title;_text.Text=Current.Text;_count.Text=$"{Index+1} de {_steps.Count} · Vista guiada";
        _back.IsEnabled=Index>0;_next.Content=Index==_steps.Count-1?"Terminar":"Siguiente";_explore.Visibility=Current.Explore is null?Visibility.Collapsed:Visibility.Visible;
        // Opening an expander changes presentation only. Never click the explained control.
        for(DependencyObject? parent=Current.Target;parent is not null&&parent!=_surface;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is Expander expander)expander.SetCurrentValue(Expander.IsExpandedProperty,true);
            if(parent is TabItem tab)tab.SetCurrentValue(TabItem.IsSelectedProperty,true);
        }
        Current.Target.BringIntoView();Place();
    }
    private void SurfaceLayout(object? sender,EventArgs e)=>Place();
    private void Place()
    {
        if(_disposed||ActualWidth<=0||ActualHeight<=0)return;
        Rect bounds;
        try{bounds=Current.Target.TransformToVisual(this).TransformBounds(new Rect(Current.Target.RenderSize));}
        catch(InvalidOperationException){bounds=Rect.Empty;}
        var viewport=new Rect(0,0,ActualWidth,ActualHeight);
        if(!bounds.IsEmpty){bounds.Inflate(4,4);bounds.Intersect(viewport);}
        HighlightBounds=bounds;
        var hole=bounds.IsEmpty?Geometry.Empty:new RectangleGeometry(bounds,5,5);
        _shade.Data=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(viewport),hole);
        _outline.Visibility=bounds.IsEmpty?Visibility.Collapsed:Visibility.Visible;
        if(!bounds.IsEmpty){Canvas.SetLeft(_outline,bounds.Left);Canvas.SetTop(_outline,bounds.Top);_outline.Width=bounds.Width;_outline.Height=bounds.Height;}
        _card.Width=Math.Min(390,Math.Max(150,ActualWidth-24));_card.MaxHeight=Math.Max(100,ActualHeight-24);
        _card.Measure(new Size(_card.Width,_card.MaxHeight));double height=_card.DesiredSize.Height;
        double x=bounds.IsEmpty?12:bounds.Right+14,y=bounds.IsEmpty?12:bounds.Top;
        if(x+_card.Width>ActualWidth-12)x=bounds.IsEmpty?12:bounds.Left-_card.Width-14;
        if(x<12){x=Math.Max(12,ActualWidth-_card.Width-12);y=bounds.IsEmpty?12:bounds.Bottom+14;if(y+height>ActualHeight-12)y=bounds.Top-height-14;}
        Canvas.SetLeft(_card,Math.Clamp(x,12,Math.Max(12,ActualWidth-_card.Width-12)));Canvas.SetTop(_card,Math.Clamp(y,12,Math.Max(12,ActualHeight-height-12)));
    }
    internal void Finish(){if(_disposed)return;Dispose();_close();if(_previousFocus is UIElement element&&element.IsVisible&&element.IsEnabled)element.Focus();}
    private void Dispose(){if(_disposed)return;_disposed=true;_surface.LayoutUpdated-=SurfaceLayout;}

    internal static IReadOnlyList<SpotlightStep> ForContent(FrameworkElement content,string title,string description)
    {
        var steps=new List<SpotlightStep>{new(content,title,description)};
        var seen=new HashSet<DependencyObject>();
        void Visit(DependencyObject parent)
        {
            if(!seen.Add(parent))return;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);
                if(child is FrameworkElement element&&element.Visibility!=Visibility.Visible)continue;
                if(child is Expander expander)
                    steps.Add(new(expander,expander.Header?.ToString()??title,"Despliega este grupo para revisar sus campos. Cambiar opciones prepara la configuración; guardar o aplicar requiere su acción correspondiente."));
                else if(child is TabItem tab)
                    steps.Add(new(tab,tab.Header?.ToString()??title,"Esta pestaña reúne una subsección del editor. Puedes cambiar entre vistas para revisar los datos antes de aplicar."));
                else if(child is Button button)
                {
                    string label=button.Content as string??System.Windows.Automation.AutomationProperties.GetName(button);
                    if(!string.IsNullOrWhiteSpace(label))steps.Add(new(button,label,Description(label)));
                }
                Visit(child);
                if(child is Expander {Content:DependencyObject expanded})Visit(expanded);
                if(child is TabItem {Content:DependencyObject tabContent})Visit(tabContent);
            }
        }
        Visit(content);return steps;
    }
    private static string Description(string label)=>label=="Modo simplificado"?"Muestra las secciones principales. Conserva los editores y las opciones; puedes volver a Inicio y cambiar al modo completo.":label=="Modo completo"?"Muestra todos los editores y herramientas compatibles con el proyecto abierto.":label.StartsWith("Randomizado guiado")?"Elige las secciones y sus opciones paso a paso. Los cambios se ejecutan sobre una copia únicamente después de confirmar el resumen final.":label.StartsWith("Vista guiada")?"Recorre los controles de la aplicación resaltados en color. Puedes retroceder, terminar o saltar todo en cualquier momento.":label.Contains("Guardar",StringComparison.OrdinalIgnoreCase)||label.Contains("Aplicar",StringComparison.OrdinalIgnoreCase)?"Esta acción confirma los cambios preparados. La vista guiada solo la señala: no la ejecuta ni guarda datos.":label.Contains("previa",StringComparison.OrdinalIgnoreCase)?"Prepara una propuesta para revisar las diferencias antes de aplicarlas.":label.Contains("Randomizar",StringComparison.OrdinalIgnoreCase)?"Abre o prepara las opciones de randomización de esta sección. Revisa el alcance y la propuesta antes de confirmar.":label.Contains("Importar",StringComparison.OrdinalIgnoreCase)?"Carga una configuración o template compatible para revisarlo en este editor.":label.Contains("Exportar",StringComparison.OrdinalIgnoreCase)?"Guarda la configuración en un archivo reutilizable.":"Control de la sección actual. Durante este recorrido solo se explica; no se pulsa ni modifica los datos.";
}
