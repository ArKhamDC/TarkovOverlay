using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TarkovOverlay.Services;

/// <summary>Захват экрана. Все координаты — физические пиксели основного монитора.</summary>
public sealed class ScreenCaptureService
{
    public Rectangle PrimaryBounds => new(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));

    public Bitmap Capture(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public Bitmap CapturePrimaryScreen() => Capture(PrimaryBounds);

    public static Point GetCursorPosition()
        => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(-1, -1);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
