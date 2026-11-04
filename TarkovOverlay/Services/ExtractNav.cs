using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

public static class ExtractNav
{
    public static bool FactionVisible(string? faction, string? side)
    {
        if (side is not ("pmc" or "scav")) return true;
        if (string.IsNullOrEmpty(faction) || faction is "shared"
            || faction.Equals("transit", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(faction, side, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts drawn on the map overview. PMC and shared are the default.
    /// A known scav raid shows scav and shared instead. The full list, including the other faction, stays on the map data.
    /// </summary>
    public static bool OverviewVisible(string? faction, string? side)
    {
        if (string.Equals(side, "scav", StringComparison.OrdinalIgnoreCase))
            return FactionVisible(faction, "scav");
        var kind = faction?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(kind) || kind is "pmc" or "shared" or "transit";
    }

    public static NearestExtract? Nearest(GameMap map, Vec3 pos, string? side)
    {
        var hit = Pick(map.Extracts, pos, side);
        if (hit is null && side is not null)
            hit = Pick(map.Extracts, pos, null);
        return hit;
    }

    private static NearestExtract? Pick(IReadOnlyList<MapExtract> extracts, Vec3 pos, string? side)
    {
        MapExtract? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var e in extracts)
        {
            if (!FactionVisible(e.Faction, side)) continue;
            var d = WorldMath.HorizontalMeters(pos.X, pos.Z, e.X, e.Z);
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }

        return best is null
            ? null
            : new NearestExtract(best, bestD, WorldMath.BearingDegrees(pos.X, pos.Z, best.X, best.Z));
    }
}
