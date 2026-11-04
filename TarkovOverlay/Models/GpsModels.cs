namespace TarkovOverlay.Models;

/// <summary>Unity world position from a screenshot file name. Y is up.</summary>
public readonly record struct Vec3(double X, double Y, double Z);

/// <summary>Unity quaternion (x, y, z, w) from a screenshot file name.</summary>
public readonly record struct Quat(double X, double Y, double Z, double W);

public sealed record ScreenshotFix(Vec3 Position, Quat Rotation, double InGameTime);

public sealed record MapExtract(string NameEn, string NameRu, string? Faction, double X, double Y, double Z, string? Id = null);

public sealed record GameMap(
    string Id,
    string NameId,
    string ScenePath,
    string NameEn,
    string NameRu,
    double NorthRotation,
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ,
    string[] Aliases,
    MapExtract[] Extracts);

public sealed record NearestExtract(MapExtract Extract, double DistanceMeters, double BearingDegrees);

/// <summary>Arrow HUD on the price overlay. Separate from the map window.</summary>
public sealed record GpsHud(bool HasBearing, double ArrowDegrees, int Meters, string Primary, string Secondary);

public sealed record MapMarker(double X, double Z, string Name, string Faction, bool Nearest, double Y = 0);

/// <summary>
/// One keycard in a map legend. <paramref name="Fill"/> is <c>#RRGGBB</c>.
/// <paramref name="RoomFill"/> is false when the map only draws a marker, not a painted room.
/// </summary>
public sealed record MapLegendEntry(string Name, string Fill, string? Tip, bool RoomFill, int Count = 0, string? ItemId = null);

/// <summary>
/// Place name drawn on top of a map underlay. <paramref name="Priority"/> is higher for larger landmarks.
/// <paramref name="Text"/> is English. <paramref name="TextRu"/> is the Russian label when one is shipped.
/// </summary>
public sealed record MapLabel(double X, double Z, string Text, double Priority, string? TextRu = null)
{
    public string Display(bool english)
        => english || string.IsNullOrWhiteSpace(TextRu) ? Text : TextRu;
}

/// <summary>Room number (or other verified caption) drawn in floor-image space. U and V run 0–1 from the top-left.</summary>
public sealed record MapImageLabel(double U, double V, string Text);

/// <summary>Floor button while an interior plan is showing. <paramref name="Caption"/> is the English control label (U, 1, 2, 3).</summary>
public sealed record MapFloorOption(int Floor, string Caption, string Tip, bool Selected);

/// <summary>
/// Map window contents. When <paramref name="UnderlayImage"/> is set, the bounds are that image's
/// world frame and <paramref name="Rotation"/> is its coordinateRotation. Otherwise this is the
/// extract schematic fitted around the player. <paramref name="ImageLabels"/> and <paramref name="Floors"/>
/// are set only for a building floor plan.
/// </summary>
public sealed record MapView(
    string Title,
    string? Message,
    double Rotation,
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ,
    double? PlayerX,
    double? PlayerZ,
    double Heading,
    MapMarker[] Markers,
    string? UnderlayImage = null,
    MapLabel[]? Labels = null,
    double CompassDegrees = double.NaN,
    string? Credit = null,
    MapImageLabel[]? ImageLabels = null,
    MapFloorOption[]? Floors = null,
    MapLegendEntry[]? Legend = null);
