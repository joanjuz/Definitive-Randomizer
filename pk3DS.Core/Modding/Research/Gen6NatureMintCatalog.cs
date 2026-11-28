#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace pk3DS.Core.Modding.Research;

public sealed record Gen6NatureMint(int NatureId, string EnglishNature);
public sealed record Gen6MintItem(int ItemId, int NatureId);

/// <summary>Nature metadata only. No XY item slots have been accredited by this catalog.</summary>
public static class Gen6NatureMintCatalog
{
    public const int ItemCount = 718;
    public static IReadOnlyList<Gen6NatureMint> Natures { get; } = Array.AsReadOnly(new[] {
        new Gen6NatureMint(1,"Lonely"),new(3,"Adamant"),new(4,"Naughty"),new(2,"Brave"),
        new(5,"Bold"),new(8,"Impish"),new(9,"Lax"),new(7,"Relaxed"),
        new(15,"Modest"),new(16,"Mild"),new(19,"Rash"),new(17,"Quiet"),
        new(20,"Calm"),new(21,"Gentle"),new(23,"Careful"),new(22,"Sassy"),
        new(10,"Timid"),new(11,"Hasty"),new(13,"Jolly"),new(14,"Naive"),new(12,"Serious") });
    public static Gen6MintItemMap? OfficialItemMap => null;
    /// <summary>XY-only candidate allocation, gated until independent gameplay acceptance.
    /// Includes the audited legacy Gen5 letter slot 623; never use a synthetic test map.</summary>
    public static Gen6MintItemMap ExperimentalItemMap { get; } = new(
        new[]{113,114,115,120,121,122,123,124,125,126,127,128,129,130,131,132,133,426,427,622,623}
        .Select((item,index)=>new Gen6MintItem(item,Natures[index].NatureId)));
}

/// <summary>A validated mapping, not evidence that these item slots are safe in a game.</summary>
public sealed class Gen6MintItemMap
{
    public IReadOnlyList<Gen6MintItem> Items { get; }
    public Gen6MintItemMap(IEnumerable<Gen6MintItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy=items.ToArray();
        if(copy.Length!=21 || copy.Any(m=>m is null || m.ItemId<=0 || m.ItemId>=Gen6NatureMintCatalog.ItemCount) ||
            copy.Select(m=>m.ItemId).Distinct().Count()!=21 ||
            !copy.Select(m=>m.NatureId).OrderBy(n=>n).SequenceEqual(Gen6NatureMintCatalog.Natures.Select(n=>n.NatureId).OrderBy(n=>n)))
            throw new InvalidDataException("El mapa Gen6 requiere 21 objetos únicos entre 1 y 717 y las 21 naturalezas exactas.");
        Items=Array.AsReadOnly(copy);
    }
}
