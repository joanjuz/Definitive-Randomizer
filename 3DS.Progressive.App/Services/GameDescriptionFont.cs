using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ThreeDS.Progressive.Services;

// Read-only CFNT/FFNT font subset: the A4/A8 sheets used by the Gen6/7 Latin fonts.
internal sealed class GameDescriptionFont
{
    private readonly byte[] _data;
    private readonly Dictionary<char,int> _map=[];
    private readonly Dictionary<int,(int Left,int Width,int Advance)> _widths=[];
    private readonly Dictionary<(int,uint),BitmapSource> _glyphs=[];
    private readonly int _cellWidth,_cellHeight,_sheetCount,_sheetSize,_format,_columns,_rows,_sheetWidth,_sheetHeight,_sheetData;
    internal int LineFeed {get;}
    internal int NominalWidth {get;}
    internal int NominalHeight {get;}
    internal string Resource {get;}
    private int U16(int at){Bounds(at,2);return BitConverter.ToUInt16(_data,at);}
    private int I32(int at){Bounds(at,4);int value=BitConverter.ToInt32(_data,at);if(value<0)throw new InvalidDataException("Puntero de fuente inválido.");return value;}
    private void Bounds(int at,int count){if(at<0||count<0||(long)at+count>_data.Length)throw new InvalidDataException("Fuente truncada.");}
    private void Magic(int at,string magic){Bounds(at,4);if(System.Text.Encoding.ASCII.GetString(_data,at,4)!=magic)throw new InvalidDataException("Bloque de fuente incompatible.");}
    internal static GameDescriptionFont Load(string romFs,int generation,bool oras)
    {
        string relative=generation==7?"a/0/2/5":oras?"a/1/6/7":"a/1/8/5";
        string path=Path.Combine(romFs,relative);var info=new FileInfo(path);
        if(!info.Exists||info.Length>32*1024*1024)throw new InvalidDataException("No se encontró un recurso de fuente compatible en esta ROM.");
        return FromArchive(File.ReadAllBytes(path),relative);
    }
    internal static GameDescriptionFont FromArchive(byte[] archive,string resource)
    {
        for(int i=0;i<=Math.Min(archive.Length-20,65536);i++)
        {
            bool cf=archive.AsSpan(i,4).SequenceEqual("CFNT"u8),ff=archive.AsSpan(i,4).SequenceEqual("FFNT"u8);
            if(!cf&&!ff)continue;
            int length=BitConverter.ToInt32(archive,i+12);
            if(length<84||length>16*1024*1024||(long)i+length>archive.Length)continue;
            return new GameDescriptionFont(archive.AsSpan(i,length).ToArray(),resource,ff);
        }
        throw new InvalidDataException("La fuente del proyecto usa un formato que todavía no se puede previsualizar.");
    }
    private GameDescriptionFont(byte[] data,string resource,bool ff)
    {
        _data=data;Resource=resource;if(U16(4)!=0xFEFF||U16(6)!=20)throw new InvalidDataException("Encabezado de fuente incompatible.");
        Magic(20,"FINF");NominalWidth=ff?data[30]:data[49];NominalHeight=ff?data[29]:data[48];
        if(NominalWidth<1||NominalHeight<1)throw new InvalidDataException("Tamaño nominal de fuente inválido.");LineFeed=ff?U16(32):data[29];
        int pointer=ff?40:36,t=I32(pointer)-8;Magic(t,"TGLP");Bounds(t,32);
        _cellWidth=data[t+8];_cellHeight=data[t+9];_sheetSize=I32(t+12);_sheetCount=ff?data[t+10]:U16(t+16);_format=U16(t+18);_columns=U16(t+20);_rows=U16(t+22);_sheetWidth=U16(t+24);_sheetHeight=U16(t+26);_sheetData=I32(t+28);
        if(_cellWidth<1||_cellHeight<1||_sheetCount<1||_columns<1||_rows<1||LineFeed<1||_sheetWidth>2048||_sheetHeight>2048||(_sheetWidth&(_sheetWidth-1))!=0||(_sheetHeight&(_sheetHeight-1))!=0||_sheetWidth<8||_sheetHeight<8||_format is not (8 or 11)||_sheetSize!=(long)_sheetWidth*_sheetHeight/(_format==11?2:1)||(_cellWidth+1)*_columns>_sheetWidth||(_cellHeight+1)*_rows>_sheetHeight)
            throw new InvalidDataException("Geometría o textura de fuente incompatible.");
        Bounds(_sheetData,checked(_sheetCount*_sheetSize));
        var seen=new HashSet<int>();int block=I32(pointer+4)-8;
        while(block>=0)
        {
            if(!seen.Add(block)||seen.Count>128)throw new InvalidDataException("Cadena de anchos de fuente inválida.");Magic(block,"CWDH");int start=U16(block+8),end=U16(block+10);if(end<start)throw new InvalidDataException("Anchos de fuente inválidos.");Bounds(block+16,(end-start+1)*3);
            for(int g=start;g<=end;g++){int at=block+16+(g-start)*3;_widths[g]=(unchecked((sbyte)data[at]),data[at+1],data[at+2]);}
            int next=I32(block+12);if(next==0)break;block=next-8;
        }
        seen.Clear();block=I32(pointer+8)-8;
        while(block>=0)
        {
            if(!seen.Add(block)||seen.Count>256)throw new InvalidDataException("Mapa de fuente inválido.");Magic(block,"CMAP");int first=U16(block+8),last=U16(block+10),method=U16(block+12),at=block+20;
            if(last<first)throw new InvalidDataException("Rango de caracteres inválido.");
            if(method==0){int firstGlyph=U16(at);for(int c=first;c<=last;c++)_map[(char)c]=firstGlyph+c-first;}
            else if(method==1){Bounds(at,(last-first+1)*2);for(int c=first;c<=last;c++)_map[(char)c]=U16(at+(c-first)*2);}
            else if(method==2){int count=U16(at);Bounds(at+2,count*4);for(int n=0;n<count;n++)_map[(char)U16(at+2+n*4)]=U16(at+4+n*4);}
            else throw new InvalidDataException("Codificación de fuente incompatible.");
            int next=I32(block+16);if(next==0)break;block=next-8;
        }
    }
    internal bool Contains(char c)=>_map.TryGetValue(c,out int glyph)&&_widths.ContainsKey(glyph)&&glyph<_sheetCount*_columns*_rows;
    internal double Advance(char c)=>Contains(c)?_widths[_map[c]].Advance:LineFeed*.6;
    private BitmapSource Glyph(int glyph,System.Windows.Media.Color? tint=null)
    {
        var color=tint??System.Windows.Media.Colors.Black;uint key=(uint)(color.A<<24|color.R<<16|color.G<<8|color.B);
        if(_glyphs.TryGetValue((glyph,key),out var cached))return cached;
        int perSheet=_columns*_rows,sheet=glyph/perSheet,cell=glyph%perSheet;
        int ox=cell%_columns*(_cellWidth+1)+1,oy=cell/_columns*(_cellHeight+1)+1;
        byte[] pixels=new byte[_cellWidth*_cellHeight*4];
        for(int y=0;y<_cellHeight;y++)for(int x=0;x<_cellWidth;x++)
        {
            int sx=ox+x,sy=oy+y,morton=0;for(int bit=0;bit<3;bit++)morton|=((sx>>bit)&1)<<(bit*2)|((sy>>bit)&1)<<(bit*2+1);
            int index=((sy/8)*(_sheetWidth/8)+sx/8)*64+morton;
            int alpha=_format==11?((_data[_sheetData+sheet*_sheetSize+index/2]>>((index%2)*4))&15)*17:_data[_sheetData+sheet*_sheetSize+index];
            int at=(y*_cellWidth+x)*4;pixels[at]=color.B;pixels[at+1]=color.G;pixels[at+2]=color.R;pixels[at+3]=(byte)(alpha*color.A/255);
        }
        var image=BitmapSource.Create(_cellWidth,_cellHeight,96,96,PixelFormats.Bgra32,null,pixels,_cellWidth*4);image.Freeze();_glyphs[(glyph,key)]=image;return image;
    }
    internal bool DrawText(DrawingContext dc,string text,Rect area,System.Windows.Media.Color color,double scale=1,double? verticalScale=null)
    {
        double sy=verticalScale??scale;double x=area.X,y=area.Y,max=0;bool missing=false;dc.PushClip(new RectangleGeometry(area));
        foreach(char c in text.Replace("\r",""))
        {
            if(c=='\n'){max=Math.Max(max,x-area.X);x=area.X;y+=LineFeed*sy;continue;}
            if(Contains(c)){int glyph=_map[c];dc.DrawImage(Glyph(glyph,color),new Rect(x+_widths[glyph].Left*scale,y,_cellWidth*scale,_cellHeight*sy));}
            else missing=true;
            x+=Advance(c)*scale;
        }
        dc.Pop();return missing||Math.Max(max,x-area.X)>area.Width||y+_cellHeight*sy>area.Bottom;
    }
    internal DescriptionRender Render(string text,int generation)
    {
        bool truncated=text.Length>10000;string sample=truncated?text[..10000]:text;
        const int width=320,height=112;double scale=generation==6?.60:1;double x=10,y=10,max=0;int lines=1;var missing=new HashSet<char>();
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.WhiteSmoke,null,new Rect(0,0,width,height));
            foreach(char c in sample.Replace("\r", ""))
            {
                if(c=='\n'){max=Math.Max(max,x-10);x=10;y+=LineFeed*scale;lines++;continue;}
                if(Contains(c)){int glyph=_map[c];dc.DrawImage(Glyph(glyph),new Rect(x+_widths[glyph].Left*scale,y,_cellWidth*scale,_cellHeight*scale));}
                else {missing.Add(c);dc.DrawRectangle(null,new System.Windows.Media.Pen(System.Windows.Media.Brushes.OrangeRed,1),new Rect(x,y,Advance(c)*scale,LineFeed*scale));}
                x+=Advance(c)*scale;
            }
        }
        max=Math.Max(max,x-10);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        string warning=max>300||y+_cellHeight*scale>102?"El texto supera el área de referencia. Revisá los saltos de línea.":"El texto cabe en el área de referencia.";
        if(truncated)warning+=" La vista previa se limita a los primeros 10.000 caracteres; el texto guardado no se recorta.";
        if(missing.Count>0)warning+=" Caracteres sin representación: "+new string(missing.Order().ToArray());
        return new(bitmap,$"{lines} línea(s). "+warning);
    }
}
internal sealed record DescriptionRender(BitmapSource Image,string Status);
