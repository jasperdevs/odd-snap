using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly int[] _pixelData;
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

    // Toolbar
    // rect, freeform, OCR, colorpicker, draw, arrow, blur, eraser, settings, close
    private const int BtnCount = 10;
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

    // Pre-rendered blurred screenshot used for all glass effects
    private readonly Bitmap _blurred;
    private const int TopBarHeight = 110;

    // Color picker state
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
    private const int MagOff = 12, MagMargin = 4;

    // Draw / Blur / Arrow state
    private readonly List<List<Point>> _drawStrokes = new();
    private List<Point>? _currentStroke;
    private readonly List<Rectangle> _blurRects = new();
    private Point _blurStart;
    private bool _isBlurring;

    private readonly List<(Point from, Point to)> _arrows = new();
    private Point _arrowStart;
    private bool _isArrowDragging;

    // Smart eraser: filled rects sampled from the screenshot
    private readonly List<(Rectangle rect, Color color)> _eraserFills = new();
    private Point _eraserStart;
    private Color _eraserColor;
    private bool _isEraserDragging;

    // Undo stack: "draw", "blur", "arrow", "eraser"
    private readonly List<string> _undoStack = new();

    // Blank cursor for color picker (we draw our own crosshair)
    private static readonly Cursor _blankCursor = CreateBlankCursor();

    // Events
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
        _blurred = BuildBlurred(screenshot);

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            if (_toolbarAnim >= 1f) { _animTimer.Stop(); return; }
            _toolbarAnim = Math.Min(1f, (float)(DateTime.UtcNow - _showTime).TotalMilliseconds / 120f);
            Invalidate(_toolbarRect);
        };
        _animTimer.Start();

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
        Invalidate();
    }

    private void CalcToolbar()
    {
        int pad = 4;
        int w = ButtonSize * BtnCount + ButtonSpacing * (BtnCount - 1) + pad * 2;
        int x = (ClientSize.Width - w) / 2;
        _toolbarRect = new Rectangle(x, ToolbarTopMargin, w, ToolbarHeight);
        for (int i = 0; i < BtnCount; i++)
            _toolbarButtons[i] = new Rectangle(
                _toolbarRect.X + pad + i * (ButtonSize + ButtonSpacing),
                _toolbarRect.Y + (ToolbarHeight - ButtonSize) / 2,
                ButtonSize, ButtonSize);
    }

    // Builds a heavily blurred copy of the entire screenshot (reused for all glass effects).
    private static Bitmap BuildBlurred(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        // 3-pass downsample/upsample at 1/32 for extreme blur
        Bitmap cur = src;
        for (int pass = 0; pass < 3; pass++)
        {
            int tw = Math.Max(2, w / 32);
            int th = Math.Max(2, h / 32);
            var tiny = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
            using (var tg = Graphics.FromImage(tiny))
            {
                tg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                tg.DrawImage(cur, new Rectangle(0, 0, tw, th),
                    new Rectangle(0, 0, cur.Width, cur.Height), GraphicsUnit.Pixel);
            }
            var up = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var ug = Graphics.FromImage(up))
            {
                ug.InterpolationMode = InterpolationMode.HighQualityBilinear;
                ug.DrawImage(tiny, new Rectangle(0, 0, w, h));
            }
            tiny.Dispose();
            if (pass > 0) cur.Dispose();
            cur = up;
        }
        return cur;
    }

    private static Cursor CreateBlankCursor()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        return new Cursor(bmp.GetHicon());
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
            _blurred.Dispose();
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
