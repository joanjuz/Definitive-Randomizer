using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
namespace ThreeDS.Progressive;
internal sealed partial class SwordShieldPrototypeWindow
{
    private void ShowModifyPersonal()
    {
        if(_project is null||_busy||!CanChangeSection())return;var project=_project;
        var fields=new ComboBox{ItemsSource=_field.ItemsSource,SelectedIndex=Math.Max(0,_field.SelectedIndex)};var value=new TextBox();var scope=new ComboBox{ItemsSource=new[]{"Todas las entradas presentes","Solo la entrada seleccionada"},SelectedIndex=1};var range=new TextBlock();var details=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=140,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var body=new Grid{Margin=new Thickness(20)};body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});var panel=new StackPanel();body.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var dialog=new Window{Title="Modificar Pokémon",Owner=DialogOwner,Width=720,Height=650,MinWidth=430,MinHeight=340,MaxWidth=SystemParameters.WorkArea.Width,MaxHeight=SystemParameters.WorkArea.Height,Content=body,WindowStartupLocation=WindowStartupLocation.CenterOwner};dialog.SetResourceReference(BackgroundProperty,"Bg");dialog.SetResourceReference(ForegroundProperty,"TextMain");var labels=new Style(typeof(System.Windows.Controls.Label));labels.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,new DynamicResourceExtension("TextSoft")));dialog.Resources.Add(typeof(System.Windows.Controls.Label),labels);
        panel.Children.Add(new TextBlock{Text="Modificar Pokémon",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});AddLabel(panel,"Campo",fields);panel.Children.Add(fields);AddLabel(panel,"Nuevo valor",value);panel.Children.Add(value);panel.Children.Add(range);AddLabel(panel,"Alcance",scope);panel.Children.Add(scope);panel.Children.Add(details);
        var actions=new WrapPanel{Margin=new Thickness(0,14,0,0),HorizontalAlignment=System.Windows.HorizontalAlignment.Right};Grid.SetRow(actions,1);body.Children.Add(actions);var preview=new Button{Content="Previsualizar cambios",Margin=new Thickness(0,0,8,0)};var apply=new Button{Content="Aplicar cambios",IsEnabled=false,Margin=new Thickness(0,0,8,0)};var close=new Button{Content="Cancelar",IsCancel=true};EditorPresentation.StyleAction(apply,EditorPresentation.ActionRole.Primary);foreach(var b in new[]{preview,apply,close})actions.Children.Add(b);
        SwordShieldEditPreview? pending=null;bool working=false;void Invalidate(){pending=null;apply.IsEnabled=false;}
        void ReadValue(){Invalidate();if(_entries.SelectedItem is SwordShieldPersonalEntry entry)value.Text=project.Read(entry.Index).Values[fields.SelectedIndex].ToString(CultureInfo.InvariantCulture);var bounds=SwordShieldPersonalTable.Range((SwordShieldField)fields.SelectedIndex);range.Text=$"Rango: {bounds.Min}–{bounds.Max}";}
        fields.SelectionChanged+=(_,_)=>ReadValue();value.TextChanged+=(_,_)=>Invalidate();scope.SelectionChanged+=(_,_)=>Invalidate();ReadValue();
        preview.Click+=async(_,_)=>
        {
            if(working)return;try
            {
                if(!int.TryParse(value.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int number))throw new ArgumentException("Introduce un valor entero.");
                int[] ids=scope.SelectedIndex==0?project.Entries.Where(e=>e.Species>0).Select(e=>e.Index).ToArray():_entries.SelectedItem is SwordShieldPersonalEntry selected?new[]{selected.Index}:Array.Empty<int>();var field=(SwordShieldField)fields.SelectedIndex;
                working=_busy=true;fields.IsEnabled=value.IsEnabled=scope.IsEnabled=preview.IsEnabled=close.IsEnabled=false;RefreshButtons();pending=await Task.Run(()=>project.PreviewPersonalBulk(ids,field,number));details.Text=$"{pending.Changes.Count} cambios pendientes.\n"+string.Join("\n",pending.Changes.Select(c=>$"#{c.Entry:000} · {FieldLabel(c.Field)}: {c.Before} → {c.After}"));apply.IsEnabled=pending.Changes.Count>0;
            }
            catch(Exception e){Invalidate();StudioDialog.Error(dialog,"Vista previa",e.Message);}
            finally{working=_busy=false;fields.IsEnabled=value.IsEnabled=scope.IsEnabled=preview.IsEnabled=close.IsEnabled=true;RefreshButtons();}
        };
        apply.Click+=(_,_)=>{if(working||pending is null)return;try{project.Apply(pending);RefreshEntries();RefreshValue();dialog.Close();}catch(Exception e){Invalidate();StudioDialog.Error(dialog,"No se pudieron aplicar los cambios",e.Message);}};close.Click+=(_,_)=>dialog.Close();dialog.Closing+=(_,e)=>{if(working)e.Cancel=true;};dialog.ShowDialog();
    }
}
