using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using TarkovOverlay.Services;
using TarkovOverlay.Utils;
using TarkovOverlay.Windows;

namespace TarkovOverlay;

public partial class App : Application
{
    private OverlayController? _controller;
    private Mutex? _singleInstance;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        AppLog.Init();
        AppLog.Info("=== TarkovOverlay стартует ===");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Необработанное исключение", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Необработанное исключение в задаче", args.Exception);
            args.SetObserved();
        };

        try
        {
            // Тестовый режим: TarkovOverlay.exe --scan-image screen.png [--cursor X,Y]
            var imageIndex = Array.IndexOf(e.Args, "--scan-image");
            if (imageIndex >= 0 && imageIndex + 1 < e.Args.Length)
            {
                await RunImageTestAsync(e.Args[imageIndex + 1], e.Args);
                Shutdown();
                return;
            }

            _singleInstance = new Mutex(true, "TarkovOverlay.SingleInstance", out var isNew);
            if (!isNew)
            {
                AppLog.Info("Оверлей уже запущен — выходим");
                Shutdown();
                return;
            }

            var overlay = new OverlayWindow();
            MainWindow = overlay;
            overlay.Show();

            _controller = new OverlayController(overlay);
            await _controller.StartAsync();

            // для проверки: сразу один проход по всему экрану (как Ctrl+Alt+S)
            if (e.Args.Contains("--full-scan-on-start"))
                _controller.TriggerFullScan();
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка запуска", ex);
            Shutdown();
        }
    }

    private async Task RunImageTestAsync(string path, string[] args)
    {
        AppLog.Info($"[Test] Сканирую картинку {path}");
        var overlay = new OverlayWindow(); // не показываем
        var controller = new OverlayController(overlay);
        _controller = controller;
        await controller.InitializeCoreAsync();
        if (!controller.Database.IsLoaded)
            await controller.Database.TryUpdateAsync(controller.Api, controller.Settings, CancellationToken.None);

        using var frame = new Bitmap(path);
        var cursorIndex = Array.IndexOf(args, "--cursor");
        if (cursorIndex >= 0 && cursorIndex + 1 < args.Length)
        {
            var xy = args[cursorIndex + 1].Split(',');
            var cursor = new System.Drawing.Point(int.Parse(xy[0]), int.Parse(xy[1]));
            if (args.Contains("--tooltip"))
            {
                var tt = new TooltipScanner(controller.Database, controller.Ocr, new ScreenCaptureService()).Scan(frame, cursor);
                AppLog.Info($"[Test] Подсказка: {(tt is null ? "не найдена" : $"{tt.Box} \"{tt.Text}\" → {tt.Item?.Name ?? "—"} | {string.Join(" | ", tt.Item is null ? [] : PriceFormat.TooltipLines(tt.Item))} ({tt.ElapsedMs} мс)")}");
                return;
            }
            var r = controller.Scanner.ScanAroundCursor(frame, cursor);
            AppLog.Info($"[Test] Курсор {cursor}: {(r?.Match is { } m ? string.Join(" | ", PriceFormat.PanelLines(m.Item)) : "ничего")} ({r?.ElapsedMs} мс)");
            return;
        }

        ItemScanner.GridDebug = args.Contains("--grid-debug");
        if (ItemScanner.GridDebug) { OcrEnginePool.DebugDir = Path.Combine(Path.GetTempPath(), "gridprep"); Directory.CreateDirectory(OcrEnginePool.DebugDir); }
        controller.Scanner.GridOcrPerCycle = 100000;
        var t1 = controller.Scanner.ScanBands(frame, verbose: true);
        var t2 = controller.Scanner.ScanBands(frame);
        AppLog.Info($"[Test] автоскан: первый проход {t1.ElapsedMs} мс ({t1.BandsOcr}/{t1.BandsTotal} полос), повтор без изменений {t2.ElapsedMs} мс");
        var result = t1;
        var markers = OverlayController.BuildCompactMarkers(result.Matches);

        // картинка с метками для проверки глазами
        using (var g = Graphics.FromImage(frame))
        using (var font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
        using (var bg = new SolidBrush(Color.FromArgb(215, 16, 19, 22)))
        using (var pen = new Pen(Color.FromArgb(255, 90, 200, 120)))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            foreach (var m in result.Matches)
                g.DrawRectangle(Pens.Yellow, m.X, m.Y, m.Width, m.Height);
            foreach (var mk in markers)
            {
                var text = string.Join("\n", mk.Lines);
                var size = g.MeasureString(text, font);
                var x = (float)mk.X - size.Width - 4;
                g.FillRectangle(bg, x, (float)mk.Y, size.Width + 4, size.Height);
                g.DrawRectangle(pen, x, (float)mk.Y, size.Width + 4, size.Height);
                g.DrawString(text, font, Brushes.White, x + 2, (float)mk.Y);
            }
        }
        var outPath = Path.ChangeExtension(path, ".annotated.png");
        frame.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        AppLog.Info($"[Test] Готово: {markers.Count} меток, картинка {outPath}");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Необработанное исключение UI", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        AppLog.Info("=== TarkovOverlay завершён ===");
        base.OnExit(e);
    }
}
