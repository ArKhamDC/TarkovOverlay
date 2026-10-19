using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TarkovOverlay.Models;
using Tesseract;

namespace TarkovOverlay.Services;

/// <summary>
/// OCR одним проходом (движок "rus+eng"). Картинка переводится в оттенки серого,
/// инвертируется (светлый текст игры → тёмный на светлом) и увеличивается.
/// Не потокобезопасен сам по себе — вызовы сериализуются блокировкой.
/// </summary>
public sealed class OcrService : IDisposable
{
    private readonly string _tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private readonly object _lock = new();
    private TesseractEngine? _engine;

    public bool IsReady => _engine is not null;
    public string Languages { get; private set; } = "";

    public void Initialize()
    {
        if (!Directory.Exists(_tessDataPath))
            throw new DirectoryNotFoundException($"Папка tessdata не найдена: {_tessDataPath}. См. README.md");

        var langs = new[] { "rus", "eng" }
            .Where(l => File.Exists(Path.Combine(_tessDataPath, l + ".traineddata")))
            .ToList();
        if (langs.Count == 0)
            throw new FileNotFoundException("В tessdata нет rus/eng.traineddata. Запустите scripts\\download-tessdata.ps1");

        Languages = string.Join("+", langs);
        _engine = new TesseractEngine(_tessDataPath, Languages, EngineMode.LstmOnly);
        AppLog.Info($"[OCR] Tesseract готов ({Languages})");
    }

    /// <summary>Распознаёт строки. Координаты результата — в системе экрана (с учётом offset и scale).</summary>
    public IReadOnlyList<OcrLine> Recognize(Bitmap bitmap, double scale, int offsetX, int offsetY)
    {
        if (_engine is null)
            return [];

        using var prepared = Preprocess(bitmap, scale);
        var bytes = ToBmpBytes(prepared);

        lock (_lock)
        {
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = _engine.Process(pix, PageSegMode.SparseText);
            using var it = page.GetIterator();

            var lines = new List<OcrLine>();
            var current = new List<OcrWord>();
            it.Begin();
            do
            {
                if (it.IsAtBeginningOf(PageIteratorLevel.TextLine) && current.Count > 0)
                {
                    lines.Add(ToLine(current));
                    current = [];
                }

                if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
                    continue;
                var text = it.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                current.Add(new OcrWord(
                    text,
                    offsetX + (int)(r.X1 / scale),
                    offsetY + (int)(r.Y1 / scale),
                    Math.Max(1, (int)(r.Width / scale)),
                    Math.Max(1, (int)(r.Height / scale)),
                    it.GetConfidence(PageIteratorLevel.Word)));
            } while (it.Next(PageIteratorLevel.Word));

            if (current.Count > 0)
                lines.Add(ToLine(current));
            return lines;
        }
    }

    internal static OcrLine ToLine(List<OcrWord> words)
    {
        var x1 = words.Min(w => w.X);
        var y1 = words.Min(w => w.Y);
        var x2 = words.Max(w => w.X + w.Width);
        var y2 = words.Max(w => w.Y + w.Height);
        return new OcrLine(string.Join(" ", words.Select(w => w.Text)), x1, y1, x2 - x1, y2 - y1, words);
    }

    internal static Bitmap Preprocess(Bitmap source, double scale)
    {
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));
        var result = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        // серый + инверсия одной матрицей
        var matrix = new ColorMatrix(
        [
            [-0.299f, -0.299f, -0.299f, 0, 0],
            [-0.587f, -0.587f, -0.587f, 0, 0],
            [-0.114f, -0.114f, -0.114f, 0, 0],
            [0, 0, 0, 1, 0],
            [1, 1, 1, 0, 1]
        ]);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);

        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    internal static byte[] ToBmpBytes(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        return ms.ToArray();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
