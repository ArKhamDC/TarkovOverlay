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
    public MapFloor[] Floors { get; init; } = [];
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
                Labels = labels.ToArray(),
                Floors = ReadFloors(map)
            });
        }

        return list;
    }

    /// <summary>
    /// The floor whose height band contains <paramref name="y"/>. When several overlap,
    /// the higher band wins, and a smaller footprint wins a tie. Null means the ground layer.
    /// </summary>
    public static MapFloor? FloorAt(IReadOnlyList<MapFloor> floors, double x, double y, double z)
    {
        MapFloor? best = null;
        var bestArea = double.PositiveInfinity;
        foreach (var floor in floors)
        {
            if (y < floor.MinY || y >= floor.MaxY) continue;
            var area = double.PositiveInfinity;
            if (floor.X0 is double x0 && floor.Z0 is double z0 && floor.X1 is double x1 && floor.Z1 is double z1)
            {
                var minX = Math.Min(x0, x1);
                var maxX = Math.Max(x0, x1);
                var minZ = Math.Min(z0, z1);
                var maxZ = Math.Max(z0, z1);
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;
                area = (maxX - minX) * (maxZ - minZ);
            }

            if (best is null
                || floor.MinY > best.MinY + 1e-6
                || (Math.Abs(floor.MinY - best.MinY) <= 1e-6 && area < bestArea))
            {
                best = floor;
                bestArea = area;
            }
        }

        return best;
    }

    private static MapFloor[] ReadFloors(JsonElement map)
    {
        if (!map.TryGetProperty("floors", out var node) || node.ValueKind != JsonValueKind.Array)
            return [];
        var floors = new List<MapFloor>();
        foreach (var floor in node.EnumerateArray())
        {
            var name = Str(floor, "name");
            var minY = Num(floor, "minY");
            var maxY = Num(floor, "maxY");
            if (string.IsNullOrWhiteSpace(name) || minY is null || maxY is null || maxY <= minY) continue;
            floors.Add(new MapFloor(
                name,
                Str(floor, "place") ?? "",
                minY.Value,
                maxY.Value,
                Num(floor, "x0"),
                Num(floor, "z0"),
                Num(floor, "x1"),
                Num(floor, "z1")));
        }

        return floors.ToArray();
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
