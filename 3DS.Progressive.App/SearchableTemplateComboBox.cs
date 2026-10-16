using System.Collections;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThreeDS.Progressive.Services;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
namespace ThreeDS.Progressive;

/// <summary>A private filtered view per selector. Typing never writes an unknown ID to a template.</summary>
internal sealed class SearchableTemplateComboBox : ComboBox
{
    public static readonly DependencyProperty ChoiceValueProperty=DependencyProperty.Register(nameof(ChoiceValue),typeof(string),typeof(SearchableTemplateComboBox),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,(d,_)=>((SearchableTemplateComboBox)d).Synchronize()));
    public string ChoiceValue{get=>(string)GetValue(ChoiceValueProperty);set=>SetValue(ChoiceValueProperty,value);}
    private TemplateChoice[] _choices=[];
    private ListCollectionView? _view;
    private TextBox? _editor;
    private bool _wrapping,_synchronizing,_filtering,_typing;
    public SearchableTemplateComboBox()
    {
        SetResourceReference(StyleProperty,typeof(ComboBox));
        IsEditable=true;IsTextSearchEnabled=false;StaysOpenOnEdit=true;IsSynchronizedWithCurrentItem=false;
        DisplayMemberPath="Label";SelectedValuePath="Value";
        ToolTip="Escribí parte del nombre o su número para buscar. Elegí un resultado para confirmar.";
    }
    protected override void OnItemsSourceChanged(IEnumerable oldValue,IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue,newValue);if(_wrapping)return;
        _choices=newValue?.Cast<TemplateChoice>().ToArray()??[];
        _view=new ListCollectionView(_choices.ToList());_wrapping=true;
        try{SetCurrentValue(ItemsSourceProperty,_view);}finally{_wrapping=false;}
        Synchronize();
    }
    public override void OnApplyTemplate()
    {
        if(_editor is not null)_editor.TextChanged-=EditorChanged;base.OnApplyTemplate();
        _editor=GetTemplateChild("PART_EditableTextBox") as TextBox;
        if(_editor is not null)_editor.TextChanged+=EditorChanged;
    }
    protected override void OnPreviewTextInput(TextCompositionEventArgs e){_typing=true;base.OnPreviewTextInput(e);}
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key is Key.Back or Key.Delete||(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key is Key.V or Key.X))_typing=true;
        if(e.Key==Key.Escape){_typing=false;Restore();}
        base.OnPreviewKeyDown(e);
    }
    private void EditorChanged(object sender,TextChangedEventArgs e)
    {
        if(_filtering||_wrapping||_synchronizing||!_typing||_editor?.IsKeyboardFocusWithin!=true)return;
        string text=_editor.Text;Filter(text);if(!IsDropDownOpen)IsDropDownOpen=true;
        _synchronizing=true;try{_editor.Text=text;_editor.CaretIndex=text.Length;}finally{_synchronizing=false;}
    }
    internal void Filter(string text)
    {
        if(_view is null)return;string query=Normalize(text);_filtering=true;
        try{_view.Filter=o=>o is TemplateChoice c&&(Normalize(c.Label).Contains(query,StringComparison.Ordinal)||Normalize(c.Value).Contains(query,StringComparison.Ordinal));}
        finally{_filtering=false;}
    }
    private static string Normalize(string text)=>new string(text.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant).ToArray());
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        if(!_wrapping&&!_filtering&&!_synchronizing&&SelectedItem is TemplateChoice choice){_typing=false;SetCurrentValue(ChoiceValueProperty,choice.Value);}
        base.OnSelectionChanged(e);
    }
    protected override void OnDropDownOpened(EventArgs e){base.OnDropDownOpened(e);if(!_typing&&_editor is not null){_editor.Focus();_editor.SelectAll();}}
    protected override void OnDropDownClosed(EventArgs e){_typing=false;Restore();base.OnDropDownClosed(e);}
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e){base.OnLostKeyboardFocus(e);if(!IsKeyboardFocusWithin&&!IsDropDownOpen){_typing=false;Restore();}}
    private void Restore(){_filtering=true;try{if(_view is not null)_view.Filter=null;}finally{_filtering=false;}Synchronize();}
    private void Synchronize()
    {
        if(_wrapping||_filtering)return;_synchronizing=true;
        try{SelectedItem=_choices.FirstOrDefault(c=>c.Value==ChoiceValue);if(!_typing)Text=(SelectedItem as TemplateChoice)?.Label??"";}
        finally{_synchronizing=false;}
    }
}
