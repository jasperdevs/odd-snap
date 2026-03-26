using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Bitmap _dimmed;
    private readonly int[] _pixelData;   // cached pixels for color picker
    private readonly int _bmpW, _bmpH;
    private readonly Rectangle _virtualBounds;

    private CaptureMode _mode = CaptureMode.Rectangle;
    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private Rectangle _selectionRect;
    private bool _hasSelection;
    private bool _hasDragged;

    private readonly List<Point> _freeformPoints = new();

    // ─── Toolbar ───────────────────────────────────────────────────
    // rect, freeform, fullscreen, OCR, colorpicker, draw, blur, settings, close
    private const int BtnCount = 9;
    private readonly Rectangle[] _toolbarButtons = new Rectangle[BtnCount];
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 44;
    private const int ButtonSize = 36;
    private const int ButtonSpacing = 4;
    private const int ToolbarTopMargin = 16;

    private float _toolbarAnim;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly DateTime _showTime;

    // ─── Color picker state ────────────────────────────────────────
    private readonly Bitmap _magBitmap;
    private readonly int[] _magPixels;
    private readonly Graphics _magGfx;
    private readonly Font _hexFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _rgbFont = new("Segoe UI", 9f);
    private readonly SolidBrush _mutedBrush = new(Color.FromArgb(140, 255, 255, 255));
    private readonly Pen _crossPen = new(Color.FromArgb(210, 255, 255, 255), 1f);
    private Point _pickerCursorPos;
    private Rectangle _pickerPrevDirty;
    private Color _pickedColor = Color.Black;
    private string _hexStr = "000000";
    private string _rgbStr = "0, 0, 0";
    private readonly System.Windows.Forms.Timer _pickerTimer;

    private const int Grid = 9, Cell = 14, Mag = Grid * Cell;
    private const int InfoH = 48, PPad = 10;
    private const int PW = Mag + PPad * 2, PH = Mag + InfoH + PPad * 2;
    private const int MagOff = 22, MagMargin = 4;

    // ─── Draw / Blur state ─────────────────────────────────────────
    private readonly List<List<Point>> _drawStrokes = new();  // each stroke is a list of points
    private List<Point>? _currentStroke;
    private readonly List<Rectangle> _blurRects = new();
    private Point _blurStart;
    private bool _isBlurring;

    // Undo stack: "draw" or "blur"
    private readonly List<string> _undoStack = new();

    // ─── Events ────────────────────────────────────────────────────
    public event Action<Rectangle>? RegionSelected;
    public event Action<Rectangle>? OcrRegionSelected;
    public event Action<Bitmap>? FreeformSelected;
    public event Action<Bitmap>? OcrFreeformSelected;
    public event Action<string>? ColorPicked;
    public event Action? SelectionCancelled;
    public event Action? SettingsRequested;

    public RegionOverlayForm(Bitmap screenshot, Rectangle virtualBounds,
        CaptureMode initialMode = CaptureMode.Rectangle)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _bmpW = screenshot.Width;
        _bmpH = screenshot.Height;
        _mode = initialMode;
        _dimmed = CreateDimmed(screenshot);
        _showTime = DateTime.UtcNow;

        // Cache pixels for color picker
        _pixelData = new int[_bmpW * _bmpH];
        var bits = screenshot.LockBits(new Rectangle(0, 0, _bmpW, _bmpH),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bits.Scan0, _pixelData, 0, _pixelData.Length);
        screenshot.UnlockBits(bits);

        // Magnifier bitmap for color picker
        _magBitmap = new Bitmap(PW, PH, PixelFormat.Format32bppArgb);
        _magPixels = new int[PW * PH];
        _magGfx = Graphics.FromImage(_magBitmap);
        _magGfx.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        SetupForm();
        CalcToolbar();

        _animTimer = new System.Windows.Forms.Timer { Interval = 12 };
        _animTimer.Tick += (_, _) =>
        {
            _toolbarAnim = Math.Min(1f, (float)(DateTime.UtcNow - _showTime).TotalMilliseconds / 180f);
            if (_toolbarAnim >= 1f) _animTimer.Stop();
            Invalidate(); // full repaint during short animation to avoid partial artifacts
        };
        _animTimer.Start();

        // Color picker cursor tracking timer (only active in ColorPicker mode)
        _pickerTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _pickerTimer.Tick += OnPickerTick;
        if (_mode == CaptureMode.ColorPicker) _pickerTimer.Start();
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
        Native.User32.SetWindowPos(Handle, Native.User32.HWND_TOPMOST,
            0, 0, 0, 0,
            Native.User32.SWP_NOMOVE | Native.User32.SWP_NOSIZE | Native.User32.SWP_SHOWWINDOW);
        Native.User32.SetForegroundWindow(Handle);
        Invalidate(); // full repaint so no black regions remain
    }

    private void CalcToolbar()
    {
        int w = ButtonSize * BtnCount + ButtonSpacing * (BtnCount - 1) + 16;
        int x = (ClientSize.Width - w) / 2;
        _toolbarRect = new Rectangle(x, ToolbarTopMargin, w, ToolbarHeight);
        for (int i = 0; i < BtnCount; i++)
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

    // ─── Paint ─────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Background: raw screenshot for color picker, dimmed for everything else.
        // Always draw full background to avoid black regions from partial invalidation.
        var bgBmp = _mode == CaptureMode.ColorPicker ? _screenshot : _dimmed;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(bgBmp, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;

        if (_mode == CaptureMode.ColorPicker)
        {
            PaintToolbar(g);
            PaintMagnifier(g);
            return;
        }

        bool isOcr = _mode == CaptureMode.Ocr;

        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
            case CaptureMode.Ocr when _hasSelection:
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_screenshot, _selectionRect, _selectionRect, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var pen = new Pen(isOcr ? Color.FromArgb(100, 180, 255) : Color.White, 2f))
                    g.DrawRectangle(pen, _selectionRect);
                DrawLabel(g, _selectionRect, isOcr);
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
        }

        // Draw annotations (always visible across modes)
        if (_drawStrokes.Count > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var drawPen = new Pen(Color.Red, 3f) { LineJoin = LineJoin.Round };
            foreach (var stroke in _drawStrokes)
                if (stroke.Count >= 2)
                    g.DrawLines(drawPen, stroke.ToArray());
            g.SmoothingMode = SmoothingMode.Default;
        }

        // Blur rects (pixelate the region)
        foreach (var br in _blurRects)
            PaintBlurRect(g, br);

        // Active blur preview
        if (_mode == CaptureMode.Blur && _isBlurring)
        {
            var previewRect = NormRect(_blurStart, PointToClient(System.Windows.Forms.Cursor.Position));
            if (previewRect.Width > 2 && previewRect.Height > 2)
            {
                using var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, previewRect);
            }
        }

        PaintToolbar(g);
    }

    // ─── Toolbar ───────────────────────────────────────────────────

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

        // 0=rect, 1=freeform, 2=fullscreen, 3=OCR, 4=colorpicker, 5=draw, 6=blur, 7=settings, 8=close
        string[] icons = { "rect", "free", "full", "ocr", "picker", "draw", "blur", "gear", "close" };
        CaptureMode[] modes = { CaptureMode.Rectangle, CaptureMode.Freeform,
            CaptureMode.Fullscreen, CaptureMode.Ocr, CaptureMode.ColorPicker,
            CaptureMode.Draw, CaptureMode.Blur };

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = new Rectangle(_toolbarButtons[i].X, _toolbarButtons[i].Y + oy,
                ButtonSize, ButtonSize);
            bool active = i < 7 && _mode == modes[i];
            bool hover = _hoveredButton == i;
            if (active || hover)
                using (var p = RRect(btn, 6))
                using (var b = new SolidBrush(Color.FromArgb((int)(t * (active ? 80 : 40)), 255, 255, 255)))
                    g.FillPath(b, p);
            int ia = (int)(t * (i >= BtnCount - 2 ? 200 : 255));
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
            case "full":
                g.DrawRectangle(pen, cx - s, cy - s + 1, s * 2, s * 2 - 5);
                g.DrawLine(pen, cx - 4, cy + s - 2, cx + 4, cy + s - 2); break;
            case "ocr":
                // "T" icon for text
                g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy - 6);
                g.DrawLine(pen, cx, cy - 6, cx, cy + 7); break;
            case "picker":
                // Eyedropper: small circle + angled line
                g.DrawEllipse(pen, cx - 4, cy - 7, 8, 8);
                g.DrawLine(pen, cx, cy + 1, cx, cy + 7); break;
            case "draw":
                // Pen/pencil: diagonal line with small tip
                g.DrawLine(pen, cx - 6, cy + 6, cx + 5, cy - 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx + 7, cy - 7); break;
            case "blur":
                // Blur: three horizontal wavy lines
                g.DrawLine(pen, cx - 6, cy - 4, cx + 6, cy - 4);
                g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                g.DrawLine(pen, cx - 6, cy + 4, cx + 6, cy + 4); break;
            case "gear":
                // Settings gear: circle with notches
                g.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);
                g.DrawLine(pen, cx, cy - 7, cx, cy - 4);
                g.DrawLine(pen, cx, cy + 4, cx, cy + 7);
                g.DrawLine(pen, cx - 7, cy, cx - 4, cy);
                g.DrawLine(pen, cx + 4, cy, cx + 7, cy); break;
            case "close":
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5); break;
        }
    }

    private void DrawLabel(Graphics g, Rectangle rect, bool isOcr)
    {
        string text = isOcr ? $"OCR  {rect.Width} x {rect.Height}" : $"{rect.Width} x {rect.Height}";
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

    // ─── Color picker magnifier ────────────────────────────────────

    private void OnPickerTick(object? sender, EventArgs e)
    {
        Native.User32.GetCursorPos(out var pt);
        var np = new Point(pt.X - _virtualBounds.X, pt.Y - _virtualBounds.Y);
        if (np == _pickerCursorPos) return;
        _pickerCursorPos = np;
        BuildMagnifier();

        var newDirty = PickerDirtyRect(_pickerCursorPos);
        if (!_pickerPrevDirty.IsEmpty) Invalidate(_pickerPrevDirty);
        Invalidate(newDirty);
        _pickerPrevDirty = newDirty;
    }

    private void BuildMagnifier()
    {
        int cx = Math.Clamp(_pickerCursorPos.X, 0, _bmpW - 1);
        int cy = Math.Clamp(_pickerCursorPos.Y, 0, _bmpH - 1);
        int argb = _pixelData[cy * _bmpW + cx];
        _pickedColor = Color.FromArgb(argb);
        _hexStr = $"{_pickedColor.R:X2}{_pickedColor.G:X2}{_pickedColor.B:X2}";
        _rgbStr = $"{_pickedColor.R}, {_pickedColor.G}, {_pickedColor.B}";

        const int bg = unchecked((int)0xF5161616);
        Array.Fill(_magPixels, bg);

        int half = Grid / 2;
        for (int gy = 0; gy < Grid; gy++)
        {
            int sy = cy - half + gy;
            for (int gx = 0; gx < Grid; gx++)
            {
                int sx = cx - half + gx;
                int c = ((uint)sx < (uint)_bmpW && (uint)sy < (uint)_bmpH)
                    ? _pixelData[sy * _bmpW + sx] : unchecked((int)0xFF000000);

                int ox = PPad + gx * Cell;
                int oy = PPad + gy * Cell;
                for (int py = 0; py < Cell - 1; py++)
                {
                    int row = (oy + py) * PW + ox;
                    for (int px = 0; px < Cell - 1; px++)
                        _magPixels[row + px] = c;
                    _magPixels[row + Cell - 1] = Lighten(c, 20);
                }
                int bot = (oy + Cell - 1) * PW + ox;
                int gl = Lighten(c, 20);
                for (int px = 0; px < Cell; px++)
                    _magPixels[bot + px] = gl;
            }
        }

        // Center pixel border
        int bx = PPad + half * Cell, by = PPad + half * Cell;
        const int w = unchecked((int)0xFFFFFFFF);
        for (int i = -1; i <= Cell; i++)
        {
            SetMagPx(bx + i, by - 1, w); SetMagPx(bx + i, by + Cell, w);
            SetMagPx(bx - 1, by + i, w); SetMagPx(bx + Cell, by + i, w);
        }

        // Color swatch
        int swY = PPad + Mag + 8;
        int swArgb = _pickedColor.ToArgb();
        for (int py = 0; py < 26; py++)
        {
            int row = (swY + py) * PW + PPad;
            for (int px = 0; px < 26; px++)
                _magPixels[row + px] = swArgb;
        }

        var bitsLock = _magBitmap.LockBits(new Rectangle(0, 0, PW, PH),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(_magPixels, 0, bitsLock.Scan0, _magPixels.Length);
        _magBitmap.UnlockBits(bitsLock);

        int ty = PPad + Mag + 8;
        _magGfx.DrawString(_hexStr, _hexFont, Brushes.White, PPad + 32, ty - 2);
        _magGfx.DrawString(_rgbStr, _rgbFont, _mutedBrush, PPad + 32, ty + 15);
    }

    private void PaintMagnifier(Graphics g)
    {
        var (px, py) = MagPos(_pickerCursorPos);
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImageUnscaled(_magBitmap, px, py);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RRect(new Rectangle(px, py, PW, PH), 10);
        using var pen = new Pen(Color.FromArgb(45, 255, 255, 255));
        g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;

        int mx = _pickerCursorPos.X, my = _pickerCursorPos.Y;
        g.DrawLine(_crossPen, mx - 10, my, mx - 3, my);
        g.DrawLine(_crossPen, mx + 3, my, mx + 10, my);
        g.DrawLine(_crossPen, mx, my - 10, mx, my - 3);
        g.DrawLine(_crossPen, mx, my + 3, mx, my + 10);
    }

    private void SetMagPx(int x, int y, int v)
    {
        if ((uint)x < (uint)PW && (uint)y < (uint)PH)
            _magPixels[y * PW + x] = v;
    }

    private static int Lighten(int c, int amt)
    {
        int r = Math.Min(((c >> 16) & 0xFF) + amt, 255);
        int gg = Math.Min(((c >> 8) & 0xFF) + amt, 255);
        int b = Math.Min((c & 0xFF) + amt, 255);
        return unchecked((int)0xFF000000) | (r << 16) | (gg << 8) | b;
    }

    private (int, int) MagPos(Point c)
    {
        int px = c.X + MagOff, py = c.Y + MagOff;
        if (px + PW > ClientSize.Width) px = c.X - MagOff - PW;
        if (py + PH > ClientSize.Height) py = c.Y - MagOff - PH;
        return (Math.Max(4, px), Math.Max(4, py));
    }

    private Rectangle PickerDirtyRect(Point cur)
    {
        var (px, py) = MagPos(cur);
        int left = Math.Min(px, cur.X - 14) - MagMargin;
        int top = Math.Min(py, cur.Y - 14) - MagMargin;
        int right = Math.Max(px + PW, cur.X + 14) + MagMargin;
        int bottom = Math.Max(py + PH, cur.Y + 14) + MagMargin;
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private void PaintBlurRect(Graphics g, Rectangle rect)
    {
        // Pixelate effect: downsample then upsample
        int blockSize = Math.Max(6, Math.Min(rect.Width, rect.Height) / 8);
        if (rect.Width < 3 || rect.Height < 3) return;

        var clamped = Rectangle.Intersect(rect, new Rectangle(0, 0, _bmpW, _bmpH));
        if (clamped.Width < 1 || clamped.Height < 1) return;

        int smallW = Math.Max(1, clamped.Width / blockSize);
        int smallH = Math.Max(1, clamped.Height / blockSize);

        using var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.Bilinear;
            sg.DrawImage(_screenshot, new Rectangle(0, 0, smallW, smallH), clamped, GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, clamped);
        g.InterpolationMode = InterpolationMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }

    // ─── Mouse ─────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;

        int btn = GetToolbarButtonAt(e.Location);
        if (btn >= 0)
        {
            if (btn == BtnCount - 1) { Cancel(); return; }       // close
            if (btn == BtnCount - 2) { SettingsRequested?.Invoke(); Cancel(); return; } // settings
            var modeMap = new[] { CaptureMode.Rectangle, CaptureMode.Freeform,
                CaptureMode.Fullscreen, CaptureMode.Ocr, CaptureMode.ColorPicker,
                CaptureMode.Draw, CaptureMode.Blur };
            SetMode(modeMap[btn]);
            return;
        }

        if (_mode == CaptureMode.ColorPicker)
        {
            ColorPicked?.Invoke(_hexStr);
            return;
        }

        _hasDragged = false;
        switch (_mode)
        {
            case CaptureMode.Rectangle:
            case CaptureMode.Ocr:
                _isSelecting = true;
                _selectionStart = _selectionEnd = e.Location;
                _hasSelection = false;
                break;
            case CaptureMode.Freeform:
                _isSelecting = true;
                _freeformPoints.Clear();
                _freeformPoints.Add(e.Location);
                break;
            case CaptureMode.Fullscreen:
                RegionSelected?.Invoke(new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
                break;
            case CaptureMode.Draw:
                _isSelecting = true;
                _currentStroke = new List<Point> { e.Location };
                _drawStrokes.Add(_currentStroke);
                break;
            case CaptureMode.Blur:
                _isBlurring = true;
                _blurStart = e.Location;
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
            case CaptureMode.Ocr when _isSelecting:
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
            case CaptureMode.Draw when _isSelecting:
                _currentStroke?.Add(e.Location);
                Invalidate();
                break;
            case CaptureMode.Blur when _isBlurring:
                Invalidate();
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        switch (_mode)
        {
            case CaptureMode.Draw when _isSelecting:
                _isSelecting = false;
                _currentStroke = null;
                _undoStack.Add("draw");
                break;
            case CaptureMode.Blur when _isBlurring:
                _isBlurring = false;
                var blurRect = NormRect(_blurStart, e.Location);
                if (blurRect.Width > 3 && blurRect.Height > 3)
                {
                    _blurRects.Add(blurRect);
                    _undoStack.Add("blur");
                }
                Invalidate();
                break;
            case CaptureMode.Rectangle when _isSelecting:
            case CaptureMode.Ocr when _isSelecting:
                _isSelecting = false;
                bool isOcr = _mode == CaptureMode.Ocr;
                if (!_hasDragged)
                {
                    var fullRect = new Rectangle(0, 0, _screenshot.Width, _screenshot.Height);
                    if (isOcr) OcrRegionSelected?.Invoke(fullRect);
                    else RegionSelected?.Invoke(fullRect);
                }
                else if (_selectionRect.Width > 2 && _selectionRect.Height > 2)
                {
                    if (isOcr) OcrRegionSelected?.Invoke(_selectionRect);
                    else RegionSelected?.Invoke(_selectionRect);
                }
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
        if (e.KeyCode == Keys.D4) SetMode(CaptureMode.Ocr);
        if (e.KeyCode == Keys.D5) SetMode(CaptureMode.ColorPicker);
        if (e.KeyCode == Keys.D6) SetMode(CaptureMode.Draw);
        if (e.KeyCode == Keys.D7) SetMode(CaptureMode.Blur);

        // Ctrl+Z: undo last draw stroke or blur rect
        if (e.KeyCode == Keys.Z && e.Control && _undoStack.Count > 0)
        {
            var last = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (last == "draw" && _drawStrokes.Count > 0)
                _drawStrokes.RemoveAt(_drawStrokes.Count - 1);
            else if (last == "blur" && _blurRects.Count > 0)
                _blurRects.RemoveAt(_blurRects.Count - 1);
            Invalidate();
        }
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
        _freeformPoints.Clear();
        _isSelecting = false; _isBlurring = false;

        if (m == CaptureMode.ColorPicker)
            _pickerTimer.Start();
        else
            _pickerTimer.Stop();

        Invalidate();
    }

    private void CompleteFreeform()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in _freeformPoints)
        { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        var bb = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        if (bb.Width < 3 || bb.Height < 3) return;
        var annotated = RenderAnnotatedBitmap();
        var r = new Bitmap(bb.Width, bb.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(r))
        {
            var pts = _freeformPoints.Select(p => new Point(p.X - minX, p.Y - minY)).ToArray();
            using var cp = new GraphicsPath(); cp.AddPolygon(pts); g.SetClip(cp);
            g.DrawImage(annotated, new Rectangle(0, 0, bb.Width, bb.Height), bb, GraphicsUnit.Pixel);
        }
        annotated.Dispose();

        if (_mode == CaptureMode.Ocr)
            OcrFreeformSelected?.Invoke(r);
        else
            FreeformSelected?.Invoke(r);
    }

    /// <summary>Renders the screenshot with draw/blur annotations baked in.</summary>
    public Bitmap RenderAnnotatedBitmap()
    {
        var result = new Bitmap(_screenshot);
        using var g = Graphics.FromImage(result);

        // Blur rects
        foreach (var br in _blurRects)
            PaintBlurRect(g, br);

        // Draw strokes
        if (_drawStrokes.Count > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.Red, 3f) { LineJoin = LineJoin.Round };
            foreach (var stroke in _drawStrokes)
                if (stroke.Count >= 2)
                    g.DrawLines(pen, stroke.ToArray());
            g.SmoothingMode = SmoothingMode.Default;
        }

        return result;
    }

    private void Cancel() => SelectionCancelled?.Invoke();
    private static Rectangle NormRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
            _animTimer.Dispose();
            _pickerTimer.Dispose();
            _magGfx.Dispose();
            _magBitmap.Dispose();
            _hexFont.Dispose();
            _rgbFont.Dispose();
            _mutedBrush.Dispose();
            _crossPen.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
}
