using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Integra pk3DS.Core sin depender de Main.Config, ventanas WinForms ni codigo de los editores.
/// Unicamente lee los archivos del proyecto al abrirlo; el guardado requiere una llamada explicita.
/// </summary>
public sealed partial class GameProjectSession
{
    private readonly GameConfig _config;
    private readonly string _personalPath;
    private byte[] _diskFingerprint;

    private GameProjectSession(GameConfig config, string root, string personalPath)
    {
        _config = config;
        ProjectRoot = root;
        History = new ProjectHistory(root,config.Version.ToString());
        _personalPath = personalPath;
        _diskFingerprint = SHA256.HashData(File.ReadAllBytes(personalPath));

        var species = config.GetText(TextName.SpeciesNames);
        var files = config.GARCPersonal.Files;
        var names = new string[files.Length - 1];
        for (int i = 0; i < names.Length; i++)
            names[i] = i < species.Length && !string.IsNullOrWhiteSpace(species[i])
                ? species[i] : "Entrada " + i.ToString("000");

        // El GARC contiene entradas adicionales para formas alternas.
        // Reutilizamos el algoritmo original, con alternativa segura si hay datos incompletos.
        try
        {
            var altForms = config.Personal.GetFormList(species, config.MaxSpeciesID);
            var expanded = config.Personal.GetPersonalEntryList(
                altForms, species, config.MaxSpeciesID, out _, out _);
            for (int i = 0; i < names.Length && i < expanded.Length; i++)
                if (!string.IsNullOrWhiteSpace(expanded[i]))
                    names[i] = expanded[i];
        }
        catch (ArgumentException)
        {
            // El nombre generico sigue permitiendo editar entradas accesibles.
        }
        catch (IndexOutOfRangeException)
        {
            // Algunas tablas o formas custom no siguen los indices vanilla.
        }

        Entries = Enumerable.Range(1, Math.Max(0, names.Length - 1))
            .Select(i => new PokemonListEntry(i, names[i]))
            .ToArray();
        Types = config.GetText(TextName.Types);
        AbilityNames = config.GetText(TextName.AbilityNames);
        ItemNames = config.GetText(TextName.ItemNames);
        MoveNames = config.GetText(TextName.MoveNames);
        // Nombres resueltos desde el code.bin y Shop.cro ACTIVOS (incluidas ROMs custom).
        // Si faltan los binarios, NO asumir tablas vanilla ni inventar asignaciones.
        TmMoveIds = TryReadTmMoves(config);
        BeachTutorMoveIds = TryReadBeachTutorMoves(config);
    }

    public string ProjectRoot { get; }
    public ProjectHistory History { get; }
    internal GameConfig GameConfiguration => _config;
    public string GameName => _config.Version.ToString();
    public int Generation => _config.Generation;
    public IReadOnlyList<PokemonListEntry> Entries { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> AbilityNames { get; }
    public IReadOnlyList<string> ItemNames { get; }
    public IReadOnlyList<string> MoveNames { get; }
    public IReadOnlyList<ushort> TmMoveIds { get; private set; }
    public IReadOnlyList<ushort> BeachTutorMoveIds { get; private set; }
    internal void RefreshMoveAssignments()
    {
        // Refresh after verified code.bin changes; the Pokémon inspector must not
        // continue displaying stale TM assignments from the opening snapshot.
        TmMoveIds=TryReadTmMoves(_config);
        BeachTutorMoveIds=TryReadBeachTutorMoves(_config);
    }
    public bool TmNamesAvailable => TmMoveIds.Count == 100;
    public bool BeachTutorNamesAvailable => BeachTutorMoveIds.Count > 0;

    public string GetMoveLabel(int id)
    {
        if (id >= 0 && id < MoveNames.Count && !string.IsNullOrWhiteSpace(MoveNames[id]))
            return MoveNames[id];
        return $"Movimiento ID {id}";
    }

    private static ushort[] TryReadTmMoves(GameConfig config)
    {
        // Mismo marcador y desplazamiento del TMEditor7.GetTMHMList.
        byte[] signature = [0x03, 0x40, 0x03, 0x41, 0x03, 0x42, 0x03, 0x43, 0x03];
        try
        {
            if (!Directory.Exists(config.ExeFS))
                return [];
            foreach (string path in Directory.EnumerateFiles(config.ExeFS)
                .Where(p => Path.GetFileName(p).Contains("code", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 0x400000 + signature.Length + 0x22 + 200 ||
                    data.Length % 0x200 != 0)
                    continue;
                int rel = data.AsSpan(0x400000).IndexOf(signature);
                if (rel < 0)
                    continue;
                int offset = checked(0x400000 + rel + signature.Length + (config.USUM ? 0x22 : 0));
                if (offset + 100 * sizeof(ushort) > data.Length)
                    continue;
                ushort[] result = new ushort[100];
                for (int i = 0; i < result.Length; i++)
                    result[i] = BitConverter.ToUInt16(data, offset + 2 * i);
                return result;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return [];
    }

    private static ushort[] TryReadBeachTutorMoves(GameConfig config)
    {
        if (config.Generation != 7)
            return [];
        string path = Path.Combine(config.RomFS, "Shop.cro");
        try
        {
            if (!File.Exists(path))
                return [];
            byte[] data = File.ReadAllBytes(path);
            byte[] counts;
            int offset;
            if (config.USUM)
            {
                if (!Gen7ShopCroLayout.TryRead(data, out var layout, out _))
                    return [];
                counts = layout.BPTutors.Counts.ToArray();
                offset = checked((int)layout.BPTutors.DataStart);
            }
            else
            {
                // SM vanilla usa el desplazamiento legado del TutorEditor7.
                if (data.Length < 0x54DE)
                    return [];
                counts = data.Skip(0x52D2).Take(4).ToArray();
                offset = 0x54DE;
            }
            int total = counts.Sum(n => (int)n);
            if (total <= 0 || total > 256 || offset < 0 ||
                (long)offset + total * 4 > data.Length)
                return [];
            ushort[] result = new ushort[total];
            for (int i = 0; i < total; i++)
                result[i] = BitConverter.ToUInt16(data, offset + i * 4);
            return result;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (OverflowException) { }
        return [];
    }

    public bool SupportsGen7Personal => _config.Generation == 7;
    public int EntryCount => _config.GARCPersonal.FileCount - 1;

    /// <summary>
    /// Convierte un indice interno de Personal en la especie base para visualizar su sprite.
    /// Las formas alternas usan la imagen base, salvo que el usuario agregue un PNG custom
    /// para esa entrada. Esta funcion no participa en el guardado.
    /// </summary>
    public int GetSpriteSpeciesId(int entryIndex)
    {
        if (entryIndex < 1 || entryIndex >= EntryCount)
            return 0;
        if (entryIndex <= _config.MaxSpeciesID)
            return entryIndex;

        var table = _config.Personal.Table;
        for (int species = 1; species <= _config.MaxSpeciesID && species < table.Length; species++)
        {
            var p = table[species];
            int first = p.FormStatsIndex;
            int others = Math.Max(0, p.FormeCount - 1);
            if (first > 0 && entryIndex >= first && entryIndex < first + others)
                return species;
        }
        return 0;
    }

    /// <summary>
    /// Solo acepta un proyecto extraido. Nunca descomprime ni mueve code.bin u otros archivos.
    /// </summary>
    public static GameProjectSession Open(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("La carpeta seleccionada no existe.");

        string[] folders = Directory.GetDirectories(root);
        string? romfs = folders.FirstOrDefault(x =>
            Path.GetFileName(x).Contains("rom", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(x, "a")));
        string? exefs = folders.FirstOrDefault(x =>
            Path.GetFileName(x).Contains("exe", StringComparison.OrdinalIgnoreCase)
            && Directory.GetFiles(x).Any(f =>
                Path.GetFileName(f).Contains("code", StringComparison.OrdinalIgnoreCase)));

        if (romfs is null || exefs is null)
            throw new InvalidDataException(
                "Selecciona la carpeta raiz del juego extraido, con RomFS/a y ExeFS/code.bin. " +
                "Studio no extrae, renombra ni descomprime archivos automaticamente.");

        string a = Path.Combine(romfs, "a");
        int count = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories)
            .Count(f => Path.GetFileName(f).Length == 1);
        var config = new GameConfig(count);
        if (config.Version == GameVersion.Invalid)
            throw new InvalidDataException(
                "Estructura RomFS no reconocida por pk3DS (archivos detectados: " + count + ").");

        // El indice de idioma 2 es ingles, igual al valor por defecto del editor existente.
        var requestedLanguage=AppPreferences.Load().GameTextLanguageIndex;
        // Gen6 original only exposes indices 0..7; unsupported selections
        // revert to English without changing game files or settings.
        config.Initialize(romfs,exefs,config.Generation==6&&requestedLanguage>7
            ?2:requestedLanguage);

        var personalPath = Path.Combine(romfs, config.GetGARCFileName("personal"));
        if (!File.Exists(personalPath))
            throw new FileNotFoundException("No se encontro la tabla personal del proyecto.", personalPath);

        return new GameProjectSession(config, root, personalPath);
    }

    public PersonalValues ReadPersonal(int index)
    {
        if (index < 1 || index >= EntryCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var p = _config.Personal.Table[index];
        var types = p.Types;
        return new PersonalValues(p.HP, p.ATK, p.DEF, p.SPA, p.SPD, p.SPE,
            p.CatchRate, types[0], types[1])
        {
            Ability1 = p.Abilities[0], Ability2 = p.Abilities[1], AbilityHidden = p.Abilities[2],
            Item1 = p.Items[0] & 0xFFFF, Item2 = p.Items[1] & 0xFFFF, Item3 = p.Items[2] & 0xFFFF,
            EvHp = p.EV_HP, EvAttack = p.EV_ATK, EvDefense = p.EV_DEF,
            EvSpecialAttack = p.EV_SPA, EvSpecialDefense = p.EV_SPD, EvSpeed = p.EV_SPE,
            BaseExperience = p.BaseEXP, BaseFriendship = p.BaseFriendship,
            HatchCycles = p.HatchCycles, GenderRatio = p.Gender,
            EggGroup1 = p.EggGroups[0], EggGroup2 = p.EggGroups[1],
            ExperienceGrowth = p.EXPGrowth, EscapeRate = p.EscapeRate,
            EvoStage = p.EvoStage, FormCount = p.FormeCount,
            FormSprite = p.FormeSprite, Color = p.Color,
            Height = p.Height, Weight = p.Weight,
            ZItem = p is PersonalInfoSM sm1 ? sm1.SpecialZ_Item : 0,
            ZBaseMove = p is PersonalInfoSM sm2 ? sm2.SpecialZ_BaseMove : 0,
            ZSpecialMove = p is PersonalInfoSM sm3 ? sm3.SpecialZ_ZMove : 0,
            RegionalVariant = p is PersonalInfoSM sm4 && sm4.LocalVariant,
            TmFlags = EncodeFlags(p.TMHM),
            TutorFlags = EncodeFlags(p.TypeTutors),
            SpecialTutorFlags = string.Join("|", p.SpecialTutors.Select(EncodeFlags)),
        };
    }

    private static string EncodeFlags(bool[] data) =>
        new string(data.Select(x => x ? '1' : '0').ToArray());

    private static bool[] DecodeFlags(string data, int length)
    {
        if (data.Length != length || data.Any(x => x is not ('0' or '1')))
            throw new ArgumentException("Longitud o contenido de compatibilidad inválido.");
        return data.Select(x => x == '1').ToArray();
    }

    /// <summary>
    /// Serializa un GARC completo en un archivo temporal, verifica el contenido y reemplaza
    /// el archivo solamente tras confirmacion explicita en la UI. No crea backups automaticos.
    /// </summary>
    public void SavePersonal(int index, PersonalValues values)
    {
        PersonalValues beforeValues=ReadPersonal(index);
        if (index < 1 || index >= EntryCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        foreach (int value in new[] { values.HP, values.Attack, values.Defense,
                     values.SpecialAttack, values.SpecialDefense, values.Speed, values.CatchRate })
            if (value is < 0 or > 255)
                throw new ArgumentOutOfRangeException(nameof(values),
                    "Las estadisticas y la captura deben estar entre 0 y 255.");
        if (values.FirstType < 0 || values.FirstType >= Types.Count ||
            values.SecondType < 0 || values.SecondType >= Types.Count)
            throw new ArgumentOutOfRangeException(nameof(values), "Tipo de Pokemon invalido.");

        foreach (int id in new[] { values.Ability1, values.Ability2, values.AbilityHidden,
                  values.BaseFriendship, values.HatchCycles, values.GenderRatio,
                  values.EggGroup1, values.EggGroup2, values.ExperienceGrowth, values.EscapeRate })
            if (id is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(values), "ID fuera del rango 0 a 255.");
        foreach (int item in new[] { values.Item1, values.Item2, values.Item3, values.BaseExperience })
            if (item is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(values), "El valor excede 65535.");
        foreach (int ev in new[] { values.EvHp, values.EvAttack, values.EvDefense,
                    values.EvSpecialAttack, values.EvSpecialDefense, values.EvSpeed })
            if (ev is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(values), "Cada EV otorgado debe estar entre 0 y 3.");

        if (values.EvoStage is < 0 or > 255 || values.FormCount is < 0 or > 255 ||
            values.FormSprite is < 0 or > 65535 || values.Color is < 0 or > 63 ||
            values.Height is < 0 or > 65535 || values.Weight is < 0 or > 65535 ||
            values.ZItem is < 0 or > 65535 || values.ZBaseMove is < 0 or > 65535 ||
            values.ZSpecialMove is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(values), "Valores de formas, tamaño o Z fuera de rango.");

        byte[] source = File.ReadAllBytes(_personalPath);
        if (!SHA256.HashData(source).AsSpan().SequenceEqual(_diskFingerprint))
            throw new IOException("El archivo personal se modifico desde que Studio lo abrio. " +
                "Cierra y vuelve a abrir el proyecto antes de guardar.");

        byte[][] files = _config.GARCPersonal.Files;
        if (index >= files.Length - 1)
            throw new InvalidDataException("Indice personal fuera del GARC.");

        byte[] candidate = (byte[])files[index].Clone();
        PersonalInfo p = _config.XY ? new PersonalInfoXY(candidate)
            : _config.ORAS ? new PersonalInfoORAS(candidate)
            : new PersonalInfoSM(candidate);
        p.HP = values.HP;
        p.ATK = values.Attack;
        p.DEF = values.Defense;
        p.SPA = values.SpecialAttack;
        p.SPD = values.SpecialDefense;
        p.SPE = values.Speed;
        p.CatchRate = values.CatchRate;
        p.Types = [values.FirstType, values.SecondType];
        p.Abilities = [values.Ability1, values.Ability2, values.AbilityHidden];
        p.Items = [values.Item1, values.Item2, values.Item3];
        p.EV_HP = values.EvHp;
        p.EV_ATK = values.EvAttack;
        p.EV_DEF = values.EvDefense;
        p.EV_SPA = values.EvSpecialAttack;
        p.EV_SPD = values.EvSpecialDefense;
        p.EV_SPE = values.EvSpeed;
        p.BaseEXP = values.BaseExperience;
        p.BaseFriendship = values.BaseFriendship;
        p.HatchCycles = values.HatchCycles;
        p.Gender = values.GenderRatio;
        p.EggGroups = [values.EggGroup1, values.EggGroup2];
        p.EXPGrowth = values.ExperienceGrowth;
        p.EscapeRate = values.EscapeRate;
        p.EvoStage = values.EvoStage;
        p.FormeCount = values.FormCount;
        p.FormeSprite = values.FormSprite;
        p.Color = values.Color;
        p.Height = values.Height;
        p.Weight = values.Weight;
        p.TMHM = DecodeFlags(values.TmFlags, p.TMHM.Length);
        p.TypeTutors = DecodeFlags(values.TutorFlags, p.TypeTutors.Length);
        string[] special = values.SpecialTutorFlags.Length == 0 ? [] : values.SpecialTutorFlags.Split('|');
        if (special.Length != p.SpecialTutors.Length)
            throw new ArgumentException("La cantidad de grupos de tutores ha cambiado.");
        for (int k = 0; k < special.Length; k++)
            p.SpecialTutors[k] = DecodeFlags(special[k], p.SpecialTutors[k].Length);
        if (p is PersonalInfoSM sm)
        {
            sm.SpecialZ_Item = values.ZItem;
            sm.SpecialZ_BaseMove = values.ZBaseMove;
            sm.SpecialZ_ZMove = values.ZSpecialMove;
            sm.LocalVariant = values.RegionalVariant;
        }
        files[index] = p.Write();

        // La ultima entrada GARC es la tabla maestra de PersonalInfo.
        long required = files.Take(files.Length - 1).Sum(f => (long)f.Length);
        if (required != files[^1].Length)
            throw new InvalidDataException(
                "La tabla maestra no coincide con las entradas individuales. No se guardo nada.");
        byte[] master = (byte[])files[^1].Clone();
        int pos = 0;
        for (int i = 0; i < files.Length - 1; i++)
        {
            Buffer.BlockCopy(files[i], 0, master, pos, files[i].Length);
            pos += files[i].Length;
        }
        files[^1] = master;

        _diskFingerprint = History.CommitGarc(_personalPath, _config.GetGARCReference("personal"),
            _diskFingerprint, files,"Pokémon","Editar Pokémon",
            ProjectHistory.ValueChanges(index,Entries.First(e=>e.Index==index).Name,beforeValues,values));
        _config.InitializePersonal();
    }
}
