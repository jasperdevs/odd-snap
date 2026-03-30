namespace Yoink.Models;

public enum AfterCaptureAction
{
    CopyToClipboard,
    ShowPreview
}

public enum ToastPosition
{
    Right,
    Left,
    TopLeft,
    TopRight
}

public enum HistoryRetentionPeriod
{
    Never,
    OneDay,
    SevenDays,
    ThirtyDays,
    NinetyDays
}

public enum CaptureImageFormat
{
    Png,
    Jpeg,
    Bmp
}

public enum WindowDetectionMode
{
    Off,
    WindowOnly
}

public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = Native.User32.MOD_ALT;
    public uint HotkeyKey { get; set; } = 0xC0; // VK_OEM_3 = backtick/tilde

    // OCR hotkey: Alt+Shift+`
    public uint OcrHotkeyModifiers { get; set; } = Native.User32.MOD_ALT | Native.User32.MOD_SHIFT;
    public uint OcrHotkeyKey { get; set; } = 0xC0;

    // Color picker hotkey: Alt+C
    public uint PickerHotkeyModifiers { get; set; } = Native.User32.MOD_ALT;
    public uint PickerHotkeyKey { get; set; } = 0x43; // VK_C

    // Optional custom-tool hotkeys (disabled by default)
    public uint ScanHotkeyModifiers { get; set; }
    public uint ScanHotkeyKey { get; set; }
    public uint StickerHotkeyModifiers { get; set; }
    public uint StickerHotkeyKey { get; set; }
    public uint FullscreenHotkeyModifiers { get; set; }
    public uint FullscreenHotkeyKey { get; set; }
    public uint ActiveWindowHotkeyModifiers { get; set; }
    public uint ActiveWindowHotkeyKey { get; set; }
    public uint RulerHotkeyModifiers { get; set; }
    public uint RulerHotkeyKey { get; set; }

    // GIF recording hotkey (disabled by default)
    public uint GifHotkeyModifiers { get; set; }
    public uint GifHotkeyKey { get; set; }
    public int GifFps { get; set; } = 15;
    public int GifMaxDuration { get; set; } = 30;

    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.ShowPreview;
    public bool SaveToFile { get; set; } = true;
    public bool AskForFileNameOnSave { get; set; }
    public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Png;
    public int CaptureMaxLongEdge { get; set; }
    public string SaveDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Yoink");
    public bool StartWithWindows { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public CaptureMode LastCaptureMode { get; set; } = CaptureMode.Rectangle;
    public WindowDetectionMode WindowDetection { get; set; } = WindowDetectionMode.WindowOnly;
    public bool SaveHistory { get; set; } = true;
    public bool MuteSounds { get; set; }
    public bool ShowCrosshairGuides { get; set; } // off by default
    public bool DetectWindows { get; set; } = true;
    public bool DetectControls { get; set; } = true;
    public bool CompressHistory { get; set; }
    public int JpegQuality { get; set; } = 85;
    public bool HasCompletedSetup { get; set; }
    public ToastPosition ToastPosition { get; set; } = ToastPosition.Right;
    public CaptureMode DefaultCaptureMode { get; set; } = CaptureMode.Rectangle;
    public bool ShowToolNumberBadges { get; set; } = true;
    public HistoryRetentionPeriod HistoryRetention { get; set; } = HistoryRetentionPeriod.Never;

    // Upload settings
    public bool AutoUploadScreenshots { get; set; }
    public Services.UploadDestination ImageUploadDestination { get; set; } = Services.UploadDestination.None;
    public Services.UploadSettings ImageUploadSettings { get; set; } = new();
    public Services.StickerSettings StickerUploadSettings { get; set; } = new();

    // Toolbar customization: which tools appear in the dock
    // null = all tools enabled (default). List of tool IDs from ToolDef.AllTools.
    public List<string>? EnabledTools { get; set; }
}

/// <summary>Definition of a toolbar tool with id, label, icon, mode, and group.</summary>
public sealed record ToolDef(string Id, string Label, char Icon, CaptureMode? Mode, int Group)
{
    /// <summary>All available tools in display order. Group 0=capture, 1=annotation.</summary>
    public static readonly ToolDef[] AllTools =
    {
        new("rect",        "Rectangle Select", '\uE257', CaptureMode.Rectangle, 0), // scan-line
        new("free",        "Freeform Select",  '\uE1CE', CaptureMode.Freeform,  0), // lasso-select
        new("picker",      "Color Picker", '\uE13E', CaptureMode.ColorPicker, 0), // pipette
        new("ocr",         "OCR",          '\uE53C', CaptureMode.Ocr,         0), // scan-text
        new("scan",        "QR/Barcode",   '\uE1DE', CaptureMode.Scan,        0), // qr-code
        new("sticker",     "Sticker",      '\uE7C5', CaptureMode.Sticker,     0), // sticker
        new("ruler",       "Ruler",        '\uE14E', CaptureMode.Ruler,       1), // ruler
        new("highlight",   "Highlight",    '\uE0F7', CaptureMode.Highlight,   1), // highlighter
        new("rectShape",   "Rectangle",    '\uE16A', CaptureMode.RectShape,   1), // square
        new("circleShape", "Circle",       '\uE07A', CaptureMode.CircleShape, 1), // circle
        new("draw",        "Draw",         '\uE1F8', CaptureMode.Draw,        1), // pencil
        new("line",        "Line",         '\uE11F', CaptureMode.Line,        1), // minus
        new("arrow",       "Arrow",        '\uE051', CaptureMode.Arrow,       1), // arrow-up-right
        new("curvedArrow", "Curved Arrow", '\uE146', CaptureMode.CurvedArrow, 1), // redo
        new("text",        "Text",         '\uE197', CaptureMode.Text,        1), // type
        new("step",        "Step Number",  '\uE1D0', CaptureMode.StepNumber,  1), // list-ordered
        new("blur",        "Blur",         '\uE5A0', CaptureMode.Blur,        1), // blend
        new("eraser",      "Eraser",       '\uE28E', CaptureMode.Eraser,      1), // eraser
        new("magnifier",   "Magnifier",    '\uE154', CaptureMode.Magnifier,   1), // search
        new("emoji",       "Emoji",        '\uE167', CaptureMode.Emoji,       1), // smile
    };

    public static List<string> DefaultEnabledIds() =>
        AllTools.Where(t => t.Id != "sticker").Select(t => t.Id).ToList();
}
