using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.CTR;
using Color=System.Windows.Media.Color;
namespace ThreeDS.Progressive.Services;

internal sealed record MovePreviewExample(string Name,int Type,string TypeName,int Category,int Power,int Accuracy,int PP);
// Static renderer for the audited SM/USUM move details layout; not an arbitrary BFLYT interpreter.
internal sealed class GameMoveDescriptionScene
{
    private readonly Dictionary<string,byte[]> _files;
    private readonly Dictionary<string,Pane> _panes=[];
    private readonly Dictionary<int,BitmapSource> _backgrounds=[];
    private sealed record Pane(string Name,Rect Area,byte[] Data);
    internal Rect DescriptionArea=>_panes["Desc_00"].Area;
    private static int I(byte[] b,int at)=>checked((int)BitConverter.ToUInt32(b,at));
    private static string Name(byte[] b,int at,int count)=>Encoding.ASCII.GetString(b,at,count).Split('\0')[0];
    internal static GameMoveDescriptionScene Load(string romFs,bool usum=true)
    {
        string path=Path.Combine(romFs,usum?"a/1/3/2":"a/1/3/1");if(!File.Exists(path)||new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("No se encontró la ficha de movimientos compatible.");
        var garc=new GARC.LazyGARC(File.ReadAllBytes(path));if(garc.FileCount<1)throw new InvalidDataException("Ficha de movimientos vacía.");
        return FromPackage(garc[0]);
    }
    internal static GameMoveDescriptionScene FromPackage(byte[] data)
    {
        var archive=new TitleAlytCodec(data);var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var entry in archive.Entries.Where(e=>e.Name.EndsWith(".bflyt")||e.Name.EndsWith(".bflim")||e.Name.Contains("CommonStatus_DSC_LOW_000_color_")))
            files[Path.GetFileName(entry.Name)]=archive.Read(entry.Id);
        return new(files);
    }
    private GameMoveDescriptionScene(Dictionary<string,byte[]> files)
    {
        _files=files;byte[] layout=Get("CommonStatus_DSC_LOW_000.bflyt");
        if(layout.Length<20||!layout.AsSpan(0,4).SequenceEqual("FLYT"u8)||BitConverter.ToUInt16(layout,4)!=0xFEFF||I(layout,12)!=layout.Length)throw new InvalidDataException("Diseño de movimientos incompatible.");
        for(int p=BitConverter.ToUInt16(layout,6);p<layout.Length;)
        {
            if(p+8>layout.Length)throw new InvalidDataException("Bloque de diseño truncado.");int length=I(layout,p+4);if(length<8||(long)p+length>layout.Length)throw new InvalidDataException("Bloque de diseño fuera de rango.");
            string tag=Name(layout,p,4);
            if(tag is "pic1" or "txt1")
            {
                if(length<104)throw new InvalidDataException("Panel de movimientos truncado.");byte[] b=layout.AsSpan(p,length).ToArray();string name=Name(b,12,24);
                double x=BitConverter.ToSingle(b,44),y=BitConverter.ToSingle(b,48),w=BitConverter.ToSingle(b,76),h=BitConverter.ToSingle(b,80);
                // Root and All have identity transforms in this audited layout.
                if(!double.IsFinite(x+y+w+h)||w<=0||h<=0||w>400||h>240)throw new InvalidDataException("Geometría del panel incompatible.");
                int anchor=b[9]&15;double left=160+x-((anchor%3) switch{1=>0,2=>w,_=>w/2});double top=120-y-((anchor/3) switch{1=>0,2=>h,_=>h/2});
                _panes[name]=new(name,new Rect(left,top,w,h),b);
            }
            p+=length;
        }
        foreach(string name in new[]{"BG_Circle_00","BG_00","TextBox_00","Desc_00","Palam_03","Palam_04"})if(!_panes.ContainsKey(name))throw new InvalidDataException("Falta un panel de la ficha de movimientos: "+name);
        _=Get("Waza_BG_00.bflim");_=Get("Battle_Infomation_Basic_01.bflim");
    }
    private byte[] Get(string name)=>_files.TryGetValue(name,out var bytes)?bytes:throw new InvalidDataException("La ROM no contiene el recurso "+name);
    private static BitmapSource Decode(byte[] raw)
    {
        using var bitmap=GameGraphicsCodec.Decode(raw);using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    private byte[] VertexColors(Pane pane,int type)
    {
        byte[] colors=pane.Data.AsSpan(84,16).ToArray();if(pane.Name!="BG_00")return colors;
        byte[] animation=Get($"CommonStatus_DSC_LOW_000_color_{type:00}.bflan");int start=animation.AsSpan().IndexOf("FLVC"u8);
        if(start<0||start+8>animation.Length)throw new InvalidDataException("No se encontró el color de tipo del movimiento.");int count=animation[start+4];
        if(count>16||start+8+count*4>animation.Length)throw new InvalidDataException("Colores de movimiento truncados.");
        for(int i=0;i<count;i++)
        {
            int at=checked(start+I(animation,start+8+i*4));if(at<0||at+24>animation.Length||animation[at+1]>15||animation[at+2]!=2||BitConverter.ToUInt16(animation,at+4)!=1||I(animation,at+8)!=12)throw new InvalidDataException("Animación de color incompatible.");
            float value=BitConverter.ToSingle(animation,at+16);if(!float.IsFinite(value)||value<0||value>255)throw new InvalidDataException("Color de movimiento inválido.");colors[animation[at+1]]=(byte)value;
        }
        return colors;
    }
    private BitmapSource Background(int type)
    {
        if(_backgrounds.TryGetValue(type,out var result))return result;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            foreach(var pair in new[]{("BG_Circle_00","Battle_Infomation_Basic_01.bflim"),("BG_00","Waza_BG_00.bflim")})
            {
                var pane=_panes[pair.Item1];var source=new FormatConvertedBitmap(Decode(Get(pair.Item2)),PixelFormats.Bgra32,null,0);int width=source.PixelWidth,height=source.PixelHeight;byte[] pixels=new byte[width*height*4];source.CopyPixels(pixels,width*4,0);byte[] colors=VertexColors(pane,type);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)for(int c=0;c<4;c++)
                {
                    double u=(double)x/Math.Max(1,width-1),v=(double)y/Math.Max(1,height-1);int channel=c==0?2:c==2?0:c;
                    double top=colors[channel]*(1-u)+colors[4+channel]*u,bottom=colors[8+channel]*(1-u)+colors[12+channel]*u;
                    pixels[(y*width+x)*4+c]=(byte)(pixels[(y*width+x)*4+c]*(top*(1-v)+bottom*v)/255);
                }
                var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);image.Freeze();dc.DrawImage(image,pane.Area);
            }
        }
        var bitmap=new RenderTargetBitmap(320,240,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();_backgrounds[type]=bitmap;return bitmap;
    }
    internal DescriptionRender Render(GameDescriptionFont font,string description,MovePreviewExample move)
    {
        if(move.Type is <0 or >17)throw new InvalidDataException("Tipo fuera del catálogo de la ficha.");bool overflow;var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            dc.DrawImage(Background(move.Type),new Rect(0,0,320,240));
            void Text(string pane,string value){if(_panes.TryGetValue(pane,out var p))font.DrawText(dc,value,p.Area,Colors.White);}
            Text("TextBox_00",move.Name);Text("TextBox_01","PP");Text("TextBox_02",move.PP.ToString());Text("TextBox_03",move.PP.ToString());Text("TextBox_04","/");
            Text("Palam_00","Categoría");Text("Palam_01","Potencia");Text("Palam_02","Precisión");Text("Palam_03",move.Power==0?"—":move.Power.ToString());Text("Palam_04",move.Accuracy==0?"—":move.Accuracy.ToString());
            font.DrawText(dc,move.TypeName,new Rect(93,46,64,20),Colors.White,.75);font.DrawText(dc,new[]{"Estado","Físico","Especial"}[Math.Clamp(move.Category,0,2)],new Rect(252,24,65,20),Colors.White,.65);
            string sample=description.Length>10000?description[..10000]:description;
            overflow=font.DrawText(dc,sample,_panes["Desc_00"].Area,Colors.White);
        }
        var bitmap=new RenderTargetBitmap(320,240,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        return new(bitmap,(description.Length>10000?"Vista limitada a los primeros 10.000 caracteres. ":"")+(overflow?"La descripción supera el cuadro o contiene caracteres sin representación. Revisá los saltos de línea.":"La descripción cabe en el cuadro del diseño."));
    }
}
