using TarkovOverlay.Models;

namespace TarkovOverlay.Utils;

public static class PriceFormat
{
    public static string NoData => L.T("нет данных", "no data");
    private static string F => L.T("Б", "F");
    private static string Sl => L.T("сл", "slot");

    public static string Short(int? price)
    {
        if (price is not { } p || p <= 0)
            return "—";
        return p >= 1_000_000 ? $"{p / 1_000_000.0:0.##}M"
             : p >= 10_000 ? $"{p / 1_000.0:0}k"
             : p >= 1_000 ? $"{p / 1_000.0:0.#}k"
             : p.ToString();
    }

    /// <summary>Короткая метка под надписью плитки. Нет данных / нельзя продать — этой части просто нет. Пусто = метки нет.</summary>
    public static IReadOnlyList<string> CompactLines(ItemPrice item)
    {
        var lines = new List<string>();
        if (item.FleaPrice is > 0)
            lines.Add($"{F} {Short(item.FleaPrice)}");
        if (item.BestTrader is { } t && t.Price > 0)
            lines.Add($"{TraderAbbrev(t.Trader)} {Short(t.Price)}");
        if (lines.Count > 0 && item.Slots > 1 && item.PerSlot is > 0)
            lines.Add($"{Short(item.PerSlot)}/{Sl}");
        return lines.Count > 0 ? lines : ["X"];
    }

    /// <summary>Подробная панель у курсора (без строк про отсутствие данных).</summary>
    public static IReadOnlyList<string> PanelLines(ItemPrice item)
    {
        var lines = new List<string>();
        var size = item.Slots > 1 ? $"  ({item.Width}×{item.Height})" : "";
        lines.Add(item.Name + size);
        if (!item.HasAnyPrice)
            return lines;
        if (item.FleaPrice is > 0)
        {
            var avg = item.Avg24hPrice is > 0 && item.Avg24hPrice != item.FleaPrice ? L.T($"  (ср. 24ч {Short(item.Avg24hPrice)})", $"  (avg 24h {Short(item.Avg24hPrice)})") : "";
            lines.Add($"{L.T("Барахолка", "Flea")}: {Short(item.FleaPrice)}{avg}");
        }
        if (item.BestTrader is { } t && t.Price > 0)
            lines.Add($"{t.Trader}: {Short(t.Price)}");
        if (item.PerSlot is > 0)
            lines.Add($"{L.T("За слот", "Per slot")}: {Short(item.PerSlot)}");
        return lines;
    }

    private static string Range(IEnumerable<int?> values)
    {
        var v = values.Where(x => x is > 0).Select(x => x!.Value).ToList();
        if (v.Count == 0) return "—";
        var (a, b) = (v.Min(), v.Max());
        return Short(a) == Short(b) ? Short(a) : $"{Short(a)}–{Short(b)}";
    }

    /// <summary>Метка для одного предмета или набора одинаковых по размеру вариантов (диапазон цен).</summary>
    public static IReadOnlyList<string> CompactLines(IReadOnlyList<ItemPrice>? variants, ItemPrice item)
    {
        if (variants is null || variants.Count < 2)
            return CompactLines(item);
        var lines = new List<string>();
        if (variants.Any(i => i.FleaPrice is > 0))
            lines.Add($"≈ {F} {Range(variants.Select(i => i.FleaPrice))}");
        if (variants.Any(i => i.BestTrader is not null))
            lines.Add($"{L.T("Тор", "Tr")} {Range(variants.Select(i => i.BestTrader?.Price))}");
        if (lines.Count > 0 && item.Slots > 1)
            lines.Add($"{Range(variants.Select(i => i.PerSlot))}/{Sl}");
        return lines.Count > 0 ? lines : ["X"];
    }

    /// <summary>Неоднозначное название: по строке на каждого кандидата (имя + размер + цены).</summary>
    public static IReadOnlyList<string> AmbiguousLines(IReadOnlyList<ItemPrice> candidates)
    {
        var lines = new List<string>();
        foreach (var c in candidates.Where(c => c.HasAnyPrice).OrderByDescending(c => c.BestPrice).Take(3))
        {
            var prices = new List<string>();
            if (c.FleaPrice is > 0) prices.Add($"{F} {Short(c.FleaPrice)}");
            if (c.BestTrader is { } t && t.Price > 0) prices.Add($"{TraderAbbrev(t.Trader)} {Short(t.Price)}");
            lines.Add($"{DistinctName(c, candidates)}: {string.Join(" ", prices)}");
        }
        return lines.Count > 0 ? lines : ["X"];
    }

    public static IReadOnlyList<string> PanelLines(IReadOnlyList<ItemPrice>? variants, ItemPrice item)
    {
        if (variants is null || variants.Count < 2)
            return PanelLines(item);
        var lines = new List<string> { L.T($"{item.ShortName}: {variants.Count} варианта", $"{item.ShortName}: {variants.Count} variants") };
        lines.AddRange(AmbiguousLines(variants));
        return lines;
    }

    /// <summary>Отличающая часть полного названия (слова, которых нет у других кандидатов).</summary>
    private static string DistinctName(ItemPrice c, IReadOnlyList<ItemPrice> all)
    {
        var words = c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var others = all.Where(o => o.Id != c.Id).SelectMany(o => o.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.ToLowerInvariant()).ToHashSet();
        var uniq = words.Where(w => !others.Contains(w.ToLowerInvariant())).ToList();
        if (uniq.Count == 0) uniq = words.ToList();
        var s = string.Join(" ", uniq.Take(2)).Trim('"', '(', ')');
        return s.Length > 14 ? s[..13] + "." : s;
    }

    /// <summary>Панель под подсказкой игры (название уже видно в подсказке).</summary>
    public static IReadOnlyList<string> TooltipLines(ItemPrice item, string? header = null, bool flea = true)
    {
        var lines = new List<string>();
        if (header is not null) lines.Add(header);
        var h = lines.Count;
        if (flea && item.FleaPrice is > 0)
        {
            var avg = item.Avg24hPrice is > 0 && item.Avg24hPrice != item.FleaPrice ? L.T($"  (ср. {Short(item.Avg24hPrice)})", $"  (avg {Short(item.Avg24hPrice)})") : "";
            lines.Add($"{L.T("Барахолка", "Flea")}: {Short(item.FleaPrice)}{avg}");
        }
        if (item.BestTrader is { } t && t.Price > 0)
            lines.Add($"{t.Trader}: {Short(t.Price)}");
        var per = flea ? item.PerSlot : (item.BestTrader?.Price is > 0 ? item.BestTrader.Price / item.Slots : null);
        if (lines.Count > h && per is > 0)
            lines.Add(item.Slots > 1 ? $"{L.T("За слот", "Per slot")}: {Short(per)} ({item.Width}×{item.Height})" : $"{L.T("За слот", "Per slot")}: {Short(per)}");
        if (lines.Count == h) lines.Add("X");
        return lines;
    }

    private static string TraderAbbrev(string trader) => trader.Length <= 4 ? trader : trader[..3];
}
