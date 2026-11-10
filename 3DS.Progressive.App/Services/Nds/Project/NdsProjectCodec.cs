using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeDS.Progressive.Services.Nds.Project;

internal enum NdsProjectReadStatus
{
    Ok,
    /// <summary>Shorter than its own header says, or no footer: an interrupted or truncated write.</summary>
    Truncated,
    /// <summary>Not a project file (wrong signature).</summary>
    NotAProject,
    /// <summary>Written by a newer, incompatible build (minimum reader version above this build's).</summary>
    NewerFormat,
    /// <summary>The header, the manifest, the footer digest or a stored table does not match its checksum.</summary>
    Corrupt,
    /// <summary>Readable bytes, but the content is inconsistent or beyond the safety limits.</summary>
    Invalid,
}

/// <summary>Header fields that are readable (and checksummed) even when the rest of the file is damaged: they let the recovery say what could be lost.</summary>
internal sealed record NdsProjectHeader(int ContainerVersion, int MinReaderVersion, long Generation, int OperationCount, DateTime SavedUtc, long TotalLength);

internal sealed record NdsProjectReadResult(NdsProjectReadStatus Status, string Message, NdsProjectDocument? Document, NdsProjectHeader? Header)
{
    internal bool Ok => Status == NdsProjectReadStatus.Ok;
}

/// <summary>
/// The container of a project (version 1). One self-verifying file:
/// <code>
/// header (64 B)   "DRDSPRJ1", container version, min reader, generation, operation count, manifest length, blob count, saved time, total length, CRC-32
/// manifest        UTF-8 JSON (<see cref="NdsProjectMeta"/>, with the blob index)
/// blobs           [u32 stored length][stored bytes] ... in the order of the index (Brotli or raw; the SHA-256 of the RAW bytes is in the index)
/// footer (40 B)   "DRDSEND1" + SHA-256 of every byte before it
/// </code>
/// Reading never throws for bad input: every failure is a classified <see cref="NdsProjectReadResult"/>. Sizes are bounded before anything is allocated.
/// </summary>
internal static class NdsProjectCodec
{
    internal const int HeaderSize = 64, FooterSize = 40, ContainerVersion = 1;
    internal static readonly byte[] Magic = "DRDSPRJ1"u8.ToArray(), EndMagic = "DRDSEND1"u8.ToArray();
    // Safety limits (a real project is a few hundred KiB; these only stop corrupt or hostile files from exhausting memory).
    internal const int MaxManifestBytes = 32 * 1024 * 1024, MaxBlobs = 4096, MaxOperations = 200_000, MaxBlobRawBytes = 256 * 1024 * 1024;
    internal const long MaxTotalBytes = 1L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };

    // ------------------------------------------------------------------------------------------------------------------------------ names

    internal static string BlobName(NdsProjectBlobKind kind, string key) => kind switch
    {
        NdsProjectBlobKind.File => "file:" + key, NdsProjectBlobKind.Overlay => "overlay:" + key, NdsProjectBlobKind.Arm9 => "arm9",
        NdsProjectBlobKind.EventData => "events:data", NdsProjectBlobKind.History => HistoryName, _ => "events:scripts",
    };

    internal const string HistoryName = "history";

    // ------------------------------------------------------------------------------------------------------------------------------ write

    /// <summary>
    /// The JSON of one operation record, computed once. An operation is immutable after it is confirmed, and a project that grows keeps the same record objects,
    /// so a save costs the new operation, not the whole history again (a single randomization records thousands of changes).
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NdsProjectOperationRecord, byte[]> RecordJson = new();

    /// <summary>The operations as one UTF-8 JSON array, in order. Stored as a compressed table (<see cref="HistoryName"/>), not in the manifest.</summary>
    internal static byte[] HistoryJson(IReadOnlyList<NdsProjectOperationRecord> operations)
    {
        var parts = operations.Select(o => RecordJson.GetValue(o, record => JsonSerializer.SerializeToUtf8Bytes(record, Json))).ToArray();
        var output = new byte[parts.Sum(p => (long)p.Length) + Math.Max(0, parts.Length - 1) + 2]; int at = 0; output[at++] = (byte)'[';
        for (int i = 0; i < parts.Length; i++) { if (i > 0) output[at++] = (byte)','; parts[i].CopyTo(output, at); at += parts[i].Length; }
        output[at] = (byte)']';
        return output;
    }

    /// <summary>Serializes the document. Blobs are ordered by name, so equal documents give equal manifests; the generation and operation count go in the header too.</summary>
    internal static byte[] Serialize(NdsProjectDocument document)
    {
        // The writer refuses what the reader would refuse, so a file that cannot be read back is never produced.
        if (Validate(document.Meta) is { } problem) throw new InvalidDataException("El proyecto no es válido: " + problem);
        return Assemble(document, document.Meta.Operations.Count > 0 ? HistoryJson(document.Meta.Operations) : null);
    }

    /// <summary>Builds the container from the document and the given history bytes, with every checksum right. Apart from <see cref="Serialize"/>, only the tests call it (to write what a hostile or buggy writer could).</summary>
    internal static byte[] Assemble(NdsProjectDocument document, byte[]? history)
    {
        var raws = new List<(NdsProjectBlobKind Kind, string Name, byte[] Raw)>();
        if (history is not null) raws.Add((NdsProjectBlobKind.History, HistoryName, history));
        foreach (var (path, data) in document.State.Files) raws.Add((NdsProjectBlobKind.File, BlobName(NdsProjectBlobKind.File, path), data));
        foreach (var (id, data) in document.State.Overlays) raws.Add((NdsProjectBlobKind.Overlay, BlobName(NdsProjectBlobKind.Overlay, id.ToString(System.Globalization.CultureInfo.InvariantCulture)), data));
        if (document.State.Arm9 is { } arm9) raws.Add((NdsProjectBlobKind.Arm9, BlobName(NdsProjectBlobKind.Arm9, ""), arm9));
        if (document.State.EventData is { } events) raws.Add((NdsProjectBlobKind.EventData, BlobName(NdsProjectBlobKind.EventData, ""), events));
        if (document.State.EventScripts is { } scripts) raws.Add((NdsProjectBlobKind.EventScripts, BlobName(NdsProjectBlobKind.EventScripts, ""), scripts));
        raws.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        if (raws.Count > MaxBlobs) throw new InvalidDataException("El proyecto tiene demasiadas tablas modificadas.");
        if (document.Meta.MinReaderVersion is < 0 or > ushort.MaxValue) throw new InvalidDataException("Versión mínima de lector fuera de rango.");
        var stored = new List<byte[]>(); var entries = new List<NdsProjectBlobEntry>();
        foreach (var (kind, name, raw) in raws)
        {
            if (raw.Length > MaxBlobRawBytes) throw new InvalidDataException(kind == NdsProjectBlobKind.History ? "El historial del proyecto supera el tamaño admitido." : "Una tabla modificada supera el tamaño admitido: " + name);
            byte[] packed = Compress(raw); bool smaller = packed.Length < raw.Length; byte[] bytes = smaller ? packed : raw;
            stored.Add(bytes); entries.Add(new(name, kind, raw.Length, bytes.Length, Convert.ToHexString(SHA256.HashData(raw)), smaller ? "br" : "raw"));
        }
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(document.Meta with { Blobs = entries }, Json);
        if (manifest.Length > MaxManifestBytes) throw new InvalidDataException("El manifiesto del proyecto (historial de eventos y ajustes) supera el tamaño admitido.");
        long total = HeaderSize + manifest.Length + stored.Sum(b => 4L + b.Length) + FooterSize;
        if (total > MaxTotalBytes) throw new InvalidDataException("El proyecto supera el tamaño admitido.");
        var output = new byte[total]; var span = output.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], ContainerVersion); BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)document.Meta.MinReaderVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], (ulong)document.Meta.Generation); BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)document.OperationCount);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)manifest.Length); BinaryPrimitives.WriteUInt32LittleEndian(span[32..], (uint)entries.Count);
        BinaryPrimitives.WriteInt64LittleEndian(span[40..], document.Meta.SavedUtc.Ticks); BinaryPrimitives.WriteUInt64LittleEndian(span[48..], (ulong)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[56..], Crc32(span[..56]));
        manifest.CopyTo(span[HeaderSize..]); int at = HeaderSize + manifest.Length;
        foreach (byte[] bytes in stored) { BinaryPrimitives.WriteUInt32LittleEndian(span[at..], (uint)bytes.Length); bytes.CopyTo(span[(at + 4)..]); at += 4 + bytes.Length; }
        EndMagic.CopyTo(span[at..]); SHA256.HashData(span[..at], span[(at + 8)..]);
        return output;
    }

    // ------------------------------------------------------------------------------------------------------------------------------- read

    /// <summary>Reads only the header (checked by its CRC-32). Returns null when even that is not trustworthy.</summary>
    internal static NdsProjectHeader? ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..8].SequenceEqual(Magic) || BinaryPrimitives.ReadUInt32LittleEndian(data[56..]) != Crc32(data[..56])) return null;
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(data[40..]);
        return new(BinaryPrimitives.ReadUInt16LittleEndian(data[8..]), BinaryPrimitives.ReadUInt16LittleEndian(data[10..]), (long)BinaryPrimitives.ReadUInt64LittleEndian(data[16..]),
            (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[24..]), int.MaxValue), ticks is >= 0 and <= 3155378975999999999 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue, (long)BinaryPrimitives.ReadUInt64LittleEndian(data[48..]));
    }

    internal static NdsProjectReadResult Read(ReadOnlySpan<byte> data)
    {
        NdsProjectReadResult Fail(NdsProjectReadStatus status, string message, NdsProjectHeader? header = null) => new(status, message, null, header);
        // Truncated only when what there is could be the start of a project (an empty file included); any other content is simply not a project, whatever its length.
        int signature = Math.Min(data.Length, Magic.Length);
        if (!data[..signature].SequenceEqual(Magic.AsSpan(0, signature))) return Fail(NdsProjectReadStatus.NotAProject, "No es un proyecto de Definitive Randomizer (firma distinta).");
        if (data.Length < HeaderSize) return Fail(NdsProjectReadStatus.Truncated, "El archivo está truncado: no llega ni a la cabecera.");
        var header = ReadHeader(data);
        if (header is null) return Fail(NdsProjectReadStatus.Corrupt, "La cabecera del proyecto está dañada (suma de comprobación distinta).");
        if (header.MinReaderVersion > ContainerVersion) return Fail(NdsProjectReadStatus.NewerFormat, $"El proyecto lo escribió una versión más reciente (requiere el lector {header.MinReaderVersion}; esta compilación entiende hasta {ContainerVersion}). No se modifica.", header);
        if (header.TotalLength < HeaderSize + FooterSize || header.TotalLength > MaxTotalBytes) return Fail(NdsProjectReadStatus.Invalid, "La longitud declarada del proyecto no es válida.", header);
        if (data.Length < header.TotalLength) return Fail(NdsProjectReadStatus.Truncated, $"El archivo está truncado: tiene {data.Length} bytes y debería tener {header.TotalLength}.", header);
        if (data.Length > header.TotalLength) return Fail(NdsProjectReadStatus.Corrupt, "El archivo tiene bytes añadidos después del final del proyecto.", header);
        int end = (int)header.TotalLength - FooterSize;
        if (!data[end..(end + 8)].SequenceEqual(EndMagic) || !SHA256.HashData(data[..end]).AsSpan().SequenceEqual(data[(end + 8)..]))
            return Fail(NdsProjectReadStatus.Corrupt, "El resumen SHA-256 del proyecto no coincide: el archivo está dañado.", header);
        uint manifestLength = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]), blobCount = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
        if (manifestLength > MaxManifestBytes || blobCount > MaxBlobs || HeaderSize + (long)manifestLength > end) return Fail(NdsProjectReadStatus.Invalid, "Los tamaños declarados del manifiesto o de las tablas no son válidos.", header);
        NdsProjectMeta? meta;
        try { meta = JsonSerializer.Deserialize<NdsProjectMeta>(data.Slice(HeaderSize, (int)manifestLength), Json); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException) { return Fail(NdsProjectReadStatus.Invalid, "El manifiesto del proyecto no es válido: " + ex.Message, header); }
        if (meta is null || meta.Format != NdsProjectMeta.FormatName || meta.Blobs is null) return Fail(NdsProjectReadStatus.Invalid, "El manifiesto no describe un proyecto DS.", header);
        // Every checksum can be valid and the content still be incomplete (a hostile or a buggy writer): nothing below may assume a part exists before this.
        if (Validate(meta) is { } problem) return Fail(NdsProjectReadStatus.Invalid, "El manifiesto del proyecto no es válido: " + problem, header);
        if (meta.MinReaderVersion != header.MinReaderVersion) return Fail(NdsProjectReadStatus.Invalid, "La cabecera y el manifiesto no coinciden (versión mínima de lector).", header);
        if (meta.Generation != header.Generation || meta.Blobs.Count != blobCount) return Fail(NdsProjectReadStatus.Invalid, "La cabecera y el manifiesto no coinciden (generación o tablas).", header);
        foreach (var blob in meta.Blobs)
            if (blob is null || blob.Name is null || blob.Sha256 is null || blob.Codec is null || !IsHex(blob.Sha256, 64)) return Fail(NdsProjectReadStatus.Invalid, "El índice de tablas está incompleto.", header);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal); var overlays = new Dictionary<int, byte[]>(); byte[]? arm9 = null, eventData = null, eventScripts = null, history = null;
        int at = HeaderSize + (int)manifestLength; var seen = new HashSet<string>(StringComparer.Ordinal); long budget = 0;
        foreach (var entry in meta.Blobs)
        {
            if (at + 4 > end) return Fail(NdsProjectReadStatus.Invalid, "El índice de tablas excede el contenido.", header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]); at += 4;
            if (entry is null || length != entry.StoredLength || at + (long)length > end || entry.RawLength is < 0 or > MaxBlobRawBytes || !seen.Add(entry.Name)) return Fail(NdsProjectReadStatus.Invalid, "El índice de tablas no coincide con su contenido.", header);
            if ((budget += entry.RawLength) > MaxTotalBytes) return Fail(NdsProjectReadStatus.Invalid, "Las tablas declaran más tamaño del admitido.", header);
            byte[] raw;
            try { raw = Decode(data.Slice(at, (int)length), entry); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { return Fail(NdsProjectReadStatus.Corrupt, $"La tabla «{entry.Name}» no se puede descomprimir: {ex.Message}", header); }
            if (!Convert.ToHexString(SHA256.HashData(raw)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) return Fail(NdsProjectReadStatus.Corrupt, $"La tabla «{entry.Name}» no coincide con su SHA-256.", header);
            at += (int)length;
            switch (entry.Kind)
            {
                case NdsProjectBlobKind.File when entry.Name.StartsWith("file:", StringComparison.Ordinal): files[entry.Name[5..]] = raw; break;
                case NdsProjectBlobKind.Overlay when entry.Name.StartsWith("overlay:", StringComparison.Ordinal) && int.TryParse(entry.Name[8..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id): overlays[id] = raw; break;
                case NdsProjectBlobKind.Arm9 when entry.Name == "arm9": arm9 = raw; break;
                case NdsProjectBlobKind.EventData when entry.Name == "events:data": eventData = raw; break;
                case NdsProjectBlobKind.EventScripts when entry.Name == "events:scripts": eventScripts = raw; break;
                case NdsProjectBlobKind.History when entry.Name == HistoryName: history = raw; break;
                default: return Fail(NdsProjectReadStatus.Invalid, $"Tabla con nombre o tipo no válido: «{entry.Name}».", header);
            }
        }
        if (at != end) return Fail(NdsProjectReadStatus.Invalid, "Quedan bytes sin describir antes del final del proyecto.", header);
        // The operations travel as a table of their own (compressed): they complete the manifest, and the whole is validated again with them.
        if (history is not null)
        {
            List<NdsProjectOperationRecord>? operations;
            try { operations = JsonSerializer.Deserialize<List<NdsProjectOperationRecord>>(history, Json); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException) { return Fail(NdsProjectReadStatus.Invalid, "El historial del proyecto no es válido: " + ex.Message, header); }
            if (operations is null || operations.Count == 0) return Fail(NdsProjectReadStatus.Invalid, "El historial del proyecto está vacío o no es una lista.", header);
            meta = meta with { Operations = operations };
            if (Validate(meta) is { } incomplete) return Fail(NdsProjectReadStatus.Invalid, "El historial del proyecto no es válido: " + incomplete, header);
        }
        if (meta.Operations.Count + (meta.Events?.Operations.Count ?? 0) != header.OperationCount) return Fail(NdsProjectReadStatus.Invalid, "La cabecera y el historial no coinciden (número de operaciones).", header);
        return new(NdsProjectReadStatus.Ok, "", new NdsProjectDocument(meta, new NdsProjectState { Files = files, Overlays = overlays, Arm9 = arm9, EventData = eventData, EventScripts = eventScripts }), header);
    }

    // -------------------------------------------------------------------------------------------------------------------------- validation

    private static bool IsHex(string? text, int length) => text is not null && text.Length == length && text.All(Uri.IsHexDigit);

    /// <summary>
    /// Checks that the manifest is complete and coherent, whoever wrote it (the same rule for writing and for reading). Returns the problem, or null. After this
    /// nothing in a document is null where the model says it is not, so the layers above never have to defend against a half-described project.
    /// </summary>
    internal static string? Validate(NdsProjectMeta meta)
    {
        if (meta.Format != NdsProjectMeta.FormatName) return "formato distinto";
        if (meta.FormatVersion < 1 || meta.MinReaderVersion < 1) return "versión de formato no admitida";
        if (meta.Generation < 1) return "generación no válida";
        if (meta.Writer is null || meta.ProjectId == Guid.Empty) return "falta el identificador del proyecto o de quien lo escribió";
        if (meta.Rom is not { } rom) return "falta la identidad de la ROM";
        if (!IsHex(rom.Sha256, 64) || !IsHex(rom.Sha1, 40) || string.IsNullOrEmpty(rom.GameCode) || rom.Game is null || rom.LastKnownPath is null || rom.Length <= 0 || rom.Revision < 0) return "la identidad de la ROM está incompleta";
        if (meta.Operations is null || meta.RecompressOverlays is null) return "falta el historial";
        if ((long)meta.Operations.Count + (meta.Events?.Operations?.Count ?? 0) > MaxOperations) return "demasiadas operaciones";
        foreach (var operation in meta.Operations)
            if (operation is null || operation.Action is null || operation.Recipe is null || operation.Changes is null || operation.Changes.Any(c => c is null)) return "una operación del historial está incompleta";
        if (meta.Events is { } events)
        {
            if (events.Operations is null || events.Audit is null || events.Audit.Any(line => line is null)) return "el historial de eventos está incompleto";
            foreach (var operation in events.Operations)
            {
                // Kinds this build does not know are kept as they are (the event session refuses them when it replays); the known ones must carry their payload.
                bool complete = operation is not null && operation.Kind switch
                {
                    null => false, NdsEventOperation.MoveKind => operation.Move is not null, NdsEventOperation.AddKind => operation.Add is not null,
                    NdsEventOperation.RemoveKind => operation.Remove is not null, _ => true,
                };
                if (!complete) return "una operación de eventos está incompleta";
            }
        }
        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------------------- helpers

    private static byte[] Compress(byte[] raw)
    {
        using var ms = new MemoryStream(); using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest, true)) brotli.Write(raw);
        return ms.ToArray();
    }

    /// <summary>Decompresses at most RawLength + 1 bytes, so a decompression bomb cannot exhaust memory, and requires exactly RawLength.</summary>
    private static byte[] Decode(ReadOnlySpan<byte> stored, NdsProjectBlobEntry entry)
    {
        if (entry.Codec == "raw") { if (stored.Length != entry.RawLength) throw new InvalidDataException("longitud distinta"); return stored.ToArray(); }
        if (entry.Codec != "br") throw new InvalidDataException("códec desconocido «" + entry.Codec + "»");
        using var input = new MemoryStream(stored.ToArray()); using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var output = new byte[entry.RawLength]; int done = 0;
        while (done < output.Length) { int n = brotli.Read(output, done, output.Length - done); if (n <= 0) break; done += n; }
        if (done != output.Length || brotli.ReadByte() != -1) throw new InvalidDataException("la longitud descomprimida no coincide");
        return output;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i => { uint c = (uint)i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; return c; }).ToArray();
    internal static uint Crc32(ReadOnlySpan<byte> data) { uint crc = 0xFFFFFFFF; foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8); return ~crc; }
}
