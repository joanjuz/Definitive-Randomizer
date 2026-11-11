using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
using ComboBox=System.Windows.Controls.ComboBox;
using Button=System.Windows.Controls.Button;

/// <summary>Real WPF controls and transactions on private fixtures; no commercial bytes in the suite.</summary>
internal static class MartSelectorChecks
{
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
    private static T? Child<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T found)return found;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(Child<T>(VisualTreeHelper.GetChild(parent,i)) is {} child)return child;
        return null;
    }
    internal static void Run(Action<bool,string> check,string? privateUsumShop=null)
    {
        // The synthetic SM layout exercises the identical selector/save path. Optional
        // USUM input is read once into our disposable fixture, never edited in place.
        using var f=new Fixture(privateUsumShop is null?GameVersion.SM:GameVersion.USUM);
        string[] mintNames=["Hura\u00f1a","Firme","P\u00edcara","Audaz","Osada","Agitada","Floja","Pl\u00e1cida","Modesta","Afable","Alocada","Mansa","Serena","Amable","Cauta","Grosera","Miedosa","Activa","Alegre","Ingenua","Seria"];
        var names=f.Game.GetText(TextName.ItemNames);names[48]="Carburante";
        for(int i=0;i<mintNames.Length;i++)names[UsumNatureMintCatalog.Mints[i].ItemId]="Menta "+mintNames[i];
        f.Game.GameTextStrings[f.Game.GameText.First(t=>t.Name==TextName.ItemNames).Index]=names;
        if(privateUsumShop is not null)
        {
            byte[][] records=f.Game.GameTextStrings.Select(lines=>TextFile.GetBytes(f.Game,lines)).ToArray();
            for(int language=0;language<10;language++)
            {
                var reference=f.Game.GetGARCReference("gametext").GetRelativeGARC(language);
                f.WriteGarc(Path.Combine(f.Game.RomFS,reference.Reference),records,reference);
            }
        }
        var project=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
        string path=Path.Combine(f.Game.RomFS,"Shop.cro");
        File.WriteAllBytes(path,privateUsumShop is null?MartSmChecks.SyntheticShop():File.ReadAllBytes(privateUsumShop));
        if(privateUsumShop is not null)
        {
            string crrPath=Path.Combine(f.Game.RomFS,".crr/static.crr");Directory.CreateDirectory(Path.GetDirectoryName(crrPath)!);
            byte[] crr=new byte[0x400];BitConverter.GetBytes(0x380).CopyTo(crr,0x350);BitConverter.GetBytes(1).CopyTo(crr,0x354);
            File.WriteAllBytes(crrPath,CroIntegrity.RebuildCrr(crr,[File.ReadAllBytes(path)]));
        }
        var initial=new GameMartService(project);
        initial.RegularShops[0].Rows[0].ItemId=48;initial.BpShops[0].Rows[0].ItemId=48;
        File.WriteAllBytes(path,initial.Prepare().Output);
        byte[] before=File.ReadAllBytes(path);
        int bpPrice=initial.BpShops[0].Rows[0].Price;
        var view=new MartEditorView(project);
        var window=new Window{Content=view,Width=1100,Height=850,Left=-32000,Top=-32000,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        window.Show();window.Activate();Pump();
        try
        {
            foreach(bool bp in new[]{false,true})
            {
                string kind=bp?"BP":"Regular";
                Field<TabControl>(view,"_tabs").SelectedIndex=bp?1:0;Pump();
                var grid=Field<DataGrid>(view,bp?"_bpGrid":"_regularGrid");
                var picker=Field<System.Windows.Controls.Primitives.Selector>(view,bp?"_bpPicker":"_regularPicker");
                var row=((MartShop)picker.SelectedItem).Rows[0];
                grid.ScrollIntoView(row);grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[1]);grid.BeginEdit();Pump();
                var combo=Child<ComboBox>(grid.Columns[1].GetCellContent(row))!;
                check((int)combo.SelectedValue==48,kind+" selector starts with Carburante");
                foreach(var mint in UsumNatureMintCatalog.Mints)
                {
                    var filtered=ComboSearch.Build(combo,names[mint.ItemId]);
                    filtered.Search.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(combo)!,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                    check(filtered.List.Items.Count==1&&row.ItemId==mint.ItemId,kind+" searches and selects mint "+mint.ItemId);
                }
                combo.SelectedValue=48;Pump();
                var search=ComboSearch.Open(combo,"Menta Firme");search.Popup.Child.Opacity=0;Pump();
                search.Search.Focus();Keyboard.Focus(search.Search);Pump();
                check(search.Popup.IsOpen&&search.List.Items.Count==1,kind+" popup retains search focus with one result");
                if(bp)
                {
                    search.List.UpdateLayout();
                    var result=(ListBoxItem)search.List.ItemContainerGenerator.ContainerFromIndex(0);
                    result.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});
                }
                else search.Search.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(search.Search)!,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                Pump();
                check(row.ItemId==114&&(int)combo.SelectedValue==114&&!search.Popup.IsOpen,kind+" choice updates model and visible selection immediately ("+(bp?"one click":"Enter")+")");
                picker.Focus();grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);Pump();
                check(row.ItemId==114,kind+" leaving and committing cell retains Menta Firme");
                picker.SelectedIndex=1;picker.SelectedIndex=0;Pump();
                check(((MartShop)picker.SelectedItem).Rows[0].ItemId==114,kind+" changing shop retains the selection");
            }
            check(File.ReadAllBytes(path).SequenceEqual(before)&&project.History.Read().Entries.Count==0,"Selector edits do not write before Apply");
            typeof(MartEditorView).GetMethod("Preview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null);
            var preview=Field<MartShopPreview>(view,"_pending");
            check(preview.Changes.Count(c=>c.Before.Contains("Carburante")&&c.After.Contains("Menta Firme"))==2,"Preview detects both Carburante to Menta Firme changes");
            Field<Button>(view,"_apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            check(File.ReadAllBytes(path).SequenceEqual(preview.Output)&&project.History.Read().Entries.Count==1,"Apply writes exact preview with one history transaction");
            var reopened=new MartEditorView(project);
            check(((MartShop)Field<System.Windows.Controls.Primitives.Selector>(reopened,"_regularPicker").SelectedItem).Rows[0].ItemId==114&&((MartShop)Field<System.Windows.Controls.Primitives.Selector>(reopened,"_bpPicker").SelectedItem).Rows[0] is {ItemId:114} saved&&saved.Price==bpPrice,"Reopening editor preserves both mints and BP price");
            var reopenedProject=Fixture.CreateSession(f.Game,f.Root,f.PersonalPath);
            var savedService=new GameMartService(reopenedProject);
            check(savedService.RegularShops[0].Rows[0].ItemId==114&&savedService.BpShops[0].Rows[0].ItemId==114,"Fresh project session reloads saved selections from disk");
        }
        finally{window.Close();}
    }
}
