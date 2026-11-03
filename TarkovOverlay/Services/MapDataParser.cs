using System.Text.Json;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>Reads map extracts and play-area bounds from json.tarkov.dev <c>/maps</c> plus name translations.</summary>
public static class MapDataParser
{
    public static List<GameMap> Parse(string mapsJson, string? enJson, string? ruJson)
    {
        using var mapsDoc = JsonDocument.Parse(mapsJson);
        var mapsNode = Child(Child(mapsDoc.RootElement, "data") ?? mapsDoc.RootElement, "maps")
                       ?? (mapsDoc.RootElement.TryGetProperty("maps", out var direct) ? direct : default);
        if (mapsNode.ValueKind != JsonValueKind.Object)
            return [];

        var en = ReadDict(enJson);
        var ru = ReadDict(ruJson);
        var list = new List<GameMap>();
        foreach (var prop in mapsNode.EnumerateObject())
        {
            var e = prop.Value;
            var id = Str(e, "id") ?? prop.Name;
            var nameId = Str(e, "nameId") ?? "";
            var scene = Str(e, "scenePath") ?? "";
            var normalized = Str(e, "normalizedName") ?? "";
            var rotation = Num(e, "coordinateToCardinalRotation") ?? 0;
            var (minX, maxX, minZ, maxZ, extracts) = ReadPoints(e, en, ru);
            var nameEn = Pick(en, id + " Name", nameId);
            var nameRu = Pick(ru, id + " Name", nameEn);
            list.Add(new GameMap(
                id, nameId, scene, nameEn, nameRu, rotation,
                minX, maxX, minZ, maxZ,
                MapIndex.Aliases(nameId, normalized, scene),
                extracts.ToArray()));
        }

        return list;
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ, List<MapExtract> Extracts) ReadPoints(
        JsonElement map, Dictionary<string, string> en, Dictionary<string, string> ru)
    {
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minZ = double.PositiveInfinity;
        var maxZ = double.NegativeInfinity;
        var extracts = new List<MapExtract>();

        void Acc(double x, double z)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }

        if (map.TryGetProperty("spawns", out var spawns) && spawns.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in spawns.EnumerateArray())
                if (TryPos(s, out var x, out _, out var z)) Acc(x, z);
        }

        if (map.TryGetProperty("extracts", out var exits) && exits.ValueKind == JsonValueKind.Array)
        {
            foreach (var ex in exits.EnumerateArray())
            {
                if (!TryPos(ex, out var x, out var y, out var z)) continue;
                Acc(x, z);
                var key = Str(ex, "name") ?? "";
                var faction = Str(ex, "faction")?.ToLowerInvariant();
                extracts.Add(new MapExtract(Pick(en, key, key), Pick(ru, key, Pick(en, key, key)), faction, x, y, z));
            }
        }

        if (double.IsInfinity(minX))
            return (0, 1, 0, 1, extracts);
        if (Math.Abs(maxX - minX) < 1) maxX = minX + 1;
        if (Math.Abs(maxZ - minZ) < 1) maxZ = minZ + 1;
        return (minX, maxX, minZ, maxZ, extracts);
    }

    private static bool TryPos(JsonElement e, out double x, out double y, out double z)
    {
        x = y = z = 0;
        if (!e.TryGetProperty("position", out var p) || p.ValueKind != JsonValueKind.Object) return false;
        var nx = Num(p, "x");
        var nz = Num(p, "z");
        if (nx is null || nz is null) return false;
        x = nx.Value;
        z = nz.Value;
        y = Num(p, "y") ?? 0;
        return true;
    }

    private static Dictionary<string, string> ReadDict(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        using var doc = JsonDocument.Parse(json);
        var root = Child(doc.RootElement, "data") ?? doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return dict;
        foreach (var p in root.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String)
                dict[p.Name] = p.Value.GetString() ?? "";
        return dict;
    }

    private static JsonElement? Child(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var c) ? c : null;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static string Pick(Dictionary<string, string> dict, string key, string fallback)
        => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
