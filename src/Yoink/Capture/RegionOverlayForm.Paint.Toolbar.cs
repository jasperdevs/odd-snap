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
using Yoink.Helpers;
using Yoink.Models;

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private void PaintToolbar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y,
            _toolbarRect.Width, _toolbarRect.Height);

        PaintFlyoutPanel(g);

        float cr = UiChrome.ToolbarCornerRadius;

        // Rounded-rect background with depth
        PaintShadow(g, r, cr, 55, 1.2f);
        using (var p = RRect(r, cr))
        {
            using var bg = new SolidBrush(UiChrome.SurfacePill);
            g.FillPath(bg, p);

            // Fluent top-edge gradient highlight (bright at top, fades to invisible at bottom)
            var highlightRect = new RectangleF(r.X + 1f, r.Y + 0.5f, r.Width - 2f, r.Height - 1f);
            using var hp = RRect(highlightRect, cr - 0.5f);
            using var gradBrush = new LinearGradientBrush(
                new PointF(r.X, r.Y),
                new PointF(r.X, r.Bottom),
                Color.FromArgb(UiChrome.IsDark ? 48 : 60, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var highlightPen = new Pen(gradBrush, 1f);
            g.DrawPath(highlightPen, hp);

            // Outer border
            using var border = new Pen(UiChrome.SurfaceBorder, 1f);
            g.DrawPath(border, p);
        }

        // Separator lines at group boundaries
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            using var sepPen = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
            if (IsVerticalDock)
            {
                int sy = _toolbarButtons[idx].Bottom + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
                g.DrawLine(sepPen, r.X + 10, sy, r.Right - 10, sy);
            }
            else
            {
                int sx = _toolbarButtons[idx].Right + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
                g.DrawLine(sepPen, sx, r.Y + 12, sx, r.Bottom - 12);
            }
        }

        // Check if active mode is a flyout tool (to highlight the "more" button)
        bool flyoutToolActive = _flyoutTools.Any(t => t.Mode == _mode);

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = _toolbarButtons[i];
            bool active = _toolbarModes[i] is { } m && _mode == m;
            if (i == _moreButtonIndex) active = flyoutToolActive;
            bool hover = _hoveredButton == i;

            // Color dot button
            if (_toolbarIcons[i] == "color")
            {
                if (hover) DrawToolbarButtonBackground(g, btn, 0.5f);
                else if (active) DrawToolbarButtonBackground(g, btn, 1f);
                int dotSize = 16;
                float dx = btn.X + (btn.Width - dotSize) / 2f;
                float dy = btn.Y + (btn.Height - dotSize) / 2f;
                int colorAlpha = active ? 255 : hover ? 230 : 175;
                using var cBrush = new SolidBrush(Color.FromArgb(colorAlpha, _toolColor.R, _toolColor.G, _toolColor.B));
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                continue;
            }

            // "More" button
            if (_toolbarIcons[i] == "more")
            {
                if (active) DrawToolbarButtonBackground(g, btn, 1f);
                else if (hover) DrawToolbarButtonBackground(g, btn, 0.5f);
                int dotAlpha = active ? 255 : hover ? 240 : 200;
                var moreColor = Color.FromArgb(dotAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B);
                DrawIcon(g, "more", btn, moreColor, active);
                continue;
            }

            if (active)
                DrawToolbarButtonBackground(g, btn, 1f);
            else if (hover)
                DrawToolbarButtonBackground(g, btn, 0.5f);

            int ia = active ? 255 : hover ? 240 : i >= BtnCount - 1 ? 130 : 200;
            var iconColor = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _toolbarIcons[i], btn, Color.FromArgb(ia, iconColor.R, iconColor.G, iconColor.B), active);
        }

        // Tooltip for main bar or flyout
        string? tipText = null;
        Rectangle tipAnchor = default;

        if (_hoveredButton >= 0 && _hoveredButton < _toolbarLabels.Length)
        {
            tipText = _toolbarLabels[_hoveredButton];
            tipAnchor = _toolbarButtons[_hoveredButton];

            if (_hoveredButton < _mainBarTools.Length)
            {
                var tool = _mainBarTools[_hoveredButton];
                if (tool.Group == 1 || tool.Group == 0)
                {
                    var hk = Services.SettingsService.LoadStatic()?.GetToolHotkey(tool.Id) ?? (0u, 0u);
                    if (hk.key != 0)
                        tipText += $"  ({Helpers.HotkeyFormatter.Format(hk.mod, hk.key)})";
                }
            }
        }
        else if (_flyoutOpen && _hoveredFlyoutButton >= 0 && _hoveredFlyoutButton < _flyoutTools.Length)
        {
            tipText = _flyoutTools[_hoveredFlyoutButton].Label;
            tipAnchor = _flyoutButtonRects[_hoveredFlyoutButton];
            var hk = Services.SettingsService.LoadStatic()?.GetToolHotkey(_flyoutTools[_hoveredFlyoutButton].Id) ?? (0u, 0u);
            if (hk.key != 0)
                tipText += $"  ({Helpers.HotkeyFormatter.Format(hk.mod, hk.key)})";
        }

        if (tipText != null)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var tipFont = UiChrome.ChromeFont(8.5f, FontStyle.Regular);
            var sz = g.MeasureString(tipText, tipFont);
            float tipW = sz.Width + 20;
            var tipOrigin = GetTooltipOrigin(tipAnchor, new SizeF(tipW, sz.Height + 10));
            float tx = tipOrigin.X + 10;
            float ty = tipOrigin.Y + 5;
            var tipRect = new RectangleF(tx - 10, ty - 5, tipW, sz.Height + 10);
            PaintShadow(g, tipRect, 6f, 40, 1.5f);
            using (var tipPath = RRect(tipRect, 6f))
            {
                using var tipBg = new SolidBrush(UiChrome.SurfaceTooltip);
                using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1f);
                g.FillPath(tipBg, tipPath);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipFg = new SolidBrush(UiChrome.SurfaceTextPrimary);
            g.DrawString(tipText, tipFont, tipFg, tx, ty);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        g.SmoothingMode = SmoothingMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }

    private void PaintFlyoutPanel(Graphics g)
    {
        if ((_flyoutOpen || _flyoutAnim > 0.001f) == false || _flyoutTools.Length == 0)
            return;

        float anim = Math.Clamp(_flyoutAnim, 0f, 1f);
        if (!_flyoutOpen && anim <= 0.02f)
            return;

        float cr = UiChrome.ToolbarCornerRadius;

        // Scale-in effect: flyout grows from a slightly smaller size
        float scale = 0.92f + 0.08f * anim;

        Rectangle hiddenRect;
        if (IsVerticalDock)
        {
            hiddenRect = new Rectangle(
                _toolbarRect.X + ((_toolbarRect.Width - _flyoutRect.Width) / 2),
                _flyoutRect.Y,
                _flyoutRect.Width,
                _flyoutRect.Height);
        }
        else
        {
            hiddenRect = new Rectangle(
                _flyoutRect.X,
                _toolbarRect.Y + ((_toolbarRect.Height - _flyoutRect.Height) / 2),
                _flyoutRect.Width,
                _flyoutRect.Height);
        }

        var fr = new Rectangle(
            hiddenRect.X + (int)Math.Round((_flyoutRect.X - hiddenRect.X) * anim),
            hiddenRect.Y + (int)Math.Round((_flyoutRect.Y - hiddenRect.Y) * anim),
            _flyoutRect.Width,
            _flyoutRect.Height);

        // Apply scale around the center of the flyout
        int scaledW = (int)Math.Round(fr.Width * scale);
        int scaledH = (int)Math.Round(fr.Height * scale);
        var scaledFr = new Rectangle(
            fr.X + (fr.Width - scaledW) / 2,
            fr.Y + (fr.Height - scaledH) / 2,
            scaledW, scaledH);

        // Fade-in opacity for shadow and panel
        int shadowAlpha = (int)Math.Round(55 * anim);
        PaintShadow(g, scaledFr, cr, shadowAlpha, 1.2f);
        using (var fp = RRect(scaledFr, cr))
        {
            var elevatedColor = UiChrome.SurfaceElevated;
            int pillAlpha = (int)Math.Round(elevatedColor.A * anim);
            using var flyBg = new SolidBrush(Color.FromArgb(Math.Clamp(pillAlpha, 1, 255), elevatedColor.R, elevatedColor.G, elevatedColor.B));
            using var flyBorder = new Pen(Color.FromArgb((int)Math.Round(UiChrome.SurfaceBorder.A * anim), UiChrome.SurfaceBorder.R, UiChrome.SurfaceBorder.G, UiChrome.SurfaceBorder.B), 1f);
            g.FillPath(flyBg, fp);
            g.DrawPath(flyBorder, fp);
        }

        for (int i = 0; i < _flyoutTools.Length; i++)
        {
            var baseRect = _flyoutButtonRects[i];
            var hiddenButtonRect = IsVerticalDock
                ? new Rectangle(
                    _toolbarRect.X + ((_toolbarRect.Width - baseRect.Width) / 2),
                    baseRect.Y,
                    baseRect.Width,
                    baseRect.Height)
                : new Rectangle(
                    baseRect.X,
                    _toolbarRect.Y + ((_toolbarRect.Height - baseRect.Height) / 2),
                    baseRect.Width,
                    baseRect.Height);
            var fb = new Rectangle(
                hiddenButtonRect.X + (int)Math.Round((baseRect.X - hiddenButtonRect.X) * anim),
                hiddenButtonRect.Y + (int)Math.Round((baseRect.Y - hiddenButtonRect.Y) * anim),
                baseRect.Width,
                baseRect.Height);
            bool fActive = _flyoutTools[i].Mode is { } fm && _mode == fm;
            bool fHover = _hoveredFlyoutButton == i;

            if (fActive)
                DrawToolbarButtonBackground(g, fb, 1f);
            else if (fHover)
                DrawToolbarButtonBackground(g, fb, 0.5f);

            int baseAlpha = fActive ? 255 : fHover ? 240 : 200;
            int fia = (int)Math.Round(baseAlpha * anim);
            var fic = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _flyoutTools[i].Id, fb, Color.FromArgb(fia, fic.R, fic.G, fic.B), fActive);
        }
    }

    private static Color ScaleAlpha(Color color, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return Color.FromArgb((int)Math.Round(color.A * factor), color.R, color.G, color.B);
    }

    private static void DrawToolbarButtonBackground(Graphics g, Rectangle bounds, float intensity)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        intensity = Math.Clamp(intensity, 0f, 1f);
        float inset = 2f;
        var bgRect = new RectangleF(
            bounds.X + inset,
            bounds.Y + inset,
            bounds.Width - (inset * 2f),
            bounds.Height - (inset * 2f));
        // Fluent: hover=15 alpha, active/selected=20 alpha
        int alpha = intensity >= 0.9f ? 20 : 15;
        using var bgBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        float radius = 5f;
        using var bgPath = RRect(bgRect, radius);
        g.FillPath(bgBrush, bgPath);
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
        PaintShadow(g, _colorPickerRect, 8f, 55, 1.2f);
        using (var bgPath = RRect(_colorPickerRect, 8))
        {
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);

            // Fluent gradient highlight
            var cpHlRect = new RectangleF(_colorPickerRect.X + 1f, _colorPickerRect.Y + 0.5f, _colorPickerRect.Width - 2f, _colorPickerRect.Height - 1f);
            using var cpHlPath = RRect(cpHlRect, 7.5f);
            using var cpGrad = new LinearGradientBrush(
                new PointF(_colorPickerRect.X, _colorPickerRect.Y),
                new PointF(_colorPickerRect.X, _colorPickerRect.Bottom),
                Color.FromArgb(UiChrome.IsDark ? 48 : 60, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var cpHlPen = new Pen(cpGrad, 1f);
            g.DrawPath(cpHlPen, cpHlPath);

            using var border = new Pen(UiChrome.SurfaceBorder);
            g.DrawPath(border, bgPath);
        }

        for (int i = 0; i < ToolColors.Length && i < ColorPickerColumns * ColorPickerRows; i++)
        {
            var swatchRect = GetColorPickerSwatchRect(i);
            using var brush = new SolidBrush(ToolColors[i]);
            g.FillEllipse(brush, swatchRect);
            if (ToolColors[i] == _toolColor)
            {
                using var selPen = new Pen(UiChrome.SurfaceTextPrimary, 2f);
                g.DrawEllipse(selPen, swatchRect);
            }
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    // Fixed button glyphs (not in ToolDef)
    private static readonly Dictionary<string, char> FixedGlyphs = new()
    {
        ["gear"]  = '\uE157', // lucide settings
        ["close"] = '\uE1B1', // lucide x
        ["more"]  = '\uE0D4', // lucide ellipsis (more-horizontal)
    };

    private static Font? _iconFontCached;
    private static Font GetIconFont() => _iconFontCached ??= IconFont.Create(UiChrome.IconGlyphSize);

    private static readonly StringFormat _iconFmt = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoClip
    };

    // Cached lookup for icon id -> glyph char (avoids LINQ FirstOrDefault per paint)
    private static Dictionary<string, char>? _iconGlyphCache;
    private static Dictionary<string, char> GetIconGlyphMap()
    {
        if (_iconGlyphCache != null) return _iconGlyphCache;
        _iconGlyphCache = new Dictionary<string, char>(ToolDef.AllTools.Length + FixedGlyphs.Count);
        foreach (var t in ToolDef.AllTools)
            _iconGlyphCache[t.Id] = t.Icon;
        foreach (var kv in FixedGlyphs)
            _iconGlyphCache[kv.Key] = kv.Value;
        return _iconGlyphCache;
    }

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c, bool active = false)
    {
        if (icon == "color") return;

        // Try Streamline icon first (line=inactive, solid=active)
        if (StreamlineIcons.HasIcon(icon))
        {
            float inset = active ? 6f : 7f;
            StreamlineIcons.DrawIcon(g, icon, b, c, inset, active);
            return;
        }

        // Fallback to font glyph
        if (!GetIconGlyphMap().TryGetValue(icon, out char glyph)) return;

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var activeFont = active ? IconFont.Create(UiChrome.IconGlyphSize + 0.3f) : null;
        var font = activeFont ?? GetIconFont();
        using var brush = new SolidBrush(c);
        var rect = new RectangleF(
            b.X + 1.6f,
            b.Y + 1.9f,
            b.Width - 3.2f,
            b.Height - 3.8f);
        g.DrawString(glyph.ToString(), font, brush, rect, _iconFmt);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }
}
