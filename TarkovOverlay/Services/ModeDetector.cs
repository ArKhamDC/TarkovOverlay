using System.Text.RegularExpressions;

namespace TarkovOverlay.Services;

/// <summary>
/// Определяет режим игры (pvp/pve/season) по логам клиента: &lt;папка игры&gt;\Logs\log_*\*application_*.log
/// (строка "... mode: Pve") и *backend_*.log (хост gw-pve / gw-pvp / prod, vhost=...). Только чтение, токены не читаются и не сохраняются.
/// </summary>
public static class ModeDetector
{
    private static readonly Regex AppMode = new(@"\|application\|.*?\bmode: (\w+)", RegexOptions.Compiled);
    private static readonly Regex Host = new(@"https://(gw-[a-z0-9-]+|prod[a-z0-9-]*)\.escapefromtarkov\.com|vhost=(\w+)", RegexOptions.Compiled);

    public static string? LastSource { get; private set; }

    private static string Tail(string path, int bytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > bytes) fs.Seek(-bytes, SeekOrigin.End);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    /// <summary>"pvp" | "pve" | "season" или null, если сигнала нет.</summary>
    public static string? Detect()
    {
        try
        {
            var root = GameInstall.Root();
            if (root is null) { LastSource = Utils.L.T("папка игры не найдена", "game folder not found"); return null; }
            var dir = GameInstall.LatestLogDirectory();
            if (dir is null) { LastSource = Utils.L.T("нет логов", "no logs"); return null; }
            // 1) backend: последний хост/vhost
            var be = dir.GetFiles("*backend_*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (be is not null)
            {
                var ms = Host.Matches(Tail(be.FullName, 256 * 1024));
                for (var i = ms.Count - 1; i >= 0; i--)
                {
                    var v = (ms[i].Groups[1].Success ? ms[i].Groups[1].Value : ms[i].Groups[2].Value).ToLowerInvariant();
                    var m = Map(v);
                    if (m is not null) { LastSource = $"{dir.Name}: backend {v}"; return m; }
                }
            }
            // 2) application: "mode: Pve"
            var app = dir.GetFiles("*application_*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (app is not null)
            {
                var ms = AppMode.Matches(Tail(app.FullName, 512 * 1024));
                if (ms.Count > 0)
                {
                    var v = ms[^1].Groups[1].Value.ToLowerInvariant();
                    var m = Map(v);
                    if (m is not null) { LastSource = $"{dir.Name}: mode {v}"; return m; }
                }
            }
            LastSource = $"{dir.Name}: {Utils.L.T("сигнала нет", "no signal")}";
        }
        catch (Exception ex) { LastSource = Utils.L.T("ошибка: ", "error: ") + ex.Message; }
        return null;
    }

    private static string? Map(string v)
    {
        if (v.Contains("season")) return "season";
        if (v.Contains("pve")) return "pve";
        if (v.StartsWith("gw-pvp") || v.StartsWith("prod") || v is "regular" or "pvp" or "main") return "pvp";
        return null;
    }
}
