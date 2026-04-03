using Yoink.Helpers;

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

public enum SoundPack
{
    Default,
    Soft,
    Retro
}

public enum RecordingFormat
{
    GIF,
    MP4,
    WebM,
    MKV
}

public enum RecordingQuality
{
    Original,
    P1080,
    P720,
    P480
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
    public string OcrLanguageTag { get; set; } = "auto";

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

    // Scrolling capture hotkey (disabled by default)
    public uint ScrollCaptureHotkeyModifiers { get; set; }
    public uint ScrollCaptureHotkeyKey { get; set; }

    // GIF recording hotkey (disabled by default)
    public uint GifHotkeyModifiers { get; set; }
    public uint GifHotkeyKey { get; set; }
    public int GifFps { get; set; } = 15;

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
    public int CaptureDelaySeconds { get; set; }
    public bool SaveHistory { get; set; } = true;
    public bool MuteSounds { get; set; }
    public bool ShowCrosshairGuides { get; set; } // off by default
    public bool ShowCursor { get; set; }
    public bool DetectWindows { get; set; } = true;
    public bool CompressHistory { get; set; }
    public int JpegQuality { get; set; } = 85;
    public bool HasCompletedSetup { get; set; }
    public ToastPosition ToastPosition { get; set; } = ToastPosition.Right;
    public CaptureMode DefaultCaptureMode { get; set; } = CaptureMode.Rectangle;
    public bool ShowToolNumberBadges { get; set; } = true;
    public HistoryRetentionPeriod HistoryRetention { get; set; } = HistoryRetentionPeriod.Never;

    // Upload settings
    public bool AutoUploadScreenshots { get; set; }
    public bool AutoUploadGifs { get; set; }
    public bool AutoUploadVideos { get; set; }
    public Services.UploadDestination ImageUploadDestination { get; set; } = Services.UploadDestination.None;
    public Services.UploadSettings ImageUploadSettings { get; set; } = new();
    public Services.StickerSettings StickerUploadSettings { get; set; } = new();

    public double ToastDurationSeconds { get; set; } = 2.5;
    public bool AutoPinPreviews { get; set; }
    public SoundPack SoundPack { get; set; } = SoundPack.Default;

    // Video recording
    public RecordingFormat RecordingFormat { get; set; } = RecordingFormat.GIF;
    public RecordingQuality RecordingQuality { get; set; } = RecordingQuality.Original;
    public int RecordingFps { get; set; } = 30;
    public bool RecordMicrophone { get; set; }
    public bool RecordDesktopAudio { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? DesktopAudioDeviceId { get; set; }

    // Toolbar customization: which tools appear in the dock
    // null = all tools enabled (default). List of tool IDs from ToolDef.AllTools.
    public List<string>? EnabledTools { get; set; }

    // Generic hotkeys for any tool by ID. Key = tool id, Value = [modifiers, virtualKey].
    // Tools with dedicated properties (rect, ocr, picker, etc.) are mapped to those properties instead.
    public Dictionary<string, uint[]>? ToolHotkeys { get; set; }

    // Virtual key codes for in-capture annotation shortcuts: 1-9, 0, -, =, [, ]
    private static readonly uint[] AnnotationKeyVks =
    {
        0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, // 1-9
        0x30, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC // 0, -, =, [, ], \
    };

    /// <summary>Compute annotation tool defaults dynamically from enabled tools (matches dock numbering).</summary>
    private Dictionary<string, uint> GetAnnotationDefaults()
    {
        var enabled = EnabledTools ?? ToolDef.DefaultEnabledIds();
        var result = new Dictionary<string, uint>();
        int idx = 0;
        foreach (var t in ToolDef.AllTools.Where(t => t.Group == 1))
        {
            if (!enabled.Contains(t.Id)) continue;
            if (idx < AnnotationKeyVks.Length)
                result[t.Id] = AnnotationKeyVks[idx++];
        }
        return result;
    }

    /// <summary>Get hotkey (mod, key) for a tool ID, checking named properties first then dictionary.</summary>
    public (uint mod, uint key) GetToolHotkey(string toolId) => toolId switch
    {
        "rect" => (HotkeyModifiers, HotkeyKey),
        "ocr" => (OcrHotkeyModifiers, OcrHotkeyKey),
        "picker" => (PickerHotkeyModifiers, PickerHotkeyKey),
        "scan" => (ScanHotkeyModifiers, ScanHotkeyKey),
        "sticker" => (StickerHotkeyModifiers, StickerHotkeyKey),
        "_fullscreen" => (FullscreenHotkeyModifiers, FullscreenHotkeyKey),
        "_activeWindow" => (ActiveWindowHotkeyModifiers, ActiveWindowHotkeyKey),
        "_scrollCapture" => (ScrollCaptureHotkeyModifiers, ScrollCaptureHotkeyKey),
        "_record" => (GifHotkeyModifiers, GifHotkeyKey),
        _ => GetGenericToolHotkey(toolId),
    };

    private (uint mod, uint key) GetGenericToolHotkey(string toolId)
    {
        // Check user-customized value first (including explicit clears stored as [0,0])
        if (ToolHotkeys != null && ToolHotkeys.TryGetValue(toolId, out var v) && v.Length >= 2)
            return (v[0], v[1]);
        // Fall back to dynamic annotation tool defaults (matches dock numbering)
        var defaults = GetAnnotationDefaults();
        if (defaults.TryGetValue(toolId, out var defKey))
            return (0u, defKey);
        return (0u, 0u);
    }

    /// <summary>Set hotkey (mod, key) for a tool ID.</summary>
    public void SetToolHotkey(string toolId, uint mod, uint key)
    {
        switch (toolId)
        {
            case "rect": HotkeyModifiers = mod; HotkeyKey = key; break;
            case "ocr": OcrHotkeyModifiers = mod; OcrHotkeyKey = key; break;
            case "picker": PickerHotkeyModifiers = mod; PickerHotkeyKey = key; break;
            case "scan": ScanHotkeyModifiers = mod; ScanHotkeyKey = key; break;
            case "sticker": StickerHotkeyModifiers = mod; StickerHotkeyKey = key; break;
            // ruler handled by generic path (annotation tool with default key 9)
            case "_fullscreen": FullscreenHotkeyModifiers = mod; FullscreenHotkeyKey = key; break;
            case "_activeWindow": ActiveWindowHotkeyModifiers = mod; ActiveWindowHotkeyKey = key; break;
            case "_scrollCapture": ScrollCaptureHotkeyModifiers = mod; ScrollCaptureHotkeyKey = key; break;
            case "_record": GifHotkeyModifiers = mod; GifHotkeyKey = key; break;
            default:
                ToolHotkeys ??= new();
                ToolHotkeys[toolId] = new[] { mod, key };
                break;
        }
    }
}

/// <summary>Definition of a toolbar tool with id, label, icon, mode, and group.</summary>
public sealed record ToolDef(string Id, string Label, char Icon, CaptureMode? Mode, int Group)
{
    /// <summary>All available tools in display order. Group 0=capture, 1=annotation.</summary>
    public static readonly ToolDef[] AllTools =
    {
        new("rect",        "Rectangle Select", '\uE257', CaptureMode.Rectangle, 0), // scan-line
        new("free",        "Freeform Select",  '\uE1CE', CaptureMode.Freeform,  0), // lasso-select
        new("ocr",         "OCR",          '\uE53C', CaptureMode.Ocr,         0), // scan-text
        new("sticker",     "Sticker",      ToolGlyphs.StickerGlyph, CaptureMode.Sticker,     0), // sticker
        new("picker",      "Color Picker", '\uE13E', CaptureMode.ColorPicker, 0), // pipette
        new("scan",        "QR/Barcode",   '\uE1DE', CaptureMode.Scan,        0), // qr-code
        new("select",      "Select",       '\uE1E3', CaptureMode.Select,      1), // cursor-click
        new("arrow",       "Arrow",        '\uE051', CaptureMode.Arrow,       1), // arrow-up-right
        new("curvedArrow", "Curved Arrow", '\uE146', CaptureMode.CurvedArrow, 1), // redo
        new("text",        "Text",         '\uE197', CaptureMode.Text,        1), // type
        new("highlight",   "Highlight",    '\uE0F7', CaptureMode.Highlight,   1), // highlighter
        new("blur",        "Blur",         '\uE5A0', CaptureMode.Blur,        1), // blend
        new("step",        "Step Number",  '\uE1D0', CaptureMode.StepNumber,  1), // list-ordered
        new("draw",        "Draw",         '\uE1F8', CaptureMode.Draw,        1), // pencil
        new("line",        "Line",         '\uE11F', CaptureMode.Line,        1), // minus
        new("ruler",       "Ruler",        '\uE14E', CaptureMode.Ruler,       1), // ruler
        new("rectShape",   "Rectangle",    '\uE16A', CaptureMode.RectShape,   1), // square
        new("circleShape", "Circle",       '\uE07A', CaptureMode.CircleShape, 1), // circle
        new("magnifier",   "Magnifier",    '\uE154', CaptureMode.Magnifier,   1), // search
        new("emoji",       "Emoji",        '\uE167', CaptureMode.Emoji,       1), // smile
        new("eraser",      "Eraser",       '\uE28E', CaptureMode.Eraser,      1), // eraser
    };

    public static bool IsCaptureTool(CaptureMode mode) =>
        AllTools.Any(t => t.Mode == mode && t.Group == 0);

    public static bool IsAnnotationTool(CaptureMode mode) =>
        AllTools.Any(t => t.Mode == mode && t.Group == 1);

    public static List<string> DefaultEnabledIds() =>
        AllTools.Select(t => t.Id).ToList();
}
