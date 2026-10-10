using System;
using System.Collections.Generic;
using System.Linq;
using pk3DS.Core.Structures;

namespace pk3DS.WinForms;

/// <summary>
/// Enables the Gen 7 item-data flag that allows vitamins to raise EVs past 100.
/// No ExeFS/code.bin patch is needed. The six item records are validated first.
/// </summary>
internal static class Gen7VitaminEVPatcher
{
    // Order: HP, Attack, Defense, Special Attack, Special Defense, Speed.
    internal static readonly string[][] VitaminAliases =
    [
        ["HP Up", "Más PS", "Mas PS"],
        ["Protein", "Proteína", "Proteina"],
        ["Iron", "Hierro"],
        ["Calcium", "Calcio"],
        ["Zinc"],
        ["Carbos", "Carburante"],
    ];

    internal static int Apply(IReadOnlyList<ushort> vitaminIds)
    {
        if (!Main.Config.USUM)
            throw new InvalidOperationException("Vitamin EV 252 is currently supported only for Ultra Sun / Ultra Moon.");

        if (vitaminIds is null || vitaminIds.Count != VitaminAliases.Length ||
            vitaminIds.Distinct().Count() != VitaminAliases.Length)
            throw new InvalidOperationException("Expected six distinct vitamin IDs.");

        var garc = Main.Config.GetGARCData("item");
        byte[][] files = garc.Files;
        var updated = new List<(int Id, byte[] Data)>();

        for (int i = 0; i < vitaminIds.Count; i++)
        {
            int id = vitaminIds[i];
            if (id <= 0 || id >= files.Length || files[id] is null || files[id].Length < 0x24)
                throw new InvalidOperationException($"Vitamin {VitaminAliases[i][0]} has an invalid item record (ID {id}).");

            var item = new Item(files[id]);
            int gain = i switch
            {
                0 => item.EVHP,
                1 => item.EVATK,
                2 => item.EVDEF,
                3 => item.EVSPA,
                4 => item.EVSPD,
                5 => item.EVSPE,
                _ => 0,
            };

            if (gain <= 0)
                throw new InvalidOperationException($"Item {VitaminAliases[i][0]} (ID {id}) does not raise its expected EV stat. No changes were saved.");

            const int VitaminPrice = 200;

            if ((item.FunctionFlags1 & ItemFlags2.AddEVAbove100) == 0 ||
                item.BuyPrice != VitaminPrice)
            {
                item.FunctionFlags1 |= ItemFlags2.AddEVAbove100;
                item.BuyPrice = VitaminPrice;

                updated.Add((id, item.Write()));
            }
        }

        // Only update after ALL six expected items have been validated.
        if (updated.Count == 0)
            return 0;

        foreach (var (id, data) in updated)
            files[id] = data;

        garc.Files = files;
        garc.Save();
        return updated.Count;
    }
}
