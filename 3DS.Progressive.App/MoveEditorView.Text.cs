using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using ThreeDS.Progressive.Services;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

// «Nombre y descripción en el juego»: edits the text of the selected move without going to Game Text or a template.
// It saves through GameTextService, the same verified path (and history entry) as the Game Text editor, in the active language.
public partial class MoveEditorView
{
    private GameProjectSession? _project;
    private GameTextService? _text;
    private int _nameFile=-1,_flavorFile=-1;
    private TextBox? _moveName,_moveDescription;
    private TextBlock? _textStatus;
    private Button? _saveText,_resetText;
    private (string Name,string Description)? _savedText;
    internal TextBox? MoveTextName=>_moveName;
    internal TextBox? MoveTextDescription=>_moveDescription;

    private void InitializeTextCard(GameProjectSession project)
    {
        _project=project;
        _nameFile=project.GameConfiguration.GameText.FirstOrDefault(t=>t.Name==TextName.MoveNames)?.Index??-1;
        _flavorFile=project.GameConfiguration.GameText.FirstOrDefault(t=>t.Name==TextName.MoveFlavor)?.Index??-1;
        if(_nameFile<0||_flavorFile<0||MoveFieldsPanel.Content is not StackPanel page)return;
        var card=new Border();card.SetResourceReference(StyleProperty,"MoveCard");
        var stack=new StackPanel();card.Child=stack;
        var heading=new TextBlock{Text="NOMBRE Y DESCRIPCIÓN EN EL JUEGO",FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};
        heading.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");stack.Children.Add(heading);
        TextBlock Caption(string text){var caption=new TextBlock{Text=text};caption.SetResourceReference(StyleProperty,"MoveLabel");stack.Children.Add(caption);return caption;}
        var nameCaption=Caption("Nombre");
        _moveName=new TextBox{Margin=new Thickness(0,0,0,12),MaxLength=40};
        System.Windows.Automation.AutomationProperties.SetLabeledBy(_moveName,nameCaption);stack.Children.Add(_moveName);
        var descriptionCaption=Caption("Descripción (los saltos de línea son los del juego)");
        _moveDescription=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=78,VerticalContentAlignment=VerticalAlignment.Top,Margin=new Thickness(0,0,0,10)};
        System.Windows.Automation.AutomationProperties.SetLabeledBy(_moveDescription,descriptionCaption);stack.Children.Add(_moveDescription);
        var actions=new WrapPanel();stack.Children.Add(actions);
        var preview=new Button{Content="Vista previa en el juego…",Margin=new Thickness(0,0,8,8),ToolTip="Muestra la descripción con la fuente y el recuadro del juego"};
        _saveText=new Button{Content="Guardar texto",Margin=new Thickness(0,0,8,8),IsEnabled=false};
        _resetText=new Button{Content="Restablecer texto",Margin=new Thickness(0,0,8,8),IsEnabled=false};
        EditorPresentation.StyleAction(_saveText,EditorPresentation.ActionRole.Primary);EditorPresentation.StyleAction(_resetText,EditorPresentation.ActionRole.Tertiary);
        actions.Children.Add(preview);actions.Children.Add(_saveText);actions.Children.Add(_resetText);
        _textStatus=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Text=$"Se guarda en Game Text (idioma {project.GameConfiguration.Language}) y queda en el historial, igual que si lo editaras allí."};
        _textStatus.SetResourceReference(TextBlock.ForegroundProperty,"TextSoft");stack.Children.Add(_textStatus);
        _moveName.TextChanged+=(_,_)=>TextEdited();_moveDescription.TextChanged+=(_,_)=>TextEdited();
        preview.Click+=(_,_)=>PreviewDescription();
        _saveText.Click+=(_,_)=>SaveMoveText();
        _resetText.Click+=(_,_)=>{if(_selected is not null)LoadMoveText(_selected.Id);};
        // Right after the basic data: name and description are what a player sees first.
        int index=page.Children.OfType<Border>().FirstOrDefault() is Border first?page.Children.IndexOf(first)+1:page.Children.Count;
        page.Children.Insert(index,card);
    }
    private GameTextService Text=>_text??=new GameTextService(_project!);
    private static string FromGame(string line)=>line.Replace("\\n","\n");
    private static string ToGame(string text)=>text.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\\n");
    internal bool HasTextChanges=>_savedText is {} saved&&_moveName is not null&&_moveDescription is not null
        &&(saved.Name!=_moveName.Text||ToGame(saved.Description)!=ToGame(_moveDescription.Text));
    private void LoadMoveText(int id)
    {
        if(_project is null||_moveName is null||_moveDescription is null)return;
        try
        {
            var names=Text.Read(_nameFile);var flavor=Text.Read(_flavorFile);
            string name=id<names.Count?names[id]:"",description=id<flavor.Count?FromGame(flavor[id]):"";
            _savedText=(name,description);
            _moveName.Text=name;_moveDescription.Text=description;
            _moveName.IsEnabled=id<names.Count;_moveDescription.IsEnabled=id<flavor.Count;
        }
        catch(Exception ex) when(ex is System.IO.IOException or System.IO.InvalidDataException or ArgumentException)
        {
            _savedText=null;_moveName.IsEnabled=_moveDescription.IsEnabled=false;
            if(_textStatus is not null)_textStatus.Text="No se pudo leer el texto del movimiento: "+ex.Message;
        }
        TextEdited();
    }
    private void TextEdited()
    {
        bool changed=HasTextChanges;
        if(_saveText is not null)_saveText.IsEnabled=changed;
        if(_resetText is not null)_resetText.IsEnabled=changed;
    }
    private void PreviewDescription()
    {
        if(_project is null||_selected is null||_moveDescription is null)return;
        int type=MoveType.SelectedIndex,category=MoveCategory.SelectedIndex;
        int Number(TextBox box)=>int.TryParse(box.Text,out int value)?value:0;
        string typeName=type>=0&&type<_project.Types.Count?_project.Types[type]:type.ToString();
        var example=new MovePreviewExample(_moveName?.Text??_selected.Name,type,typeName,category,Number(MovePower),Number(MoveAccuracy),Number(MovePP));
        var dialog=new TemplateDescriptionWindow(_moveDescription.Text,_project,example){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true)_moveDescription.Text=dialog.Description;
    }
    internal void SaveMoveText()
    {
        if(_project is null||_selected is null||_savedText is not {} saved||_moveName is null||_moveDescription is null||!HasTextChanges)return;
        int id=_selected.Id;
        string name=_moveName.Text.Trim(),description=ToGame(_moveDescription.Text.TrimEnd());
        if(name.Length==0){StudioDialog.Error(Window.GetWindow(this),"Nombre del movimiento","El nombre no puede quedar vacío.");return;}
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar texto del movimiento",
            $"Se guardará el texto de {saved.Name} en Game Text.",
            (name!=saved.Name?$"Nombre: {saved.Name} → {name}\n":"")+(description!=ToGame(saved.Description)?"Descripción:\n"+FromGame(description):"")))return;
        try
        {
            if(name!=saved.Name){var names=Text.Read(_nameFile).ToArray();names[id]=name;Text.Save(_nameFile,names);}
            if(description!=ToGame(saved.Description)){var flavor=Text.Read(_flavorFile).ToArray();flavor[id]=description;Text.Save(_flavorFile,flavor);}
            LoadMoveText(id);
            if(name!=saved.Name)MoveNameText.Text=name;
            if(_textStatus is not null)_textStatus.Text="Texto guardado y verificado en Game Text; queda registrado en el historial. Los demás editores muestran el nombre nuevo al volver a abrir el proyecto.";
        }
        catch(Exception ex) when(ex is System.IO.IOException or System.IO.InvalidDataException or ArgumentException or InvalidOperationException)
        {
            StudioDialog.Error(Window.GetWindow(this),"No se pudo guardar el texto",ex.Message);
        }
    }
}
