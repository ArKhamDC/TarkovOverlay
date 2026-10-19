using System.Text.Json.Serialization;

namespace TarkovOverlay.Models;

/// <summary>Лучшая цена продажи торговцу.</summary>
public sealed record TraderOffer(string Trader, int Price);

/// <summary>Предмет и его цены (все цены в рублях).</summary>
public sealed record ItemPrice(
    string Id,
    string Name,
    string ShortName,
    int Width,
    int Height,
    int? LastLowPrice,
    int? Avg24hPrice,
    TraderOffer? BestTrader,
    bool NoFlea)
{
    [JsonIgnore] public int Slots => Math.Max(1, Width * Height);

    /// <summary>Цена на барахолке: последняя минимальная, иначе средняя за 24ч.</summary>
    [JsonIgnore] public int? FleaPrice => NoFlea ? null : (LastLowPrice > 0 ? LastLowPrice : Avg24hPrice > 0 ? Avg24hPrice : null);

    /// <summary>Лучшая из цен (барахолка / торговец).</summary>
    [JsonIgnore] public int? BestPrice
    {
        get
        {
            var flea = FleaPrice ?? 0;
            var trader = BestTrader?.Price ?? 0;
            var best = Math.Max(flea, trader);
            return best > 0 ? best : null;
        }
    }

    [JsonIgnore] public int? PerSlot => BestPrice is { } p ? p / Slots : null;

    [JsonIgnore] public bool HasAnyPrice => BestPrice is not null;
}

public enum MarkerStyle { Compact, Panel, Status, Tooltip }

/// <summary>Метка на оверлее. X/Y — физические пиксели экрана (для Compact/Panel X — правый край метки).</summary>
public sealed record PriceMarker(double X, double Y, IReadOnlyList<string> Lines, MarkerStyle Style, double MaxW = 0, double MaxH = 0);

public sealed record OcrWord(string Text, int X, int Y, int Width, int Height, float Confidence);

public sealed record OcrLine(string Text, int X, int Y, int Width, int Height, IReadOnlyList<OcrWord> Words);

public enum MatchKind { FullName, ShortName }

public sealed record MatchedItem(ItemPrice Item, int X, int Y, int Width, int Height, int Score, MatchKind Kind, string OcrText)
{
    /// <summary>Несколько равноценных вариантов (диапазон цен "≈"); null — предмет определён однозначно.</summary>
    public IReadOnlyList<ItemPrice>? Variants { get; init; }
    public string Source { get; init; } = "";
    public bool Ambiguous => Source.EndsWith(":?", StringComparison.Ordinal);
}
