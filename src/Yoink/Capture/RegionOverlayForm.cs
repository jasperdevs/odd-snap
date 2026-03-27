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

    // Toolbar: rect, free, OCR, picker, draw, highlight, arrow, curvedArrow, text, step, blur, eraser, magnifier, emoji, [color], gear, close
    private const int BtnCount = 17;
    private readonly Rectangle[] _toolbarButtons = new Rectangle[BtnCount];
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 44;
    private const int ButtonSize = 36;
    private const int ButtonSpacing = 4;
    private const int ToolbarTopMargin = 16;

    private float _toolbarAnim;
    private float _dashOffset; // animated marching ants
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

    // Typed undo stack: all annotations in creation order
    private readonly List<Annotation> _undoStack = new();

    // Draw / Blur / Arrow state
    private List<Point>? _currentStroke;
    private Point _blurStart;
    private bool _isBlurring;

    private Point _arrowStart;
    private bool _isArrowDragging;

    // Curved arrows: freehand path with arrowhead at end
    private List<Point>? _currentCurvedArrow;
    private bool _isCurvedArrowDragging;

    // Highlight rectangles (semi-transparent, yellow default)
    private Point _highlightStart;
    private bool _isHighlighting;
    private static readonly Color DefaultHighlightColor = Color.FromArgb(255, 255, 220, 0);

    // Step numbering
    private int _nextStepNumber = 1;

    // Region auto-detect
    private Rectangle _autoDetectRect;
    private bool _autoDetectActive;

    // Color picker popup state
    private bool _colorPickerOpen;
    private Rectangle _colorPickerRect;

    // Smart eraser state
    private Point _eraserStart;
    private Color _eraserColor;
    private bool _isEraserDragging;
    private bool _isTyping;
    private Point _textPos;
    private string _textBuffer = "";
    private float _textFontSize = 24f;
    private bool _textBold = true; // default bold
    private string _textFontFamily = "Segoe UI";
    private int _textResizeHandle = -1;
    private bool _textResizing;
    private Point _textResizeStart;
    private bool _textDragging;
    private Point _textDragOffset;

    // Font picker popup
    private bool _fontPickerOpen;
    private Rectangle _fontPickerRect;
    private int _fontPickerHovered = -1;
    private int _fontPickerScroll;
    private static readonly string[] FontChoices = {
        "Segoe UI", "Arial", "Calibri", "Consolas", "Courier New",
        "Comic Sans MS", "Georgia", "Impact", "Lucida Console",
        "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
        "Cascadia Code", "Segoe UI Semibold"
    };

    // Emoji tool state
    private bool _emojiPickerOpen;
    private Rectangle _emojiPickerRect;
    private string _emojiSearch = "";
    private int _emojiHovered = -1;
    private int _emojiScrollOffset;
    private string? _selectedEmoji;
    private bool _isPlacingEmoji;
    private float _emojiPlaceSize = 32f;

    // Common emoji palette (searchable)
    private static readonly (string emoji, string name)[] EmojiPalette = {
        ("\U0001F600", "grinning"), ("\U0001F602", "joy"), ("\U0001F605", "sweat smile"),
        ("\U0001F609", "wink"), ("\U0001F60D", "heart eyes"), ("\U0001F618", "kiss"),
        ("\U0001F60E", "sunglasses"), ("\U0001F914", "thinking"), ("\U0001F928", "raised eyebrow"),
        ("\U0001F644", "rolling eyes"), ("\U0001F62D", "crying"), ("\U0001F621", "angry"),
        ("\U0001F525", "fire"), ("\U00002764", "heart"), ("\U0001F44D", "thumbs up"),
        ("\U0001F44E", "thumbs down"), ("\U0001F44F", "clap"), ("\U0001F4AF", "100"),
        ("\U00002705", "check"), ("\U0000274C", "cross mark"), ("\U000026A0", "warning"),
        ("\U0001F6A8", "alert"), ("\U0001F4A1", "light bulb"), ("\U0001F50D", "magnifying"),
        ("\U0001F4CC", "pin"), ("\U0001F4CB", "clipboard"), ("\U0001F4DD", "memo"),
        ("\U0001F3AF", "target"), ("\U0001F680", "rocket"), ("\U00002B50", "star"),
        ("\U0001F389", "party"), ("\U0001F381", "gift"), ("\U0001F512", "lock"),
        ("\U0001F513", "unlock"), ("\U0001F504", "arrows"), ("\U0001F4E2", "loudspeaker"),
        ("\U0001F6AB", "prohibited"), ("\U000023F0", "alarm clock"), ("\U0001F4C8", "chart up"),
        ("\U0001F4C9", "chart down"), ("\U0001F4E7", "email"), ("\U0001F4BB", "laptop"),
        ("\U0001F5A5", "desktop"), ("\U0001F4F1", "phone"), ("\U0001F3A8", "palette"),
        ("\U0001F3B5", "music note"), ("\U0001F4F7", "camera"), ("\U0001F4C1", "folder"),
        ("\U0001F41B", "bug"), ("\U0001F527", "wrench"), ("\U00002699", "gear"),
        ("\U0001F6E0", "tools"), ("\U0001F4A9", "poop"), ("\U0001F47B", "ghost"),
        ("\U0001F916", "robot"), ("\U0001F47D", "alien"), ("\U0001F4A3", "bomb"),
        ("\U0001F480", "skull"), ("\U0001F440", "eyes"), ("\U0001F4AC", "speech bubble"),
        ("\U0001F4AD", "thought bubble"), ("\U0001F449", "point right"), ("\U0001F448", "point left"),
        ("\U0001F446", "point up"), ("\U0001F447", "point down"), ("\U0000270B", "hand"),
    };

    // Tool color (shared across draw, arrow, text)
    private Color _toolColor = Color.Red;
    private static readonly Color[] ToolColors = {
        Color.Red, Color.FromArgb(255, 136, 0), Color.FromArgb(255, 220, 0),
        Color.FromArgb(0, 200, 0), Color.FromArgb(0, 136, 255), Color.White
    };
    private int _toolColorIndex = 0;

    // (typed _undoStack is defined above with annotation state)

    // Blank cursor for color picker (we draw our own crosshair)
    private static readonly Cursor _blankCursor = CreateBlankCursor();

    public CaptureMode CurrentMode => _mode;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCrosshairGuides { get; set; }

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

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps for smooth animation
        _animTimer.Tick += (_, _) =>
        {
            float elapsed = (float)(DateTime.UtcNow - _showTime).TotalMilliseconds;
            _toolbarAnim = Math.Min(1f, elapsed / 120f);
            // Steady accumulator for smooth dash crawl (no modulo discontinuity)
            _dashOffset += 0.35f;
            if (_dashOffset > 1000f) _dashOffset -= 1000f;
            if (_hasSelection || _autoDetectActive || ShowCrosshairGuides)
                Invalidate();
            else if (_toolbarAnim < 1f)
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
