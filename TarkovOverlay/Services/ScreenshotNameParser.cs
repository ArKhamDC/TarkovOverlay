using System.Globalization;
using System.Text.RegularExpressions;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

/// <summary>
/// Escape from Tarkov writes world position and camera rotation into in-raid screenshot file names.
/// Example: 2026-08-05[14-30]_300.4, 2.1, -50.7_0.0, 0.7, 0.0, 0.7_5.43 (0).png
/// The image itself is never opened.
/// </summary>
public static partial class ScreenshotNameParser
{
    private const string Num = @"-?\d+(?:\.\d+)?";

    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}\[\d{2}[-.]\d{2}(?:[-.]\d{2})?\]_?" +
        @"(?<x>" + Num + @"),\s*(?<y>" + Num + @"),\s*(?<z>" + Num + @")_?" +
        @"(?<qx>" + Num + @"),\s*(?<qy>" + Num + @"),\s*(?<qz>" + Num + @"),\s*(?<qw>" + Num + @")" +
        @"(?:_(?<time>" + Num + @"))?\s*\(\d+\)\.png$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex NamePattern();

    public static bool TryParse(string? pathOrName, out ScreenshotFix fix)
    {
        fix = null!;
        if (string.IsNullOrWhiteSpace(pathOrName)) return false;
        var trimmed = pathOrName.Trim();
        var slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        var m = NamePattern().Match(name);
        if (!m.Success) return false;
        if (!N(m, "x", out var x) || !N(m, "y", out var y) || !N(m, "z", out var z)) return false;
        if (!N(m, "qx", out var qx) || !N(m, "qy", out var qy) || !N(m, "qz", out var qz) || !N(m, "qw", out var qw)) return false;
        var time = m.Groups["time"].Success && N(m, "time", out var t) ? t : 0;
        fix = new ScreenshotFix(new Vec3(x, y, z), new Quat(qx, qy, qz, qw), time);
        return true;
    }

    private static bool N(Match m, string group, out double value)
        => double.TryParse(m.Groups[group].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
