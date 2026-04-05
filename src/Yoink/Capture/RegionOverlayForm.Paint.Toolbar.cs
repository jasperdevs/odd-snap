using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
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
        int sepY1 = r.Y + 10;
        int sepY2 = r.Bottom - 10;
        foreach (int idx in _sepAfter)
        {
            if (idx < 0 || idx >= _toolbarButtons.Length - 1) continue;
            int sx = _toolbarButtons[idx].Right + (UiChrome.ToolbarButtonSpacing + GroupGap) / 2;
            using var sepPen = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
            g.DrawLine(sepPen, sx, sepY1, sx, sepY2);
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
                if (hover)
                {
                    using var hoverBrush = new SolidBrush(UiChrome.SurfaceHover);
                    g.FillEllipse(hoverBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
                }
                int dotSize = 16;
                float dx = btn.X + (btn.Width - dotSize) / 2f;
                float dy = btn.Y + (btn.Height - dotSize) / 2f;
                using var cBrush = new SolidBrush(_toolColor);
                g.FillEllipse(cBrush, dx, dy, dotSize, dotSize);
                continue;
            }

            // "More" button: draw three dots instead of icon glyph
            if (_toolbarIcons[i] == "more")
            {
                if (active || hover)
                {
                    var hlColor = active
                        ? Color.FromArgb(32, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B)
                        : UiChrome.SurfaceHover;
                    using var hlBrush = new SolidBrush(hlColor);
                    g.FillEllipse(hlBrush, btn.X + 1f, btn.Y + 1f, btn.Width - 2f, btn.Height - 2f);
                }
                int dotAlpha = active ? 255 : hover ? 220 : 165;
                using var dotBrush = new SolidBrush(Color.FromArgb(dotAlpha, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                float cy = btn.Y + btn.Height / 2f - 1.5f;
                float cx = btn.X + btn.Width / 2f;
                g.FillEllipse(dotBrush, cx - 8f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx - 1.5f, cy, 3f, 3f);
                g.FillEllipse(dotBrush, cx + 5f, cy, 3f, 3f);
                continue;
            }

            // Active/hover circle highlight
            if (active)
            {
                using var activeBrush = new SolidBrush(Color.FromArgb(18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                g.FillEllipse(activeBrush, btn.X + 3f, btn.Y + 3f, btn.Width - 6f, btn.Height - 6f);
            }
            else if (hover)
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                g.FillEllipse(hoverBrush, btn.X + 3f, btn.Y + 3f, btn.Width - 6f, btn.Height - 6f);
            }

            int ia = active ? 255 : hover ? 220 : i >= BtnCount - 1 ? 135 : 165;
            var iconColor = UiChrome.SurfaceTextPrimary;
            DrawIcon(g, _toolbarIcons[i], btn, Color.FromArgb(ia, iconColor.R, iconColor.G, iconColor.B));
        }

        // Flyout panel (above toolbar)
        if (_flyoutOpen && _flyoutTools.Length > 0)
        {
            var fr = _flyoutRect;
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
                var fb = _flyoutButtonRects[i];
                bool fActive = _flyoutTools[i].Mode is { } fm && _mode == fm;
                bool fHover = _hoveredFlyoutButton == i;

                if (fActive)
                {
                    using var ab = new SolidBrush(Color.FromArgb(18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                    g.FillEllipse(ab, fb.X + 4f, fb.Y + 4f, fb.Width - 8f, fb.Height - 8f);
                }
                else if (fHover)
                {
                    using var hb = new SolidBrush(Color.FromArgb(18, UiChrome.SurfaceTextPrimary.R, UiChrome.SurfaceTextPrimary.G, UiChrome.SurfaceTextPrimary.B));
                    g.FillEllipse(hb, fb.X + 4f, fb.Y + 4f, fb.Width - 8f, fb.Height - 8f);
                }

                int fia = fActive ? 255 : fHover ? 220 : 165;
                var fic = UiChrome.SurfaceTextPrimary;
                DrawIcon(g, _flyoutTools[i].Id, fb, Color.FromArgb(fia, fic.R, fic.G, fic.B));
            }
        }

        // Tooltip for main bar or flyout
        string? tipText = null;
        Rectangle tipAnchor = default;
        float tipBelow = r.Bottom + 6;

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
            tipBelow = _flyoutRect.Bottom + 6; // tooltip below the flyout

            var hk = Services.SettingsService.LoadStatic()?.GetToolHotkey(_flyoutTools[_hoveredFlyoutButton].Id) ?? (0u, 0u);
            if (hk.key != 0)
                tipText += $"  ({Helpers.HotkeyFormatter.Format(hk.mod, hk.key)})";
        }

        if (tipText != null)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var tipFont = UiChrome.ChromeFont(8.25f, FontStyle.Regular);
            var sz = g.MeasureString(tipText, tipFont);
            float tx = tipAnchor.X + tipAnchor.Width / 2f - sz.Width / 2f;
            float ty = tipBelow;
            float tipW = sz.Width + 20;
            if (tx - 10 < 4) tx = 14;
            if (tx - 10 + tipW > Width - 4) tx = Width - 4 - tipW + 10;
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
        int px = colorBtn.X + colorBtn.Width / 2 - pw / 2;
        int py = colorBtn.Y + colorBtn.Height + 8;

        _colorPickerRect = new Rectangle(px, py, pw, ph);

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

    private static void DrawIcon(Graphics g, string icon, Rectangle b, Color c)
    {
        if (icon == "color") return;
        if (icon == "sticker")
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var body = new RectangleF(b.X + 9.5f, b.Y + 8.5f, b.Width - 19f, b.Height - 19f);
            using var pen = new Pen(c, 1.8f)
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
        var font = GetIconFont();
        using var brush = new SolidBrush(c);
        var rect = new RectangleF(b.X, b.Y, b.Width, b.Height);
        g.DrawString(glyph.ToString(), font, brush, rect, _iconFmt);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }
}
