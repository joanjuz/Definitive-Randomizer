using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

internal static class StoryQuoteCatalogue
{
    private sealed record Quote(string Game,int File,int Line,int Lines,int BankCount,string EnglishSha256,string Speaker,string Context,string Source);
    private sealed record Catalogue(int Format,Quote[] Matches);
    private static readonly Lazy<Quote[]> Quotes=new(()=>
    {
        using var stream=typeof(StoryQuoteCatalogue).Assembly.GetManifestResourceStream("ThreeDS.Progressive.story-quote-metadata.json");
        if(stream is null)return [];
        var data=JsonSerializer.Deserialize<Catalogue>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        return data is {Format:1}?data.Matches:[];
    });
    internal static Dictionary<int,TextContentEvidence[]> Read(GameConfig config,int bankCount,IReadOnlyList<int> localCounts)
    {
        var candidates=Quotes.Value.Where(q=>q.Game==config.Version.ToString()&&q.BankCount==bankCount).ToArray();
        if(candidates.Length==0)return [];
        string path=Path.Combine(config.RomFS,config.GetGARCReference("storytext").GetRelativeGARC(2).Reference);
        if(!File.Exists(path))return [];
        try
        {
            var records=new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            if(records.Length!=bankCount)return [];
            var translate=ContextTranslator(config);
            var lines=new Dictionary<int,string[]>();
            var result=new Dictionary<int,List<TextContentEvidence>>();
            var hashes=new Dictionary<(int,int),string>();
            foreach(var q in candidates)
            {
                if(q.File<0||q.File>=records.Length||localCounts[q.File]!=q.Lines)continue;
                if(!lines.TryGetValue(q.File,out var file))lines[q.File]=file=new TextFile(config,records[q.File]).Lines;
                if(file.Length!=q.Lines||q.Line<0||q.Line>=file.Length)continue;
                if(!hashes.TryGetValue((q.File,q.Line),out var hash))
                    hashes[(q.File,q.Line)]=hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TextContentClassifier.Normalize(file[q.Line]))));
                if(hash!=q.EnglishSha256)continue;
                if(!result.TryGetValue(q.File,out var evidence))result[q.File]=evidence=[];
                evidence.Add(new("Personaje: "+q.Speaker,q.Line,string.IsNullOrWhiteSpace(q.Context)?q.Speaker:q.Speaker+" · "+translate(q.Context),q.Source));
            }
            return result.ToDictionary(p=>p.Key,p=>p.Value.DistinctBy(e=>(e.Category,e.Match)).ToArray());
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException or FormatException)
        {
            // An absent/modified English reference must not prevent text editing.
            return [];
        }
    }
    private static Func<string,string> ContextTranslator(GameConfig config)
    {
        try
        {
            int id=config.GameText.First(t=>t.Name==TextName.metlist_000000).Index;
            string path=Path.Combine(config.RomFS,config.GetGARCReference("gametext").GetRelativeGARC(2).Reference);
            var records=new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            var english=new TextFile(config,records[id]).Lines;
            var localized=config.GetText(TextName.metlist_000000);
            var pairs=Enumerable.Range(0,Math.Min(english.Length,localized.Length))
                .Where(i=>english[i].Length>3&&!string.IsNullOrWhiteSpace(localized[i])&&english[i]!=localized[i])
                .Select(i=>(English:english[i],Local:localized[i])).OrderByDescending(p=>p.English.Length).ToArray();
            var cache=new Dictionary<string,string>(StringComparer.Ordinal);
            return context=>
            {
                if(cache.TryGetValue(context,out var cached))return cached;
                string original=context;
                foreach(var pair in pairs)
                    if(context.Contains(pair.English,StringComparison.OrdinalIgnoreCase))
                    context=Regex.Replace(context,@"(?<![\p{L}\p{N}])"+Regex.Escape(pair.English)+@"(?![\p{L}\p{N}])",_=>pair.Local,RegexOptions.IgnoreCase);
                cache[original]=context;return context;
            };
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException or InvalidOperationException or FormatException){return text=>text;}
    }

}
