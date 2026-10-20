using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;
using UserControl=System.Windows.Controls.UserControl;
using Button=System.Windows.Controls.Button;
using ListBox=System.Windows.Controls.ListBox;
using Brush=System.Windows.Media.Brush;
using HorizontalAlignment=System.Windows.HorizontalAlignment;

namespace ThreeDS.Progressive;

public sealed class HistoryView : UserControl
{
    public HistoryView(ProjectHistory history)
    {
        Background=(Brush)FindResource("Bg");var grid=new Grid {Margin=new Thickness(22)};
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());
        var header=new StackPanel {Margin=new Thickness(0,0,0,20)};
        header.Children.Add(new TextBlock {Text="Historial del proyecto",FontSize=23,FontWeight=FontWeights.SemiBold});
        header.Children.Add(new TextBlock {Text="Cambios confirmados, parámetros y archivos utilizados. Conserva la secuencia para ampliar los templates globales.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,14)});
        var export=new Button {Content="Exportar historial (JSON)",HorizontalAlignment=HorizontalAlignment.Left};
        export.Click+=(_,_)=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog {FileName="historial-progressive.json",Filter="Historial JSON|*.json"};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            try{history.Export(dialog.FileName);StudioDialog.Notice(Window.GetWindow(this),"Historial exportado","El archivo contiene las acciones confirmadas y sus parámetros.");}
            catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"No se pudo exportar",ex.Message);}
        };
        header.Children.Add(export);
        var validation=new TextBlock{Text="Estado actual sin comprobar. El historial registra operaciones pasadas.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        header.Children.Add(validation);
        var verify=new Button{Content="Comprobar archivos actuales",HorizontalAlignment=HorizontalAlignment.Left};header.Children.Add(verify);
        grid.Children.Add(header);
        HistoryInspection? inspection=null;
        CancellationTokenSource? pending=null;
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(300)});body.ColumnDefinitions.Add(new());Grid.SetRow(body,1);grid.Children.Add(body);
        var details=new StackPanel {Margin=new Thickness(20,0,0,0)};
        var scroll=new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        var entries=history.Read().Entries.Reverse().ToArray();
        var list=new ListBox();body.Children.Add(list);
        string State(HistoryEntry entry)=>inspection is null||!inspection.Entries.TryGetValue(entry.Id,out var state)?"Sin comprobar":state switch
            {HistoryEntryState.Verified=>"Cadena coherente con archivos actuales",HistoryEntryState.OutOfDate=>"Desfasado: no acredita el estado actual",_=>"Sin comprobación binaria"};
        void Fill()
        {
            Guid? selected=(list.SelectedItem as ListBoxItem)?.Tag is HistoryEntry chosen?chosen.Id:null;
            list.Items.Clear();
            foreach(var entry in entries)list.Items.Add(new ListBoxItem {Tag=entry,Content=new TextBlock {Text=$"{entry.Time.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n{entry.Module} · {entry.Action}\n"+(entry.Kind=="Action"?"Acción y opciones · "+entry.ChangedValueCount+" valores afectados":entry.Changes.Count+" cambios")+"\n"+State(entry),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,7,3,7)}});
            if(entries.Length>0)list.SelectedIndex=Array.FindIndex(entries,entry=>entry.Id==selected) is int index&&index>=0?index:0;
        }
        list.SelectionChanged+=(_,_)=>
        {
            if(list.SelectedItem is not ListBoxItem {Tag:HistoryEntry entry})return;details.Children.Clear();
            Add(entry.Action,true);Add(State(entry),true);Add(entry.Time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            if(entry.Kind=="Action")Add("Se conserva la acción y sus opciones. Las randomizaciones usarán una semilla nueva en cada ROM.");
            foreach(var change in entry.Changes){Add(change.Name+" · "+change.Field,true);Add(change.Before+" → "+change.After);}
            if(entry.Parameters.Count>0){Add("Parámetros",true);foreach(var parameter in entry.Parameters)Add(parameter.Key+": "+parameter.Value);}
            Add("Archivos registrados",true);foreach(var file in entry.Files)Add(file.Path+"\n"+file.BeforeSha256+" → "+file.AfterSha256);
        };
        Fill();if(entries.Length==0)Add("Todavía no hay cambios guardados en este proyecto.");
        async Task VerifyAsync()
        {
            if(pending is not null)return;
            var cancellation=new CancellationTokenSource();pending=cancellation;verify.IsEnabled=false;
            using var interaction=UiProgress.BlockInteraction();
            UiProgress.Begin("Comprobando historial y archivos actuales…");
            validation.Text="Comprobando SHA-256 de los archivos registrados…";
            inspection=null;Fill();
            try
            {
                var result=await Task.Run(()=>history.InspectCurrentState(cancellation.Token),cancellation.Token);
                if(cancellation.IsCancellationRequested||!IsLoaded)return;
                inspection=result;entries=result.Document.Entries.Reverse().ToArray();validation.Text=result.Summary;Fill();
                if(entries.Length==0){details.Children.Clear();Add("Todavía no hay cambios guardados en este proyecto.");}
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
            catch(Exception ex){if(IsLoaded)validation.Text="No se pudo comprobar el historial: "+ex.Message;}
            finally{pending=null;cancellation.Dispose();verify.IsEnabled=true;UiProgress.End();}
        }
        verify.Click+=async(_,_)=>await VerifyAsync();
        Loaded+=async(_,_)=>await VerifyAsync();
        Unloaded+=(_,_)=>pending?.Cancel();
        void Add(string text,bool strong=false)=>details.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,Foreground=(Brush)FindResource(strong?"TextMain":"TextSoft"),Margin=new Thickness(0,0,0,12)});
        Content=grid;
    }
}
