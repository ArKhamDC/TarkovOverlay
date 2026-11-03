using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TarkovOverlay.Models;

namespace TarkovOverlay.Windows;

/// <summary>
/// Прозрачное окно поверх игры: не перехватывает мышь/фокус, скрыто из захвата экрана
/// (WDA_EXCLUDEFROMCAPTURE), принимает глобальные горячие клавиши.
/// Метки приходят в физических пикселях и переводятся в единицы WPF с учётом DPI.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private IntPtr _handle;
    private readonly List<int> _hotkeys = [];

    public event Action<int>? HotkeyPressed;

    public OverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => PlaceGps();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(_handle, GwlExstyle);
        SetWindowLong(_handle, GwlExstyle, exStyle | WsExTransparent | WsExToolwindow | WsExNoActivate);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    /// <summary>Скрыть окно из захвата экрана (Windows 10 2004+).</summary>
    public bool ExcludeFromCapture(out int error)
    {
        error = 0;
        if (_handle == IntPtr.Zero)
            return false;
        if (SetWindowDisplayAffinity(_handle, WdaExcludeFromCapture))
            return true;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    private volatile System.Drawing.Rectangle[] _markerRects = [];
    private System.Drawing.Rectangle[] _gpsRects = [];
    private Border? _gpsPanel;
    private string _gpsKey = "";

    /// <summary>
    /// Прямоугольники видимых меток в физических пикселях. Используются, чтобы OCR не читал
    /// собственные метки, если Windows не дала скрыть окно из захвата (у прозрачных WPF-окон так бывает).
    /// </summary>
    public System.Drawing.Rectangle[] MarkerRects => _gpsRects.Length == 0 ? _markerRects : CombineRects(_markerRects, _gpsRects);

    public bool RegisterHotkey(int id, uint modifiers, uint virtualKey)
    {
        if (_handle == IntPtr.Zero || !RegisterHotKey(_handle, id, modifiers | ModNoRepeat, virtualKey))
            return false;
        _hotkeys.Add(id);
        return true;
    }

    public void UnregisterHotkeys()
    {
        foreach (var id in _hotkeys)
            UnregisterHotKey(_handle, id);
        _hotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void SetMarkers(IReadOnlyList<PriceMarker> markers)
    {
        MarkerCanvas.Children.Clear();
        var dpi = VisualTreeHelper.GetDpi(this);
        var rects = new List<System.Drawing.Rectangle>(markers.Count);

        foreach (var marker in markers)
        {
            var border = CreateMarker(marker, dpi.DpiScaleX);
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = border.DesiredSize;

            var x = marker.X / dpi.DpiScaleX;
            var y = marker.Y / dpi.DpiScaleY;
            if (marker.Style is MarkerStyle.Compact or MarkerStyle.Panel)
                x -= size.Width;
            if (marker.Style == MarkerStyle.Tooltip && marker.MaxW > 0)
            {
                border.MinWidth = marker.MaxW / dpi.DpiScaleX;
                border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                size = border.DesiredSize;
            } // выравнивание по правому краю

            // не вылезать за экран
            x = Math.Clamp(x, 0, Math.Max(0, ActualWidth - size.Width));
            y = Math.Clamp(y, 0, Math.Max(0, ActualHeight - size.Height));

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            MarkerCanvas.Children.Add(border);
            rects.Add(new System.Drawing.Rectangle(
                (int)(x * dpi.DpiScaleX) - 2, (int)(y * dpi.DpiScaleY) - 2,
                (int)Math.Ceiling(size.Width * dpi.DpiScaleX) + 4, (int)Math.Ceiling(size.Height * dpi.DpiScaleY) + 4));
        }
        _markerRects = rects.ToArray();
        if (_gpsPanel is not null)
        {
            MarkerCanvas.Children.Add(_gpsPanel);
            PlaceGps();
        }
    }

    /// <summary>Arrow toward the nearest extract. Independent of the movable map window.</summary>
    public void SetGps(GpsHud? hud)
    {
        var key = hud is null ? "" : $"{hud.HasBearing}|{hud.ArrowDegrees:0.0}|{hud.Meters}|{hud.Primary}|{hud.Secondary}";
        if (key == _gpsKey) return;
        _gpsKey = key;
        if (_gpsPanel is not null)
            MarkerCanvas.Children.Remove(_gpsPanel);
        _gpsPanel = hud is null ? null : BuildGps(hud);
        _gpsRects = [];
        if (_gpsPanel is null) return;
        MarkerCanvas.Children.Add(_gpsPanel);
        PlaceGps();
    }

    private void PlaceGps()
    {
        if (_gpsPanel is null) return;
        _gpsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _gpsPanel.DesiredSize;
        var x = Math.Max(0, ActualWidth - size.Width - 16);
        var y = 16.0;
        Canvas.SetLeft(_gpsPanel, x);
        Canvas.SetTop(_gpsPanel, y);
        var dpi = VisualTreeHelper.GetDpi(this);
        _gpsRects =
        [
            new System.Drawing.Rectangle(
                (int)(x * dpi.DpiScaleX) - 2,
                (int)(y * dpi.DpiScaleY) - 2,
                (int)Math.Ceiling(size.Width * dpi.DpiScaleX) + 4,
                (int)Math.Ceiling(size.Height * dpi.DpiScaleY) + 4)
        ];
    }

    private static Border BuildGps(GpsHud hud)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (hud.HasBearing)
        {
            var arrow = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new(12, 0), new(24, 22), new(12, 16), new(0, 22) },
                Fill = Accent,
                Width = 24,
                Height = 22,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(hud.ArrowDegrees)
            };
            stack.Children.Add(arrow);
            stack.Children.Add(new TextBlock
            {
                Text = hud.Primary,
                Foreground = Brushes.White,
                FontFamily = Font,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (!string.IsNullOrWhiteSpace(hud.Secondary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = hud.Secondary,
                    Foreground = Secondary,
                    FontFamily = Font,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 168,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = hud.Primary,
                Foreground = StatusAccent,
                FontFamily = Font,
                FontSize = 12
            });
        }

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = hud.HasBearing ? Accent : StatusAccent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = stack,
            IsHitTestVisible = false
        };
    }

    private static System.Drawing.Rectangle[] CombineRects(System.Drawing.Rectangle[] a, System.Drawing.Rectangle[] b)
    {
        var all = new System.Drawing.Rectangle[a.Length + b.Length];
        Array.Copy(a, all, a.Length);
        Array.Copy(b, 0, all, a.Length, b.Length);
        return all;
    }

    private static readonly Brush PanelBackground = Freeze(new SolidColorBrush(Color.FromArgb(225, 16, 19, 22)));
    private static readonly Brush CompactBackground = Freeze(new SolidColorBrush(Color.FromArgb(205, 16, 19, 22)));
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromArgb(255, 90, 200, 120)));
    private static readonly Brush StatusAccent = Freeze(new SolidColorBrush(Color.FromArgb(255, 230, 180, 70)));
    private static readonly Brush Secondary = Freeze(new SolidColorBrush(Color.FromArgb(255, 205, 210, 214)));
    private static readonly FontFamily Font = new("Segoe UI");

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private static Border CreateMarker(PriceMarker marker, double dpiScale)
    {
        var stack = new StackPanel();
        // метка не выходит за прямоугольник своей плитки: уменьшаем шрифт и режем лишние строки
        var lines = marker.Lines;
        double fs = 13;
        if (marker.Style == MarkerStyle.Compact && marker.MaxW > 0)
        {
            var maxW = marker.MaxW / dpiScale - 8;
            var maxH = marker.MaxH / dpiScale - 2;
            while (fs > 9 && lines.Any(l => l.Length * fs * 0.56 > maxW)) fs -= 1;
            var fit = Math.Max(1, (int)(maxH / (fs + 1)));
            if (lines.Count > fit) lines = lines.Take(fit).ToList();
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var first = i == 0;
            stack.Children.Add(new TextBlock
            {
                Text = lines[i],
                Foreground = first || marker.Style is MarkerStyle.Compact or MarkerStyle.Tooltip ? Brushes.White : Secondary,
                FontFamily = Font,
                FontSize = marker.Style switch { MarkerStyle.Panel => first ? 14 : 13, MarkerStyle.Compact => fs, MarkerStyle.Tooltip => 14, _ => 12 },
                FontWeight = first && marker.Style != MarkerStyle.Tooltip ? FontWeights.SemiBold : FontWeights.Normal,
                LineHeight = marker.Style == MarkerStyle.Compact ? fs + 1 : double.NaN,
                LineStackingStrategy = marker.Style == MarkerStyle.Compact ? LineStackingStrategy.BlockLineHeight : LineStackingStrategy.MaxHeight,
                Margin = new Thickness(0, first || marker.Style == MarkerStyle.Compact ? 0 : 1, 0, 0)
            });
        }

        return new Border
        {
            Background = marker.Style is MarkerStyle.Panel or MarkerStyle.Tooltip ? PanelBackground : CompactBackground,
            BorderBrush = marker.Style == MarkerStyle.Status ? StatusAccent : Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = marker.Style == MarkerStyle.Panel ? new Thickness(8, 5, 8, 6) : marker.Style == MarkerStyle.Tooltip ? new Thickness(8, 3, 8, 4) : new Thickness(3, 0, 3, 1),
            Child = stack
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
