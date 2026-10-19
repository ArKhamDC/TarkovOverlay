using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using TarkovOverlay.Models;
using TarkovOverlay.Utils;
using TarkovOverlay.Windows;

namespace TarkovOverlay.Services;

/// <summary>
/// Связывает всё вместе. Сканирование идёт в одном фоновом цикле (без наложения сканов),
/// UI обновляется только при изменении меток.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private const int HotkeyToggle = 1, HotkeyFullScan = 2, HotkeyRefresh = 3, HotkeyExit = 4, HotkeyMode = 5;
    private const uint ModAlt = 0x1, ModControl = 0x2;

    private readonly OverlayWindow _overlay;
    private readonly Dispatcher _dispatcher;
    private readonly TarkovDevApiClient _api = new();
    private readonly ItemDatabase _database = new();          // pve
    private readonly ItemDatabase _databasePvp = new();       // regular (pvp + season)
    private volatile string _mode = "pve";                    // эффективный режим: pvp | pve | season
    private DateTime _nextDetect = DateTime.MinValue;
    private ItemDatabase DbFor(string mode) => mode == "pve" ? _database : _databasePvp;
    private static string Label(string m) => m switch { "pvp" => "PvP", "season" => L.T("Сезон", "Season"), _ => "PvE" };
    private string Header => _mode == "season" ? L.T("[Сезон · только торговцы]", "[Season · traders only]") : $"[{Label(_mode)}]";
    private readonly OcrService _ocr = new();
    private readonly ScreenCaptureService _capture = new();
    private readonly CancellationTokenSource _cts = new();

    private AppSettings _settings = new();
    private ItemScanner? _scanner;
    private TooltipScanner? _tooltip;
    private OcrEnginePool? _bandPool;
    private TrayIcon? _tray;
    private Task? _scanLoop;
    private Task? _updater;

    private volatile bool _visible = true;
    private volatile bool _fullScanRequested;
    private volatile bool _disposed;
    private bool _wasDragging;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private readonly object _stateLock = new();
    private IReadOnlyList<PriceMarker> _fullMarkers = [];
    private DateTime _fullMarkersUntil = DateTime.MinValue;
    private PriceMarker? _cursorMarker;
    private string? _statusText;
    private DateTime _statusUntil = DateTime.MinValue;
    private string _lastUiKey = "";

    public OverlayController(OverlayWindow overlay)
    {
        _overlay = overlay;
        _dispatcher = overlay.Dispatcher;
    }

    public ItemDatabase Database => _database;
    public OcrService Ocr => _ocr;
    public AppSettings Settings => _settings;

    /// <summary>Инициализация без цикла сканирования (используется и в тестовом режиме).</summary>
    public async Task InitializeCoreAsync()
    {
        _settings = AppSettings.Load();
        L.Init(_settings.UiLanguage);
        AppLog.Info($"[Settings] режим={_settings.GameMode}, язык игры={_settings.Language}, интерфейс={L.Code}, скрыть из захвата={_settings.ExcludeOverlayFromCapture}, " +
                    $"только при активной игре={_settings.RequireGameForeground}");

        try
        {
            _ocr.Initialize();
        }
        catch (Exception ex)
        {
            AppLog.Error("[OCR] Не удалось инициализировать Tesseract", ex);
        }

        await _database.LoadCacheAsync(_settings.ForData("pve"));
        await _databasePvp.LoadCacheAsync(_settings.ForData("regular"));
        _mode = ResolveMode(log: true);
        _scanner = new ItemScanner(_database, _ocr, _capture, _settings);
        _tooltip = new TooltipScanner(_database, _ocr, _capture);
        try
        {
            var threads = _settings.OcrThreads > 0 ? _settings.OcrThreads : Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
            _bandPool = new OcrEnginePool("bands", Path.Combine(AppContext.BaseDirectory, "tessdata_fast"), threads);
            _scanner.BandPool = _bandPool;
            try { _scanner.BestPool = new OcrEnginePool("best", Path.Combine(AppContext.BaseDirectory, "tessdata"), 3); } catch (Exception ex2) { AppLog.Error("[OCR] best-пул не создан", ex2); }
        }
        catch (Exception ex)
        {
            AppLog.Error("[OCR] Пул tessdata_fast не создан", ex);
        }
    }

    public ItemScanner Scanner => _scanner ?? throw new InvalidOperationException("Not initialized");
    public TarkovDevApiClient Api => _api;

    public async Task StartAsync()
    {
        await InitializeCoreAsync();

        var excluded = false;
        if (_settings.ExcludeOverlayFromCapture)
        {
            excluded = _overlay.ExcludeFromCapture(out var error);
            AppLog.Info(excluded
                ? "[Overlay] Окно скрыто из захвата экрана (OCR не видит свои метки)"
                : $"[Overlay] SetWindowDisplayAffinity не сработал (код {error}) — свои метки закрашиваются перед OCR");
        }
        if (!excluded)
        { _scanner!.MaskProvider = () => _overlay.MarkerRects; _tooltip!.MaskProvider = () => _overlay.MarkerRects; }

        RegisterHotkeys();
        _tray = new TrayIcon(ToggleVisibility, RequestFullScan, RequestRefresh, OpenFolder, Exit);
        _tray.AddModeMenu(SetModeChoice);
        _tray.AddAction(L.T("Создать ярлык на рабочем столе", "Create desktop shortcut"), CreateShortcut, 4);
        _tray.AddLanguageMenu(_settings.UiLanguage, _settings.Language,
            v => ChangeLanguage(() => _settings.UiLanguage = v), v => ChangeLanguage(() => _settings.Language = v));
        _tray.SetMode(_settings.ModeChoice, Label(_mode));
        _database.Changed += () => _dispatcher.BeginInvoke(() => _tray?.SetTooltip(DbFor(_mode).Status));
        _databasePvp.Changed += () => _dispatcher.BeginInvoke(() => _tray?.SetTooltip(DbFor(_mode).Status));
        _tooltip!.Db = DbFor(_mode);

        _updater = Task.Run(() => _database.RunUpdaterAsync(_api, _settings.ForData("pve"), _cts.Token));
        _ = Task.Run(() => _databasePvp.RunUpdaterAsync(_api, _settings.ForData("regular"), _cts.Token));
        _scanLoop = Task.Run(() => ScanLoopAsync(_cts.Token));

        ShowStatus(L.T($"TarkovOverlay запущен · {Label(_mode)}{(_settings.ModeChoice == "auto" ? " (авто)" : "")} · {ModeHotkeyName} режим · " +
                   "наведите на предмет — цена под подсказкой · Ctrl+Alt+O скрыть · Ctrl+Alt+R обновить цены · Ctrl+Alt+X выход",
                   $"TarkovOverlay started · {Label(_mode)}{(_settings.ModeChoice == "auto" ? " (auto)" : "")} · {ModeHotkeyName} mode · " +
                   "hover an item — price appears under the tooltip · Ctrl+Alt+O hide · Ctrl+Alt+R refresh prices · Ctrl+Alt+X exit"), TimeSpan.FromSeconds(10));
    }

    public static string ModeHotkeyName = "Ctrl+Alt+M";
    private (string, bool) RegisterModeHotkey()
    {
        foreach (var (name, m, vk) in new[] { ("Ctrl+Alt+M", ModControl | ModAlt, 0x4Du), ("Ctrl+Shift+M", ModControl | 0x4u, 0x4Du), ("Ctrl+Alt+P", ModControl | ModAlt, 0x50u) })
            if (_overlay.RegisterHotkey(HotkeyMode, m, vk)) { ModeHotkeyName = name; return ($"режим {name}", true); }
        return ("режим (M/Shift+M/P)", false);
    }

    private void RegisterHotkeys()
    {
        var mods = ModControl | ModAlt;
        var results = new[]
        {
            ("Ctrl+Alt+O", _overlay.RegisterHotkey(HotkeyToggle, mods, 0x4F)),
            ("Ctrl+Alt+R", _overlay.RegisterHotkey(HotkeyRefresh, mods, 0x52)),
            ("Ctrl+Alt+X", _overlay.RegisterHotkey(HotkeyExit, mods, 0x58)),
            RegisterModeHotkey()
        };
        AppLog.Info("[Hotkeys] " + string.Join(", ", results.Select(r => $"{r.Item1}={(r.Item2 ? "ok" : "ЗАНЯТО")}")));
        _overlay.HotkeyPressed += id =>
        {
            switch (id)
            {
                case HotkeyToggle: ToggleVisibility(); break;
                case HotkeyFullScan: RequestFullScan(); break;
                case HotkeyRefresh: RequestRefresh(); break;
                case HotkeyExit: Exit(); break;
                case HotkeyMode: CycleMode(); break;
            }
        };
    }

    // ---------------- действия ----------------

    private void ToggleVisibility()
    {
        _visible = !_visible;
        _tray?.SetVisibleState(_visible);
        AppLog.Info(_visible ? "[Overlay] Показан" : "[Overlay] Скрыт");
        if (_visible)
            ShowStatus(L.T("TarkovOverlay включён", "TarkovOverlay enabled"), TimeSpan.FromSeconds(2));
        PushToUi();
    }

    public void TriggerFullScan() => RequestFullScan();

    private void RequestFullScan()
    {
        if (!_visible)
            ToggleVisibility();
        _scanner?.ResetBandCache();
        _fullScanRequested = true;
    }

    private void CycleMode()
    {
        string[] order = ["auto", "pvp", "pve", "season"];
        SetModeChoice(order[(Array.IndexOf(order, _settings.ModeChoice) + 1) % order.Length]);
    }

    private void SetModeChoice(string choice)
    {
        _settings.ModeChoice = choice;
        if (choice != "auto") _settings.LastManualMode = choice;
        _settings.Save();
        _nextDetect = DateTime.MinValue;
        ApplyMode(ResolveMode(log: true), toast: true);
    }

    /// <summary>auto → по логам игры, если сигнала нет — последний ручной выбор.</summary>
    private string ResolveMode(bool log = false)
    {
        if (_settings.ModeChoice != "auto") return _settings.ModeChoice;
        var d = ModeDetector.Detect();
        if (log) AppLog.Info($"[Mode] авто: {d ?? "не определён"} ({ModeDetector.LastSource})");
        return d ?? _settings.LastManualMode;
    }

    private void ApplyMode(string mode, bool toast)
    {
        var changed = mode != _mode;
        _mode = mode;
        if (_tooltip is not null) _tooltip.Db = DbFor(mode);
        if (changed || toast) AppLog.Info($"[Mode] режим цен: {Label(mode)} (выбор {_settings.ModeChoice})");
        _dispatcher.BeginInvoke(() => { _tray?.SetMode(_settings.ModeChoice, Label(mode)); _tray?.SetTooltip(DbFor(mode).Status); });
        if (toast || changed)
            ShowStatus($"{L.T("Режим цен", "Price mode")}: {Label(mode)}{(_settings.ModeChoice == "auto" ? L.T(" (авто)", " (auto)") : "")}" +
                       (mode == "season" ? L.T(" — барахолки сезона нет в tarkov.dev, показываю торговцев", " — no season flea data on tarkov.dev, showing traders") : ""), TimeSpan.FromSeconds(2.5));
    }

    /// <summary>Сохраняет выбор языка и перезапускает оверлей (новый экземпляр ждёт освобождения mutex).</summary>
    private void ChangeLanguage(Action apply)
    {
        apply();
        _settings.Save();
        AppLog.Info($"[Lang] интерфейс={_settings.UiLanguage}, игра={_settings.Language} — перезапуск");
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TarkovOverlay.exe");
            Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception ex) { AppLog.Error("[Lang] не удалось перезапустить", ex); return; }
        Exit();
    }

    private void CreateShortcut()
    {
        try { var lnk = ShortcutHelper.Create(); AppLog.Info($"[Shortcut] {lnk}"); ShowStatus(L.T("Ярлык «Tarkov Overlay» создан на рабочем столе", "\"Tarkov Overlay\" shortcut created on the desktop"), TimeSpan.FromSeconds(3)); }
        catch (Exception ex) { AppLog.Error("[Shortcut] не удалось создать ярлык", ex); ShowStatus(L.T("Не удалось создать ярлык", "Could not create shortcut"), TimeSpan.FromSeconds(3)); }
    }

    private void RequestRefresh()
    {
        AppLog.Info("[DB] Обновление цен по запросу");
        ShowStatus(L.T("Обновляю цены...", "Refreshing prices..."), TimeSpan.FromSeconds(4));
        _database.RequestRefresh();
        _databasePvp.RequestRefresh();
    }

    private static void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось открыть папку", ex);
        }
    }

    private void Exit()
    {
        AppLog.Info("Выход по команде пользователя");
        _dispatcher.BeginInvoke(() => Application.Current.Shutdown());
    }

    private void ShowStatus(string text, TimeSpan duration)
    {
        lock (_stateLock)
        {
            _statusText = text;
            _statusUntil = DateTime.Now + duration;
        }
        PushToUi();
    }

    // ---------------- цикл сканирования ----------------

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        var lastActive = (bool?)null;
        var errors = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var running = GameDetector.IsTarkovRunning();
                var foreground = !_settings.RequireGameForeground || GameDetector.IsTarkovForeground();
                if (_settings.ModeChoice == "auto" && DateTime.Now >= _nextDetect)
                {
                    _nextDetect = DateTime.Now.AddSeconds(5);
                    var m = ResolveMode();
                    if (m != _mode) ApplyMode(m, toast: true);
                }
                var ready = DbFor(_mode).IsLoaded && _ocr.IsReady && _scanner is not null;

                var active = _visible && running && foreground;
                if (active != lastActive)
                {
                    AppLog.Info($"[Loop] автоскан экрана: {(active ? "ВКЛ" : "выкл")} (игра запущена={running}, активна={foreground}, видим={_visible})");
                    lastActive = active;
                }

                // пока зажата левая кнопка (перетаскивание предмета) — метки скрыты, после отпускания пересканируем
                var dragging = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (active && dragging)
                {
                    lock (_stateLock) _fullMarkers = [];
                    _wasDragging = true;
                }
                else if (active && ready && _bandPool is not null && _settings.AutoFullScan)
                {
                    var released = _wasDragging;
                    _wasDragging = false;
                    _fullScanRequested = false;
                    var r = _scanner!.ScanBands();
                    if (r.Changed || released)
                    {
                        var markers = BuildCompactMarkers(r.Matches, _settings.GridCellPx > 0 ? _settings.GridCellPx : 63.0 * _capture.PrimaryBounds.Height / 1080);
                        lock (_stateLock)
                        {
                            _fullMarkers = markers;
                            _fullMarkersUntil = DateTime.MaxValue;
                        }
                    }
                }
                else if (!active)
                {
                    lock (_stateLock) _fullMarkers = [];
                }

                PriceMarker? cursorMarker = null;
                if (active && ready && _tooltip is not null)
                {
                    var t = _tooltip.Scan();
                    if (t?.Item is { } it)
                        cursorMarker = new PriceMarker(t.Box.X, t.Box.Bottom + 6, PriceFormat.TooltipLines(it, Header, _mode != "season"), MarkerStyle.Tooltip, t.Box.Width);
                }
                lock (_stateLock)
                    _cursorMarker = cursorMarker;

                PushToUi();
                errors = 0;
            }
            catch (Exception ex)
            {
                if (++errors <= 5 || errors % 100 == 0)
                    AppLog.Error($"[Loop] Ошибка сканирования (#{errors})", ex);
            }

            try
            {
                await Task.Delay(_settings.AutoFullScan ? _settings.ScanIntervalMs : _settings.TooltipIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal static List<PriceMarker> BuildCompactMarkers(IReadOnlyList<MatchedItem> matches, double cell = 63)
    {
        var markers = new List<PriceMarker>();
        (double W, double H) Fit(MatchedItem m, double nameH)
        {
            var items = m.Variants ?? [m.Item];
            var w = items.Min(i => Math.Min(i.Width, i.Height) == Math.Max(i.Width, i.Height) ? i.Width : Math.Min(i.Width, i.Height));
            var h = items.Min(i => Math.Min(i.Width, i.Height));
            // плитка: ширина w клеток (либо повернут — берём меньшую сторону), высота от надписи до низа плитки
            return (Math.Max(1, w) * cell - 6, Math.Max(1, h) * cell - nameH - 6);
        }
        foreach (var m in matches.OrderByDescending(m => m.Score))
        {
            if (m.Kind == MatchKind.FullName)
                continue; // строка подсказки игры: подробности и так в панели у курсора
            if (m.Ambiguous)
            {
                var amb = PriceFormat.AmbiguousLines(m.Variants ?? [m.Item]);
                var (aw, ah) = Fit(m, m.Height + 4);
                markers.Add(new PriceMarker(m.X + m.Width + 3.0, m.Y + m.Height + 2.0, amb.Count > 0 ? amb : ["X"], MarkerStyle.Compact, aw, ah));
                continue;
            }
            // метка под надписью плитки, выровнена по её правому краю (X = правый край)
            var x = m.X + m.Width + 3.0;
            var y = m.Y + m.Height + 2.0;
            if (markers.Any(p => Math.Abs(p.X - x) < 30 && Math.Abs(p.Y - y) < 20))
                continue;
            var lines = PriceFormat.CompactLines(m.Variants, m.Item);
            if (lines.Count == 0) continue;
            var (fw, fh) = Fit(m, m.Height + 4);
            markers.Add(new PriceMarker(x, y, lines, MarkerStyle.Compact, fw, fh));
        }
        return markers;
    }

    private void PushToUi()
    {
        if (_disposed)
            return;

        List<PriceMarker> markers = [];
        lock (_stateLock)
        {
            var now = DateTime.Now;
            if (_visible)
            {
                if (now < _fullMarkersUntil)
                    markers.AddRange(_fullMarkers);
                if (_cursorMarker is not null)
                    markers.Add(_cursorMarker);
            }

            string? status = now < _statusUntil ? _statusText : null;
            // нет цен вообще — постоянная подсказка, пока игра активна
            if (status is null && _visible && !_database.IsLoaded && GameDetector.IsTarkovRunning())
                status = _database.Status;
            if (status is not null && (_visible || now < _statusUntil))
                markers.Add(new PriceMarker(12, 12, [status], MarkerStyle.Status));
        }

        var key = string.Join("|", markers.Select(m => $"{m.X:0},{m.Y:0},{m.Style},{string.Join("/", m.Lines)}"));
        if (key == _lastUiKey)
            return;
        _lastUiKey = key;
        _dispatcher.BeginInvoke(() => _overlay.SetMarkers(markers));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            Task.WaitAll(new[] { _scanLoop, _updater }.Where(t => t is not null).Cast<Task>().ToArray(), TimeSpan.FromSeconds(5));
        }
        catch { /* отмена */ }

        _overlay.UnregisterHotkeys();
        _tray?.Dispose();
        _ocr.Dispose();
        _bandPool?.Dispose();
        _scanner?.BestPool?.Dispose();
        _api.Dispose();
    }
}
