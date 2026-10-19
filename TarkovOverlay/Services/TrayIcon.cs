using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using TarkovOverlay.Utils;

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
        if (_modeItems.TryGetValue("auto", out var a)) a.Text = $"{L.T("Авто (по логам игры)", "Auto (from game logs)")}: {effectiveLabel}";
    }

    public void AddAction(string text, Action action, int index)
        => _icon.ContextMenuStrip!.Items.Insert(index, new Forms.ToolStripMenuItem(text, null, (_, _) => action()));

    public void AddModeMenu(Action<string> setMode)
    {
        var root = new Forms.ToolStripMenuItem(L.T("Режим цен", "Price mode") + "\tCtrl+Alt+M");
        foreach (var (k, t) in new[] { ("auto", L.T("Авто (по логам игры)", "Auto (from game logs)")), ("pvp", "PvP"), ("pve", "PvE"), ("season", L.T("Сезон (цены торговцев, барахолки нет в tarkov.dev)", "Season (trader prices; no flea data on tarkov.dev)")) })
        {
            var it = new Forms.ToolStripMenuItem(t, null, (_, _) => setMode(k));
            _modeItems[k] = it; root.DropDownItems.Add(it);
        }
        _icon.ContextMenuStrip!.Items.Insert(0, root);
    }

    /// <summary>Подменю «Язык»: интерфейс и клиент игры. Выбор сохраняется, оверлей перезапускается.</summary>
    public void AddLanguageMenu(string uiChoice, string gameChoice, Action<string> setUi, Action<string> setGame)
    {
        var root = new Forms.ToolStripMenuItem(L.T("Язык / Language", "Language / Язык"));
        var ui = new Forms.ToolStripMenuItem(L.T("Интерфейс", "Interface"));
        foreach (var (k, t) in new[] { ("auto", L.T("Авто (язык Windows)", "Auto (Windows language)")), ("ru", "Русский"), ("en", "English") })
            ui.DropDownItems.Add(new Forms.ToolStripMenuItem(t, null, (_, _) => setUi(k)) { Checked = k == uiChoice });
        var game = new Forms.ToolStripMenuItem(L.T("Клиент игры (названия предметов)", "Game client (item names)"));
        foreach (var (k, t) in new[] { ("auto", L.T("Авто (русский + английский)", "Auto (Russian + English)")), ("ru", "Русский"), ("en", "English") })
            game.DropDownItems.Add(new Forms.ToolStripMenuItem(t, null, (_, _) => setGame(k)) { Checked = k == gameChoice });
        root.DropDownItems.Add(ui);
        root.DropDownItems.Add(game);
        _icon.ContextMenuStrip!.Items.Insert(1, root);
    }

    public TrayIcon(Action toggle, Action fullScan, Action refresh, Action openFolder, Action exit)
    {
        _iconImage = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        _toggleItem = new Forms.ToolStripMenuItem(L.T("Скрыть оверлей", "Hide overlay") + "\tCtrl+Alt+O", null, (_, _) => toggle());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Пересканировать экран", "Rescan screen"), null, (_, _) => fullScan()));
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Обновить цены", "Refresh prices") + "\tCtrl+Alt+R", null, (_, _) => refresh()));
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Открыть папку с логом и настройками", "Open log & settings folder"), null, (_, _) => openFolder()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Выход", "Exit") + "\tCtrl+Alt+X", null, (_, _) => exit()));

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
        => _toggleItem.Text = (overlayVisible ? L.T("Скрыть оверлей", "Hide overlay") : L.T("Показать оверлей", "Show overlay")) + "\tCtrl+Alt+O";

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
