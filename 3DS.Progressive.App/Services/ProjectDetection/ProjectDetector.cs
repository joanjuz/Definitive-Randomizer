using System.IO;
using System.Text.Json;
using pk3DS.Core;
using pk3DS.Core.NDS;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Nds;
using ThreeDS.Progressive.Services.Switch;

namespace ThreeDS.Progressive.Services.ProjectDetection;

internal enum DetectedPlatform { None, NintendoDS, Nintendo3DS, NintendoSwitch }

internal enum DetectedKind { None, DsRom, DsEventCheckpoint, Extracted3dsFolder, SwitchExtraction }

/// <summary>
/// Outcome classes of the detector. Only <see cref="Compatible"/> is a candidate: the engine of the platform still verifies everything
/// (profile tables, SHA-1/SHA-256, structure) when it opens the project.
/// </summary>
internal enum DetectionStatus
{
    /// <summary>Recognised type and supported game or revision.</summary>
    Compatible,
    /// <summary>Recognised type, but the game, revision, build or container is not supported.</summary>
    Unsupported,
    /// <summary>The entry looks like a supported type but a required part is missing, truncated or damaged.</summary>
    Incomplete,
    /// <summary>Contradictory evidence: more than one platform or kind fits, or the name and the content disagree.</summary>
    Ambiguous,
    /// <summary>Nothing recognisable.</summary>
    Unknown,
    /// <summary>The path does not exist or cannot be read.</summary>
    Inaccessible,
}

internal sealed record ProjectDetection(DetectionStatus Status, DetectedPlatform Platform, DetectedKind Kind, string Path, string Game, string Message,
    IReadOnlyList<string> Evidence, string? SourcePath = null)
{
    /// <summary>True only for a supported entry. The opening engine must still confirm it.</summary>
    internal bool IsCandidate => Status == DetectionStatus.Compatible;
    internal string PlatformName => Platform switch { DetectedPlatform.NintendoDS => "Nintendo DS", DetectedPlatform.Nintendo3DS => "Nintendo 3DS", DetectedPlatform.NintendoSwitch => "Nintendo Switch", _ => "" };
}

/// <summary>
/// Universal project detector (phase B1). It identifies what a path is by CONTENT (headers, signatures, folder structures) and never by name or extension
/// alone. It performs minimal, read-only reads and never writes, extracts, hashes whole files, recovers saves or opens a project: the existing engines
/// (<c>NdsProject.Open</c>, <c>GameProjectSession.Open</c>, <c>SwordShieldProject.Open</c>) stay solely responsible for opening, and none of their checks is relaxed here.
/// It reuses the readers of the project: <see cref="NdsRom"/> (header CRC and file table), <see cref="NdsIdentity.Profiles"/>, <see cref="GameConfig"/> (GARC count of a
/// 3DS RomFS) and <see cref="SwordShieldIdentity"/> (title id and build id).
/// </summary>
internal static class ProjectDetector
{
    private const long MaxCheckpointBytes = 8 * 1024 * 1024;
    private const string CheckpointSuffix = ".ndsevents.json";

    /// <summary>Detects the entry. Never throws; every failure becomes a classified result with an explanation.</summary>
    internal static ProjectDetection Detect(string? path)
    {
        string raw = (path ?? "").Trim().Trim('"').Trim();
        if (raw.Length == 0) return Result(DetectionStatus.Unknown, "", "No se indicó ningún archivo ni carpeta.");
        string full;
        try { full = Path.GetFullPath(raw); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return Result(DetectionStatus.Inaccessible, raw, "La ruta no es válida: " + ex.Message); }
        try
        {
            if (Directory.Exists(full)) return DetectFolder(full);
            if (File.Exists(full)) return DetectFile(full);
            return Result(DetectionStatus.Inaccessible, full, "La ruta no existe. Comprueba que el archivo o la carpeta siguen en esa ubicación y que la unidad está conectada.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        { return Result(DetectionStatus.Inaccessible, full, "Windows no permite leer esta ruta (permisos). Mueve el proyecto a una carpeta tuya o cambia los permisos."); }
        catch (IOException ex)
        { return Result(DetectionStatus.Inaccessible, full, "No se pudo leer la ruta (¿está abierta en otro programa o la unidad no responde?): " + ex.Message); }
        catch (Exception ex)
        { return Result(DetectionStatus.Unknown, full, "No se pudo analizar la entrada: " + ex.Message); }
    }

    private static ProjectDetection Result(DetectionStatus status, string path, string message, DetectedPlatform platform = DetectedPlatform.None, DetectedKind kind = DetectedKind.None,
        string game = "", IEnumerable<string>? evidence = null, string? source = null) =>
        new(status, platform, kind, path, game, message, (evidence ?? []).ToList().AsReadOnly(), source);

    // ------------------------------------------------------------------------------------------------------------ files

    private static ProjectDetection DetectFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length == 0) return Result(DetectionStatus.Unknown, path, "El archivo está vacío.");
        byte[] head = ReadHead(path, 0x200);
        bool nameIsCheckpoint = path.EndsWith(CheckpointSuffix, StringComparison.OrdinalIgnoreCase);
        // Container signatures that are recognised but never opened: the project needs an extracted, decrypted tree or a plain .nds.
        if (Container(head) is { } container) return Result(DetectionStatus.Unsupported, path, container);
        if (LooksLikeJson(head)) return DetectCheckpoint(path, info.Length, nameIsCheckpoint);
        // Everything else is judged as a Nintendo DS ROM by its header, whatever the extension says.
        var ds = DetectDsRom(path, nameIsCheckpoint);
        return ds;
    }

    private static byte[] ReadHead(string path, int count)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(count, input.Length)];
        int read = 0; while (read < buffer.Length) { int n = input.Read(buffer, read, buffer.Length - read); if (n <= 0) break; read += n; }
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static bool At(byte[] data, int offset, string ascii) =>
        data.Length >= offset + ascii.Length && System.Text.Encoding.ASCII.GetBytes(ascii).AsSpan().SequenceEqual(data.AsSpan(offset, ascii.Length));

    private static bool AtBytes(byte[] data, int offset, byte[] expected) => data.Length >= offset + expected.Length && expected.AsSpan().SequenceEqual(data.AsSpan(offset, expected.Length));

    private static string? Container(byte[] head)
    {
        if (At(head, 0, "PFS0")) return "Es un paquete NSP de Switch. Definitive Randomizer no abre NSP: usa una extracción de la versión 1.3.2 con las carpetas exefs y romfs.";
        if (At(head, 0x100, "HEAD")) return "Es una imagen XCI de Switch. Definitive Randomizer no abre XCI: usa una extracción de la versión 1.3.2 con las carpetas exefs y romfs.";
        if (At(head, 0x100, "NCSD")) return "Es una imagen de cartucho de 3DS (.3ds/.cci). Definitive Randomizer no descifra ni extrae ROM comerciales: usa una extracción ya descifrada (carpeta con RomFS y ExeFS).";
        if (At(head, 0x100, "NCCH")) return "Es un contenedor CXI/CFA de 3DS. Definitive Randomizer no lo abre: usa una extracción ya descifrada (carpeta con RomFS y ExeFS).";
        if (head.Length >= 4 && BitConverter.ToUInt32(head, 0) == 0x2020 && head.Length >= 0x20 && head[4] == 0 && head[5] == 0 && head[6] == 0 && head[7] == 0) return "Es un archivo CIA de 3DS. Definitive Randomizer no lo abre: usa una extracción ya descifrada (carpeta con RomFS y ExeFS).";
        if (AtBytes(head, 0, [0x50, 0x4B, 0x03, 0x04]) || AtBytes(head, 0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]) || At(head, 0, "Rar!")) return "Es un archivo comprimido. Extráelo primero y selecciona el archivo .nds o la carpeta extraída.";
        return null;
    }

    private static bool LooksLikeJson(byte[] head)
    {
        int i = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        while (i < head.Length && head[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return i < head.Length && head[i] is (byte)'{' or (byte)'[';
    }

    private static ProjectDetection DetectDsRom(string path, bool nameIsCheckpoint)
    {
        NdsRom rom;
        try { rom = NdsRom.Open(path); }
        catch (InvalidDataException ex)
        {
            string name = nameIsCheckpoint ? "Tiene nombre de checkpoint de eventos pero no es JSON ni una ROM de Nintendo DS" : Path.GetExtension(path).Equals(".nds", StringComparison.OrdinalIgnoreCase) ? "Tiene extensión .nds pero su cabecera no es válida" : "No es una ROM de Nintendo DS ni un formato conocido";
            return Result(Path.GetExtension(path).Equals(".nds", StringComparison.OrdinalIgnoreCase) ? DetectionStatus.Incomplete : DetectionStatus.Unknown, path, $"{name}: {ex.Message}");
        }
        using (rom)
        {
            var h = rom.Header; var profile = NdsIdentity.Profiles.FirstOrDefault(p => p.GameCode == h.GameCode && p.Version == h.Version);
            var evidence = new[] { $"Cabecera NDS con CRC válido · código {h.GameCode} · revisión {h.Version} · título «{h.Title}»", $"{rom.Files.Count} archivos en la tabla de archivos" };
            if (nameIsCheckpoint)
                return Result(DetectionStatus.Ambiguous, path, "El nombre indica un checkpoint de eventos, pero el contenido es una ROM de Nintendo DS. Renombra el archivo con su extensión .nds si es la ROM; no se abre con este nombre.", DetectedPlatform.NintendoDS, DetectedKind.DsRom, evidence: evidence);
            if (profile is null)
                return Result(DetectionStatus.Unsupported, path, $"ROM de Nintendo DS reconocida, pero el juego no está admitido: {h.GameCode} revisión {h.Version} («{h.Title}»). Solo se admiten Diamante, Platino, HeartGold, Negro, Blanco y Blanco 2 en las revisiones acreditadas.", DetectedPlatform.NintendoDS, DetectedKind.DsRom, evidence: evidence);
            return Result(DetectionStatus.Compatible, path, $"ROM de Nintendo DS: {profile.Game}.", DetectedPlatform.NintendoDS, DetectedKind.DsRom, profile.Game,
                evidence.Append("Perfil acreditado: " + profile.GameCode + "/" + profile.Version).Append("El SHA-1 y las tablas del juego los verifica el motor DS al abrir; las tablas modificadas solo abren con «Admitir romhacks»."));
        }
    }

    private static ProjectDetection DetectCheckpoint(string path, long length, bool nameIsCheckpoint)
    {
        if (length > MaxCheckpointBytes) return Result(DetectionStatus.Unknown, path, "El archivo JSON es demasiado grande para ser un checkpoint de eventos de Definitive Randomizer.");
        byte[] bytes = File.ReadAllBytes(path);
        try { using var _ = JsonDocument.Parse(bytes); }
        catch (JsonException ex) { return Result(DetectionStatus.Incomplete, path, "El archivo JSON está truncado o dañado: " + ex.Message); }
        NdsEventDocument? document;
        try { document = JsonSerializer.Deserialize<NdsEventDocument>(bytes); }
        catch (JsonException) { document = null; }
        if (document is null || document.Format != NdsEventSession.Format)
            return Result(DetectionStatus.Unknown, path, "Es un JSON válido, pero no un checkpoint de eventos de Nintendo DS de Definitive Randomizer (para plantillas o historiales usa su propia pantalla).");
        var evidence = new List<string> { $"Formato {document.Format} · versión {document.Version} · juego {document.Game}/{document.Revision} · {(document.Operations?.Count ?? document.Edits?.Count ?? 0)} operaciones" };
        if (document.Version is < 1 or > 3)
            return Result(DetectionStatus.Unsupported, path, $"Checkpoint de eventos de versión {document.Version}: esta aplicación lee las versiones 1, 2 y 3. Puede ser de una versión más reciente.", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence);
        if (document.Game != "CPUE" || document.Revision != 1)
            return Result(DetectionStatus.Unsupported, path, $"Checkpoint de eventos para {document.Game} revisión {document.Revision}: solo existe el editor de eventos de Platino USA (CPUE rev. 1).", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence);
        if (string.IsNullOrWhiteSpace(document.SourcePath) || !document.SourcePath.EndsWith(".nds", StringComparison.OrdinalIgnoreCase))
            return Result(DetectionStatus.Incomplete, path, "El checkpoint no indica una ROM de origen .nds válida.", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence);
        if (!nameIsCheckpoint)
            return Result(DetectionStatus.Unsupported, path, "El contenido es un checkpoint de eventos, pero el motor solo abre archivos con nombre «*.ndsevents.json». Copia el archivo con ese nombre; el detector no renombra nada.", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence, source: document.SourcePath);
        if (!File.Exists(document.SourcePath))
            return Result(DetectionStatus.Incomplete, path, "El checkpoint es válido, pero la ROM de origen que indica no existe: " + document.SourcePath + ". Recoloca la ROM original en esa ruta.", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence, source: document.SourcePath);
        var source = DetectDsRom(document.SourcePath, nameIsCheckpoint: false);
        evidence.AddRange(source.Evidence);
        if (source.Kind != DetectedKind.DsRom || source.Status is DetectionStatus.Incomplete or DetectionStatus.Unknown)
            return Result(DetectionStatus.Incomplete, path, "El checkpoint es válido, pero su ROM de origen no se puede leer como ROM de Nintendo DS: " + source.Message, DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence, source: document.SourcePath);
        var game = NdsIdentity.Profiles.FirstOrDefault(p => p.GameCode == "CPUE" && p.Version == 1)!.Game;
        if (source.Status != DetectionStatus.Compatible || !source.Game.Equals(game, StringComparison.Ordinal))
            return Result(DetectionStatus.Unsupported, path, "La ROM de origen del checkpoint no es Platino USA (CPUE rev. 1): " + source.Message, DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, evidence: evidence, source: document.SourcePath);
        return Result(DetectionStatus.Compatible, path, $"Checkpoint de eventos de {game}.", DetectedPlatform.NintendoDS, DetectedKind.DsEventCheckpoint, game,
            evidence.Append("El SHA-256 de la ROM de origen y la reproducción de las operaciones los verifica el motor DS al cargar."), document.SourcePath);
    }

    // ---------------------------------------------------------------------------------------------------------- folders

    private static ProjectDetection DetectFolder(string root)
    {
        var folders = Directory.GetDirectories(root);
        // The same rules GameProjectSession.Open uses to find the two halves of an extracted 3DS game.
        string? romfs3ds = folders.FirstOrDefault(x => Path.GetFileName(x).Contains("rom", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(x, "a")));
        string? exefs3ds = folders.FirstOrDefault(x => Path.GetFileName(x).Contains("exe", StringComparison.OrdinalIgnoreCase) && Directory.GetFiles(x).Any(f => Path.GetFileName(f).Contains("code", StringComparison.OrdinalIgnoreCase)));
        // The same files SwordShieldProject.Open requires.
        bool npdm = File.Exists(Path.Combine(root, "exefs", "main.npdm")), main = File.Exists(Path.Combine(root, "exefs", "main"));
        bool personal = File.Exists(Path.Combine(root, "romfs", SwordShieldProject.PersonalRelativePath));
        bool is3ds = romfs3ds is not null && exefs3ds is not null, isSwitch = npdm && main;
        bool anySwitch = npdm || main || personal, any3ds = romfs3ds is not null || exefs3ds is not null;
        if (is3ds && isSwitch)
            return Result(DetectionStatus.Ambiguous, root, "La carpeta contiene a la vez una extracción de 3DS (RomFS con «a» y ExeFS con «code») y una de Switch (exefs/main.npdm y exefs/main). Separa los proyectos en carpetas distintas.");
        if (is3ds) return Detect3ds(root, romfs3ds!, exefs3ds!);
        if (isSwitch) return DetectSwitch(root, personal);
        if (any3ds && anySwitch)
            return Result(DetectionStatus.Ambiguous, root, "La carpeta tiene piezas incompletas de dos plataformas a la vez (3DS y Switch). Selecciona la carpeta raíz de una única extracción.");
        if (any3ds)
            return Result(DetectionStatus.Incomplete, root, romfs3ds is null ? "Parece una extracción de 3DS, pero falta la carpeta RomFS con su subcarpeta «a». Selecciona la raíz que contiene RomFS y ExeFS." : "Parece una extracción de 3DS, pero falta ExeFS con el archivo «code» (code.bin). Selecciona la raíz que contiene RomFS y ExeFS.", DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder);
        if (anySwitch)
        {
            var missing = new List<string>(); if (!npdm) missing.Add("exefs/main.npdm"); if (!main) missing.Add("exefs/main"); if (!personal) missing.Add("romfs/" + SwordShieldProject.PersonalRelativePath);
            return Result(DetectionStatus.Incomplete, root, "Parece una extracción de Espada/Escudo, pero falta: " + string.Join(", ", missing) + ". Usa la extracción preparada del juego y su actualización 1.3.2.", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);
        }
        // Common mistakes: the inner folder of an extraction, or a folder that merely holds ROM files.
        if (Directory.Exists(Path.Combine(root, "a")) && Directory.GetDirectories(Path.Combine(root, "a")).Length > 0)
            return Result(DetectionStatus.Incomplete, root, "Esta carpeta parece ser el propio RomFS de una extracción de 3DS. Sube un nivel y selecciona la raíz que contiene RomFS y ExeFS.", DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder);
        if (File.Exists(Path.Combine(root, "main.npdm")))
            return Result(DetectionStatus.Incomplete, root, "Esta carpeta parece ser el ExeFS de una extracción de Switch. Sube un nivel y selecciona la raíz que contiene exefs y romfs.", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);
        var roms = Directory.GetFiles(root, "*.nds").Length;
        if (roms > 0) return Result(DetectionStatus.Unknown, root, $"La carpeta contiene {roms} archivo(s) .nds, pero no es un proyecto. Selecciona directamente el archivo .nds que quieres abrir.");
        return Result(DetectionStatus.Unknown, root, "La carpeta no contiene ninguna extracción reconocible (3DS: RomFS y ExeFS; Switch: exefs y romfs).");
    }

    private static ProjectDetection Detect3ds(string root, string romfs, string exefs)
    {
        string a = Path.Combine(romfs, "a");
        int count = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Count(f => Path.GetFileName(f).Length == 1);
        var evidence = new[] { $"RomFS «{Path.GetFileName(romfs)}/a» con {count} archivos GARC", $"ExeFS «{Path.GetFileName(exefs)}» con un archivo «code»" };
        var version = new GameConfig(count).Version;
        string? game = version switch
        {
            GameVersion.XY => "Pokémon X/Y", GameVersion.ORAS => "Pokémon Rubí Omega/Zafiro Alfa", GameVersion.SM => "Pokémon Sol/Luna", GameVersion.USUM => "Pokémon Ultrasol/Ultraluna", _ => null,
        };
        if (version is GameVersion.ORASDEMO or GameVersion.SMDEMO)
            return Result(DetectionStatus.Unsupported, root, $"Extracción de 3DS reconocida como demo ({(version == GameVersion.ORASDEMO ? "Rubí Omega/Zafiro Alfa" : "Sol/Luna")}, {count} archivos GARC). Las demos no están en la matriz de compatibilidad pública.", DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, evidence: evidence);
        if (game is null)
            return Result(DetectionStatus.Unsupported, root, $"Extracción de 3DS con una estructura RomFS no reconocida ({count} archivos GARC). Se admiten X/Y (271), OR/AS (299), Sol/Luna (311) y UltraSol/UltraLuna (333). ¿Es una extracción incompleta o de otro juego?", DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, evidence: evidence);
        return Result(DetectionStatus.Compatible, root, $"Extracción descifrada de 3DS: {game}.", DetectedPlatform.Nintendo3DS, DetectedKind.Extracted3dsFolder, game,
            evidence.Append("El motor 3DS carga los catálogos, comprueba guardados interrumpidos y valida el proyecto al abrir."));
    }

    private static ProjectDetection DetectSwitch(string root, bool personal)
    {
        try { SwordShieldProject.CheckNoLinks(root); }
        catch (IOException ex) { return Result(DetectionStatus.Inaccessible, root, ex.Message, DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction); }
        if (!personal)
            return Result(DetectionStatus.Incomplete, root, "Falta romfs/" + SwordShieldProject.PersonalRelativePath + ": la extracción de Switch está incompleta (no es la actualización preparada).", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);
        string npdmPath = Path.Combine(root, "exefs", "main.npdm"), mainPath = Path.Combine(root, "exefs", "main");
        if (new FileInfo(npdmPath).Length > 1024 * 1024)
            return Result(DetectionStatus.Unknown, root, "exefs/main.npdm tiene un tamaño imposible para un NPDM.", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);
        byte[] npdm = File.ReadAllBytes(npdmPath); byte[] nso = ReadHead(mainPath, 0x100);
        SwordShieldIdentity identity;
        try { identity = SwordShieldIdentity.Read(npdm, nso); }
        catch (InvalidDataException ex)
        {
            return Result(DetectionStatus.Unsupported, root, "La extracción de Switch no es Pokémon Espada ni Escudo o está dañada: " + ex.Message, DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction);
        }
        var evidence = new[] { $"Title ID {identity.TitleId} · {identity.Game}", $"Build ID {identity.BuildId}" };
        if (!identity.CanEdit)
            return Result(DetectionStatus.Unsupported, root, $"{identity.Game} reconocido, pero su compilación no es la 1.3.2 acreditada. La aplicación solo edita esa versión (otras versiones serían de solo lectura).", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction, identity.Game, evidence);
        return Result(DetectionStatus.Compatible, root, $"Extracción de {identity.Game} 1.3.2.", DetectedPlatform.NintendoSwitch, DetectedKind.SwitchExtraction, identity.Game,
            evidence.Append("El motor Switch valida la tabla personal y las dependencias al abrir; el detector solo lee main.npdm y la cabecera de main."));
    }
}
