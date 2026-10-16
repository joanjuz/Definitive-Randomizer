using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ThreeDS.Progressive.Services;

internal sealed record TitleDarcEntry(int Id, string Name, int Offset, int Length, bool Folder);
internal sealed class TitleDarcCodec
{
    private readonly byte[] _source;
    private readonly int _prefix, _table;
    internal IReadOnlyList<TitleDarcEntry> Entries { get; }
    private static uint U(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
    internal TitleDarcCodec(byte[] source)
    {
        if (source.Length < 40 || source.Length > 128 * 1024 * 1024) throw new InvalidDataException("Tamaño DARC inválido.");
        _source = (byte[])source.Clone();
        int prefix = -1;
        for (int i = 0; i <= source.Length - 28; i += 4)
            if (source.AsSpan(i, 4).SequenceEqual("darc"u8)) { prefix = i; break; }
        if (prefix < 0) throw new InvalidDataException("No se encontró un encabezado DARC alineado.");
        _prefix = prefix;
        if (BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(prefix + 4)) != 0xFEFF ||
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(prefix + 6)) != 28 ||
            U(source, prefix + 12) != source.Length - prefix) throw new InvalidDataException("Encabezado DARC incompatible.");
        int table = checked((int)U(source, prefix + 16)), tableSize = checked((int)U(source, prefix + 20));
        int data = checked((int)U(source, prefix + 24));
        if (table < 28 || tableSize < 12 || (long)table + tableSize > data || data > source.Length - prefix)
            throw new InvalidDataException("Tabla DARC fuera de rango.");
        _table = prefix + table;
        int count = checked((int)U(source, _table + 8));
        if (count < 1 || count > 10000 || count * 12L > tableSize || U(source, _table) >> 24 != 1)
            throw new InvalidDataException("Raíz DARC inválida.");
        int names = _table + count * 12, endNames = _table + tableSize;
        var entries = new List<TitleDarcEntry>();
        for (int i = 0; i < count; i++)
        {
            int at = _table + i * 12; uint flags = U(source, at); bool folder = flags >> 24 == 1;
            if (flags >> 24 > 1) throw new InvalidDataException("Tipo de entrada DARC inválido.");
            int name = checked(names + (int)(flags & 0xFFFFFF));
            if (name < names || name >= endNames || (name - names) % 2 != 0) throw new InvalidDataException("Nombre DARC fuera de rango.");
            int end = name; while (end + 1 < endNames && BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(end)) != 0) end += 2;
            if (end + 1 >= endNames) throw new InvalidDataException("Nombre DARC sin terminador.");
            string text = new UnicodeEncoding(false, false, true).GetString(source, name, end - name);
            int offset = checked((int)U(source, at + 4)), length = checked((int)U(source, at + 8));
            if (folder ? offset >= count || length <= i || length > count : offset < data || length < 1 || (long)offset + length > source.Length - prefix)
                throw new InvalidDataException("Entrada DARC fuera de rango.");
            entries.Add(new(i, text, offset, length, folder));
        }
        var files = entries.Where(e => !e.Folder).OrderBy(e => e.Offset).ToArray();
        for (int i = 1; i < files.Length; i++)
            if ((long)files[i - 1].Offset + files[i - 1].Length > files[i].Offset) throw new InvalidDataException("Archivos DARC solapados.");
        Entries = entries.AsReadOnly();
    }
    internal byte[] Read(int id)
    {
        var entry = Entry(id); return _source.AsSpan(_prefix + entry.Offset, entry.Length).ToArray();
    }
    private TitleDarcEntry Entry(int id) => id >= 0 && id < Entries.Count && !Entries[id].Folder
        ? Entries[id] : throw new ArgumentException("Seleccioná un archivo DARC válido.");
    internal byte[] Replace(int id, byte[] replacement)
    {
        var entry = Entry(id);
        if (replacement.Length < 1 || replacement.Length > 64 * 1024 * 1024) throw new InvalidDataException("Tamaño de reemplazo inválido.");
        // Shift by a multiple of 128 to preserve every existing texture alignment.
        int delta = checked(((replacement.Length - entry.Length + 127) / 128) * 128);
        // C# truncates negative division toward zero; choose floor-to-alignment for shrinking.
        if (replacement.Length < entry.Length) delta = -((entry.Length - replacement.Length) / 128) * 128;
        int start = _prefix + entry.Offset, oldEnd = start + entry.Length;
        byte[] output = new byte[checked(_source.Length + delta)];
        _source.AsSpan(0, start).CopyTo(output);
        replacement.CopyTo(output, start);
        _source.AsSpan(oldEnd).CopyTo(output.AsSpan(oldEnd + delta));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(_prefix + 12), checked((uint)(output.Length - _prefix)));
        foreach (var file in Entries.Where(e => !e.Folder))
        {
            int at = _table + file.Id * 12;
            if (file.Id == id) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 8), (uint)replacement.Length);
            else if (file.Offset >= entry.Offset + entry.Length)
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 4), checked((uint)(file.Offset + delta)));
        }
        var check = new TitleDarcCodec(output);
        foreach (var file in Entries.Where(e => !e.Folder))
            if (!check.Read(file.Id).SequenceEqual(file.Id == id ? replacement : Read(file.Id)))
                throw new InvalidDataException("La relectura DARC no conserva los archivos.");
        return output;
    }
}
