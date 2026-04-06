using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly int[] _pixelData;
    private readonly int _bmpW, _bmpH;
    private readonly Rectangle _virtualBounds;
    private readonly WindowDetectionMode _windowDetectionMode;

    private CaptureMode _mode = CaptureMode.Rectangle;
    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private Rectangle _selectionRect;
    private bool _hasSelection;
    private bool _hasDragged;

    private readonly List<Point> _freeformPoints = new();

    // Dynamic toolbar built from enabled tools + fixed buttons (color, close)
    private ToolDef[] _visibleTools = ToolDef.AllTools;
    private ToolDef[] _mainBarTools = Array.Empty<ToolDef>();
    private ToolDef[] _flyoutTools = Array.Empty<ToolDef>();
    private int BtnCount => _mainBarTools.Length + (_flyoutTools.Length > 0 ? 1 : 0) + 2; // +more +color +close
    private int _moreButtonIndex = -1; // index of "..." button in _toolbarButtons
    private Rectangle[] _toolbarButtons = Array.Empty<Rectangle>();
    private string[] _toolbarIcons = Array.Empty<string>();
    private string[] _toolbarLabels = Array.Empty<string>();
    private CaptureMode?[] _toolbarModes = Array.Empty<CaptureMode?>();
    private int _hoveredButton = -1;

    // Flyout state
    private bool _flyoutOpen;
    private Rectangle _flyoutRect;
    private Rectangle[] _flyoutButtonRects = Array.Empty<Rectangle>();
    private int _hoveredFlyoutButton = -1;
    private float _flyoutAnim;
    private float _flyoutAnimStart;
    private float _flyoutAnimTarget;
    private DateTime _flyoutAnimStartedAt;
    private bool _showToolNumberBadges = true;
    private Rectangle _toolbarRect;
    private float _toolbarAnim;
    private Point _lastCursorPos;
    private Point _prevCursorPos; // crosshair ghosting fix
    private Rectangle _lastSelectionRect;
    private Rectangle _lastAutoDetectRect;
    private CrosshairGuideForm? _verticalCrosshairForm;
    private CrosshairGuideForm? _horizontalCrosshairForm;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly System.Windows.Forms.Timer _autoDetectTimer;
    private DateTime _showTime;
    private const float FlyoutAnimDurationMs = 135f;
    private ToolbarForm? _toolbarForm;
    private bool _allowDeactivation;
    private Point _pendingAutoDetectPoint = Point.Empty;

    private const int TopBarHeight = 110;

    // Color picker state
    private readonly Bitmap _magBitmap;
    private readonly int[] _magPixels;
    private readonly Graphics _magGfx;
    private readonly Font _hexFont = UiChrome.ChromeFont(11f, FontStyle.Bold);
    private readonly Font _rgbFont = UiChrome.ChromeFont(9f);
    private readonly SolidBrush _mutedBrush = new(Color.FromArgb(140, 255, 255, 255));
    private readonly Pen _crossPen = new(Color.FromArgb(210, 255, 255, 255), 1f);
    private Point _pickerCursorPos;
    private Point _lastMagnifierSamplePoint = new(-1, -1);
    private Color _pickedColor = Color.Black;
    private string _hexStr = "000000";
    private string _rgbStr = "0, 0, 0";
    private readonly System.Windows.Forms.Timer _pickerTimer;

    private const int Grid = 11, Cell = 14, Mag = Grid * Cell;
    private const int InfoH = 0, PPad = 0;
    private const int PW = Mag, PH = Mag;
    private const int MagOff = 8, MagMargin = 4;

    // Typed undo stack: all annotations in creation order
    private readonly List<Annotation> _undoStack = new();
    private Bitmap? _committedAnnotationsBitmap;
    private bool _committedAnnotationsDirty = true;

    // Draw / Blur / Arrow state
    private List<Point>? _currentStroke;
    private Point _blurStart;
    private bool _isBlurring;
    private Bitmap? _blurPreviewBitmap;
    private Graphics? _blurPreviewGraphics;
    private Size _blurPreviewSize;

    private Point _arrowStart;
    private bool _isArrowDragging;

    // Straight line
    private Point _lineStart;
    private bool _isLineDragging;

    // Ruler tool
    private Point _rulerStart;
    private bool _isRulerDragging;

    // Curved arrows: freehand path with arrowhead at end
    private List<Point>? _currentCurvedArrow;
    private bool _isCurvedArrowDragging;

    // Highlight rectangles (semi-transparent, yellow default)
    private Point _highlightStart;
    private bool _isHighlighting;
    private static readonly Color DefaultHighlightColor = Color.FromArgb(255, 255, 220, 0);

    // Shape tools
    private Point _shapeStart;
    private bool _isRectShapeDragging;
    private bool _isCircleShapeDragging;

    // Step numbering
    private int _nextStepNumber = 1;

    // Region auto-detect
    private Rectangle _autoDetectRect;
    private bool _autoDetectActive;

    // Color picker popup state
    private bool _colorPickerOpen;
    private Rectangle _colorPickerRect;
    private PickerMagnifierForm? _captureMagnifierForm;

    private static RegionOverlayForm? _currentOverlay;

    // Select tool state
    private int _selectedAnnotationIndex = -1;
    private bool _isSelectDragging;
    private bool _isSelectResizing;
    private int _selectResizeHandle = -1; // 0=TL,1=TR,2=BL,3=BR
    private Point _selectDragStart;
    private Rectangle _selectHandleBounds; // cached bounds for handle hit-testing
    private const int SelectHandleSize = 8;
    private const int SelectHandleHitSize = 14; // larger hit area for easier clicking

    // Smart eraser state
    private Point _eraserStart;
    private Color _eraserColor;
    private bool _isEraserDragging;
    private bool _isTyping;
    private Point _textPos;
    private string _textBuffer = "";
    private float _textFontSize = 24f;
    private bool _textBold = true; // default bold
    private bool _textItalic;
    private bool _textStroke = true; // outline stroke enabled by default
    private bool _textShadow = true; // shadow enabled by default
    private string _textFontFamily = UiChrome.FallbackFamilyName;
    private TextBox? _textBox; // real textbox for native text editing

    // Inline text formatting toolbar hit rects (computed during paint)
    private RectangleF _textToolbarRect;
    private RectangleF _textBoldBtnRect;
    private RectangleF _textItalicBtnRect;
    private RectangleF _textStrokeBtnRect;
    private RectangleF _textShadowBtnRect;
    private RectangleF _textFontBtnRect;
    private int _hoveredTextBtn = -1; // 0=B, 1=I, 2=S, 3=Sh, 4=Font, -1=none
    private string _textBtnTooltip = "";
    private int _textResizeHandle = -1;
    private bool _textResizing;
    private Point _textResizeStart;
    private bool _textDragging;
    private Point _textDragOffset;
    private RectangleF _activeTextRectCache;
    private float _activeTextMeasureWidth;
    private readonly RectangleF[] _activeTextHandleCache = new RectangleF[4];
    private bool _activeTextLayoutDirty = true;

    // Font picker popup
    private bool _fontPickerOpen;
    private Rectangle _fontPickerRect;
    private int _fontPickerHovered = -1;
    private int _fontPickerScroll;
    private string _fontSearch = "";
    private string[]? _filteredFonts;
    private TextBox? _fontSearchBox;

    // Font cache for picker rendering (avoid creating Font objects every paint)
    private static readonly Dictionary<string, Font> _fontCache = new();

    // Emoji rendering (Direct2D for color emoji)
    private readonly EmojiRenderer _emojiRenderer = new();

    // Emoji tool state
    private bool _emojiPickerOpen;
    private Rectangle _emojiPickerRect;
    private string _emojiSearch = "";
    private int _emojiHovered = -1;
    private TextBox? _emojiSearchBox;
    private int _emojiScrollOffset;
    private string? _selectedEmoji;
    private bool _isPlacingEmoji;
    private float _emojiPlaceSize = 32f;
    private int _emojiWarmupIndex;
    private bool _emojiWarmupPending;

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

    // Events
    public event Action<Rectangle>? RegionSelected;
    public event Action<Rectangle>? OcrRegionSelected;
    public event Action<Bitmap>? FreeformSelected;
    public event Action<string>? ColorPicked;
    public event Action<Rectangle>? ScanRegionSelected;
    public event Action<Rectangle>? StickerRegionSelected;
    public event Action? SelectionCancelled;
    public RegionOverlayForm(Bitmap screenshot, Rectangle virtualBounds,
        CaptureMode initialMode = CaptureMode.Rectangle,
        WindowDetectionMode windowDetectionMode = WindowDetectionMode.WindowOnly)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _windowDetectionMode = windowDetectionMode;
        _bmpW = _screenshot.Width;
        _bmpH = _screenshot.Height;
        _mode = initialMode;
        _showTime = DateTime.UtcNow;

        // Cache pixels for color picker
        _pixelData = new int[_bmpW * _bmpH];
        var bits = _screenshot.LockBits(new Rectangle(0, 0, _bmpW, _bmpH),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(bits.Scan0, _pixelData, 0, _pixelData.Length);
        _screenshot.UnlockBits(bits);

        // Magnifier bitmap for color picker
        _magBitmap = new Bitmap(PW, PH, PixelFormat.Format32bppArgb);
        _magPixels = new int[PW * PH];
        _magGfx = Graphics.FromImage(_magBitmap);
        _magGfx.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        SetupForm();
        CalcToolbar();

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            float elapsed = (float)(DateTime.UtcNow - _showTime).TotalMilliseconds;
            _toolbarAnim = EaseOutCubic(Math.Min(1f, elapsed / 180f));
            if (Math.Abs(_flyoutAnim - _flyoutAnimTarget) > 0.01f)
            {
                float flyoutElapsed = (float)(DateTime.UtcNow - _flyoutAnimStartedAt).TotalMilliseconds;
                float t = Math.Clamp(flyoutElapsed / FlyoutAnimDurationMs, 0f, 1f);
                float eased = EaseInOutQuint(t);
                _flyoutAnim = _flyoutAnimStart + ((_flyoutAnimTarget - _flyoutAnimStart) * eased);
            }
            else
            {
                _flyoutAnim = _flyoutAnimTarget;
                if (_flyoutAnimTarget <= 0f)
                {
                    _flyoutAnim = 0f;
                    _hoveredFlyoutButton = -1;
                }
            }
            UpdateToolbarSurfaceOnly();
            Invalidate(new Rectangle(_toolbarRect.X - 12, _toolbarRect.Y - 48,
                _toolbarRect.Width + 24, _toolbarRect.Height + 160));
            if (_toolbarAnim >= 1f && Math.Abs(_flyoutAnim - _flyoutAnimTarget) <= 0.01f)
            {
                if (_flyoutAnimTarget <= 0f)
                {
                    _toolbarForm?.UpdateSurface();
                }
                _animTimer.Stop();
            }
        };

        _pickerTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _pickerTimer.Tick += OnPickerTick;
        if (_mode == CaptureMode.ColorPicker) _pickerTimer.Start();

        _autoDetectTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _autoDetectTimer.Tick += (_, _) =>
        {
            _autoDetectTimer.Stop();
            if (_isSelecting || !ToolDef.IsCaptureTool(_mode) || IsPointInOverlayUi(_pendingAutoDetectPoint))
                return;

            UpdateAutoDetectRect(_pendingAutoDetectPoint);
        };

        _currentOverlay = this;
    }

    private static float EaseOutCubic(float t)
        => 1f - (float)Math.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    private static float EaseInOutQuint(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f
            ? 16f * t * t * t * t * t
            : 1f - (float)Math.Pow(-2f * t + 2f, 5f) / 2f;
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
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Opaque, true);
        KeyPreview = true;
    }

    private const int GroupGap = UiChrome.ToolbarGroupGap; // spacing between tool groups (includes separator line)

    // Separator indices (computed dynamically based on visible tools)
    private int[] _sepAfter = Array.Empty<int>();

    private void CalcToolbar()
    {
        bool hasMore = _flyoutTools.Length > 0;
        _toolbarButtons = new Rectangle[BtnCount];
        _toolbarIcons = new string[BtnCount];
        _toolbarLabels = new string[BtnCount];
        _toolbarModes = new CaptureMode?[BtnCount];

        for (int i = 0; i < _mainBarTools.Length; i++)
        {
            _toolbarIcons[i] = _mainBarTools[i].Id;
            _toolbarLabels[i] = _mainBarTools[i].Label;
            _toolbarModes[i] = _mainBarTools[i].Mode;
        }
        int idx = _mainBarTools.Length;
        if (hasMore)
        {
            _moreButtonIndex = idx;
            _toolbarIcons[idx] = "more";
            _toolbarLabels[idx] = "More tools";
            _toolbarModes[idx] = null;
            idx++;
        }
        else
        {
            _moreButtonIndex = -1;
        }
        _toolbarIcons[idx] = "color";
        _toolbarLabels[idx] = "Color";
        _toolbarModes[idx] = null;
        _toolbarIcons[idx + 1] = "close";
        _toolbarLabels[idx + 1] = "Close (Esc)";
        _toolbarModes[idx + 1] = null;

        // Compute group gaps: between tool groups
        var gaps = new List<int>();
        for (int i = 0; i < _mainBarTools.Length - 1; i++)
            if (_mainBarTools[i].Group != _mainBarTools[i + 1].Group || _mainBarTools[i].Mode == CaptureMode.Freeform)
                gaps.Add(i);
        gaps.Add(_mainBarTools.Length - 1 + (hasMore ? 1 : 0)); // gap before color/gear/close
        _sepAfter = gaps.ToArray();

        int pad = UiChrome.ToolbarInnerPadding;
        int primarySpan = UiChrome.ToolbarButtonSize * BtnCount
                        + UiChrome.ToolbarButtonSpacing * (BtnCount - 1)
                        + pad * 2
                        + _sepAfter.Length * UiChrome.ToolbarGroupGap;
        int w = IsVerticalDock ? UiChrome.ToolbarHeight : primarySpan;
        int h = IsVerticalDock ? primarySpan : UiChrome.ToolbarHeight;
        Rectangle screenBounds;
        try
        {
            screenBounds = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        }
        catch
        {
            screenBounds = Screen.PrimaryScreen?.WorkingArea ?? _virtualBounds;
        }

        _toolbarRect = ToolbarLayout.GetToolbarRect(_virtualBounds, screenBounds, w, h, CaptureDockSide);
        int cx = _toolbarRect.X + pad;
        int cy = _toolbarRect.Y + pad;
        for (int i = 0; i < BtnCount; i++)
        {
            _toolbarButtons[i] = IsVerticalDock
                ? new Rectangle(
                    _toolbarRect.X + (UiChrome.ToolbarHeight - UiChrome.ToolbarButtonSize) / 2,
                    cy,
                    UiChrome.ToolbarButtonSize,
                    UiChrome.ToolbarButtonSize)
                : new Rectangle(
                    cx,
                    _toolbarRect.Y + (UiChrome.ToolbarHeight - UiChrome.ToolbarButtonSize) / 2,
                    UiChrome.ToolbarButtonSize,
                    UiChrome.ToolbarButtonSize);

            if (IsVerticalDock)
            {
                cy += UiChrome.ToolbarButtonSize + UiChrome.ToolbarButtonSpacing;
                if (Array.IndexOf(_sepAfter, i) >= 0) cy += GroupGap;
            }
            else
            {
                cx += UiChrome.ToolbarButtonSize + UiChrome.ToolbarButtonSpacing;
                if (Array.IndexOf(_sepAfter, i) >= 0) cx += GroupGap;
            }
        }

        // Calculate flyout rect adjacent to the main toolbar and toward the capture area.
        if (hasMore && _flyoutTools.Length > 0)
        {
            int flyPad = UiChrome.ToolbarFlyoutPadding;
            int flyW = IsVerticalDock
                ? UiChrome.ToolbarHeight
                : UiChrome.ToolbarButtonSize * _flyoutTools.Length + UiChrome.ToolbarButtonSpacing * (_flyoutTools.Length - 1) + flyPad * 2;
            int flyH = IsVerticalDock
                ? UiChrome.ToolbarButtonSize * _flyoutTools.Length + UiChrome.ToolbarButtonSpacing * (_flyoutTools.Length - 1) + flyPad * 2
                : UiChrome.ToolbarHeight;

            var moreAnchor = _moreButtonIndex >= 0 ? _toolbarButtons[_moreButtonIndex] : _toolbarRect;
            int flyX;
            int flyY;
            if (IsVerticalDock)
            {
                flyX = IsRightDock ? _toolbarRect.X - flyW - 8 : _toolbarRect.Right + 8;
                flyY = moreAnchor.Y + (moreAnchor.Height / 2) - (flyH / 2);
                flyX = Math.Clamp(flyX, 4, Math.Max(4, ClientSize.Width - flyW - 4));
                flyY = Math.Clamp(flyY, 4, Math.Max(4, ClientSize.Height - flyH - 4));
            }
            else
            {
                flyX = _toolbarRect.Right - flyW;
                flyY = IsBottomDock ? _toolbarRect.Y - flyH - 8 : _toolbarRect.Bottom + 8;
                flyX = Math.Clamp(flyX, 4, Math.Max(4, ClientSize.Width - flyW - 4));
                flyY = Math.Clamp(flyY, 4, Math.Max(4, ClientSize.Height - flyH - 4));
            }

            _flyoutRect = new Rectangle(flyX, flyY, flyW, flyH);
            _flyoutButtonRects = new Rectangle[_flyoutTools.Length];
            int fcx = flyX + flyPad;
            int fcy = flyY + flyPad;
            for (int i = 0; i < _flyoutTools.Length; i++)
            {
                _flyoutButtonRects[i] = IsVerticalDock
                    ? new Rectangle(
                        flyX + (flyW - UiChrome.ToolbarButtonSize) / 2,
                        fcy,
                        UiChrome.ToolbarButtonSize,
                        UiChrome.ToolbarButtonSize)
                    : new Rectangle(
                        fcx,
                        flyY + (flyH - UiChrome.ToolbarButtonSize) / 2,
                        UiChrome.ToolbarButtonSize,
                        UiChrome.ToolbarButtonSize);

                if (IsVerticalDock)
                    fcy += UiChrome.ToolbarButtonSize + UiChrome.ToolbarButtonSpacing;
                else
                    fcx += UiChrome.ToolbarButtonSize + UiChrome.ToolbarButtonSpacing;
            }
        }
    }

    private static Cursor CreateBlankCursor()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        return new Cursor(bmp.GetHicon());
    }

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
}
