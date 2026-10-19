using TarkovOverlay.Utils;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

public sealed record CursorScanResult(MatchedItem? Match, Point Cursor, long ElapsedMs, int Lines, bool Cached);

public sealed record FullScanResult(IReadOnlyList<MatchedItem> Matches, long ElapsedMs, int Lines);

/// <summary>
/// Два режима:
///  • у курсора (постоянно): небольшой участок вокруг курсора → подсказка игры (полное имя)
///    или надпись на плитке под курсором (короткое имя) → одна панель с ценами;
///  • весь экран (по горячей клавише): короткие имена на всех плитках → компактные метки.
/// </summary>
public sealed class ItemScanner
{
    private readonly ItemDatabase _database;
    private readonly OcrService _ocr;
    private readonly ScreenCaptureService _capture;
    private readonly AppSettings _settings;

    /// <summary>Области собственных меток (физ. пиксели) — закрашиваются перед OCR.</summary>
    public Func<Rectangle[]>? MaskProvider { get; set; }

    private byte[]? _lastSample;
    private Point _lastCursor = new(-1, -1);
    private CursorScanResult? _lastResult;
    private int _cursorScans;
    private string _lastLoggedMatch = "";

    public ItemScanner(ItemDatabase database, OcrService ocr, ScreenCaptureService capture, AppSettings settings)
    {
        _database = database;
        _ocr = ocr;
        _capture = capture;
        _settings = settings;
    }

    /// <param name="frame">Для теста: готовый кадр экрана вместо захвата.</param>
    /// <param name="cursorOverride">Для теста: позиция курсора.</param>
    public CursorScanResult? ScanAroundCursor(Bitmap? frame = null, Point? cursorOverride = null)
    {
        var bounds = frame is null ? _capture.PrimaryBounds : new Rectangle(0, 0, frame.Width, frame.Height);
        var cursor = cursorOverride ?? ScreenCaptureService.GetCursorPosition();
        if (!bounds.Contains(cursor))
            return null;

        var region = Rectangle.Intersect(bounds, new Rectangle(
            cursor.X - _settings.CursorRegionLeft,
            cursor.Y - _settings.CursorRegionUp,
            _settings.CursorRegionLeft + _settings.CursorRegionRight,
            _settings.CursorRegionUp + _settings.CursorRegionDown));
        if (region.Width < 32 || region.Height < 32)
            return null;

        using var bmp = frame is null ? _capture.Capture(region) : frame.Clone(region, PixelFormat.Format24bppRgb);
        ApplyMask(bmp, region.Location);

        // картинка почти не изменилась (мелкая анимация игры не считается) — повторно не распознаём
        var sample = Sample(bmp);
        if (_lastResult is not null && cursor == _lastCursor && !ChangedEnough(_lastSample, sample))
            return _lastResult with { Cached = true };

        var sw = Stopwatch.StartNew();
        var lines = _ocr.Recognize(bmp, _settings.CursorOcrScale, region.X, region.Y);
        var matches = Match(lines);
        sw.Stop();

        // 1) подсказка игры с полным именем — самый надёжный вариант
        var best = matches
            .Where(m => m.Kind == MatchKind.FullName)
            .OrderBy(m => Distance(m, cursor))
            .FirstOrDefault();

        // 2) иначе — короткое имя плитки, внутри которой курсор. Надпись стоит в правом верхнем углу
        //    плитки, размер плитки известен из базы (ширина×высота в клетках, возможно повёрнута).
        var cell = _settings.GridCellPx > 0 ? _settings.GridCellPx : 63.0 * bounds.Height / 1080.0;
        best ??= matches
            .Where(m => m.Kind == MatchKind.ShortName && TileUnderCursor(m, cursor, cell) is { } tile
                        // внутри настоящей плитки не может быть надписи другого предмета
                        // (иначе это плитка, закрытая окном/другими предметами)
                        && !matches.Any(o => !ReferenceEquals(o, m) && tile.Contains(o.X + o.Width / 2, o.Y + o.Height / 2)))
            .OrderBy(m => cursor.Y - m.Y)
            .FirstOrDefault();

        _cursorScans++;
        var matchKey = best is null ? "-" : $"{best.Item.Name} ({best.Kind}, {best.Score})";
        if (_cursorScans <= 5 || matchKey != _lastLoggedMatch || _cursorScans % 200 == 0)
        {
            AppLog.Info($"[Cursor] #{_cursorScans} {sw.ElapsedMilliseconds} мс, строк OCR={lines.Count}, найдено={matches.Count}, показ: {matchKey}" +
                        (best is null ? "" : $" ← \"{best.OcrText}\""));
            _lastLoggedMatch = matchKey;
        }

        _lastSample = sample;
        _lastCursor = cursor;
        _lastResult = new CursorScanResult(best, cursor, sw.ElapsedMilliseconds, lines.Count, false);
        return _lastResult;
    }

    public FullScanResult ScanFullScreen(Bitmap? frame = null)
    {
        var sw = Stopwatch.StartNew();
        using var captured = frame is null ? _capture.CapturePrimaryScreen() : null;
        var source = frame ?? captured!;
        if (captured is not null)
            ApplyMask(captured, Point.Empty);

        var lines = _ocr.Recognize(source, _settings.FullScanOcrScale, 0, 0);
        var matches = Match(lines);
        sw.Stop();

        AppLog.Info($"[FullScan] {sw.ElapsedMilliseconds} мс, строк OCR={lines.Count}, найдено предметов={matches.Count}");
        foreach (var m in matches.Take(60))
            AppLog.Info($"[FullScan]   \"{m.OcrText}\" → {m.Item.Name} [{m.Kind}, {m.Score}] @({m.X},{m.Y})");
        return new FullScanResult(matches, sw.ElapsedMilliseconds, lines.Count);
    }


    // ---------------- непрерывный скан всего экрана (полосы, параллельно, только изменившиеся) ----------------

    public OcrEnginePool? BandPool { get; set; }
    public OcrEnginePool? BestPool { get; set; }
    private readonly Dictionary<Rectangle, (byte[] Sample, List<OcrWord> Words)> _bandCache = new();
    private List<MatchedItem> _lastFull = [];

    public sealed record BandScanResult(IReadOnlyList<MatchedItem> Matches, long ElapsedMs, int BandsTotal, int BandsOcr, bool Changed);

    public void ResetBandCache() { lock (_bandCache) _bandCache.Clear(); _mem.Clear(); }

    public BandScanResult ScanBands(Bitmap? frame = null, bool verbose = false)
    {
        var sw = Stopwatch.StartNew();
        using var captured = frame is null ? _capture.CapturePrimaryScreen() : null;
        var source = frame ?? captured!;
        if (captured is not null)
            ApplyMask(captured, Point.Empty);

        _lastSource = source;
        var W = source.Width; var H = source.Height;
        var k = H / 1080.0;
        int bandH = (int)(100 * k), step = (int)(76 * k), half = W / 2, ov = (int)(40 * k);
        var rects = new List<Rectangle>();
        for (var y = 0; y < H; y += step)
        {
            var h = Math.Min(bandH, H - y);
            rects.Add(new Rectangle(0, y, Math.Min(W, half + ov), h));
            rects.Add(new Rectangle(half - ov, y, W - (half - ov), h));
            if (y + bandH >= H) break;
        }

        // яркость всего кадра один раз, выборки по полосам без копирования картинок
        byte[] buf; int stride, bpp;
        var fd = source.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, source.PixelFormat);
        try
        {
            stride = Math.Abs(fd.Stride); bpp = Image.GetPixelFormatSize(source.PixelFormat) / 8;
            buf = new byte[stride * H];
            Marshal.Copy(fd.Scan0, buf, 0, buf.Length);
        }
        finally { source.UnlockBits(fd); }
        if (bpp < 3) bpp = 3;

        var todo = new List<(Rectangle R, Bitmap Bmp, byte[] Sample)>();
        foreach (var r in rects)
        {
            var sample = new byte[((r.Height + 2) / 3) * ((r.Width + 2) / 3)];
            var n = 0;
            for (var y = r.Y; y < r.Bottom; y += 3)
            for (var x = r.X; x < r.Right; x += 3)
            {
                var i = y * stride + x * bpp;
                var lv = (buf[i] * 29 + buf[i + 1] * 150 + buf[i + 2] * 77) >> 8;
                sample[n++] = (byte)(buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0 ? 0 : Math.Max(1, lv)); // 0 = закрашено (свои метки)
            }
            bool cached;
            lock (_bandCache)
                cached = _bandCache.TryGetValue(r, out var c) && !ChangedEnough(c.Sample, sample);
            if (!cached)
                todo.Add((r, source.Clone(r, PixelFormat.Format24bppRgb), sample));
        }

        if (todo.Count > 0 && BandPool is not null)
        {
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = BandPool.Size }, t =>
            {
                try
                {
                    var words = BandPool.RecognizeWords(t.Bmp, _settings.FullScanOcrScale, t.R.X, t.R.Y)
                        .Where(w => w.Height <= 22 * k && w.Text.Any(char.IsLetterOrDigit)).ToList();
                    lock (_bandCache) _bandCache[t.R] = (t.Sample, words);
                }
                finally { t.Bmp.Dispose(); }
            });
        }

        if (todo.Count == 0 && !_gridPending)
            return new BandScanResult(_lastFull, sw.ElapsedMilliseconds, rects.Count, 0, false);

        List<OcrWord> all;
        lock (_bandCache) all = _bandCache.Values.SelectMany(v => v.Words).ToList();
        // убрать дубли из перекрытий полос
        var words = new List<OcrWord>();
        foreach (var w in all.OrderByDescending(w => w.Confidence))
            if (!words.Any(o => Math.Abs(o.X - w.X) < 8 && Math.Abs(o.Y - w.Y) < 6))
                words.Add(w);
        var cell = _settings.GridCellPx > 0 ? _settings.GridCellPx : 63.0 * k;
        var matches = new List<MatchedItem>();

        // 1) подсказки игры (длинные строки) — полное имя
        foreach (var line in BuildLines(words))
        {
            if (line.Words.Count < 2) continue;
            var full = _database.MatchFullName(line.Text, _settings.MinFullNameScore);
            if (full is not null)
                matches.Add(new MatchedItem(full.Item, line.X, line.Y, line.Width, line.Height, full.Score, MatchKind.FullName, line.Text) { Source = "full" });
        }

        // 2) надписи плиток: слово и пара соседних слов
        var units = new List<(string Text, int X, int Y, int W, int H, float Conf, int Parts)>();
        foreach (var w in words)
        {
            units.Add((w.Text, w.X, w.Y, w.Width, w.Height, w.Confidence, 1));
            var next = words.Where(o => Math.Abs(o.Y - w.Y) < 5 && o.X - (w.X + w.Width) is >= 0 and <= 6).OrderBy(o => o.X).FirstOrDefault();
            if (next is not null)
            {
                var y1 = Math.Min(w.Y, next.Y);
                units.Add(($"{w.Text} {next.Text}", w.X, y1, next.X + next.Width - w.X,
                    Math.Max(w.Y + w.Height, next.Y + next.Height) - y1, Math.Min(w.Confidence, next.Confidence), 2));
            }
        }
        var resolved = new List<(MatchedItem M, int Parts, float Conf)>();
        foreach (var u in units)
        {
            if (u.Text.Split(' ').Any(ItemDatabase.IsStopWord)) continue;
            var R = u.X + u.W;
            var left = words.Where(o => Math.Abs(o.Y - u.Y) < 6 && o.X + o.Width < u.X - 2 && u.X - (o.X + o.Width) < cell * 4).Select(o => o.X + o.Width).ToList();
            var aw = left.Count > 0 ? Math.Max(1, (int)Math.Round((R - left.Max()) / cell)) : 99;
            var below = words.Where(o => o.Y - u.Y > 20 && o.Y - u.Y < cell * 5 && Math.Abs(o.X + o.Width - R) < 9).Select(o => o.Y).ToList();
            var ah = below.Count > 0 ? Math.Max(1, (int)Math.Round((below.Min() - u.Y) / cell)) : 99;
            var tm = _database.MatchTile(u.Text, u.Conf, aw, ah);
            if (tm is null) continue;
            resolved.Add((new MatchedItem(tm.Items[0], u.X, u.Y, u.W, u.H, (int)u.Conf, MatchKind.ShortName, u.Text)
                { Variants = tm.Items.Count > 1 ? tm.Items : null, Source = "p1:" + tm.Kind }, u.Parts, u.Conf));
        }
        foreach (var r in resolved.OrderByDescending(r => r.Parts).ThenByDescending(r => r.Conf))
        {
            var m = r.M;
            if (matches.Any(o => Math.Abs(o.Y - m.Y) < 9 && o.X <= m.X + m.Width && m.X <= o.X + o.Width)) continue;
            if (matches.Any(o => o.Kind == MatchKind.FullName && o.Y - 4 <= m.Y && m.Y <= o.Y + o.Height + 4 && o.X - 4 <= m.X && m.X <= o.X + o.Width)) continue;
            matches.Add(m);
        }

        // 2b) второе чтение неопознанных "похожих на надпись" слов моделью best (PSM 7, x3)
        var p2Count = SecondRead(matches, words, buf, stride, bpp, W, H, k);

        // 3) проход по сетке: пустые места решётки 63 px рядом с найденными плитками
        var gridCount = GridPass(matches, buf, stride, bpp, W, H, k, cell, source);

        // "≈"-совпадение без соседей на той же решётке — скорее всего мусор (экипировка, текстуры)
        static double MD(double a, double c) { var m = ((a % c) + c) % c; return Math.Min(m, c - m); }
        matches.RemoveAll(m => m.Variants is not null && !m.Ambiguous && !(m.Source.StartsWith("p1:exact", StringComparison.Ordinal) && m.Score >= 85 && TextNormalizer.Normalize(m.OcrText).Length >= 5) && !matches.Any(o => !ReferenceEquals(o, m) && o.Kind == MatchKind.ShortName
            && Math.Abs(o.X + o.Width - (m.X + m.Width)) < cell * 3 && Math.Abs(o.Y - m.Y) < cell * 3
            && MD(o.X + o.Width - (m.X + m.Width), cell) <= 6 * k && MD(o.Y - m.Y, cell) <= 6 * k));

        // неоднозначные названия: выбираем вариант по иконке плитки (сравнение с картинками tarkov.dev)
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.Variants is not { Count: >= 2 and <= 8 } v) continue;
            var scored = v.Select(it => (It: it, S: IconScore(it, m, buf, stride, bpp, W, H, cell, k))).OrderByDescending(t => t.S).ToList();
            if (scored[0].S >= 0.45 && scored[0].S - scored[1].S >= 0.25)
            {
                matches[i] = m with { Item = scored[0].It, Variants = null, Source = "icon:" + m.Source };
                if (verbose) AppLog.Info($"[Icon] \"{m.OcrText}\" → {scored[0].It.Name} ({scored[0].S:0.00} vs {scored[1].S:0.00})");
            }
        }

        // одинаковые плитки: копия уже опознанной плитки того же размера (сравнение всей плитки) получает тот же предмет
        var copies = 0;
        var known = matches.Where(m => m.Kind == MatchKind.ShortName && m.Variants is null && !m.Ambiguous)
            .GroupBy(m => m.Item.Id).Select(g => g.First())
            .Select(m => (M: m, V: TileVec(m.X + m.Width, m.Y, m.Item.Width, m.Item.Height, buf, stride, bpp, W, H, cell, k))).Where(t => t.V is not null).ToList();
        foreach (var (R, Y) in _allSlots.ToList())
        {
            if (matches.Any(o => Math.Abs(o.X + o.Width - R) < 30 * k && Math.Abs(o.Y - Y) < 9 * k)) continue;
            foreach (var (km, kv) in known)
            {
                var v = TileVec(R, Y, km.Item.Width, km.Item.Height, buf, stride, bpp, W, H, cell, k);
                if (v is null || Ncc(v, kv!) < 0.9) continue;
                var w = (int)(40 * k);
                matches.Add(km with { X = R - w, Y = Y, Width = w, Height = km.Height, Source = "copy:" + km.Source });
                copies++;
                break;
            }
        }
        if (verbose) AppLog.Info($"[Copy] одинаковых плиток: +{copies}");

        matches = Stabilize(matches, k, buf, stride, bpp, W, H);
        if (verbose)
            foreach (var m in matches.OrderBy(m => m.Y).ThenBy(m => m.X))
                AppLog.Info($"[AutoScan]   {m.Source,-14} \"{m.OcrText}\" → {m.Item.Name}{(m.Variants is { } v ? $" (≈{v.Count})" : "")} @({m.X + m.Width},{m.Y})");
        _lastFull = matches;
        sw.Stop();
        AppLog.Info($"[AutoScan] {sw.ElapsedMilliseconds} мс, полос OCR {todo.Count}/{rects.Count}, слов {words.Count}, предметов {matches.Count} (сетка +{gridCount}, 2-е чтение +{p2Count}, OCR ячеек {_lastSlotOcr})");
        return new BandScanResult(matches, sw.ElapsedMilliseconds, rects.Count, todo.Count, true);
    }



    private sealed class Mem { public MatchedItem M = null!; public byte[] Fp = []; public DateTime Seen; }
    private readonly List<Mem> _mem = [];

    private static byte[] Fingerprint(MatchedItem m, double k, byte[] buf, int stride, int bpp, int W, int H)
    {
        // вся плитка (по размеру предмета, с иконкой), шаг 3 px
        var cell = 63.0 * k;
        int R = m.X + m.Width + (int)(3 * k), top = Math.Max(0, m.Y - (int)(3 * k));
        int x0 = Math.Max(0, R - (int)(Math.Max(1, Math.Min(m.Item.Width, m.Item.Height)) * cell) + 4), x1 = Math.Min(W, R - 2);
        int y1 = Math.Min(H, top + (int)(Math.Max(1, Math.Min(m.Item.Width, m.Item.Height)) * cell) - 2);
        var list = new List<byte>(1024);
        for (var y = top + 2; y < y1; y += 3)
        for (var x = x0; x < x1; x += 3)
        {
            var i = y * stride + x * bpp;
            var black = buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0;
            list.Add(black ? (byte)0 : (byte)Math.Max(1, (buf[i] * 29 + buf[i + 1] * 150 + buf[i + 2] * 77) >> 8));
        }
        return list.ToArray();
    }

    private static bool FpSame(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return false;
        int valid = 0, diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == 0 || b[i] == 0) continue;
            valid++;
            if (Math.Abs(a[i] - b[i]) > 40) diff++;
        }
        return valid >= 30 && diff <= valid * 0.05;
    }

    /// <summary>
    /// Метка живёт, пока не изменились пиксели под её надписью (отпечаток), а не N циклов.
    /// Старые метки помнятся (до 3 мин): после прокрутки назад они возвращаются сразу.
    /// </summary>
    private List<MatchedItem> Stabilize(List<MatchedItem> fresh, double k, byte[] buf, int stride, int bpp, int W, int H)
    {
        static bool Overlap(MatchedItem a, MatchedItem b, double k) =>
            Math.Abs(a.X + a.Width - (b.X + b.Width)) < 30 * k && Math.Abs(a.Y - b.Y) < 9 * k;
        var now = DateTime.Now;
        var result = new List<MatchedItem>(fresh);
        foreach (var f in fresh.Where(f => f.Kind != MatchKind.FullName))
        {
            _mem.RemoveAll(e => Overlap(e.M, f, k));
            _mem.Add(new Mem { M = f, Fp = Fingerprint(f, k, buf, stride, bpp, W, H), Seen = now });
        }
        foreach (var e in _mem)
        {
            if (fresh.Any(f => ReferenceEquals(f, e.M))) continue;
            if (!FpSame(e.Fp, Fingerprint(e.M, k, buf, stride, bpp, W, H))) continue;
            e.Seen = now;
            result.Add(e.M);
        }
        _mem.RemoveAll(e => now - e.Seen > TimeSpan.FromMinutes(3));
        if (_mem.Count > 700) _mem.RemoveRange(0, _mem.Count - 700);
        return result;
    }

    private readonly Dictionary<ulong, string> _p2Cache = new();

    private int SecondRead(List<MatchedItem> matches, List<OcrWord> words, byte[] buf, int stride, int bpp, int W, int H, double k)
    {
        if (BestPool is null) return 0;
        byte Lum(int x, int y) { var i = y * stride + x * bpp; return (byte)((buf[i] * 29 + buf[i + 1] * 150 + buf[i + 2] * 77) >> 8); }
        var cand = new List<(OcrWord W, Rectangle Rect, ulong Hash)>();
        foreach (var w in words)
        {
            var key = TextNormalizer.Normalize(w.Text);
            if (key.Length < 3 || w.Height < 6 * k || w.Height > 18 * k || w.Width < 10 * k) continue;
            if (ItemDatabase.IsStopWord(w.Text) || key.All(char.IsDigit)) continue;
            if (w.Confidence >= 85 && key.Length >= 4) continue;
            if (matches.Any(o => Math.Abs(o.Y - w.Y) < 9 * k && o.X <= w.X + w.Width + 4 && w.X - 4 <= o.X + o.Width)) continue;
            var r = Rectangle.FromLTRB(Math.Max(0, w.X - 4), Math.Max(0, w.Y - 3), Math.Min(W, w.X + w.Width + 4), Math.Min(H, w.Y + w.Height + 3));
            if (r.Width < 8 || r.Height < 6) continue;
            ulong h = 1469598103934665603UL;
            for (var y = r.Top; y < r.Bottom; y++)
            for (var x = r.Left; x < r.Right; x++) h = (h ^ (ulong)(Lum(x, y) >> 5)) * 1099511628211UL;
            cand.Add((w, r, h));
        }
        List<(Rectangle Rect, ulong Hash)> todo;
        lock (_p2Cache)
        {
            if (_p2Cache.Count > 4000) _p2Cache.Clear();
            var pending = cand.Where(c => !_p2Cache.ContainsKey(c.Hash)).DistinctBy(c => c.Hash).ToList();
            if (pending.Count > 40) _gridPending = true;
            todo = pending.Take(40).Select(c => (c.Rect, c.Hash)).ToList();
        }
        if (todo.Count > 0)
        {
            var bmps = todo.Select(t => (t.Hash, Bmp: CloneRect(t.Rect))).ToList();
            Parallel.ForEach(bmps, new ParallelOptions { MaxDegreeOfParallelism = BestPool.Size }, t =>
            {
                try { var text = BestPool.ReadLine(t.Bmp, 3.0); lock (_p2Cache) _p2Cache[t.Hash] = text; }
                finally { t.Bmp.Dispose(); }
            });
        }
        var added = 0;
        foreach (var c in cand)
        {
            string? text; lock (_p2Cache) _p2Cache.TryGetValue(c.Hash, out text);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var toks = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => TextNormalizer.Normalize(t).Length > 0).ToList();
            if (toks.Count == 0 || toks.Count > 3) continue;
            foreach (var t in new[] { string.Join(" ", toks), toks[^1] }.Distinct())
            {
                var key = TextNormalizer.Normalize(t);
                if (key.Length < 3 || t.Split(' ').Any(ItemDatabase.IsStopWord)) continue;
                if (key.Length == 3 && toks.Count > 1) continue;
                var tm = _database.MatchTile(t, 90);
                if (tm is null || tm.Kind == "?") continue;
                if (matches.Any(o => Math.Abs(o.Y - c.W.Y) < 9 * k && o.X <= c.W.X + c.W.Width + 4 && c.W.X - 4 <= o.X + o.Width)) break;
                matches.Add(new MatchedItem(tm.Items[0], c.W.X, c.W.Y, c.W.Width, c.W.Height, 90, MatchKind.ShortName, t)
                    { Variants = tm.Items.Count > 1 ? tm.Items : null, Source = "p2:" + tm.Kind });
                added++;
                break;
            }
        }
        return added;
    }

    private Bitmap CloneRect(Rectangle r)
    {
        // кадр берётся из последнего захвата
        return _lastSource!.Clone(r, PixelFormat.Format24bppRgb);
    }
    private Bitmap? _lastSource;


    private readonly Dictionary<(string, int, int), byte[]?> _iconCache = new();

    private byte[]? IconPixels(ItemPrice it, int tw, int th)
    {
        lock (_iconCache)
        {
            if (_iconCache.TryGetValue((it.Id, tw, th), out var cached)) return cached;
            byte[]? px = null;
            var path = Path.Combine(AppContext.BaseDirectory, "icons", it.Id + ".png");
            if (File.Exists(path))
            {
                try
                {
                    using var src = new Bitmap(path);
                    using var bmp = new Bitmap(tw, th, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        g.DrawImage(src, new Rectangle(0, 0, tw, th));
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    px = new byte[tw * th * 3];
                    for (var y = 0; y < th; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * tw * 3, tw * 3);
                    bmp.UnlockBits(data);
                }
                catch (Exception ex) { AppLog.Warn($"[Icon] {path}: {ex.Message}"); }
            }
            _iconCache[(it.Id, tw, th)] = px;
            return px;
        }
    }

    /// <summary>Нормированная корреляция плитки (по размеру кандидата) с его иконкой; закрашенные (свои метки) пиксели не учитываются.</summary>
    private double IconScore(ItemPrice it, MatchedItem m, byte[] buf, int stride, int bpp, int W, int H, double cell, double k)
    {
        int tw = (int)Math.Round(it.Width * cell), th = (int)Math.Round(it.Height * cell);
        int x1 = m.X + m.Width + (int)(3 * k), y0 = m.Y - (int)(3 * k), x0 = x1 - tw;
        if (x0 < 0 || y0 < 0 || x1 > W || y0 + th > H) return -1;
        var icon = IconPixels(it, tw, th);
        if (icon is null) return -1;
        int top = (int)(16 * k), mg = (int)(8 * k);
        double sa = 0, sb = 0, n = 0;
        for (var y = top; y < th - mg; y += 2)
        for (var x = mg; x < tw - mg; x += 2)
        {
            var i = (y0 + y) * stride + (x0 + x) * bpp; var j = (y * tw + x) * 3;
            if (buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0) continue;
            for (var c = 0; c < 3; c++) { sa += buf[i + c]; sb += icon[j + c]; n++; }
        }
        if (n < 300) return -1;
        double ma = sa / n, mb = sb / n, ab = 0, aa = 0, bb = 0;
        for (var y = top; y < th - mg; y += 2)
        for (var x = mg; x < tw - mg; x += 2)
        {
            var i = (y0 + y) * stride + (x0 + x) * bpp; var j = (y * tw + x) * 3;
            if (buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0) continue;
            for (var c = 0; c < 3; c++) { var a = buf[i + c] - ma; var b = icon[j + c] - mb; ab += a * b; aa += a * a; bb += b * b; }
        }
        return ab / (Math.Sqrt(aa * bb) + 1e-6);
    }


    private static float[]? TileVec(int R, int Y, int w, int h, byte[] buf, int stride, int bpp, int W, int H, double cell, double k)
    {
        int x1 = R + (int)(3 * k), y0 = Y - (int)(3 * k);
        int tw = (int)(w * cell), th = (int)(h * cell), x0 = x1 - tw;
        if (x0 < 0 || y0 < 0 || x1 > W || y0 + th > H) return null;
        int top = (int)(16 * k), mg = (int)(8 * k);
        var v = new List<float>(800);
        for (var y = top; y < th - mg; y += 3)
        for (var x = mg; x < tw - mg; x += 3)
        {
            var i = (y0 + y) * stride + (x0 + x) * bpp;
            v.Add(buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0 ? float.NaN : (buf[i] * 29 + buf[i + 1] * 150 + buf[i + 2] * 77) >> 8);
        }
        return v.ToArray();
    }

    private static double Ncc(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1;
        double sa = 0, sb = 0; var n = 0;
        for (var i = 0; i < a.Length; i++) { if (float.IsNaN(a[i]) || float.IsNaN(b[i])) continue; sa += a[i]; sb += b[i]; n++; }
        if (n < 50) return -1;
        double ma = sa / n, mb = sb / n, ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (float.IsNaN(a[i]) || float.IsNaN(b[i])) continue;
            var x = a[i] - ma; var y = b[i] - mb; ab += x * y; aa += x * x; bb += y * y;
        }
        return aa < 1 || bb < 1 ? -1 : ab / Math.Sqrt(aa * bb);
    }

    private static List<OcrLine> BuildLines(List<OcrWord> words)
    {
        var lines = new List<OcrLine>();
        var sorted = words.OrderBy(w => w.X).ToList();
        var used = new bool[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            if (used[i]) continue;
            var line = new List<OcrWord> { sorted[i] }; used[i] = true;
            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (used[j]) continue;
                var last = line[^1];
                if (Math.Abs(sorted[j].Y - last.Y) < 5 && sorted[j].X - (last.X + last.Width) is >= -2 and <= 8)
                { line.Add(sorted[j]); used[j] = true; }
            }
            lines.Add(OcrService.ToLine(line));
        }
        return lines;
    }

    private readonly Dictionary<ulong, string> _slotCache = new();
    private readonly List<(int R, int Y)> _allSlots = new();
    private int _lastSlotOcr;
    public static bool GridDebug;
    private bool _gridPending;
    public int GridOcrPerCycle { get; set; } = 60;

    private int GridPass(List<MatchedItem> matches, byte[] buf, int stride, int bpp, int W, int H, double k, double cell, Bitmap source)
    {
        _lastSlotOcr = 0;
        byte Lum(int x, int y) { var i = y * stride + x * bpp; return (byte)((buf[i] * 29 + buf[i + 1] * 150 + buf[i + 2] * 77) >> 8); }
        static double ModDist(double a, double c) { var m = ((a % c) + c) % c; return Math.Min(m, c - m); }

        var anchors = matches.Where(m => m.Kind == MatchKind.ShortName).Select(m => (R: (double)(m.X + m.Width), Y: (double)m.Y)).ToList();
        var clusters = new List<(double R0, double Y0, List<(double R, double Y)> Pts)>();
        foreach (var a in anchors)
        {
            var c = clusters.FirstOrDefault(c => ModDist(a.R - c.R0, cell) <= 6 * k && ModDist(a.Y - c.Y0, cell) <= 6 * k);
            if (c.Pts is null) clusters.Add((a.R, a.Y, [a]));
            else c.Pts.Add(a);
        }

        var slots = new List<(int R, int Y)>();
        _allSlots.Clear();
        static double Centered(double d, double c) { var m = ((d % c) + c) % c; return m > c / 2 ? m - c : m; }
        static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }
        // ложные решётки (экипировка, подсказки, края плиток) дают сотни пустых ячеек: берём самую крупную
        // и только заметно отличающиеся крупные (≥6 точек) — например, соседний контейнер
        var kept = new List<(double R0, double Y0, List<(double R, double Y)> Pts)>();
        foreach (var c in clusters.Where(c => c.Pts.Count >= 3).OrderByDescending(c => c.Pts.Count))
        {
            if (kept.Count > 0 && c.Pts.Count < 6) continue;
            if (kept.Any(o => ModDist(c.R0 - o.R0, cell) <= 15 * k && ModDist(c.Y0 - o.Y0, cell) <= 15 * k)) continue;
            kept.Add(c);
        }
        foreach (var (cr0, cy0, pts) in kept)
        {
            // начало решётки — по медиане отклонений, а не по первой (возможно неточной) точке
            var r0 = cr0 + Median(pts.Select(p => Centered(p.R - cr0, cell)).ToList());
            var y0 = cy0 + Median(pts.Select(p => Centered(p.Y - cy0, cell)).ToList());
            double minR = pts.Min(p => p.R) - cell, maxR = pts.Max(p => p.R) + cell;
            double minY = pts.Min(p => p.Y) - cell, maxY = pts.Max(p => p.Y) + cell;
            for (var n = (int)Math.Floor((minR - r0) / cell); r0 + n * cell <= maxR; n++)
            for (var m = (int)Math.Floor((minY - y0) / cell); y0 + m * cell <= maxY; m++)
            {
                int R = (int)Math.Round(r0 + n * cell), Y = (int)Math.Round(y0 + m * cell);
                if (R < minR || Y < minY || R < cell || R > W - 4 || Y < 3 || Y > H - 16 * k) continue;
                if (matches.Any(o => Math.Abs(o.X + o.Width - R) < 30 * k && Math.Abs(o.Y - Y) < 9 * k)) continue;
                if (matches.Any(o => o.Kind == MatchKind.FullName && o.X - 4 <= R && R - 50 <= o.X + o.Width && o.Y - 8 <= Y && Y <= o.Y + o.Height + 4)) continue;
                if (slots.Any(s => Math.Abs(s.R - R) < 25 * k && Math.Abs(s.Y - Y) < 8 * k)) continue;
                _allSlots.Add((R, Y));
                // есть ли в месте надписи светлый текст на тёмном фоне
                int x0 = Math.Max(2, R - (int)(58 * k)), x1 = Math.Min(W - 3, R + 2), ya = Math.Max(2, Y - 2), yb = Math.Min(H - 3, Y + (int)(11 * k));
                var count = 0;
                for (var y = ya; y < yb && count < 20; y++)
                for (var x = x0; x < x1; x++)
                {
                    var v = Lum(x, y);
                    if (v <= 120) continue;
                    var mn = 255;
                    for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++) mn = Math.Min(mn, Lum(x + dx, y + dy));
                    if (v - mn > 60 && ++count >= 20) break;
                }
                if (count < 20) continue;
                slots.Add((R, Y));
            }
        }
        if (slots.Count == 0) { _gridPending = false; return 0; }

        // OCR только ячеек с новым содержимым (кэш по хэшу пикселей)
        var crops = new List<(int R, int Y, Rectangle Rect, ulong Hash)>();
        foreach (var (R, Y) in slots)
        {
            var x0 = Math.Max(0, R - (int)cell + (int)(6 * k));
            var rect = Rectangle.FromLTRB(x0, Math.Max(0, Y - (int)(3 * k)), Math.Min(W, R + (int)(3 * k)), Math.Min(H, Y + (int)(12 * k)));
            ulong h = 1469598103934665603UL;
            // грубый хэш (яркость/32) по строкам самой надписи — устойчив к шуму и к меткам оверлея ниже
            for (var y = rect.Top; y < Math.Min(rect.Bottom, Y + (int)(9 * k)); y++)
            for (var x = rect.Left; x < rect.Right; x++)
                h = (h ^ (ulong)(Lum(x, y) >> 5)) * 1099511628211UL;
            crops.Add((R, Y, rect, h));
        }
        List<(Rectangle Rect, ulong Hash, Bitmap Bmp)> todo;
        lock (_slotCache)
        {
            if (_slotCache.Count > 6000) _slotCache.Clear();
            var pending = crops.Where(c => !_slotCache.ContainsKey(c.Hash)).DistinctBy(c => c.Hash).ToList();
            _gridPending = pending.Count > GridOcrPerCycle; // остальное — в следующих циклах, чтобы метки появлялись быстро
            todo = pending.Take(GridOcrPerCycle)
                .Select(c => (c.Rect, c.Hash, source.Clone(c.Rect, PixelFormat.Format24bppRgb))).ToList();
        }
        _lastSlotOcr = todo.Count;
        if (todo.Count > 0 && BandPool is not null)
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = BandPool.Size }, t =>
            {
                try
                {
                    var text = BandPool.ReadLine(t.Bmp, 3.0);
                    lock (_slotCache) _slotCache[t.Hash] = text;
                }
                finally { t.Bmp.Dispose(); }
            });

        var added = 0;
        foreach (var c in crops)
        {
            string? text;
            lock (_slotCache) _slotCache.TryGetValue(c.Hash, out text);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (GridDebug) AppLog.Info($"[Grid] ({c.R},{c.Y}) h={c.Hash:x} '{text}'");
            var toks = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => TextNormalizer.Normalize(t).Length > 0).ToList();
            var tries = new List<string>();
            if (toks.Count >= 2) tries.Add($"{toks[^2]} {toks[^1]}");
            if (toks.Count >= 1) tries.Add(toks[^1]);
            foreach (var t in tries)
            {
                var key = TextNormalizer.Normalize(t);
                if (key.Length < 3 || t.Split(' ').Any(ItemDatabase.IsStopWord)) continue;
                if (key.Length == 3 && toks.Count > 1) continue; // короткий код среди мусора — скорее текстура
                var tm = _database.MatchTile(t, 90);
                if (tm is null || tm.Kind == "?") continue;
                var w = (int)(40 * k);
                matches.Add(new MatchedItem(tm.Items[0], c.R - w, c.Y, w, (int)(10 * k), 90, MatchKind.ShortName, t)
                    { Variants = tm.Items.Count > 1 ? tm.Items : null, Source = "grid:" + tm.Kind });
                added++;
                break;
            }
        }
        return added;
    }

    internal List<MatchedItem> Match(IReadOnlyList<OcrLine> lines)
    {
        var result = new List<MatchedItem>();
        foreach (var line in lines)
        {
            var full = _database.MatchFullName(line.Text, _settings.MinFullNameScore);
            if (full is not null)
            {
                result.Add(new MatchedItem(full.Item, line.X, line.Y, line.Width, line.Height, full.Score, MatchKind.FullName, line.Text));
                continue;
            }

            var words = line.Words;
            for (var i = 0; i < words.Count; i++)
            {
                // сначала пара соседних слов ("Мед. Док"), потом одно слово
                if (i + 1 < words.Count && AreAdjacent(words[i], words[i + 1]))
                {
                    var pair = _database.MatchShortName($"{words[i].Text} {words[i + 1].Text}",
                        Math.Min(words[i].Confidence, words[i + 1].Confidence));
                    if (pair is not null)
                    {
                        var x2 = words[i + 1].X + words[i + 1].Width;
                        var y1 = Math.Min(words[i].Y, words[i + 1].Y);
                        var y2 = Math.Max(words[i].Y + words[i].Height, words[i + 1].Y + words[i + 1].Height);
                        result.Add(new MatchedItem(pair.Item, words[i].X, y1, x2 - words[i].X, y2 - y1, pair.Score,
                            MatchKind.ShortName, $"{words[i].Text} {words[i + 1].Text}"));
                        i++;
                        continue;
                    }
                }

                var single = _database.MatchShortName(words[i].Text, words[i].Confidence);
                if (single is not null)
                    result.Add(new MatchedItem(single.Item, words[i].X, words[i].Y, words[i].Width, words[i].Height,
                        single.Score, MatchKind.ShortName, words[i].Text));
            }
        }
        return result;
    }

    private static Rectangle? TileUnderCursor(MatchedItem m, Point cursor, double cell)
    {
        var right = m.X + m.Width + cell * 0.12;
        var top = m.Y - cell * 0.2;
        foreach (var (w, h) in new[] { (m.Item.Width, m.Item.Height), (m.Item.Height, m.Item.Width) })
        {
            var rect = new Rectangle((int)(right - w * cell), (int)top, (int)(w * cell), (int)(h * cell));
            if (rect.Contains(cursor))
                return rect;
        }
        return null;
    }

    private static bool AreAdjacent(OcrWord a, OcrWord b)
        => b.X - (a.X + a.Width) < Math.Max(a.Height, b.Height) * 1.5 && Math.Abs(a.Y - b.Y) < Math.Max(a.Height, b.Height);

    private static double Distance(MatchedItem m, Point p)
    {
        var dx = Math.Max(Math.Max(m.X - p.X, 0), p.X - (m.X + m.Width));
        var dy = Math.Max(Math.Max(m.Y - p.Y, 0), p.Y - (m.Y + m.Height));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void ApplyMask(Bitmap bmp, Point origin)
    {
        var rects = MaskProvider?.Invoke();
        if (rects is null || rects.Length == 0)
            return;
        using var g = Graphics.FromImage(bmp);
        foreach (var r in rects)
        {
            var local = r with { X = r.X - origin.X - 3, Y = r.Y - origin.Y - 3, Width = r.Width + 6, Height = r.Height + 6 };
            local.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (!local.IsEmpty)
                g.FillRectangle(Brushes.Black, local);
        }
    }

    /// <summary>Выборка яркости (каждый 3-й пиксель по обеим осям) для сравнения кадров.</summary>
    private static byte[] Sample(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            var result = new List<byte>(bmp.Width * bmp.Height / 9 + 1);
            for (var y = 0; y < bmp.Height; y += 3)
            for (var x = 0; x < bmp.Width; x += 3)
            {
                var i = y * stride + x * 3;
                result.Add((byte)((buffer[i] * 29 + buffer[i + 1] * 150 + buffer[i + 2] * 77) >> 8));
            }
            return result.ToArray();
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>Заметное изменение: ≥0.4% точек выборки изменили яркость больше чем на 40.</summary>
    private static bool ChangedEnough(byte[]? previous, byte[] current)
    {
        if (previous is null || previous.Length != current.Length)
            return true;
        var threshold = Math.Max(3, current.Length / 250);
        var changed = 0;
        for (var i = 0; i < current.Length; i++)
            if (previous[i] != 0 && current[i] != 0 && Math.Abs(previous[i] - current[i]) > 40 && ++changed >= threshold)
                return true;
        return false;
    }
}
