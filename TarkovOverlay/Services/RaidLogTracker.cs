using System.Globalization;
using System.Text.RegularExpressions;

namespace TarkovOverlay.Services;

public readonly record struct RaidObservation(DateTimeOffset When, string Line);

/// <summary>
/// In-raid / map / side state from lines the game already writes.
/// Enter: application|GameStarting or GameStarted.
/// Map: scene preset path, or Location: on TRACE-NetworkGameCreate.
/// Leave: UserMatchOver, GameStopping(), matching aborted/cancelled, or a profile reselect while in raid.
/// </summary>
public sealed partial class RaidLogTracker
{
    private static readonly Regex Timestamp = new(
        @"^(?<d>\d{4}-\d{2}-\d{2}) (?<t>\d{2}:\d{2}:\d{2}\.\d{1,7})(?:\s+(?<z>[+-]\d{2}:\d{2}))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Scene = new(
        @"scene preset path:(?<p>maps/[A-Za-z0-9_]+\.bundle)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Location = new(
        @"Location:\s*(?<m>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Side = new(
        "\"side\"\\s*:\\s*\"(?<s>Pmc|Savage)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private DateTimeOffset _lastApplied = DateTimeOffset.MinValue;

    public bool InRaid { get; private set; }
    public string? ScenePath { get; private set; }
    public string? LocationId { get; private set; }
    public string? SideName { get; private set; }
    public DateTimeOffset? EnteredAt { get; private set; }

    public void Reset()
    {
        InRaid = false;
        ScenePath = null;
        LocationId = null;
        SideName = null;
        EnteredAt = null;
        _lastApplied = DateTimeOffset.MinValue;
    }

    /// <summary>The game process is gone, so a trailing GameStarted in an old log is not a live raid.</summary>
    public void NoteGameClosed() => InRaid = false;

    public static bool TryTimestamp(string line, out DateTimeOffset when)
    {
        when = default;
        var m = Timestamp.Match(line);
        if (!m.Success) return false;
        var text = m.Groups["d"].Value + " " + m.Groups["t"].Value;
        string fmt;
        if (m.Groups["z"].Success)
        {
            text += " " + m.Groups["z"].Value;
            fmt = "yyyy-MM-dd HH:mm:ss.FFFFFFF zzz";
        }
        else fmt = "yyyy-MM-dd HH:mm:ss.FFFFFFF";
        return DateTimeOffset.TryParseExact(text, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
    }

    public static bool IsInteresting(string line)
        => line.Contains("scene preset path:", StringComparison.Ordinal)
           || line.Contains("TRACE-NetworkGameCreate profileStatus", StringComparison.Ordinal)
           || line.Contains("GameStarted", StringComparison.Ordinal)
           || line.Contains("GameStarting", StringComparison.Ordinal)
           || line.Contains("GameStopping()", StringComparison.Ordinal)
           || line.Contains("UserMatchOver", StringComparison.Ordinal)
           || line.Contains("matching aborted", StringComparison.OrdinalIgnoreCase)
           || line.Contains("matching cancelled", StringComparison.OrdinalIgnoreCase)
           || line.Contains("SelectProfile ProfileId:", StringComparison.Ordinal)
           || line.Contains("SelectedProfile ProfileId:", StringComparison.Ordinal)
           || line.Contains("PrepareSelectedProfileLocally ProfileId:", StringComparison.Ordinal)
           || line.Contains("\"side\":", StringComparison.Ordinal);

    public void Ingest(IReadOnlyList<RaidObservation> ordered)
    {
        foreach (var item in ordered)
        {
            if (item.When < _lastApplied) continue;
            if (item.When > _lastApplied) _lastApplied = item.When;
            Apply(item.Line, item.When);
        }
    }

    private void Apply(string line, DateTimeOffset when)
    {
        var side = Side.Match(line);
        if (side.Success)
            SideName = side.Groups["s"].Value.Equals("Savage", StringComparison.Ordinal) ? "scav" : "pmc";

        var scene = Scene.Match(line);
        if (scene.Success) ScenePath = scene.Groups["p"].Value;

        if (line.Contains("TRACE-NetworkGameCreate profileStatus", StringComparison.Ordinal))
        {
            var loc = Location.Match(line);
            if (loc.Success) LocationId = loc.Groups["m"].Value;
        }

        if (line.Contains("matching aborted", StringComparison.OrdinalIgnoreCase)
            || line.Contains("matching cancelled", StringComparison.OrdinalIgnoreCase)
            || line.Contains("UserMatchOver", StringComparison.Ordinal)
            || line.Contains("GameStopping()", StringComparison.Ordinal))
        {
            Exit();
            return;
        }

        if (line.Contains("SelectProfile ProfileId:", StringComparison.Ordinal)
            || line.Contains("SelectedProfile ProfileId:", StringComparison.Ordinal)
            || line.Contains("PrepareSelectedProfileLocally ProfileId:", StringComparison.Ordinal))
        {
            SideName = null;
            if (InRaid) Exit();
            return;
        }

        if (line.Contains("application|GameStarting", StringComparison.Ordinal)
            || line.Contains("application|GameStarted", StringComparison.Ordinal))
            Enter(when);
    }

    private void Enter(DateTimeOffset when)
    {
        InRaid = true;
        EnteredAt ??= when;
    }

    private void Exit()
    {
        InRaid = false;
        ScenePath = null;
        LocationId = null;
        SideName = null;
        EnteredAt = null;
    }
}
