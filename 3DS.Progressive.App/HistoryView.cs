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
        header.Children.Add(export);grid.Children.Add(header);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(300)});body.ColumnDefinitions.Add(new());Grid.SetRow(body,1);grid.Children.Add(body);
        var details=new StackPanel {Margin=new Thickness(20,0,0,0)};
        var scroll=new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        var entries=history.Read().Entries.Reverse().ToArray();
        var list=new ListBox();body.Children.Add(list);
        foreach(var entry in entries)list.Items.Add(new ListBoxItem {Tag=entry,Content=new TextBlock {Text=$"{entry.Time.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n{entry.Module} · {entry.Action}\n{entry.Changes.Count} cambios",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,7,3,7)}});
        list.SelectionChanged+=(_,_)=>
        {
            if(list.SelectedItem is not ListBoxItem {Tag:HistoryEntry entry})return;details.Children.Clear();
            Add(entry.Action,true);Add(entry.Time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            foreach(var change in entry.Changes){Add(change.Name+" · "+change.Field,true);Add(change.Before+" → "+change.After);}
            if(entry.Parameters.Count>0){Add("Parámetros",true);foreach(var parameter in entry.Parameters)Add(parameter.Key+": "+parameter.Value);}
            Add("Archivos verificados",true);foreach(var file in entry.Files)Add(file.Path+"\n"+file.BeforeSha256+" → "+file.AfterSha256);
        };
        if(entries.Length>0)list.SelectedIndex=0;else Add("Todavía no hay cambios guardados en este proyecto.");
        void Add(string text,bool strong=false)=>details.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,Foreground=(Brush)FindResource(strong?"TextMain":"TextSoft"),Margin=new Thickness(0,0,0,12)});
        Content=grid;
    }
}
