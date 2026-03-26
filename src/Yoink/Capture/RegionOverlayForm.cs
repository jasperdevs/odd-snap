using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Yoink.Capture;

/// <summary>
/// Fullscreen borderless form that displays the frozen screenshot with a dark overlay
/// and lets the user select a rectangular region.
/// </summary>
public sealed class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmedScreenshot;
    private readonly Rectangle _virtualBounds;

    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private Rectangle _selectionRect;
    private bool _hasSelection;

    /// <summary>
    /// Fires when the user completes a region selection.
    /// The rectangle is in bitmap-local coordinates (0,0 = top-left of captured area).
    /// </summary>
    public event Action<Rectangle>? RegionSelected;

    /// <summary>
    /// Fires when the user cancels (ESC or right-click).
    /// </summary>
    public event Action? SelectionCancelled;

    public RegionOverlayForm(Bitmap screenshot, Rectangle virtualBounds)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;

        // Pre-render the dimmed version for performance
        _dimmedScreenshot = CreateDimmedScreenshot(screenshot);

        SetupForm();
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

        // Double buffering for flicker-free rendering
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        KeyPreview = true;
    }

    private static Bitmap CreateDimmedScreenshot(Bitmap original)
    {
        var dimmed = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dimmed);
        g.DrawImage(original, 0, 0);

        // Dark overlay at 40% opacity
        using var overlay = new SolidBrush(Color.FromArgb(102, 0, 0, 0));
        g.FillRectangle(overlay, 0, 0, dimmed.Width, dimmed.Height);

        return dimmed;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        if (!_hasSelection)
        {
            // Just draw the dimmed screenshot
            g.DrawImage(_dimmedScreenshot, 0, 0);
            return;
        }

        // Draw the dimmed screenshot as the base
        g.DrawImage(_dimmedScreenshot, 0, 0);

        // Draw the original (un-dimmed) screenshot inside the selection rect
        // This makes the selected area appear "bright" while the rest is dark
        g.CompositingMode = CompositingMode.SourceOver;
        var srcRect = _selectionRect;
        g.DrawImage(_screenshot, _selectionRect, srcRect, GraphicsUnit.Pixel);

        // White stroke around selection (2px, UI only)
        using var strokePen = new Pen(Color.White, 2f);
        g.DrawRectangle(strokePen, _selectionRect);

        // Draw selection dimensions label
        DrawDimensionLabel(g);
    }

    private void DrawDimensionLabel(Graphics g)
    {
        string text = $"{_selectionRect.Width} x {_selectionRect.Height}";
        using var font = new Font("Segoe UI", 11f, FontStyle.Regular);
        var textSize = g.MeasureString(text, font);

        // Position below the selection rectangle
        float labelX = _selectionRect.X;
        float labelY = _selectionRect.Bottom + 6;

        // If it would go off-screen, put it above instead
        if (labelY + textSize.Height > ClientSize.Height)
            labelY = _selectionRect.Y - textSize.Height - 6;

        // Background pill for the label
        var labelRect = new RectangleF(
            labelX - 4, labelY - 2,
            textSize.Width + 8, textSize.Height + 4);

        using var bgBrush = new SolidBrush(Color.FromArgb(200, 30, 30, 30));
        using var textBrush = new SolidBrush(Color.White);
        using var roundPath = GetRoundedRect(labelRect, 4);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(bgBrush, roundPath);
        g.SmoothingMode = SmoothingMode.Default;

        g.DrawString(text, font, textBrush, labelX, labelY);
    }

    private static GraphicsPath GetRoundedRect(RectangleF rect, float radius)
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isSelecting = true;
            _selectionStart = e.Location;
            _selectionEnd = e.Location;
            _hasSelection = false;
        }
        else if (e.Button == MouseButtons.Right)
        {
            Cancel();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isSelecting)
        {
            _selectionEnd = e.Location;
            _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);
            _hasSelection = _selectionRect.Width > 2 && _selectionRect.Height > 2;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _isSelecting)
        {
            _isSelecting = false;
            _selectionEnd = e.Location;
            _selectionRect = GetNormalizedRect(_selectionStart, _selectionEnd);

            if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
            {
                // Form coordinates map 1:1 to bitmap coordinates
                RegionSelected?.Invoke(_selectionRect);
            }
            else
            {
                // Selection too small, reset
                _hasSelection = false;
                Invalidate();
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Cancel();
        }

        base.OnKeyDown(e);
    }

    private void Cancel()
    {
        SelectionCancelled?.Invoke();
    }

    private static Rectangle GetNormalizedRect(Point start, Point end)
    {
        int x = Math.Min(start.X, end.X);
        int y = Math.Min(start.Y, end.Y);
        int w = Math.Abs(end.X - start.X);
        int h = Math.Abs(end.Y - start.Y);
        return new Rectangle(x, y, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmedScreenshot.Dispose();
        }

        base.Dispose(disposing);
    }

    // Prevent the form from showing in Alt+Tab
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
