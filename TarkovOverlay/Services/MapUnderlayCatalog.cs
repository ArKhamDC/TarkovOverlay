using System.Text.Json;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>
/// Raster underlay aligned to a tarkov.dev interactive map frame.
/// <see cref="Rotation"/> is <c>coordinateRotation</c>. <see cref="CompassDegrees"/> is the
/// base location's <c>coordinateToCardinalRotation</c> and is only used to draw the north tick.
/// </summary>
public sealed class MapUnderlay
{
    public required string Id { get; init; }
    public required string ImagePath { get; init; }
    public double Rotation { get; init; }
    public double CompassDegrees { get; init; }
    public double MinX { get; init; }
    public double MaxX { get; init; }
    public double MinZ { get; init; }
    public double MaxZ { get; init; }
    public required string[] Aliases { get; init; }
    public required MapLabel[] Labels { get; init; }
}

/// <summary>Loads <c>Maps/underlays.json</c> and keeps entries whose image file is present.</summary>
public static class MapUnderlayCatalog
{
    public static IReadOnlyList<MapUnderlay> LoadFromAppDirectory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Maps");
        var jsonPath = Path.Combine(root, "underlays.json");
        if (!File.Exists(jsonPath)) return [];
        return Load(File.ReadAllText(jsonPath), Path.Combine(root, "images"));
    }

    public static IReadOnlyList<MapUnderlay> Load(string json, string imageDirectory)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<MapUnderlay>();
        foreach (var map in maps.EnumerateArray())
        {
            var id = Str(map, "id");
            var image = Str(map, "image");
            var rotation = Num(map, "coordinateRotation");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(image) || rotation is null)
                continue;
            if (!map.TryGetProperty("bounds", out var bounds)) continue;
            var x0 = Num(bounds, "x0");
            var z0 = Num(bounds, "z0");
            var x1 = Num(bounds, "x1");
            var z1 = Num(bounds, "z1");
            if (x0 is null || z0 is null || x1 is null || z1 is null) continue;
            if (Math.Abs(x1.Value - x0.Value) < 1 || Math.Abs(z1.Value - z0.Value) < 1) continue;

            var file = Path.Combine(imageDirectory, Path.GetFileName(image));
            if (!File.Exists(file)) continue;

            var aliases = new List<string>();
            if (map.TryGetProperty("aliases", out var aliasNode) && aliasNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var alias in aliasNode.EnumerateArray())
                {
                    if (alias.ValueKind != JsonValueKind.String) continue;
                    var n = MapIndex.Norm(alias.GetString());
                    if (n.Length > 0 && !aliases.Contains(n)) aliases.Add(n);
                }
            }

            var labels = new List<MapLabel>();
            if (map.TryGetProperty("labels", out var labelNode) && labelNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var label in labelNode.EnumerateArray())
                {
                    var text = Str(label, "text");
                    var lx = Num(label, "x");
                    var lz = Num(label, "z");
                    if (string.IsNullOrWhiteSpace(text) || lx is null || lz is null) continue;
                    labels.Add(new MapLabel(lx.Value, lz.Value, text, Num(label, "priority") ?? 100));
                }
            }

            list.Add(new MapUnderlay
            {
                Id = id,
                ImagePath = Path.GetFullPath(file),
                Rotation = rotation.Value,
                CompassDegrees = Num(map, "cardinalRotation") ?? rotation.Value,
                MinX = Math.Min(x0.Value, x1.Value),
                MaxX = Math.Max(x0.Value, x1.Value),
                MinZ = Math.Min(z0.Value, z1.Value),
                MaxZ = Math.Max(z0.Value, z1.Value),
                Aliases = aliases.ToArray(),
                Labels = labels.ToArray()
            });
        }

        return list;
    }

    public static MapUnderlay? Match(IReadOnlyList<MapUnderlay> underlays, GameMap map)
    {
        if (underlays.Count == 0 || map.Aliases.Length == 0) return null;
        foreach (var alias in map.Aliases)
        {
            foreach (var underlay in underlays)
            {
                if (Array.IndexOf(underlay.Aliases, alias) >= 0) return underlay;
            }
        }
        return null;
    }

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
}
