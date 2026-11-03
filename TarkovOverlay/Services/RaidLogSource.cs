using System.Text;

namespace TarkovOverlay.Services;

/// <summary>Tails the newest application, notifications, and output logs. Does not read backend logs or keep raw lines.</summary>
internal sealed class RaidLogSource
{
    private const int TailBytes = 4 * 1024 * 1024;
    private readonly RaidLogTracker _tracker = new();
    private readonly Dictionary<string, long> _pos = new(StringComparer.OrdinalIgnoreCase);
    private string? _dir;

    public RaidLogTracker Tracker => _tracker;

    public void Poll()
    {
        var dir = GameInstall.LatestLogDirectory();
        if (dir is null) return;
        if (!string.Equals(_dir, dir.FullName, StringComparison.OrdinalIgnoreCase))
        {
            _dir = dir.FullName;
            _pos.Clear();
            _tracker.Reset();
        }

        var batch = new List<RaidObservation>();
        foreach (var token in new[] { "application", "notifications", "output" })
        {
            var file = Newest(dir, token);
            if (file is not null) batch.AddRange(ReadNew(file));
        }

        batch.Sort((a, b) => a.When.CompareTo(b.When));
        _tracker.Ingest(batch);
    }

    private static FileInfo? Newest(DirectoryInfo dir, string token)
    {
        FileInfo? best = null;
        IEnumerable<FileInfo> files;
        try { files = dir.EnumerateFiles("*.log"); }
        catch { return null; }

        foreach (var file in files)
        {
            var name = file.Name;
            if (name.Contains("backend", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc) best = file;
        }

        return best;
    }

    private List<RaidObservation> ReadNew(FileInfo file)
    {
        var list = new List<RaidObservation>();
        try
        {
            using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var initial = !_pos.TryGetValue(file.FullName, out var offset);
            if (initial) offset = Math.Max(0, fs.Length - TailBytes);
            if (offset > fs.Length) offset = 0;
            if (offset >= fs.Length)
            {
                _pos[file.FullName] = fs.Length;
                return list;
            }

            fs.Seek(offset, SeekOrigin.Begin);
            var count = (int)Math.Min(int.MaxValue, fs.Length - offset);
            var buf = new byte[count];
            var read = fs.Read(buf, 0, count);
            var text = Encoding.UTF8.GetString(buf, 0, read);
            if (initial && offset > 0)
            {
                var nl = text.IndexOf('\n');
                text = nl >= 0 ? text[(nl + 1)..] : "";
            }

            var consumed = read;
            if (text.Length > 0 && text[^1] != '\n' && text[^1] != '\r')
            {
                var last = text.LastIndexOf('\n');
                if (last >= 0)
                {
                    var leftover = text[(last + 1)..];
                    consumed -= Encoding.UTF8.GetByteCount(leftover);
                    text = text[..(last + 1)];
                }
                else if (!initial)
                {
                    return list;
                }
            }

            _pos[file.FullName] = offset + Math.Max(0, consumed);
            DateTimeOffset carry = DateTimeOffset.MinValue;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (RaidLogTracker.TryTimestamp(line, out var ts)) carry = ts;
                if (!RaidLogTracker.IsInteresting(line)) continue;
                list.Add(new RaidObservation(carry == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : carry, line));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("[GPS] не удалось прочитать лог рейда", ex);
        }

        return list;
    }
}
