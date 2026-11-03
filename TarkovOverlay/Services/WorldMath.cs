using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>
/// Tarkov screenshot coordinates are Unity world space: +X right, +Y up, +Z forward.
/// Heading 0 faces +Z. Positive heading turns toward +X (the player's right).
/// </summary>
public static class WorldMath
{
    public static double HeadingDegrees(Quat q)
    {
        var len = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (len < 1e-8) return 0;
        var x = q.X / len;
        var y = q.Y / len;
        var z = q.Z / len;
        var w = q.W / len;
        var fx = 2 * (x * z + w * y);
        var fz = 1 - 2 * (x * x + y * y);
        return Math.Atan2(fx, fz) * 180.0 / Math.PI;
    }

    public static double BearingDegrees(double fromX, double fromZ, double toX, double toZ)
        => Math.Atan2(toX - fromX, toZ - fromZ) * 180.0 / Math.PI;

    public static double HorizontalMeters(double fromX, double fromZ, double toX, double toZ)
    {
        var dx = toX - fromX;
        var dz = toZ - fromZ;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Shortest signed turn from <paramref name="heading"/> to <paramref name="bearing"/>, degrees, clockwise positive.</summary>
    public static double RelativeDegrees(double bearing, double heading)
    {
        var d = bearing - heading;
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }

    /// <summary>Rotate world (x, z) around Y. Used with tarkov.dev <c>coordinateToCardinalRotation</c>.</summary>
    public static (double X, double Y) Project(double x, double z, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        return (x * c - z * s, x * s + z * c);
    }

    /// <summary>Clockwise degrees from screen-up for a player facing <paramref name="headingDegrees"/> after the map rotation.</summary>
    public static double ScreenAngleDegrees(double x, double z, double headingDegrees, double mapRotationDegrees)
    {
        var rad = headingDegrees * Math.PI / 180.0;
        var fx = Math.Sin(rad);
        var fz = Math.Cos(rad);
        var a = Project(x, z, mapRotationDegrees);
        var b = Project(x + fx, z + fz, mapRotationDegrees);
        return Math.Atan2(b.X - a.X, b.Y - a.Y) * 180.0 / Math.PI;
    }
}
