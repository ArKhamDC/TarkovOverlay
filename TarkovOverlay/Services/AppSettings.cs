using System.Text.Json;

namespace TarkovOverlay.Services;

/// <summary>Настройки: %LocalAppData%\TarkovOverlay\settings.json (создаётся при первом запуске).</summary>
public sealed class AppSettings
{
    /// <summary>"pve" или "regular" (PvP).</summary>
    public string GameMode { get; set; } = "pve";
    /// <summary>Выбор режима: auto | pvp | pve | season. auto — по логам игры, иначе последний ручной выбор.</summary>
    public string ModeChoice { get; set; } = "auto";
    /// <summary>Последний ручной выбор (запасной вариант для auto).</summary>
    public string LastManualMode { get; set; } = "pve";

    public AppSettings ForData(string dataMode) => new()
    {
        GameMode = dataMode, Language = Language, UiLanguage = UiLanguage, RefreshMinutes = RefreshMinutes
    };

    private static readonly object SaveLock = new();

    public void Save()
    {
        lock (SaveLock)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options)); }
            catch (Exception ex) { AppLog.Error("[Settings] Не удалось сохранить settings.json", ex); }
        }
    }
    /// <summary>Язык клиента игры (названия предметов для OCR): auto | ru | en. auto — ищет и по русским, и по английским названиям.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Язык интерфейса оверлея: auto (по языку Windows) | ru | en.</summary>
    public string UiLanguage { get; set; } = "auto";
    /// <summary>Скрывать оверлей от захвата экрана (чтобы OCR не читал собственные метки).</summary>
    public bool ExcludeOverlayFromCapture { get; set; } = true;
    /// <summary>Сканировать только когда окно игры активно.</summary>
    public bool RequireGameForeground { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 350;
    /// <summary>Потоков OCR для автоскана (0 = авто: половина ядер, 2..6).</summary>
    public int OcrThreads { get; set; } = 0;
    /// <summary>Цены на всех плитках экрана (экспериментально, по умолчанию выкл.). Основной режим — панель под подсказкой игры.</summary>
    public bool AutoFullScan { get; set; } = false;
    public int TooltipIntervalMs { get; set; } = 60;
    public int RefreshMinutes { get; set; } = 20;
    /// <summary>Увеличение картинки перед OCR (мелкий шрифт игры).</summary>
    public double CursorOcrScale { get; set; } = 2.0;
    public double FullScanOcrScale { get; set; } = 2.0;
    /// <summary>Минимальная похожесть полного названия (0..100).</summary>
    public int MinFullNameScore { get; set; } = 84;
    /// <summary>Область вокруг курсора (физические пиксели).</summary>
    public int CursorRegionLeft { get; set; } = 220;
    public int CursorRegionRight { get; set; } = 440;
    public int CursorRegionUp { get; set; } = 160;
    public int CursorRegionDown { get; set; } = 160;
    /// <summary>Размер клетки инвентаря в пикселях (0 = авто: 63 px на 1080p).</summary>
    public double GridCellPx { get; set; } = 0;
    /// <summary>Сколько секунд держать метки после полного сканирования (Ctrl+Alt+S).</summary>
    public int FullScanLabelSeconds { get; set; } = 15;

    /// <summary>Стрелка к ближайшему выходу и окно карты.</summary>
    public bool GpsEnabled { get; set; } = true;
    /// <summary>Нажимать клавишу скриншота игры, пока идёт рейд.</summary>
    public bool GpsAutoScreenshot { get; set; } = true;
    /// <summary>Интервал автоскриншота, секунды.</summary>
    public int GpsAutoScreenshotSeconds { get; set; } = 10;
    /// <summary>Удалять скриншот после успешного разбора координат из имени файла.</summary>
    public bool GpsDeleteAfterParse { get; set; } = true;
    /// <summary>Папка скриншотов. Пусто — Documents\Escape from Tarkov\Screenshots.</summary>
    public string GpsScreenshotsPath { get; set; } = "";
    /// <summary>Клавиша скриншота в игре: PrintScreen, F12, буква или код виртуальной клавиши.</summary>
    public string GpsScreenshotKey { get; set; } = "PrintScreen";
    /// <summary>Показывать окно карты (не влияет на стрелку GPS).</summary>
    public bool GpsMapVisible { get; set; } = true;
    public double GpsMapLeft { get; set; } = 24;
    public double GpsMapTop { get; set; } = 80;
    public double GpsMapWidth { get; set; } = 420;
    public double GpsMapHeight { get; set; } = 320;
    /// <summary>Прозрачность окна карты, 0.3–1.</summary>
    public double GpsMapOpacity { get; set; } = 0.85;

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovOverlay");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("[Settings] settings.json повреждён, использую значения по умолчанию", ex);
            settings = new AppSettings();
        }

        settings.GameMode = settings.GameMode?.Trim().ToLowerInvariant() is "regular" or "pvp" ? "regular" : "pve";
        static string Norm(string? m) => m?.Trim().ToLowerInvariant() switch { "pvp" or "regular" => "pvp", "pve" => "pve", "season" => "season", _ => "" };
        settings.ModeChoice = Norm(settings.ModeChoice) is { Length: > 0 } mc ? mc : (settings.ModeChoice?.Trim().ToLowerInvariant() == "auto" ? "auto" : "auto");
        settings.LastManualMode = Norm(settings.LastManualMode) is { Length: > 0 } lm ? lm : (settings.GameMode == "regular" ? "pvp" : "pve");
        static string Lang(string? l) => l?.Trim().ToLowerInvariant() switch { "ru" => "ru", "en" => "en", _ => "auto" };
        settings.Language = Lang(settings.Language);
        settings.UiLanguage = Lang(settings.UiLanguage);
        settings.ScanIntervalMs = Math.Clamp(settings.ScanIntervalMs, 100, 5000);
        settings.RefreshMinutes = Math.Clamp(settings.RefreshMinutes, 5, 240);
        settings.CursorOcrScale = Math.Clamp(settings.CursorOcrScale, 1.0, 4.0);
        settings.FullScanOcrScale = Math.Clamp(settings.FullScanOcrScale, 1.0, 3.0);
        settings.MinFullNameScore = Math.Clamp(settings.MinFullNameScore, 60, 100);
        settings.GpsAutoScreenshotSeconds = Math.Clamp(settings.GpsAutoScreenshotSeconds, 2, 600);
        settings.GpsScreenshotsPath = settings.GpsScreenshotsPath?.Trim() ?? "";
        settings.GpsScreenshotKey = ScreenshotHotkey.Canonical(settings.GpsScreenshotKey);
        settings.GpsMapWidth = Math.Clamp(settings.GpsMapWidth <= 0 ? 420 : settings.GpsMapWidth, 220, 2400);
        settings.GpsMapHeight = Math.Clamp(settings.GpsMapHeight <= 0 ? 320 : settings.GpsMapHeight, 180, 1600);
        settings.GpsMapOpacity = Math.Clamp(settings.GpsMapOpacity <= 0 ? 0.85 : settings.GpsMapOpacity, 0.3, 1);

        try
        {
            Directory.CreateDirectory(Dir);
            lock (SaveLock)
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options)); // дописывает новые ключи
        }
        catch (Exception ex)
        {
            AppLog.Error("[Settings] Не удалось сохранить settings.json", ex);
        }
        return settings;
    }
}
