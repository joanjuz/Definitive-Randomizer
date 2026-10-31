using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using pk3DS.Core;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;

internal static class FeatureVisibilityChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var nested in Tree(child))yield return nested;
    }
    internal static void Run(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            var trainer=new GameTrainerService(f.Session);var keys=trainer.RandomOptions.SelectMany(group=>group.Fields).Select(field=>field.Key).ToArray();
            check(keys.Any(key=>key.EndsWith(".ZMove",StringComparison.Ordinal))==(f.Game.Generation==7),version+" trainer individual Z-Move control only appears in Gen7");
            var ruleView=new TrainerRulesWindow([new ThreeDS.Progressive.Models.TrainerRuleTableRow{SupportsZMoves=f.Game.Generation==7}],true);
            var grid=(DataGrid)typeof(TrainerRulesWindow).GetField("_grid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ruleView)!;
            check(grid.Columns.Any(column=>column.Header?.ToString()=="Z-Move")== (f.Game.Generation==7),version+" trainer cap table Z-Move column only appears in Gen7");
            var moves=new GameMoveService(f.Session);
            check(moves.AllMoveNames.Count<=f.Game.Moves.Length&&(!f.Game.Generation.Equals(6)||!moves.AllMoveNames.Any(row=>row.Name.Contains("(Físico)")||row.Name.Contains("(Especial)"))),version+" move catalog excludes unavailable Gen7 Z variants from Gen6");
            var moveView=new MoveEditorView(f.Session);var toolsField=typeof(MoveEditorView).GetField("_tools",BindingFlags.Instance|BindingFlags.NonPublic)!;var moveTools=(BatchToolsView)toolsField.GetValue(moveView)!;
            check(((Button)moveView.FindName("BalanceMovesButton")).Visibility==(f.Game.SM?Visibility.Collapsed:Visibility.Visible),version+" Progressive balance is visible only in compatible games");
            if(f.Game.SM){bool rejected=false;try{moveTools.OpenAction(ThreeDS.Progressive.Models.BatchAction.MoveBalance);}catch(ArgumentException){rejected=true;}check(rejected,"SM direct balance navigation rejected");}
            else moveTools.OpenAction(ThreeDS.Progressive.Models.BatchAction.MoveBalance);
            var mechanics=new SystemsView(f.Session,"Mechanics",_=>{},()=>{},()=>{});
            var buttons=Tree(mechanics).OfType<Button>().Select(b=>b.Content?.ToString()).ToArray();
            check(buttons.Contains("Abrir Catch Zones")== (f.Game.Generation==6)&&buttons.Contains("Abrir O-Powers")== (f.Game.Generation==6)&&buttons.Contains("Abrir intercambios Gen6")== (f.Game.Generation==6),version+" mechanics only expose Gen6 actions in Gen6");
            check(buttons.Contains("Los Pokémon intercambiados siempre obedecen")==(f.Game.Generation==7)&&buttons.Contains("Quitar formas Mega obtenibles")==(f.Game.Generation==7),version+" Gen7 obedience and Mega adjustments only appear in Gen7");
            check(Tree(new GameTextView(f.Session,story:true)).OfType<Button>().Any(b=>b.Content?.ToString()=="Pista de GAME FREAK en el Ultraviaje")==f.Game.USUM,version+" Ultra Warp Ride hint only appears in USUM story text");
            check(buttons.Contains("Abrir Player Level Caps")==f.Game.USUM&&buttons.Contains("Abrir USUM Relearner")==f.Game.USUM&&buttons.Contains("Abrir CRO Recipes")==f.Game.USUM,version+" USUM mechanics are absent from incompatible games");
            var tools=new SystemsView(f.Session,"PokemonTools",_=>{},()=>{},()=>{});
            check(Tree(tools).OfType<Button>().Any(b=>b.Content?.ToString()=="Abrir OWSE [Dev]")== (f.Game.Generation==7),version+" Gen7 map explorer is hidden in Gen6");
            var settings=new SystemsView(f.Session,"Configuration",_=>{},()=>{},()=>{});
            check(Tree(settings).OfType<ComboBox>().Single().Items.Count==(f.Game.Generation==6?8:10),version+" language selector only includes supported languages");
            var main=new MainWindow();
            try
            {
                main.LoadProject(f.Session);
                check(((Button)main.FindName("MartsButton")).Visibility==(f.Game.Generation is 6 or 7?Visibility.Visible:Visibility.Collapsed),version+" sidebar hides unsupported store editor");
                check(((Button)main.FindName("MachineTutorButton")).Content.ToString()!.Contains(f.Game.Generation==6?"MO":"tutores"),version+" sidebar describes the active machine editor");
                var host=(ContentControl)main.FindName("SectionHost");var before=host.Content;
                var navigate=typeof(MainWindow).GetMethod("NavigateSystem",BindingFlags.Instance|BindingFlags.NonPublic)!;
                foreach(string section in new[]{"PlayerCaps","UsumRelearner","CroRecipes","Trade6","CatchZones","OPower","OWSE","Marts","TitleScreen6","Tutor6","GarcShuffle"}.Where(s=>!ProjectFeatureAvailability.Supports(f.Session,s)))
                {
                    navigate.Invoke(main,[section]);
                    check(ReferenceEquals(host.Content,before),version+" direct navigation rejects unsupported section "+section);
                    typeof(MainWindow).GetMethod("Section_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[new Button{Tag=section},new RoutedEventArgs()]);
                    check(ReferenceEquals(host.Content,before),version+" routed click rejects unsupported section "+section);
                }
            }
            finally{main.Close();}
        }
    }
}
