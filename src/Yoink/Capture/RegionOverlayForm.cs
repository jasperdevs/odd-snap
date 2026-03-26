using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmedScreenshot;
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

    // Toolbar
    private readonly Rectangle[] _toolbarButtons = new Rectangle[5];
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 44;
    private const int ButtonSize = 36;
    private const int ButtonSpacing = 4;
    private const int ToolbarTopMargin = 16;

    // Toolbar animation
    private float _toolbarAnimProgress;
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
        _dimmedScreenshot = CreateDimmedScreenshot(screenshot);
        _showTime = DateTime.UtcNow;
        _toolbarAnimProgress = 0f;

        SetupForm();
        CalculateToolbarLayout();

        // Toolbar slide-in animation
        _animTimer = new System.Windows.Forms.Timer { Interval = 12 };
        _animTimer.Tick += (_, _) =>
        {
            float elapsed = (float)(DateTime.UtcNow - _showTime).TotalMilliseconds;
            _toolbarAnimProgress = Math.Min(1f, elapsed / 180f); // 180ms slide-in
            if (_toolbarAnimProgress >= 1f) _animTimer.Stop();
            InvalidateToolbar();
        };
        _animTimer.Start();
    }

    private void SetupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(
            _virtualBounds.X, _virtualBounds.Y,
            _virtualBounds.Width, _virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        KeyPreview = true;
    }

    private void CalculateToolbarLayout()
    {
        int totalW = ButtonSize * 5 + ButtonSpacing * 4;
        int toolbarW = totalW + 16;
        int toolbarX = (ClientSize.Width - toolbarW) / 2;
        _toolbarRect = new Rectangle(toolbarX, ToolbarTopMargin, toolbarW, ToolbarHeight);

        for (int i = 0; i < 5; i++)
        {
            _toolbarButtons[i] = new Rectangle(
                _toolbarRect.X + 8 + i * (ButtonSize + ButtonSpacing),
                _toolbarRect.Y + (ToolbarHeight - ButtonSize) / 2,
                ButtonSize, ButtonSize);
        }
    }

    private static Bitmap CreateDimmedScreenshot(Bitmap original)
    {
        var dimmed = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dimmed);
        g.DrawImage(original, 0, 0);
        using var overlay = new SolidBrush(Color.FromArgb(102, 0, 0, 0));
        g.FillRectangle(overlay, 0, 0, dimmed.Width, dimmed.Height);
        return dimmed;
    }

    // ─── Painting ──────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Base: if selection is active, draw even darker; otherwise normal dim
        if (_hasSelection || (_hoveredWindowRect.Width > 0 && _mode == CaptureMode.Window))
        {
            g.DrawImage(_dimmedScreenshot, 0, 0);
            // Extra darken outside selection
            g.CompositingMode = CompositingMode.SourceOver;
            using var extraDim = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
            g.FillRectangle(extraDim, 0, 0, ClientSize.Width, ClientSize.Height);
        }
        else
        {
            g.DrawImage(_dimmedScreenshot, 0, 0);
        }
        g.CompositingMode = CompositingMode.SourceOver;

        switch (_mode)
        {
            case CaptureMode.Rectangle: PaintRectangleSelection(g); break;
            case CaptureMode.Freeform: PaintFreeformSelection(g); break;
            case CaptureMode.Window: PaintWindowHighlight(g); break;
            case CaptureMode.Fullscreen: break;
        }

        PaintToolbar(g);
    }

    private void PaintRectangleSelection(Graphics g)
    {
        if (!_hasSelection) return;
        g.DrawImage(_screenshot, _selectionRect, _selectionRect, GraphicsUnit.Pixel);
        using var pen = new Pen(Color.White, 2f);
        g.DrawRectangle(pen, _selectionRect);
        DrawDimensionLabel(g, _selectionRect);
    }

    private void PaintFreeformSelection(Graphics g)
    {
        if (_freeformPoints.Count < 2) return;
        using var pen = new Pen(Color.White, 2f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(pen, _freeformPoints.ToArray());
        if (!_isSelecting && _freeformPoints.Count > 2)
            g.DrawLine(pen, _freeformPoints[^1], _freeformPoints[0]);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintWindowHighlight(Graphics g)
    {
        if (_hoveredWindowRect.Width <= 0 || _hoveredWindowRect.Height <= 0) return;
        var clipped = Rectangle.Intersect(_hoveredWindowRect,
            new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        if (clipped.Width > 0 && clipped.Height > 0)
            g.DrawImage(_screenshot, clipped, clipped, GraphicsUnit.Pixel);
        using var pen = new Pen(Color.FromArgb(200, 0, 120, 215), 3f);
        g.DrawRectangle(pen, _hoveredWindowRect);
    }

    private void PaintToolbar(Graphics g)
    {
        // Animated slide-in from top + fade
        float t = EaseOutCubic(_toolbarAnimProgress);
        int offsetY = (int)((1f - t) * -30);
        int alpha = (int)(t * 200);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var animRect = new Rectangle(_toolbarRect.X, _toolbarRect.Y + offsetY,
            _toolbarRect.Width, _toolbarRect.Height);

        using var bgPath = GetRoundedRect(animRect, 10);
        using var bgBrush = new SolidBrush(Color.FromArgb(alpha, 32, 32, 32));
        g.FillPath(bgBrush, bgPath);
        using var borderPen = new Pen(Color.FromArgb((int)(t * 50), 255, 255, 255), 1f);
        g.DrawPath(borderPen, bgPath);

        string[] icons = { "rect", "free", "win", "full", "close" };
        for (int i = 0; i < 5; i++)
        {
            var btn = new Rectangle(
                _toolbarButtons[i].X, _toolbarButtons[i].Y + offsetY,
                _toolbarButtons[i].Width, _toolbarButtons[i].Height);
            bool isActive = i < 4 && (int)_mode == i;
            bool isHovered = _hoveredButton == i;

            if (isActive || isHovered)
            {
                using var btnPath = GetRoundedRect(btn, 6);
                using var btnBrush = new SolidBrush(
                    isActive ? Color.FromArgb((int)(t * 80), 255, 255, 255) :
                    Color.FromArgb((int)(t * 40), 255, 255, 255));
                g.FillPath(btnBrush, btnPath);
            }

            var iconAlpha = (int)(t * 255);
            DrawToolbarIcon(g, icons[i], btn,
                i == 4 ? Color.FromArgb(iconAlpha * 200 / 255, 255, 255, 255)
                       : Color.FromArgb(iconAlpha, 255, 255, 255));
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - MathF.Pow(1f - x, 3f);
    }

    private static void DrawToolbarIcon(Graphics g, string icon, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        int s = 8;

        switch (icon)
        {
            case "rect":
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4);
                break;
            case "free":
                g.DrawBezier(pen, cx - s, cy + s - 4, cx - s + 4, cy - s,
                    cx + s - 4, cy + s - 2, cx + s, cy - s + 4);
                break;
            case "win":
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4);
                g.DrawLine(pen, cx - s, cy - s + 7, cx + s, cy - s + 7);
                break;
            case "full":
                g.DrawRectangle(pen, cx - s, cy - s + 1, s * 2, s * 2 - 5);
                g.DrawLine(pen, cx - 4, cy + s - 2, cx + 4, cy + s - 2);
                break;
            case "close":
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                break;
        }
    }

    private void DrawDimensionLabel(Graphics g, Rectangle rect)
    {
        string text = $"{rect.Width} x {rect.Height}";
        using var font = new Font("Segoe UI", 11f);
        var size = g.MeasureString(text, font);
        float lx = rect.X;
        float ly = rect.Bottom + 8;
        if (ly + size.Height > ClientSize.Height) ly = rect.Y - size.Height - 8;
        var labelRect = new RectangleF(lx - 6, ly - 3, size.Width + 12, size.Height + 6);
        using var bg = new SolidBrush(Color.FromArgb(210, 24, 24, 24));
        using var fg = new SolidBrush(Color.White);
        using var path = GetRoundedRect(labelRect, 6);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(bg, path);
        g.SmoothingMode = SmoothingMode.Default;
        g.DrawString(text, font, fg, lx, ly);
    }

    private static GraphicsPath GetRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ─── Mouse ─────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }

        int btnIdx = GetToolbarButtonAt(e.Location);
        if (btnIdx >= 0) { HandleToolbarClick(btnIdx); base.OnMouseDown(e); return; }

        _hasDragged = false;

        switch (_mode)
        {
            case CaptureMode.Rectangle:
                _isSelecting = true;
                _selectionStart = e.Location;
                _selectionEnd = e.Location;
                _hasSelection = false;
                break;
            case CaptureMode.Freeform:
                _isSelecting = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(e.Location);
                break;
            case CaptureMode.Window:
                if (_hoveredWindowRect.Width > 0 && _hoveredWindowRect.Height > 0)
                    RegionSelected?.Invoke(_hoveredWindowRect);
                break;
            case CaptureMode.Fullscreen:
                RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                break;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int btnIdx = GetToolbarButtonAt(e.Location);
        if (btnIdx != _hoveredButton) { _hoveredButton = btnIdx; InvalidateToolbar(); }
        Cursor = btnIdx >= 0 ? Cursors.Hand : Cursors.Cross;

        switch (_mode)
        {
            case CaptureMode.Rectangle when _isSelecting:
                _selectionEnd = e.Location;
                _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);
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
                var newRect = WindowDetector.GetWindowRectAtPoint(e.Location, _virtualBounds);
                if (newRect != _hoveredWindowRect) { _hoveredWindowRect = newRect; Invalidate(); }
                break;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseUp(e); return; }

        switch (_mode)
        {
            case CaptureMode.Rectangle when _isSelecting:
                _isSelecting = false;
                if (!_hasDragged)
                {
                    // Single click with no drag = fullscreen capture
                    RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                }
                else
                {
                    _selectionEnd = e.Location;
                    _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);
                    if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                        RegionSelected?.Invoke(_selectionRect);
                    else { _hasSelection = false; Invalidate(); }
                }
                break;
            case CaptureMode.Freeform when _isSelecting:
                _isSelecting = false;
                if (!_hasDragged)
                    RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                else if (_freeformPoints.Count > 2)
                    CompleteFreeform();
                break;
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Cancel();
        if (e.KeyCode == Keys.D1) SetMode(CaptureMode.Rectangle);
        if (e.KeyCode == Keys.D2) SetMode(CaptureMode.Freeform);
        if (e.KeyCode == Keys.D3) SetMode(CaptureMode.Window);
        if (e.KeyCode == Keys.D4) SetMode(CaptureMode.Fullscreen);
        base.OnKeyDown(e);
    }

    // ─── Toolbar ───────────────────────────────────────────────────

    private int GetToolbarButtonAt(Point p)
    {
        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
        return -1;
    }

    private void HandleToolbarClick(int idx)
    {
        if (idx == 4) { Cancel(); return; }
        SetMode((CaptureMode)idx);
    }

    private void SetMode(CaptureMode mode)
    {
        _mode = mode;
        _hasSelection = false;
        _hasDragged = false;
        _freeformPoints.Clear();
        _hoveredWindowRect = Rectangle.Empty;
        _isSelecting = false;
        Invalidate();
    }

    private void InvalidateToolbar()
    {
        Invalidate(new Rectangle(_toolbarRect.X - 2, _toolbarRect.Y - 32,
            _toolbarRect.Width + 4, _toolbarRect.Height + 36));
    }

    // ─── Freeform ──────────────────────────────────────────────────

    private void CompleteFreeform()
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        var bbox = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bbox.Width < 3 || bbox.Height < 3) return;

        var result = new Bitmap(bbox.Width, bbox.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            var offsetPts = _freeformPoints.Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();
            using var clipPath = new GraphicsPath();
            clipPath.AddPolygon(offsetPts);
            g.SetClip(clipPath);
            g.DrawImage(_screenshot, new Rectangle(0, 0, bbox.Width, bbox.Height), bbox, GraphicsUnit.Pixel);
        }
        FreeformSelected?.Invoke(result);
    }

    private void Cancel() => SelectionCancelled?.Invoke();

    private static Rectangle GetNormalizedRect(Point start, Point end)
    {
        int x = Math.Min(start.X, end.X); int y = Math.Min(start.Y, end.Y);
        return new Rectangle(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _dimmedScreenshot.Dispose(); _animTimer.Dispose(); }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
    }
}
