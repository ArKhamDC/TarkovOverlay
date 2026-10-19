using System.Text.Json;
using TarkovOverlay.Models;
using TarkovOverlay.Utils;

namespace TarkovOverlay.Services;

public sealed record NameMatch(ItemPrice Item, int Score);

/// <summary>
/// База цен + индекс для сопоставления текста OCR.
/// Кэш с версией: items_cache_v2_{режим}_{язык}.json. Обновление в фоне: повтор с нарастающей паузой,
/// после успеха — раз в RefreshMinutes.
/// </summary>
public sealed class ItemDatabase
{
    private const int CacheVersion = 2;

    /// <summary>Слова интерфейса, которые не должны считаться названиями предметов.</summary>
    private static readonly HashSet<string> StopWords = new[]
    {
        "Общее", "Вещи", "Здоровье", "Персонаж", "Умения", "Карта", "Задания", "Достижения", "Престиж",
        "Назад", "Торговцы", "Барахолка", "Сборки", "Справочник", "Сообщения", "Опрос", "Расширения",
        "Убежище", "Главное", "Меню", "Уши", "Голова", "Лицо", "Глаза", "Повязка", "Броня", "Разгрузка",
        "Жетон", "Кобура", "Ремне", "Спине", "Ножны", "Карманы", "Рюкзак", "Защищенный", "Защищённый",
        "Контейнер", "Быстрый", "Доступ", "Оружие", "Новый", "Пресет", "Preset", "New", "Хранилище",
        "Схрон", "Сортировать", "Фильтр", "Поиск", "Найти", "Купить", "Продать", "Осмотреть", "Использовать",
        "Принять", "Отмена", "Готово", "Закрыть", "Выход", "Рейд", "Уровень", "Вес", "Скупщик", "Прапор",
        "Терапевт", "Лыжник", "Миротворец", "Механик", "Барахольщик", "Егерь", "Реф", "Смотритель",
        "PvE", "PvP", "BEAR", "USEC", "Default", "Стандарт"
    }.Select(TextNormalizer.Normalize).ToHashSet();

    private readonly string _cacheDir = AppSettings.Dir;
    private readonly SemaphoreSlim _refreshSignal = new(0);
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private volatile string _status = "Цены: загрузка...";

    public int Count => _snapshot.Items.Count;
    public bool IsLoaded => _snapshot.Items.Count > 0;
    public DateTime? DataTime => _snapshot.SavedAt;
    public string Status => _status;
    public event Action? Changed;

    private string CachePath(AppSettings s) => Path.Combine(_cacheDir, $"items_cache_v{CacheVersion}_{s.GameMode}_{s.Language}.json");

    // ---------------- загрузка ----------------

    public async Task LoadCacheAsync(AppSettings settings)
    {
        RetireOldCache();
        var path = CachePath(settings);
        if (!File.Exists(path))
        {
            AppLog.Info("[DB] Кэша v2 нет — ждём загрузку с tarkov.dev");
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var cache = await JsonSerializer.DeserializeAsync<CacheFile>(stream, JsonOptions);
            if (cache is null || cache.Version != CacheVersion || cache.Items.Count == 0
                || cache.GameMode != settings.GameMode || cache.Language != settings.Language)
            {
                AppLog.Warn("[DB] Кэш не подходит (версия/режим/язык) — игнорирую");
                return;
            }
            SetItems(cache.Items, cache.SavedAt, settings);
            AppLog.Info($"[DB] Загружено из кэша: {cache.Items.Count} предметов, данные от {cache.SavedAt:dd.MM HH:mm}");
        }
        catch (Exception ex)
        {
            AppLog.Error("[DB] Не удалось прочитать кэш", ex);
        }
    }

    /// <summary>Старый кэш другого формата (давал цены 0 ₽) — переименовываем, чтобы не мешал.</summary>
    private void RetireOldCache()
    {
        try
        {
            var old = Path.Combine(_cacheDir, "items_cache.json");
            if (File.Exists(old))
            {
                File.Move(old, Path.Combine(_cacheDir, "items_cache.v1.bak"), overwrite: true);
                AppLog.Info("[DB] Старый кэш items_cache.json (несовместимый формат) переименован в items_cache.v1.bak");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("[DB] Не удалось переименовать старый кэш", ex);
        }
    }

    public async Task<bool> TryUpdateAsync(TarkovDevApiClient api, AppSettings settings, CancellationToken ct)
    {
        try
        {
            var items = (await api.FetchItemsAsync(settings.GameMode, settings.Language, ct)).ToList();
            if (items.Count < 500)
                throw new InvalidOperationException($"Слишком мало предметов в ответе: {items.Count}");

            var now = DateTime.Now;
            SetItems(items, now, settings);
            await SaveCacheAsync(settings, items, now);
            AppLog.Info($"[DB] Цены обновлены: {items.Count} предметов ({ModeName(settings)})");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("[DB] Ошибка загрузки цен с tarkov.dev", ex);
            return false;
        }
    }

    /// <summary>Фоновый цикл обновления: повтор 30с → 1м → 2м → 5м, после успеха — каждые RefreshMinutes.</summary>
    public async Task RunUpdaterAsync(TarkovDevApiClient api, AppSettings settings, CancellationToken ct)
    {
        int[] retrySeconds = [30, 60, 120, 300];
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var ok = await TryUpdateAsync(api, settings, ct);
            TimeSpan wait;
            if (ok)
            {
                failures = 0;
                wait = TimeSpan.FromMinutes(settings.RefreshMinutes);
                SetStatus($"Цены: {Count} предм., {ModeName(settings)}, обновлено {DateTime.Now:HH:mm}");
            }
            else
            {
                wait = TimeSpan.FromSeconds(retrySeconds[Math.Min(failures, retrySeconds.Length - 1)]);
                failures++;
                var next = DateTime.Now + wait;
                SetStatus(IsLoaded
                    ? $"tarkov.dev недоступен — цены из кэша от {DataTime:dd.MM HH:mm}, повтор в {next:HH:mm}"
                    : $"Нет данных о ценах: tarkov.dev недоступен, повтор в {next:HH:mm:ss}");
                AppLog.Info($"[DB] Следующая попытка через {wait.TotalSeconds:0} с");
            }

            try
            {
                await _refreshSignal.WaitAsync(wait, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void RequestRefresh()
    {
        if (_refreshSignal.CurrentCount == 0)
            _refreshSignal.Release();
    }

    private void SetStatus(string status)
    {
        _status = status;
        Changed?.Invoke();
    }

    private static string ModeName(AppSettings s) => s.GameMode == "regular" ? "PvP" : "PvE";

    private async Task SaveCacheAsync(AppSettings settings, List<ItemPrice> items, DateTime savedAt)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = CachePath(settings);
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, new CacheFile
                {
                    Version = CacheVersion,
                    SavedAt = savedAt,
                    GameMode = settings.GameMode,
                    Language = settings.Language,
                    Items = items
                }, JsonOptions);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("[DB] Не удалось сохранить кэш", ex);
        }
    }

    private void SetItems(List<ItemPrice> items, DateTime savedAt, AppSettings settings)
    {
        _snapshot = Snapshot.Build(items, savedAt);
        if (_status.StartsWith("Цены: загрузка", StringComparison.Ordinal) || _status.StartsWith("Нет данных", StringComparison.Ordinal))
            _status = $"Цены: {items.Count} предм., {ModeName(settings)}, данные от {savedAt:dd.MM HH:mm}";
        Changed?.Invoke();
    }

    // ---------------- сопоставление ----------------

    /// <summary>
    /// Полное название (строка подсказки игры). Возвращает null, если совпадение слабое
    /// или неоднозначное — лучше не показать ничего, чем показать неверную цену.
    /// </summary>
    public NameMatch? MatchFullName(string text, int minScore)
    {
        var snap = _snapshot;
        var n = TextNormalizer.Normalize(text);
        if (n.Length < 6 || snap.Full.Count == 0)
            return null;

        FullEntry? best = null;
        var bestScore = 0;
        var secondScore = 0;

        foreach (var e in snap.Full)
        {
            var el = e.Norm.Length;
            int score;
            if (el == n.Length && e.Norm == n)
                score = 100;
            else if (el >= 8 && n.Length > el && n.Contains(e.Norm, StringComparison.Ordinal))
                score = Math.Min(99, 86 + 13 * el / n.Length);              // строка содержит название целиком
            else if (n.Length >= 12 && el > n.Length && e.Norm.StartsWith(n, StringComparison.Ordinal))
                score = Math.Min(97, 100 * n.Length / el + 10);              // название обрезано справа
            else
            {
                if (Math.Abs(el - n.Length) > Math.Max(el, n.Length) / 4)
                    continue;
                score = FuzzyMatcher.Score(n, e.Norm);
            }

            if (score > bestScore)
            {
                if (best is not null && best.Item.Id != e.Item.Id)
                    secondScore = bestScore;
                bestScore = score;
                best = e;
            }
            else if (score > secondScore && best is not null && best.Item.Id != e.Item.Id)
            {
                secondScore = score;
            }
        }

        if (best is null || bestScore < minScore)
            return null;
        if (bestScore < 98 && secondScore >= bestScore - 3)
            return null; // два похожих предмета — не угадываем
        return new NameMatch(best.Item, bestScore);
    }


    /// <summary>Полное название из подсказки игры: точное совпадение, иначе нечёткое (уникально).</summary>
    public ItemPrice? MatchTooltip(string text)
    {
        var snap = _snapshot;
        var n = TextNormalizer.Normalize(text);
        if (n.Length < 3) return null;
        var exact = snap.Full.Where(e => e.Norm == n).Select(e => e.Item).DistinctBy(i => i.Id).ToList();
        if (exact.Count >= 1) return exact.OrderByDescending(i => i.BestPrice ?? 0).First(); // одноимённые варианты (плакаты) — цены близки
        var sh = MatchShortName(text, 90)?.Item;
        if (n.Length < 6) return sh;
        var full = MatchFullName(text, 82)?.Item;
        if (full is not null) return full;
        if (sh is not null) return sh;
        if (n.Length >= 10)
        {
            var pre = snap.Full.Where(e => e.Norm.StartsWith(n) || n.StartsWith(e.Norm) && e.Norm.Length >= 10).Select(e => e.Item).DistinctBy(i => i.Id).ToList();
            if (pre.Count >= 1 && pre.Select(i => i.Name).Distinct().Count() == 1) return pre.OrderByDescending(i => i.BestPrice ?? 0).First();
        }
        return null;
    }

    /// <summary>Короткое название (надпись на плитке инвентаря). Только однозначные совпадения.</summary>
    public NameMatch? MatchShortName(string text, float confidence)
    {
        var snap = _snapshot;
        var n = TextNormalizer.Normalize(text);
        if (n.Length < 3 || n.Length > 24 || StopWords.Contains(n) || n.All(char.IsDigit))
            return null;
        if (n.Length == 3 && confidence < 85)
            return null;

        if (snap.Short.TryGetValue(n, out var item))
            return item is null ? null : new NameMatch(item, 100);

        // одна опечатка OCR допускается только для длинных слов и только если вариант единственный
        if (n.Length < 6 || confidence < 60)
            return null;

        ItemPrice? found = null;
        foreach (var (key, candidate) in snap.Short)
        {
            if (candidate is null || Math.Abs(key.Length - n.Length) > 1 || StopWords.Contains(key))
                continue;
            if (FuzzyMatcher.Levenshtein(n, key) != 1)
                continue;
            if (found is not null && found.Id != candidate.Id)
                return null;
            found = candidate;
        }
        return found is null ? null : new NameMatch(found, 90);
    }


    public static bool IsStopWord(string text) => StopWords.Contains(TextNormalizer.Normalize(text));

    public sealed record TileMatch(IReadOnlyList<ItemPrice> Items, string Kind);

    /// <summary>
    /// Надпись плитки → предмет(ы). exact → prefix (обрезанное имя) → fuzzy (ошибка OCR) → prefix~ (обрезка + ошибка).
    /// Неоднозначность снимается размером плитки (availW×availH в клетках, 99 = неизвестно);
    /// одинаковые по размеру варианты с близкой ценой (≤2.5×) → диапазон "≈". Иначе null.
    /// </summary>
    public TileMatch? MatchTile(string text, float confidence, int availW = 99, int availH = 99)
    {
        var snap = _snapshot;
        var key = TextNormalizer.Normalize(text);
        if (key.Length < 2 || key.Length > 30 || StopWords.Contains(key) || key.All(char.IsDigit))
            return null;
        var (cands, kind) = snap.CandCache.GetOrAdd(key, k => FindCandidates(snap, k));
        if (cands.Count == 0)
            return null;
        if (key.Length <= 3 && (kind != "exact" || confidence < 70))
            return null;
        if (key.Length == 2 && confidence < 92)
            return null;
        IReadOnlyList<ItemPrice> c = cands;
        if (c.Count > 1)
        {
            var f = c.Where(i => (i.Width <= availW && i.Height <= availH) || (i.Height <= availW && i.Width <= availH)).ToList();
            if (f.Count > 0) c = f;
        }
        if (c.Count == 1)
            return new TileMatch(c, kind);
        if (c.Count > 6)
            return null;
        var sizes = c.Select(i => (Math.Min(i.Width, i.Height), Math.Max(i.Width, i.Height))).Distinct().Count();
        var prices = c.Select(i => i.BestPrice ?? 0).Where(p => p > 0).ToList();
        if (sizes == 1 && prices.Count > 0 && prices.Max() <= 2.5 * prices.Min())
            return new TileMatch(c, kind + "≈");
        // название точное, но предметов несколько с разными размерами/ценами — честный "?"
        if (kind == "exact" && key.Length >= 5)
            return new TileMatch(c, "?");
        return null;
    }

    private static (List<ItemPrice>, string) FindCandidates(Snapshot snap, string key)
    {
        if (snap.ShortAll.TryGetValue(key, out var exact))
            return (exact, "exact");
        List<ItemPrice> Collect(IEnumerable<string> keys) => keys.SelectMany(k => snap.ShortAll[k]).DistinctBy(i => i.Id).ToList();
        if (key.Length >= 4)
        {
            var pre = snap.ShortAll.Keys.Where(k => k.Length > key.Length && k.StartsWith(key, StringComparison.Ordinal) && key.Length >= 0.45 * k.Length).ToList();
            if (pre.Count > 0) return (Collect(pre), "prefix");
        }
        if (key.Length >= 5)
        {
            var md = key.Length < 9 ? 1 : 2;
            var fz = snap.ShortAll.Keys.Where(k => Math.Abs(k.Length - key.Length) <= md && FuzzyMatcher.Levenshtein(key, k) <= md).ToList();
            if (fz.Count > 0) return (Collect(fz), "fuzzy");
            if (key.Length >= 6)
            {
                var pt = snap.ShortAll.Keys.Where(k => k.Length > key.Length && FuzzyMatcher.Levenshtein(key, k[..key.Length]) <= 1).ToList();
                if (pt.Count > 0) return (Collect(pt), "prefix~");
            }
        }
        return ([], "");
    }

    private sealed record FullEntry(ItemPrice Item, string Norm);

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new([], [], new Dictionary<string, ItemPrice?>(), null);

        /// <summary>Нормализованное короткое имя → все предметы с ним.</summary>
        public Dictionary<string, List<ItemPrice>> ShortAll { get; } = new(StringComparer.Ordinal);
        public System.Collections.Concurrent.ConcurrentDictionary<string, (List<ItemPrice> Items, string Kind)> CandCache { get; } = new();

        private Snapshot(List<ItemPrice> items, List<FullEntry> full, Dictionary<string, ItemPrice?> shortIndex, DateTime? savedAt)
        {
            Items = items;
            Full = full;
            Short = shortIndex;
            SavedAt = savedAt;
        }

        public List<ItemPrice> Items { get; }
        public List<FullEntry> Full { get; }
        /// <summary>Нормализованное короткое имя → предмет, или null если имя у нескольких предметов.</summary>
        public Dictionary<string, ItemPrice?> Short { get; }
        public DateTime? SavedAt { get; }

        public static Snapshot Build(List<ItemPrice> items, DateTime savedAt)
        {
            var full = items
                .Select(i => new FullEntry(i, TextNormalizer.Normalize(i.Name)))
                .Where(e => e.Norm.Length >= 3)
                .ToList();

            var shortIndex = new Dictionary<string, ItemPrice?>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var key = TextNormalizer.Normalize(item.ShortName);
                if (key.Length < 3 || StopWords.Contains(key))
                    continue;
                if (shortIndex.TryGetValue(key, out var existing))
                {
                    if (existing is not null && existing.Id != item.Id)
                        shortIndex[key] = null; // неоднозначно
                }
                else
                {
                    shortIndex[key] = item;
                }
            }
            var snap = new Snapshot(items, full, shortIndex, savedAt);
            foreach (var item in items)
            {
                var key = TextNormalizer.Normalize(item.ShortName);
                if (key.Length < 2 || StopWords.Contains(key))
                    continue;
                if (!snap.ShortAll.TryGetValue(key, out var list))
                    snap.ShortAll[key] = list = [];
                if (list.All(i => i.Id != item.Id))
                    list.Add(item);
            }
            return snap;
        }
    }

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public DateTime SavedAt { get; set; }
        public string GameMode { get; set; } = "";
        public string Language { get; set; } = "";
        public List<ItemPrice> Items { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
