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

    // Dynamic toolbar built from enabled tools + fixed buttons (color, gear, close)
    private ToolDef[] _visibleTools = ToolDef.AllTools;
    private int BtnCount => _visibleTools.Length + 3; // +3 for color, gear, close
    private Rectangle[] _toolbarButtons = Array.Empty<Rectangle>();
    private int _hoveredButton = -1;
    private Rectangle _toolbarRect;
    private const int ToolbarHeight = 48;
    private const int ButtonSize = 34;
    private const int ButtonSpacing = 2;
    private const int ToolbarTopMargin = 16;

    private float _toolbarAnim;
    private float _dashOffset; // animated marching ants
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly DateTime _showTime;

    // Pre-rendered blurred screenshot used for all glass effects
    private Bitmap? _blurred;
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

    // Straight line
    private Point _lineStart;
    private bool _isLineDragging;

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

    // Emoji rendering (Direct2D for color emoji)
    private readonly EmojiRenderer _emojiRenderer = new();

    // Emoji tool state
    private bool _emojiPickerOpen;
    private Rectangle _emojiPickerRect;
    private string _emojiSearch = "";
    private int _emojiHovered = -1;
    private int _emojiScrollOffset;
    private string? _selectedEmoji;
    private bool _isPlacingEmoji;
    private float _emojiPlaceSize = 32f;

    // Full emoji palette (searchable by name - includes semantic tags after |)
    private static readonly (string emoji, string name)[] EmojiPalette = {
        // Smileys
        ("\U0001F600", "grinning|happy face"), ("\U0001F603", "smiley|happy"), ("\U0001F604", "smile|happy"),
        ("\U0001F601", "grin|happy teeth"), ("\U0001F605", "sweat smile|nervous"), ("\U0001F602", "joy|laugh crying"),
        ("\U0001F923", "rofl|funny"), ("\U0001F609", "wink|flirt"), ("\U0001F60A", "blush|happy shy"),
        ("\U0001F607", "innocent|angel halo"), ("\U0001F60D", "heart eyes|love"), ("\U0001F929", "star struck|amazed"),
        ("\U0001F618", "kiss|love blow"), ("\U0001F617", "kissing"), ("\U0001F61B", "tongue|silly playful"),
        ("\U0001F92A", "zany|crazy wild"), ("\U0001F60E", "sunglasses|cool"), ("\U0001F913", "nerd|smart glasses"),
        ("\U0001F914", "thinking|hmm wonder"), ("\U0001F928", "raised eyebrow|skeptical doubt"), ("\U0001F610", "neutral|meh"),
        ("\U0001F611", "expressionless|blank"), ("\U0001F636", "no mouth|silent quiet"), ("\U0001F644", "rolling eyes|annoyed"),
        ("\U0001F60F", "smirk|sly"), ("\U0001F62C", "grimacing|awkward cringe"), ("\U0001F925", "lying|pinocchio"),
        ("\U0001F60C", "relieved|calm peaceful"), ("\U0001F614", "pensive|sad thoughtful"), ("\U0001F62A", "sleepy|tired"),
        ("\U0001F924", "drooling"), ("\U0001F634", "sleeping|zzz"), ("\U0001F637", "mask|sick covid"),
        ("\U0001F912", "thermometer|sick fever"), ("\U0001F915", "bandage|hurt injured"), ("\U0001F922", "nauseated|sick gross"),
        ("\U0001F92E", "vomiting|sick gross"), ("\U0001F927", "sneezing|sick cold"), ("\U0001F975", "hot|warm sweating"),
        ("\U0001F976", "cold|freezing"), ("\U0001F974", "woozy|drunk"), ("\U0001F635", "dizzy|confused"),
        ("\U0001F92F", "exploding head|mind blown wow"), ("\U0001F920", "cowboy"), ("\U0001F973", "partying|party celebrate"),
        ("\U0001F978", "disguise|spy hidden"), ("\U0001F62D", "crying|sob sad tears"),
        ("\U0001F622", "cry|tear sad"), ("\U0001F625", "sad|disappointed"), ("\U0001F624", "angry huff|frustrated"),
        ("\U0001F621", "angry|mad"), ("\U0001F620", "rage|furious"), ("\U0001F92C", "swearing|cursing"),
        ("\U0001F608", "devil|evil"), ("\U0001F47F", "imp|evil"), ("\U0001F480", "skull|dead death"),
        ("\U0001F4A9", "poop|shit"), ("\U0001F921", "clown|funny"), ("\U0001F47B", "ghost|spooky"),
        ("\U0001F47D", "alien|space ufo"), ("\U0001F916", "robot|tech"), ("\U0001F63A", "cat smile"),
        // Gestures & People
        ("\U0001F44D", "thumbs up|yes good ok like approve"), ("\U0001F44E", "thumbs down|no bad dislike reject"), ("\U0001F44F", "clap|applause bravo"),
        ("\U0001F64C", "raised hands|hooray celebrate"), ("\U0001F91D", "handshake|deal agree"), ("\U0001F64F", "pray|please thanks hope"),
        ("\U0000270D", "writing hand|note"), ("\U0001F4AA", "muscle|strong power flex"), ("\U0001F449", "point right|this here"),
        ("\U0001F448", "point left"), ("\U0001F446", "point up|look above"), ("\U0001F447", "point down|look below"),
        ("\U0000261D", "index up"), ("\U0000270B", "hand|stop wait high five"), ("\U0001F91A", "back hand"),
        ("\U0001F596", "vulcan|spock"), ("\U0001F918", "rock|metal"), ("\U0001F919", "call me|phone"),
        ("\U0001F90C", "pinched"), ("\U0001F90F", "pinch|small tiny"), ("\U0000270C", "peace|victory"),
        ("\U0001F91E", "crossed fingers|luck hope"), ("\U0001F91F", "love you"), ("\U0001F440", "eyes|look see watch"),
        ("\U0001F441", "eye|see watch"), ("\U0001F9E0", "brain|smart think idea"), ("\U0001F5E3", "speaking head|talk say"),
        // Hearts & Symbols
        ("\U00002764", "heart|love red"), ("\U0001F9E1", "orange heart|love"), ("\U0001F49B", "yellow heart|love"),
        ("\U0001F49A", "green heart|love"), ("\U0001F499", "blue heart|love"), ("\U0001F49C", "purple heart|love"),
        ("\U0001F5A4", "black heart|love dark"), ("\U0001F90D", "white heart|love"), ("\U0001F494", "broken heart|sad"),
        ("\U0001F495", "two hearts|love"), ("\U0001F496", "sparkling heart|love"), ("\U0001F4AF", "100|perfect score"),
        ("\U0001F4A5", "boom|explosion bang"), ("\U0001F4A2", "anger|mad"), ("\U0001F4AB", "dizzy star"),
        ("\U0001F4AC", "speech bubble|talk chat message"), ("\U0001F4AD", "thought bubble|think idea"),
        ("\U00002705", "check|yes done correct complete"), ("\U0000274C", "cross mark|no wrong error delete"), ("\U00002753", "question|help why"),
        ("\U00002757", "exclamation|important alert"), ("\U000026A0", "warning|caution danger"), ("\U0001F6AB", "prohibited|ban forbidden no"),
        ("\U0001F6D1", "stop sign|halt"), ("\U0000267B", "recycle|environment green"), ("\U00002B50", "star|favorite rate"),
        ("\U0001F31F", "glowing star|sparkle shine"), ("\U00002728", "sparkles|magic new clean"), ("\U0001F525", "fire|hot lit trending popular"),
        ("\U0001F4A3", "bomb|explosive"), ("\U0001F4A1", "light bulb|idea tip hint"), ("\U0001F514", "bell|notification alert ring"),
        // Objects & Tools
        ("\U0001F50D", "magnifying glass|search find zoom"), ("\U0001F4CC", "pin|location mark save"), ("\U0001F4CB", "clipboard|copy paste"),
        ("\U0001F4DD", "memo|note write"), ("\U0001F4C1", "folder|file directory"), ("\U0001F4C2", "open folder|file"),
        ("\U0001F4C4", "document|page file"), ("\U0001F4C8", "chart up|graph growth increase"), ("\U0001F4C9", "chart down|graph decline decrease"),
        ("\U0001F4CA", "bar chart|data graph stats"), ("\U0001F4E7", "email|mail message"), ("\U0001F4E2", "loudspeaker|announce"),
        ("\U0001F4E3", "megaphone|announce"), ("\U0001F512", "lock|secure private"), ("\U0001F513", "unlock|open"),
        ("\U0001F511", "key|password access"), ("\U0001F527", "wrench|fix repair"), ("\U00002699", "gear|settings config"),
        ("\U0001F6E0", "tools|fix build"), ("\U0001F5D1", "trash|delete remove"), ("\U0001F4F7", "camera|photo screenshot"),
        ("\U0001F4F8", "camera flash|photo"), ("\U0001F3A5", "movie camera|video film"), ("\U0001F4F1", "phone|mobile"),
        ("\U0001F4BB", "laptop|computer"), ("\U0001F5A5", "desktop|computer monitor"), ("\U00002328", "keyboard|type"),
        ("\U0001F5A8", "printer|print"), ("\U0001F50B", "battery|power energy"), ("\U0001F50C", "plug|electric power"),
        ("\U000023F0", "alarm clock|time wake"), ("\U0000231A", "watch|time"), ("\U0001F4B0", "money bag|rich cash"),
        ("\U0001F4B3", "credit card|payment"), ("\U0001F4E6", "package|box delivery"), ("\U0001F381", "gift|present birthday"),
        ("\U0001F3AF", "target|goal aim bullseye"), ("\U0001F3C6", "trophy|winner champion"), ("\U0001F396", "medal|award"),
        // Nature & Animals
        ("\U0001F436", "dog"), ("\U0001F431", "cat"), ("\U0001F42D", "mouse"),
        ("\U0001F430", "rabbit"), ("\U0001F43B", "bear"), ("\U0001F43C", "panda"),
        ("\U0001F414", "chicken"), ("\U0001F41B", "bug"), ("\U0001F41D", "bee"),
        ("\U0001F40D", "snake"), ("\U0001F422", "turtle"), ("\U0001F419", "octopus"),
        ("\U0001F988", "shark"), ("\U0001F984", "unicorn"), ("\U0001F409", "dragon"),
        ("\U0001F332", "tree"), ("\U0001F333", "tree2"), ("\U0001F335", "cactus"),
        ("\U0001F339", "rose"), ("\U0001F33B", "sunflower"), ("\U0001F340", "four leaf"),
        ("\U0001F30D", "globe"), ("\U0001F30E", "americas"), ("\U0001F30F", "asia"),
        // Food & Drink
        ("\U0001F34E", "apple"), ("\U0001F34F", "green apple"), ("\U0001F353", "strawberry"),
        ("\U0001F352", "cherry"), ("\U0001F349", "watermelon"), ("\U0001F34C", "banana"),
        ("\U0001F355", "pizza"), ("\U0001F354", "burger"), ("\U0001F35F", "fries"),
        ("\U0001F32E", "taco"), ("\U0001F370", "cake"), ("\U0001F36D", "lollipop"),
        ("\U00002615", "coffee"), ("\U0001F37A", "beer"), ("\U0001F377", "wine"),
        // Travel & Transport
        ("\U0001F680", "rocket"), ("\U00002708", "airplane"), ("\U0001F697", "car"),
        ("\U0001F695", "taxi"), ("\U0001F6B2", "bicycle"), ("\U0001F3E0", "house"),
        ("\U0001F3E2", "office"), ("\U0001F3D7", "construction"), ("\U0001F5FC", "tower"),
        // Activities & Celebration
        ("\U0001F389", "party"), ("\U0001F38A", "confetti"), ("\U0001F388", "balloon"),
        ("\U0001F3B5", "music note"), ("\U0001F3B6", "notes"), ("\U0001F3A8", "palette"),
        ("\U0001F3AE", "gaming"), ("\U0001F3B2", "dice"), ("\U0001F504", "arrows"),
        // Flags & misc
        ("\U0001F6A8", "alert"), ("\U0001F4A8", "dash"), ("\U0001F4A4", "zzz sleep"),
        ("\U0001F573", "hole"), ("\U0001F648", "see no evil"), ("\U0001F649", "hear no evil"),
        ("\U0001F64A", "speak no evil"), ("\U0001F4F0", "newspaper"), ("\U0001F5DE", "rolled newspaper"),
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

    public void SetEnabledTools(List<string>? enabledIds)
    {
        _visibleTools = enabledIds == null
            ? ToolDef.AllTools
            : ToolDef.AllTools.Where(t => enabledIds.Contains(t.Id)).ToArray();
        _toolbarButtons = new Rectangle[BtnCount];
        CalcToolbar();
    }

    // Events
    public event Action<Rectangle>? RegionSelected;
    public event Action<Rectangle>? OcrRegionSelected;
    public event Action<Bitmap>? FreeformSelected;
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

        _animTimer = new System.Windows.Forms.Timer { Interval = 32 }; // ~30fps for smooth animation
        _animTimer.Tick += (_, _) =>
        {
            float elapsed = (float)(DateTime.UtcNow - _showTime).TotalMilliseconds;
            _toolbarAnim = Math.Min(1f, elapsed / 120f);
            _dashOffset += 0.6f;
            if (_dashOffset > 1000f) _dashOffset -= 1000f;

            if (_toolbarAnim < 1f)
            {
                // Toolbar still animating in - repaint toolbar area
                Invalidate(new Rectangle(_toolbarRect.X - 10, _toolbarRect.Y - 40,
                    _toolbarRect.Width + 20, _toolbarRect.Height + 80));
            }
            else if (_hasSelection)
            {
                // Only repaint selection border area (thin strip around selection)
                int m = 6;
                var s = _selectionRect;
                Invalidate(new Rectangle(s.X - m, s.Y - m, s.Width + m * 2, m * 2)); // top
                Invalidate(new Rectangle(s.X - m, s.Bottom - m, s.Width + m * 2, m * 2 + 30)); // bottom + label
                Invalidate(new Rectangle(s.X - m, s.Y, m * 2, s.Height)); // left
                Invalidate(new Rectangle(s.Right - m, s.Y, m * 2, s.Height)); // right
            }
            else if (_autoDetectActive)
            {
                int m = 4;
                var s = _autoDetectRect;
                Invalidate(new Rectangle(s.X - m, s.Y - m, s.Width + m * 2, m * 2));
                Invalidate(new Rectangle(s.X - m, s.Bottom - m, s.Width + m * 2, m * 2));
                Invalidate(new Rectangle(s.X - m, s.Y, m * 2, s.Height));
                Invalidate(new Rectangle(s.Right - m, s.Y, m * 2, s.Height));
            }
            // Crosshair guides need cursor-area repaint but are lightweight
            else if (ShowCrosshairGuides)
            {
                Invalidate(); // crosshairs span full screen, can't optimize easily
            }
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

    private const int SepWidth = 8;

    // Separator indices (computed dynamically based on visible tools)
    private int[] _sepAfter = Array.Empty<int>();

    private void CalcToolbar()
    {
        _toolbarButtons = new Rectangle[BtnCount];

        // Compute group separators: between last group-0 tool and first group-1 tool,
        // and between last tool and the fixed buttons (color, gear, close)
        var seps = new List<int>();
        for (int i = 0; i < _visibleTools.Length - 1; i++)
            if (_visibleTools[i].Group != _visibleTools[i + 1].Group)
                seps.Add(i);
        seps.Add(_visibleTools.Length - 1); // separator before color/gear/close
        _sepAfter = seps.ToArray();

        int pad = 8;
        int w = ButtonSize * BtnCount + ButtonSpacing * (BtnCount - 1) + pad * 2 + _sepAfter.Length * SepWidth;
        int x = (ClientSize.Width - w) / 2;
        _toolbarRect = new Rectangle(x, ToolbarTopMargin, w, ToolbarHeight);
        int cx = _toolbarRect.X + pad;
        for (int i = 0; i < BtnCount; i++)
        {
            _toolbarButtons[i] = new Rectangle(
                cx, _toolbarRect.Y + (ToolbarHeight - ButtonSize) / 2,
                ButtonSize, ButtonSize);
            cx += ButtonSize + ButtonSpacing;
            if (Array.IndexOf(_sepAfter, i) >= 0) cx += SepWidth;
        }
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
            _blurred?.Dispose();
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
