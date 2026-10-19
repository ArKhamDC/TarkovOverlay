using System.Diagnostics;
using System.Net;
using System.Text.Json;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>
/// Загрузка цен с tarkov.dev через JSON API (https://json.tarkov.dev).
/// GraphQL (api.tarkov.dev/graphql) с июля 2026 отвечает "GraphQL server unavailable",
/// а сам сайт tarkov.dev работает на этих JSON-файлах.
/// </summary>
public sealed class TarkovDevApiClient : IDisposable
{
    private const string BaseUrl = "https://json.tarkov.dev";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public TarkovDevApiClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovOverlay/2.0");
    }

    /// <param name="gameMode">"pve" или "regular"</param>
    public async Task<IReadOnlyList<ItemPrice>> FetchItemsAsync(string gameMode, string lang, CancellationToken ct = default)
    {
        var mode = gameMode == "regular" ? "regular" : "pve";
        var sw = Stopwatch.StartNew();
        AppLog.Info($"[API] Загружаю {BaseUrl}/{mode}/items (+items_{lang}, traders_{lang})...");

        var itemsTask = GetBytesAsync($"{BaseUrl}/{mode}/items", ct);
        var namesTask = GetBytesAsync($"{BaseUrl}/{mode}/items_{lang}", ct);
        var tradersTask = GetBytesAsync($"{BaseUrl}/{mode}/traders_{lang}", ct);
        await Task.WhenAll(itemsTask, namesTask, tradersTask);

        var names = ParseTranslations(namesTask.Result);
        var traderNames = ParseTranslations(tradersTask.Result);

        using var doc = JsonDocument.Parse(itemsTask.Result);
        var itemsNode = doc.RootElement.GetProperty("data").GetProperty("items");

        var result = new List<ItemPrice>(itemsNode.ValueKind == JsonValueKind.Object ? 6000 : 0);
        var skippedNoName = 0;
        foreach (var prop in itemsNode.EnumerateObject())
        {
            var e = prop.Value;
            var id = GetString(e, "id") ?? prop.Name;

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (e.TryGetProperty("types", out var typesNode) && typesNode.ValueKind == JsonValueKind.Array)
                foreach (var t in typesNode.EnumerateArray())
                    if (t.GetString() is { } s) types.Add(s);

            // пресеты оружия в инвентаре не отображаются по своему имени — только мешают сопоставлению
            if (types.Contains("preset"))
                continue;

            var name = names.GetValueOrDefault($"{id} Name");
            var shortName = names.GetValueOrDefault($"{id} ShortName");
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(id, StringComparison.Ordinal))
            {
                skippedNoName++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(shortName) || shortName.StartsWith(id, StringComparison.Ordinal))
                shortName = name;

            TraderOffer? best = null;
            if (e.TryGetProperty("sellToTrader", out var sells) && sells.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sells.EnumerateArray())
                {
                    var price = GetInt(s, "priceRUB") ?? 0;
                    if (price <= 0 || (best is not null && price <= best.Price))
                        continue;
                    var traderId = GetString(s, "trader") ?? "";
                    var traderName = traderNames.GetValueOrDefault($"{traderId} Nickname") ?? traderId;
                    best = new TraderOffer(traderName, price);
                }
            }

            result.Add(new ItemPrice(
                id,
                name.Trim(),
                shortName.Trim(),
                Math.Max(1, GetInt(e, "width") ?? 1),
                Math.Max(1, GetInt(e, "height") ?? 1),
                GetInt(e, "lastLowPrice"),
                GetInt(e, "avg24hPrice"),
                best,
                types.Contains("noFlea")));
        }

        AppLog.Info($"[API] Получено предметов: {result.Count} (без перевода пропущено: {skippedNoName}) за {sw.ElapsedMilliseconds} мс");
        return result;
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var preview = body.Length > 200 ? body[..200] : body;
            throw new HttpRequestException($"{url}: HTTP {(int)response.StatusCode} {preview}");
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static Dictionary<string, string> ParseTranslations(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String)
                dict[p.Name] = p.Value.GetString() ?? "";
        return dict;
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return null;
        var d = v.GetDouble();
        return d >= int.MaxValue ? int.MaxValue : (int)Math.Round(d);
    }

    public void Dispose() => _http.Dispose();
}
