namespace OddSnap.Models;

/// <summary>Definition of a toolbar tool with id, label, icon, mode, and group.</summary>
public sealed record ToolDef(string Id, string Label, char Icon, CaptureMode? Mode, int Group)
{
    /// <summary>All available tools in display order. Group 0=capture, 1=annotation.</summary>
    public static readonly ToolDef[] AllTools =
    {
        new("rect",        "Rectangle Select", '\uE257', CaptureMode.Rectangle, 0),
        new("center",      "Center Select",    '\uE257', CaptureMode.Center,    0),
        new("free",        "Freeform Select",  '\uE1CE', CaptureMode.Freeform,  0),
        new("ocr",         "OCR",          '\uE53C', CaptureMode.Ocr,         0),
        new("sticker",     "Sticker",      ToolGlyphs.StickerGlyph, CaptureMode.Sticker,     0),
        new("upscale",     "Upscale",      ToolGlyphs.UpscaleGlyph, CaptureMode.Upscale,     0),
        new("picker",      "Color Picker", '\uE13E', CaptureMode.ColorPicker, 0),
        new("scan",        "QR/Barcode",   '\uE1DE', CaptureMode.Scan,        0),
        new("select",      "Select",       '\uE1E3', CaptureMode.Select,      1),
        new("arrow",       "Arrow",        '\uE051', CaptureMode.Arrow,       1),
        new("curvedArrow", "Curved Arrow", '\uE146', CaptureMode.CurvedArrow, 1),
        new("text",        "Text",         '\uE197', CaptureMode.Text,        1),
        new("highlight",   "Highlight",    '\uE0F7', CaptureMode.Highlight,   1),
        new("blur",        "Blur",         '\uE5A0', CaptureMode.Blur,        1),
        new("step",        "Step Number",  '\uE1D0', CaptureMode.StepNumber,  1),
        new("draw",        "Draw",         '\uE1F8', CaptureMode.Draw,        1),
        new("line",        "Line",         '\uE11F', CaptureMode.Line,        1),
        new("ruler",       "Ruler",        '\uE14E', CaptureMode.Ruler,       1),
        new("magnifier",   "Magnifier",    '\uE721', CaptureMode.Magnifier,   1),
        new("rectShape",   "Rectangle",    '\uE16A', CaptureMode.RectShape,   1),
        new("circleShape", "Circle",       '\uE07A', CaptureMode.CircleShape, 1),
        new("emoji",       "Emoji",        '\uE167', CaptureMode.Emoji,       1),
        new("eraser",      "Eraser",       '\uE28E', CaptureMode.Eraser,      1),
    };

    /// <summary>Toolbar actions that launch a capture flow instead of selecting an overlay mode.</summary>
    public static readonly ToolDef[] ToolbarActions =
    {
        new("_fullscreen",    "Fullscreen capture", ToolGlyphs.FullscreenGlyph, null, 2),
        new("_activeWindow",  "Active window",      ToolGlyphs.ActiveWindowGlyph, null, 2),
        new("_scrollCapture", "Scroll capture",     ToolGlyphs.ScrollCaptureGlyph, null, 2),
        new("_record",        "Record",             ToolGlyphs.RecordGlyph, null, 2),
    };

    public static ToolDef[] AllToolbarItems() =>
        AllTools.Where(t => t.Group == 0)
            .Concat(ToolbarActions)
            .Concat(AllTools.Where(t => t.Group == 1))
            .ToArray();

    public static List<string> DefaultToolbarOrderIds() =>
        AllToolbarItems().Select(t => t.Id).ToList();

    public static List<string> DefaultPinnedToolbarIds() =>
        AllTools.Where(t => t.Group == 0).Select(t => t.Id).ToList();

    public static bool IsCaptureTool(CaptureMode mode) =>
        AllTools.Any(t => t.Mode == mode && t.Group == 0);

    public static bool IsAnnotationTool(CaptureMode mode) =>
        AllTools.Any(t => t.Mode == mode && t.Group == 1);

    public static List<string> DefaultEnabledIds() =>
        AllTools.Select(t => t.Id).ToList();

    public static HashSet<string> FlyoutToolIds() =>
        new(AllTools.Where(t => t.Group == 1).Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
}
