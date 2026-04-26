using System.Drawing;
using System.Windows.Media.Imaging;

namespace OddSnap.Helpers;

/// <summary>
/// Shared rendering helpers for semantic tool icons.
/// All icons are rendered through the Windows Fluent/MDL2 icon font facade.
/// </summary>
public static class ToolIcons
{
    private static readonly Dictionary<string, string> ToolToIconId = new()
    {
        ["_fullscreen"] = "fullscreen",
        ["_activeWindow"] = "activeWindow",
        ["_scrollCapture"] = "scrollCapture",
        ["_record"] = "record",
    };

    public static BitmapSource RenderToolIconWpf(string toolId, char glyph, Color color, int size, bool active = false)
    {
        var iconId = ToolToIconId.TryGetValue(toolId, out var mapped) ? mapped : toolId;
        return FluentIcons.RenderWpf(iconId, color, size, active)
               ?? FluentIcons.RenderWpf("warning", color, size)!;
    }

    public static BitmapSource RenderStickerWpf(Color color, int size)
        => FluentIcons.RenderWpf("sticker", color, size)
           ?? FluentIcons.RenderWpf("warning", color, size)!;

    public static BitmapSource RenderRecordWpf(Color color, int size)
        => FluentIcons.RenderWpf("record", color, size)
           ?? FluentIcons.RenderWpf("warning", color, size)!;

    public static BitmapSource RenderFolderWpf(Color color, int size)
        => FluentIcons.RenderWpf("folder", color, size)
           ?? FluentIcons.RenderWpf("warning", color, size)!;

    public static BitmapSource RenderAiRedirectWpf(Color color, int size, bool active = false)
        => FluentIcons.RenderWpf("ai_redirect", color, size, active)
           ?? FluentIcons.RenderWpf("search", color, size)!;

    public static BitmapSource RenderGoogleLensWpf(Color color, int size, bool active = false)
        => FluentIcons.RenderWpf("search", color, size, active)
           ?? FluentIcons.RenderWpf("warning", color, size)!;
}
