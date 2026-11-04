namespace TarkovOverlay.Models;

/// <summary>Unity world position from a screenshot file name. Y is up.</summary>
public readonly record struct Vec3(double X, double Y, double Z);

/// <summary>Unity quaternion (x, y, z, w) from a screenshot file name.</summary>
public readonly record struct Quat(double X, double Y, double Z, double W);

public sealed record ScreenshotFix(Vec3 Position, Quat Rotation, double InGameTime);

public sealed record MapExtract(string NameEn, string NameRu, string? Faction, double X, double Y, double Z);

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

/// <summary>Place name drawn on top of a map underlay. <paramref name="Priority"/> is higher for larger landmarks.</summary>
public sealed record MapLabel(double X, double Z, string Text, double Priority);

/// <summary>
/// A height band where the player is on a named floor. Bounds are optional;
/// when set, the band applies only inside that footprint.
/// </summary>
public sealed record MapFloor(
    string Name,
    string Place,
    double MinY,
    double MaxY,
    double? X0,
    double? Z0,
    double? X1,
    double? Z1);

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
    double? CameraMinX = null,
    double? CameraMaxX = null,
    double? CameraMinZ = null,
    double? CameraMaxZ = null,
    double? FloorMinX = null,
    double? FloorMaxX = null,
    double? FloorMinZ = null,
    double? FloorMaxZ = null);
