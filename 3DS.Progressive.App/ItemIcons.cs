using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThreeDS.Progressive;

/// <summary>Original supplied artwork. Crops retain alpha and are cached, never fetched online.</summary>
internal static class ItemIcons
{
    private static readonly BitmapSource Atlas=Load();
    private static readonly Dictionary<string,BitmapSource> Cache=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,Int32Rect> Regions=new(StringComparer.Ordinal)
    {
        ["Dna"]=new(0,0,270,310),["Raid"]=new(270,0,280,310),["MegaRaid"]=new(550,0,290,310),["Atom"]=new(840,0,280,310),["Charmander"]=new(1120,0,380,310),
        ["Bag"]=new(0,335,320,255),["Candy"]=new(320,335,140,255),["CandyXL"]=new(460,335,240,255),["RareCandy"]=new(700,335,230,255),["Radar"]=new(930,335,290,255),["Pass"]=new(1220,335,280,255),
        ["Stardust"]=new(0,620,220,260),["Scroll"]=new(220,620,260,260),["Ticket"]=new(480,620,310,260),["FastPass"]=new(790,620,260,260),["Incense"]=new(1050,620,230,260),["Incubator"]=new(1280,620,220,260),
        ["Trade"]=new(0,900,310,240),["LuckyTrade"]=new(310,900,290,240),["Gengar"]=new(600,900,250,240),["Research"]=new(850,900,300,240),["SpecialResearch"]=new(1150,900,350,240),
        ["Pokeball"]=new(0,1140,300,300),["Avatar"]=new(300,1140,275,300),["Trainers"]=new(575,1140,325,300),["Community"]=new(900,1140,310,300),["Group"]=new(1210,1140,290,300),
        ["Bug"]=new(0,1460,300,290),["Mystery"]=new(300,1460,260,290),["League"]=new(560,1460,220,290),["BallRing"]=new(780,1460,220,290),["BallCircle"]=new(1000,1460,230,290),["Friendship"]=new(1230,1460,270,290)
    };
    internal static IReadOnlyCollection<string> Keys=>Regions.Keys;
    private static BitmapSource Load()
    {
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri("pack://application:,,,/3DS.Progressive;component/Assets/ItemIcons.png");bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    internal static BitmapSource Get(string key)
    {
        lock(Cache)
        {
            if(Cache.TryGetValue(key,out var cached))return cached;
            var region=Regions[key];var crop=new CroppedBitmap(Atlas,region);var pixels=new byte[region.Width*region.Height*4];
            var rgba=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);rgba.CopyPixels(pixels,region.Width*4,0);
            int left=region.Width,top=region.Height,right=-1,bottom=-1;
            for(int y=0;y<region.Height;y++)for(int x=0;x<region.Width;x++)if(pixels[(y*region.Width+x)*4+3]>16){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            if(right<left)throw new InvalidOperationException("El icono no contiene píxeles: "+key);
            var result=new CroppedBitmap(Atlas,new(region.X+left,region.Y+top,right-left+1,bottom-top+1));result.Freeze();Cache[key]=result;return result;
        }
    }
    internal static string KeyForLabel(string label)
    {
        label=label.ToLower(CultureInfo.InvariantCulture);
        if(label.Contains("batch"))return "Group";
        if(label.Contains("global template"))return "Community";
        if(label.Contains("templates"))return "Scroll";
        if(label.Contains("historial"))return "Research";
        if(label.Contains("story text"))return "SpecialResearch";
        if(label.Contains("game text"))return "Research";
        if(label.Contains("tiendas"))return "Bag";
        if(label.Contains("field items"))return "Incense";
        if(label.Contains("objetos"))return "Bag";
        if(label.Contains("evoluciones"))return "Dna";
        if(label.Contains("estáticos"))return "Mystery";
        if(label.Contains("encuentros"))return "Research";
        if(label.Contains("entrenadores"))return "Trainers";
        if(label.Contains("level up"))return "Candy";
        if(label.Contains("movimientos"))return "Raid";
        if(label.Contains("mt y"))return "Scroll";
        if(label.Contains("mecánicas"))return "Atom";
        if(label.Contains("herramientas pokémon"))return "RareCandy";
        if(label.Contains("herramientas rom"))return "Ticket";
        if(label.Contains("automatización"))return "FastPass";
        if(label.Contains("opciones"))return "Radar";
        if(label.Contains("guiado")||label.Contains("guía")||label.Contains("ayuda"))return "SpecialResearch";
        if(label.Contains("completado"))return "Friendship";
        if(label.Contains("aviso"))return "Mystery";
        if(label.Contains("modo"))return "Avatar";
        return "Pokeball";
    }
    internal static BitmapSource ForLabel(string label)=>Get(KeyForLabel(label));
}