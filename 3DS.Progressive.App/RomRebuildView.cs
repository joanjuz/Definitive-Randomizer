using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

internal sealed class RomRebuildView : UserControl
{
    private readonly GameProjectSession _project;
    private readonly GameRomRebuildService _service;
    private readonly ComboBox _kind;
    private readonly TextBlock _detail;
    private readonly Button _run;
    private bool _busy;
    internal bool ConfirmLeave()=>!_busy;
    internal RomRebuildView(GameProjectSession project)
    {
        _project=project;
        _service=new GameRomRebuildService(project);
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(20)};
        Content=root;
        var panel=new StackPanel();
        root.Children.Add(panel);
        panel.Children.Add(new TextBlock
        {
            Text="Rebuild · Reconstrucción de archivos ROM",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")
        });
        panel.Children.Add(new TextBlock
        {
            Text="Reutiliza los empaquetadores originales de pk3DS. Prepara la salida en una carpeta temporal " +
                "y verifica su SHA-256 antes de moverla al destino. Nunca reemplaza archivos existentes ni escribe el proyecto fuente.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,8,0,16)
        });
        _kind=new ComboBox{MinWidth=310,HorizontalAlignment=System.Windows.HorizontalAlignment.Left,
            ItemsSource=new[]{"RomFS (.romfs)", "ExeFS (.exefs)", ".3DS completo", "Trimmed .3DS"},
            SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
        panel.Children.Add(_kind);
        _run=new Button{Content="Seleccionar salida y reconstruir…",
            HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};
        _run.Click+=async(_,_)=>await BuildAsync();
        panel.Children.Add(_run);
        var card=new Border{Background=(Brush)FindResource("Panel"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(9),Padding=new Thickness(14)};
        panel.Children.Add(card);
        _detail=new TextBlock{Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,
            Text="No se ha iniciado ninguna reconstrucción. Para .3DS se necesita ExHeader en la raíz extraída."};
        card.Child=_detail;
    }
    internal static string HistorySummary(HistoryDocument history)
    {
        if(history.Entries.Count==0)return "No hay cambios registrados. Se reconstruirán los archivos actuales; las modificaciones externas pueden no figurar en el historial.";
        var text=new System.Text.StringBuilder();
        text.AppendLine($"{history.Entries.Count} acciones guardadas · {history.Entries.SelectMany(e=>e.Files).Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()} archivos registrados");
        foreach(var entry in history.Entries)
        {
            text.AppendLine($"\n{entry.Time.ToLocalTime():dd/MM/yyyy HH:mm:ss} · {entry.Module} · {entry.Action}");
            text.AppendLine($"{entry.ChangedValueCount} valores registrados");
            foreach(var change in entry.Changes)text.AppendLine($"  {change.Name} · {change.Field}: {change.Before} → {change.After}");
            foreach(var parameter in entry.Parameters)text.AppendLine($"  {parameter.Key}: {parameter.Value}");
            foreach(var file in entry.Files)text.AppendLine("  Archivo: "+file.Path);
        }
        text.AppendLine("\nEl historial puede contener cambios sustituidos por ediciones posteriores. Se empaqueta el estado guardado actual, no las opciones pendientes de Global/Batch ni ediciones sin aplicar.");
        return text.ToString();
    }

    private async Task BuildAsync()
    {
        if(_busy)return;
        RomRebuildKind kind=(RomRebuildKind)_kind.SelectedIndex;
        string history;
        try{history=HistorySummary(_project.History.Read());}
        catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo leer el historial",ex.Message);return;}
        var picker=new Microsoft.Win32.SaveFileDialog
        {
            Title="Guardar resultado de Rebuild",
            Filter=kind switch
            {
                RomRebuildKind.RomFS=>"RomFS|*.romfs",
                RomRebuildKind.ExeFS=>"ExeFS|*.exefs",
                _=>"ROM Nintendo 3DS|*.3ds"
            },
            FileName=kind switch
            {
                RomRebuildKind.RomFS=>"romfs.bin",
                RomRebuildKind.ExeFS=>"exefs.bin",
                RomRebuildKind.Trimmed3DS=>"Progressive_trimmed.3ds",
                _=>"Progressive.3ds"
            }
        };
        if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        if(File.Exists(picker.FileName))
        {
            StudioDialog.Error(Window.GetWindow(this),"Rebuild",
                "Seleccioná otro nombre: no se sobrescriben archivos existentes.");
            return;
        }
        if(!StudioDialog.Confirm(Window.GetWindow(this),"Reconstruir ROM",
            $"Se reconstruirá {kind} con el motor pk3DS original en una ruta temporal. "+
            "Puede tardar varios minutos. ¿Continuar?",history))return;
        using var interaction=UiProgress.BlockInteraction();
        _busy=true;_run.IsEnabled=false;_kind.IsEnabled=false;
        UiProgress.Begin("Rebuild · "+kind);
        try
        {
            var result=await _service.BuildAsync(kind,picker.FileName);
            _detail.Text=$"Reconstrucción terminada. Integridad del archivo verificada.\n"+
                $"Archivo: {result.File}\nTamaño: {result.Bytes:N0} bytes\n"+
                $"SHA-256: {result.Hash}\nLa carpeta fuente no fue modificada.";
            StudioDialog.Notice(Window.GetWindow(this),"Rebuild completado",
                "Archivo reconstruido con el empaquetador original y SHA-256 verificado.",result.File);
        }
        catch(Exception ex)
        {
            // Preserve the complete stack trace locally. The UI previously only
            // displayed 'Object reference not set...', with no actionable stage.
            string stage=UiProgress.Snapshot.Stage;
            string? diagnostics=null;
            try
            {
                string folder=Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),"3DS-Progressive","logs");
                Directory.CreateDirectory(folder);
                diagnostics=Path.Combine(folder,"rebuild-errors.log");
                File.AppendAllText(diagnostics,
                    $"[{DateTimeOffset.Now:O}] Kind={kind} Stage={stage}\n{ex}\n---\n");
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
            _detail.Text="La reconstrucción se detuvo; el proyecto de origen no fue modificado.\n"+
                "Etapa: "+stage+"\nError: "+ex.Message+
                (diagnostics is null?"":"\nDiagnóstico completo: "+diagnostics);
            StudioDialog.Error(Window.GetWindow(this),"Falló Rebuild",
                "Etapa: "+stage+"\n"+ex.Message+
                (diagnostics is null?"":"\nDetalles técnicos guardados en: "+diagnostics));
        }
        finally{_busy=false;_run.IsEnabled=true;_kind.IsEnabled=true;UiProgress.End();}
    }
}
