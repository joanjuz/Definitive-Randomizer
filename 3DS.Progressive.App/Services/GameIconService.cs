using System.IO;
using System.Text;
using System.Drawing;
using System.Security.Cryptography;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed class GameIconService
{
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _source,_draft;
    internal bool Dirty=>!_source.SequenceEqual(_draft);
    internal GameIconService(GameProjectSession project)
    {
        _project=project;
        var paths=Directory.GetFiles(project.GameConfiguration.ExeFS).Where(path=>Path.GetFileName(path).Equals("icon.bin",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(paths.Length!=1)throw new InvalidDataException("Falta un icon.bin SMDH único en ExeFS.");
        _path=paths[0];_source=File.ReadAllBytes(_path);Validate(_source);_draft=(byte[])_source.Clone();
    }
    private static void Validate(byte[] data)
    {if(data.Length!=0x36C0||!data.AsSpan(0,4).SequenceEqual("SMDH"u8))throw new InvalidDataException("El archivo debe ser un SMDH de 0x36C0 bytes.");}
    private static int Offset(int language,int field)
    {if(language is <0 or >15||field is <0 or >2)throw new ArgumentOutOfRangeException();return 8+language*0x200+(field==0?0:field==1?0x80:0x180);}
    internal string Read(int language,int field)=>Encoding.Unicode.GetString(_draft,Offset(language,field),field==1?0x100:0x80).TrimEnd('\0');
    internal void Edit(int language,int field,string text)
    {
        int offset=Offset(language,field),size=field==1?0x100:0x80;
        if(text.Contains('\0')||Encoding.Unicode.GetByteCount(text)>size)throw new ArgumentException($"El campo admite {size/2} unidades UTF-16 y no admite caracteres NUL.");
        byte[] encoded=new UnicodeEncoding(false,false,true).GetBytes(text);var span=_draft.AsSpan(offset,size);span.Clear();encoded.CopyTo(span);
    }
    // RGB565 tiles as documented in https://3dbrew.org/wiki/SMDH#Icon_graphics.
    private static int PixelOffset(int size,int x,int y)
    {
        int morton=0;for(int bit=0;bit<3;bit++){morton|=((x>>bit)&1)<<(2*bit);morton|=((y>>bit)&1)<<(2*bit+1);}
        return 2*(((y/8)*(size/8)+x/8)*64+morton);
    }
    internal Bitmap Image(bool large)
    {
        int size=large?48:24,start=large?0x24C0:0x2040;var bitmap=new Bitmap(size,size);
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {int at=start+PixelOffset(size,x,y);int value=_draft[at]|_draft[at+1]<<8;int r=(value>>11)&31,g=(value>>5)&63,b=value&31;
         bitmap.SetPixel(x,y,Color.FromArgb((r<<3)|(r>>2),(g<<2)|(g>>4),(b<<3)|(b>>2)));}
        return bitmap;
    }
    internal void ReplaceImage(bool large,Bitmap image)
    {
        int size=large?48:24,start=large?0x24C0:0x2040;if(image.Width!=size||image.Height!=size)throw new ArgumentException($"La imagen debe medir {size} × {size} píxeles.");
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {var color=image.GetPixel(x,y);int value=((color.R>>3)<<11)|((color.G>>2)<<5)|(color.B>>3);int at=start+PixelOffset(size,x,y);_draft[at]=(byte)value;_draft[at+1]=(byte)(value>>8);}
    }
    internal void Import(byte[] smdh){Validate(smdh);_draft=(byte[])smdh.Clone();}
    internal byte[] Export()=>(byte[])_draft.Clone();
    internal void Discard()=>_draft=(byte[])_source.Clone();
    internal void Apply()
    {
        if(!Dirty)throw new InvalidOperationException("No hay cambios de SMDH.");
        GarcTransaction.ReadUnchanged(_path,SHA256.HashData(_source));
        var change=new FileChange(_path,_source,_draft);var rows=new List<BatchChange>();
        for(int language=0;language<16;language++)for(int field=0;field<3;field++)
        {int offset=Offset(language,field),size=field==1?0x100:0x80;if(!_source.AsSpan(offset,size).SequenceEqual(_draft.AsSpan(offset,size)))rows.Add(new(language,"Idioma "+language,new[]{"Título corto","Descripción","Editor"}[field],Encoding.Unicode.GetString(_source,offset,size).TrimEnd('\0'),Read(language,field)));}
        rows.Add(new(-1,"icon.bin","SHA-256",Convert.ToHexString(change.Fingerprint),Convert.ToHexString(SHA256.HashData(_draft))));
        _project.History.Commit([change],"SMDH","Guardar icono y metadatos",rows,new Dictionary<string,string>{{"Scope","SMDH"}});
        _source=(byte[])_draft.Clone();
    }
}
