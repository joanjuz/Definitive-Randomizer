using pk3DS.Core;
using ThreeDS.Progressive.Services;
internal static class TextCommandChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var version in new[]{GameVersion.XY,GameVersion.ORAS,GameVersion.SM,GameVersion.USUM})
        {
            using var f=new Fixture(version);
            string[] original=[@"Hola, [VAR TRNAME(0000)]!\nTu Pokémon: [VAR PKNAME(0001)].\c", "[VAR GENDBR(00FF,0101)]", "[VAR BD00(0006,0000,0000)]", "[VAR 0114(0006)]"];
            var rows=TextCommandCatalogue.Build(f.Game,original);
            check(f.Game.Variables.All(v=>rows.Any(r=>r.Name==v.Name)),version+" catalogue includes every variable registered by the active codec");
            check(rows.Select(r=>r.Name).Distinct().Count()==rows.Count,version+" command names are unique");
            check(rows.Single(r=>r.Name=="TRNAME").Observed&&rows.Single(r=>r.Name=="TRNAME").Example=="[VAR TRNAME(0000)]",version+" exact observed variable retained");
            check(rows.Single(r=>r.Name=="GENDBR").Example=="[VAR GENDBR(00FF,0101)]",version+" grammar parameter sequence retained");
            check(rows.Single(r=>r.Name=="BD00").Function.Contains("no está documentada")&&rows.Single(r=>r.Name=="0114").Observed,version+" undocumented numeric controls not assigned invented functions");
            check(!TextCommandCatalogue.Build(f.Game,[]).Single(r=>r.Name=="GENDBR").CanCopy,version+" complex grammar has no invented parameter template");
            var literals=TextCommandCatalogue.Build(f.Game,[@"\[VAR TRNAME(0000)] \\n"]);
            check(!literals.Single(r=>r.Name=="TRNAME").Observed&&!literals.Single(r=>r.Name==@"\n").Observed,version+" escaped literals never detected as active variable or line break");
            check(literals.Single(r=>r.Name==@"\\").Observed,version+" escaped backslash correctly detected");
            check(TextCommandCatalogue.Build(f.Game,[@"\\[VAR TRNAME(0000)]"]).Single(r=>r.Name=="TRNAME").Observed,version+" control following an escaped backslash remains active");
            foreach(var row in rows.Where(r=>r.CanCopy))
            {
                var bytes=TextFile.GetBytes(f.Game,[row.Example]);
                var decoded=new TextFile(f.Game,bytes).Lines;
                check(decoded.Length==1&&decoded[0]==row.Example,version+" reversible copy syntax "+row.Name);
            }
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();
        using var f=new Fixture(GameVersion.USUM);
        var rows=TextCommandCatalogue.Build(f.Game,["[VAR TRNAME(0000)]"]);
        var view=new ThreeDS.Progressive.TextCommandsWindow(rows,"USUM");
        check(view.Content is System.Windows.Controls.DockPanel&&view.WindowStyle==System.Windows.WindowStyle.None,"Command reference uses application theme and custom header without opening a window");
        view.Close();
    }
}
