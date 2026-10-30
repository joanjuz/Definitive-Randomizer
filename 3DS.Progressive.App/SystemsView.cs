using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;
using Panel=System.Windows.Controls.Panel;
using Clipboard=System.Windows.Clipboard;

namespace ThreeDS.Progressive;

/// <summary>Real system operations, not disabled navigation tiles.</summary>
internal sealed class SystemsView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly Action<string> _navigate;
    private readonly Action _backup;
    private readonly Action _personalBatch;
    private readonly TextBlock _status;
    internal StudioDialog CreateGarcDialog()=>StudioDialog.Create(Window.GetWindow(this),"About GARCs",
        $"{_project.GameConfiguration.Files.Count()} GARCs · {_project.GameName}",
        string.Join(Environment.NewLine,_project.GameConfiguration.Files.Select(file=>file.Name+" → "+_project.GameConfiguration.GetGARCFileName(file.Name))),"i","Cerrar",false);
    internal SystemsView(GameProjectSession project,string section,
        Action<string> navigate,Action backup,Action personalBatch)
    {
        _project=project;_navigate=navigate;_backup=backup;_personalBatch=personalBatch;
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel {Margin=new Thickness(22)};
        Content=root;
        var heading=new TextBlock
        {
            Text=section switch
            {
                "Mechanics"=>"Mecánicas y CRO",
                "PokemonTools"=>"Herramientas Pokémon",
                "RomTools"=>"Herramientas ROM",
                "Automation"=>"Automatización",
                _=>"Configuración"
            },
            FontSize=25,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain"),
            Margin=new Thickness(0,0,0,14)
        };
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        _status=new TextBlock {Foreground=(Brush)FindResource("TextSoft"),
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(scroll);
        var stack=new StackPanel();
        scroll.Content=stack;
        switch(section)
        {
            case "Mechanics":BuildMechanics(stack);break;
            case "PokemonTools":BuildPokemonTools(stack);break;
            case "RomTools":BuildRomTools(stack);break;
            case "Automation":BuildAutomation(stack);break;
            default:BuildSettings(stack);break;
        }
    }
    private Border Card(string name,string description)
    {
        var panel=new StackPanel{Margin=new Thickness(0,0,0,10)};
        panel.Children.Add(new TextBlock
        {
            Text=name,FontSize=17,FontWeight=FontWeights.SemiBold,
            Foreground=(Brush)FindResource("TextMain"),Margin=new Thickness(0,0,0,7)
        });
        panel.Children.Add(new TextBlock
        {
            Text=description,TextWrapping=TextWrapping.Wrap,
            Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,0,0,11)
        });
        return new Border {BorderBrush=(Brush)FindResource("Stroke"),
            Background=(Brush)FindResource("Panel"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(15),Margin=new Thickness(0,0,0,12),
            Child=panel};
    }
    private static Button AddButton(Panel panel,string text,Action run)
    {
        var b=new Button {Content=text,Margin=new Thickness(0,0,9,7)};
        b.Click+=(_,_)=>{try{run();}catch(Exception ex)
        {StudioDialog.Error(Window.GetWindow(b),"Herramientas del sistema",ex.Message);}};
        panel.Children.Add(b);return b;
    }
    internal void OpenRebuildForChecks()=>_navigate("Rebuild");
    internal void OpenPlayerCapsForChecks()=>_navigate("PlayerCaps");
    internal void OpenCroRecipesForChecks()=>_navigate("CroRecipes");
    internal void OpenRelearnerForChecks()=>_navigate("UsumRelearner");
    private void BuildMechanics(StackPanel stack)
    {
        if(_project.GameConfiguration.ORAS)
        {
            var tutors=Card("Tutores BP · ORAS","Edita y randomiza los 60 movimientos de tutor y sus precios. Sincroniza las listas de compatibilidad y la tabla de tienda.");
            AddButton((StackPanel)tutors.Child,"Abrir tutores ORAS",()=>_navigate("Tutor6"));stack.Children.Add(tutors);
        }

        var cap=Card("Player Level Caps · USUM",
            "21 checkpoints de historia con flags y Event Work. Presets, edición de condiciones, Sync Trainer Caps y parche conjunto Battle.cro + code.bin con preview e historial.");
        var box=(StackPanel)cap.Child;
        var open=AddButton(box,"Abrir Player Level Caps",()=>_navigate("PlayerCaps"));
        open.IsEnabled=_project.GameConfiguration.USUM;
        if(!_project.GameConfiguration.USUM)open.ToolTip="Solo validado para Ultra Sun y Ultra Moon.";
        if(ProjectFeatureAvailability.Supports(_project,"PlayerCaps"))stack.Children.Add(cap);
        var patches=Card("USUM Relearner · Cafeterías y aprendizaje",
            "Parche original CAFE.asm + Learn.asm en Ultra Sun / Ultra Moon. Copia aislada, vista previa de code.bin y StoryText, verificación de firmas e historial conjunto.");
        AddButton((StackPanel)patches.Child,"Abrir USUM Relearner",()=>
            _navigate("UsumRelearner")).IsEnabled=_project.GameConfiguration.USUM;
        if(ProjectFeatureAvailability.Supports(_project,"UsumRelearner"))stack.Children.Add(patches);
        var recipes=Card("CRO Recipes · Mecánicas personalizadas",
            "Importar múltiples recetas JSON; resolver dependencias y relocaciones con el motor original, previsualizar Battle.cro y aplicar una sola transacción con historial.");
        AddButton((StackPanel)recipes.Child,"Abrir CRO Recipes",()=>
            _navigate("CroRecipes")).IsEnabled=_project.GameConfiguration.USUM;
        if(ProjectFeatureAvailability.Supports(_project,"CroRecipes"))stack.Children.Add(recipes);
        var staticCard=Card("Parcheos de encuentros e intercambios",
            "Gift BST Baseline, Totem BST y Level Caps por ID, Trades Accept Any Pokémon y limpieza de diálogos españoles en encuentros estáticos.");
        AddButton((StackPanel)staticCard.Child,"Abrir Encuentros estáticos",()=>_navigate("StaticEncounters")).IsEnabled=
            _project.Generation==7;
        if(_project.Generation==7)stack.Children.Add(staticCard);
        var trades6=Card("Intercambios Progressive · Gen6","Aceptar cualquier Pokémon y editar o randomizar las ofertas en XY/ORAS. En ORAS se habilita también el selector PC; todos los cambios se verifican y guardan juntos.");
        AddButton((StackPanel)trades6.Child,"Abrir intercambios Gen6",()=>_navigate("Trade6"));if(ProjectFeatureAvailability.Supports(_project,"Trade6"))stack.Children.Add(trades6);
        var catchZones=Card("Enable Catch Zones · Gen6","Parches Progressive para encuentros, mapas y archivos adicionales XY/ORAS. Vista previa verificada e historial conjunto; se rechazan archivos previos incompatibles.");
        AddButton((StackPanel)catchZones.Child,"Abrir Catch Zones",()=>_navigate("CatchZones"));if(ProjectFeatureAvailability.Supports(_project,"CatchZones"))stack.Children.Add(catchZones);
        var opower=Card("O-Powers · Gen6","Costes, progresión, eficacia, duración y usabilidad de XY/ORAS. Se conservan las referencias de texto y los campos desconocidos. Otorgar S/MAX sigue requiriendo investigación.");
        AddButton((StackPanel)opower.Child,"Abrir O-Powers",()=>_navigate("OPower"));if(ProjectFeatureAvailability.Supports(_project,"OPower"))stack.Children.Add(opower);
        var item=Card("Objetos y economía",
            "Fix Economy aplica precios UPR-ZX con vista previa, filtros y guardado verificado. Los parches adicionales de CRO se incorporarán separadamente cuando existan firmas comprobadas.");
        AddButton((StackPanel)item.Child,"Abrir editor de objetos",()=>_navigate("Items"));
        if(_project.GameConfiguration.USUM)AddButton((StackPanel)item.Child,"Persistent Items · restaurar consumibles tras el combate",()=>_navigate("PersistentItems"));
        stack.Children.Add(item);
    }
    private void BuildPokemonTools(StackPanel stack)
    {
        var facility=Card(_project.Generation==6?"Battle Maison · Instalaciones de combate":"Battle Tree / Royal · Instalaciones de combate",
            "Editar entrenadores, sus opciones Pokémon, especies, objetos, naturaleza, movimientos y EV con formatos originales e historial. Exportación de datos a texto.");
        var facilityPanel=(StackPanel)facility.Child;
        AddButton(facilityPanel,_project.Generation==6?"Abrir Battle Maison":"Abrir Battle Tree",()=>_navigate("FacilityNormal"));
        AddButton(facilityPanel,_project.Generation==6?"Abrir Super Maison":"Abrir Battle Royal",()=>_navigate("FacilityAlternate"));
        stack.Children.Add(facility);
        var learn=Card("Egg Moves · Movimientos huevo",
            "Editor original Gen6/Gen7, especies y formas, aprendizaje y randomizador con tamaño y STAB configurables. Vista previa y guardado con historial.");
        AddButton((StackPanel)learn.Child,"Abrir Egg Moves",()=>_navigate("EggMoves"));
        stack.Children.Add(learn);
        var mega=Card("Mega Evolutions · Evoluciones Mega",
            "Editá ambas Mega formas, piedras y habilitación por especie sin perder los bytes originales. Rayquaza conserva su regla especial.");
        AddButton((StackPanel)mega.Child,"Abrir Mega Evolutions",()=>_navigate("MegaEvolutions"));
        stack.Children.Add(mega);
        var typeChart=Card("Type Chart · Efectividad de tipos",
            "Matriz editable de 18×18 para Gen6 y Gen7, con inmunidades, resistencias y supereficacia. Vista previa y guardado con historial.");
        AddButton((StackPanel)typeChart.Child,"Abrir Type Chart",()=>_navigate("TypeChart"))
            .IsEnabled=_project.Generation is 6 or 7;
        stack.Children.Add(typeChart);
        var shiny=Card("Shiny Rate · Frecuencia shiny",
            "Rerolls PID y Everything Shiny usando el código ARM original. Firma verificada, vista previa, restaurar vanilla e historial.");
        AddButton((StackPanel)shiny.Child,"Abrir Shiny Rate",()=>_navigate("ShinyRate"))
            .IsEnabled=_project.Generation is 6 or 7;
        stack.Children.Add(shiny);
        var pickup=Card("Recogida · Pickup","Objetos comunes y raros Gen6; objetos y probabilidades por nivel Gen7. Randomización legal, vista previa e historial.");
        AddButton((StackPanel)pickup.Child,"Abrir Pickup",()=>_navigate("Pickup"));stack.Children.Add(pickup);
        var owse=Card("OWSE [Dev] · Mapas y scripts",
            "Explorador experimental de zonas USUM, entradas de encuentros y scripts. " +
            "El análisis es de solo lectura hasta comprobar la compatibilidad de edición.");
        AddButton((StackPanel)owse.Child,"Abrir OWSE [Dev]",()=>_navigate("OWSE"))
            .IsEnabled=_project.Generation==7;
        if(ProjectFeatureAvailability.Supports(_project,"OWSE"))stack.Children.Add(owse);
    }
    private void BuildRomTools(StackPanel stack)
    {
        var extract=Card("Extraer ROM · CXI / 3DS",
            "Extracción en una carpeta nueva, con verificación de archivos y sin modificar el contenedor original.");
        AddButton((StackPanel)extract.Child,"Abrir extracción",()=>_navigate("Extraction"));
        stack.Children.Add(extract);
        var rebuild=Card("Rebuild · RomFS, ExeFS y .3DS",
            "Reconstrucción original de pk3DS: RomFS, ExeFS, 3DS completo y Trimmed 3DS. " +
            "Salida externa, staging temporal, verificación SHA-256 y progreso por etapas reales.");
        AddButton((StackPanel)rebuild.Child,"Abrir Rebuild",()=>_navigate("Rebuild"));
        stack.Children.Add(rebuild);
        var integrity=Card("Reconstruir CRO / CRR",
            "Verifica y recalcula hashes CRO. La tabla CRR se actualiza opcionalmente, con vista previa e historial conjunto.");
        AddButton((StackPanel)integrity.Child,"Abrir CRO / CRR",()=>_navigate("CroIntegrity"));
        stack.Children.Add(integrity);
        var restore=Card("Restaurar archivos de un backup","Recupera archivos seleccionados de RomFS/ExeFS con vista previa, fingerprints e historial.");
        AddButton((StackPanel)restore.Child,"Abrir restauración selectiva",()=>_navigate("RestoreSelected"));stack.Children.Add(restore);
        {var title=Card((_project.Generation==7?"Logos e imágenes de inicio · Gen":"Pantallas de título · Gen")+_project.Generation,_project.GameConfiguration.USUM?"Logos 2D y animaciones de inicio: editor de imágenes y vídeos MOFLEX de acceso a la partida.":"Importa y exporta imágenes del título con verificación e historial.");AddButton((StackPanel)title.Child,"Abrir pantallas de título",()=>_navigate(_project.Generation==6?"TitleScreen6":"TitleScreen7"));stack.Children.Add(title);}
        if(_project.Generation==6){var shuffle=Card("GARC Shuffler Gen6","Mezcla referencias FATB a archivos simples con lista vanilla de protección y backup obligatorio.");AddButton((StackPanel)shuffle.Child,"Abrir GARC Shuffler",()=>_navigate("GarcShuffle"));stack.Children.Add(shuffle);}
        var graphics=Card("Imágenes BCLIM / BFLIM","Vista previa y exportación PNG; conversión a BCLIM RGBA8 en un archivo independiente.");
        AddButton((StackPanel)graphics.Child,"Abrir herramientas gráficas",()=>_navigate("Graphics"));stack.Children.Add(graphics);
        var icon=Card("Icono y metadatos SMDH","Edita los títulos y el publisher en 16 idiomas e importa/exporta imágenes del icono con historial.");
        AddButton((StackPanel)icon.Child,"Abrir editor SMDH",()=>_navigate("SMDH"));stack.Children.Add(icon);
        var audit=Card("Auditoría de archivos de la ROM",
            "Inventario reproducible con rutas relativas, tamaño y SHA-256 de cada archivo extraído. Exporta un JSON para comparar respaldos o detectar archivos alterados; no modifica el juego.");
        var panel=(StackPanel)audit.Child;
        AddButton(panel,"Exportar inventario SHA-256…",async ()=>
        {
            var picker=new Microsoft.Win32.SaveFileDialog
                {Filter="Inventario JSON|*.json",FileName="3ds-progressive-inventory.json"};
            if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
            string root=_project.ProjectRoot,target=picker.FileName;
            UiProgress.Begin("Calculando hashes SHA-256 de los archivos de la ROM…");
            try
            {
                var items=await Task.Run(()=>
                {
                    UiProgress.Stage("Enumerando archivos RomFS/ExeFS…");
                    var files=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
                        .Where(f=>f.StartsWith(Path.Combine(root,"RomFS")+Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase)
                            ||f.StartsWith(Path.Combine(root,"ExeFS")+Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).ToArray();
                    var result=new List<RomFile>(files.Length);
                    UiProgress.Count("Archivos localizados",0,Math.Max(files.Length,1));
                    for(int i=0;i<files.Length;i++)
                    {
                        using var input=File.OpenRead(files[i]);
                        string digest=Convert.ToHexString(SHA256.HashData(input));
                        result.Add(new RomFile(Path.GetRelativePath(root,files[i]),
                            input.Length,digest));
                        UiProgress.Count("Calculando SHA-256 · "+Path.GetFileName(files[i]),
                            i+1,Math.Max(files.Length,1));
                    }
                    return result.ToArray();
                });
                File.WriteAllText(target,JsonSerializer.Serialize(new
                {
                    Game=_project.GameName,Project=Path.GetFileName(root),
                    Files=items
                },new JsonSerializerOptions{WriteIndented=true}));
                _status.Text=$"Inventario exportado: {items.Length} archivos con SHA-256.";
            }
            catch(Exception ex)
            {
                StudioDialog.Error(Window.GetWindow(this),"Inventario de ROM",ex.Message);
                _status.Text="Error al exportar el inventario: "+ex.Message;
            }
            finally{UiProgress.End();}
        });
        stack.Children.Add(audit);
        var lz=Card("LZ11 · Compresión y descompresión",
            "Compatible con pk3DS vanilla. Seleccioná un archivo de hasta 15 MB y un destino nuevo; no reemplaza el archivo original.");
        var lzButtons=new WrapPanel();((StackPanel)lz.Child).Children.Add(lzButtons);
        AddButton(lzButtons,"Descomprimir LZ11…",()=>_ = RunLz11Async(true));
        AddButton(lzButtons,"Comprimir LZ11…",()=>_ = RunLz11Async(false));
        stack.Children.Add(lz);
        var backup=Card("Backup completo verificado",
            "Copia de trabajo y su historial. Se utiliza el mismo motor ProjectBackup del editor, sin duplicar ni cambiar su política.");
        AddButton((StackPanel)backup.Child,"Crear backup del proyecto…",_backup);
        stack.Children.Add(backup);
        var exports=Card("Exportaciones de datos",
            "Cada editor ofrece sus propios CSV, JSON o templates. Para evitar exportaciones incompletas, se reutilizan los editores con su validación existente.");
        var buttons=new WrapPanel();((StackPanel)exports.Child).Children.Add(buttons);
        AddButton(buttons,"MT y tutores",()=>_navigate("MachineTutor"));
        AddButton(buttons,"Level Up Moves",()=>_navigate("LevelUp"));
        AddButton(buttons,"Historia y textos",()=>_navigate("GameText"));
        stack.Children.Add(exports);
    }
    private async Task RunLz11Async(bool decompress)
    {
        var input=new Microsoft.Win32.OpenFileDialog{Title=decompress?"LZ11 · Archivo comprimido":"LZ11 · Archivo original"};
        if(input.ShowDialog(Window.GetWindow(this))!=true)return;
        if(new FileInfo(input.FileName).Length>15*1024*1024)
        {
            StudioDialog.Error(Window.GetWindow(this),"LZ11","El motor vanilla limita esta operación a 15 MB.");return;
        }
        var output=new Microsoft.Win32.SaveFileDialog
        {
            Title=decompress?"Guardar archivo descomprimido":"Guardar archivo LZ11",
            FileName=Path.GetFileNameWithoutExtension(input.FileName)+(decompress?"_dec.bin":".lz")
        };
        if(output.ShowDialog(Window.GetWindow(this))!=true)return;
        string source=Path.GetFullPath(input.FileName),dest=Path.GetFullPath(output.FileName);
        if(File.Exists(dest)||string.Equals(source,dest,StringComparison.OrdinalIgnoreCase))
        {
            StudioDialog.Error(Window.GetWindow(this),"LZ11",
                "El destino debe ser un archivo nuevo y distinto del original.");return;
        }
        UiProgress.Begin(decompress?"LZ11 · Descomprimiendo…":"LZ11 · Comprimiendo…");
        try
        {
            await Task.Run(()=>
            {
                if(decompress)pk3DS.Core.CTR.LZSS.Decompress(source,dest);
                else pk3DS.Core.CTR.LZSS.Compress(source,dest);
            });
            if(!File.Exists(dest)||new FileInfo(dest).Length==0)
                throw new InvalidDataException("El motor no generó un archivo de salida válido.");
            _status.Text=$"LZ11 completado: {Path.GetFileName(dest)} · "+
                $"{new FileInfo(dest).Length:N0} bytes. Archivo fuente intacto.";
        }
        catch(Exception ex)
        {
            try{if(File.Exists(dest))File.Delete(dest);}catch(IOException){}
            StudioDialog.Error(Window.GetWindow(this),"LZ11",ex.Message);
        }
        finally{UiProgress.End();}
    }
    private void BuildAutomation(StackPanel stack)
    {
        var summary=Card("Flujos de randomización disponibles",
            "Entrá directamente a los motores reales con preview y guardado seguro. No hay un Randomize All global implementado aquí todavía; se evita simular una ejecución de módulos no integrados.");
        var commands=new WrapPanel();((StackPanel)summary.Child).Children.Add(commands);
        AddButton(commands,"Pokémon · Randomización masiva",_personalBatch);
        AddButton(commands,"Entrenadores",()=>_navigate("Trainers"));
        AddButton(commands,"Encuentros salvajes",()=>_navigate("Encounters"));
        AddButton(commands,"Encuentros estáticos",()=>_navigate("StaticEncounters"));
        AddButton(commands,"Level Up · Randomizar todos",()=>_navigate("LevelUp"));
        stack.Children.Add(summary);
        var templates=Card("Global Template + Batch ROM Builder",
            "Recetas originales v1/coverage8 con acciones y assets. Construcción .3ds con copia limpia por ROM, semilla única y manifiesto SHA-256 independiente.");
        var templateCommands=new WrapPanel();((StackPanel)templates.Child).Children.Add(templateCommands);
        AddButton(templateCommands,"Cargar / guardar Global Template",()=>_navigate("GlobalTemplate"));
        AddButton(templateCommands,"Abrir Batch ROM Builder",()=>_navigate("BatchRom"));
        stack.Children.Add(templates);
        var history=Card("Historial de ejecuciones",
            "Cada guardado confirmado registra el módulo, las opciones y los archivos afectados. Consultá el historial antes de iniciar otra randomización.");
        AddButton((StackPanel)history.Child,"Abrir historial",()=>_navigate("History"));
        stack.Children.Add(history);
    }
    private void BuildSettings(StackPanel stack)
    {
        var prefsLanguage=AppPreferences.Load();
        var language=Card("Options → Language · Idioma de datos del juego",
            "Selección original de pk3DS. Cambia los nombres, movimientos y textos que se LEEN " +
            "de los archivos del juego, no el idioma de los textos fijos de la interfaz WPF. " +
            "No traduce ni modifica los datos guardados en la ROM.");
        var languagePanel=(StackPanel)language.Child;
        var languagePicker=new System.Windows.Controls.ComboBox
        {
            MinWidth=260,HorizontalAlignment=System.Windows.HorizontalAlignment.Left,
            ItemsSource=new[]{"カタカナ · Japonés", "漢字 · Japonés (kanji)", "English",
                "Français", "Italiano", "Deutsch", "Español", "한국",
                "中文 (简体)", "中文 (繁體)"}.Take(_project.Generation==6?8:10).ToArray(),
            SelectedIndex=Math.Clamp(prefsLanguage.GameTextLanguageIndex,0,_project.Generation==6?7:9),
            Margin=new Thickness(0,0,0,10)
        };
        languagePanel.Children.Add(languagePicker);
        AddButton(languagePanel,"Guardar idioma y recargar proyecto",()=>
        {
            if(_project.Generation==6&&languagePicker.SelectedIndex>7)
                throw new NotSupportedException("Los juegos Gen6 no admiten los dos últimos idiomas.");
            prefsLanguage.GameTextLanguageIndex=languagePicker.SelectedIndex;
            prefsLanguage.Save();
            _navigate("ReloadLanguage");
        });
        stack.Children.Add(language);
        var help=Card("Options → About pk3DS / About GARCs",
            "Información de la aplicación y la distribución real de GARC del juego activo.");
        var helpButtons=new WrapPanel();
        ((StackPanel)help.Child).Children.Add(helpButtons);
        AddButton(helpButtons,"About pk3DS",()=>StudioDialog.Notice(Window.GetWindow(this),
            "Acerca de Definitive Randomizer",
            "Interfaz Definitive Randomizer sobre pk3DS.Core. Proyecto abierto: "+_project.GameName+
            " · Gen "+_project.Generation+". Historial de modificaciones verificadas. " +
            "Los parches binarios necesitan validación en runtime."));
        AddButton(helpButtons,"About GARCs",()=>
        {
            if(StudioDialog.AutomatedChecks)return;
            CreateGarcDialog().ShowDialog();
        });
        stack.Children.Add(help);
        var paths=Card("Carpetas predeterminadas · solo en este equipo",
            "Se guardan en AppData local, nunca en RomFS ni ExeFS. Se reutilizan al abrir proyectos y al crear backups.");
        var panel=(StackPanel)paths.Child;
        var prefs=AppPreferences.Load();
        TextBox Field(string label,string value)
        {
            panel.Children.Add(new TextBlock{Text=label,Foreground=(Brush)FindResource("TextSoft"),
                Margin=new Thickness(0,5,0,6)});
            var box=new TextBox{Text=value,MinHeight=32,Margin=new Thickness(0,0,0,10)};
            panel.Children.Add(box);return box;
        }
        var project=Field("Carpeta inicial al abrir proyecto",prefs.DefaultProjectFolder);
        var backup=Field("Carpeta inicial para backups",prefs.DefaultBackupFolder);
        AddButton(panel,"Guardar carpetas predeterminadas",()=>
        {
            prefs.DefaultProjectFolder=project.Text.Trim();
            prefs.DefaultBackupFolder=backup.Text.Trim();
            prefs.Save();_status.Text="Preferencias guardadas en este equipo.";
        });
        stack.Children.Add(paths);
        var info=Card("Proyecto y compatibilidad",
            $"Proyecto actual: {_project.GameName}\nGeneración: {_project.Generation}\nRuta: {_project.ProjectRoot}\n" +
            "Interfaz oscura WPF. Los parches de código se habilitan solamente donde se ha investigado su compatibilidad.");
        AddButton((StackPanel)info.Child,"Copiar ruta del proyecto",()=>
        {Clipboard.SetText(_project.ProjectRoot);_status.Text="Ruta copiada al portapapeles.";});
        stack.Children.Add(info);
    }
    private sealed record RomFile(string Path,long Bytes,string Sha256);
}
