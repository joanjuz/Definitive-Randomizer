using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive.Services;
using Brush=System.Windows.Media.Brush;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
using UserControl=System.Windows.Controls.UserControl;
using Panel=System.Windows.Controls.Panel;
using Image=System.Windows.Controls.Image;

namespace ThreeDS.Progressive;
internal sealed class IconView : UserControl
{
    private readonly GameIconService _service;
    private readonly TextBox[] _fields=[];
    private readonly Image _small=new(){Width=96,Height=96},_large=new(){Width=144,Height=144};
    private readonly TextBlock _status=new();
    private bool _loading;
    private int _language;
    internal bool ConfirmLeave()=>!_service.Dirty||StudioDialog.Confirm(Window.GetWindow(this),"SMDH pendiente","¿Descartar los cambios de icono y metadatos sin guardar?");
    internal IconView(GameProjectSession project)
    {
        _service=new(project);Background=(Brush)FindResource("Bg");
        var stack=new StackPanel{Margin=new Thickness(22)};Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(new TextBlock{Text="Icono y metadatos SMDH",FontSize=24,Foreground=(Brush)FindResource("TextMain")});
        stack.Children.Add(new TextBlock{Text="Edita títulos de 16 idiomas e iconos RGB565 de 24 × 24 y 48 × 48. Los demás campos se conservan al editar textos o imágenes.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("TextSoft"),Margin=new Thickness(0,8,0,12)});
        var languages=new ComboBox{ItemsSource=new[]{"Japonés","Inglés","Francés","Alemán","Italiano","Español","Chino simplificado","Coreano","Neerlandés","Portugués","Ruso","Chino tradicional","Reservado 12","Reservado 13","Reservado 14","Reservado 15"},SelectedIndex=0,Width=260,HorizontalAlignment=System.Windows.HorizontalAlignment.Left};stack.Children.Add(languages);
        _fields=Enumerable.Range(0,3).Select(index=>new TextBox{MaxLength=index==1?128:64,Margin=new Thickness(0,5,0,12)}).ToArray();
        for(int i=0;i<3;i++){int field=i;stack.Children.Add(new TextBlock{Text=new[]{"Título corto","Descripción","Editor / publisher"}[i],Foreground=(Brush)FindResource("TextSoft")});stack.Children.Add(_fields[i]);_fields[i].TextChanged+=(_,_)=>{if(!_loading)Try(()=>_service.Edit(_language,field,_fields[field].Text));};}
        languages.SelectionChanged+=(_,_)=>{_language=languages.SelectedIndex;Refresh();};
        var images=new WrapPanel();stack.Children.Add(images);
        foreach(bool large in new[]{false,true})
        {
            var panel=new StackPanel{Margin=new Thickness(0,10,30,12)};images.Children.Add(panel);panel.Children.Add(large?_large:_small);
            Add(panel,"Importar PNG "+(large?"48 × 48":"24 × 24"),()=>ImportImage(large));Add(panel,"Exportar PNG",()=>ExportImage(large));
        }
        var buttons=new WrapPanel();stack.Children.Add(buttons);
        Add(buttons,"Importar SMDH…",Import);Add(buttons,"Exportar SMDH…",Export);
        Add(buttons,"Guardar y verificar",()=>Try(()=>{for(int field=0;field<3;field++)_service.Edit(_language,field,_fields[field].Text);if(!StudioDialog.Confirm(Window.GetWindow(this),"Guardar SMDH","¿Guardar icon.bin y registrar los cambios en el historial?"))return;_service.Apply();_status.Text="SMDH guardado y verificado.";}));
        Add(buttons,"Descartar",()=>{_service.Discard();Refresh();});
        _status.Foreground=(Brush)FindResource("TextSoft");_status.TextWrapping=TextWrapping.Wrap;_status.Margin=new Thickness(0,10,0,0);stack.Children.Add(_status);Refresh();
    }
    private static void Add(Panel panel,string title,Action action){var button=new Button{Content=title,Margin=new Thickness(0,8,8,0)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
    private void Try(Action action){try{action();}catch(Exception ex){StudioDialog.Error(Window.GetWindow(this),"Editor SMDH",ex.Message);}}
    private void Refresh()
    {
        _loading=true;try{for(int i=0;i<3;i++)_fields[i].Text=_service.Read(_language,i);
        foreach(bool large in new[]{false,true}){using var bitmap=_service.Image(large);using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.StreamSource=stream;source.EndInit();source.Freeze();(large?_large:_small).Source=source;}}
        finally{_loading=false;}
    }
    private void ImportImage(bool large)=>Try(()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="PNG|*.png"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;if(new FileInfo(dialog.FileName).Length>5*1024*1024)throw new InvalidDataException("La imagen supera 5 MiB.");using var image=new System.Drawing.Bitmap(dialog.FileName);_service.ReplaceImage(large,image);Refresh();});
    private void ExportImage(bool large)=>Try(()=>{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG|*.png",FileName=large?"icon-48.png":"icon-24.png"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;using var image=_service.Image(large);image.Save(dialog.FileName,System.Drawing.Imaging.ImageFormat.Png);});
    private void Import()=>Try(()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="SMDH|*.bin;*.smdh"};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;if(new FileInfo(dialog.FileName).Length!=0x36C0)throw new InvalidDataException("Tamaño SMDH inválido.");_service.Import(File.ReadAllBytes(dialog.FileName));Refresh();});
    private void Export()=>Try(()=>{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="SMDH|*.bin;*.smdh",FileName="icon.bin"};if(dialog.ShowDialog(Window.GetWindow(this))==true)File.WriteAllBytes(dialog.FileName,_service.Export());});
}
