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

public sealed record MapMarker(double X, double Z, string Name, string Faction, bool Nearest);

/// <summary>
/// Place name drawn on top of a map underlay. <paramref name="Priority"/> is higher for larger landmarks.
/// <paramref name="Text"/> is English. <paramref name="TextRu"/> is the Russian label when one is shipped.
/// </summary>
public sealed record MapLabel(double X, double Z, string Text, double Priority, string? TextRu = null)
{
    public string Display(bool english)
        => english || string.IsNullOrWhiteSpace(TextRu) ? Text : TextRu;
}

/// <summary>
/// Map window contents. When <paramref name="UnderlayImage"/> is set, the bounds are that image's
/// world frame and <paramref name="Rotation"/> is its coordinateRotation. Otherwise this is the
/// extract schematic fitted around the player.
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
    string? Credit = null);
