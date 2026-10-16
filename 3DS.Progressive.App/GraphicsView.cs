using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using UserControl=System.Windows.Controls.UserControl;
using Image=System.Windows.Controls.Image;

namespace ThreeDS.Progressive;
internal sealed class GraphicsView : UserControl
{
    private readonly Image _preview=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _status=new();
    private readonly System.Windows.Controls.ComboBox _format=new(){Width=150,Margin=new Thickness(0,0,8,8),ItemsSource=Enum.GetValues<pk3DS.Core.CTR.XLIMEncoding>().Cast<object>().Append("Paleta Gen6").ToArray(),SelectedItem=pk3DS.Core.CTR.XLIMEncoding.RGBA8};
    private byte[]? _png;
    private string? _source;
    internal GraphicsView()
    {
        Background=(Brush)FindResource("Bg");var root=new DockPanel{Margin=new Thickness(22)};Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Imágenes BCLIM / BFLIM",FontSize=24,Foreground=(Brush)FindResource("TextMain")});
        header.Children.Add(new TextBlock{Text="Abre BCLIM, BFLIM o PNG; exporta PNG o convierte a BCLIM/BFLIM con el formato seleccionado. ETC1 comprime el color; ETC1A4 conserva alfa de 4 bits. La salida se elige por separado y no reemplaza el archivo de origen.",Foreground=(Brush)FindResource("TextSoft"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
        var buttons=new WrapPanel();header.Children.Add(buttons);
        Add(buttons,"Abrir imagen…",Open);Add(buttons,"Exportar PNG…",()=>Export(false));buttons.Children.Add(_format);Add(buttons,"Exportar BCLIM…",()=>Export(true));Add(buttons,"Exportar BFLIM…",()=>Export(true,true));
        _status.Foreground=(Brush)FindResource("TextSoft");_status.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(_preview);
    }
    private static void Add(WrapPanel panel,string text,Action action){var button=new Button{Content=text,Margin=new Thickness(0,0,8,8)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
    private void Try(Action action){try{action();}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Herramientas gráficas",ex.Message);}}
    private void Open()=>Try(()=>
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="Imágenes compatibles|*.bclim;*.bflim;*.png|Todos los archivos|*.*"};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        if(new FileInfo(picker.FileName).Length>128*1024*1024)throw new InvalidDataException("La imagen supera 128 MiB.");
        byte[] bytes=File.ReadAllBytes(picker.FileName);using var stream=new MemoryStream(bytes);
        bool png=bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10});
        if(png&&(bytes.Length<24||System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4)) is 0 or >4096||System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4)) is 0 or >4096))throw new InvalidDataException("Dimensiones PNG fuera de rango.");
        using var bitmap=png?new System.Drawing.Bitmap(stream):GameGraphicsCodec.Decode(bytes);
        if(bitmap.Width>4096||bitmap.Height>4096)throw new InvalidDataException("Dimensiones máximas 4096 × 4096.");
        using var output=new MemoryStream();bitmap.Save(output,System.Drawing.Imaging.ImageFormat.Png);_png=output.ToArray();_source=Path.GetFullPath(picker.FileName);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=new MemoryStream(_png);image.EndInit();image.Freeze();_preview.Source=image;
        _status.Text=$"{Path.GetFileName(_source)} · {bitmap.Width} × {bitmap.Height} · origen intacto";
    });
    private async void Export(bool bclim,bool bflim=false)
    {
      try
      {
        if(_png is null)throw new InvalidOperationException("Abrí una imagen primero.");
        bool indexed=_format.SelectedItem is string;
        if(bclim&&bflim&&indexed)throw new ArgumentException("La paleta Gen6 se exporta en BCLIM.");
        var picker=new Microsoft.Win32.SaveFileDialog{Filter=bclim?(bflim?"BFLIM|*.bflim":"BCLIM|*.bclim"):"PNG|*.png",FileName=Path.GetFileNameWithoutExtension(_source)+(bclim?"-"+_format.SelectedItem+(bflim?".bflim":".bclim"):"-export.png")};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;
        if(Path.GetFullPath(picker.FileName).Equals(_source,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La salida no puede reemplazar el archivo de origen.");
        byte[] output=_png;
        if(bclim)
        {
            var format=indexed?pk3DS.Core.CTR.XLIMEncoding.RGB5A1:(pk3DS.Core.CTR.XLIMEncoding)_format.SelectedItem;byte[] source=_png;
            IsEnabled=false;_status.Text="Convirtiendo imagen · "+format+"…";
            output=await Task.Run(()=>{using var stream=new MemoryStream(source);using var image=new System.Drawing.Bitmap(stream);return indexed?GameGraphicsCodec.EncodePalette(image):GameGraphicsCodec.Encode(image,format,bflim);});
        }
        await File.WriteAllBytesAsync(picker.FileName,output);_status.Text="Imagen exportada: "+Path.GetFileName(picker.FileName);
      }
      catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Herramientas gráficas",ex.Message);}
      finally{IsEnabled=true;}
    }
}
