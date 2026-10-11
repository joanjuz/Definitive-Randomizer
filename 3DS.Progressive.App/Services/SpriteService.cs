using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Resuelve un sprite local personalizado o descarga una imagen al cache por especie.
/// No inserta imagenes Pokemon en el repositorio ni requiere modificar la ROM.
/// </summary>
public sealed class SpriteService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(7)
    };

    private readonly string _cacheFolder;
    private readonly string _customFolder;

    public SpriteService()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "3DS Progressive", "sprites");
        _cacheFolder = Path.Combine(root, "cache");
        _customFolder = Path.Combine(root, "custom");
    }

    public async Task<SpriteResult> GetSpriteAsync(int personalEntry, int speciesId, CancellationToken cancellationToken = default)
    {
        if (speciesId is < 1 or > 1025)
            return new SpriteResult(null, "Sprite no disponible para esta entrada.");

        // Las imagenes de formas especificas pueden sobrescribirse sin cambiar la aplicacion.
        // entry-820.png tiene prioridad sobre 025.png / species-025.png.
        string[] customCandidates =
        [
            Path.Combine(_customFolder, $"entry-{personalEntry:D3}.png"),
            Path.Combine(_customFolder, $"species-{speciesId:D3}.png"),
            Path.Combine(_customFolder, $"{speciesId:D3}.png"),
            Path.Combine(_customFolder, $"{speciesId}.png")
        ];

        foreach (string path in customCandidates)
        {
            if (!File.Exists(path))
                continue;
            try
            {
                var sprite = await LoadLocalAsync(path, cancellationToken);
                return new SpriteResult(sprite, "Sprite personalizado");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // Un PNG corrupto no debe impedir mostrar un sprite valido del cache.
            }
        }

        string cached = Path.Combine(_cacheFolder, $"{speciesId:D3}.png");
        if (File.Exists(cached))
        {
            try
            {
                return new SpriteResult(await LoadLocalAsync(cached, cancellationToken), "Sprite en caché");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // Intentar descarga de nuevo si el cache se corrompio.
            }
        }

        try
        {
            // Sprites alojados por PokeAPI. Se obtienen al abrir la especie,
            // solo como recurso visual opcional y sin incorporarlos al codigo fuente.
            using var response = await Client.GetAsync(
                $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{speciesId}.png",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new SpriteResult(null, "No hay sprite remoto; podés agregar un PNG personalizado.");
            if (response.Content.Headers.ContentLength is > 2_000_000)
                return new SpriteResult(null, "El sprite remoto excede el tamaño permitido.");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length is 0 or > 2_000_000)
                return new SpriteResult(null, "El sprite remoto no es válido.");
            BitmapSource image = Decode(bytes);

            Directory.CreateDirectory(_cacheFolder);
            try
            {
                await File.WriteAllBytesAsync(cached, bytes, cancellationToken);
            }
            catch (IOException)
            {
                // El cache es opcional; la imagen puede mostrarse igualmente.
            }
            catch (UnauthorizedAccessException)
            {
                // Sin permisos de cache, se mantiene la imagen en memoria.
            }
            return new SpriteResult(image, "Sprite de PokéAPI");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SpriteResult(null, "Sin conexión para descargar el sprite.");
        }
        catch (HttpRequestException)
        {
            return new SpriteResult(null, "Sin conexión para descargar el sprite.");
        }
        catch (IOException)
        {
            return new SpriteResult(null, "Error al leer el sprite.");
        }
        catch (Exception)
        {
            return new SpriteResult(null, "No se pudo cargar el sprite.");
        }
    }

    private static async Task<BitmapSource> LoadLocalAsync(string file, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        return Decode(bytes);
    }

    private static BitmapSource Decode(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed record SpriteResult(ImageSource? Image, string Description);
