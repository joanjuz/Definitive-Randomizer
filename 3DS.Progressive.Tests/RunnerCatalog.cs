// Fixed catalogue of runner options for --help/--list. Program.cs remains the source of truth:
// when adding, renaming or removing an option there, update this table and RUNNER_PRUEBAS.md.
internal enum RunnerCategory { Synthetic, Ui, Real }

internal sealed record RunnerOption(string Name,string Arguments,RunnerCategory Category,bool InCi);

internal static class RunnerCatalog
{
    internal static readonly RunnerOption[] Options=
    [
        new("--batch-gen6-only","",RunnerCategory.Synthetic,false),
        new("--batch80-only","",RunnerCategory.Synthetic,false),
        new("--catch-batch-only","",RunnerCategory.Synthetic,false),
        new("--editor-modules-only","",RunnerCategory.Synthetic,false),
        new("--general-manual-only","",RunnerCategory.Synthetic,false),
        new("--global-apply-only","",RunnerCategory.Synthetic,true),
        new("--global-options-only","",RunnerCategory.Synthetic,true),
        new("--guidance-only","",RunnerCategory.Synthetic,true),
        new("--history-inspection-only","",RunnerCategory.Synthetic,true),
        new("--item-icons-only","",RunnerCategory.Synthetic,false),
        new("--legacy-parity-only","",RunnerCategory.Synthetic,true),
        new("--manual-edits-only","",RunnerCategory.Synthetic,false),
        new("--master-batch-only","",RunnerCategory.Synthetic,false),
        new("--native-gen6-only","",RunnerCategory.Synthetic,false),
        new("--native-wild7-only","",RunnerCategory.Synthetic,false),
        new("--nds-only","",RunnerCategory.Synthetic,true),
        new("--performance-only","",RunnerCategory.Synthetic,false),
        new("--pickup-only","",RunnerCategory.Synthetic,false),
        new("--pokefeur-only","",RunnerCategory.Synthetic,false),
        new("--rebuild-cache-only","",RunnerCategory.Synthetic,false),
        new("--rebuild-stability-only","",RunnerCategory.Synthetic,true),
        new("--remote-sprites-only","",RunnerCategory.Synthetic,false),
        new("--requested-only","",RunnerCategory.Synthetic,false),
        new("--shiny-batch-only","",RunnerCategory.Synthetic,false),
        new("--single-purchase-only","",RunnerCategory.Synthetic,false),
        new("--sm-marts-only","",RunnerCategory.Synthetic,false),
        new("--starter-text-only","",RunnerCategory.Synthetic,false),
        new("--statics7-batch-only","",RunnerCategory.Synthetic,false),
        new("--swsh-expansion-only","",RunnerCategory.Synthetic,false),
        new("--swsh-only","",RunnerCategory.Synthetic,true),
        new("--swsh-parity-only","",RunnerCategory.Synthetic,true),
        new("--synthetic-only","",RunnerCategory.Synthetic,true),
        new("--template-feedback-only","",RunnerCategory.Synthetic,false),
        new("--template-latest-only","",RunnerCategory.Synthetic,false),
        new("--template-studio-only","",RunnerCategory.Synthetic,false),
        new("--text-classification-only","",RunnerCategory.Synthetic,false),
        new("--text-commands-only","",RunnerCategory.Synthetic,false),
        new("--text-search-only","",RunnerCategory.Synthetic,false),
        new("--title-movies-only","",RunnerCategory.Synthetic,false),
        new("--titles6-only","",RunnerCategory.Synthetic,false),
        new("--tm6-compatibility-only","",RunnerCategory.Synthetic,false),
        new("--tm7-text-only","",RunnerCategory.Synthetic,false),
        new("--trades6-only","",RunnerCategory.Synthetic,false),
        new("--tutors6-only","",RunnerCategory.Synthetic,false),
        new("--all-ui-only","",RunnerCategory.Ui,false),
        new("--global-apply-ui-only","",RunnerCategory.Ui,false),
        new("--interaction-lock-only","",RunnerCategory.Ui,true),
        new("--rebuild-marts-ui-only","",RunnerCategory.Ui,true),
        new("--nds-ui-only","",RunnerCategory.Ui,true),
        new("--requested-ui-only","",RunnerCategory.Ui,true),
        new("--swsh-ui-only","",RunnerCategory.Ui,true),
        new("--template-feedback-ui-only","",RunnerCategory.Ui,true),
        new("--template-latest-ui-only","",RunnerCategory.Ui,true),
        new("--template-studio-ui-only","",RunnerCategory.Ui,true),
        new("--text-classification-ui-only","",RunnerCategory.Ui,true),
        new("--text-commands-ui-only","",RunnerCategory.Ui,true),
        new("--tm-default-ui-only","",RunnerCategory.Ui,false),
        new("--utility-ui-only","",RunnerCategory.Ui,false),
        new("--visual-redesign-only","",RunnerCategory.Ui,true),
        new("--audit-catch-templates","exactamente 2",RunnerCategory.Real,false),
        new("--check-catch-batch-real-copies","2 o más",RunnerCategory.Real,false),
        new("--check-catch-real-copies","2 o más",RunnerCategory.Real,false),
        new("--check-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-marts6-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-marts7-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-master-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-real-rebuild-roundtrip","2 o más",RunnerCategory.Real,false),
        new("--check-shiny-independent-copies","2 o más",RunnerCategory.Real,false),
        new("--check-statics7-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-title-movie-roundtrip","exactamente 2",RunnerCategory.Real,false),
        new("--check-trades6-package-copy","exactamente 3",RunnerCategory.Real,false),
        new("--check-tutors6-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--check-tutors6-independent-copies","2 o más",RunnerCategory.Real,false),
        new("--check-tutors7-full-batch-real","2 o más",RunnerCategory.Real,false),
        new("--description-preview-fonts","opcionales",RunnerCategory.Real,false),
        new("--diagnose-text-catalogue","exactamente 1",RunnerCategory.Real,false),
        new("--export-local-english-for-text-research","2 o más",RunnerCategory.Real,false),
        new("--extract-independent-image","exactamente 2",RunnerCategory.Real,false),
        new("--interrupt-save-check","exactamente 1",RunnerCategory.Real,false),
        new("--nds-gen5-probe","1 o más",RunnerCategory.Real,false),
        new("--nds-gen5-random-probe","2 o más",RunnerCategory.Real,false),
        new("--nds-roundtrip-probe","2 o más",RunnerCategory.Real,false),
        new("--nds-ui-probe","exactamente 2",RunnerCategory.Real,false),
        new("--pokefeur-probe","exactamente 3",RunnerCategory.Real,false),
        new("--read-facilities","1 o más",RunnerCategory.Real,false),
        new("--read-field6","1 o más",RunnerCategory.Real,false),
        new("--read-hm6","1 o más",RunnerCategory.Real,false),
        new("--read-machine7-recipes","1 o más",RunnerCategory.Real,false),
        new("--read-only-roms","1 o más",RunnerCategory.Real,false),
        new("--read-static-profiles","1 o más",RunnerCategory.Real,false),
        new("--read-text-classification","1 o más",RunnerCategory.Real,false),
        new("--read-title-movies","1 o más",RunnerCategory.Real,false),
        new("--read-titles6","1 o más",RunnerCategory.Real,false),
        new("--read-tm6-compatibility","1 o más",RunnerCategory.Real,false),
        new("--requested-real","1 o más",RunnerCategory.Real,false),
        new("--review73-real","1 o más",RunnerCategory.Real,false),
        new("--single-purchase-backend","exactamente 1",RunnerCategory.Real,false),
        new("--single-purchase-copies","exactamente 2",RunnerCategory.Real,false),
        new("--sword-surprise-audit","exactamente 1",RunnerCategory.Real,false),
        new("--sword-surprise-probe","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-divine-probe","exactamente 1",RunnerCategory.Real,false),
        new("--swsh-expansion-probe","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-gameplay-probe","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-global-probe","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-parity-probe","exactamente 1",RunnerCategory.Real,false),
        new("--swsh-polish-probe","exactamente 1",RunnerCategory.Real,false),
        new("--swsh-probe-mod","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-random-probe","exactamente 3",RunnerCategory.Real,false),
        new("--swsh-read-only","exactamente 1",RunnerCategory.Real,false),
        new("--swsh-table-probe","exactamente 1",RunnerCategory.Real,false),
        new("--swsh-version-probe","exactamente 2",RunnerCategory.Real,false),
        new("--swsh-visual-preview","exactamente 2",RunnerCategory.Real,false),
        new("--template-feedback-copies","1 o más",RunnerCategory.Real,false),
        new("--template-studio-shops-copies","1 o más",RunnerCategory.Real,false),
        new("--verify-existing-rebuild","exactamente 3",RunnerCategory.Real,false),
    ];

    internal static bool IsHelp(string[] args)=>args.Length==1&&args[0] is "--help" or "--list" or "-h";

    internal static void Print()
    {
        Console.WriteLine("Uso: dotnet 3DS.Progressive.Tests.dll [opción [argumentos...]]");
        Console.WriteLine("Sin opciones: regresión sintética completa (XY, ORAS, SM y USUM) y, en Windows, comprobaciones WPF fuera de pantalla.");
        Console.WriteLine("--global-apply-ui-only es un alias de --global-apply-only. [CI] = incluida en el workflow de CI.");
        foreach(var (category,title) in new[]{(RunnerCategory.Synthetic,"Suites sintéticas y de componentes (sin argumentos)"),(RunnerCategory.Ui,"Suites de interfaz WPF fuera de pantalla (sin argumentos)"),(RunnerCategory.Real,"Datos reales, copias y probes (requieren rutas; usar siempre copias aisladas)")})
        {
            Console.WriteLine();Console.WriteLine(title+":");
            foreach(var option in Options.Where(o=>o.Category==category))
                Console.WriteLine($"  {option.Name,-46} {(option.Arguments.Length==0?"":"args: "+option.Arguments),-22} {(option.InCi?"[CI]":"")}".TrimEnd());
        }
    }

    internal static void RejectUnknown(string[] args)
    {
        Console.Error.WriteLine("Opción no reconocida o número de argumentos incorrecto: "+string.Join(" ",args));
        Console.Error.WriteLine("Usa --help para ver las opciones disponibles y sus argumentos.");
        Environment.ExitCode=2;
    }
}
