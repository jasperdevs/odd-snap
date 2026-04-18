using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using OddSnap.Helpers;

namespace OddSnap.Capture;

internal sealed class LiveSelectionAdornerForm : Form
{
    private readonly Rectangle _virtualBounds;
    private readonly string _hint;

    private readonly Pen _selectionPen = new(Color.White, 2.0f)
    {
        DashStyle = DashStyle.Dash,
        DashPattern = new[] { 4f, 3f },
        LineJoin = LineJoin.Miter
    };
    private readonly Font _labelFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _labelBackgroundBrush = new(UiChrome.SurfacePill);
    private readonly SolidBrush _labelTextBrush = new(UiChrome.SurfaceTextPrimary);
    private readonly SolidBrush _hintTextBrush = new(Color.White);
    private readonly SolidBrush _hintStrokeBrush = new(Color.FromArgb(180, 0, 0, 0));

    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;
    private Rectangle _contentBounds;
    private Rectangle _selection;
    private string _label = "";

    public LiveSelectionAdornerForm(Rectangle virtualBounds, string hint)
    {
        _virtualBounds = virtualBounds;
        _hint = hint;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = GetHintContentBounds();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateSurface();
    }

    public void SetSelection(Rectangle selection, string label)
    {
        _selection = selection;
        _label = label;
        UpdateSurface();
    }

    private void UpdateSurface()
    {
        _contentBounds = GetContentBounds();
        if (_contentBounds.Width <= 0 || _contentBounds.Height <= 0)
            return;

        var screenBounds = new Rectangle(
            _virtualBounds.X + _contentBounds.X,
            _virtualBounds.Y + _contentBounds.Y,
            _contentBounds.Width,
            _contentBounds.Height);

        if (Bounds != screenBounds)
            Bounds = screenBounds;

        var size = _contentBounds.Size;
        if (_surface == null || _surface.Width != size.Width || _surface.Height != size.Height)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _surface = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            _surfaceGraphics = Graphics.FromImage(_surface);
        }

        var g = _surfaceGraphics!;
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(-_contentBounds.X, -_contentBounds.Y);

        if (_selection.Width <= 2 || _selection.Height <= 2)
            DrawHint(g);
        else
            DrawSelection(g);

        g.ResetTransform();
        g.Flush(FlushIntention.Sync);
        UpdateLayeredWindowSurface(screenBounds);
    }

    private Rectangle GetContentBounds()
        => _selection.Width <= 2 || _selection.Height <= 2
            ? GetHintContentBounds()
            : GetSelectionContentBounds();

    private Rectangle GetHintContentBounds()
    {
        var size = TextRenderer.MeasureText(_hint, _hintFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int width = Math.Max(1, size.Width + 8);
        int height = Math.Max(1, size.Height + 8);
        int x = Math.Max(0, _virtualBounds.Width / 2 - width / 2);
        int y = Math.Max(0, _virtualBounds.Height / 2 - height / 2);
        return ClampToVirtualClient(new Rectangle(x, y, width, height));
    }

    private Rectangle GetSelectionContentBounds()
    {
        var bounds = Rectangle.Inflate(_selection, 8, 8);
        if (!string.IsNullOrWhiteSpace(_label))
        {
            var labelBounds = GetLabelBounds(_selection, _label, out _, out _);
            bounds = Rectangle.Union(bounds, Rectangle.Ceiling(labelBounds));
        }

        bounds.Inflate(4, 4);
        return ClampToVirtualClient(bounds);
    }

    private Rectangle ClampToVirtualClient(Rectangle rect)
    {
        var client = new Rectangle(0, 0, _virtualBounds.Width, _virtualBounds.Height);
        rect.Intersect(client);
        return rect.Width <= 0 || rect.Height <= 0 ? new Rectangle(0, 0, 1, 1) : rect;
    }

    private void DrawHint(Graphics g)
    {
        if (string.IsNullOrWhiteSpace(_hint))
            return;

        var size = g.MeasureString(_hint, _hintFont);
        float x = _contentBounds.X + (_contentBounds.Width - size.Width) / 2f;
        float y = _contentBounds.Y + (_contentBounds.Height - size.Height) / 2f;
        DrawOutlinedText(g, _hint, _hintFont, x, y);
    }

    private void DrawSelection(Graphics g)
    {
        g.DrawRectangle(_selectionPen, _selection);

        if (string.IsNullOrWhiteSpace(_label))
            return;

        var labelRect = GetLabelBounds(_selection, _label, out float x, out float y);
        using var path = RoundedRect(labelRect, labelRect.Height / 2f);
        g.FillPath(_labelBackgroundBrush, path);
        g.DrawString(_label, _labelFont, _labelTextBrush, x, y);
    }

    private RectangleF GetLabelBounds(Rectangle selection, string label, out float x, out float y)
    {
        var size = TextRenderer.MeasureText(label, _labelFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        x = selection.X + selection.Width / 2f - size.Width / 2f;
        y = selection.Bottom + 6;
        if (y + size.Height > _virtualBounds.Height - 10)
            y = selection.Y - size.Height - 6;

        return new RectangleF(x - 8, y - 2, size.Width + 16, size.Height + 4);
    }

    private void DrawOutlinedText(Graphics g, string text, Font font, float x, float y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                g.DrawString(text, font, _hintStrokeBrush, x + dx, y + dy);
            }
        }

        g.DrawString(text, font, _hintTextBrush, x, y);
    }

    private void UpdateLayeredWindowSurface(Rectangle screenBounds)
    {
        if (_surface is null)
            return;

        var screenPoint = new Native.User32.POINT { X = screenBounds.X, Y = screenBounds.Y };
        var size = new Native.User32.SIZE { cx = screenBounds.Width, cy = screenBounds.Height };
        var sourcePoint = new Native.User32.POINT { X = 0, Y = 0 };
        var blend = new Native.User32.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        IntPtr hdcScreen = Native.User32.GetDC(IntPtr.Zero);
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        try
        {
            hdcMem = Native.User32.CreateCompatibleDC(hdcScreen);
            hBmp = _surface.GetHbitmap(Color.FromArgb(0));
            hOld = Native.User32.SelectObject(hdcMem, hBmp);
            Native.User32.UpdateLayeredWindow(Handle, hdcScreen, ref screenPoint, ref size,
                hdcMem, ref sourcePoint, 0, ref blend, 2);
        }
        finally
        {
            if (hdcMem != IntPtr.Zero && hOld != IntPtr.Zero)
                Native.User32.SelectObject(hdcMem, hOld);
            if (hBmp != IntPtr.Zero)
                Native.User32.DeleteObject(hBmp);
            if (hdcMem != IntPtr.Zero)
                Native.User32.DeleteDC(hdcMem);
            Native.User32.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _selectionPen.Dispose();
            _labelFont.Dispose();
            _hintFont.Dispose();
            _labelBackgroundBrush.Dispose();
            _labelTextBrush.Dispose();
            _hintTextBrush.Dispose();
            _hintStrokeBrush.Dispose();
        }

        base.Dispose(disposing);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
