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
}
