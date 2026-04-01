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
    public event Action? OnScrollCapture;
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
        bool dark = Theme.IsDark;

        // Win11 flyout colors
        var bg   = dark ? Color.FromArgb(44, 44, 44)  : Color.FromArgb(252, 252, 252);
        var fg   = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(22, 22, 22);
        var hov  = dark ? Color.FromArgb(60, 60, 60)  : Color.FromArgb(238, 238, 238);
        var sep  = dark ? Color.FromArgb(55, 55, 55)  : Color.FromArgb(226, 226, 226);
        var quit = dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(100, 100, 100);

        var menu = new ContextMenuStrip
        {
            BackColor = bg,
            ForeColor = fg,
            ShowImageMargin = false,
            Padding = new Padding(2, 3, 2, 3),
            Font = new Font("Segoe UI Variable Text", 9f),
            DropShadowEnabled = true,
        };
        menu.Renderer = new ThemedMenuRenderer(bg, hov, sep, dark);

        // Apply Win11 rounded corners via DWM
        menu.HandleCreated += (s, _) =>
        {
            try
            {
                var h = ((ContextMenuStrip)s!).Handle;
                int round = Native.Dwm.DWMWCP_ROUND;
                Native.Dwm.DwmSetWindowAttribute(h, Native.Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int dm = dark ? 1 : 0;
                Native.Dwm.DwmSetWindowAttribute(h, Native.Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dm, sizeof(int));
            }
            catch { }
        };

        // Helper to create uniform menu items
        ToolStripMenuItem Item(string text, Color? color = null) => new(text)
        {
            ForeColor = color ?? fg,
            Padding = new Padding(8, 5, 14, 5),
        };

        var captureItem = Item("Screenshot");     captureItem.Click += (_, _) => OnCapture?.Invoke();
        var ocrItem     = Item("Text capture");   ocrItem.Click     += (_, _) => OnOcr?.Invoke();
        var pickerItem  = Item("Color picker");   pickerItem.Click  += (_, _) => OnColorPicker?.Invoke();
        bool isRecording = Capture.RecordingForm.Current != null;
        var gifItem = Item(isRecording ? "Stop Recording" : "Record",
                          isRecording ? Color.FromArgb(239, 68, 68) : (Color?)null);
        gifItem.Click += (_, _) =>
        {
            if (Capture.RecordingForm.Current != null)
                Capture.RecordingForm.Current.RequestStop();
            else
                OnGifRecord?.Invoke();
        };
        var scrollItem  = Item("Scroll capture"); scrollItem.Click  += (_, _) => OnScrollCapture?.Invoke();
        var settingsItem = Item("Settings");      settingsItem.Click += (_, _) => OnSettings?.Invoke();
        var historyItem = Item("History");        historyItem.Click += (_, _) => OnHistory?.Invoke();
        var quitItem    = Item("Quit", quit);     quitItem.Click    += (_, _) => OnQuit?.Invoke();

        menu.Items.AddRange(new ToolStripItem[] {
            captureItem, ocrItem, pickerItem, gifItem, scrollItem,
            new ToolStripSeparator(),
            settingsItem, historyItem,
            new ToolStripSeparator(),
            quitItem
        });
        return menu;
    }


    private void ShowMenu()
    {
        // Recreate the whole menu each time to pick up theme changes
        var fresh = CreateThemedMenu();
        var previous = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = fresh;
        previous?.Dispose();

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
                if (icon != null) return ToGrayscaleIcon(icon);
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

    private static Icon ToGrayscaleIcon(Icon icon)
    {
        using var bmp = icon.ToBitmap();
        var gray = new Bitmap(bmp.Width, bmp.Height);
        using var g = Graphics.FromImage(gray);
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        });
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(matrix);
        g.DrawImage(bmp, new Rectangle(0, 0, gray.Width, gray.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
        return Icon.FromHandle(gray.GetHicon());
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
/// <summary>Win11-style context menu renderer.</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _bg, _hover, _sep;

    public ThemedMenuRenderer(Color bg, Color hover, Color sep, bool isDark)
        : base(new Win11ColorTable(bg))
    { _bg = bg; _hover = hover; _sep = sep; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_hover);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = 8;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_sep);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* DWM */ }
}

internal sealed class Win11ColorTable : ProfessionalColorTable
{
    private readonly Color _bg;
    public Win11ColorTable(Color bg) { _bg = bg; }
    public override Color MenuBorder => Color.Transparent;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color ToolStripDropDownBackground => _bg;
    public override Color ImageMarginGradientBegin => _bg;
    public override Color ImageMarginGradientMiddle => _bg;
    public override Color ImageMarginGradientEnd => _bg;
}
