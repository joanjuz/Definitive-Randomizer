using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;

/// <summary>Shared story banks reviewed locally; never extrapolate an index between games.</summary>
internal static class KnownStoryTextCatalogue
{
    internal static readonly string[] Categories=["Escenas y reacciones","Legendarios y eventos especiales","Bayas y cultivos","Interacciones con Pokémon","Pruebas y Cristales Z","Entrenamiento Extremo","Equipo Rainbow Rocket","Ultraviaje dimensional","Entrenamiento y experiencia"];
    private sealed record Reviewed(string Game,int File,int BankCount,int Lines,string EnglishSha256,string Title,string Category);
    private sealed record Data(int Format,Reviewed[] Matches);
    private static readonly Lazy<Reviewed[]> ReviewedEntries=new(()=>
    {
        using var stream=typeof(KnownStoryTextCatalogue).Assembly.GetManifestResourceStream("ThreeDS.Progressive.story-reviewed-metadata.json");
        if(stream is null)return [];
        var data=JsonSerializer.Deserialize<Data>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        return data is {Format:1}?data.Matches:[];
    });
    internal static Dictionary<int,KnownTextPurpose> ReadReviewed(GameConfig config,IReadOnlyList<int> localCounts)
    {
        var candidates=ReviewedEntries.Value.Where(r=>r.Game==config.Version.ToString()&&r.BankCount==localCounts.Count).ToArray();
        if(candidates.Length==0)return [];
        string path=Path.Combine(config.RomFS,config.GetGARCReference("storytext").GetRelativeGARC(2).Reference);
        if(!File.Exists(path))return [];
        try
        {
            var records=new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            if(records.Length!=localCounts.Count)return [];
            var result=new Dictionary<int,KnownTextPurpose>();
            foreach(var entry in candidates)
            {
                if(entry.File<0||entry.File>=records.Length||localCounts[entry.File]!=entry.Lines)continue;
                var lines=new TextFile(config,records[entry.File]).Lines;
                if(lines.Length!=entry.Lines)continue;
                if(MatchesEnglishFile(lines,entry.EnglishSha256))result[entry.File]=new(entry.Title,entry.Category);
            }
            return result;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException or FormatException){return [];}
    }
    internal static bool MatchesEnglishFile(IReadOnlyList<string> lines,string expected) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0",lines.Select(TextContentClassifier.Normalize)))))==expected;
    internal static KnownTextPurpose? Find(string game,int count,int id)
    {
        if(game=="XY"&&count==424)return id switch
        {
            1=>new("Propinas","Compras y servicios"),
            59=>new("Combates Aéreos","Combates y desafíos"),
            61=>new("Combate Inverso","Combates y desafíos"),
            92 or 93 or 94=>new("Tutores de movimientos","Movimientos y habilidades"),
            114=>new("Entregas de Pokémon Global Link","Regalo Misterioso"),
            115=>new("Inspector de Motes","Compras y servicios"),
            116=>new("Relato de Kalos","Relatos y leyendas"),
            118=>new("Restauración de fósiles","Fósiles y formas"),
            119=>new("Zumos de Baya","Compras y servicios"),
            120=>new("Juez de características","Compras y servicios"),
            130=>new("Uso de Golpe Roca","Exploración y obstáculos"),
            131=>new("Evaluación de Poder Oculto","Compras y servicios"),
            145=>new("Ascensores","Exploración y obstáculos"),
            148=>new("Gimnasios y desplazamientos","Lugares y mapas"),
            150=>new("Celebración y Emblema de Kalos","Historia principal"),
            151=>new("Relato del hombre y su Pokémon","Relatos y leyendas"),
            220=>new("Aparatos eléctricos de Rotom","Fósiles y formas"),
            221=>new("Meteorito de Deoxys","Fósiles y formas"),
            395 or 396 or 398=>new("Trabajos del Hotel Ricachilton","Misiones secundarias"),
            422=>new("Mr. Buen Rollo","Poderes O"),
            _=>null
        };
        return null;
    }
}
