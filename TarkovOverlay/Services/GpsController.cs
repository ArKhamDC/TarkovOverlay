using System.Windows.Threading;
using TarkovOverlay.Models;
using TarkovOverlay.Utils;
using TarkovOverlay.Windows;

namespace TarkovOverlay.Services;

/// <summary>
/// Raid GPS from screenshot file names: arrow to the nearest extract, plus a separate map window.
/// Auto-screenshot only while a raid is detected and the game window is in front.
/// </summary>
public sealed class GpsController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly OverlayWindow _overlay;
    private readonly MapWindow _map;
    private readonly MapCatalog _catalog = new();
    private readonly RaidLogSource _logs = new();
    private readonly ScreenshotWatcher _shots = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private List<GameMap> _maps = [];
    private IReadOnlyList<MapUnderlay> _underlays = [];
    private GameMap? _lastMap;
    private ScreenshotFix? _fix;
    private string? _watchFolder;
    private volatile bool _overlayVisible = true;
    private bool _wasInRaid;
    private bool _loggedMissing;
    private bool _announcedShot;
    private int _keyFailures;
    private string _sig = "";
    private DateTime _nextShot = DateTime.MinValue;
    private DateTime _nextAdopt = DateTime.MinValue;
    private Task? _loop;

    public event Action? StateChanged;

    public GpsController(AppSettings settings, Dispatcher dispatcher, OverlayWindow overlay, MapWindow map)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _overlay = overlay;
        _map = map;
        _map.ApplyPlacement(settings.GpsMapLeft, settings.GpsMapTop, settings.GpsMapWidth, settings.GpsMapHeight, settings.GpsMapOpacity);
        _map.BoundsCommitted += OnBounds;
        _map.UserHid += OnUserHid;
    }

    public void SetOverlayVisible(bool visible)
    {
        _overlayVisible = visible;
        NotifySettingsChanged();
    }

    public void NotifySettingsChanged()
    {
        lock (_gate) _sig = "\0";
        Publish();
    }

    public void Start()
    {
        _shots.Parsed += OnParsed;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private void OnParsed(ScreenshotFix fix, string path)
    {
        lock (_gate) _fix = fix;
        AppLog.Info($"[GPS] позиция {fix.Position.X:0.#}, {fix.Position.Y:0.#}, {fix.Position.Z:0.#}, курс {WorldMath.HeadingDegrees(fix.Rotation):0}°");
        if (_settings.GpsDeleteAfterParse)
            _ = DeleteLater(path, _cts.Token);
        Publish();
    }

    private void OnBounds(double left, double top, double width, double height)
    {
        _settings.GpsMapLeft = left;
        _settings.GpsMapTop = top;
        _settings.GpsMapWidth = width;
        _settings.GpsMapHeight = height;
        _settings.Save();
    }

    private void OnUserHid()
    {
        _settings.GpsMapVisible = false;
        _settings.Save();
        StateChanged?.Invoke();
        NotifySettingsChanged();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try { await _catalog.LoadAsync(ct); }
        catch (OperationCanceledException) { return; }
        try
        {
            _underlays = MapUnderlayCatalog.LoadFromAppDirectory();
            AppLog.Info($"[GPS] подложки карт: {_underlays.Count}");
        }
        catch (Exception ex)
        {
            _underlays = [];
            AppLog.Error("[GPS] не удалось загрузить подложки карт", ex);
        }
        lock (_gate) _maps = _catalog.Maps.ToList();

        lock (_gate)
        {
            _logs.Poll();
            if (!GameDetector.IsTarkovRunning()) _logs.Tracker.NoteGameClosed();
            ObserveRaidEdge();
        }
        Publish();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                lock (_gate)
                {
                    _maps = _catalog.Maps.ToList();
                    EnsureWatcher();
                    _logs.Poll();
                    if (!GameDetector.IsTarkovRunning()) _logs.Tracker.NoteGameClosed();
                    ObserveRaidEdge();
                    if (_logs.Tracker.InRaid && GameDetector.IsTarkovRunning() && _fix is null)
                        TryAdopt();
                    MaybeShoot();
                }
                Publish();
            }
            catch (Exception ex)
            {
                AppLog.Error("[GPS] цикл", ex);
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void EnsureWatcher()
    {
        if (!_settings.GpsEnabled)
        {
            _shots.Stop();
            _watchFolder = null;
            return;
        }

        var folder = ScreenshotPath.Resolve(_settings.GpsScreenshotsPath);
        if (!Directory.Exists(folder))
        {
            if (!_loggedMissing)
            {
                _loggedMissing = true;
                AppLog.Warn("[GPS] папка скриншотов не найдена (Documents\\Escape from Tarkov\\Screenshots или GpsScreenshotsPath)");
            }
            _shots.Stop();
            _watchFolder = null;
            return;
        }

        _loggedMissing = false;
        _watchFolder = folder;
        if (_shots.Broken || !string.Equals(_shots.Folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            _shots.Start(folder);
            AppLog.Info("[GPS] слежу за новыми скриншотами");
        }
    }

    private void ObserveRaidEdge()
    {
        var inRaid = _logs.Tracker.InRaid && GameDetector.IsTarkovRunning();
        if (_wasInRaid && !inRaid)
        {
            _fix = null;
            _lastMap = null;
            _announcedShot = false;
            _nextShot = DateTime.MinValue;
            AppLog.Info("[GPS] рейд завершён");
        }
        else if (!_wasInRaid && inRaid)
        {
            _nextShot = DateTime.MinValue;
            _nextAdopt = DateTime.MinValue;
            AppLog.Info("[GPS] рейд начался");
        }
        _wasInRaid = inRaid;
    }

    private void TryAdopt()
    {
        if (_fix is not null || DateTime.UtcNow < _nextAdopt) return;
        _nextAdopt = DateTime.UtcNow.AddSeconds(5);
        var folder = _watchFolder;
        if (folder is null || !Directory.Exists(folder)) return;
        FileInfo? best = null;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.png"))
                if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc) best = file;
        }
        catch (Exception ex)
        {
            AppLog.Error("[GPS] не удалось просмотреть папку скриншотов", ex);
            return;
        }

        if (best is null || !ScreenshotNameParser.TryParse(best.Name, out var fix)) return;
        var entered = _logs.Tracker.EnteredAt;
        if (entered is { } at && best.LastWriteTimeUtc < at.UtcDateTime.AddSeconds(-20)) return;
        if (entered is null && DateTime.UtcNow - best.LastWriteTimeUtc > TimeSpan.FromMinutes(3)) return;
        _fix = fix;
        AppLog.Info($"[GPS] взята последняя позиция из {best.Name}");
    }

    private void MaybeShoot()
    {
        if (!_settings.GpsEnabled || !_settings.GpsAutoScreenshot || !_overlayVisible) return;
        if (!_logs.Tracker.InRaid || !GameDetector.IsTarkovRunning()) return;
        if (DateTime.UtcNow < _nextShot) return;
        if (!GameDetector.IsTarkovForeground()) return;

        var vk = ScreenshotHotkey.ToVirtualKey(_settings.GpsScreenshotKey);
        var ok = ScreenshotKeySender.TrySend(vk, out var err);
        _nextShot = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.GpsAutoScreenshotSeconds, 2, 600));
        if (!ok)
        {
            if (_keyFailures++ < 3)
                AppLog.Warn($"[GPS] не удалось нажать клавишу скриншота (код {err}). Если игра запущена от администратора, оверлей тоже нужно запустить от администратора.");
            return;
        }

        if (_announcedShot) return;
        _announcedShot = true;
        AppLog.Info($"[GPS] автоскриншот ({ScreenshotHotkey.Canonical(_settings.GpsScreenshotKey)})");
    }

    private void Publish()
    {
        GpsHud? hud = null;
        MapView? map = null;
        var showMap = false;
        bool visible;
        lock (_gate)
        {
            visible = _overlayVisible;
            var live = _settings.GpsEnabled && visible && _logs.Tracker.InRaid && GameDetector.IsTarkovRunning();
            if (live)
            {
                var (resolved, uncertain) = ResolveMap();
                var gameMap = WithShippedExtracts(resolved);
                var nearest = gameMap is null || _fix is null ? null : ExtractNav.Nearest(gameMap, _fix.Position, _logs.Tracker.SideName);
                hud = BuildHud(gameMap, uncertain, nearest, _fix);
                showMap = _settings.GpsMapVisible;
                if (showMap) map = BuildMap(gameMap, uncertain, nearest, _fix);
            }

            var key = Sig(hud, map, showMap, visible);
            if (key == _sig) return;
            _sig = key;
        }

        var hudCopy = hud;
        var mapCopy = map;
        var open = showMap;
        var opacity = Math.Clamp(_settings.GpsMapOpacity, 0.3, 1);
        _dispatcher.BeginInvoke(() =>
        {
            _overlay.SetGps(visible ? hudCopy : null);
            _map.Opacity = opacity;
            if (open) _map.Render(mapCopy);
            _map.SetOpen(open);
        });
    }

    private (GameMap? Map, bool Uncertain) ResolveMap()
    {
        var resolved = MapIndex.Find(_maps, _logs.Tracker.LocationId) ?? MapIndex.Find(_maps, _logs.Tracker.ScenePath);
        if (resolved is not null)
        {
            _lastMap = resolved;
            return (resolved, false);
        }
        return _lastMap is null ? (null, true) : (_lastMap, true);
    }

    private GpsHud? BuildHud(GameMap? map, bool uncertain, NearestExtract? nearest, ScreenshotFix? fix)
    {
        if (fix is null) return null;
        if (map is null || nearest is null)
            return new GpsHud(false, 0, 0, L.T("GPS · карта не определена", "GPS · map unknown"), "");

        var heading = WorldMath.HeadingDegrees(fix.Rotation);
        var rel = WorldMath.RelativeDegrees(nearest.BearingDegrees, heading);
        var meters = (int)Math.Round(nearest.DistanceMeters);
        var name = Pick(nearest.Extract.NameEn, nearest.Extract.NameRu);
        var mapName = Pick(map.NameEn, map.NameRu) + (uncertain ? "?" : "");
        return new GpsHud(true, rel, meters, Meters(meters), $"{name} · {mapName}");
    }

    private MapView BuildMap(GameMap? map, bool uncertain, NearestExtract? nearest, ScreenshotFix? fix)
    {
        if (map is null)
        {
            var quiet = L.T("GPS · карта не определена", "GPS · map unknown");
            return new MapView(quiet, quiet, 0, 0, 1, 0, 1, null, null, 0, []);
        }

        var side = _logs.Tracker.SideName;
        var underlay = MapUnderlayCatalog.Match(_underlays, map);
        var shipped = underlay is { Extracts.Length: > 0 };
        var extracts = shipped ? underlay!.Extracts : map.Extracts;
        var shown = new List<MapExtract>();
        foreach (var e in extracts)
        {
            var visible = shipped
                ? ExtractNav.OverviewVisible(e.Faction, side)
                : ExtractNav.FactionVisible(e.Faction, side);
            if (visible) shown.Add(e);
        }
        if (shown.Count == 0)
            shown.AddRange(extracts);

        var markers = new List<MapMarker>(shown.Count);
        foreach (var e in shown)
            markers.Add(new MapMarker(e.X, e.Z, Pick(e.NameEn, e.NameRu), e.Faction ?? "", nearest is not null && ReferenceEquals(e, nearest.Extract)));

        var mapName = Pick(map.NameEn, map.NameRu) + (uncertain ? "?" : "");
        var title = nearest is null
            ? mapName
            : $"{mapName} · {Meters((int)Math.Round(nearest.DistanceMeters))} · {Pick(nearest.Extract.NameEn, nearest.Extract.NameRu)}";
        var heading = fix is null ? 0 : WorldMath.HeadingDegrees(fix.Rotation);
        if (underlay is not null)
        {
            var labels = shipped
                ? shown.Select(e => new MapLabel(e.X, e.Z, e.NameEn, 200, e.NameRu)).ToArray()
                : underlay.Labels;
            return new MapView(
                title, null, underlay.Rotation,
                underlay.MinX, underlay.MaxX, underlay.MinZ, underlay.MaxZ,
                fix?.Position.X, fix?.Position.Z, heading, markers.ToArray(),
                underlay.ImagePath, labels, underlay.CompassDegrees, underlay.Credit);
        }

        var minX = map.MinX;
        var maxX = map.MaxX;
        var minZ = map.MinZ;
        var maxZ = map.MaxZ;
        if (fix is { } player)
        {
            minX = Math.Min(minX, player.Position.X);
            maxX = Math.Max(maxX, player.Position.X);
            minZ = Math.Min(minZ, player.Position.Z);
            maxZ = Math.Max(maxZ, player.Position.Z);
        }

        var padX = Math.Max(30, (maxX - minX) * 0.06);
        var padZ = Math.Max(30, (maxZ - minZ) * 0.06);
        return new MapView(
            title, null, map.NorthRotation,
            minX - padX, maxX + padX, minZ - padZ, maxZ + padZ,
            fix?.Position.X, fix?.Position.Z, heading, markers.ToArray());
    }

    private static string Sig(GpsHud? hud, MapView? map, bool showMap, bool overlayVisible)
    {
        var h = hud is null ? "-" : $"{hud.HasBearing}|{hud.ArrowDegrees:0.0}|{hud.Meters}|{hud.Primary}|{hud.Secondary}";
        var nearest = map?.Markers.FirstOrDefault(m => m.Nearest)?.Name ?? "";
        var m = map is null
            ? "-"
            : $"{showMap}|{map.Title}|{map.UnderlayImage}|{map.PlayerX:0.#}|{map.PlayerZ:0.#}|{map.Heading:0.#}|{map.Markers.Length}|{nearest}";
        return (overlayVisible ? "1" : "0") + h + m;
    }

    private GameMap? WithShippedExtracts(GameMap? map)
    {
        if (map is null) return null;
        var underlay = MapUnderlayCatalog.Match(_underlays, map);
        if (underlay is not { Extracts.Length: > 0 }) return map;
        return map with { Extracts = underlay.Extracts };
    }

    private static string Pick(string en, string ru)
        => L.En ? (string.IsNullOrWhiteSpace(en) ? ru : en) : (string.IsNullOrWhiteSpace(ru) ? en : ru);

    private static string Meters(int meters) => L.T($"{meters} м", $"{meters} m");

    private async Task DeleteLater(string path, CancellationToken ct)
    {
        try
        {
            string? folder;
            lock (_gate) folder = _watchFolder;
            if (folder is null || !ScreenshotPath.IsInside(folder, path)) return;
            var name = Path.GetFileName(path);
            long last = -1;
            var stable = 0;
            for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(200, ct);
                if (!_settings.GpsDeleteAfterParse || !File.Exists(path)) return;
                long len;
                try { len = new FileInfo(path).Length; }
                catch { continue; }
                if (len > 0 && len == last) stable++;
                else stable = 0;
                last = len;
                if (stable < 2) continue;
                try
                {
                    File.Delete(path);
                    AppLog.Info($"[GPS] скриншот удалён: {name}");
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            AppLog.Warn($"[GPS] не удалось удалить скриншот: {name}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("[GPS] удаление скриншота", ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); }
        catch { /* shutting down */ }
        _shots.Dispose();
        _catalog.Dispose();
        try
        {
            if (_map.Dispatcher.CheckAccess()) ShutdownMap();
            else _map.Dispatcher.Invoke(ShutdownMap);
        }
        catch { /* window already gone */ }
    }

    private void ShutdownMap()
    {
        _map.PrepareForExit();
        _map.Close();
    }
}
