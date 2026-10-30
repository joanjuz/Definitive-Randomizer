using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NX;

// Format reference: kwsch/pkNX PersonalInfo8SWSH (GPL-3.0), see docs/3ds-progressive/SWITCH_SWSH_PROTOTIPO.md.
public enum SwordShieldField { HP, Attack, Defense, Speed, SpecialAttack, SpecialDefense, Type1, Type2, CatchRate, Ability1, Ability2, AbilityHidden, Item1, Item2, Item3, Gender, HatchCycles, BaseFriendship, Growth, EggGroup1, EggGroup2, Color, BaseExperience, Height, Weight, YieldHP, YieldAttack, YieldDefense, YieldSpeed, YieldSpecialAttack, YieldSpecialDefense }
public sealed record SwordShieldPersonalEntry(int Index, int Species, int Form, bool Present, IReadOnlyList<int> Values)
{
    public string Name { get; init; } = "";
    public int BST => Values[0] + Values[1] + Values[2] + Values[3] + Values[4] + Values[5];
    public string Label => Species == 0 ? $"Especie sin resolver · entrada {Index:0000}" : $"{Species:000} {Name} · forma {Form} · BST {BST} · entrada {Index:0000}";
}

/// <summary>Restricted, lossless editor. Copies all bytes and changes only an explicitly selected byte.</summary>
public sealed class SwordShieldPersonalTable
{
    public const int EntrySize = 0xB0;
    public const int FieldCount = 31;
    public const int EntryCount = 1192;
    public const int MaxSpecies = 898;
    private readonly byte[] _data;
    private readonly int[] _species = new int[EntryCount];
    private readonly int[] _forms = new int[EntryCount];
    public SwordShieldPersonalTable(ReadOnlySpan<byte> data)
    {
        if (data.Length != EntrySize * EntryCount)
            throw new InvalidDataException("personal_total.bin no coincide con el perfil de Espada/Escudo 1.3.2 (1192 entradas de 176 bytes).");
        _data = data.ToArray();
        for (int i = 1; i < EntryCount; i++)
        {
            int at = i * EntrySize;
            if ((_data[at + 0x21] & 0x40) == 0) continue;
            for (int s = 0; s < 6; s++)
                if (_data[at + s] == 0) throw new InvalidDataException($"Estadística cero en la entrada {i}.");
            if (_data[at + 6] > 17 || _data[at + 7] > 17)
                throw new InvalidDataException($"Tipo desconocido en la entrada {i}.");
            int first = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at + 0x1E));
            int count = _data[at + 0x20];
            if (first != 0 && (first >= EntryCount || first + Math.Max(0, count - 2) >= EntryCount))
                throw new InvalidDataException($"Índice de formas fuera de la tabla en la entrada {i}.");
        }
        // Base rows use National Dex order. ModelID at 0x4C is a graphics identifier,
        // which diverges from the species number (e.g. Grookey 810 has model 948).
        for (int species = 1; species <= MaxSpecies; species++) _species[species] = species;
        for (int species = 1; species <= MaxSpecies; species++)
        {
            int at = species * EntrySize;
            int first = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at + 0x1E));
            int count = _data[at + 0x20];
            if (first == 0 || count <= 1) continue;
            if (first <= MaxSpecies || first + count - 2 >= EntryCount)
                throw new InvalidDataException($"Vínculo de formas inválido para la especie {species}.");
            for (int form = 1; form < count; form++)
            {
                int entry = first + form - 1;
                if (_species[entry] != 0)
                    throw new InvalidDataException($"La entrada de forma {entry} pertenece a más de una especie.");
                _species[entry] = species; _forms[entry] = form;
            }
        }
        if (Entries().Count == 0) throw new InvalidDataException("La tabla no contiene entradas presentes en el juego.");
    }
    public SwordShieldPersonalEntry Read(int index)
    {
        if (index <= 0 || index >= EntryCount) throw new ArgumentOutOfRangeException(nameof(index));
        int at = index * EntrySize;
        var values = new int[FieldCount];
        for (int i = 0; i < values.Length; i++)
        {
            int offset=at+FieldOffset((SwordShieldField)i);
            values[i]=i>=25?(BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset))>>(2*(i-25)))&3:i==21?_data[offset]&63:i is >=12 and <=14?BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(offset)):i is >=9 and <=11 or >=22 and <=24?BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset)):_data[offset];
        }
        return new(index, _species[index], _forms[index], (_data[at + 0x21] & 0x40) != 0, Array.AsReadOnly(values));
    }
    public IReadOnlyList<SwordShieldPersonalEntry> Entries()
    {
        var entries = new List<SwordShieldPersonalEntry>();
        for (int i = 1; i < EntryCount; i++) if ((_data[i * EntrySize + 0x21] & 0x40) != 0) entries.Add(Read(i));
        return entries.AsReadOnly();
    }
    public byte[] WithValues(IReadOnlyList<int> indices,SwordShieldField field,int value)
    {
        ValidateValue(field,value);if(indices.Count==0||indices.Distinct().Count()!=indices.Count)throw new InvalidDataException("Alcance de Pokémon vacío o duplicado.");
        foreach(int index in indices){var entry=Read(index);if(!entry.Present||entry.Species==0)throw new InvalidDataException("Entrada no editable en el alcance.");}
        var output=ToArray();foreach(int index in indices)WriteValue(output,index,field,value);return output;
    }
    public byte[] WithValue(int index, SwordShieldField field, int value)
    {
        var entry = Read(index);
        if (!entry.Present) throw new InvalidDataException("La entrada no está presente en este juego.");
        if (entry.Species == 0) throw new InvalidDataException("No se pudo resolver la especie de esta forma; no se permite editarla.");
        ValidateValue(field, value);
        var result = ToArray(); WriteValue(result, index, field, value); return result;
    }
    public static int FieldOffset(SwordShieldField field)
    {
        int id=(int)field;if(id<0||id>=FieldCount)throw new ArgumentOutOfRangeException(nameof(field));
        return id<9?id:id<12?0x18+(id-9)*2:id<15?0x0C+(id-12)*2:id<21?0x12+id-15:id==21?0x21:id<25?0x22+(id-22)*2:0x0A;
    }
    public static (int Min,int Max) Range(SwordShieldField field)
    {
        _=FieldOffset(field);int id=(int)field;
        return id<6?(1,255):id is 6 or 7?(0,17):id==8?(0,255):id<12?(1,267):id<15?(-1,1607):id==18?(0,5):id is 19 or 20?(0,15):id==21?(0,9):id>=25?(0,3):id>=22?(0,65535):(0,255);
    }
    public static void ValidateValue(SwordShieldField field,int value)
    {var range=Range(field);if(value<range.Min||value>range.Max)throw new ArgumentOutOfRangeException(nameof(value),$"Rango permitido: {range.Min}–{range.Max}.");}
    internal static void WriteValue(byte[] output,int index,SwordShieldField field,int value)
    {
        ValidateValue(field,value);int id=(int)field,at=index*EntrySize+FieldOffset(field);
        if(id>=25){int shift=(id-25)*2;ushort word=BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(at));BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at),(ushort)((word&~(3<<shift))|(value<<shift)));}
        else if(id==21)output[at]=(byte)((output[at]&0xC0)|value);
        else if(id is >=9 and <=14 or >=22 and <=24)BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at),unchecked((ushort)value));
        else output[at]=(byte)value;
    }

    public byte[] ToArray() => (byte[])_data.Clone();
}
