// Upstream license: GPL-3.0; see docs/third-party/PokeFeur-LICENSE.txt.
// Registry builder adapted from JonatanUHC/PokeFeurRandomSWSH CustomSwShRandomizer.ps1
// commit e69afd5b9d4e5f892237bb7fb30aed10101d9c0a. Strict schema checks added locally.
using System;
using System.Collections.Generic;
using System.Text;
namespace pk3DS.Core.NX;
public static class PokeFeurScriptRegistry
{
    private sealed class Entry
    {
        public ulong ScriptId;
        public string AmxPath;
        public string TextPath;
    }

    private static ushort ReadU16(byte[] data, int offset) { return BitConverter.ToUInt16(data, offset); }
    private static uint ReadU32(byte[] data, int offset) { return BitConverter.ToUInt32(data, offset); }
    private static ulong ReadU64(byte[] data, int offset) { return BitConverter.ToUInt64(data, offset); }
    private static int ReadI32(byte[] data, int offset) { return BitConverter.ToInt32(data, offset); }

    private static int GetFieldAddress(byte[] data, int tablePos, int fieldIndex)
    {
        if (tablePos < 4 || tablePos >= data.Length) return -1;
        int vtablePos = tablePos - ReadI32(data, tablePos);
        if (vtablePos < 0 || vtablePos + 4 > data.Length) return -1;
        int vtableLength = ReadU16(data, vtablePos);
        int slotPos = vtablePos + 4 + (fieldIndex * 2);
        if (slotPos + 2 > vtablePos + vtableLength) return -1;
        int fieldOffset = ReadU16(data, slotPos);
        if (fieldOffset == 0) return -1;
        int fieldAddress = tablePos + fieldOffset;
        return (fieldAddress >= 0 && fieldAddress < data.Length) ? fieldAddress : -1;
    }

    private static string ReadString(byte[] data, int fieldAddress)
    {
        if (fieldAddress < 0 || fieldAddress + 4 > data.Length) return "";
        int stringPos = fieldAddress + (int)ReadU32(data, fieldAddress);
        if (stringPos < 0 || stringPos + 4 > data.Length) return "";
        int length = (int)ReadU32(data, stringPos);
        if (length < 0 || stringPos + 4 + length > data.Length) return "";
        return Encoding.UTF8.GetString(data, stringPos + 4, length);
    }

    private static List<Entry> Parse(byte[] data)
    {
        var fb=new SwitchFlatBuffer(data);
        void Known(int table,int fields){int vt=table-fb.I32(table);if(fb.U16(vt)>4+2*fields||fb.U16(vt+2)>(fields==1?8:24))throw new System.IO.InvalidDataException("Registro de scripts con campos desconocidos; no se reescribe.");}
        Known(fb.Root,1);var entries=new List<Entry>();var seen=new HashSet<ulong>();
        foreach(int table in fb.Tables(fb.Root,0,true)){
            Known(table,3);int at=fb.Field(table,0,8);if(at<0)throw new System.IO.InvalidDataException("ID de script ausente.");
            ulong id=fb.U64(at);if(!seen.Add(id))throw new System.IO.InvalidDataException("ID de script duplicado.");
            entries.Add(new Entry{ScriptId=id,AmxPath=fb.String(table,1),TextPath=fb.String(table,2)});
        }
        return entries;
    }
    public sealed record ScriptRecord(ulong Id,string Amx,string Text);
    public static IReadOnlyList<ScriptRecord> Read(byte[] data)=>Parse(data).ConvertAll(e=>new ScriptRecord(e.ScriptId,e.AmxPath,e.TextPath)).AsReadOnly();
    private static ulong Fnv1a64(string text)
    {
        unchecked
        {
            ulong hash = 0xCBF29CE484222645UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 0x00000100000001B3UL;
            }
            return hash;
        }
    }

    private static void Upsert(List<Entry> entries, ulong scriptId, string amxPath, string textPath)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].ScriptId == scriptId)
            {
                entries[i].AmxPath = amxPath;
                entries[i].TextPath = textPath;
                return;
            }
        }
        entries.Add(new Entry { ScriptId = scriptId, AmxPath = amxPath, TextPath = textPath });
    }

    private static void AddU16(List<byte> bytes, int value) { bytes.AddRange(BitConverter.GetBytes((ushort)value)); }
    private static void AddU32(List<byte> bytes, uint value) { bytes.AddRange(BitConverter.GetBytes(value)); }
    private static void AddI32(List<byte> bytes, int value) { bytes.AddRange(BitConverter.GetBytes(value)); }
    private static void AddU64(List<byte> bytes, ulong value) { bytes.AddRange(BitConverter.GetBytes(value)); }

    private static void SetU32(List<byte> bytes, int offset, uint value)
    {
        byte[] raw = BitConverter.GetBytes(value);
        for (int i = 0; i < 4; i++) bytes[offset + i] = raw[i];
    }

    private static void Align(List<byte> bytes, int alignment)
    {
        while ((bytes.Count % alignment) != 0) bytes.Add(0);
    }

    private static byte[] Build(List<Entry> entries)
    {
        List<byte> bytes = new List<byte>();
        List<int> vectorOffsetLocations = new List<int>();
        List<int> tablePositions = new List<int>();
        List<Tuple<int, string>> stringRefs = new List<Tuple<int, string>>();

        AddU32(bytes, 0);
        int rootVtable = bytes.Count;
        AddU16(bytes, 6);
        AddU16(bytes, 8);
        AddU16(bytes, 4);
        Align(bytes, 4);

        int rootTable = bytes.Count;
        SetU32(bytes, 0, (uint)rootTable);
        AddI32(bytes, rootTable - rootVtable);
        int rootVectorField = bytes.Count;
        AddU32(bytes, 0);

        int vectorPos = bytes.Count;
        AddU32(bytes, (uint)entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            vectorOffsetLocations.Add(bytes.Count);
            AddU32(bytes, 0);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            int vtablePos = bytes.Count;
            AddU16(bytes, 10);
            AddU16(bytes, 24);
            AddU16(bytes, 8);
            AddU16(bytes, 16);
            AddU16(bytes, 20);
            Align(bytes, 8);

            int tablePos = bytes.Count;
            tablePositions.Add(tablePos);
            AddI32(bytes, tablePos - vtablePos);
            AddU32(bytes, 0);
            AddU64(bytes, entry.ScriptId);
            int amxOffsetLocation = bytes.Count;
            AddU32(bytes, 0);
            int textOffsetLocation = bytes.Count;
            AddU32(bytes, 0);
            stringRefs.Add(Tuple.Create(amxOffsetLocation, entry.AmxPath ?? ""));
            stringRefs.Add(Tuple.Create(textOffsetLocation, entry.TextPath ?? ""));
        }

        SetU32(bytes, rootVectorField, (uint)(vectorPos - rootVectorField));
        for (int i = 0; i < tablePositions.Count; i++)
        {
            SetU32(bytes, vectorOffsetLocations[i], (uint)(tablePositions[i] - vectorOffsetLocations[i]));
        }

        foreach (Tuple<int, string> item in stringRefs)
        {
            Align(bytes, 4);
            int stringPos = bytes.Count;
            byte[] raw = Encoding.UTF8.GetBytes(item.Item2);
            AddU32(bytes, (uint)raw.Length);
            bytes.AddRange(raw);
            bytes.Add(0);
            SetU32(bytes, item.Item1, (uint)(stringPos - item.Item1));
        }

        return bytes.ToArray();
    }

    public static byte[] Patch(byte[] data, bool runtimePcControls, bool earlyBikeEnabled, bool dexAnimationsEnabled)
    {
        if(!runtimePcControls&&!earlyBikeEnabled&&!dexAnimationsEnabled)return (byte[])data.Clone();
        List<Entry> entries = Parse(data);
        if (entries.Count == 0) throw new ArgumentException("El registro de scripts está vacío o es inválido.");

        if (earlyBikeEnabled)
        {
            Upsert(entries, Fnv1a64("grant_early_bike"), "bin/script/amx/early_bike.amx", "script/script_dummy.dat");
        }

        if (runtimePcControls)
        {
            Upsert(entries, Fnv1a64("randomizer_pc_menu"), "bin/script/amx/randomizer_pc_menu.amx", "script/script_dummy.dat");
            Upsert(entries, 13435777772824609090UL, "bin/script/amx/randomizer_pc_menu.amx", "script/pokemoncenter.dat");
        }

        if (dexAnimationsEnabled)
        {
            // Le module fait apparaitre un objet de terrain dont le nom de script est
            // getFNV1aHashedString("dex_dialog") (patches/dex_animations.hpp). Sans cette
            // entree l'objet apparait mais l'interaction ne lance aucun script.
            Upsert(entries, Fnv1a64("dex_dialog"), "bin/script/amx/dex_animations.amx", "script/script_dummy.dat");
        }

        var result=Build(entries);var checkedEntries=Read(result);if(checkedEntries.Count!=entries.Count)throw new System.IO.InvalidDataException("Reapertura del registro fallida.");return result;
    }
}
