using System.IO;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Holds unchanged, read-only dependencies stable until the history transaction finishes.</summary>
internal sealed class Map3dsDependencyLease : IDisposable
{
    private readonly List<FileStream> _streams = [];

    internal Map3dsDependencyLease(EditorProposal proposal)
    {
        var written = proposal.Files.Select(f => Path.GetFullPath(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dependency in proposal.Dependencies)
            {
                if (written.Contains(Path.GetFullPath(dependency.Path)))
                {
                    // FileTransaction checks the fingerprint of files it replaces; a read lock would prevent that replacement.
                    GarcTransaction.ReadUnchanged(dependency.Path, dependency.Fingerprint);
                    continue;
                }
                var stream = new FileStream(dependency.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                _streams.Add(stream);
                if (!SHA256.HashData(stream).AsSpan().SequenceEqual(dependency.Fingerprint))
                    throw new IOException("Una dependencia cambió desde la vista previa. Vuelve a abrir el editor; no se escribió nada.");
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var stream in _streams) stream.Dispose();
        _streams.Clear();
    }
}