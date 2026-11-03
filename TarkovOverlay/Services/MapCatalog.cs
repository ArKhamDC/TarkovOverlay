using System.Net;
using System.Text.Json;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

internal sealed class MapCatalog : IDisposable
{
    private const int CacheVersion = 1;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    private readonly HttpClient _http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public MapCatalog() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovOverlay/2.0");

    public IReadOnlyList<GameMap> Maps { get; private set; } = [];

    public async Task LoadAsync(CancellationToken ct)
    {
        var cache = Path.Combine(AppSettings.Dir, "maps_cache.json");
        var cached = ReadCache(cache);
        var freshEnough = cached is not null && File.Exists(cache)
                          && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromHours(12);
        if (cached is { Count: > 0 }) Maps = cached;
        if (freshEnough) return;

        try
        {
            var mapsTask = GetAsync("https://json.tarkov.dev/regular/maps", ct);
            var enTask = GetAsync("https://json.tarkov.dev/regular/maps_en", ct);
            var ruTask = GetAsync("https://json.tarkov.dev/regular/maps_ru", ct);
            await Task.WhenAll(mapsTask, enTask, ruTask);
            var parsed = MapDataParser.Parse(mapsTask.Result, enTask.Result, ruTask.Result);
            if (parsed.Count == 0) return;
            Maps = parsed;
            WriteCache(cache, parsed);
            AppLog.Info($"[GPS] карты tarkov.dev: {parsed.Count}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error("[GPS] не удалось обновить карты tarkov.dev", ex);
        }
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static List<GameMap>? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dto = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            return dto is { Version: CacheVersion, Maps.Count: > 0 } ? dto.Maps : null;
        }
        catch (Exception ex)
        {
            AppLog.Error("[GPS] кэш карт повреждён", ex);
            return null;
        }
    }

    private static void WriteCache(string path, List<GameMap> maps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new CacheFile(CacheVersion, maps), Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("[GPS] не удалось сохранить кэш карт", ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record CacheFile(int Version, List<GameMap> Maps);
}
