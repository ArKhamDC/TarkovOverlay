using System.Globalization;
using System.Text.Json;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>One floor raster of a building. Floor 0 is underground; 1 is ground. A negative floor is a named level below that.</summary>
public sealed class InteriorFloor
{
    public required int Floor { get; init; }
    public required string Key { get; init; }
    public required string ImagePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public required InteriorLabel[] Labels { get; init; }

    /// <summary>Short title-bar caption. Null uses <see cref="InteriorCatalog.FloorCaption"/>.</summary>
    public string? Caption { get; init; }

    /// <summary>Tooltip and map title. Null uses <see cref="InteriorCatalog.FloorTip"/>.</summary>
    public string? Tip { get; init; }
}

/// <summary>
/// First matching band wins. <see cref="Below"/> null matches every remaining height.
/// Height is world Y from the screenshot name.
/// </summary>
public readonly record struct FloorBand(double? Below, int Floor);

/// <summary>XZ footprint of one bunker volume. Corners are opposite; order does not matter.</summary>
public readonly record struct BunkerBox(double X0, double Z0, double X1, double Z1, string? Name)
{
    public bool Contains(double x, double z)
        => x >= Math.Min(X0, X1) && x <= Math.Max(X0, X1)
           && z >= Math.Min(Z0, Z1) && z <= Math.Max(Z0, Z1);
}

/// <summary>
/// One tarkov.dev Bunkers extent. Height is half-open: <c>Y0 &lt;= y &lt; Y1</c>.
/// Any box in that band selects <see cref="Floor"/>.
/// </summary>
public sealed class BunkerExtent
{
    public double Y0 { get; init; }
    public double Y1 { get; init; }
    public int Floor { get; init; }
    public required BunkerBox[] Boxes { get; init; }

    public bool Contains(double x, double y, double z)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return false;
        if (!(y >= Y0 && y < Y1)) return false;
        foreach (var box in Boxes)
            if (box.Contains(x, z)) return true;
        return false;
    }
}

/// <summary>Verified caption in floor-image pixels, origin top-left.</summary>
public sealed class InteriorLabel
{
    public required string Text { get; init; }
    public string? TextRu { get; init; }
    public double U { get; init; }
    public double V { get; init; }
    public required string Kind { get; init; }

    /// <summary>Room numbers stay numeric. Named rooms (Overseer) follow the overlay language.</summary>
    public string Display(bool english)
    {
        if (Kind is "room" or "marked") return Text;
        return english || string.IsNullOrWhiteSpace(TextRu) ? Text : TextRu!;
    }

    public MapImageLabel ToImage(int width, int height, bool english)
        => new(U / width, V / height, Display(english));
}

/// <summary>
/// Building footprint in the same world frame as the overview.
/// <see cref="X0"/>/<see cref="Z0"/> is the image top-left; <see cref="X1"/>/<see cref="Z1"/> is the bottom-right.
/// </summary>
public sealed class InteriorBuilding
{
    public required string Id { get; init; }
    public required string MapId { get; init; }
    public required string NameEn { get; init; }
    public required string NameRu { get; init; }
    public double X0 { get; init; }
    public double Z0 { get; init; }
    public double X1 { get; init; }
    public double Z1 { get; init; }
    public double Rotation { get; init; }
    public required string[] Aliases { get; init; }
    public required InteriorFloor[] Floors { get; init; }
    public string? Credit { get; init; }

    /// <summary>The building is the whole map. The image still uses rectXZ, and the player never returns to a separate outdoor overview.</summary>
    public bool CoversMap { get; init; }

    /// <summary>When set, height selects the floor unless the player pinned a floor on the current band.</summary>
    public FloorBand[]? FloorByY { get; init; }

    /// <summary>
    /// Tarkov.dev Bunkers volumes. A position inside any volume uses that extent's floor.
    /// Everywhere else uses <see cref="OpenFloor"/>. Null when this building is not split that way.
    /// </summary>
    public BunkerExtent[]? BunkerExtents { get; init; }

    /// <summary>Floor used when <see cref="BunkerExtents"/> is set and the position matches none of them.</summary>
    public int? OpenFloor { get; init; }

    public double MinX => Math.Min(X0, X1);
    public double MaxX => Math.Max(X0, X1);
    public double MinZ => Math.Min(Z0, Z1);
    public double MaxZ => Math.Max(Z0, Z1);
    public double Area => Math.Abs((X1 - X0) * (Z1 - Z0));

    /// <summary>Same world→image mapping the overview uses for extracts (<see cref="WorldMath.TryImageUv"/>).</summary>
    public bool TryUv(double x, double z, out double u, out double v)
        => WorldMath.TryImageUv(x, z, MinX, MaxX, MinZ, MaxZ, Rotation, out u, out v);

    public bool Contains(double x, double z)
        => CoversMap || (TryUv(x, z, out var u, out var v) && u >= -1e-9 && u <= 1 + 1e-9 && v >= -1e-9 && v <= 1 + 1e-9);
}

/// <summary>
/// Which building and floor the GPS map is showing. A null building is the overview.
/// <paramref name="LockBand"/> is the floor the building's rule selected when the player pinned a choice.
/// Null follows the rule, or the last manual floor when the building has no rule.
/// The pin lasts until that rule selects a different floor.
/// </summary>
public readonly record struct InteriorFix(string? BuildingId, int Floor, int? LockBand = null)
{
    public static InteriorFix Overview => new(null, InteriorCatalog.DefaultFloor);
}

/// <summary>Loads <c>Maps/interiors.json</c> and picks a floor when a world position enters a footprint.</summary>
public static class InteriorCatalog
{
    public const int DefaultFloor = 1;

    public static IReadOnlyList<InteriorBuilding> LoadFromAppDirectory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Maps");
        var jsonPath = Path.Combine(root, "interiors.json");
        if (!File.Exists(jsonPath)) return [];
        return Load(File.ReadAllText(jsonPath), Path.Combine(root, "interiors"));
    }

    public static IReadOnlyList<InteriorBuilding> Load(string json, string imageDirectory)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
            return [];

        var credit = Str(doc.RootElement, "credit");
        var list = new List<InteriorBuilding>();
        foreach (var map in maps.EnumerateArray())
        {
            var mapId = Str(map, "id");
            var rotation = Num(map, "coordinateRotation");
            if (string.IsNullOrWhiteSpace(mapId) || rotation is null) continue;
            if (!map.TryGetProperty("buildings", out var buildings) || buildings.ValueKind != JsonValueKind.Array)
                continue;

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

            foreach (var building in buildings.EnumerateArray())
            {
                var id = Str(building, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!building.TryGetProperty("rectXZ", out var rect)) continue;
                var x0 = Num(rect, "x0");
                var z0 = Num(rect, "z0");
                var x1 = Num(rect, "x1");
                var z1 = Num(rect, "z1");
                if (x0 is null || z0 is null || x1 is null || z1 is null) continue;
                if (Math.Abs(x1.Value - x0.Value) < 1e-3 || Math.Abs(z1.Value - z0.Value) < 1e-3) continue;
                if (!building.TryGetProperty("floors", out var floorsNode) || floorsNode.ValueKind != JsonValueKind.Array)
                    continue;

                var floors = new List<InteriorFloor>();
                foreach (var floor in floorsNode.EnumerateArray())
                {
                    var index = Num(floor, "floor");
                    var image = Str(floor, "image");
                    var width = Num(floor, "width");
                    var height = Num(floor, "height");
                    if (index is null || string.IsNullOrWhiteSpace(image) || width is null || height is null) continue;
                    if (width.Value < 1 || height.Value < 1) continue;
                    var file = Path.Combine(imageDirectory, Path.GetFileName(image));
                    if (!File.Exists(file)) continue;

                    var labels = new List<InteriorLabel>();
                    if (floor.TryGetProperty("labels", out var labelNode) && labelNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var label in labelNode.EnumerateArray())
                        {
                            if (!VerifiedRoom(label)) continue;
                            var text = Str(label, "text");
                            var u = Num(label, "u");
                            var v = Num(label, "v");
                            if (string.IsNullOrWhiteSpace(text) || u is null || v is null) continue;
                            labels.Add(new InteriorLabel
                            {
                                Text = text,
                                TextRu = Str(label, "textRu"),
                                U = u.Value,
                                V = v.Value,
                                Kind = Str(label, "kind") ?? "room"
                            });
                        }
                    }

                    var key = Str(floor, "floorKey");
                    var caption = Str(floor, "caption");
                    var tip = Str(floor, "tip");
                    floors.Add(new InteriorFloor
                    {
                        Floor = (int)index.Value,
                        Key = string.IsNullOrWhiteSpace(key) ? FloorCaption((int)index.Value) : key,
                        ImagePath = Path.GetFullPath(file),
                        Width = (int)width.Value,
                        Height = (int)height.Value,
                        Labels = labels.ToArray(),
                        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption,
                        Tip = string.IsNullOrWhiteSpace(tip) ? null : tip
                    });
                }

                if (floors.Count == 0) continue;
                floors.Sort((a, b) => a.Floor.CompareTo(b.Floor));
                var name = Str(building, "name") ?? id;
                list.Add(new InteriorBuilding
                {
                    Id = id,
                    MapId = mapId,
                    NameEn = name,
                    NameRu = Str(building, "nameRu") ?? name,
                    X0 = x0.Value,
                    Z0 = z0.Value,
                    X1 = x1.Value,
                    Z1 = z1.Value,
                    Rotation = rotation.Value,
                    Aliases = aliases.ToArray(),
                    Floors = floors.ToArray(),
                    Credit = Str(building, "credit") ?? credit,
                    CoversMap = Bool(building, "coversMap"),
                    FloorByY = ReadBands(building),
                    BunkerExtents = ReadBunkers(building, out var openFloor),
                    OpenFloor = openFloor
                });
            }
        }

        return list;
    }

    public static IReadOnlyList<InteriorBuilding> ForMap(IReadOnlyList<InteriorBuilding> buildings, GameMap map)
    {
        if (buildings.Count == 0 || map.Aliases.Length == 0) return [];
        var list = new List<InteriorBuilding>();
        foreach (var building in buildings)
        {
            foreach (var alias in map.Aliases)
            {
                if (Array.IndexOf(building.Aliases, alias) < 0) continue;
                list.Add(building);
                break;
            }
        }

        return list;
    }

    /// <summary>Smallest footprint wins when rects overlap. Null when the point is outside every building.</summary>
    public static InteriorBuilding? Hit(IReadOnlyList<InteriorBuilding> buildings, double x, double z)
    {
        InteriorBuilding? best = null;
        var bestArea = double.MaxValue;
        foreach (var building in buildings)
        {
            if (!building.Contains(x, z)) continue;
            if (building.Area < bestArea)
            {
                best = building;
                bestArea = building.Area;
            }
        }

        return best;
    }

    /// <summary>Prefer <paramref name="preferred"/> when that floor exists; otherwise ground (1), then the lowest floor.</summary>
    public static InteriorFloor SelectFloor(InteriorBuilding building, int preferred)
    {
        foreach (var floor in building.Floors)
            if (floor.Floor == preferred) return floor;
        foreach (var floor in building.Floors)
            if (floor.Floor == DefaultFloor) return floor;
        return building.Floors[0];
    }

    /// <summary>
    /// Entering a building opens floor 1, or the floor its height rule selects.
    /// A floor the player already picked is kept only while they stay in that building.
    /// On a height-ruled building the pin lasts until Y moves into a different band.
    /// Leaving every footprint returns to the overview.
    /// </summary>
    public static (InteriorFix State, InteriorBuilding? Building, InteriorFloor? Floor) Advance(
        IReadOnlyList<InteriorBuilding> onMap, InteriorFix state, double x, double z)
        => Advance(onMap, state, x, double.NaN, z);

    public static (InteriorFix State, InteriorBuilding? Building, InteriorFloor? Floor) Advance(
        IReadOnlyList<InteriorBuilding> onMap, InteriorFix state, double x, double y, double z)
    {
        var hit = Hit(onMap, x, z);
        if (hit is null || hit.Floors.Length == 0)
            return (InteriorFix.Overview, null, null);

        var chosen = hit.Id == state.BuildingId ? state.Floor : DefaultFloor;
        int? held = null;
        if (HasFloorRule(hit))
        {
            var band = FloorFromRule(hit, x, y, z);
            var same = hit.Id == state.BuildingId;
            if (same && state.LockBand is { } locked && (band is null || band.Value == locked))
            {
                chosen = state.Floor;
                held = locked;
            }
            else if (band is { } next)
            {
                chosen = next;
            }
            else if (same)
            {
                chosen = state.Floor;
                held = state.LockBand;
            }
            else
            {
                chosen = DefaultFloor;
            }
        }

        var floor = SelectFloor(hit, chosen);
        return (new InteriorFix(hit.Id, floor.Floor, held), hit, floor);
    }

    public static InteriorFix RememberFloor(InteriorFix state, int floor) => state with { Floor = floor };

    /// <summary>
    /// Remember a title-bar floor. Buildings with a height rule pin that choice against the band Y is in now,
    /// and the next band change follows height again.
    /// </summary>
    public static InteriorFix PinFloor(IReadOnlyList<InteriorBuilding> buildings, InteriorFix state, int floor, double y)
        => PinFloor(buildings, state, floor, double.NaN, y, double.NaN);

    /// <summary>
    /// Remember a title-bar floor against the rule's current result.
    /// Height rules use Y. Bunker rules use the full position. The next time the rule selects a different floor, it wins.
    /// </summary>
    public static InteriorFix PinFloor(IReadOnlyList<InteriorBuilding> buildings, InteriorFix state, int floor, double x, double y, double z)
    {
        if (string.IsNullOrEmpty(state.BuildingId))
            return state with { Floor = floor };
        InteriorBuilding? building = null;
        foreach (var candidate in buildings)
        {
            if (candidate.Id != state.BuildingId) continue;
            building = candidate;
            break;
        }

        if (building is null || !HasFloorRule(building))
            return state with { Floor = floor, LockBand = null };
        return new InteriorFix(state.BuildingId, floor, FloorFromRule(building, x, y, z));
    }

    /// <summary>Null when <paramref name="y"/> is missing or the building has no height rule.</summary>
    public static int? FloorFromAltitude(InteriorBuilding building, double y)
    {
        var rules = building.FloorByY;
        if (rules is not { Length: > 0 } || double.IsNaN(y)) return null;
        foreach (var rule in rules)
        {
            if (rule.Below is null || y < rule.Below.Value)
                return rule.Floor;
        }

        return rules[^1].Floor;
    }

    /// <summary>
    /// Floor the building's rule selects. Bunker extents win over height bands.
    /// Null when the position is incomplete or the building has no rule.
    /// </summary>
    public static int? FloorFromRule(InteriorBuilding building, double x, double y, double z)
    {
        if (building.BunkerExtents is { Length: > 0 } && building.OpenFloor is int open)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) return null;
            foreach (var extent in building.BunkerExtents)
                if (extent.Contains(x, y, z)) return extent.Floor;
            return open;
        }

        return FloorFromAltitude(building, y);
    }

    /// <summary>Extracts on a height-ruled building are drawn only on the floor their Y selects. Other buildings show every extract inside the footprint.</summary>
    public static bool ExtractOnFloor(InteriorBuilding building, double y, int floor)
    {
        if (building.FloorByY is not { Length: > 0 }) return true;
        var band = FloorFromAltitude(building, y);
        return band is null || band.Value == floor;
    }

    /// <summary>
    /// Height-ruled buildings use Y. Bunker-ruled buildings use the full position:
    /// an extract is drawn only on the floor its extent selects.
    /// </summary>
    public static bool ExtractOnFloor(InteriorBuilding building, double x, double y, double z, int floor)
    {
        if (building.BunkerExtents is { Length: > 0 } && building.OpenFloor is not null)
        {
            var band = FloorFromRule(building, x, y, z);
            return band is null || band.Value == floor;
        }

        return ExtractOnFloor(building, y, floor);
    }

    private static bool HasFloorRule(InteriorBuilding building)
        => building.FloorByY is { Length: > 0 }
           || (building.BunkerExtents is { Length: > 0 } && building.OpenFloor is not null);

    public static MapFloorOption[]? FloorOptions(InteriorBuilding building, int selected)
    {
        if (building.Floors.Length < 2) return null;
        var options = new MapFloorOption[building.Floors.Length];
        for (var i = 0; i < building.Floors.Length; i++)
        {
            var floor = building.Floors[i];
            var caption = string.IsNullOrWhiteSpace(floor.Caption) ? FloorCaption(floor.Floor) : floor.Caption!;
            var tip = string.IsNullOrWhiteSpace(floor.Tip) ? FloorTip(floor.Floor) : floor.Tip!;
            options[i] = new MapFloorOption(floor.Floor, caption, tip, floor.Floor == selected);
        }

        return options;
    }

    public static string FloorCaption(int floor)
        => floor == 0 ? "U" : floor.ToString(CultureInfo.InvariantCulture);

    public static string FloorTip(int floor)
        => floor == 0 ? "Underground" : $"Floor {floor}";

    private static FloorBand[]? ReadBands(JsonElement building)
    {
        if (!building.TryGetProperty("floorByY", out var bands) || bands.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<FloorBand>();
        foreach (var band in bands.EnumerateArray())
        {
            var floor = Num(band, "floor");
            if (floor is null) continue;
            list.Add(new FloorBand(Num(band, "below"), (int)floor.Value));
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    private static BunkerExtent[]? ReadBunkers(JsonElement building, out int? openFloor)
    {
        openFloor = null;
        if (!building.TryGetProperty("bunkerExtents", out var node) || node.ValueKind != JsonValueKind.Array)
            return null;
        var open = Num(building, "openFloor");
        if (open is null) return null;

        var list = new List<BunkerExtent>();
        foreach (var extent in node.EnumerateArray())
        {
            var y0 = Num(extent, "y0");
            var y1 = Num(extent, "y1");
            var floor = Num(extent, "floor");
            if (y0 is null || y1 is null || floor is null) continue;
            if (!extent.TryGetProperty("boxes", out var boxes) || boxes.ValueKind != JsonValueKind.Array)
                continue;

            var boxList = new List<BunkerBox>();
            foreach (var box in boxes.EnumerateArray())
            {
                var x0 = Num(box, "x0");
                var z0 = Num(box, "z0");
                var x1 = Num(box, "x1");
                var z1 = Num(box, "z1");
                if (x0 is null || z0 is null || x1 is null || z1 is null) continue;
                boxList.Add(new BunkerBox(x0.Value, z0.Value, x1.Value, z1.Value, Str(box, "name")));
            }

            if (boxList.Count == 0) continue;
            list.Add(new BunkerExtent
            {
                Y0 = y0.Value,
                Y1 = y1.Value,
                Floor = (int)floor.Value,
                Boxes = boxList.ToArray()
            });
        }

        if (list.Count == 0) return null;
        openFloor = (int)open.Value;
        return list.ToArray();
    }

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static bool VerifiedRoom(JsonElement label)
    {
        var confidence = Str(label, "confidence");
        if (!string.Equals(confidence, "verified", StringComparison.OrdinalIgnoreCase)) return false;
        var kind = Str(label, "kind");
        return !string.Equals(kind, "stairs", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }
}
