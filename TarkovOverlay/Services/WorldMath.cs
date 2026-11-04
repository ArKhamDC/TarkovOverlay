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

    /// <summary>
    /// Normalized image position for a world point inside a map frame.
    /// <paramref name="rotationDegrees"/> is tarkov.dev <c>coordinateRotation</c>.
    /// (0, 0) is the top-left of the frame and (1, 1) is the bottom-right. Y grows down.
    /// </summary>
    public static bool TryImageUv(
        double x, double z,
        double minX, double maxX, double minZ, double maxZ, double rotationDegrees,
        out double u, out double v)
    {
        var (minPX, maxPX, minPY, maxPY) = ProjectedBounds(minX, maxX, minZ, maxZ, rotationDegrees);
        var spanX = maxPX - minPX;
        var spanY = maxPY - minPY;
        if (spanX < 1e-6 || spanY < 1e-6)
        {
            u = v = 0;
            return false;
        }

        var p = Project(x, z, rotationDegrees);
        u = (p.X - minPX) / spanX;
        v = (maxPY - p.Y) / spanY;
        return true;
    }

    /// <summary>Inverse of <see cref="TryImageUv"/>.</summary>
    public static bool TryWorldFromImageUv(
        double u, double v,
        double minX, double maxX, double minZ, double maxZ, double rotationDegrees,
        out double x, out double z)
    {
        var (minPX, maxPX, minPY, maxPY) = ProjectedBounds(minX, maxX, minZ, maxZ, rotationDegrees);
        var spanX = maxPX - minPX;
        var spanY = maxPY - minPY;
        if (spanX < 1e-6 || spanY < 1e-6)
        {
            x = z = 0;
            return false;
        }

        (x, z) = Unproject(minPX + u * spanX, maxPY - v * spanY, rotationDegrees);
        return true;
    }

    /// <summary>
    /// Pull a point that sits just outside a map frame onto the edge.
    /// Points already inside, and points farther than <paramref name="slack"/> of the frame, stay put.
    /// </summary>
    public static (double X, double Z) ClampNearFrame(
        double x, double z,
        double minX, double maxX, double minZ, double maxZ, double rotationDegrees,
        double slack = 0.04)
    {
        if (!TryImageUv(x, z, minX, maxX, minZ, maxZ, rotationDegrees, out var u, out var v))
            return (x, z);
        if (u >= -1e-9 && u <= 1 + 1e-9 && v >= -1e-9 && v <= 1 + 1e-9)
            return (x, z);
        if (u < -slack || u > 1 + slack || v < -slack || v > 1 + slack)
            return (x, z);
        var cu = Math.Clamp(u, 0, 1);
        var cv = Math.Clamp(v, 0, 1);
        return TryWorldFromImageUv(cu, cv, minX, maxX, minZ, maxZ, rotationDegrees, out var cx, out var cz)
            ? (cx, cz)
            : (x, z);
    }

    /// <summary>
    /// Letterbox the world frame into a view. Matches the historical schematic fit:
    /// uniform scale, Y flipped so projected +Y is screen-up.
    /// </summary>
    public static bool TryFit(
        double minX, double maxX, double minZ, double maxZ, double rotationDegrees,
        double viewW, double viewH, out MapProjection fit)
    {
        fit = default;
        if (viewW < 2 || viewH < 2) return false;
        var (minPX, maxPX, minPY, maxPY) = ProjectedBounds(minX, maxX, minZ, maxZ, rotationDegrees);
        var spanX = Math.Max(1, maxPX - minPX);
        var spanY = Math.Max(1, maxPY - minPY);
        var scale = Math.Min(viewW / spanX, viewH / spanY);
        var usedW = spanX * scale;
        var usedH = spanY * scale;
        fit = new MapProjection(
            (viewW - usedW) / 2,
            (viewH - usedH) / 2,
            scale,
            minPX,
            maxPY,
            usedW,
            usedH,
            rotationDegrees);
        return true;
    }

    /// <summary>Inverse of <see cref="Project"/>.</summary>
    public static (double X, double Z) Unproject(double projectedX, double projectedY, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        return (projectedX * c + projectedY * s, -projectedX * s + projectedY * c);
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

    private static (double MinPX, double MaxPX, double MinPY, double MaxPY) ProjectedBounds(
        double minX, double maxX, double minZ, double maxZ, double rotationDegrees)
    {
        var corners = new[]
        {
            Project(minX, minZ, rotationDegrees),
            Project(maxX, minZ, rotationDegrees),
            Project(minX, maxZ, rotationDegrees),
            Project(maxX, maxZ, rotationDegrees)
        };
        var minPX = corners[0].X;
        var maxPX = corners[0].X;
        var minPY = corners[0].Y;
        var maxPY = corners[0].Y;
        for (var i = 1; i < corners.Length; i++)
        {
            if (corners[i].X < minPX) minPX = corners[i].X;
            if (corners[i].X > maxPX) maxPX = corners[i].X;
            if (corners[i].Y < minPY) minPY = corners[i].Y;
            if (corners[i].Y > maxPY) maxPY = corners[i].Y;
        }
        return (minPX, maxPX, minPY, maxPY);
    }
}

/// <summary>Letterboxed placement of a map frame inside a view, in device-independent pixels.</summary>
public readonly record struct MapProjection(
    double OriginX,
    double OriginY,
    double Scale,
    double MinPX,
    double MaxPY,
    double UsedW,
    double UsedH,
    double RotationDegrees)
{
    public (double X, double Y) ToScreen(double worldX, double worldZ)
    {
        var p = WorldMath.Project(worldX, worldZ, RotationDegrees);
        return (OriginX + (p.X - MinPX) * Scale, OriginY + (MaxPY - p.Y) * Scale);
    }

    public (double X, double Z) FromScreen(double screenX, double screenY)
    {
        var px = MinPX + (screenX - OriginX) / Scale;
        var py = MaxPY - (screenY - OriginY) / Scale;
        return WorldMath.Unproject(px, py, RotationDegrees);
    }
}

/// <summary>
/// Zoom and follow for a letterboxed <see cref="MapProjection"/>.
/// One scale and origin still feed <see cref="MapProjection.ToScreen"/>, so the underlay,
/// extracts, room labels, and the player marker stay on the same projection.
/// The player glyph itself is drawn at a fixed size in <c>MapWindow</c>; this type only moves it.
/// </summary>
public static class MapCamera
{
    public const double MinZoom = 1;
    public const double MaxZoom = 8;
    public const double Step = 1.25;
    /// <summary>Player may sit this far from the view center before the next fix recenters. Stops sub-meter jitter from sliding the map.</summary>
    public const double DeadzonePixels = 22;
    public const double DefaultSpanMeters = 220;
    public const double TightFrameZoom = 2;

    public static double ClampZoom(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom)) return 1;
        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Zoom that shows about <see cref="DefaultSpanMeters"/> across the shorter side of the view.
    /// A building-sized frame zooms to <see cref="TightFrameZoom"/> so follow still has room to pan.
    /// </summary>
    public static double DefaultZoom(MapProjection fit, double viewW, double viewH)
    {
        if (fit.Scale < 1e-9 || viewW < 2 || viewH < 2) return 1;
        var shorter = Math.Min(viewW, viewH);
        var zoom = shorter / (fit.Scale * DefaultSpanMeters);
        var frameShort = Math.Min(fit.UsedW, fit.UsedH) / fit.Scale;
        if (frameShort < DefaultSpanMeters * 1.15)
            zoom = Math.Max(zoom, TightFrameZoom);
        return ClampZoom(zoom);
    }

    /// <summary>
    /// Scale <paramref name="fit"/> by <paramref name="zoom"/> and place
    /// (<paramref name="anchorX"/>, <paramref name="anchorZ"/>) at the view center.
    /// Zoom 1 with the anchor on the letterboxed frame center matches <paramref name="fit"/>.
    /// </summary>
    public static MapProjection Apply(
        MapProjection fit, double viewW, double viewH, double zoom, double anchorX, double anchorZ)
    {
        zoom = ClampZoom(zoom);
        var anchor = fit.ToScreen(anchorX, anchorZ);
        var panX = viewW / 2 - anchor.X * zoom;
        var panY = viewH / 2 - anchor.Y * zoom;
        return fit with
        {
            OriginX = panX + fit.OriginX * zoom,
            OriginY = panY + fit.OriginY * zoom,
            Scale = fit.Scale * zoom,
            UsedW = fit.UsedW * zoom,
            UsedH = fit.UsedH * zoom
        };
    }

    /// <summary>Change zoom while keeping the world point under (<paramref name="focusScreenX"/>, <paramref name="focusScreenY"/>) fixed.</summary>
    public static (double AnchorX, double AnchorZ, double Zoom) ZoomAbout(
        MapProjection fit, double viewW, double viewH,
        double zoom, double anchorX, double anchorZ,
        double focusScreenX, double focusScreenY, double newZoom)
    {
        newZoom = ClampZoom(newZoom);
        var current = Apply(fit, viewW, viewH, zoom, anchorX, anchorZ);
        var (worldX, worldZ) = current.FromScreen(focusScreenX, focusScreenY);
        var focus = fit.ToScreen(worldX, worldZ);
        var anchorContentX = focus.X - (focusScreenX - viewW / 2) / newZoom;
        var anchorContentY = focus.Y - (focusScreenY - viewH / 2) / newZoom;
        var anchor = fit.FromScreen(anchorContentX, anchorContentY);
        return (anchor.X, anchor.Z, newZoom);
    }

    /// <summary>
    /// Drag the map by (<paramref name="dx"/>, <paramref name="dy"/>) screen pixels.
    /// Positive dx follows the pointer to the right.
    /// </summary>
    public static (double X, double Z) Pan(
        MapProjection fit, double viewW, double viewH, double zoom,
        double anchorX, double anchorZ, double dx, double dy)
    {
        var view = Apply(fit, viewW, viewH, zoom, anchorX, anchorZ);
        return view.FromScreen(viewW / 2 - dx, viewH / 2 - dy);
    }

    /// <summary>
    /// Keep the current anchor while the player is inside the deadzone.
    /// Outside it, put the player on the view center.
    /// </summary>
    public static (double X, double Z) Follow(
        MapProjection fit, double viewW, double viewH, double zoom,
        double anchorX, double anchorZ, double playerX, double playerZ, double deadzonePixels)
    {
        var view = Apply(fit, viewW, viewH, zoom, anchorX, anchorZ);
        var at = view.ToScreen(playerX, playerZ);
        var dx = at.X - viewW / 2;
        var dy = at.Y - viewH / 2;
        if (dx * dx + dy * dy <= deadzonePixels * deadzonePixels)
            return (anchorX, anchorZ);
        return (playerX, playerZ);
    }
}
