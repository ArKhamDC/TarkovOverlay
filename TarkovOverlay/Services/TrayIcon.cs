using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TarkovOverlay.Services;

/// <summary>Значок в трее: показать/скрыть, сканировать экран, обновить цены, открыть лог, выход.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Drawing.Icon _iconImage;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _modeItems = new();

    public void SetMode(string choice, string effectiveLabel)
    {
        foreach (var (k, it) in _modeItems) it.Checked = k == choice;
        if (_modeItems.TryGetValue("auto", out var a)) a.Text = $"Авто (по логам игры): {effectiveLabel}";
    }

    public void AddAction(string text, Action action, int index)
        => _icon.ContextMenuStrip!.Items.Insert(index, new Forms.ToolStripMenuItem(text, null, (_, _) => action()));

    public void AddModeMenu(Action<string> setMode)
    {
        var root = new Forms.ToolStripMenuItem("Режим цен\tCtrl+Alt+M");
        foreach (var (k, t) in new[] { ("auto", "Авто (по логам игры)"), ("pvp", "PvP"), ("pve", "PvE"), ("season", "Сезон (цены торговцев, барахолки нет в tarkov.dev)") })
        {
            var it = new Forms.ToolStripMenuItem(t, null, (_, _) => setMode(k));
            _modeItems[k] = it; root.DropDownItems.Add(it);
        }
        _icon.ContextMenuStrip!.Items.Insert(0, root);
    }

    public TrayIcon(Action toggle, Action fullScan, Action refresh, Action openFolder, Action exit)
    {
        _iconImage = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        _toggleItem = new Forms.ToolStripMenuItem("Скрыть оверлей\tCtrl+Alt+O", null, (_, _) => toggle());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Пересканировать экран", null, (_, _) => fullScan()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Обновить цены\tCtrl+Alt+R", null, (_, _) => refresh()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Открыть папку с логом и настройками", null, (_, _) => openFolder()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход\tCtrl+Alt+X", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Text = "TarkovOverlay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => toggle();
    }

    public void SetVisibleState(bool overlayVisible)
        => _toggleItem.Text = overlayVisible ? "Скрыть оверлей\tCtrl+Alt+O" : "Показать оверлей\tCtrl+Alt+O";

    public void SetTooltip(string text)
    {
        var t = "TarkovOverlay — " + text;
        _icon.Text = t.Length > 127 ? t[..127] : t;
    }

    private static Drawing.Icon CreateIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Drawing.Color.Transparent);
            using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 20, 24, 28));
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(255, 90, 200, 120), 2);
            g.FillEllipse(bg, 1, 1, 29, 29);
            g.DrawEllipse(pen, 1, 1, 29, 29);
            using var font = new Drawing.Font("Segoe UI", 15, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var fg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 90, 200, 120));
            var size = g.MeasureString("₽", font);
            g.DrawString("₽", font, fg, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Drawing.Icon)Drawing.Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }
}
