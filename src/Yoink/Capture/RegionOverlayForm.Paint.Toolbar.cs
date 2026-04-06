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
        var r = new Rectangle(_toolbarRect.X, _toolbarRect.Y,
            _toolbarRect.Width, _toolbarRect.Height);

        PaintFlyoutPanel(g);

        // Pill background -- solid dark, subtle border and shadow
        PaintShadow(g, r, UiChrome.ToolbarHeight / 2f, 70, 1.2f);
        using (var p = RRect(r, UiChrome.ToolbarHeight / 2))
        {
            using var bg = new SolidBrush(UiChrome.SurfacePill);
            using var border = new Pen(UiChrome.SurfaceBorder, 1.4f);
            g.FillPath(bg, p);
            g.DrawPath(border, p);
        }

        // Separator lines at group boundaries
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            using var sepPen = new Pen(Color.FromArgb(18, UiChrome.SurfaceTextPrimary), 1.2f);
            if (IsVerticalDock)
            {
                int sy = _toolbarButtons[idx].Bottom + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
                g.DrawLine(sepPen, r.X + 11, sy, r.Right - 11, sy);
            }
            else
            {
                int sx = _toolbarButtons[idx].Right + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
                g.DrawLine(sepPen, sx, r.Y + 11, sx, r.Bottom - 11);
            }
        }

        // Check if active mode is a flyout tool (to highlight the "more" button)
        bool flyoutToolActive = _flyoutTools.Any(t => t.Mode == _mode);

        for (int i = 0; i < BtnCount; i++)
        {
            var btn = _toolbarButtons[i];
            bool active = _toolbarModes[i] is { } m && _mode == m;
            if (i == _moreButtonIndex) active = flyoutToolActive; // only highlight if a flyout tool is active, not just open
            bool hover = _hoveredButton == i;

            // Color dot button
            if (_toolbarIcons[i] == "color")
            {
                int dotSize = 16;
                float dx = btn.X + (btn.Width - dotSize) / 2f;
                float dy = btn.Y + (btn.Height - dotSize) / 2f;
                int colorAlpha = active ? 255 : hover ? 208 : 154;
                using var cBrush = new SolidBrush(Color.FromArgb(colorAlpha, _toolColor.R, _toolColor.G, _toolColor.B));
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                continue;
            }

            // "More" button: draw three dots instead of icon glyph
            if (_toolbarIcons[i] == "more")
            {
                int dotAlpha = active ? 255 : hover ? 208 : 116;
                using var dotBrush = new SolidBrush(Color.FromArgb(dotAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                float cy = btn.Y + btn.Height / 2f - 1.5f;
                float cx = btn.X + btn.Width / 2f;
                g.FillEllipse(dotBrush, cx - 8f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx - 1.5f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx + 5f, cy, 3f, 3f);
                continue;
            }

            if (active)
                DrawToolbarIconHalo(g, btn, 1f);
            else if (hover)
                DrawToolbarIconHalo(g, btn, 0.4f);

            int ia = active ? 255 : hover ? 202 : i >= BtnCount - 1 ? 106 : 120;
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
            var tipFont = UiChrome.ChromeFont(8.25f, FontStyle.Regular);
            var sz = g.MeasureString(tipText, tipFont);
            float tipW = sz.Width + 20;
            var tipOrigin = GetTooltipOrigin(tipAnchor, new SizeF(tipW, sz.Height + 8));
            float tx = tipOrigin.X + 10;
            float ty = tipOrigin.Y + 4;
            var tipRect = new RectangleF(tx - 10, ty - 4, tipW, sz.Height + 8);
            PaintShadow(g, tipRect, tipRect.Height / 2f, 52, 1f);
            using (var tipPath = RRect(tipRect, tipRect.Height / 2f))
            {
                using var tipBg = new SolidBrush(UiChrome.SurfaceTooltip);
                using var tipBorder = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
                g.FillPath(tipBg, tipPath);
                g.DrawPath(tipBorder, tipPath);
            }
            using var tipFg = new SolidBrush(UiChrome.SurfaceTextPrimary);
            g.DrawString(tipText, tipFont, tipFg, tx, ty);
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    private void PaintFlyoutPanel(Graphics g)
    {
        if ((_flyoutOpen || _flyoutAnim > 0.001f) == false || _flyoutTools.Length == 0)
            return;

        float anim = Math.Clamp(_flyoutAnim, 0f, 1f);
        if (!_flyoutOpen && anim <= 0.02f)
            return;

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
        PaintShadow(g, fr, UiChrome.ToolbarHeight / 2f, 70, 1.2f);
        using (var fp = RRect(fr, UiChrome.ToolbarHeight / 2))
        {
            using var flyBg = new SolidBrush(UiChrome.SurfacePill);
            using var flyBorder = new Pen(UiChrome.SurfaceBorder, 1.4f);
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
                DrawToolbarIconHalo(g, fb, 1f);
            else if (fHover)
                DrawToolbarIconHalo(g, fb, 0.4f);

            int fia = fActive ? 255 : fHover ? 202 : 120;
            var fic = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _flyoutTools[i].Id, fb, Color.FromArgb(fia, fic.R, fic.G, fic.B), fActive);
        }
    }

    private static Color ScaleAlpha(Color color, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return Color.FromArgb((int)Math.Round(color.A * factor), color.R, color.G, color.B);
    }

    private static void DrawToolbarIconHalo(Graphics g, Rectangle bounds, float intensity)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        intensity = Math.Clamp(intensity, 0f, 1f);
        var inset = 2.6f - (0.9f * intensity);
        var haloRect = new RectangleF(
            bounds.X + inset,
            bounds.Y + inset,
            bounds.Width - (inset * 2f),
            bounds.Height - (inset * 2f));
        var baseColor = UiChrome.SurfaceTextPrimary;
        var alpha = 9f + (13f * intensity);
        using var haloBrush = new SolidBrush(Color.FromArgb((int)Math.Round(alpha), baseColor.R, baseColor.G, baseColor.B));
        g.FillEllipse(haloBrush, haloRect);
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>
    /// Called by the separate ToolbarForm to paint toolbar, tooltips, and popups.
    /// Graphics is already translated so overlay coordinates map correctly.
    /// </summary>
    public void PaintToolbarTo(Graphics g, Rectangle clip, Point unused)
    {
        ApplyUiGraphics(g);
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
        int cols = 6, rows = 1, swatchSize = 28, pad = 4;
        int pw = cols * (swatchSize + pad) + pad;
        int ph = rows * (swatchSize + pad) + pad;

        // Position below the color button
        int colorBtnIdx = BtnCount - 3;
        var colorBtn = _toolbarButtons[colorBtnIdx];
        _colorPickerRect = PositionPopupFromAnchor(colorBtn, pw, ph);
        int px = _colorPickerRect.X;
        int py = _colorPickerRect.Y;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintShadow(g, _colorPickerRect, 8f, 58, 1f);
        using (var bgPath = RRect(_colorPickerRect, 8))
        {
            using var bg = new SolidBrush(UiChrome.SurfaceElevated);
            g.FillPath(bg, bgPath);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle);
            g.DrawPath(border, bgPath);
        }

        for (int i = 0; i < ToolColors.Length && i < cols * rows; i++)
        {
            int col = i % cols, row = i / cols;
            int sx = px + pad + col * (swatchSize + pad);
            int sy = py + pad + row * (swatchSize + pad);
            using var brush = new SolidBrush(ToolColors[i]);
            g.FillEllipse(brush, sx, sy, swatchSize, swatchSize);
            if (ToolColors[i] == _toolColor)
            {
                using var selPen = new Pen(UiChrome.SurfaceTextPrimary, 2f);
                g.DrawEllipse(selPen, sx, sy, swatchSize, swatchSize);
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
        if (icon == "rect")
        {
            DrawRectangleSelectIcon(g, b, c, active);
            return;
        }
        if (icon == "free")
        {
            DrawFreeformSelectIcon(g, b, c, active);
            return;
        }
        if (icon == "sticker")
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(b.X + 9.5f, b.Y + 8.5f, b.Width - 19f, b.Height - 19f);
            using var pen = new Pen(c, active ? 2.05f : 1.8f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var path = new GraphicsPath();
            path.AddArc(body.X, body.Y, 6, 6, 180, 90);
            path.AddLine(body.X + 6, body.Y, body.Right - 7, body.Y);
            path.AddLine(body.Right - 7, body.Y, body.Right, body.Y + 7);
            path.AddLine(body.Right, body.Y + 7, body.Right, body.Bottom - 6);
            path.AddArc(body.Right - 6, body.Bottom - 6, 6, 6, 0, 90);
            path.AddArc(body.X, body.Bottom - 6, 6, 6, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);

            g.DrawLine(pen, body.Right - 7, body.Y, body.Right - 7, body.Y + 7);
            g.DrawLine(pen, body.Right - 7, body.Y + 7, body.Right, body.Y + 7);
            g.SmoothingMode = SmoothingMode.Default;
            return;
        }
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

    private static void DrawRectangleSelectIcon(Graphics g, Rectangle b, Color c, bool active = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, active ? 2.2f : 1.95f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var centerX = b.X + (b.Width / 2f);
        var centerY = b.Y + (b.Height / 2f) - 0.65f;
        var size = active ? 15.8f : 15.1f;
        var half = size / 2f;
        float left = centerX - half;
        float top = centerY - half;
        float right = centerX + half;
        float bottom = centerY + half;
        float arm = size * 0.36f;

        g.DrawLine(pen, left, top + arm, left, top);
        g.DrawLine(pen, left, top, left + arm, top);

        g.DrawLine(pen, right - arm, top, right, top);
        g.DrawLine(pen, right, top, right, top + arm);

        g.DrawLine(pen, left, bottom - arm, left, bottom);
        g.DrawLine(pen, left, bottom, left + arm, bottom);

        g.DrawLine(pen, right - arm, bottom, right, bottom);
        g.DrawLine(pen, right, bottom - arm, right, bottom);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private static void DrawFreeformSelectIcon(Graphics g, Rectangle b, Color c, bool active = false)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, active ? 2.15f : 1.9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            DashPattern = new[] { 1.8f, 2.1f }
        };

        var points = new[]
        {
            new PointF(b.X + 8.4f,  b.Y + 18.2f),
            new PointF(b.X + 10.2f, b.Y + 11.6f),
            new PointF(b.X + 16.0f, b.Y + 8.3f),
            new PointF(b.X + 22.6f, b.Y + 10.1f),
            new PointF(b.X + 26.0f, b.Y + 15.9f),
            new PointF(b.X + 23.7f, b.Y + 22.1f),
            new PointF(b.X + 17.2f, b.Y + 24.5f),
            new PointF(b.X + 11.0f, b.Y + 22.2f),
            new PointF(b.X + 8.4f,  b.Y + 18.2f)
        };
        g.DrawCurve(pen, points, 0.45f);

        using var dotBrush = new SolidBrush(c);
        float dotSize = 3.0f;
        g.FillEllipse(dotBrush, points[0].X - dotSize / 2f, points[0].Y - dotSize / 2f, dotSize, dotSize);
        g.SmoothingMode = SmoothingMode.Default;
    }
}
