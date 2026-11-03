using System.Diagnostics;
using Microsoft.Win32;

namespace TarkovOverlay.Services;

/// <summary>Escape from Tarkov install folder, from the uninstall registry key or the running process. Read-only.</summary>
public static class GameInstall
{
    private static string? _root;

    public static string? Root()
    {
        if (_root is not null && Directory.Exists(_root)) return _root;
        foreach (var key in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov"
                 })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(key);
                if (k?.GetValue("InstallLocation") is string p && Directory.Exists(Path.Combine(p, "Logs")))
                    return _root = p;
            }
            catch { /* registry may be unreadable */ }
        }

        try
        {
            var proc = Process.GetProcessesByName("EscapeFromTarkov").FirstOrDefault();
            var path = proc?.MainModule?.FileName;
            if (path is not null) return _root = Path.GetDirectoryName(path);
        }
        catch { /* process path can be denied */ }

        return null;
    }

    public static DirectoryInfo? LatestLogDirectory()
    {
        var root = Root();
        if (root is null) return null;
        var logs = Path.Combine(root, "Logs");
        if (!Directory.Exists(logs)) return null;
        try
        {
            return new DirectoryInfo(logs).GetDirectories("log_*").OrderByDescending(d => d.CreationTimeUtc).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
