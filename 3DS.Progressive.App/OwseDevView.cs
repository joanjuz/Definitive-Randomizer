using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using ListBox=System.Windows.Controls.ListBox;
using TextBox=System.Windows.Controls.TextBox;
using UserControl=System.Windows.Controls.UserControl;

namespace ThreeDS.Progressive;

/// <summary>
/// Read-only Gen7 OWSE explorer. Zone relationship, script slot 7/8 and raw
/// bytes come from original GARC data; no partially understood writes.
/// </summary>
internal sealed class OwseDevView : UserControl
{
    private readonly TextBox _details;
    private readonly ListBox _zones;
    private readonly (int Id,int ParentMap,string Location)[] _zoneItems;
    private readonly LazyGARCFile _encounters;
    internal OwseDevView(GameProjectSession project)
    {
        if(project.Generation!=7)throw new NotSupportedException("OWSE Dev actual solo admite Gen7.");
        var config=project.GameConfiguration;
        _encounters=config.GetlzGARCData("encdata");
        var zones=config.GetlzGARCData("zonedata");
        if(zones.FileCount==0||zones[0].Length<ZoneData7.SIZE)
            throw new InvalidDataException("zonedata no contiene entradas de zona.");
        var rawZones=ZoneData7.GetArray(zones[0]);
        var names=config.GetText(TextName.metlist_000000);
        _zoneItems=rawZones.Select((z,i)=>(Id:i,ParentMap:z.ParentMap,
            Location:z.ParentMap>=0&&z.ParentMap<names.Length?names[z.ParentMap]:$"Mapa {z.ParentMap}")).ToArray();
        Background=(Brush)FindResource("Bg");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var top=new StackPanel{Margin=new Thickness(0,0,0,13)};
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="OWSE [Dev] · Explorador de zonas",
            FontSize=23,FontWeight=FontWeights.SemiBold,Foreground=(Brush)FindResource("TextMain")});
        top.Children.Add(new TextBlock
        {
            Text="Lectura de zonedata y encdata originales. Para cada zona se muestran las entradas " +
                 "ZS (scripts de zona) y ZI (información). Vista experimental: sin escritura de scripts.",
            TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),
            Margin=new Thickness(0,6,0,7)
        });
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(320)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(grid);
        _zones=new ListBox
        {
            Margin=new Thickness(0,0,12,0),
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1),
            ItemsSource=_zoneItems.Select(x=>$"{x.Id:000}  ·  {x.Location}  (mapa {x.ParentMap})").ToArray()
        };
        grid.Children.Add(_zones);
        _details=new TextBox
        {
            IsReadOnly=true,FontFamily=new FontFamily("Consolas"),FontSize=12,
            AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            Background=(Brush)FindResource("Panel"),Foreground=(Brush)FindResource("TextMain"),
            BorderBrush=(Brush)FindResource("Stroke"),BorderThickness=new Thickness(1)
        };
        Grid.SetColumn(_details,1);grid.Children.Add(_details);
        _zones.SelectionChanged+=(_,_)=>ShowZone(_zones.SelectedIndex);
        _zones.SelectedIndex=0;
    }
    private void ShowZone(int index)
    {
        if(index<0||index>=_zoneItems.Length)return;
        var zone=_zoneItems[index];
        var log=new StringBuilder();
        log.AppendLine($"Zona {zone.Id:000} · {zone.Location}");
        log.AppendLine($"ParentMap: {zone.ParentMap} · GARC: encdata/zonedata");
        log.AppendLine("Slots vanilla de cada zona: índice = zona × 11 + slot");
        foreach(var (slot,name) in new[]{(7,"ZS · Zone Script"),(8,"ZI · Zone Info")})
        {
            int id=checked(index*11+slot);
            log.AppendLine();
            log.AppendLine($"--- {name} / entrada {id} ---");
            if(id>=_encounters.FileCount)
            {
                log.AppendLine("Sin entrada para esta zona.");
                continue;
            }
            try
            {
                byte[] data=_encounters[id];
                log.AppendLine($"{data.Length:N0} bytes (datos originales, sin editar)");
                log.AppendLine(Convert.ToHexString(data.AsSpan(0,Math.Min(256,data.Length))));
                if(data.Length>256)log.AppendLine("… vista hexadecimal limitada a 256 bytes");
            }
            catch(Exception ex) when(ex is InvalidDataException or IndexOutOfRangeException)
            {
                log.AppendLine("No se pudo interpretar la entrada: "+ex.Message);
            }
        }
        _details.Text=log.ToString();
    }
}
