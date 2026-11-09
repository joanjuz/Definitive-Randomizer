using System.Windows;
using System.Windows.Controls;
using ThreeDS.Progressive.Services;
using CheckBox=System.Windows.Controls.CheckBox;

namespace ThreeDS.Progressive;

/// <summary>A single session setting, shared by the supported item editors; recipes persist it.</summary>
internal sealed class UsumMintPoolControl : StackPanel
{
    internal UsumMintPoolControl(GameProjectSession project,Action? changed=null,bool showStatus=true)
    {
        if(!project.GameConfiguration.USUM){Visibility=Visibility.Collapsed;return;}
        Margin=new Thickness(0,4,0,10);
        var toggle=new CheckBox{Content=new TextBlock{Text=UsumMintItemPolicy.Label,TextWrapping=TextWrapping.Wrap},IsChecked=project.MintItems.Include,IsEnabled=false};
        System.Windows.Automation.AutomationProperties.SetName(toggle,UsumMintItemPolicy.Label);
        var report=new TextBlock{Text="Comprobando dependencia de mentas…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0),Visibility=showStatus?Visibility.Visible:Visibility.Collapsed};
        report.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");Children.Add(toggle);if(showStatus)Children.Add(report);
        bool refreshing=false,available=false;
        Loaded+=async(_,_)=>
        {
            refreshing=true;toggle.IsEnabled=false;
            try
            {
                var state=await Task.Run(project.MintItems.Inspect);
                available=state.Available;toggle.IsChecked=project.MintItems.Include;toggle.IsEnabled=available||project.MintItems.Include;report.Text=state.Report;
            }
            catch(Exception ex){report.Text="No se pudo verificar el parche: "+ex.Message;}
            finally{refreshing=false;}
        };
        toggle.Click+=async(_,_)=>
        {
            if(refreshing)return;
            using var interaction=UiProgress.BlockInteraction();
            bool requested=toggle.IsChecked==true;toggle.IsEnabled=false;
            try
            {
                if(requested)await Task.Run(()=>project.MintItems.Capture());
                project.MintItems.Include=requested;changed?.Invoke();
            }
            catch(Exception ex){available=false;toggle.IsChecked=project.MintItems.Include;report.Text=ex.Message;}
            finally{toggle.IsEnabled=available||project.MintItems.Include;}
        };
    }
}
