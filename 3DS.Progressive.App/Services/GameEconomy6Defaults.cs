namespace ThreeDS.Progressive.Services;

/// <summary>EconomyFixer.ApplyLegacy: shop exclusions at 1000, then thirteen historical overrides.</summary>
internal static class GameEconomy6Defaults
{
    private static readonly IReadOnlyDictionary<int,int> Prices=Build();
    private static IReadOnlyDictionary<int,int> Build()
    {
        var prices=Enumerable.Range(328,100).Concat(new[]{618,619,620,690,691,692,693,694,701,737}).ToDictionary(id=>id,_=>1000);
        foreach(var pair in new (int Id,int Price)[]{(4,100),(3,150),(2,200),(79,50),(76,50),(77,50),(567,10),(570,10),(93,7500),(565,10),(566,10),(569,10),(568,10)})
            prices[pair.Id]=pair.Price;
        return prices;
    }
    internal static int? TargetPrice(int id)=>Prices.TryGetValue(id,out int price)?price:null;
}
