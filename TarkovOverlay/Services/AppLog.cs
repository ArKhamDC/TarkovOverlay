namespace TarkovOverlay.Services;

public static class AppLog
{
    private static readonly object Lock = new();
    private static string? _path;

    public static string? LogPath => _path;

    public static void Init()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovOverlay");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "tarkov_overlay.log");

        try
        {
            // не даём логу расти бесконечно
            if (File.Exists(_path) && new FileInfo(_path).Length > 5 * 1024 * 1024)
                File.Move(_path, _path + ".old", overwrite: true);
        }
        catch { /* не критично */ }

        File.AppendAllText(_path, $"{Environment.NewLine}=== TarkovOverlay запущен {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level,-5}] {message}";
        lock (Lock)
        {
            try
            {
                if (_path is not null)
                    File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch { /* лог не должен ронять приложение */ }
        }
    }
}
