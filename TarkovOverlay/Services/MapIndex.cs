using System.Text.RegularExpressions;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

public static partial class MapIndex
{
    [GeneratedRegex(@"maps/(?<id>[A-Za-z0-9_]+)\.bundle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex ScenePathPattern();

    [GeneratedRegex(@"rcid:(?<id>[^.\\/\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex RcidPattern();

    [GeneratedRegex(@"_?preset$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PresetSuffix();

    public static string Norm(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        Span<char> buf = stackalloc char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                buf[n++] = char.ToLowerInvariant(c);
        }
        return new string(buf[..n]);
    }

    public static string[] Aliases(string? nameId, string? normalizedName, string? scenePath)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? s)
        {
            var n = Norm(s);
            if (n.Length > 0) set.Add(n);
        }

        Add(nameId);
        Add(normalizedName);
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            var file = Path.GetFileNameWithoutExtension(scenePath.Replace('\\', '/'));
            Add(file);
            Add(PresetSuffix().Replace(file, ""));
        }

        return set.Count == 0 ? [] : set.ToArray();
    }

    public static GameMap? Find(IReadOnlyList<GameMap> maps, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || maps.Count == 0) return null;

        var scene = ScenePathPattern().Match(token);
        if (scene.Success)
        {
            var path = "maps/" + scene.Groups["id"].Value + ".bundle";
            var exact = maps.FirstOrDefault(m => m.ScenePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        foreach (var key in Keys(token))
        {
            GameMap? hit = null;
            var ambiguous = false;
            foreach (var map in maps)
            {
                if (Array.IndexOf(map.Aliases, key) < 0) continue;
                if (hit is not null) { ambiguous = true; break; }
                hit = map;
            }
            if (!ambiguous && hit is not null) return hit;
        }

        return null;
    }

    private static IEnumerable<string> Keys(string token)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Yield(string? raw)
        {
            var n = Norm(raw);
            if (n.Length > 0) seen.Add(n);
        }

        var scene = ScenePathPattern().Match(token);
        if (scene.Success)
        {
            var file = scene.Groups["id"].Value;
            Yield(file);
            Yield(PresetSuffix().Replace(file, ""));
        }

        var rcid = RcidPattern().Match(token);
        if (rcid.Success) Yield(rcid.Groups["id"].Value);
        if (!token.Contains(' ', StringComparison.Ordinal) && !token.Contains('|', StringComparison.Ordinal))
            Yield(token);
        return seen;
    }
}
