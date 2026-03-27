namespace Yoink.Models;

public enum AfterCaptureAction
{
    CopyToClipboard,
    ShowPreview
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

    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.ShowPreview;
    public bool SaveToFile { get; set; } = true;
    public string SaveDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Yoink");
    public bool StartWithWindows { get; set; } = true;
    public CaptureMode LastCaptureMode { get; set; } = CaptureMode.Rectangle;
    public bool SaveHistory { get; set; } = true;
    public bool MuteSounds { get; set; }
    public bool ShowCrosshairGuides { get; set; } // off by default
    public bool CompressHistory { get; set; }
    public int JpegQuality { get; set; } = 85;
    public bool HasCompletedSetup { get; set; }

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
        new("rect",        "Rectangle",    '\uF551', CaptureMode.Rectangle,   0),
        new("free",        "Freeform",     '\uF564', CaptureMode.Freeform,    0),
        new("ocr",         "OCR",          '\uF561', CaptureMode.Ocr,         0),
        new("picker",      "Color Picker", '\uF3C9', CaptureMode.ColorPicker, 0),
        new("draw",        "Draw",         '\uF513', CaptureMode.Draw,        1),
        new("highlight",   "Highlight",    '\uF466', CaptureMode.Highlight,   1),
        new("line",        "Line",         '\uF48D', CaptureMode.Line,        1),
        new("arrow",       "Arrow",        '\uF2A0', CaptureMode.Arrow,       1),
        new("curvedArrow", "Curved Arrow", '\uF2DB', CaptureMode.CurvedArrow, 1),
        new("text",        "Text",         '\uF5E8', CaptureMode.Text,        1),
        new("step",        "Step Number",  '\uF4DC', CaptureMode.StepNumber,  1),
        new("blur",        "Blur",         '\uF44B', CaptureMode.Blur,        1),
        new("eraser",      "Eraser",       '\uF3C3', CaptureMode.Eraser,      1),
        new("magnifier",   "Magnifier",    '\uF4A8', CaptureMode.Magnifier,   1),
        new("emoji",       "Emoji",        '\uF58E', CaptureMode.Emoji,       1),
    };

    public static List<string> DefaultEnabledIds() =>
        AllTools.Select(t => t.Id).ToList();
}
