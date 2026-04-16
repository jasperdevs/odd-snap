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

// Unified dash/border constants for consistency across all selection borders
// Every dashed border in the app uses these same values.

namespace Yoink.Capture;

public sealed partial class RegionOverlayForm
{
    private static void ApplyUiGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.None;

        // Blit the cached screenshot + committed annotations layer.
        var clip = e.ClipRectangle;
        var committed = GetCommittedAnnotationsBitmap();
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(committed, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        bool isOcr = _mode == CaptureMode.Ocr;
        bool isScan = _mode == CaptureMode.Scan;
        bool isSelectionMode = _mode is CaptureMode.Rectangle or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker or CaptureMode.Upscale;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Live tool previews (active drawing in progress)
        PaintAnnotations(g);

        // Select tool: draw selection highlight and handles
        if (_mode == CaptureMode.Select && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            var bounds = GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var selRect = Rectangle.Inflate(bounds, 4, 4);
                var selPen = _selectDashPen ??= new Pen(Color.FromArgb(200, 255, 255, 255), 2.0f) { DashStyle = DashStyle.Dash, DashPattern = new[] { 4f, 3f } };
                g.DrawRectangle(selPen, selRect);

                // Corner handles
                int hs = SelectHandleSize;
                var hBrush = _selectHandleBrush ??= new SolidBrush(UiChrome.SurfaceTextPrimary);
                var hPen = _selectHandlePen ??= new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
                var corners = new[] {
                    new Rectangle(selRect.X - hs / 2, selRect.Y - hs / 2, hs, hs),
                    new Rectangle(selRect.Right - 1 - hs / 2, selRect.Y - hs / 2, hs, hs),
                    new Rectangle(selRect.X - hs / 2, selRect.Bottom - 1 - hs / 2, hs, hs),
                    new Rectangle(selRect.Right - 1 - hs / 2, selRect.Bottom - 1 - hs / 2, hs, hs),
                };
                foreach (var c in corners) { g.FillRectangle(hBrush, c); g.DrawRectangle(hPen, c); }
            }
        }

        if (_mode == CaptureMode.ColorPicker)
            return; // magnifier is its own layered window, overlay stays static

        if (isSelectionMode && !_isSelecting && !_hasSelection && _autoDetectActive && _autoDetectRect.Width > 0)
        {
            // Clamp the rect so dashes stay within the visible client area
            var drawRect = ClampRectToClient(_autoDetectRect);
            if (drawRect.Width > 0 && drawRect.Height > 0)
            {
                g.DrawRectangle(ShadowPen(30), drawRect.X + 1, drawRect.Y + 1, drawRect.Width, drawRect.Height);
                g.DrawRectangle(DashedPen(180), drawRect);
            }
            _lastAutoDetectRect = _autoDetectRect;
        }
        else if (isSelectionMode && !_hasSelection && !_isSelecting)
        {
            _lastAutoDetectRect = Rectangle.Empty;
        }

        // Selection borders (on top of everything)
        switch (_mode)
        {
            case CaptureMode.Rectangle when _hasSelection:
            case CaptureMode.Ocr when _hasSelection:
            case CaptureMode.Scan when _hasSelection:
            case CaptureMode.Sticker when _hasSelection:
            case CaptureMode.Upscale when _hasSelection:
                // Subtle outer shadow
                {
                    var sr = _selectionRect;
                    sr.Inflate(1, 1);
                    g.DrawRectangle(ShadowPen(40), sr);
                }
                // Static dashed border
                g.DrawRectangle(DashedPen(255), _selectionRect);
                DrawLabel(g, _selectionRect, isOcr, isScan);
                _lastSelectionRect = _selectionRect;
                break;

            case CaptureMode.Freeform when _freeformPoints.Count >= 2:
                DrawFreeformSelectionPreview(g, _freeformPoints);
                break;
        }

        if (!_hasSelection)
            _lastSelectionRect = Rectangle.Empty;

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Clamp a rectangle so it stays 2px inside the client area (prevents dashes from being cut off at screen edges).</summary>
    private Rectangle ClampRectToClient(Rectangle rect)
    {
        const int pad = 2;
        int x = Math.Max(pad, rect.X);
        int y = Math.Max(pad, rect.Y);
        int right = Math.Min(ClientSize.Width - pad - 1, rect.Right);
        int bottom = Math.Min(ClientSize.Height - pad - 1, rect.Bottom);
        return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    // Cached GDI objects for hot-path rendering (avoid allocation per frame).
    private static Pen? _cachedDash180, _cachedDash255, _cachedDash120, _cachedDash220;
    private static Pen? _cachedShadow30, _cachedShadow40;
    private static Pen? _selectDashPen, _selectHandlePen;
    private static SolidBrush? _selectHandleBrush;

    // Monochrome white accent for selection borders
    private static readonly Color SelectionAccent = Color.FromArgb(255, 255, 255, 255);

    /// <summary>Cached selection pen — dashed white stroke.</summary>
    private static Pen DashedPen(int alpha, float width = 2.0f)
    {
        // Fast path: return cached instance for the common alpha values used every frame.
        ref Pen? slot = ref _cachedDash180; // dummy init
        if (width == 2.0f)
        {
            switch (alpha)
            {
                case 120: slot = ref _cachedDash120; break;
                case 180: slot = ref _cachedDash180; break;
                case 220: slot = ref _cachedDash220; break;
                case 255: slot = ref _cachedDash255; break;
                default: slot = ref _cachedDash180; goto create;
            }
            if (slot != null) return slot;
        }
        else goto create;

        create:
        var pen = new Pen(Color.FromArgb(alpha, SelectionAccent.R, SelectionAccent.G, SelectionAccent.B), width)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 4f, 3f },
            LineJoin = LineJoin.Miter
        };
        if (width == 2.0f && (alpha is 120 or 180 or 220 or 255))
            slot = pen;
        return pen;
    }

    private static Pen ShadowPen(int alpha)
    {
        ref Pen? slot = ref alpha == 30 ? ref _cachedShadow30 : ref _cachedShadow40;
        return slot ??= new Pen(Color.FromArgb(alpha, 0, 0, 0), 4f);
    }

    private static void DrawFreeformSelectionPreview(Graphics g, List<Point> pts)
    {
        if (pts.Count < 2)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shadowPen = new Pen(Color.FromArgb(30, 0, 0, 0), 4f) { LineJoin = LineJoin.Round };
        using var outlinePen = new Pen(Color.FromArgb(255, SelectionAccent.R, SelectionAccent.G, SelectionAccent.B), 2.0f)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 4f, 3f },
            LineJoin = LineJoin.Round
        };
        var path = pts.ToArray();
        g.DrawLines(shadowPen, path);
        g.DrawLines(outlinePen, path);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void DrawLabel(Graphics g, Rectangle rect, bool isOcr, bool isScan = false)
    {
        string text = GetSelectionLabelText(rect, isOcr, isScan);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var font = UiChrome.ChromeFont(10f);
        var lr = GetLabelBounds(rect, isOcr, isScan, text, font, out float lx, out float ly);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = RRect(lr, 8))
        {
            using var lblBg = new SolidBrush(UiChrome.SurfacePill);
            g.FillPath(lblBg, p);
            using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
            g.DrawPath(border, p);
        }
        g.SmoothingMode = SmoothingMode.Default;
        using var fg = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.DrawString(text, font, fg, lx, ly);
        g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    private static string GetSelectionLabelText(Rectangle rect, bool isOcr, bool isScan)
        => isOcr ? $"OCR  {rect.Width} x {rect.Height}"
        : isScan ? $"SCAN  {rect.Width} x {rect.Height}"
        : $"{rect.Width} x {rect.Height}";

    private RectangleF GetLabelBounds(Rectangle rect, bool isOcr, bool isScan)
    {
        string text = GetSelectionLabelText(rect, isOcr, isScan);
        var font = UiChrome.ChromeFont(10f);
        return GetLabelBounds(rect, isOcr, isScan, text, font, out _, out _);
    }

    private RectangleF GetLabelBounds(Rectangle rect, bool isOcr, bool isScan, string text, Font font, out float lx, out float ly)
    {
        var sz = TextRenderer.MeasureText(text, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        lx = rect.X;
        ly = rect.Bottom + 8;
        if (ly + sz.Height > ClientSize.Height) ly = rect.Y - sz.Height - 8;
        return new RectangleF(lx - 8, ly - 3, sz.Width + 16, sz.Height + 6);
    }

    private static void PaintShadow(Graphics g, RectangleF rect, float radius, int alpha = 52, float yOffset = 1f)
    {
        var oldSmooth = g.SmoothingMode;
        var oldComp = g.CompositingMode;
        var oldCompQual = g.CompositingQuality;
        var oldPix = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Fluent 2-layer shadow: ambient (soft, wide) + directional (tighter, more Y-offset)
        var ambient = rect;
        ambient.Inflate(8f, 8f);
        ambient.Offset(0, yOffset + 1f);
        int ambientAlpha = Math.Clamp((int)(alpha * 0.10f), 1, 255);
        using (var path = RRect(ambient, radius + 8f))
        using (var brush = new SolidBrush(Color.FromArgb(ambientAlpha, 0, 0, 0)))
            g.FillPath(brush, path);

        var directional = rect;
        directional.Inflate(3f, 3f);
        directional.Offset(0, yOffset + 4f);
        int dirAlpha = Math.Clamp((int)(alpha * 0.22f), 1, 255);
        using (var path = RRect(directional, radius + 3f))
        using (var brush = new SolidBrush(Color.FromArgb(dirAlpha, 0, 0, 0)))
            g.FillPath(brush, path);

        g.SmoothingMode = oldSmooth;
        g.CompositingMode = oldComp;
        g.CompositingQuality = oldCompQual;
        g.PixelOffsetMode = oldPix;
    }

    private void PaintRuler(Graphics g, Point from, Point to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        float nx = 0, ny = 0;
        if (dist > 1) { nx = -dy / dist; ny = dx / dist; }
        const float tickHalf = 6f;

        using var shadowPen = new Pen(Color.FromArgb(70, 0, 0, 0), 3f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(shadowPen, from.X + 1, from.Y + 1, to.X + 1, to.Y + 1);

        using var pen = new Pen(UiChrome.SurfaceTextPrimary, 1.8f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(pen, from, to);

        using var tickPen = new Pen(UiChrome.SurfaceTextPrimary, 1.8f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(tickPen, from.X - nx * tickHalf, from.Y - ny * tickHalf,
                            from.X + nx * tickHalf, from.Y + ny * tickHalf);
        g.DrawLine(tickPen, to.X - nx * tickHalf, to.Y - ny * tickHalf,
                            to.X + nx * tickHalf, to.Y + ny * tickHalf);

        string text = $"{(int)dist}px  \u00b7  {Math.Abs(dx):0} \u00d7 {Math.Abs(dy):0}  \u00b7  {angle:0.0}\u00b0";
        var font = UiChrome.ChromeFont(9.5f);
        var sz = g.MeasureString(text, font);
        var mid = new PointF((from.X + to.X) / 2f, (from.Y + to.Y) / 2f);
        var label = new RectangleF(mid.X - sz.Width / 2f - 10, mid.Y - sz.Height - 14, sz.Width + 20, sz.Height + 10);
        PaintShadow(g, label, 8f, 48, 1f);
        using var path = RRect(label, 8f);
        using var bg = new SolidBrush(UiChrome.SurfacePill);
        using var border = new Pen(UiChrome.SurfaceBorderSubtle, 1.4f);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        using var fg = new SolidBrush(UiChrome.SurfaceTextPrimary);
        g.DrawString(text, font, fg, label.X + 10, label.Y + 5);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
        g.SmoothingMode = SmoothingMode.Default;
    }

    private Graphics GetBlurPreviewGraphics(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        if (_blurPreviewBitmap == null || _blurPreviewSize != size)
        {
            _blurPreviewGraphics?.Dispose();
            _blurPreviewBitmap?.Dispose();
            _blurPreviewBitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            _blurPreviewGraphics = Graphics.FromImage(_blurPreviewBitmap);
            _blurPreviewSize = size;
        }

        return _blurPreviewGraphics!;
    }

    private void PaintBlurRect(Graphics g, Rectangle rect)
    {
        int blockSize = Math.Max(6, Math.Min(rect.Width, rect.Height) / 8);
        if (rect.Width < 3 || rect.Height < 3) return;
        var clamped = Rectangle.Intersect(rect, new Rectangle(0, 0, _bmpW, _bmpH));
        if (clamped.Width < 1 || clamped.Height < 1) return;
        int sw = Math.Max(1, clamped.Width / blockSize);
        int sh = Math.Max(1, clamped.Height / blockSize);
        var small = GetBlurPreviewGraphics(new Size(sw, sh));
        small.Clear(Color.Transparent);
        small.InterpolationMode = InterpolationMode.Bilinear;
        small.DrawImage(_screenshot, new Rectangle(0, 0, sw, sh), clamped, GraphicsUnit.Pixel);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_blurPreviewBitmap!, clamped);
        g.InterpolationMode = InterpolationMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;
    }
}
