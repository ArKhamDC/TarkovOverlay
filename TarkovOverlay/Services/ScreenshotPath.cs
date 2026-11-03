namespace TarkovOverlay.Services;

public static class ScreenshotPath
{
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured.Trim());
            if (Directory.Exists(full)) return full;
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var name in new[] { "Escape from Tarkov", "Escape From Tarkov" })
        {
            var candidate = Path.Combine(docs, name, "Screenshots");
            if (Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(docs, "Escape from Tarkov", "Screenshots");
    }

    public static bool IsInside(string folder, string path)
    {
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
