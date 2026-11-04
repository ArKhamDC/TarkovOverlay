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

    /// <summary>Full extract list shipped with the underlay, including factions hidden on the overview.</summary>
    public MapExtract[] Extracts { get; init; } = [];

    /// <summary>Short credit drawn on the map when the underlay is someone else's art.</summary>
    public string? Credit { get; init; }
}

/// <summary>Loads <c>Maps/underlays.json</c> and keeps entries whose image file is present.</summary>
public static class MapUnderlayCatalog
{
    public static IReadOnlyList<MapUnderlay> LoadFromAppDirectory()
        => LoadDirectory(Path.Combine(AppContext.BaseDirectory, "Maps"));

    /// <summary>
    /// Loads <paramref name="mapsDirectory"/>/underlays.json plus any <c>{id}-extracts.json</c> beside it.
    /// Shipped extracts replace that map's place-name labels with bilingual extract names.
    /// </summary>
    public static IReadOnlyList<MapUnderlay> LoadDirectory(string mapsDirectory)
    {
        var jsonPath = Path.Combine(mapsDirectory, "underlays.json");
        if (!File.Exists(jsonPath)) return [];
        return Load(File.ReadAllText(jsonPath), Path.Combine(mapsDirectory, "images"), ReadShippedExtracts(mapsDirectory));
    }

    public static IReadOnlyList<MapUnderlay> Load(string json, string imageDirectory)
        => Load(json, imageDirectory, null);

    public static IReadOnlyList<MapUnderlay> Load(
        string json, string imageDirectory, IReadOnlyDictionary<string, MapExtract[]>? shippedExtracts)
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
                    var text = Str(label, "text") ?? Str(label, "textEn") ?? Str(label, "name");
                    var lx = Num(label, "x");
                    var lz = Num(label, "z");
                    if (string.IsNullOrWhiteSpace(text) || lx is null || lz is null) continue;
                    var ru = Str(label, "textRu") ?? Str(label, "nameRu");
                    labels.Add(new MapLabel(lx.Value, lz.Value, text, Num(label, "priority") ?? 100, string.IsNullOrWhiteSpace(ru) ? null : ru));
                }
            }

            var extracts = Array.Empty<MapExtract>();
            if (shippedExtracts is not null && shippedExtracts.TryGetValue(id, out var shipped) && shipped.Length > 0)
            {
                // World x/z from the extracts file. Do not snap them to landmark labels.
                extracts = shipped;
                labels = OverviewLabels(shipped);
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
                Extracts = extracts,
                Credit = Str(map, "credit")
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

    private static Dictionary<string, MapExtract[]> ReadShippedExtracts(string mapsDirectory)
    {
        var result = new Dictionary<string, MapExtract[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(mapsDirectory)) return result;
        foreach (var path in Directory.EnumerateFiles(mapsDirectory, "*-extracts.json"))
        {
            var id = Path.GetFileName(path);
            const string suffix = "-extracts.json";
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                id = id[..^suffix.Length];
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("map", out var mapId) && mapId.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(mapId.GetString()))
                    id = mapId.GetString()!;
                var extracts = ParseExtracts(doc.RootElement);
                if (extracts.Length > 0) result[id] = extracts;
            }
            catch (JsonException)
            {
                // A broken sidecar must not drop every other underlay.
            }
            catch (IOException)
            {
            }
        }

        return result;
    }

    private static MapExtract[] ParseExtracts(JsonElement root)
    {
        if (!root.TryGetProperty("extracts", out var node) || node.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<MapExtract>();
        foreach (var e in node.EnumerateArray())
        {
            // px/py are a precomputed preview. The GPS projects world x/z itself.
            var x = Num(e, "x");
            var z = Num(e, "z");
            var name = Str(e, "name") ?? Str(e, "textEn") ?? Str(e, "text");
            if (x is null || z is null || string.IsNullOrWhiteSpace(name)) continue;
            var ru = Str(e, "nameRu") ?? Str(e, "textRu");
            list.Add(new MapExtract(
                name,
                string.IsNullOrWhiteSpace(ru) ? name : ru,
                Str(e, "faction")?.ToLowerInvariant(),
                x.Value,
                Num(e, "y") ?? 0,
                z.Value,
                Str(e, "id")));
        }

        return list.ToArray();
    }

    private static List<MapLabel> OverviewLabels(IEnumerable<MapExtract> extracts)
    {
        var labels = new List<MapLabel>();
        foreach (var e in extracts)
        {
            if (!ExtractNav.OverviewVisible(e.Faction, null)) continue;
            labels.Add(new MapLabel(e.X, e.Z, e.NameEn, 200, e.NameRu));
        }

        return labels;
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
