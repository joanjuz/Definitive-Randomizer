using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal sealed record TitleArchive(int Id, string Label, string Description="");
internal sealed class GameTitleScreenService
{
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _source;
    private GARC.LazyGARC _garc;
    private readonly Dictionary<int, byte[]> _edited = [];
    internal IReadOnlyList<TitleArchive> Archives { get; }
    internal bool Gen7=>_project.Generation==7;
    internal string ArchivePath=>_path;
    internal bool Dirty => _edited.Count != 0;
    internal GameTitleScreenService(GameProjectSession project)
    {
        _project = project;
        _path = Path.Combine(project.GameConfiguration.RomFS, project.Generation==7?"a/0/9/9":project.GameConfiguration.ORAS ? "a/1/5/2" : "a/1/6/5");
        if (new FileInfo(_path).Length > 512 * 1024 * 1024) throw new InvalidDataException("El GARC de títulos supera 512 MiB.");
        _source = File.ReadAllBytes(_path); ValidateSimpleRecords(_source); _garc = new(_source);
        if(Gen7)
        {
            if(_garc.FileCount<27)throw new InvalidDataException("El GARC no contiene los 18 recursos de título Gen7.");
            Archives=Enumerable.Range(9,18).Select(i=>
            {
                var files=new TitleAlytCodec(_garc[i]).Entries.Where(e=>!e.Folder).ToArray();
                int images=files.Count(e=>e.Name.EndsWith(".bflim",StringComparison.OrdinalIgnoreCase));
                string layout=files.FirstOrDefault(e=>e.Name.EndsWith(".bflyt",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(e.Name).StartsWith("Title",StringComparison.OrdinalIgnoreCase))?.Name??"";
                var match=System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(layout),@"^Title(Moon|Sun)_Main_Up_000(?:_([a-z]{2}))?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                string identity=$"{project.GameName} · paquete gráfico";
                if(match.Success)
                {
                    string game=match.Groups[1].Value.Equals("Moon",StringComparison.OrdinalIgnoreCase)?"Luna":"Sol";
                    if(project.GameConfiguration.USUM)game="Ultra "+game;
                    string language=match.Groups[2].Value.ToLowerInvariant() switch {"es"=>"Español","de"=>"Alemán","fr"=>"Francés","it"=>"Italiano","kr"=>"Coreano","cn"=>"Chino simplificado","tw"=>"Chino tradicional","us"=>"Inglés (US)",""=>"Original sin sufijo de idioma",var code=>"Idioma "+code};
                    identity=game+" · "+language+(project.GameConfiguration.USUM?" · logo 2D (inicio de partida)":" · logo y efectos del título");
                }
                string description=$"{identity}. Layout: {layout}. Paquete ALYT/SARC #{i}: {images} texturas BFLIM, {files.Count(e=>e.Name.EndsWith(".bflyt",StringComparison.OrdinalIgnoreCase))} diseños BFLYT y {files.Count(e=>e.Name.EndsWith(".bflan",StringComparison.OrdinalIgnoreCase))} animaciones BFLAN. Archivos: "+string.Join(", ",files.Select(e=>e.Name))+". Se editan las texturas; los diseños, animaciones y demás archivos se conservan."+(project.GameConfiguration.USUM?" En USUM se verificó este logo al iniciar una partida nueva. Para la secuencia de acceso a una partida guardada usá Animaciones y vídeos MOFLEX.":"");
                return new TitleArchive(i,$"{identity} · #{i} · {images} texturas",description);
            }).ToArray();
            foreach(var archive in Archives)if(!new TitleAlytCodec(_garc[archive.Id]).Entries.Any(e=>e.Name.EndsWith(".bflim",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Recurso de título Gen7 sin imágenes BFLIM.");
            return;
        }
        string[] languages = project.GameConfiguration.ORAS ? ["Sin sufijo · logo japonés completo", "Alemán", "Español", "Francés", "Italiano", "Japonés", "Coreano", "Inglés"] : ["DE", "ES", "FR", "IT", "JP", "KO", "EN"];
        string[] games = project.GameConfiguration.ORAS ? ["OR", "AS"] : ["X", "Y"];
        int first = project.GameConfiguration.ORAS ? 1120 : 467;
        if (_garc.FileCount < first + languages.Length * 2) throw new InvalidDataException("El GARC no contiene las pantallas de título vanilla esperadas.");
        Archives = Enumerable.Range(0, languages.Length * 2).Select(i =>
        {
            int id=first+i;var entries=new TitleDarcCodec(_garc[id]).Entries.Where(e=>!e.Folder).ToArray();
            string identity=games[i/languages.Length]+" · "+languages[i%languages.Length];
            string[] layouts=entries.Where(e=>e.Name.EndsWith(".bclyt",StringComparison.OrdinalIgnoreCase)).Select(e=>e.Name).ToArray();
            string[] animations=entries.Where(e=>e.Name.EndsWith(".bclan",StringComparison.OrdinalIgnoreCase)).Select(e=>e.Name).ToArray();
            string[] images=entries.Where(e=>e.Name.EndsWith(".bclim",StringComparison.OrdinalIgnoreCase)).Select(e=>e.Name).ToArray();
            string phases=animations.Any(n=>n.Contains("_logo_in",StringComparison.OrdinalIgnoreCase))?" Incluye entrada del logo (logo_in), transición de Start (start_anime) y bucle del título (title_loop).":"";
            string description=$"{identity}. GARC: {(project.GameConfiguration.ORAS?"a/1/5/2":"a/1/6/5")}, registro #{id}. Diseños: {string.Join(", ",layouts)}. Animaciones: {string.Join(", ",animations)}. Imágenes BCLIM: {string.Join(", ",images)}.{phases} Se editan las imágenes; los diseños y las animaciones se conservan.";
            return new TitleArchive(id,$"{identity} · #{id} · {images.Length} texturas",description);
        }).ToArray();
        if(project.GameConfiguration.ORAS)
        {
            const int id=1119;
            var entries=new TitleDarcCodec(_garc[id]).Entries.Where(e=>!e.Folder).ToArray();
            string[] images=entries.Where(e=>e.Name.EndsWith(".bclim",StringComparison.OrdinalIgnoreCase)).Select(e=>e.Name).ToArray();
            if(!images.Contains("gf_logo.bclim")||!images.Contains("presented_by.bclim"))
                throw new InvalidDataException("El recurso de presentación ORAS no contiene las imágenes GAME FREAK esperadas.");
            Archives=Archives.Append(new TitleArchive(id,"ORAS · Presentación GAME FREAK · #1119 · "+images.Length+" texturas",
                "Presentación del bosque: texto Presented by y logo GAME FREAK. Compartida por Rubí Omega y Zafiro Alfa. GARC a/1/5/2, registro #1119. Imágenes BCLIM: "+string.Join(", ",images)+". Se conservan los diseños, la animación y los demás recursos; el fondo del bosque es independiente.")).ToArray();
        }
    }
    private static void ValidateSimpleRecords(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        if(bytes.Length<28 || reader.ReadUInt32()!=0x47415243)throw new InvalidDataException("GARC de títulos inválido.");
        int header=reader.ReadInt32();reader.ReadUInt16();ushort version=reader.ReadUInt16();
        if(header!=(version==0x400?28:version==0x600?36:0))throw new InvalidDataException("Versión GARC incompatible.");
        reader.BaseStream.Position=header;
        if(reader.ReadUInt32()!=0x4641544F)throw new InvalidDataException("FATO de títulos inválida.");
        int fato=reader.ReadInt32(),count=reader.ReadUInt16();
        if(fato!=12+count*4 || (long)header+fato+12L+count*16L>bytes.Length)throw new InvalidDataException("Tabla GARC incompatible.");
        reader.BaseStream.Position=header+fato;
        if(reader.ReadUInt32()!=0x46415442 || reader.ReadInt32()!=12+count*16 || reader.ReadInt32()!=count)throw new InvalidDataException("FATB de títulos incompatible.");
        for(int i=0;i<count;i++){if(reader.ReadUInt32()!=1)throw new InvalidDataException("El GARC de títulos contiene carpetas o subarchivos no soportados; no se guardará.");reader.BaseStream.Position+=12;}
    }
    private byte[] Record(int archive)
    {
        if (!Archives.Any(a => a.Id == archive)) throw new ArgumentException("Pantalla de título inválida.");
        return _edited.TryGetValue(archive, out var record) ? record : _garc[archive];
    }
    internal IReadOnlyList<TitleDarcEntry> Images(int archive) => (Gen7?new TitleAlytCodec(Record(archive)).Entries:new TitleDarcCodec(Record(archive)).Entries).Where(e => !e.Folder && e.Name.EndsWith(Gen7?".bflim":".bclim", StringComparison.OrdinalIgnoreCase)).ToArray();
    internal byte[] Read(int archive, int image)
    {
        if (!Images(archive).Any(e => e.Id == image)) throw new ArgumentException("Imagen de título inválida.");
        return Gen7?new TitleAlytCodec(Record(archive)).Read(image):new TitleDarcCodec(Record(archive)).Read(image);
    }
    internal void Replace(int archive, int image, byte[] bclim)
    {
        using var original = GameGraphicsCodec.Decode(Read(archive, image));
        if (bclim.Length < 40 || !bclim.AsSpan(bclim.Length - 40, 4).SequenceEqual(Gen7?"FLIM"u8:"CLIM"u8)) throw new InvalidDataException("El título requiere el mismo contenedor gráfico que el original (BCLIM Gen6 / BFLIM Gen7).");
        using var replacement = GameGraphicsCodec.Decode(bclim);
        if (original.Width != replacement.Width || original.Height != replacement.Height) throw new InvalidDataException("La imagen debe conservar las dimensiones del título.");
        byte[] previous=Read(archive,image);
        if(Gen7&&(previous.Length!=bclim.Length||!previous.AsSpan(previous.Length-40).SequenceEqual(bclim.AsSpan(bclim.Length-40))))
            throw new InvalidDataException("El BFLIM del título debe conservar formato, tamaño, orientación y encabezado originales. Importá un PNG para convertirlo de forma compatible.");
        if (previous.SequenceEqual(bclim)) return;
        byte[] record = Gen7?new TitleAlytCodec(Record(archive)).Replace(image,bclim):new TitleDarcCodec(Record(archive)).Replace(image, bclim);
        _edited[archive] = record;
    }
    internal byte[] Build()
    {
        var writer = new GARC.LazyGARC(_source);
        foreach (var pair in _edited) { _ = writer[pair.Key]; writer[pair.Key] = pair.Value; }
        byte[] output = writer.Save(); var check = new GARC.LazyGARC(output);
        var rawBefore = new GARC.MemGARC(_source); var rawAfter = new GARC.MemGARC(output);
        if (check.FileCount != _garc.FileCount) throw new InvalidDataException("La cantidad de registros de títulos cambió.");
        for (int i = 0; i < check.FileCount; i++)
            if (_edited.TryGetValue(i, out var expected) ? !check[i].SequenceEqual(expected) : !rawBefore.GetFile(i).SequenceEqual(rawAfter.GetFile(i)))
                throw new InvalidDataException("La relectura no conserva el GARC de títulos.");
        return output;
    }
    internal void Apply()
    {
        if (!Dirty) return;
        byte[] current = GarcTransaction.ReadUnchanged(_path, SHA256.HashData(_source)), output = Build();
        var changes = _edited.Keys.Select(id => new BatchChange(id, Archives.Single(a => a.Id == id).Label, "Imágenes", "Original", "Reemplazo verificado")).ToArray();
        _project.History.Commit([new FileChange(_path, current, output)], Gen7?"Pantallas de título Gen7":"Pantallas de título Gen6", "Reemplazar imágenes", changes);
        _source = output; _garc = new(output); _edited.Clear();
    }
}
