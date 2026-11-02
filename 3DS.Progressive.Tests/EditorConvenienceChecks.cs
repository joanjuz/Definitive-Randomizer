using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

/// <summary>Type-to-search in long drop-downs and copy/paste of a trainer's Pokémon (off-screen; the search list is driven without opening its window).</summary>
internal static class EditorConvenienceChecks
{
    internal static void RunUi(Action<bool,string> check)
    {
        var parameters=new HwndSourceParameters("editor convenience offscreen checks"){WindowStyle=unchecked((int)0x80000000),Width=1100,Height=800,PositionX=-32000,PositionY=-32000};
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);

        // Search: accents and case do not matter, the number matches too, and only choosing a result changes the selection.
        var choices=Enumerable.Range(0,40).Select(i=>new MoveChoice(i,i switch{7=>"Poké Ball",21=>"Éxtasis",33=>"Cristal Normal Z",_=>"Objeto "+i})).ToArray();
        var combo=new System.Windows.Controls.ComboBox{ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=3,Width=240};
        int changes=0;combo.SelectionChanged+=(_,_)=>changes++;
        using(var source=new HwndSource(parameters){RootVisual=new Border{Child=combo}})
        {
            Pump();
            check(ComboSearch.IsSearchable(combo)&&!ComboSearch.IsSearchable(new System.Windows.Controls.ComboBox{ItemsSource=new[]{"A","B","C"}}),"ComboSearch_OnlyLongListsUseTheSearch");
            var list=ComboSearch.Build(combo,"");
            check(list.List.Items.Count==40&&Equals(list.List.SelectedItem?.GetType().GetProperty("Item")?.GetValue(list.List.SelectedItem),choices[3]),"ComboSearch_EmptyQueryShowsEverythingWithTheCurrentValueSelected");
            list.Search.Text="extasis";
            check(list.List.Items.Count==1&&changes==0&&(int)combo.SelectedValue==3,"ComboSearch_TypingFiltersWithoutAccentsAndDoesNotChangeTheValue");
            list.Search.Text="cristal z";
            check(list.List.Items.Count==1,"ComboSearch_EveryWordMustMatch");
            list.Search.Text="033";
            check(list.List.Items.Count==1,"ComboSearch_NumberMatches");
            list.Search.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            check((int)combo.SelectedValue==33&&changes==1&&!list.Popup.IsOpen,"ComboSearch_EnterChoosesTheHighlightedResult");
            // Opened for real (drawn invisible): taking the keyboard and pumping input must not close the list.
            var opened=ComboSearch.Open(combo,"poke");opened.Popup.Child.Opacity=0;
            Pump();opened.Search.Focus();Keyboard.Focus(opened.Search);Pump();Pump();
            bool stayed=opened.Popup.IsOpen&&opened.Popup.StaysOpen&&opened.List.Items.Count==1;
            opened.Search.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();
            check(stayed&&!opened.Popup.IsOpen&&(int)combo.SelectedValue==7,"ComboSearch_OpenListStaysOpenWhileTypingAndClosesOnChoice");
            combo.SelectedValue=33;changes=1;
            // In an active window (off-screen): typing in the list does not deactivate the window, so it stays open;
            // a click elsewhere in the window closes it without choosing.
            {
                var other=new System.Windows.Controls.ComboBox{ItemsSource=choices,DisplayMemberPath="Display",SelectedValuePath="Id",SelectedValue=3};
                var outside=new Border{Height=40,Background=System.Windows.Media.Brushes.Transparent};
                var panel=new StackPanel();panel.Children.Add(other);panel.Children.Add(outside);
                var window=new Window{Content=panel,Width=400,Height=300,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
                window.Show();window.Activate();Pump();
                var inWindow=ComboSearch.Open(other,"");inWindow.Popup.Child.Opacity=0;
                Pump();inWindow.Search.Focus();Keyboard.Focus(inWindow.Search);Pump();Pump();
                bool open=inWindow.Popup.IsOpen;
                outside.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseDownEvent});Pump();
                check(open&&!inWindow.Popup.IsOpen&&(int)other.SelectedValue==3,"ComboSearch_ListStaysOpenInAnActiveWindowAndAClickOutsideClosesIt");
                window.Close();
            }
            var none=ComboSearch.Build(combo,"zzzz");
            none.Search.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            check(none.List.Items.Count==0&&(int)combo.SelectedValue==33&&changes==1,"ComboSearch_NoResultKeepsTheValue");
        }

        // Copy a trainer's Pokémon and paste it on another trainer, in a slot outside the team.
        foreach(var version in new[]{GameVersion.ORAS,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            var service=new GameTrainerService(f.Session);
            typeof(RecordEditorView).GetField("_copiedPokemon",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.SetValue(null,null);
            var view=new RecordEditorView(service);
            using var source=new HwndSource(parameters){RootVisual=view};Pump();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var list=(System.Windows.Controls.ListBox)view.FindName("RecordList");
            void Select(int index){list.SelectedIndex=index;Pump();}
            Dictionary<string,FrameworkElement> Fields()=>(Dictionary<string,FrameworkElement>)typeof(RecordEditorView).GetField("_fields",flags)!.GetValue(view)!;
            Select(0);
            var buttons=Descendants<System.Windows.Controls.Button>(view).Where(b=>b.Content is "Copiar Pokémon" or "Pegar Pokémon").ToArray();
            check(buttons.Count(b=>b.Content is "Copiar Pokémon")==6&&buttons.Where(b=>b.Content is "Pegar Pokémon").All(b=>!b.IsEnabled),version+" TrainerPokemon_EverySlotHasCopyAndPasteDisabledUntilCopied");
            var fields=Fields();
            ((System.Windows.Controls.ComboBox)fields["P0.Species"]).SelectedValue=25;((System.Windows.Controls.TextBox)fields["P0.Level"]).Text="42";
            ((System.Windows.Controls.ComboBox)fields["P0.Move0"]).SelectedValue=85;((System.Windows.Controls.ComboBox)fields["P0.Item"]).SelectedValue=4;
            view.CopyTrainerPokemon(fields,"P0.","Pokémon 1");
            // Copying keeps the source unsaved changes; discard them by moving on without saving.
            typeof(RecordEditorView).GetField("_dirty",flags)!.SetValue(view,false);
            Select(1);fields=Fields();
            int count=int.Parse(((System.Windows.Controls.TextBox)fields["Count"]).Text);int slot=Math.Min(count,5);
            check(Descendants<System.Windows.Controls.Button>(view).Where(b=>b.Content is "Pegar Pokémon").All(b=>b.IsEnabled),version+" TrainerPokemon_PasteEnabledOnOtherTrainers");
            view.PasteTrainerPokemon(fields,$"P{slot}.",slot,$"Pokémon {slot+1}");
            check((int)((System.Windows.Controls.ComboBox)fields[$"P{slot}.Species"]).SelectedValue==25&&((System.Windows.Controls.TextBox)fields[$"P{slot}.Level"]).Text=="42"
                &&(int)((System.Windows.Controls.ComboBox)fields[$"P{slot}.Move0"]).SelectedValue==85&&(int)((System.Windows.Controls.ComboBox)fields[$"P{slot}.Item"]).SelectedValue==4
                &&int.Parse(((System.Windows.Controls.TextBox)fields["Count"]).Text)>=slot+1&&((System.Windows.Controls.Button)view.FindName("SaveButton")).IsEnabled,version+" TrainerPokemon_PasteCopiesEveryFieldAndJoinsTheTeam");
            ((System.Windows.Controls.Button)view.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Pump();
            var saved=EditorValues.Flatten(new GameTrainerService(f.Session).Read(service.Entries[1].Id));
            check(saved[$"P{slot}.Species"]==25&&saved[$"P{slot}.Level"]==42&&saved[$"P{slot}.Move0"]==85&&saved["Count"]>=slot+1,version+" TrainerPokemon_PastedPokemonIsSaved");
        }
    }

    // Logical tree: the cards of slots outside the team are collapsed, so their buttons are not in the visual tree.
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if(child is T found)yield return found;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
