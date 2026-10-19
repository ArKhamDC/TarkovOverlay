using System.Collections.Concurrent;
using System.Drawing;
using TarkovOverlay.Models;
using Tesseract;

namespace TarkovOverlay.Services;

/// <summary>
/// Пул движков Tesseract для параллельного OCR (один движок = один поток).
/// Для полосового прохода по экрану используется tessdata_fast (в ~2.5 раза быстрее best).
/// </summary>
public sealed class OcrEnginePool : IDisposable
{
    private readonly ConcurrentBag<TesseractEngine> _engines = new();
    private readonly SemaphoreSlim _semaphore;
    private bool _disposed;

    public int Size { get; }
    public static string? DebugDir;
    private static int _dbgN;
    public string Name { get; }

    public OcrEnginePool(string name, string dataPath, int size)
    {
        if (!Directory.Exists(dataPath))
            throw new DirectoryNotFoundException($"Нет папки {dataPath}. Запустите scripts\\download-tessdata.ps1");
        var langs = string.Join("+", new[] { "rus", "eng" }.Where(l => File.Exists(Path.Combine(dataPath, l + ".traineddata"))));
        if (langs.Length == 0)
            throw new FileNotFoundException($"В {dataPath} нет rus/eng.traineddata");

        Name = name;
        Size = Math.Max(1, size);
        for (var i = 0; i < Size; i++)
            _engines.Add(new TesseractEngine(dataPath, langs, EngineMode.LstmOnly));
        _semaphore = new SemaphoreSlim(Size, Size);
        AppLog.Info($"[OCR] Пул '{name}': {Size} движков ({langs}, {Path.GetFileName(dataPath)})");
    }

    private T Use<T>(Func<TesseractEngine, T> action)
    {
        _semaphore.Wait();
        if (!_engines.TryTake(out var engine))
        {
            _semaphore.Release();
            throw new InvalidOperationException("OCR pool is empty");
        }
        try
        {
            return action(engine);
        }
        finally
        {
            _engines.Add(engine);
            _semaphore.Release();
        }
    }

    /// <summary>Слова (разреженный текст). Координаты — экранные.</summary>
    public List<OcrWord> RecognizeWords(Bitmap crop, double scale, int offsetX, int offsetY)
    {
        using var prepared = OcrService.Preprocess(crop, scale);
        var bytes = OcrService.ToBmpBytes(prepared);
        return Use(engine =>
        {
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix, PageSegMode.SparseText);
            using var it = page.GetIterator();
            var words = new List<OcrWord>();
            it.Begin();
            do
            {
                if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r))
                    continue;
                var text = it.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;
                words.Add(new OcrWord(text,
                    offsetX + (int)(r.X1 / scale), offsetY + (int)(r.Y1 / scale),
                    Math.Max(1, (int)(r.Width / scale)), Math.Max(1, (int)(r.Height / scale)),
                    it.GetConfidence(PageIteratorLevel.Word)));
            } while (it.Next(PageIteratorLevel.Word));
            return words;
        });
    }

    /// <summary>Одна строка текста (PSM 7) — для надписи одной плитки.</summary>
    public string ReadLine(Bitmap crop, double scale)
    {
        using var scaled = OcrService.Preprocess(crop, scale);
        // белое поле вокруг надписи — Tesseract заметно лучше читает строку не вплотную к краю
        const int pad = 12;
        using var prepared = new Bitmap(scaled.Width + 2 * pad, scaled.Height + 2 * pad, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(prepared))
        {
            g.Clear(Color.White);
            g.DrawImageUnscaled(scaled, pad, pad);
        }
        var bytes = OcrService.ToBmpBytes(prepared);
        if (DebugDir is not null)
            prepared.Save(Path.Combine(DebugDir, $"{Interlocked.Increment(ref _dbgN):0000}.png"), System.Drawing.Imaging.ImageFormat.Png);
        return Use(engine =>
        {
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            return (page.GetText() ?? "").Trim();
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        for (var i = 0; i < Size; i++)
            _semaphore.Wait(TimeSpan.FromSeconds(5));
        while (_engines.TryTake(out var e))
            e.Dispose();
    }
}
