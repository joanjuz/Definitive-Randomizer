using System.Windows;
using System.Windows.Controls;
using Button=System.Windows.Controls.Button;
using Panel=System.Windows.Controls.Panel;
namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldPrototypeWindow
{
    private Button _gameplayButton=null!;
    private Button _pokeRuntimeButton=null!;
    private void BuildGameplay(Panel panel)
    {
        _pokeRuntimeButton=AddButton(panel,"Runtime · encuentros, level caps y herramientas…",()=>
        {
            if(_project is null)return;
            if(EmbeddedNavigate is not null){EmbeddedNavigate("Runtime");return;}
            new SwordPokeFeurWindow(_project){Owner=DialogOwner}.ShowDialog();
            _preview=null;RefreshJournal();RefreshButtons();
        });
        _gameplayButton=AddButton(panel,"Entrenadores, encuentros y Field Items…",()=>
        {
            if(_project is null)return;
            if(EmbeddedNavigate is not null){EmbeddedNavigate("Gameplay");return;}
            var window=new SwordShieldGameplayWindow(_project){Owner=DialogOwner};window.ShowDialog();
            _preview=null;RefreshJournal();RefreshButtons();
        });
    }
}
