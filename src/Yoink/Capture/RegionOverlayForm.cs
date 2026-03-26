using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _virtualBounds;

    private CaptureMode _mode = CaptureMode.Rectangle;
    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private Rectangle _selectionRect;
    private bool _hasSelection;
    private bool _hasDragged;

    private readonly List<Point> _freeformPoints = new();
    private Rectangle _hoveredWindowRect;

    private readonly Rectangle[] _toolbarButtons = new Rectangle[4];
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 44;
    private const int ButtonSize = 36;
    private const int ButtonSpacing = 4;
    private const int ToolbarTopMargin = 16;

    private float _toolbarAnim;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly DateTime _showTime;

    public event Action<Rectangle>? RegionSelected;
    public event Action<Bitmap>? FreeformSelected;
    public event Action? SelectionCancelled;

    public RegionOverlayForm(Bitmap screenshot, Rectangle virtualBounds,
        CaptureMode initialMode = CaptureMode.Rectangle)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _mode = initialMode;
        _dimmed = CreateDimmed(screenshot);
        _showTime = DateTime.UtcNow;

        SetupForm();
        CalcToolbar();

        _animTimer = new System.Windows.Forms.Timer { Interval = 12 };
        _animTimer.Tick += (_, _) =>
        {
            _toolbarAnim = Math.Min(1f, (float)(DateTime.UtcNow - _showTime).TotalMilliseconds / 180f);
            if (_toolbarAnim >= 1f) _animTimer.Stop();
            Invalidate(new Rectangle(_toolbarRect.X - 2, _toolbarRect.Y - 32,
                _toolbarRect.Width + 4, _toolbarRect.Height + 36));
        };
        _animTimer.Start();
    }

    private void SetupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(_virtualBounds.X, _virtualBounds.Y,
            _virtualBounds.Width, _virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Force above ALL other topmost windows (PiP, etc.)
        Native.User32.SetWindowPos(Handle, Native.User32.HWND_TOPMOST,
            0, 0, 0, 0,
            Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_SHOWWINDOW);
        Native.User32.SetForegroundWindow(Handle);
    }

    private void CalcToolbar()
    {
        int w = ButtonSize * 4 + ButtonSpacing * 3 + 16;
        int x = (ClientSize.Width - w) / 2;
        _toolbarRect = new Rectangle(x, ToolbarTopMargin, w, ToolbarHeight);
        for (int i = 0; i < 4; i++)
            _toolbarButtons[i] = new Rectangle(
                _toolbarRect.X + 8 + i * (ButtonSize + ButtonSpacing),
                _toolbarRect.Y + (ToolbarHeight - ButtonSize) / 2,
                ButtonSize, ButtonSize);
    }

    private static Bitmap CreateDimmed(Bitmap src)
    {
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(src, 0, 0);
        using var brush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        g.FillRectangle(brush, 0, 0, bmp.Width, bmp.Height);
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_dimmed, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;

        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_screenshot, _selectionRect, _selectionRect, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var pen = new Pen(Color.White, 2f))
                    g.DrawRectangle(pen, _selectionRect);
                DrawLabel(g, _selectionRect);
                break;

            case CaptureMode.Freeform when _freeformPoints.Count >= 2:
                using (var pen = new Pen(Color.White, 2f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLines(pen, _freeformPoints.ToArray());
                    if (!_isSelecting && _freeformPoints.Count > 2)
                        g.DrawLine(pen, _freeformPoints[^1], _freeformPoints[0]);
                    g.SmoothingMode = SmoothingMode.Default;
                }
                break;

            case CaptureMode.Window when _hoveredWindowRect.Width > 0:
                var cr = Rectangle.Intersect(_hoveredWindowRect,
                    new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                if (cr.Width > 0)
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(_screenshot, cr, cr, GraphicsUnit.Pixel);
                    g.CompositingMode = CompositingMode.SourceOver;
                }
                using (var pen = new Pen(Color.FromArgb(200, 0, 120, 215), 3f))
                    g.DrawRectangle(pen, _hoveredWindowRect);
                break;
        }

        PaintToolbar(g);
    }

    private void PaintToolbar(Graphics g)
    {
        float t = 1f - MathF.Pow(1f - _toolbarAnim, 3f);
        int oy = (int)((1f - t) * -30);
        int a = (int)(t * 200);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y + oy,
            _toolbarRect.Width, _toolbarRect.Height);

        using (var p = RRect(r, 10))
        {
            using var b = new SolidBrush(Color.FromArgb(a, 32, 32, 32));
            g.FillPath(b, p);
            using var bp = new Pen(Color.FromArgb((int)(t * 50), 255, 255, 255));
            g.DrawPath(bp, p);
        }

        // Buttons: 0=rect, 1=freeform, 2=fullscreen, 3=close
        string[] icons = { "rect", "free", "full", "close" };
        CaptureMode[] modes = { CaptureMode.Rectangle, CaptureMode.Freeform, CaptureMode.Fullscreen };
        for (int i = 0; i < 4; i++)
        {
            var btn = new Rectangle(_toolbarButtons[i].X, _toolbarButtons[i].Y + oy,
                ButtonSize, ButtonSize);
            bool active = i < 3 && _mode == modes[i];
            bool hover = _hoveredButton == i;
            if (active || hover)
                using (var p = RRect(btn, 6))
                using (var b = new SolidBrush(Color.FromArgb((int)(t * (active ? 80 : 40)), 255, 255, 255)))
                    g.FillPath(b, p);
            int ia = (int)(t * (i == 3 ? 200 : 255));
            DrawIcon(g, icons[i], btn, Color.FromArgb(ia, 255, 255, 255));
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c)
    {
        using var pen = new Pen(c, 1.6f);
        int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2, s = 8;
        switch (icon)
        {
            case "rect": g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4); break;
            case "free": g.DrawBezier(pen, cx - s, cy + s - 4, cx - s + 4, cy - s,
                cx + s - 4, cy + s - 2, cx + s, cy - s + 4); break;
            case "win":
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4);
                g.DrawLine(pen, cx - s, cy - s + 7, cx + s, cy - s + 7); break;
            case "full":
                g.DrawRectangle(pen, cx - s, cy - s + 1, s * 2, s * 2 - 5);
                g.DrawLine(pen, cx - 4, cy + s - 2, cx + 4, cy + s - 2); break;
            case "close":
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5); break;
        }
    }

    private void DrawLabel(Graphics g, Rectangle rect)
    {
        string text = $"{rect.Width} x {rect.Height}";
        using var font = new Font("Segoe UI", 11f);
        var sz = g.MeasureString(text, font);
        float lx = rect.X, ly = rect.Bottom + 8;
        if (ly + sz.Height > ClientSize.Height) ly = rect.Y - sz.Height - 8;
        var lr = new RectangleF(lx - 6, ly - 3, sz.Width + 12, sz.Height + 6);
        using var bg = new SolidBrush(Color.FromArgb(210, 24, 24, 24));
        using var fg = new SolidBrush(Color.White);
        using var p = RRect(lr, 6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(bg, p);
        g.SmoothingMode = SmoothingMode.Default;
        g.DrawString(text, font, fg, lx, ly);
    }

    private static GraphicsPath RRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ─── Mouse ─────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;

        int btn = GetToolbarButtonAt(e.Location);
        if (btn >= 0)
        {
            if (btn == 3) { Cancel(); return; }
            // Map: 0=Rectangle, 1=Freeform, 2=Fullscreen
            var modeMap = new[] { CaptureMode.Rectangle, CaptureMode.Freeform, CaptureMode.Fullscreen };
            SetMode(modeMap[btn]);
            return;
        }

        _hasDragged = false;
        switch (_mode)
        {
            case CaptureMode.Rectangle:
                _isSelecting = true;
                _selectionStart = _selectionEnd = e.Location;
                _hasSelection = false;
                break;
            case CaptureMode.Freeform:
                _isSelecting = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(e.Location);
                break;
            case CaptureMode.Window:
                if (_hoveredWindowRect.Width > 0) RegionSelected?.Invoke(_hoveredWindowRect);
                break;
            case CaptureMode.Fullscreen:
                RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int btn = GetToolbarButtonAt(e.Location);
        if (btn != _hoveredButton) { _hoveredButton = btn; Invalidate(); }
        Cursor = btn >= 0 ? Cursors.Hand : Cursors.Cross;

        switch (_mode)
        {
            case CaptureMode.Rectangle when _isSelecting:
                _selectionEnd = e.Location;
                _selectionRect = NormRect(_selectionStart, _selectionEnd);
                if (_selectionRect.Width > 3 || _selectionRect.Height > 3) _hasDragged = true;
                _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
                Invalidate();
                break;
            case CaptureMode.Freeform when _isSelecting:
                _freeformPoints.Add(e.Location);
                _hasDragged = true;
                Invalidate();
                break;
            case CaptureMode.Window:
                var wr = WindowDetector.GetWindowRectAtPoint(e.Location, _virtualBounds);
                if (wr != _hoveredWindowRect) { _hoveredWindowRect = wr; Invalidate(); }
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        switch (_mode)
        {
            case CaptureMode.Rectangle when _isSelecting:
                _isSelecting = false;
                if (!_hasDragged)
                    RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                else if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                    RegionSelected?.Invoke(_selectionRect);
                else { _hasSelection = false; Invalidate(); }
                break;
            case CaptureMode.Freeform when _isSelecting:
                _isSelecting = false;
                if (!_hasDragged)
                    RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                else if (_freeformPoints.Count > 2) CompleteFreeform();
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Cancel();
        if (e.KeyCode == Keys.D1) SetMode(CaptureMode.Rectangle);
        if (e.KeyCode == Keys.D2) SetMode(CaptureMode.Freeform);
        if (e.KeyCode == Keys.D3) SetMode(CaptureMode.Fullscreen);
    }

    private int GetToolbarButtonAt(Point p)
    {
        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
        return -1;
    }

    private void SetMode(CaptureMode m)
    {
        _mode = m; _hasSelection = false; _hasDragged = false;
        _freeformPoints.Clear(); _hoveredWindowRect = Rectangle.Empty;
        _isSelecting = false; Invalidate();
    }

    private void CompleteFreeform()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        var bb = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bb.Width < 3 || bb.Height < 3) return;
        var r = new Bitmap(bb.Width, bb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(r))
        {
            var pts = _freeformPoints.Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();
            using var cp = new GraphicsPath(); cp.AddPolygon(pts); g.SetClip(cp);
            g.DrawImage(_screenshot, new Rectangle(0, 0, bb.Width, bb.Height), bb, GraphicsUnit.Pixel);
        }
        FreeformSelected?.Invoke(r);
    }

    private void Cancel() => SelectionCancelled?.Invoke();
    private static Rectangle NormRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _dimmed.Dispose(); _animTimer.Dispose(); }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
}
