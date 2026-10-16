using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pk3DS.Core.CTR;
using Color=System.Windows.Media.Color;
namespace ThreeDS.Progressive.Services;

// Original summary background combined with the native XY/ORAS move-detail panes.
internal sealed class GameMoveDescription6Scene
{
    private sealed record Pane(Rect Area,byte[] Data,int ColorOffset=76,int MaterialOffset=92);
    private readonly List<string> _layers=[];
    private readonly Dictionary<string,byte[]> _files;
    private readonly Dictionary<string,Pane> _panes=[];
    private readonly Dictionary<string,(string Texture,Color Color)> _materials=[];
    private BitmapSource? _background;
    private readonly byte[] _summaryTexture,_summaryColors;
    internal Rect DescriptionArea=>_panes["14"].Area;
    private static void Bounds(byte[] b,int at,int length){if(at<0||length<0||(long)at+length>b.Length)throw new InvalidDataException("Ficha de movimientos Gen6 truncada.");}
    private static int U(byte[] b,int at){Bounds(b,at,4);return checked((int)BitConverter.ToUInt32(b,at));}
    private static string Name(byte[] b,int at,int length){Bounds(b,at,length);return Encoding.ASCII.GetString(b,at,length).Split('\0')[0];}
    internal static GameMoveDescription6Scene Load(string romFs)
    {
        string path=Path.Combine(romFs,"a/0/0/5");var info=new FileInfo(path);
        if(!info.Exists||info.Length>32*1024*1024)throw new InvalidDataException("No se encontró la ficha original de movimientos Gen6.");
        var garc=new GARC.LazyGARC(File.ReadAllBytes(path));if(garc.FileCount<=11)throw new InvalidDataException("La ROM no contiene el detalle de movimientos Gen6.");
        string summaryPath=Path.Combine(romFs,"a/0/0/0");var summaryInfo=new FileInfo(summaryPath);
        if(!summaryInfo.Exists||summaryInfo.Length>32*1024*1024)throw new InvalidDataException("No se encontró el fondo del resumen de movimientos Gen6.");
        var summary=new GARC.LazyGARC(File.ReadAllBytes(summaryPath));
        foreach(int index in new[]{2,3})
        {
            if(index>=summary.FileCount)continue;var package=new TitleDarcCodec(summary[index]);
            if(package.Entries.Any(e=>e.Name=="waza.bclyt"))return FromPackage(garc[11],summary[index]);
        }
        throw new InvalidDataException("Falta el resumen original de movimientos Gen6.");
    }
    internal static GameMoveDescription6Scene FromPackage(byte[] data,byte[]? summaryData=null)
    {
        var archive=new TitleDarcCodec(data);var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var file in archive.Entries.Where(e=>!e.Folder&&(e.Name.EndsWith(".bclyt")||e.Name.EndsWith(".bclim"))))files[Path.GetFileName(file.Name)]=archive.Read(file.Id);
        if(summaryData is null)throw new InvalidDataException("Falta el fondo original del resumen Gen6.");
        var summary=new TitleDarcCodec(summaryData);
        byte[] Read(string name)
        {
            var entry=summary.Entries.SingleOrDefault(e=>!e.Folder&&e.Name==name)??throw new InvalidDataException("Falta el asset original Gen6: "+name);
            return summary.Read(entry.Id);
        }
        return new(files,Read("back.bclim"),Read("waza.bclyt"));
    }
    private byte[] Get(string name)=>_files.TryGetValue(name,out var b)?b:throw new InvalidDataException("Falta el recurso de movimientos Gen6: "+name);
    private GameMoveDescription6Scene(Dictionary<string,byte[]> files,byte[] summaryTexture,byte[] summaryLayout)
    {
        _summaryTexture=summaryTexture;_summaryColors=[];
        Bounds(summaryLayout,0,20);if(!summaryLayout.AsSpan(0,4).SequenceEqual("CLYT"u8)||U(summaryLayout,12)!=summaryLayout.Length)throw new InvalidDataException("Fondo del resumen Gen6 incompatible.");
        for(int at=BitConverter.ToUInt16(summaryLayout,6);at<summaryLayout.Length;)
        {
            int size=U(summaryLayout,at+4);Bounds(summaryLayout,at,size);if(size<8)throw new InvalidDataException("Bloque de resumen Gen6 inválido.");
            if(Name(summaryLayout,at,4)=="pic1"&&size>=96&&Name(summaryLayout,at+12,16)=="22")_summaryColors=summaryLayout.AsSpan(at+76,16).ToArray();
            at+=size;
        }
        if(_summaryColors.Length!=16)throw new InvalidDataException("Faltan los colores originales del fondo Gen6.");
        _files=files;byte[] layout=Get("waza_info.bclyt");Bounds(layout,0,20);
        if(!layout.AsSpan(0,4).SequenceEqual("CLYT"u8)||BitConverter.ToUInt16(layout,4)!=0xFEFF||U(layout,12)!=layout.Length)throw new InvalidDataException("Diseño de movimientos Gen6 incompatible.");
        byte[]? materials=null;string[] textures=[];
        for(int at=BitConverter.ToUInt16(layout,6);at<layout.Length;)
        {
            int length=U(layout,at+4);Bounds(layout,at,length);if(length<8)throw new InvalidDataException("Bloque CLYT inválido.");string tag=Name(layout,at,4);byte[] b=layout.AsSpan(at,length).ToArray();
            if(tag=="txl1")
            {
                Bounds(b,8,4);int count=BitConverter.ToUInt16(b,8);Bounds(b,12,count*4);textures=new string[count];
                for(int i=0;i<count;i++){int start=checked(12+U(b,12+i*4));Bounds(b,start,1);int end=Array.IndexOf(b,(byte)0,start);if(end<0)throw new InvalidDataException("Nombre de textura Gen6 inválido.");textures[i]=Name(b,start,end-start);}
            }
            if(tag=="mat1")materials=b;
            if(tag is "txt1" or "pic1" or "wnd1")
            {
                Bounds(b,0,100);string name=Name(b,12,16);
                // Only audited root-level panes are composed; party/status controls and transitions are excluded.
                if((tag=="wnd1"&&(b[8]&1)!=0)||new[]{"1o","26","24","22","29","27","25","k","m","o","q","s","u","18","16","1a","1e","1g","1c","1i","14"}.Contains(name))
                {
                    double x=BitConverter.ToSingle(b,36),y=BitConverter.ToSingle(b,40),w=BitConverter.ToSingle(b,68),h=BitConverter.ToSingle(b,72);
                    if(!double.IsFinite(x+y+w+h)||w<=0||h<=0||w>400||h>240||b[9]!=4)throw new InvalidDataException("Geometría de la ficha Gen6 incompatible.");
                    if(tag=="wnd1"){Bounds(b,104,52);if(U(b,96)!=104||b[92]!=1||b.AsSpan(76,16).ToArray().Any(v=>v!=0))throw new InvalidDataException("Marco de ventana Gen6 incompatible.");}
                    _panes[name]=new(new Rect(160+x-w/2,120-y-h/2,w,h),b,tag=="wnd1"?104:76,tag=="wnd1"?120:92);
                    if(tag=="wnd1"||name is "1o" or "26" or "24" or "22" or "29" or "27" or "25")_layers.Add(name);
                }
            }
            at+=length;
        }
        // XY and ORAS use different pane IDs for the same three separator textures.
        foreach(var pair in new[]{("26","29"),("24","27"),("22","25")})
            if(!_panes.ContainsKey(pair.Item1)&&_panes.TryGetValue(pair.Item2,out var separator))_panes[pair.Item1]=separator;
        if(!_layers.Contains("1o")||!_layers.Any(n=>n=="1t"||n=="1w"))throw new InvalidDataException("Falta el cuadro de fondo de movimientos Gen6.");
        if(materials is null)throw new InvalidDataException("Faltan materiales de movimientos Gen6.");Bounds(materials,8,4);int materialCount=BitConverter.ToUInt16(materials,8);Bounds(materials,12,materialCount*4);
        foreach(string name in _layers)
        {
            if(!_panes.TryGetValue(name,out var pane))throw new InvalidDataException("Falta el fondo original Gen6: "+name);
            int index=BitConverter.ToUInt16(pane.Data,pane.MaterialOffset);if(index>=materialCount)throw new InvalidDataException("Material de fondo Gen6 inválido.");int m=U(materials,12+index*4);Bounds(materials,m,64);
            int texture=BitConverter.ToUInt16(materials,m+52);if(texture>=textures.Length)throw new InvalidDataException("Textura de fondo Gen6 inválida.");
            _materials[name]=(textures[texture],Color.FromArgb(materials[m+27],materials[m+24],materials[m+25],materials[m+26]));
        }
        foreach(string name in new[]{"k","m","o","q","s","u","18","16","1a","1e","1g","1c","1i","14"})if(!_panes.ContainsKey(name))throw new InvalidDataException("Falta un campo del detalle Gen6: "+name);
    }
    private BitmapSource Background()
    {
        if(_background is not null)return _background;
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            foreach(string name in _layers.Where(n=>n=="1o"||_materials[n].Texture is "line.bclim" or "black_win.bclim"))
            {
                var pane=_panes[name];var material=_materials[name];bool summary=name=="1o";
                using var bitmap=GameGraphicsCodec.Decode(summary?_summaryTexture:Get(material.Texture));using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;
                var decoded=new BitmapImage();decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.StreamSource=stream;decoded.EndInit();decoded.Freeze();
                var image=new FormatConvertedBitmap(decoded,PixelFormats.Bgra32,null,0);int w=image.PixelWidth,h=image.PixelHeight;byte[] pixels=new byte[w*h*4];image.CopyPixels(pixels,w*4,0);
                byte[] tint=summary?[255,255,255,255]:[material.Color.B,material.Color.G,material.Color.R,material.Color.A];
                byte[] colors=summary?_summaryColors:pane.Data.AsSpan(pane.ColorOffset,16).ToArray();
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int c=0;c<4;c++)
                {
                    int vc=c==0?2:c==2?0:c;double u=(double)x/Math.Max(1,w-1),v=(double)y/Math.Max(1,h-1);
                    double top=colors[vc]*(1-u)+colors[4+vc]*u,bottom=colors[8+vc]*(1-u)+colors[12+vc]*u;
                    double value=pixels[(y*w+x)*4+c]*tint[c]/255.0*(top*(1-v)+bottom*v)/255.0;if(c==3&&!summary)value=value*pane.Data[10]/255;
                    pixels[(y*w+x)*4+c]=(byte)value;
                }
                var source=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);source.Freeze();dc.DrawImage(source,pane.Area);
            }
        }
        var result=new RenderTargetBitmap(320,240,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return _background=result;
    }
    private bool Text(DrawingContext dc,GameDescriptionFont font,string name,string text,bool fit=false)
    {
        var pane=_panes[name];Bounds(pane.Data,0,116);double sx=BitConverter.ToSingle(pane.Data,100)/font.NominalWidth,sy=BitConverter.ToSingle(pane.Data,104)/font.NominalHeight;
        if(!double.IsFinite(sx+sy)||sx<=0||sy<=0||sx>4||sy>4)throw new InvalidDataException("Tamaño de texto Gen6 incompatible.");
        if(fit){double width=text.Sum(font.Advance)*sx;if(width>pane.Area.Width){double factor=pane.Area.Width/width;sx*=factor;sy*=factor;}}
        var color=Color.FromArgb(pane.Data[99],pane.Data[96],pane.Data[97],pane.Data[98]);return font.DrawText(dc,text,pane.Area,color,sx,sy);
    }
    internal DescriptionRender Render(GameDescriptionFont font,string description,MovePreviewExample move)
    {
        if(move.Type is <0 or >17||move.Category is <0 or >2)throw new InvalidDataException("Tipo o categoría de movimiento Gen6 inválido.");bool overflow;var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            dc.DrawImage(Background(),new Rect(0,0,320,240));
            Text(dc,font,"k",move.Name,true);Text(dc,font,"o","PP");Text(dc,font,"q",move.PP.ToString());Text(dc,font,"s","/");Text(dc,font,"u",move.PP.ToString());Text(dc,font,"18","Categoría");Text(dc,font,"1e","Potencia");Text(dc,font,"1c","Precisión");Text(dc,font,"1g",move.Power==0?"—":move.Power.ToString());Text(dc,font,"1i",move.Accuracy==0?"—":move.Accuracy.ToString());
            // The native type/category pictures are dynamic runtime replacements. Display their labels in the original panes.
            var type=_panes["m"].Area;double ts=Math.Min(.6,type.Width/Math.Max(1,move.TypeName.Sum(font.Advance)));font.DrawText(dc,move.TypeName,type,Colors.White,ts,.6);
            var category=_panes["16"].Area;string label=new[]{"Estado","Físico","Especial"}[move.Category];double cs=Math.Min(.6,category.Width/Math.Max(1,label.Sum(font.Advance)));font.DrawText(dc,label,category,Colors.White,cs,.6);
            overflow=Text(dc,font,"14",description.Length>10000?description[..10000]:description);
        }
        var result=new RenderTargetBitmap(320,240,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();
        return new(result,(description.Length>10000?"Vista limitada a los primeros 10.000 caracteres. ":"")+(overflow?"La descripción supera el cuadro o contiene caracteres sin representación. Revisá los saltos de línea.":"La descripción cabe en el cuadro del diseño."));
    }
}
