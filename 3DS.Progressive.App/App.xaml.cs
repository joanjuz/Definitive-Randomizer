using System.Windows;

namespace ThreeDS.Progressive;

public partial class App : System.Windows.Application
{
    public App()
    {
        InitializeComponent();
        AccessiblePalette.Install(this);
        PresentationAccessibility.Install();
    }
}
