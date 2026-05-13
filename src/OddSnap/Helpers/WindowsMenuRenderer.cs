using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OddSnap.Helpers;

public static class WindowsMenuRenderer
{
    public const int DefaultWidth = 340;
    public const int RowHeight = 29;
    private const int BaseDpi = 96;

    public static ContextMenuStrip Create(bool showImages = true, int minWidth = DefaultWidth)
    {
        OddSnap.UI.Theme.Refresh();
        var bg = UiChrome.SurfaceElevated;
        var fg = UiChrome.SurfaceTextPrimary;
        var hover = UiChrome.IsDark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
        var active = UiChrome.IsDark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        var muted = UiChrome.SurfaceTextMuted;
        var sep = UiChrome.SurfaceBorderSubtle;

        var menu = new ContextMenuStrip
        {
            BackColor = bg,
            ForeColor = fg,
            ShowImageMargin = showImages,
            ShowCheckMargin = false,
            Padding = new Padding(4, 5, 4, 5),
            Font = UiChrome.ChromeFont(8.5f),
            DropShadowEnabled = true,
            MinimumSize = new Size(minWidth, 0),
            Renderer = new Renderer(bg, fg, hover, active, muted, sep, showImages)
        };

        ApplyMenuMetricsForCurrentDpi(menu, minWidth);
        menu.Opening += (_, _) => ApplyMenuMetricsForCurrentDpi(menu, minWidth);
        menu.HandleCreated += (s, _) =>
        {
            try
            {
                var strip = (ContextMenuStrip)s!;
                ApplyMenuMetricsForCurrentDpi(strip, minWidth);
                var handle = strip.Handle;
                OddSnap.Native.Dwm.TrySetWindowCornerPreference(handle, OddSnap.Native.Dwm.DWMWCP_ROUND);
                OddSnap.Native.Dwm.TrySetImmersiveDarkMode(handle, UiChrome.IsDark);
                ApplyRoundedRegion(strip);
            }
            catch { }
        };
        menu.SizeChanged += (_, _) => ApplyRoundedRegion(menu);
        menu.Disposed += (_, _) => menu.Region?.Dispose();

        return menu;
    }

    public static ToolStripMenuItem Item(
        string text,
        string? shortcut = null,
        string? iconId = null,
        bool active = false,
        bool danger = false)
    {
        text = OddSnap.Services.LocalizationService.Translate(text);

        var color = danger
            ? Color.FromArgb(239, 68, 68)
            : UiChrome.SurfaceTextPrimary;
        var imageColor = danger
            ? color
            : active
                ? Color.FromArgb(255, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B)
                : Color.FromArgb(215, UiChrome.SurfaceTextSecondary.R, UiChrome.SurfaceTextSecondary.G, UiChrome.SurfaceTextSecondary.B);

        return new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Height = RowHeight,
            Width = DefaultWidth - 8,
            ForeColor = color,
            Image = iconId is null ? null : FluentIcons.RenderBitmap(iconId, imageColor, 20, active),
            ImageScaling = ToolStripItemImageScaling.None,
            ShortcutKeyDisplayString = shortcut ?? string.Empty,
            Tag = active
        };
    }

    public static int NormalizeItemWidths(ContextMenuStrip menu, int minWidth = DefaultWidth)
    {
        ApplyMenuMetricsForCurrentDpi(menu, minWidth);

        int dpi = GetDeviceDpi(menu);
        int width = ScaleForDpi(minWidth, dpi);
        int textPadding = ScaleForDpi(menu.ShowImageMargin ? 124 : 76, dpi);
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem menuItem)
                continue;

            int text = TextRenderer.MeasureText(g, menuItem.Text, menuItem.Font).Width;
            int shortcut = string.IsNullOrWhiteSpace(menuItem.ShortcutKeyDisplayString)
                ? 0
                : TextRenderer.MeasureText(g, menuItem.ShortcutKeyDisplayString, menuItem.Font).Width;
            width = Math.Max(width, text + shortcut + textPadding);
        }

        SetMenuWidth(menu, width);
        return width;
    }

    public static void SetMenuWidth(ContextMenuStrip menu, int width)
    {
        int dpi = GetDeviceDpi(menu);
        int inset = ScaleForDpi(8, dpi);
        int rowHeight = GetScaledRowHeight(menu);

        width = Math.Max(ScaleForDpi(120, dpi), width);
        menu.MinimumSize = new Size(width, 0);
        menu.Width = width;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.AutoSize = false;
                menuItem.Width = width - inset;
                menuItem.Height = rowHeight;
            }
        }
    }

    public static int GetScaledRowHeight(ContextMenuStrip? menu)
    {
        int dpi = GetDeviceDpi(menu);
        int scaledMinimum = ScaleForDpi(RowHeight, dpi);
        if (menu is null)
            return scaledMinimum;

        int measuredText = TextRenderer.MeasureText("Ag", menu.Font).Height + ScaleForDpi(12, dpi);
        return Math.Max(scaledMinimum, measuredText);
    }

    public static int EstimateMenuHeight(ContextMenuStrip? menu, int itemCount)
    {
        int dpi = GetDeviceDpi(menu);
        return GetScaledRowHeight(menu) * Math.Max(1, itemCount) + ScaleForDpi(12, dpi);
    }

    private static void ApplyMenuMetricsForCurrentDpi(ContextMenuStrip menu, int minWidth)
    {
        int dpi = GetDeviceDpi(menu);
        menu.Padding = new Padding(
            ScaleForDpi(4, dpi),
            ScaleForDpi(5, dpi),
            ScaleForDpi(4, dpi),
            ScaleForDpi(5, dpi));

        SetMenuWidth(menu, Math.Max(menu.Width, ScaleForDpi(minWidth, dpi)));
    }

    private static int GetDeviceDpi(Control? control)
        => Math.Max(BaseDpi, control?.DeviceDpi ?? BaseDpi);

    private static int ScaleForDpi(int value, int dpi)
        => Math.Max(1, (int)Math.Round(value * (dpi / (double)BaseDpi), MidpointRounding.AwayFromZero));

    private static void ApplyRoundedRegion(ContextMenuStrip menu)
    {
        if (menu.Width <= 0 || menu.Height <= 0)
            return;

        using var path = Renderer.RoundedRect(new Rectangle(0, 0, menu.Width, menu.Height), 8);
        var previous = menu.Region;
        menu.Region = new Region(path);
        previous?.Dispose();
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        private readonly Color _bg;
        private readonly Color _fg;
        private readonly Color _hover;
        private readonly Color _active;
        private readonly Color _muted;
        private readonly Color _sep;
        private readonly bool _showImages;

        public Renderer(Color bg, Color fg, Color hover, Color active, Color muted, Color sep, bool showImages)
            : base(new ColorTable(bg))
        {
            _bg = bg;
            _fg = fg;
            _hover = hover;
            _active = active;
            _muted = muted;
            _sep = sep;
            _showImages = showImages;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_bg);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_sep);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool active = e.Item.Tag is true;
            if (!e.Item.Selected && !active)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int insetX = ScaleForDpi(e.ToolStrip, 4);
            int insetY = ScaleForDpi(e.ToolStrip, 2);
            var rect = new Rectangle(insetX, insetY, e.Item.Width - (insetX * 2), e.Item.Height - (insetY * 2));
            using var brush = new SolidBrush(active ? _active : _hover);
            using var path = RoundedRect(rect, ScaleForDpi(e.ToolStrip, 6));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image is null)
                return;

            int iconBox = ScaleForDpi(e.ToolStrip, 20);
            int size = Math.Min(ScaleForDpi(e.ToolStrip, 16), Math.Min(e.Item.Height - ScaleForDpi(e.ToolStrip, 9), e.Image.Width));
            int x = ScaleForDpi(e.ToolStrip, 14) + (iconBox - size) / 2;
            int y = e.Item.ContentRectangle.Y + (e.Item.Height - size) / 2;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(e.Image, new Rectangle(x, y, size, size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is not ToolStripMenuItem item)
            {
                base.OnRenderItemText(e);
                return;
            }

            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            string shortcut = item.ShortcutKeyDisplayString ?? string.Empty;
            int left = ScaleForDpi(e.ToolStrip, _showImages ? 43 : 14);
            int shortcutWidth = string.IsNullOrEmpty(shortcut)
                ? 0
                : TextRenderer.MeasureText(e.Graphics, shortcut, item.Font).Width + ScaleForDpi(e.ToolStrip, 18);

            var labelRect = new Rectangle(
                left,
                0,
                Math.Max(ScaleForDpi(e.ToolStrip, 24), item.Width - left - shortcutWidth - ScaleForDpi(e.ToolStrip, 12)),
                item.Height);
            TextRenderer.DrawText(
                e.Graphics,
                item.Text,
                item.Font,
                labelRect,
                item.ForeColor.IsEmpty ? _fg : item.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (shortcut.Length == 0)
                return;

            var shortcutRect = new Rectangle(item.Width - shortcutWidth - ScaleForDpi(e.ToolStrip, 12), 0, shortcutWidth, item.Height);
            TextRenderer.DrawText(
                e.Graphics,
                shortcut,
                item.Font,
                shortcutRect,
                _muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int left = ScaleForDpi(e.ToolStrip, _showImages ? 42 : 10);
            int y = e.Item.Height / 2;
            using var pen = new Pen(_sep);
            e.Graphics.DrawLine(pen, left, y, e.Item.Width - ScaleForDpi(e.ToolStrip, 10), y);
        }

        private static int ScaleForDpi(ToolStrip? toolStrip, int value)
            => WindowsMenuRenderer.ScaleForDpi(value, GetDeviceDpi(toolStrip));

        public static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class ColorTable : ProfessionalColorTable
    {
        private readonly Color _bg;

        public ColorTable(Color bg)
        {
            _bg = bg;
        }

        public override Color MenuBorder => Color.Transparent;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
    }
}
