using System.IO;
using System.Text.Json;

namespace ThreeDS.Progressive.Services;

public sealed partial class ProjectHistory
{
    /// <summary>Read saved evidence without recovering, initializing or rewriting the journal.</summary>
    internal HistoryDocument ReadForDataExport()
    {
        lock (_gate)
        {
            RejectPending();
            if (!File.Exists(_path)) return new(1, _game, "", []);
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Historial demasiado grande para procedencia.");
            var doc = JsonSerializer.Deserialize<HistoryDocument>(stream, new JsonSerializerOptions { MaxDepth = 32 })
                ?? throw new InvalidDataException("Historial no disponible.");
            if (doc.Version is not (1 or 2) || doc.Game != _game || doc.Entries is null ||
                doc.Entries.Any(e => e is null || e.Parameters is null || e.Files is null))
                throw new InvalidDataException("Historial no compatible.");
            return doc;
        }
    }
}
