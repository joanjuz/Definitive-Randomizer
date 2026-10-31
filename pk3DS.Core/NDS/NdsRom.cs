#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pk3DS.Core.NDS;

// Container layout follows GBATEK and Universal Pokemon Randomizer ZX (GPLv3, see docs/third-party).
public sealed record NdsHeader(string Title, string GameCode, byte UnitCode, byte Version, uint NtrEnd, uint HeaderSize)
{
    public bool IsDsiEnhanced => (UnitCode & 2) != 0;
}

public sealed record NdsFileEntry(int Id, string Path, uint Start, uint End)
{
    public int Length => checked((int)(End - Start));
}

/// <summary>ARM9 overlay table entry (32 bytes). Bit 0 of <see cref="Flags"/> marks a BLZ-compressed overlay.</summary>
public sealed record NdsOverlay(int Index, int Id, uint RamAddress, uint RamSize, int FileId, int StoredSize, byte Flags)
{
    public bool Compressed => (Flags & 1) != 0;
}

/// <summary>
/// Read-only view of a Nintendo DS ROM with a writer that keeps the original layout:
/// changed files are written in place when they fit their slot, otherwise moved to free space after the data.
/// </summary>
public sealed class NdsRom : IDisposable
{
    private const int HeaderCrcOffset = 0x15E;
    private readonly FileStream _stream;
    private readonly List<NdsFileEntry> _files;
    private readonly List<NdsOverlay> _overlays;
    private readonly uint _overlayTable;
    private readonly List<(uint Start, uint End)> _regions;
    private readonly uint _freeLimit;

    public string Path { get; }
    public NdsHeader Header { get; }
    public IReadOnlyList<NdsFileEntry> Files => _files;
    public IReadOnlyList<NdsOverlay> Overlays => _overlays;
    public long Length => _stream.Length;
    /// <summary>End of the free space usable for relocated files (start of the DSi area, or the end of the dump).</summary>
    public uint FreeSpaceEnd => _freeLimit;

    private NdsRom(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (_stream.Length < 0x1000)
                throw new InvalidDataException("El archivo es demasiado pequeño para ser una ROM de NDS.");
            byte[] header = ReadAt(0, 0x1000);
            if (Crc16(header.AsSpan(0, HeaderCrcOffset)) != BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(HeaderCrcOffset)))
                throw new InvalidDataException("El CRC de la cabecera NDS no coincide: la ROM está dañada o no es de NDS.");
            Header = new NdsHeader(Ascii(header, 0, 12), Ascii(header, 0x0C, 4), header[0x12], header[0x1E], U32(header, 0x80), U32(header, 0x84));
            _files = ReadFileTable(header);
            _overlayTable = U32(header, 0x50);
            _overlays = ReadOverlayTable(_overlayTable, U32(header, 0x54));
            _regions = OccupiedRegions(header);
            _freeLimit = FreeLimit(header);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public static NdsRom Open(string path) => new(path);

    public void Dispose() => _stream.Dispose();

    public NdsFileEntry File(string path) =>
        _files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal))
        ?? throw new FileNotFoundException("La ROM no contiene el archivo: " + path);

    public byte[] ReadFile(NdsFileEntry entry) => ReadAt(entry.Start, entry.Length);

    public byte[] ReadFile(string path) => ReadFile(File(path));

    public NdsOverlay Overlay(int id) => _overlays.FirstOrDefault(o => o.Id == id)
        ?? throw new InvalidDataException("La ROM no contiene el overlay " + id + ".");

    /// <summary>Overlay contents as the game loads them: BLZ-decompressed when the table marks it compressed.</summary>
    public byte[] ReadOverlay(int id)
    {
        var overlay = Overlay(id);
        byte[] stored = ReadFile(_files[overlay.FileId]);
        return overlay.Compressed ? pk3DS.Core.CTR.BLZCoder.Decompress(stored) : stored;
    }

    private const uint NitroCode = 0xDEC00621;

    /// <summary>
    /// ARM9 binary as stored: its ROM offset, RAM address and size, the 12-byte footer when the binary has one, and where the
    /// module parameters are; <paramref name="Compressed"/> when their "compressed end" field is set (BLZ, as in HeartGold).
    /// </summary>
    private sealed record Arm9Layout(uint Offset, uint RamAddress, uint Size, byte[]? Footer, int ModuleParams, bool Compressed);

    private Arm9Layout ReadArm9Layout()
    {
        byte[] header = ReadAt(0, 0x200);
        uint offset = U32(header, 0x20), ram = U32(header, 0x28), size = U32(header, 0x2C);
        byte[] after = ReadAt(offset + size, 12);
        // DSi-enhanced games (Black, White, White 2) have no footer but a BLZ-compressed ARM9: readable, never rewritten.
        if (U32(after, 0) != NitroCode)
            return new Arm9Layout(offset, ram, size, null, -1, HasBlzFooter(offset, size));
        int moduleParams = checked((int)U32(after, 4));
        bool compressed = moduleParams + 0x18 <= size && U32(ReadAt(offset + (uint)moduleParams + 0x14, 4), 0) != 0 && HasBlzFooter(offset, size);
        return new Arm9Layout(offset, ram, size, after, moduleParams, compressed);
    }

    // BLZ footer: header length 8-11 and a compressed length close to the stored size (UPR-ZX NDSRom.getARM9).
    private bool HasBlzFooter(uint offset, uint size)
    {
        if (size < 8)
            return false;
        byte[] footer = ReadAt(offset + size - 8, 8);
        uint packed = U32(footer, 0) & 0xFFFFFF;
        return footer[3] is >= 0x08 and <= 0x0B && packed > size * 9L / 10 && packed < size * 11L / 10;
    }

    /// <summary>ARM9 binary as the game runs it: BLZ-decompressed when its module parameters mark it compressed.</summary>
    public byte[] ReadArm9()
    {
        var layout = ReadArm9Layout();
        byte[] stored = ReadAt(layout.Offset, checked((int)layout.Size));
        return layout.Compressed ? pk3DS.Core.CTR.BLZCoder.Decompress(stored) : stored;
    }

    // A changed ARM9 is stored the way the original was (compressed or not) and must fit where the original was.
    private byte[] StoreArm9(Arm9Layout layout, byte[] arm9)
    {
        if (Header.IsDsiEnhanced)
            throw new InvalidOperationException("No se modifica el ARM9 de juegos DSi-enhanced: está protegido por las firmas DSi.");
        if (arm9.Length != ReadArm9().Length)
            throw new InvalidDataException($"El ARM9 debe conservar su tamaño ({arm9.Length} bytes recibidos).");
        if (!layout.Compressed)
            return arm9;
        if (layout.ModuleParams < 0)
            throw new InvalidOperationException("El ARM9 comprimido no tiene parámetros de módulo: no se puede reescribir.");
        byte[] packed = pk3DS.Core.CTR.BLZCoder.CompressArm9(arm9);
        if (!pk3DS.Core.CTR.BLZCoder.Decompress(packed).AsSpan().SequenceEqual(arm9) || !packed.AsSpan(0, 0x4000).SequenceEqual(arm9.AsSpan(0, 0x4000)))
            throw new InvalidDataException("La compresión BLZ del ARM9 no reproduce sus datos.");
        if (packed.Length > layout.Size)
            throw new InvalidDataException($"El ARM9 comprimido ({packed.Length} bytes) no cabe en su sitio ({layout.Size} bytes).");
        // The module parameters (in the uncompressed start) tell the loader where the compressed data ends.
        BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(layout.ModuleParams + 0x14), layout.RamAddress + (uint)packed.Length);
        return packed;
    }

    /// <summary>
    /// Writes a new ROM to <paramref name="output"/> with the given file contents replaced. Unchanged bytes are copied verbatim,
    /// so saving without replacements reproduces the original file exactly. The source ROM is never modified.
    /// </summary>
    public void Save(string output, IReadOnlyDictionary<int, byte[]> replacements) => Save(output, replacements, null, null);

    /// <summary>
    /// Like <see cref="Save(string, IReadOnlyDictionary{int, byte[]})"/>, also replacing overlays by id; the decompressed size must
    /// not change. By default an overlay is written uncompressed (compressed flag cleared). Ids in <paramref name="recompress"/> are
    /// BLZ-compressed again, and the result must decompress to the same bytes before it is written. A new <paramref name="arm9"/>
    /// (decompressed, same size) is stored like the original and written in place; it is rejected for DSi-enhanced games.
    /// </summary>
    public void Save(string output, IReadOnlyDictionary<int, byte[]> replacements, IReadOnlyDictionary<int, byte[]>? overlays,
        IReadOnlySet<int>? recompress = null, byte[]? arm9 = null)
    {
        var arm9Layout = arm9 is null ? null : ReadArm9Layout();
        byte[]? arm9Stored = arm9Layout is null ? null : StoreArm9(arm9Layout, arm9!);
        var overlayFiles = new Dictionary<int, (NdsOverlay Overlay, byte[] Data, byte Flags)>();
        foreach (var (id, data) in overlays ?? new Dictionary<int, byte[]>())
        {
            var overlay = Overlay(id);
            if (data.Length != ReadOverlay(id).Length)
                throw new InvalidDataException($"El overlay {id} debe conservar su tamaño descomprimido ({data.Length} bytes recibidos).");
            if (recompress?.Contains(id) == true && overlay.Compressed)
            {
                byte[] packed = pk3DS.Core.CTR.BLZCoder.Compress(data);
                if (!pk3DS.Core.CTR.BLZCoder.Decompress(packed).AsSpan().SequenceEqual(data))
                    throw new InvalidDataException($"La compresión BLZ del overlay {id} no reproduce sus datos.");
                overlayFiles[overlay.FileId] = (overlay, packed, overlay.Flags);
            }
            else
                overlayFiles[overlay.FileId] = (overlay, data, (byte)(overlay.Flags & ~1));
        }
        string target = System.IO.Path.GetFullPath(output);
        if (string.Equals(target, Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La ROM de salida debe ser distinta de la original.");
        if (System.IO.File.Exists(target))
            throw new IOException("Ya existe la ROM de salida: " + target);
        foreach (int id in replacements.Keys)
        {
            if (id < 0 || id >= _files.Count)
                throw new ArgumentOutOfRangeException(nameof(replacements), "Identificador de archivo NDS fuera de rango: " + id);
            // Overlays go through the overlay parameter, which also updates their table entry.
            if (_files[id].Path.StartsWith("overlay/", StringComparison.Ordinal))
                throw new NotSupportedException("Los overlays se reemplazan con el parámetro de overlays: " + _files[id].Path);
        }

        var all = new Dictionary<int, byte[]>(replacements);
        foreach (var (fileId, item) in overlayFiles)
            all[fileId] = item.Data;

        // Plan every write before touching the output, so an oversize file rejects the whole save.
        var placed = new Dictionary<int, uint>();
        uint append = Align(Header.NtrEnd, 0x200);
        foreach (var (id, data) in all.OrderBy(p => p.Key))
        {
            var entry = _files[id];
            if (data.Length <= SlotLength(entry))
            {
                placed[id] = entry.Start;
                continue;
            }
            if ((ulong)append + (ulong)data.Length > _freeLimit || !IsPadding(append, data.Length))
                throw new InvalidDataException($"No hay espacio libre para el archivo {entry.Path} ({data.Length} bytes) en esta ROM.");
            placed[id] = append;
            append = Align(append + (uint)data.Length, 0x200);
        }

        string temp = target + ".partial";
        try
        {
            using (var output2 = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                _stream.Position = 0;
                _stream.CopyTo(output2);
                byte[] header = new byte[0x1000];
                output2.Position = 0;
                output2.ReadExactly(header);
                uint fatOffset = U32(header, 0x48);
                uint ntrEnd = Header.NtrEnd;
                foreach (var (id, data) in all)
                {
                    var entry = _files[id];
                    uint start = placed[id];
                    // Fill what the file no longer uses with the padding byte, so stale data does not remain.
                    if (start != entry.Start)
                        WriteAt(output2, entry.Start, Padding(entry.Length));
                    else if (data.Length < entry.Length)
                        WriteAt(output2, entry.Start + (uint)data.Length, Padding(entry.Length - data.Length));
                    WriteAt(output2, start, data);
                    byte[] fat = new byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(fat, start);
                    BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(4), start + (uint)data.Length);
                    WriteAt(output2, fatOffset + (uint)id * 8, fat);
                    ntrEnd = Math.Max(ntrEnd, Align(start + (uint)data.Length, 4));
                }
                if (arm9Layout is not null && arm9Stored is not null)
                {
                    // Same place; a shorter compressed binary moves its footer up and pads the rest of the old space.
                    uint oldEnd = arm9Layout.Offset + arm9Layout.Size + (arm9Layout.Footer is null ? 0u : 12u);
                    WriteAt(output2, arm9Layout.Offset, arm9Stored);
                    uint end = arm9Layout.Offset + (uint)arm9Stored.Length;
                    if (arm9Layout.Footer is not null)
                    {
                        WriteAt(output2, end, arm9Layout.Footer);
                        end += 12;
                    }
                    if (end < oldEnd)
                        WriteAt(output2, end, new byte[oldEnd - end]);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x2C), (uint)arm9Stored.Length);
                }
                foreach (var (overlay, data, flags) in overlayFiles.Values)
                {
                    byte[] stored = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(stored, ((uint)data.Length & 0xFFFFFF) | ((uint)flags << 24));
                    WriteAt(output2, _overlayTable + (uint)overlay.Index * 32 + 28, stored);
                }
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x80), ntrEnd);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(HeaderCrcOffset), Crc16(header.AsSpan(0, HeaderCrcOffset)));
                WriteAt(output2, 0, header[..0x200]);
                output2.Flush(true);
            }
            System.IO.File.Move(temp, target);
        }
        catch
        {
            if (System.IO.File.Exists(temp))
                System.IO.File.Delete(temp);
            throw;
        }
    }

    /// <summary>Bytes available to a file at its current position before the next occupied region.</summary>
    public long SlotLength(NdsFileEntry entry)
    {
        uint next = _regions.Where(r => r.Start > entry.Start).Select(r => r.Start).DefaultIfEmpty(_freeLimit).Min();
        return Math.Min(next, _freeLimit) - (long)entry.Start;
    }

    private static void WriteAt(FileStream stream, uint offset, byte[] data)
    {
        stream.Position = offset;
        stream.Write(data, 0, data.Length);
    }

    private static byte[] Padding(int length) => Enumerable.Repeat((byte)0xFF, length).ToArray();

    // Relocation targets must be untouched padding in the original dump.
    private bool IsPadding(uint offset, int length) => ReadAt(offset, length).All(b => b == 0xFF);

    private byte[] ReadAt(uint offset, int length)
    {
        if ((ulong)offset + (ulong)length > (ulong)_stream.Length)
            throw new InvalidDataException("Lectura fuera de la ROM NDS.");
        byte[] data = new byte[length];
        _stream.Position = offset;
        _stream.ReadExactly(data);
        return data;
    }

    private List<NdsFileEntry> ReadFileTable(byte[] header)
    {
        uint fntOffset = U32(header, 0x40), fntSize = U32(header, 0x44), fatOffset = U32(header, 0x48), fatSize = U32(header, 0x4C);
        if (fatSize % 8 != 0)
            throw new InvalidDataException("La tabla FAT de la ROM NDS está truncada.");
        byte[] fnt = ReadAt(fntOffset, checked((int)fntSize));
        byte[] fat = ReadAt(fatOffset, checked((int)fatSize));
        int count = (int)(fatSize / 8);
        var names = new string[count];
        int directories = BinaryPrimitives.ReadUInt16LittleEndian(fnt.AsSpan(6));
        if (directories < 1 || directories > 0x1000 || directories * 8 > fnt.Length)
            throw new InvalidDataException("La tabla de nombres de la ROM NDS es inválida.");
        void Walk(int directory, string prefix)
        {
            int at = checked((int)U32(fnt, directory * 8));
            int file = BinaryPrimitives.ReadUInt16LittleEndian(fnt.AsSpan(directory * 8 + 4));
            while (at < fnt.Length && fnt[at] != 0)
            {
                int length = fnt[at] & 0x7F;
                bool isDirectory = (fnt[at] & 0x80) != 0;
                string name = Encoding.ASCII.GetString(fnt, at + 1, length);
                at += 1 + length;
                if (isDirectory)
                {
                    int child = BinaryPrimitives.ReadUInt16LittleEndian(fnt.AsSpan(at)) & 0xFFF;
                    at += 2;
                    if (child >= directories)
                        throw new InvalidDataException("Directorio NDS fuera de rango.");
                    Walk(child, prefix + name + "/");
                }
                else
                {
                    if (file >= count)
                        throw new InvalidDataException("Archivo NDS fuera de rango en la tabla de nombres.");
                    names[file++] = prefix + name;
                }
            }
        }
        Walk(0, "");
        var files = new List<NdsFileEntry>(count);
        for (int id = 0; id < count; id++)
        {
            uint start = U32(fat, id * 8), end = U32(fat, id * 8 + 4);
            if (end < start || end > _stream.Length)
                throw new InvalidDataException("Entrada FAT inválida en la ROM NDS: " + id);
            // Files without a name are overlays; name them after their FAT id.
            files.Add(new NdsFileEntry(id, names[id] ?? $"overlay/{id:0000}", start, end));
        }
        return files;
    }

    private List<NdsOverlay> ReadOverlayTable(uint offset, uint size)
    {
        var overlays = new List<NdsOverlay>();
        if (offset == 0 || size == 0)
            return overlays;
        if (size % 32 != 0)
            throw new InvalidDataException("La tabla de overlays ARM9 está truncada.");
        byte[] table = ReadAt(offset, checked((int)size));
        for (int i = 0; i < table.Length / 32; i++)
        {
            int at = i * 32, fileId = checked((int)U32(table, at + 24));
            if (fileId >= _files.Count)
                throw new InvalidDataException("Overlay ARM9 con archivo fuera de rango: " + i);
            uint stored = U32(table, at + 28);
            overlays.Add(new NdsOverlay(i, checked((int)U32(table, at)), U32(table, at + 4), U32(table, at + 8), fileId, (int)(stored & 0xFFFFFF), (byte)(stored >> 24)));
        }
        return overlays;
    }

    private List<(uint Start, uint End)> OccupiedRegions(byte[] header)
    {
        var regions = new List<(uint, uint)> { (0, U32(header, 0x84)) };
        void Add(int offsetField, int sizeField, uint extra = 0)
        {
            uint start = U32(header, offsetField), size = U32(header, sizeField);
            if (start != 0 && size != 0)
                regions.Add((start, start + size + extra));
        }
        Add(0x20, 0x2C, 12); // ARM9 plus its optional 12-byte footer
        Add(0x30, 0x3C);
        Add(0x40, 0x44);
        Add(0x48, 0x4C);
        Add(0x50, 0x54);
        Add(0x58, 0x5C);
        uint banner = U32(header, 0x68);
        if (banner != 0)
            regions.Add((banner, banner + BannerSize(BinaryPrimitives.ReadUInt16LittleEndian(ReadAt(banner, 2)))));
        if ((header[0x12] & 2) != 0)
        {
            Add(0x1C0, 0x1CC); // ARM9i
            Add(0x1D0, 0x1DC); // ARM7i
            Add(0x1F0, 0x1F4); // digest sector hash table
            Add(0x1F8, 0x1FC); // digest block hash table
        }
        regions.AddRange(_files.Where(f => f.End > f.Start).Select(f => (f.Start, f.End)));
        return regions;
    }

    // Free space after the data: up to the DSi area for DSi-enhanced ROMs, otherwise up to the end of the dump.
    private uint FreeLimit(byte[] header)
    {
        uint limit = (uint)Math.Min(_stream.Length, uint.MaxValue);
        if ((header[0x12] & 2) != 0)
            foreach (int field in new[] { 0x1C0, 0x1D0 })
            {
                uint start = U32(header, field);
                if (start >= Header.NtrEnd && start != 0)
                    limit = Math.Min(limit, start);
            }
        return limit;
    }

    private static uint BannerSize(ushort version) => version switch
    {
        0x0002 => 0x940,
        0x0003 => 0xA40,
        0x0103 => 0x23C0,
        _ => 0x840,
    };

    private static uint Align(uint value, uint alignment) => (value + alignment - 1) & ~(alignment - 1);
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static string Ascii(byte[] data, int offset, int length) => Encoding.ASCII.GetString(data, offset, length).TrimEnd('\0');

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }
}
