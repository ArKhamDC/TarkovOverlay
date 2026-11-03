using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

internal sealed class ScreenshotWatcher : IDisposable
{
    private readonly Dictionary<string, long> _seenAt = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private string? _folder;

    public string? Folder => _folder;
    public bool Broken { get; private set; }

    public event Action<ScreenshotFix, string>? Parsed;

    public void Start(string folder)
    {
        Stop();
        _folder = folder;
        Broken = false;
        var watcher = new FileSystemWatcher(folder)
        {
            Filter = "*.png",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024
        };
        watcher.Created += OnCreated;
        watcher.Renamed += OnRenamed;
        watcher.Error += (_, _) => Broken = true;
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    public void Stop()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
        _folder = null;
        lock (_seenAt) _seenAt.Clear();
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => Handle(e.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs e) => Handle(e.FullPath);

    private void Handle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var folder = _folder;
        if (folder is null || !ScreenshotPath.IsInside(folder, path)) return;
        if (!ScreenshotNameParser.TryParse(path, out var fix)) return;
        var now = Environment.TickCount64;
        lock (_seenAt)
        {
            if (_seenAt.TryGetValue(path, out var at) && now - at < 3000) return;
            if (_seenAt.Count > 400) _seenAt.Clear();
            _seenAt[path] = now;
        }
        Parsed?.Invoke(fix, path);
    }

    public void Dispose() => Stop();
}
