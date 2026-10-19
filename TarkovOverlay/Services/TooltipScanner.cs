using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TarkovOverlay.Models;

namespace TarkovOverlay.Services;

public sealed record TooltipResult(Rectangle Box, ItemPrice? Item, string Text, long ElapsedMs, bool Cached);

/// <summary>
/// Подсказка игры при наведении: тёмный прямоугольник со светлым полным названием рядом с курсором.
/// Находим его, распознаём только его (модель best), сопоставляем полное название, кэш по содержимому.
/// </summary>
public sealed class TooltipScanner
{
    private ItemDatabase _db;
    public ItemDatabase Db { get => _db; set { lock (_cache) { _db = value; _cache.Clear(); } } }
    private readonly OcrService _ocr;
    private readonly ScreenCaptureService _capture;
    private readonly Dictionary<ulong, (ItemPrice? Item, string Text)> _cache = new();
    public Func<Rectangle[]>? MaskProvider { get; set; }

    public TooltipScanner(ItemDatabase db, OcrService ocr, ScreenCaptureService capture)
    {
        _db = db; _ocr = ocr; _capture = capture;
    }

    public TooltipResult? Scan(Bitmap? frame = null, Point? cursorOverride = null)
    {
        var sw = Stopwatch.StartNew();
        var screen = frame is null ? _capture.PrimaryBounds : new Rectangle(0, 0, frame.Width, frame.Height);
        var cur = cursorOverride ?? ScreenCaptureService.GetCursorPosition();
        if (!screen.Contains(cur)) return null;
        var k = screen.Height / 1080.0;
        var region = Rectangle.FromLTRB(cur.X - (int)(300 * k), cur.Y - (int)(150 * k), cur.X + (int)(400 * k), cur.Y + (int)(60 * k));
        region.Intersect(screen);
        if (region.Width < 50 || region.Height < 30) return null;

        using var bmp = frame is null ? _capture.Capture(region) : frame.Clone(region, PixelFormat.Format24bppRgb);
        var rects = MaskProvider?.Invoke();
        if (rects is { Length: > 0 })
        {
            using var g = Graphics.FromImage(bmp);
            foreach (var r in rects)
            {
                var local = new Rectangle(r.X - region.X - 3, r.Y - region.Y - 3, r.Width + 6, r.Height + 6);
                g.FillRectangle(Brushes.White, local); // свою панель не принимать за подсказку
            }
        }

        int W = bmp.Width, H = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var buf = new byte[data.Stride * H];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        var stride = data.Stride;
        bmp.UnlockBits(data);

        // тёмные серые пиксели фона подсказки
        var dark = new bool[W * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var i = y * stride + x * 3;
            int b = buf[i], gg = buf[i + 1], r = buf[i + 2];
            int mx = Math.Max(r, Math.Max(gg, b)), mn = Math.Min(r, Math.Min(gg, b));
            dark[y * W + x] = mx < 30 && mx - mn < 8;
        }

        // связные области; берём подходящий прямоугольник ближе всего к курсору
        var seen = new bool[W * H];
        var stack = new Stack<int>();
        Rectangle? best = null; double bestDist = double.MaxValue;
        int cx = cur.X - region.X, cy = cur.Y - region.Y;
        for (var start = 0; start < W * H; start++)
        {
            if (!dark[start] || seen[start]) continue;
            int minX = W, minY = H, maxX = 0, maxY = 0, area = 0;
            stack.Push(start); seen[start] = true;
            while (stack.Count > 0)
            {
                var p = stack.Pop(); int px = p % W, py = p / W; area++;
                if (px < minX) minX = px; if (px > maxX) maxX = px; if (py < minY) minY = py; if (py > maxY) maxY = py;
                foreach (var q in new[] { p - 1, p + 1, p - W, p + W })
                {
                    if (q < 0 || q >= W * H || seen[q] || !dark[q]) continue;
                    if ((q == p - 1 && px == 0) || (q == p + 1 && px == W - 1)) continue;
                    seen[q] = true; stack.Push(q);
                }
            }
            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            if (bh < 16 * k || bh > 120 * k || bw < 30 * k || bw > 700 * k || area < 0.5 * bw * bh) continue;
            var bright = 0;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                if (buf[y * stride + x * 3 + 1] > 110) bright++;
            if (bright < 15) continue;
            var dx = Math.Max(0, Math.Max(minX - cx, cx - maxX)); var dy = Math.Max(0, Math.Max(minY - cy, cy - maxY));
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 120 * k || dist >= bestDist) continue;
            bestDist = dist; best = new Rectangle(minX, minY, bw, bh);
        }
        if (best is not { } box) return null;

        ulong h = 1469598103934665603UL;
        // хэш по средней части (низ подсказки может закрашиваться маской нашей панели)
        for (var y = box.Top + 3; y < box.Bottom - 8; y++)
        for (var x = box.Left + 2; x < box.Right - 2; x++)
            h = (h ^ (ulong)(buf[y * stride + x * 3 + 1] >> 4)) * 1099511628211UL;
        var screenBox = new Rectangle(box.X + region.X, box.Y + region.Y, box.Width, box.Height);
        lock (_cache)
            if (_cache.TryGetValue(h, out var c))
                return new TooltipResult(screenBox, c.Item, c.Text, sw.ElapsedMilliseconds, true);

        using var crop = bmp.Clone(box, PixelFormat.Format24bppRgb);
        var lines = _ocr.Recognize(crop, 2.0, 0, 0);
        var text = string.Join(" ", lines.Select(l => l.Text)).Trim();
        var item = _db.MatchTooltip(text);
        lock (_cache)
        {
            if (_cache.Count > 2000) _cache.Clear();
            _cache[h] = (item, text);
        }
        AppLog.Info($"[Tooltip] \"{text}\" → {item?.Name ?? "—"} ({sw.ElapsedMilliseconds} мс)");
        return new TooltipResult(screenBox, item, text, sw.ElapsedMilliseconds, false);
    }
}
