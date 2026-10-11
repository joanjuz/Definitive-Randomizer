using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Integra pk3DS.Core sin depender de Main.Config, ventanas WinForms ni codigo de los editores.
/// Unicamente lee los archivos del proyecto al abrirlo; el guardado requiere una llamada explicita.
/// </summary>
public sealed class GameProjectSession
{
    private readonly GameConfig _config;
    private readonly string _personalPath;
    private byte[] _diskFingerprint;

    private GameProjectSession(GameConfig config, string root, string personalPath)
    {
        _config = config;
        ProjectRoot = root;
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
    }

    public string ProjectRoot { get; }
    public string GameName => _config.Version.ToString();
    public int Generation => _config.Generation;
    public IReadOnlyList<PokemonListEntry> Entries { get; }
    public IReadOnlyList<string> Types { get; }
    public int EntryCount => _config.GARCPersonal.FileCount - 1;

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
        config.Initialize(romfs, exefs, 2);

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
            p.CatchRate, types[0], types[1]);
    }

    /// <summary>
    /// Serializa un GARC completo en un archivo temporal, verifica el contenido y reemplaza
    /// el archivo solamente tras confirmacion explicita en la UI. No crea backups automaticos.
    /// </summary>
    public void SavePersonal(int index, PersonalValues values)
    {
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

        string temp = _personalPath + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var output = new GARCFile(
                new GARC.MemGARC(source),
                _config.GetGARCReference("personal"),
                temp);
            output.Files = files;
            output.Save();

            var check = new GARC.MemGARC(File.ReadAllBytes(temp));
            if (check.FileCount != files.Length ||
                !check.GetFile(index).AsSpan().SequenceEqual(files[index]) ||
                !check.GetFile(files.Length - 1).AsSpan().SequenceEqual(master))
                throw new InvalidDataException(
                    "Fallo la verificacion posterior al guardado del GARC.");

            // No se deja un archivo parcial. El reemplazo es atomico en NTFS.
            if (!SHA256.HashData(File.ReadAllBytes(_personalPath))
                    .AsSpan().SequenceEqual(_diskFingerprint))
                throw new IOException("El proyecto cambio durante el guardado. No se reemplazo.");

            File.Replace(temp, _personalPath, null);
            _diskFingerprint = SHA256.HashData(File.ReadAllBytes(_personalPath));
            _config.InitializePersonal();
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
