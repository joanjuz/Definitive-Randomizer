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
/// The operation history is split into blocks (<c>history:000000</c>, <c>history:000001</c> …), each a JSON array of consecutive operations compressed on its own.
/// Where one block ends is a pure function of the operations (<see cref="HistoryBlocks"/>), and a block that is full never changes again, so a save compresses
/// only the last, open block: the blocks before it are reused exactly as they were stored.
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
        NdsProjectBlobKind.EventData => "events:data", NdsProjectBlobKind.History => HistoryPrefix + key, _ => "events:scripts",
    };

    internal const string HistoryPrefix = "history:";
    /// <summary>A block closes once its operations weigh this much (one per operation plus one per recorded change) or once it holds this many operations.</summary>
    internal const int HistoryBlockWeight = 8192, HistoryBlockOperations = 256;

    /// <summary>The name of history block <paramref name="index"/>: fixed width, so ordering the tables by name keeps the blocks in the order of the operations.</summary>
    internal static string HistoryBlockName(int index) => HistoryPrefix + index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Where each history block starts and how many operations it has. Greedy from the first operation: a block closes as soon as it reaches
    /// <see cref="HistoryBlockWeight"/> or <see cref="HistoryBlockOperations"/>. Appending operations therefore never moves an earlier boundary; only the last
    /// block may still grow (it is open when it has not reached either limit).
    /// </summary>
    internal static List<(int Start, int Count, bool Closed)> HistoryBlocks(IReadOnlyList<NdsProjectOperationRecord> operations)
    {
        var blocks = new List<(int, int, bool)>(); int start = 0; long weight = 0;
        for (int i = 0; i < operations.Count; i++)
        {
            weight += (operations[i].Changes?.Count ?? 0) + 1;
            if (weight >= HistoryBlockWeight || i + 1 - start >= HistoryBlockOperations) { blocks.Add((start, i + 1 - start, true)); start = i + 1; weight = 0; }
        }
        if (start < operations.Count) blocks.Add((start, operations.Count - start, false));
        return blocks;
    }

    // ------------------------------------------------------------------------------------------------------------------------------ write

    /// <summary>
    /// The JSON of one operation record, computed once. An operation is immutable after it is confirmed, and a project that grows keeps the same record objects,
    /// so a save costs the new operation, not the whole history again (a single randomization records thousands of changes). Kept only while the record is in
    /// the open block: once its block is full the block itself is kept (<see cref="StoredBlocks"/>) and this copy is released.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NdsProjectOperationRecord, byte[]> RecordJson = new();

    /// <summary>A history block exactly as it is stored: its operations (the same objects), its stored bytes and their description in the index.</summary>
    private sealed record StoredBlock(NdsProjectOperationRecord[] Members, byte[] Stored, long RawLength, string Sha256, string Codec)
    {
        internal bool Holds(IReadOnlyList<NdsProjectOperationRecord> operations, int start, int count)
        {
            if (Members.Length != count) return false;
            for (int i = 0; i < count; i++) if (!ReferenceEquals(Members[i], operations[start + i])) return false;
            return true;
        }
    }

    /// <summary>
    /// The blocks already compressed, by their first operation. A block is reused only when it holds exactly the same operation objects (compared by reference,
    /// so an equal-looking but different operation is never mistaken for it), and its stored bytes were proved to decompress to its SHA-256 when it was made or read.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NdsProjectOperationRecord, StoredBlock> StoredBlocks = new();

    /// <summary>The operations as one UTF-8 JSON array, in order (the raw content of one history block).</summary>
    internal static byte[] HistoryJson(IReadOnlyList<NdsProjectOperationRecord> operations) => HistoryJson(operations, 0, operations.Count);

    private static byte[] HistoryJson(IReadOnlyList<NdsProjectOperationRecord> operations, int start, int count)
    {
        var parts = new byte[count][];
        for (int i = 0; i < count; i++) parts[i] = RecordJson.GetValue(operations[start + i], record =>
        {
            // The one moment the JSON of an operation is produced is also when it is proved readable (a save later verifies the container, not every past operation again).
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, Json);
            if (JsonSerializer.Deserialize<NdsProjectOperationRecord>(json, Json) is not { Action: not null, Recipe: not null, Changes: not null } back || back.Changes.Count != record.Changes.Count)
                throw new InvalidDataException("Una operación del historial no se puede leer después de escribirla.");
            return json;
        });
        var output = new byte[parts.Sum(p => (long)p.Length) + Math.Max(0, parts.Length - 1) + 2]; int at = 0; output[at++] = (byte)'[';
        for (int i = 0; i < parts.Length; i++) { if (i > 0) output[at++] = (byte)','; parts[i].CopyTo(output, at); at += parts[i].Length; }
        output[at] = (byte)']';
        return output;
    }

    /// <summary>One history block, reused when it is already stored and built (and kept) otherwise.</summary>
    private static Part HistoryBlock(IReadOnlyList<NdsProjectOperationRecord> operations, int index, int start, int count, bool closed)
    {
        var first = operations[start]; string name = HistoryBlockName(index);
        if (StoredBlocks.TryGetValue(first, out var known) && known.Holds(operations, start, count)) return new(NdsProjectBlobKind.History, name, known.RawLength, known.Stored, known.Sha256, known.Codec);
        var part = Pack(NdsProjectBlobKind.History, name, HistoryJson(operations, start, count));
        // Proved once, here: these stored bytes give the raw block. Reusing them later needs no new proof (the save still verifies the whole container it writes).
        var entry = new NdsProjectBlobEntry(name, NdsProjectBlobKind.History, part.RawLength, part.Stored.Length, part.Sha256, part.Codec);
        if (!Convert.ToHexString(SHA256.HashData(Decode(part.Stored, entry))).Equals(part.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("Un bloque del historial no se puede leer después de comprimirlo.");
        Remember(operations.Skip(start).Take(count).ToArray(), part); Prove(part.Stored, entry);
        // A full block never changes again: the JSON of its operations is not needed any more.
        if (closed) for (int i = start; i < start + count; i++) RecordJson.Remove(operations[i]);
        return part;
    }

    private static void Remember(NdsProjectOperationRecord[] members, Part part) => StoredBlocks.AddOrUpdate(members[0], new(members, part.Stored, part.RawLength, part.Sha256, part.Codec));

    /// <summary>Serializes the document. Blobs are ordered by name, so equal documents give equal manifests; the generation and operation count go in the header too.</summary>
    internal static byte[] Serialize(NdsProjectDocument document)
    {
        // The writer refuses what the reader would refuse, so a file that cannot be read back is never produced.
        if (Validate(document.Meta) is { } problem) throw new InvalidDataException("El proyecto no es válido: " + problem);
        var operations = document.Meta.Operations; var blocks = HistoryBlocks(operations);
        return Build(document, blocks.Select((b, i) => HistoryBlock(operations, i, b.Start, b.Count, b.Closed)).ToList());
    }

    /// <summary>
    /// Builds the container from the document and the given raw history blocks (in order; null or empty for none), with every checksum right. Apart from
    /// <see cref="Serialize"/>, only the tests call it (to write what a hostile or buggy writer could).
    /// </summary>
    internal static byte[] Assemble(NdsProjectDocument document, IReadOnlyList<byte[]>? history) =>
        Build(document, (history ?? []).Select((raw, i) => Pack(NdsProjectBlobKind.History, HistoryBlockName(i), raw)).ToList());

    /// <summary>One table as it is stored: Brotli when that is smaller, raw otherwise, described by the SHA-256 of the raw bytes.</summary>
    private readonly record struct Part(NdsProjectBlobKind Kind, string Name, long RawLength, byte[] Stored, string Sha256, string Codec);

    private static Part Pack(NdsProjectBlobKind kind, string name, byte[] raw)
    {
        if (raw.Length > MaxBlobRawBytes) throw new InvalidDataException(kind == NdsProjectBlobKind.History ? "Un bloque del historial del proyecto supera el tamaño admitido." : "Una tabla modificada supera el tamaño admitido: " + name);
        byte[] packed = Compress(raw); bool smaller = packed.Length < raw.Length;
        return new(kind, name, raw.Length, smaller ? packed : raw, Convert.ToHexString(SHA256.HashData(raw)), smaller ? "br" : "raw");
    }

    private static byte[] Build(NdsProjectDocument document, List<Part> parts)
    {
        foreach (var (path, data) in document.State.Files) parts.Add(Pack(NdsProjectBlobKind.File, BlobName(NdsProjectBlobKind.File, path), data));
        foreach (var (id, data) in document.State.Overlays) parts.Add(Pack(NdsProjectBlobKind.Overlay, BlobName(NdsProjectBlobKind.Overlay, id.ToString(System.Globalization.CultureInfo.InvariantCulture)), data));
        if (document.State.Arm9 is { } arm9) parts.Add(Pack(NdsProjectBlobKind.Arm9, BlobName(NdsProjectBlobKind.Arm9, ""), arm9));
        if (document.State.EventData is { } events) parts.Add(Pack(NdsProjectBlobKind.EventData, BlobName(NdsProjectBlobKind.EventData, ""), events));
        if (document.State.EventScripts is { } scripts) parts.Add(Pack(NdsProjectBlobKind.EventScripts, BlobName(NdsProjectBlobKind.EventScripts, ""), scripts));
        parts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        if (parts.Count > MaxBlobs) throw new InvalidDataException("El proyecto tiene demasiadas tablas modificadas o un historial demasiado largo.");
        if (document.Meta.MinReaderVersion is < 0 or > ushort.MaxValue) throw new InvalidDataException("Versión mínima de lector fuera de rango.");
        var entries = parts.Select(p => new NdsProjectBlobEntry(p.Name, p.Kind, p.RawLength, p.Stored.Length, p.Sha256, p.Codec)).ToList();
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(document.Meta with { Blobs = entries }, Json);
        if (manifest.Length > MaxManifestBytes) throw new InvalidDataException("El manifiesto del proyecto (historial de eventos y ajustes) supera el tamaño admitido.");
        long total = HeaderSize + manifest.Length + parts.Sum(p => 4L + p.Stored.Length) + FooterSize;
        if (total > MaxTotalBytes) throw new InvalidDataException("El proyecto supera el tamaño admitido.");
        var output = new byte[total]; var span = output.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], ContainerVersion); BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)document.Meta.MinReaderVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], (ulong)document.Meta.Generation); BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)document.OperationCount);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)manifest.Length); BinaryPrimitives.WriteUInt32LittleEndian(span[32..], (uint)entries.Count);
        BinaryPrimitives.WriteInt64LittleEndian(span[40..], document.Meta.SavedUtc.Ticks); BinaryPrimitives.WriteUInt64LittleEndian(span[48..], (ulong)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[56..], Crc32(span[..56]));
        manifest.CopyTo(span[HeaderSize..]); int at = HeaderSize + manifest.Length;
        foreach (var part in parts) { BinaryPrimitives.WriteUInt32LittleEndian(span[at..], (uint)part.Stored.Length); part.Stored.CopyTo(span[(at + 4)..]); at += 4 + part.Stored.Length; }
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

    /// <summary>
    /// Checks a container that this build has just written, without rebuilding its history: the header, the digest of the file, the manifest, and every table
    /// (the history included) decompressed and compared with its SHA-256. The operations are NOT deserialized again (each one was proved readable when its
    /// JSON was produced), so verifying a save does not cost the whole history. The document returned has no operations; use <see cref="Read"/> to open a project.
    /// </summary>
    internal static NdsProjectReadResult Verify(ReadOnlySpan<byte> data) => Read(data, history: false);

    internal static NdsProjectReadResult Read(ReadOnlySpan<byte> data) => Read(data, history: true);

    private static NdsProjectReadResult Read(ReadOnlySpan<byte> data, bool history)
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
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal); var overlays = new Dictionary<int, byte[]>(); byte[]? arm9 = null, eventData = null, eventScripts = null;
        var blocks = new List<(byte[] Raw, byte[] Stored, NdsProjectBlobEntry Entry)>();
        int at = HeaderSize + (int)manifestLength; var seen = new HashSet<string>(StringComparer.Ordinal); long budget = 0;
        foreach (var entry in meta.Blobs)
        {
            if (at + 4 > end) return Fail(NdsProjectReadStatus.Invalid, "El índice de tablas excede el contenido.", header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]); at += 4;
            if (entry is null || length != entry.StoredLength || at + (long)length > end || entry.RawLength is < 0 or > MaxBlobRawBytes || !seen.Add(entry.Name)) return Fail(NdsProjectReadStatus.Invalid, "El índice de tablas no coincide con su contenido.", header);
            if ((budget += entry.RawLength) > MaxTotalBytes) return Fail(NdsProjectReadStatus.Invalid, "Las tablas declaran más tamaño del admitido.", header);
            // History blocks: block n must be the n-th one, so the operations can only be put back in the order they were written.
            bool block = entry.Kind == NdsProjectBlobKind.History;
            if (block && entry.Name != HistoryBlockName(blocks.Count)) return Fail(NdsProjectReadStatus.Invalid, $"Bloque del historial fuera de orden o con nombre no válido: «{entry.Name}».", header);
            var stored = data.Slice(at, (int)length);
            byte[] raw;
            // Checking a save just written: a block whose exact stored bytes were already proved to give this content needs no second decompression.
            if (block && !history && Proven(stored, entry)) raw = [];
            else
            {
                try { raw = Decode(stored, entry); }
                // Brotli reports damaged input as InvalidOperationException ("Decoder ran into invalid data"): it is damaged content like the others, never an exception.
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException) { return Fail(NdsProjectReadStatus.Corrupt, $"La tabla «{entry.Name}» no se puede descomprimir: {ex.Message}", header); }
                if (!Convert.ToHexString(SHA256.HashData(raw)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) return Fail(NdsProjectReadStatus.Corrupt, $"La tabla «{entry.Name}» no coincide con su SHA-256.", header);
            }
            at += (int)length;
            switch (entry.Kind)
            {
                case NdsProjectBlobKind.File when entry.Name.StartsWith("file:", StringComparison.Ordinal): files[entry.Name[5..]] = raw; break;
                case NdsProjectBlobKind.Overlay when entry.Name.StartsWith("overlay:", StringComparison.Ordinal) && int.TryParse(entry.Name[8..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id): overlays[id] = raw; break;
                case NdsProjectBlobKind.Arm9 when entry.Name == "arm9": arm9 = raw; break;
                case NdsProjectBlobKind.EventData when entry.Name == "events:data": eventData = raw; break;
                case NdsProjectBlobKind.EventScripts when entry.Name == "events:scripts": eventScripts = raw; break;
                case NdsProjectBlobKind.History: blocks.Add((raw, history ? stored.ToArray() : [], entry)); break;
                default: return Fail(NdsProjectReadStatus.Invalid, $"Tabla con nombre o tipo no válido: «{entry.Name}».", header);
            }
        }
        if (at != end) return Fail(NdsProjectReadStatus.Invalid, "Quedan bytes sin describir antes del final del proyecto.", header);
        // The operations travel as tables of their own (compressed blocks): they complete the manifest, and the whole is validated again with them.
        if (!history) return new(NdsProjectReadStatus.Ok, "", new NdsProjectDocument(meta, new NdsProjectState { Files = files, Overlays = overlays, Arm9 = arm9, EventData = eventData, EventScripts = eventScripts }), header);
        if (blocks.Count > 0)
        {
            var operations = new List<NdsProjectOperationRecord>(); var spans = new List<(int Start, int Count)>();
            foreach (var (raw, _, entry) in blocks)
            {
                List<NdsProjectOperationRecord>? part;
                try { part = JsonSerializer.Deserialize<List<NdsProjectOperationRecord>>(raw, Json); }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException) { return Fail(NdsProjectReadStatus.Invalid, $"El historial del proyecto no es válido ({entry.Name}): " + ex.Message, header); }
                if (part is null || part.Count == 0) return Fail(NdsProjectReadStatus.Invalid, $"Un bloque del historial está vacío o no es una lista ({entry.Name}).", header);
                if (operations.Count + (long)part.Count > MaxOperations) return Fail(NdsProjectReadStatus.Invalid, "El historial del proyecto tiene demasiadas operaciones.", header);
                spans.Add((operations.Count, part.Count)); operations.AddRange(part);
            }
            meta = meta with { Operations = operations };
            if (Validate(meta) is { } incomplete) return Fail(NdsProjectReadStatus.Invalid, "El historial del proyecto no es válido: " + incomplete, header);
            // The blocks of a file this rule wrote are kept as they are stored, so the next save does not compress them again. Other boundaries are simply rebuilt then.
            var canonical = HistoryBlocks(operations);
            if (canonical.Count == spans.Count && canonical.Select(b => (b.Start, b.Count)).SequenceEqual(spans))
                for (int i = 0; i < blocks.Count; i++)
                {
                    var (_, stored, entry) = blocks[i]; var part = new Part(NdsProjectBlobKind.History, entry.Name, entry.RawLength, stored, entry.Sha256.ToUpperInvariant(), entry.Codec);
                    if (part.Sha256 != entry.Sha256) continue; // only the form this writer produces, so a reused block is byte-identical to a rebuilt one
                    Remember(operations.Skip(spans[i].Start).Take(spans[i].Count).ToArray(), part); Prove(stored, entry);
                }
        }
        if (meta.Operations.Count + (meta.Events?.Operations.Count ?? 0) != header.OperationCount) return Fail(NdsProjectReadStatus.Invalid, "La cabecera y el historial no coinciden (número de operaciones).", header);
        return new(NdsProjectReadStatus.Ok, "", new NdsProjectDocument(meta, new NdsProjectState { Files = files, Overlays = overlays, Arm9 = arm9, EventData = eventData, EventScripts = eventScripts }), header);
    }

    /// <summary>
    /// Stored history blocks known to decompress to the content their index entry describes, by the SHA-256 of the stored bytes. A fact about the bytes, true
    /// whoever asks; it only spares the second decompression when a save checks the container it has just written (opening a project always decompresses).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ProvenBlocks = new(StringComparer.Ordinal);

    private static string Described(NdsProjectBlobEntry entry) => entry.Codec + ":" + entry.RawLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + entry.Sha256.ToUpperInvariant();

    private static void Prove(ReadOnlySpan<byte> stored, NdsProjectBlobEntry entry)
    {
        if (ProvenBlocks.Count > 65_536) ProvenBlocks.Clear();
        ProvenBlocks[Convert.ToHexString(SHA256.HashData(stored))] = Described(entry);
    }

    private static bool Proven(ReadOnlySpan<byte> stored, NdsProjectBlobEntry entry) =>
        ProvenBlocks.TryGetValue(Convert.ToHexString(SHA256.HashData(stored)), out var described) && described == Described(entry);

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
