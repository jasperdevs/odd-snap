using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Yoink.Helpers;

namespace Yoink.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private Icon? _defaultIcon;
    private Icon? _recordingIcon;
    private bool _isShowingRecording;

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
        _defaultIcon = CreateDefaultIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = "Yoink",
            Icon = _defaultIcon,
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

    public void UpdateRecordingState(bool isRecording)
    {
        if (isRecording == _isShowingRecording) return;
        _isShowingRecording = isRecording;
        if (isRecording)
        {
            _recordingIcon ??= CreateRecordingIcon();
            _notifyIcon.Icon = _recordingIcon;
        }
        else
            _notifyIcon.Icon = _defaultIcon;
    }

    private ContextMenuStrip CreateThemedMenu()
    {
        Theme.Refresh();
        bool dark = Theme.IsDark;

        var bg  = dark ? Color.FromArgb(44, 44, 44)    : Color.FromArgb(249, 249, 249);
        var fg  = dark ? Color.FromArgb(235, 235, 235)  : Color.FromArgb(20, 20, 20);
        var hov = dark ? Color.FromArgb(58, 58, 58)     : Color.FromArgb(232, 232, 232);
        var sep = dark ? Color.FromArgb(60, 60, 60)     : Color.FromArgb(218, 218, 218);
        var mut = dark ? Color.FromArgb(115, 115, 115)  : Color.FromArgb(140, 140, 140);
        var quitC = dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(120, 120, 120);
        var recRed = Color.FromArgb(239, 68, 68);

        var menu = new ContextMenuStrip
        {
            BackColor = bg,
            ForeColor = fg,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(2, 4, 2, 4),
            Font = UiChrome.ChromeFont(9f),
            DropShadowEnabled = true,
        };
        menu.Renderer = new CleanMenuRenderer(bg, hov, sep, dark);

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

        bool isRec = Capture.RecordingForm.Current != null;

        ToolStripMenuItem Item(string text, Color? color = null) => new(text)
        {
            ForeColor = color ?? fg,
            Padding = new Padding(12, 5, 16, 5),
        };

        var captureItem  = Item("Screenshot");
        var ocrItem      = Item("Text capture");
        var pickerItem   = Item("Color picker");
        var recordItem   = isRec ? Item("Stop recording", recRed) : Item("Record");
        var scrollItem   = Item("Scroll capture");
        var settingsItem = Item("Settings");
        var historyItem  = Item("History");
        var quitItem     = Item("Quit", quitC);

        captureItem.Click += (_, _) => OnCapture?.Invoke();
        ocrItem.Click     += (_, _) => OnOcr?.Invoke();
        pickerItem.Click  += (_, _) => OnColorPicker?.Invoke();
        recordItem.Click  += (_, _) =>
        {
            if (Capture.RecordingForm.Current != null)
                Capture.RecordingForm.Current.RequestStop();
            else
                OnGifRecord?.Invoke();
        };
        scrollItem.Click   += (_, _) => OnScrollCapture?.Invoke();
        settingsItem.Click += (_, _) => OnSettings?.Invoke();
        historyItem.Click  += (_, _) => OnHistory?.Invoke();
        quitItem.Click     += (_, _) => OnQuit?.Invoke();

        menu.Items.AddRange(new ToolStripItem[]
        {
            captureItem, ocrItem, pickerItem, recordItem, scrollItem,
            new ToolStripSeparator(),
            settingsItem, historyItem,
            new ToolStripSeparator(),
            quitItem,
        });

        return menu;
    }

    private void ShowMenu()
    {
        var fresh = CreateThemedMenu();
        var previous = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = fresh;
        previous?.Dispose();

        var showMethod = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        showMethod?.Invoke(_notifyIcon, null);
    }

    // ── Tray icon ────────────────────────────────────────────────

    private static Icon CreateDefaultIcon()
    {
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
        return CreateFallbackIcon(false);
    }

    private static Icon CreateRecordingIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return OverlayRecordingDot(ToGrayscaleIcon(icon));
            }
        }
        catch { }
        return CreateFallbackIcon(true);
    }

    private static Icon OverlayRecordingDot(Icon baseIcon)
    {
        using var baseBmp = baseIcon.ToBitmap();
        var bmp = new Bitmap(baseBmp.Width, baseBmp.Height);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(baseBmp, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Max(8, bmp.Width / 3);
        int x = bmp.Width - d - 1, y = bmp.Height - d - 1;
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(white, x - 1, y - 1, d + 2, d + 2);
        using var red = new SolidBrush(Color.FromArgb(239, 68, 68));
        g.FillEllipse(red, x, y, d, d);
        var result = Icon.FromHandle(bmp.GetHicon());
        baseIcon.Dispose();
        return result;
    }

    private static Icon CreateFallbackIcon(bool recording)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0, 0, 0, 0));
        using var pen = new Pen(Color.White, 3f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLine(pen, 6, 4, 16, 16);
        g.DrawLine(pen, 26, 4, 16, 16);
        g.DrawLine(pen, 16, 16, 16, 28);
        if (recording)
        {
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(white, 20, 21, 12, 12);
            using var red = new SolidBrush(Color.FromArgb(239, 68, 68));
            g.FillEllipse(red, 21, 22, 10, 10);
        }
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
        g.DrawImage(bmp, new Rectangle(0, 0, gray.Width, gray.Height),
            0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
        return Icon.FromHandle(gray.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _defaultIcon?.Dispose();
        _recordingIcon?.Dispose();
    }
}

/// <summary>Clean Win11-style renderer. No icons, just text + right-aligned shortcuts.</summary>
internal sealed class CleanMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _bg, _hover, _sep;

    public CleanMenuRenderer(Color bg, Color hover, Color sep, bool isDark)
        : base(new Win11ColorTable(bg))
    {
        _bg = bg; _hover = hover; _sep = sep;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_hover);
        using var path = RoundedRect(r, 6);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Draw label
        var textRect = new Rectangle(14, 0, e.Item.Width - 28, e.Item.Height);
        TextRenderer.DrawText(g, e.Item.Text, e.Item.Font, textRect, e.Item.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(_sep);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_bg);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
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
