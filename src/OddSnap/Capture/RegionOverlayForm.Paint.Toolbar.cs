using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
using System.Globalization;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    private static Pen? _swatchSelectionPen;
    private static int _swatchSelectionPenKey;

    private static Pen GetSwatchSelectionPen()
    {
        int key = UiChrome.SurfaceTextPrimary.ToArgb();
        if (_swatchSelectionPen is null || _swatchSelectionPenKey != key)
        {
            _swatchSelectionPen?.Dispose();
            _swatchSelectionPen = new Pen(UiChrome.SurfaceTextPrimary, 2f);
            _swatchSelectionPenKey = key;
        }
        return _swatchSelectionPen;
    }

    private void PaintToolbar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y,
            _toolbarRect.Width, _toolbarRect.Height);

        float cr = UiChrome.ScaledToolbarCornerRadius;

        WindowsDockRenderer.PaintSurface(g, r, cr);

        // Separator lines at group boundaries
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            if (IsVerticalDock)
            {
                int sy = _toolbarButtons[idx].Bottom + (UiChrome.ScaledToolbarButtonSpacing + GroupGap) / 2;
                var inset = UiChrome.ScaleInt(10);
                WindowsDockRenderer.PaintDivider(g, new Point(r.X + inset, sy), new Point(r.Right - inset, sy));
            }
            else
            {
                int sx = _toolbarButtons[idx].Right + (UiChrome.ScaledToolbarButtonSpacing + GroupGap) / 2;
                var inset = UiChrome.ScaleInt(12);
                WindowsDockRenderer.PaintDivider(g, new Point(sx, r.Y + inset), new Point(sx, r.Bottom - inset));
            }
        }

        // Check if active mode is a flyout tool (to highlight the "more" button)
        bool flyoutToolActive = _flyoutOpen || _flyoutTools.Any(t => string.Equals(t.Id, _activeToolId, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = _toolbarButtons[i];
            bool active = _toolbarModes[i] is { } && string.Equals(_toolbarToolIds[i], _activeToolId, StringComparison.OrdinalIgnoreCase);
            if (i == _moreButtonIndex) active = flyoutToolActive;
            bool hover = _hoveredButton == i;

            // Color dot button
            if (_toolbarIcons[i] == "color")
            {
                WindowsDockRenderer.PaintButton(g, btn, active, hover);
                int dotSize = 16;
                float dx = btn.X + (btn.Width - dotSize) / 2f;
                float dy = btn.Y + (btn.Height - dotSize) / 2f;
                int colorAlpha = active ? 255 : hover ? 230 : 175;
                g.FillEllipse(SketchRenderer.GetToolColorBrush(Color.FromArgb(colorAlpha, _toolColor.R, _toolColor.G, _toolColor.B)), dx, dy, dotSize, dotSize);
                continue;
            }

            // "More" button
            if (_toolbarIcons[i] == "more")
            {
                WindowsDockRenderer.PaintButton(g, btn, active, hover);
                int dotAlpha = active ? 255 : hover ? 240 : 200;
                var moreColor = Color.FromArgb(dotAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B);
                DrawIcon(g, "more", btn, moreColor, active);
                continue;
            }

            WindowsDockRenderer.PaintButton(g, btn, active, hover);

            int ia = active ? 255 : hover ? 240 : i >= BtnCount - 1 ? 130 : 200;
            var iconColor = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _toolbarIcons[i], btn, Color.FromArgb(ia, iconColor.R, iconColor.G, iconColor.B), active);
        }

        g.SmoothingMode = SmoothingMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }

    /// <summary>
    /// Called by the separate ToolbarForm to paint toolbar, tooltips, and popups.
    /// Graphics is already translated so overlay coordinates map correctly.
    /// </summary>
    public void PaintToolbarTo(Graphics g)
    {
        ApplyUiGraphics(g);
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var state = g.Save();
        PaintToolbar(g);
        if (_colorPickerOpen) PaintColorPicker(g);
        if (_emojiPickerOpen) PaintEmojiPicker(g);
        if (_fontPickerOpen) PaintFontPicker(g);
        g.Restore(state);
    }

    private void PaintColorPicker(Graphics g)
    {
        // Small popup grid of color swatches
        int pw = ColorPickerColumns * (ColorPickerSwatchSize + ColorPickerPadding) + ColorPickerPadding;
        int ph = ColorPickerRows * (ColorPickerSwatchSize + ColorPickerPadding) + ColorPickerPadding;

        // Position below the color button
        var colorBtn = _toolbarButtons[ColorButtonIndex];
        _colorPickerRect = PositionPopupFromAnchor(colorBtn, pw, ph);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        WindowsDockRenderer.PaintSurface(g, _colorPickerRect);

        for (int i = 0; i < ToolColors.Length && i < ColorPickerColumns * ColorPickerRows; i++)
        {
            var swatchRect = GetColorPickerSwatchRect(i);
            g.FillEllipse(SketchRenderer.GetToolColorBrush(ToolColors[i]), swatchRect);
            if (ToolColors[i] == _toolColor)
                g.DrawEllipse(GetSwatchSelectionPen(), swatchRect);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static readonly StringFormat _iconFmt = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoClip
    };

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c, bool active = false)
    {
        if (icon == "color") return;

        // Try Streamline icon first (line=inactive, solid=active)
        if (FluentIcons.HasIcon(icon))
        {
            float inset = active ? 6f : 7f;
            FluentIcons.DrawIcon(g, icon, b, c, inset, active);
            return;
        }

        return;
    }
}
