#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.NDS;

/// <summary>
/// What else changes with a Gen4 TM or tutor move (UPR-ZX Gen4RomHandler.setTMMoves and setMoveTutorMoves, GPLv3): the TM's item
/// description, the colour of its disc, the lines where people name the move, and HeartGold's Headbutt tutor, which teaches its
/// move from a script instead of the tutor list. The Game Corner and Battle Frontier prize lists keep their text.
/// </summary>
internal static class NdsMoveLists
{
    private const int Tm01Item = 328, IlexForestScript = 92, IlexForestText = 115;
    private static readonly int[] HeadbuttScriptOffsets = [0xF55, 0xFC5, 0x100A, 0x104C];
    // TM disc palette by Gen4 type (Normal … Dark; "???" like Ghost), UPR-ZX typeTMPaletteNumber.
    private static readonly int[] Palettes = [402, 398, 413, 403, 410, 412, 610, 411, 408, 411, 406, 400, 405, 409, 401, 404, 399, 407];
    // Start of the item palette table in the ARM9 (UPR-ZX dpItemPalettesPrefix and pthgssItemPalettesPrefix).
    private static readonly byte[] DpPalettes = Convert.FromHexString("8D018E01210132018D018F0122013301"), PtHgssPalettes = Convert.FromHexString("8D018E01210133018D018F0122013401");

    internal static void FollowUp(Gen4Data data, IReadOnlyList<NdsCell> cells, IReadOnlyDictionary<string, int> edits, Dictionary<string, byte[]> files, byte[]? arm9, List<Gen5Change> changes)
    {
        var profile = data.Profile;
        var changed = cells.Where(c => edits.TryGetValue(c.Key, out int v) && v != c.Value).Select(c => (c.Local, Old: c.Value, New: edits[c.Key])).ToArray();
        var tms = changed.Where(c => c.Local.StartsWith("TM", StringComparison.Ordinal)).Select(c => (Index: int.Parse(c.Local[2..]), c.Old, c.New)).ToArray();
        var headbutt = changed.Where(c => profile.Kind == Gen4Kind.HeartGoldSoulSilver && c.Local == "Tutor" + (profile.TutorCount - 1)).ToArray();
        if (tms.Length == 0 && headbutt.Length == 0)
            return;
        var moveNames = data.Text(profile.MoveNamesText);
        string textPath = data.PathOf(Gen4Profile.Text);
        var text = files.TryGetValue(textPath, out byte[]? draftText) ? Narc.Read(draftText) : data.CopyNarc(Gen4Profile.Text);
        var lines = new Dictionary<int, string[]>();
        string[] Lines(int file) => lines.TryGetValue(file, out var l) ? l : lines[file] = [.. Gen4Text.Read(text.Files[file])];
        void Rename(int file, int oldMove, int newMove)
        {
            string from = moveNames[oldMove], to = moveNames[newMove];
            var l = Lines(file);
            // HeartGold's Roar TM text writes the move in capitals.
            for (int i = 0; i < l.Length; i++)
                l[i] = l[i].Replace(from, to, StringComparison.Ordinal).Replace(from.ToUpperInvariant(), to.ToUpperInvariant(), StringComparison.Ordinal);
        }

        if (tms.Length > 0)
        {
            if (arm9 is null)
                throw new InvalidOperationException("Las MT cambiaron sin ARM9.");
            var moveDescriptions = data.Text(profile.MoveDescriptionsText);
            var moves = data.CopyNarc(Gen4Profile.Moves).Files;
            int width = profile.Kind == Gen4Kind.HeartGoldSoulSilver ? 36 : 38;
            // Found in the original ARM9: TM01's colour is part of the prefix, so it changes with the first edit.
            int palettes = Gen4Data.FindUnique(data.OriginalArm9(), profile.Kind == Gen4Kind.DiamondPearl ? DpPalettes : PtHgssPalettes, "la tabla de colores de los objetos");
            foreach (var (index, oldMove, newMove) in tms)
            {
                // The move's five-line description, rewrapped for the item's three lines.
                Lines(profile.ItemDescriptionsText)[Tm01Item + index] = Rewrap(moveDescriptions[newMove], width);
                foreach (int file in profile.TmTexts.TryGetValue(index + 1, out var texts) ? texts : [])
                    Rename(file, oldMove, newMove);
                int type = moves[newMove].Length > 4 ? moves[newMove][4] : 0;
                BinaryPrimitives.WriteUInt16LittleEndian(arm9.AsSpan(palettes + index * 8 + 2), (ushort)Palettes[type < Palettes.Length ? type : 9]);
            }
            changes.Add(new Gen5Change("Movimientos de las MT", "Textos actualizados", 0, tms.Length));
        }

        foreach (var (_, oldMove, newMove) in headbutt)
        {
            string scriptPath = data.PathOf(Gen4Profile.Scripts);
            var scripts = files.TryGetValue(scriptPath, out byte[]? draftScripts) ? Narc.Read(draftScripts) : data.CopyNarc(Gen4Profile.Scripts);
            byte[] script = scripts.Files[IlexForestScript];
            if (HeadbuttScriptOffsets.Any(o => o + 2 > script.Length || BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(o)) != oldMove))
                throw new InvalidDataException("El script del tutor de Golpe Cabeza no tiene la forma esperada.");
            foreach (int offset in HeadbuttScriptOffsets)
                BinaryPrimitives.WriteUInt16LittleEndian(script.AsSpan(offset), (ushort)newMove);
            files[scriptPath] = scripts.Write();
            Rename(IlexForestText, oldMove, newMove);
            changes.Add(new Gen5Change("Tutor del Bosque Encina", "Textos actualizados", 0, 1));
        }

        foreach (var (file, newLines) in lines)
        {
            var old = Gen4Text.Read(text.Files[file]);
            var edited = Enumerable.Range(0, newLines.Length).Where(i => newLines[i] != old[i]).ToDictionary(i => i, i => newLines[i]);
            if (edited.Count > 0)
                text.Files[file] = Gen4Text.Replace(text.Files[file], edited);
        }
        files[textPath] = text.Write();
    }

    // UPR-ZX rewriteDescriptionForNewLineSize: lines joined, then split again at the given width.
    internal static string Rewrap(string description, int width)
    {
        var words = description.Replace("-\\n", "", StringComparison.Ordinal).Replace("\\n", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string line = "";
        foreach (string word in words)
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line);
                line = "";
            }
            line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0)
            lines.Add(line);
        return string.Join("\\n", lines);
    }
}
