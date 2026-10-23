using System.Windows;
using ThreeDS.Progressive.Services.Switch;
namespace ThreeDS.Progressive;
public partial class RecordEditorView
{
    private void ShowSwitchTradePreview()
    {
        if(Editor is not SwordShieldRecordEditor sw)return;
        try
        {
            Randomizer_Click(this,new RoutedEventArgs());
            if(_options.TryGetValue("Trade",out var trade)&&trade is System.Windows.Controls.CheckBox check)check.IsChecked=true;
            if(_options.TryGetValue("Enabled",out var enabled)&&enabled is System.Windows.Controls.CheckBox flag)flag.IsChecked=false;
            _proposal=sw.PreviewTradeRemoval();ChangeList.ItemsSource=_proposal.Changes;ProposalText.Text=$"{_proposal.Changes.Count} cambios: métodos de intercambio normal, con objeto y Shelmet/Karrablast → subir de nivel al 37. Destinos y formas se conservan. Revisa y aplica la propuesta.";ApplyButton.IsEnabled=_proposal.Changes.Count>0;
        }
        catch(Exception ex){Invalidate();ProposalText.Text=ex.Message;}
    }
}
