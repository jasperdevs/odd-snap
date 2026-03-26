using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

/// <summary>
/// Fullscreen overlay form with toolbar for all capture modes.
/// </summary>
public sealed class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmedScreenshot;
    private readonly Rectangle _virtualBounds;

    // Current mode
    private CaptureMode _mode = CaptureMode.Rectangle;

    // Rectangle / Freeform selection state
    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private Rectangle _selectionRect;
    private bool _hasSelection;

    // Freeform path
    private readonly List<Point> _freeformPoints = new();

    // Window capture state
    private Rectangle _hoveredWindowRect;

    // Toolbar state
    private readonly Rectangle[] _toolbarButtons = new Rectangle[5];
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 44;
    private const int ButtonSize = 36;
    private const int ButtonSpacing = 4;
    private const int ToolbarTopMargin = 16;

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
        SetupForm();
        CalculateToolbarLayout();
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
        int totalButtonsWidth = ButtonSize * 5 + ButtonSpacing * 4;
        int toolbarWidth = totalButtonsWidth + 16;
        int toolbarX = (ClientSize.Width - toolbarWidth) / 2;
        _toolbarRect = new Rectangle(toolbarX, ToolbarTopMargin, toolbarWidth, ToolbarHeight);

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
        g.DrawImage(_dimmedScreenshot, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;

        switch (_mode)
        {
            case CaptureMode.Rectangle:
                PaintRectangleSelection(g);
                break;
            case CaptureMode.Freeform:
                PaintFreeformSelection(g);
                break;
            case CaptureMode.Window:
                PaintWindowHighlight(g);
                break;
            case CaptureMode.Fullscreen:
                // Just dimmed, user clicks to capture all
                break;
        }

        PaintToolbar(g);
    }

    private void PaintRectangleSelection(Graphics g)
    {
        if (!_hasSelection) return;

        // Bright original inside selection
        g.DrawImage(_screenshot, _selectionRect, _selectionRect, GraphicsUnit.Pixel);

        // White stroke
        using var pen = new Pen(Color.White, 2f);
        g.DrawRectangle(pen, _selectionRect);

        DrawDimensionLabel(g, _selectionRect);
    }

    private void PaintFreeformSelection(Graphics g)
    {
        if (_freeformPoints.Count < 2) return;

        // Draw the freeform path outline
        using var pen = new Pen(Color.White, 2f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(pen, _freeformPoints.ToArray());

        // If we have enough points and are not actively selecting, fill it
        if (!_isSelecting && _freeformPoints.Count > 2)
        {
            // Close the path
            g.DrawLine(pen, _freeformPoints[^1], _freeformPoints[0]);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintWindowHighlight(Graphics g)
    {
        if (_hoveredWindowRect.Width <= 0 || _hoveredWindowRect.Height <= 0) return;

        // Show original screenshot in hovered window area
        var clipped = Rectangle.Intersect(_hoveredWindowRect,
            new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        if (clipped.Width > 0 && clipped.Height > 0)
        {
            g.DrawImage(_screenshot, clipped, clipped, GraphicsUnit.Pixel);
        }

        using var pen = new Pen(Color.FromArgb(200, 0, 120, 215), 3f);
        g.DrawRectangle(pen, _hoveredWindowRect);
    }

    private void PaintToolbar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Frosted glass pill background
        using var bgPath = GetRoundedRect(_toolbarRect, 8);
        using var bgBrush = new SolidBrush(Color.FromArgb(200, 32, 32, 32));
        g.FillPath(bgBrush, bgPath);
        using var borderPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
        g.DrawPath(borderPen, bgPath);

        // Draw each button
        string[] icons = { "rect", "free", "win", "full", "close" };
        for (int i = 0; i < 5; i++)
        {
            var btn = _toolbarButtons[i];
            bool isActive = i < 4 && (int)_mode == i;
            bool isHovered = _hoveredButton == i;

            // Button background on hover/active
            if (isActive || isHovered)
            {
                using var btnPath = GetRoundedRect(btn, 6);
                using var btnBrush = new SolidBrush(
                    isActive ? Color.FromArgb(80, 255, 255, 255) :
                    Color.FromArgb(40, 255, 255, 255));
                g.FillPath(btnBrush, btnPath);
            }

            // Draw icon
            var iconColor = (i == 4) ? Color.FromArgb(200, 255, 255, 255) : Color.White;
            DrawToolbarIcon(g, icons[i], btn, iconColor);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawToolbarIcon(Graphics g, string icon, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        int cx = bounds.X + bounds.Width / 2;
        int cy = bounds.Y + bounds.Height / 2;
        int s = 8; // half-size of icon area

        switch (icon)
        {
            case "rect":
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4);
                break;
            case "free":
                // Curved freeform line
                g.DrawBezier(pen,
                    cx - s, cy + s - 4,
                    cx - s + 4, cy - s,
                    cx + s - 4, cy + s - 2,
                    cx + s, cy - s + 4);
                break;
            case "win":
                // Window icon: rectangle with title bar
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2, s * 2 - 4);
                g.DrawLine(pen, cx - s, cy - s + 7, cx + s, cy - s + 7);
                break;
            case "full":
                // Monitor/fullscreen icon
                g.DrawRectangle(pen, cx - s, cy - s + 1, s * 2, s * 2 - 5);
                g.DrawLine(pen, cx - 4, cy + s - 2, cx + 4, cy + s - 2); // stand
                break;
            case "close":
                // X icon
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
        float ly = rect.Bottom + 6;
        if (ly + size.Height > ClientSize.Height)
            ly = rect.Y - size.Height - 6;

        var labelRect = new RectangleF(lx - 4, ly - 2, size.Width + 8, size.Height + 4);
        using var bg = new SolidBrush(Color.FromArgb(200, 30, 30, 30));
        using var fg = new SolidBrush(Color.White);
        using var path = GetRoundedRect(labelRect, 4);
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

    // ─── Mouse handling ────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }

        // Check toolbar click first
        int btnIdx = GetToolbarButtonAt(e.Location);
        if (btnIdx >= 0)
        {
            HandleToolbarClick(btnIdx);
            base.OnMouseDown(e);
            return;
        }

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
        // Toolbar hover
        int btnIdx = GetToolbarButtonAt(e.Location);
        if (btnIdx != _hoveredButton)
        {
            _hoveredButton = btnIdx;
            InvalidateToolbar();
        }

        // Update cursor
        Cursor = btnIdx >= 0 ? Cursors.Hand : Cursors.Cross;

        switch (_mode)
        {
            case CaptureMode.Rectangle when _isSelecting:
                _selectionEnd = e.Location;
                _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);
                _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
                Invalidate();
                break;

            case CaptureMode.Freeform when _isSelecting:
                _freeformPoints.Add(e.Location);
                Invalidate();
                break;

            case CaptureMode.Window:
                var newRect = WindowDetector.GetWindowRectAtPoint(e.Location, _virtualBounds);
                if (newRect != _hoveredWindowRect)
                {
                    _hoveredWindowRect = newRect;
                    Invalidate();
                }
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
                _selectionEnd = e.Location;
                _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);
                if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                    RegionSelected?.Invoke(_selectionRect);
                else
                {
                    _hasSelection = false;
                    Invalidate();
                }
                break;

            case CaptureMode.Freeform when _isSelecting:
                _isSelecting = false;
                if (_freeformPoints.Count > 2)
                    CompleteFreeform();
                break;
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Cancel();
        // Mode shortcuts: 1-4
        if (e.KeyCode == Keys.D1) SetMode(CaptureMode.Rectangle);
        if (e.KeyCode == Keys.D2) SetMode(CaptureMode.Freeform);
        if (e.KeyCode == Keys.D3) SetMode(CaptureMode.Window);
        if (e.KeyCode == Keys.D4) SetMode(CaptureMode.Fullscreen);
        base.OnKeyDown(e);
    }

    // ─── Toolbar logic ─────────────────────────────────────────────

    private int GetToolbarButtonAt(Point p)
    {
        for (int i = 0; i < _toolbarButtons.Length; i++)
            if (_toolbarButtons[i].Contains(p)) return i;
        return -1;
    }

    private void HandleToolbarClick(int index)
    {
        if (index == 4) { Cancel(); return; }
        SetMode((CaptureMode)index);
    }

    private void SetMode(CaptureMode mode)
    {
        _mode = mode;
        _hasSelection = false;
        _freeformPoints.Clear();
        _hoveredWindowRect = Rectangle.Empty;
        _isSelecting = false;
        Invalidate();
    }

    private void InvalidateToolbar()
    {
        Invalidate(new Rectangle(_toolbarRect.X - 2, _toolbarRect.Y - 2,
            _toolbarRect.Width + 4, _toolbarRect.Height + 4));
    }

    // ─── Freeform completion ───────────────────────────────────────

    private void CompleteFreeform()
    {
        // Get bounding box of the freeform path
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        var bbox = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bbox.Width < 3 || bbox.Height < 3) return;

        // Create a bitmap with just the freeform area, transparent outside
        var result = new Bitmap(bbox.Width, bbox.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            // Create clip region from freeform points
            var offsetPoints = _freeformPoints
                .Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();

            using var clipPath = new GraphicsPath();
            clipPath.AddPolygon(offsetPoints);
            g.SetClip(clipPath);

            // Draw the original screenshot into the clipped area
            g.DrawImage(_screenshot,
                new Rectangle(0, 0, bbox.Width, bbox.Height),
                bbox, GraphicsUnit.Pixel);
        }

        FreeformSelected?.Invoke(result);
    }

    private void Cancel() => SelectionCancelled?.Invoke();

    private static Rectangle GetNormalizedRect(Point start, Point end)
    {
        int x = Math.Min(start.X, end.X);
        int y = Math.Min(start.Y, end.Y);
        return new Rectangle(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dimmedScreenshot.Dispose();
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }
}
