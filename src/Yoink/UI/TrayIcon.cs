using System.Drawing;
using System.Windows.Forms;

namespace Yoink.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OnCapture;
    public event Action? OnOcr;
    public event Action? OnColorPicker;
    public event Action? OnGifRecord;
    public event Action? OnSettings;
    public event Action? OnHistory;
    public event Action? OnQuit;

    public TrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Yoink",
            Icon = CreateDefaultIcon(),
            Visible = true
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OnCapture?.Invoke();
            else if (e.Button == MouseButtons.Right)
                ShowMenu();
        };
    }

    private ContextMenuStrip CreateThemedMenu()
    {
        Theme.Refresh();
        bool isDark = Theme.IsDark;
        var bg = isDark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
        var fg = isDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(20, 20, 20);
        var hoverBg = isDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(230, 230, 230);
        var sepColor = isDark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(215, 215, 215);

        var menu = new ContextMenuStrip
        {
            BackColor = bg,
            ForeColor = fg,
            ShowImageMargin = false,
            Padding = new Padding(4),
            Font = new Font("Segoe UI", 9f),
        };

        // Custom renderer for rounded feel and hover colors
        menu.Renderer = new ThemedMenuRenderer(bg, hoverBg, sepColor, isDark);

        var captureItem = new ToolStripMenuItem("Screenshot") { ForeColor = fg };
        captureItem.Click += (_, _) => OnCapture?.Invoke();

        var ocrItem = new ToolStripMenuItem("Text capture (OCR)") { ForeColor = fg };
        ocrItem.Click += (_, _) => OnOcr?.Invoke();

        var pickerItem = new ToolStripMenuItem("Color picker") { ForeColor = fg };
        pickerItem.Click += (_, _) => OnColorPicker?.Invoke();

        var gifItem = new ToolStripMenuItem("Record GIF") { ForeColor = fg };
        gifItem.Click += (_, _) => OnGifRecord?.Invoke();

        var settingsItem = new ToolStripMenuItem("Settings") { ForeColor = fg };
        settingsItem.Click += (_, _) => OnSettings?.Invoke();

        var historyItem = new ToolStripMenuItem("History") { ForeColor = fg };
        historyItem.Click += (_, _) => OnHistory?.Invoke();

        var quitItem = new ToolStripMenuItem("Quit") { ForeColor = fg };
        quitItem.Click += (_, _) => OnQuit?.Invoke();

        menu.Items.AddRange(new ToolStripItem[] {
            captureItem, ocrItem, pickerItem, gifItem,
            new ToolStripSeparator(),
            settingsItem, historyItem,
            new ToolStripSeparator(),
            quitItem });

        return menu;
    }

    private void ShowMenu()
    {
        // Recreate the whole menu each time to pick up theme changes
        var fresh = CreateThemedMenu();
        _notifyIcon.ContextMenuStrip = fresh;

        var showMethod = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        showMethod?.Invoke(_notifyIcon, null);
    }

    private static Icon CreateDefaultIcon()
    {
        // Try to load the real icon from the exe's resources
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch { }

        // Fallback: draw a simple Y
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0, 0, 0, 0));
        using var pen = new Pen(Color.White, 3f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.DrawLine(pen, 6, 4, 16, 16);
        g.DrawLine(pen, 26, 4, 16, 16);
        g.DrawLine(pen, 16, 16, 16, 28);
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}

/// <summary>
/// Custom renderer that gives the context menu a modern, themed look.
/// </summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _bg;
    private readonly Color _hover;
    private readonly Color _sep;
    private readonly bool _isDark;

    public ThemedMenuRenderer(Color bg, Color hover, Color sep, bool isDark)
        : base(new ThemedColorTable(bg, hover))
    {
        _bg = bg;
        _hover = hover;
        _sep = sep;
        _isDark = isDark;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        if (e.Item.Selected)
        {
            using var brush = new SolidBrush(_hover);
            using var gp = RoundedRect(r, 4);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, gp);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(_sep);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var borderColor = _isDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
        using var pen = new Pen(borderColor);
        var rect = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        using var gp = RoundedRect(rect, 8);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, gp);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ThemedColorTable : ProfessionalColorTable
{
    private readonly Color _bg;
    private readonly Color _hover;

    public ThemedColorTable(Color bg, Color hover) { _bg = bg; _hover = hover; }

    public override Color MenuBorder => Color.Transparent;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => _hover;
    public override Color MenuStripGradientBegin => _bg;
    public override Color MenuStripGradientEnd => _bg;
    public override Color ToolStripDropDownBackground => _bg;
    public override Color ImageMarginGradientBegin => _bg;
    public override Color ImageMarginGradientMiddle => _bg;
    public override Color ImageMarginGradientEnd => _bg;
}
