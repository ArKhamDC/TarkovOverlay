using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovOverlay.Models;
using TarkovOverlay.Services;
using TarkovOverlay.Utils;

namespace TarkovOverlay.Windows;

/// <summary>
/// Separate topmost map window. Drag the title to move, drag the edges to resize.
/// It does not take keyboard focus from the game. The GPS arrow stays on the price overlay.
/// </summary>
public partial class MapWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const double MinW = 220;
    private const double MinH = 180;

    private IntPtr _handle;
    private bool _exit;
    private string? _gesture;
    private Point _start;
    private double _ol, _ot, _ow, _oh;
    private static readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private MapView? _view;

    public event Action<double, double, double, double>? BoundsCommitted;
    public event Action? UserHid;

    public MapWindow()
    {
        InitializeComponent();
        BuildEdges();
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
        new WindowInteropHelper(this).EnsureHandle();
    }

    public void ApplyPlacement(double left, double top, double width, double height, double opacity)
    {
        Width = Math.Clamp(width, MinW, 2400);
        Height = Math.Clamp(height, MinH, 1600);
        Left = left;
        Top = top;
        Opacity = Math.Clamp(opacity, 0.3, 1);
        EnsureOnScreen();
    }

    public void SetOpen(bool open)
    {
        if (open)
        {
            if (!IsVisible) Show();
            Topmost = true;
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    public void Render(MapView? view)
    {
        _view = view;
        TitleText.Text = string.IsNullOrWhiteSpace(view?.Title) ? L.T("Карта", "Map") : view!.Title;
        Draw();
    }

    public bool ExcludeFromCapture(out int error)
    {
        error = 0;
        if (_handle == IntPtr.Zero) return false;
        if (SetWindowDisplayAffinity(_handle, WdaExcludeFromCapture)) return true;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    public void PrepareForExit() => _exit = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(_handle, GwlExstyle);
        SetWindowLong(_handle, GwlExstyle, ex | WsExToolwindow | WsExNoActivate);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exit)
        {
            e.Cancel = true;
            Hide();
            UserHid?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }
        return IntPtr.Zero;
    }

    private void BuildEdges()
    {
        AddEdge("N", horizontal: true, end: false, Cursors.SizeNS);
        AddEdge("S", horizontal: true, end: true, Cursors.SizeNS);
        AddEdge("W", horizontal: false, end: false, Cursors.SizeWE);
        AddEdge("E", horizontal: false, end: true, Cursors.SizeWE);
        AddCorner("NW", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
        AddCorner("NE", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
        AddCorner("SW", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
        AddCorner("SE", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
    }

    private void AddEdge(string tag, bool horizontal, bool end, Cursor cursor)
    {
        var r = new Rectangle { Tag = tag, Fill = Brushes.Transparent, Cursor = cursor };
        if (horizontal)
        {
            r.Height = 6;
            r.VerticalAlignment = end ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        }
        else
        {
            r.Width = 6;
            r.HorizontalAlignment = end ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        r.MouseLeftButtonDown += (_, e) => BeginGesture(tag, e);
        Edges.Children.Add(r);
    }

    private void AddCorner(string tag, HorizontalAlignment h, VerticalAlignment v, Cursor cursor)
    {
        var r = new Rectangle
        {
            Tag = tag,
            Width = 14,
            Height = 14,
            Fill = Brushes.Transparent,
            Cursor = cursor,
            HorizontalAlignment = h,
            VerticalAlignment = v
        };
        r.MouseLeftButtonDown += (_, e) => BeginGesture(tag, e);
        Edges.Children.Add(r);
    }

    private void OnTitleDown(object sender, MouseButtonEventArgs e) => BeginGesture("move", e);

    private void BeginGesture(string gesture, MouseButtonEventArgs e)
    {
        _gesture = gesture;
        _start = CursorDip();
        _ol = Left;
        _ot = Top;
        _ow = Width;
        _oh = Height;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnGestureMove(object sender, MouseEventArgs e)
    {
        if (_gesture is null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = CursorDip();
        ApplyGesture(now.X - _start.X, now.Y - _start.Y);
    }

    private void OnGestureUp(object sender, MouseButtonEventArgs e) => FinishGesture();

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture is not null) FinishGesture();
    }

    private void FinishGesture()
    {
        if (_gesture is null) return;
        _gesture = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        BoundsCommitted?.Invoke(Left, Top, Width, Height);
    }

    private void ApplyGesture(double dx, double dy)
    {
        if (_gesture == "move")
        {
            Left = _ol + dx;
            Top = _ot + dy;
            return;
        }

        var g = _gesture ?? "";
        var west = g.Contains('W');
        var east = g.Contains('E');
        var north = g.Contains('N');
        var south = g.Contains('S');
        var w = _ow;
        var h = _oh;
        var l = _ol;
        var t = _ot;
        if (east) w = Math.Max(MinW, _ow + dx);
        if (west)
        {
            w = Math.Max(MinW, _ow - dx);
            l = _ol + (_ow - w);
        }
        if (south) h = Math.Max(MinH, _oh + dy);
        if (north)
        {
            h = Math.Max(MinH, _oh - dy);
            t = _ot + (_oh - h);
        }
        Left = l;
        Top = t;
        Width = w;
        Height = h;
    }

    private void EnsureOnScreen()
    {
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        if (double.IsNaN(Left) || Left + Width < vsLeft + 40 || Left > vsRight - 40
            || Top + Height < vsTop + 40 || Top > vsBottom - 40)
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - Width - 24);
            Top = area.Top + 24;
        }
    }

    private void Draw()
    {
        MapCanvas.Children.Clear();
        var view = _view;
        var w = MapCanvas.ActualWidth;
        var h = MapCanvas.ActualHeight;
        if (w < 2 || h < 2) return;
        var underlay = LoadUnderlay(view?.UnderlayImage);
        if (view is null || (underlay is null && view.Markers.Length == 0 && view.PlayerX is null))
        {
            AddMessage(view?.Message ?? L.T("Карта скрыта вне рейда", "Map hides outside a raid"), w, h);
            return;
        }

        if (!WorldMath.TryFit(view.MinX, view.MaxX, view.MinZ, view.MaxZ, view.Rotation, w, h, out var fit))
            return;

        Point ToScreen(double x, double z)
        {
            var p = fit.ToScreen(x, z);
            return new Point(p.X, p.Y);
        }

        if (underlay is not null)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = underlay,
                Width = fit.UsedW,
                Height = fit.UsedH,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(img, fit.OriginX);
            Canvas.SetTop(img, fit.OriginY);
            MapCanvas.Children.Add(img);
        }

        MapCanvas.Children.Add(new Rectangle
        {
            Width = fit.UsedW,
            Height = fit.UsedH,
            Stroke = FrameStroke,
            StrokeThickness = 1,
            IsHitTestVisible = false
        });
        Canvas.SetLeft(MapCanvas.Children[^1], fit.OriginX);
        Canvas.SetTop(MapCanvas.Children[^1], fit.OriginY);

            if (underlay is not null)
            {
                DrawScale(fit);
                DrawLabels(view, fit, ToScreen);
                DrawCredit(view, fit);
                if (!double.IsNaN(view.CompassDegrees))
                    DrawNorth(fit, WorldMath.ScreenAngleDegrees(0, 0, view.CompassDegrees, view.Rotation));
            }

        foreach (var marker in view.Markers.OrderBy(m => m.Nearest))
        {
            var p = ToScreen(marker.X, marker.Z);
            var radius = marker.Nearest ? 5.5 : 3.5;
            var dot = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = BrushFor(marker.Faction),
                Stroke = marker.Nearest ? Brushes.White : null,
                StrokeThickness = marker.Nearest ? 1.5 : 0,
                ToolTip = marker.Name
            };
            Canvas.SetLeft(dot, p.X - radius);
            Canvas.SetTop(dot, p.Y - radius);
            MapCanvas.Children.Add(dot);
            if (!marker.Nearest) continue;
            var label = new Border
            {
                Background = LabelBg,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = marker.Name,
                    Foreground = Brushes.White,
                    FontFamily = UiFont,
                    FontSize = 12
                }
            };
            label.Measure(new Size(w, h));
            var lx = Math.Clamp(p.X + 8, 2, Math.Max(2, w - label.DesiredSize.Width - 2));
            var ly = Math.Clamp(p.Y - 8, 2, Math.Max(2, h - label.DesiredSize.Height - 2));
            Canvas.SetLeft(label, lx);
            Canvas.SetTop(label, ly);
            MapCanvas.Children.Add(label);
        }

        if (view.PlayerX is { } px && view.PlayerZ is { } pz)
        {
            var at = ToScreen(px, pz);
            var angle = WorldMath.ScreenAngleDegrees(px, pz, view.Heading, view.Rotation);
            var arrow = new Polygon
            {
                Points = new PointCollection { new(0, -10), new(7, 8), new(0, 4), new(-7, 8) },
                Fill = Brushes.White,
                Stroke = PmcBrush,
                StrokeThickness = 1.2,
                IsHitTestVisible = false,
                RenderTransform = new TransformGroup
                {
                    Children = { new RotateTransform(angle), new TranslateTransform(at.X, at.Y) }
                }
            };
            MapCanvas.Children.Add(arrow);
        }
    }

    private void DrawScale(MapProjection fit)
    {
        var meters = 100;
        while (meters > 25 && meters * fit.Scale > fit.UsedW * 0.4) meters /= 2;
        var length = meters * fit.Scale;
        if (length < 8 || fit.UsedH < 36) return;
        var x = fit.OriginX + 10;
        var y = fit.OriginY + fit.UsedH - 14;
        MapCanvas.Children.Add(new Line
        {
            X1 = x,
            Y1 = y,
            X2 = x + length,
            Y2 = y,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        });
        var caption = new TextBlock
        {
            Text = L.T($"{meters} м", $"{meters} m"),
            Foreground = Brushes.White,
            FontFamily = UiFont,
            FontSize = 11,
            IsHitTestVisible = false
        };
        caption.Measure(new Size(fit.UsedW, fit.UsedH));
        Canvas.SetLeft(caption, x);
        Canvas.SetTop(caption, Math.Max(fit.OriginY + 2, y - caption.DesiredSize.Height - 2));
        MapCanvas.Children.Add(caption);
    }

    private void DrawNorth(MapProjection fit, double angle)
    {
        if (fit.UsedW < 48 || fit.UsedH < 48) return;
        var cx = fit.OriginX + fit.UsedW / 2;
        var cy = fit.OriginY + 16;
        var rad = angle * Math.PI / 180.0;
        var nx = cx + Math.Sin(rad) * 14;
        var ny = cy - Math.Cos(rad) * 14;
        MapCanvas.Children.Add(new Line
        {
            X1 = cx,
            Y1 = cy,
            X2 = nx,
            Y2 = ny,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        });
        var letter = new TextBlock
        {
            Text = "N",
            Foreground = Brushes.White,
            FontFamily = UiFont,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        letter.Measure(new Size(40, 20));
        Canvas.SetLeft(letter, nx - letter.DesiredSize.Width / 2);
        Canvas.SetTop(letter, ny - letter.DesiredSize.Height - 1);
        MapCanvas.Children.Add(letter);
    }

    private void DrawLabels(MapView view, MapProjection fit, Func<double, double, Point> toScreen)
    {
        var labels = view.Labels;
        if (labels is null || labels.Length == 0) return;
        var placed = new List<Rect>();
        var limit = 0;
        foreach (var label in labels.OrderByDescending(l => l.Priority).ThenBy(l => l.Display(L.En).Length))
        {
            if (limit >= 28) break;
            var text = label.Display(L.En);
            var at = toScreen(label.X, label.Z);
            if (at.X < fit.OriginX + 4 || at.Y < fit.OriginY + 4
                || at.X > fit.OriginX + fit.UsedW - 4 || at.Y > fit.OriginY + fit.UsedH - 4)
                continue;
            if (NearMarker(view, toScreen, at)) continue;

            var block = new TextBlock
            {
                Text = text,
                Foreground = LabelFg,
                FontFamily = UiFont,
                FontSize = 12,
                IsHitTestVisible = false
            };
            block.Measure(new Size(fit.UsedW, fit.UsedH));
            var width = block.DesiredSize.Width;
            var height = block.DesiredSize.Height;
            var x = Math.Clamp(at.X + 6, fit.OriginX + 2, Math.Max(fit.OriginX + 2, fit.OriginX + fit.UsedW - width - 2));
            var y = Math.Clamp(at.Y - height - 2, fit.OriginY + 2, Math.Max(fit.OriginY + 2, fit.OriginY + fit.UsedH - height - 2));
            var rect = new Rect(x - 2, y - 1, width + 4, height + 2);
            if (placed.Any(p => p.IntersectsWith(rect))) continue;
            placed.Add(rect);

            var shadow = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontFamily = UiFont,
                FontSize = 12,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(shadow, x + 1);
            Canvas.SetTop(shadow, y + 1);
            MapCanvas.Children.Add(shadow);
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            MapCanvas.Children.Add(block);
            limit++;
        }
    }

    private void DrawCredit(MapView view, MapProjection fit)
    {
        if (string.IsNullOrWhiteSpace(view.Credit) || fit.UsedW < 160 || fit.UsedH < 48) return;
        var block = new TextBlock
        {
            Text = view.Credit,
            Foreground = CreditFg,
            FontFamily = UiFont,
            FontSize = 10,
            IsHitTestVisible = false
        };
        block.Measure(new Size(fit.UsedW, fit.UsedH));
        var x = fit.OriginX + fit.UsedW - block.DesiredSize.Width - 8;
        var y = fit.OriginY + fit.UsedH - block.DesiredSize.Height - 6;
        if (x < fit.OriginX + 8) return;
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        MapCanvas.Children.Add(block);
    }

    private static bool NearMarker(MapView view, Func<double, double, Point> toScreen, Point at)
    {
        if (view.PlayerX is { } px && view.PlayerZ is { } pz)
        {
            var player = toScreen(px, pz);
            if (Math.Abs(player.X - at.X) < 20 && Math.Abs(player.Y - at.Y) < 20) return true;
        }

        foreach (var marker in view.Markers)
        {
            if (!marker.Nearest) continue;
            var p = toScreen(marker.X, marker.Z);
            if (Math.Abs(p.X - at.X) < 18 && Math.Abs(p.Y - at.Y) < 18) return true;
        }
        return false;
    }

    private static ImageSource? LoadUnderlay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_images.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            _images[path] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            _images[path] = null;
            AppLog.Warn($"[GPS] не удалось открыть подложку {System.IO.Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private void AddMessage(string text, double w, double h)
    {
        var block = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Foreground = QuietBrush,
            FontFamily = UiFont,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Width = Math.Max(40, w - 24),
            IsHitTestVisible = false
        };
        block.Measure(new Size(w, h));
        Canvas.SetLeft(block, 12);
        Canvas.SetTop(block, Math.Max(8, (h - block.DesiredSize.Height) / 2));
        MapCanvas.Children.Add(block);
    }

    private static Brush BrushFor(string faction) => faction switch
    {
        "pmc" => PmcBrush,
        "scav" => ScavBrush,
        "shared" => SharedBrush,
        _ => OtherBrush
    };

    private Point CursorDip()
    {
        GetCursorPos(out var p);
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is null) return new Point(p.X, p.Y);
        return src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
    }

    private static readonly Brush PmcBrush = Freeze(Color.FromRgb(90, 200, 120));
    private static readonly Brush ScavBrush = Freeze(Color.FromRgb(230, 180, 80));
    private static readonly Brush SharedBrush = Freeze(Color.FromRgb(142, 180, 255));
    private static readonly Brush OtherBrush = Freeze(Color.FromRgb(200, 205, 210));
    private static readonly Brush QuietBrush = Freeze(Color.FromRgb(230, 180, 70));
    private static readonly Brush LabelBg = Freeze(Color.FromArgb(210, 16, 19, 22));
    private static readonly Brush LabelFg = Freeze(Color.FromRgb(236, 232, 214));
    private static readonly Brush FrameStroke = Freeze(Color.FromArgb(50, 255, 255, 255));
    private static readonly Brush CreditFg = Freeze(Color.FromArgb(180, 236, 232, 214));
    private static readonly FontFamily UiFont = new("Segoe UI");

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}
