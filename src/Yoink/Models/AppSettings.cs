namespace Yoink.Models;

public enum AfterCaptureAction
{
    CopyToClipboard,
    ShowPreview
}

public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = Native.User32.MOD_CONTROL | Native.User32.MOD_SHIFT;
    public uint HotkeyKey { get; set; } = (uint)Native.User32.VK_F1;
    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.CopyToClipboard;
    public bool SaveToFile { get; set; }
    public string SaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public bool StartWithWindows { get; set; }
    public CaptureMode LastCaptureMode { get; set; } = CaptureMode.Rectangle;
}
