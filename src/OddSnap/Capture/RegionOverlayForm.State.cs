using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    public CaptureMode CurrentMode => _mode;

    public void SetShowToolNumberBadges(bool show)
    {
        _showToolNumberBadges = show;
        RefreshToolbar();
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCrosshairGuides { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AnnotationStrokeShadow { get; set; } = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DetectWindows { get; set; } = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowCaptureMagnifier { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public CaptureDockSide CaptureDockSide { get; set; } = CaptureDockSide.Top;

    private bool IsVerticalDock => CaptureDockSide is CaptureDockSide.Left or CaptureDockSide.Right;
    private bool IsBottomDock => CaptureDockSide == CaptureDockSide.Bottom;
    private bool IsTopDock => CaptureDockSide == CaptureDockSide.Top;
    private bool IsLeftDock => CaptureDockSide == CaptureDockSide.Left;
    private bool IsRightDock => CaptureDockSide == CaptureDockSide.Right;

    public void SetEnabledTools(List<string>? enabledIds)
    {
        var flyoutIds = ToolDef.FlyoutToolIds();
        if (enabledIds == null)
        {
            _visibleTools = ToolDef.AllTools;
        }
        else
        {
            _visibleTools = ToolDef.AllTools.Where(t => enabledIds.Contains(t.Id)).ToArray();
        }
        _mainBarTools = _visibleTools.Where(t => !flyoutIds.Contains(t.Id)).ToArray();
        _flyoutTools = _visibleTools.Where(t => flyoutIds.Contains(t.Id)).ToArray();
        RefreshToolbar();
    }

    // All system fonts, cached once
    private static string[]? _allSystemFonts;
    private static string[] GetSystemFonts()
    {
        if (_allSystemFonts != null) return _allSystemFonts;
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        _allSystemFonts = fonts.Families
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return _allSystemFonts;
    }

    private string[] GetFilteredFonts()
    {
        if (_filteredFonts != null) return _filteredFonts;
        var all = GetSystemFonts();
        if (string.IsNullOrEmpty(_fontSearch))
        {
            _filteredFonts = all;
            return _filteredFonts;
        }
        var terms = _fontSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _filteredFonts = all.Where(f =>
        {
            foreach (var term in terms)
                if (f.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            return true;
        }).ToArray();
        return _filteredFonts;
    }

    private Rectangle GetOverlayUiBounds()
    {
        Rectangle bounds = Rectangle.Empty;
        static Rectangle InflateIfNeeded(Rectangle r, int pad)
        {
            if (r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
            r.Inflate(pad, pad);
            return r;
        }

        void Add(Rectangle r)
        {
            if (r.IsEmpty) return;
            bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }

        Add(InflateIfNeeded(_toolbarRect, 12));
        Add(InflateForRepaint(Rectangle.Round(GetTextToolbarBounds())));
        Add(InflateForRepaint(Rectangle.Round(GetActiveTextRect())));
        Add(InflateIfNeeded(GetColorPickerBounds(), 12));
        Add(InflateIfNeeded(GetFontPickerBounds(), 12));
        Add(InflateIfNeeded(GetEmojiPickerBounds(), 12));
        return bounds;
    }

    private bool IsPointInOverlayUi(Point p)
    {
        if (IsPointInToolbarChrome(p)) return true;
        if (_emojiPickerOpen && _emojiPickerRect.Contains(p)) return true;
        if (_fontPickerOpen && _fontPickerRect.Contains(p)) return true;
        if (_colorPickerOpen && _colorPickerRect.Contains(p)) return true;
        return false;
    }

    private bool IsPointInToolbarChrome(Point p)
    {
        var tbBounds = _toolbarRect;
        tbBounds.Inflate(8, 8);
        if (IsVerticalDock)
            tbBounds.Width += 10;
        else
            tbBounds.Height += 10;
        return tbBounds.Contains(p);
    }

    private Rectangle PositionPopupFromAnchor(Rectangle anchor, int width, int height, int gap = 8)
    {
        int x;
        int y;

        if (IsVerticalDock)
        {
            x = IsRightDock ? anchor.X - width - gap : anchor.Right + gap;
            y = anchor.Y + (anchor.Height / 2) - (height / 2);
            y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - height - 8));
            x = Math.Clamp(x, 8, Math.Max(8, ClientSize.Width - width - 8));
        }
        else
        {
            x = anchor.X + (anchor.Width / 2) - (width / 2);
            y = IsBottomDock ? anchor.Y - height - gap : anchor.Bottom + gap;
            x = Math.Clamp(x, 8, Math.Max(8, ClientSize.Width - width - 8));
            y = Math.Clamp(y, 8, Math.Max(8, ClientSize.Height - height - 8));
        }

        return new Rectangle(x, y, width, height);
    }

    private PointF GetTooltipOrigin(Rectangle anchor, SizeF size, float gap = 6f)
    {
        float x;
        float y;

        if (IsVerticalDock)
        {
            x = IsRightDock ? anchor.X - size.Width - gap : anchor.Right + gap;
            y = anchor.Y + (anchor.Height / 2f) - (size.Height / 2f);
            y = Math.Clamp(y, 4f, Math.Max(4f, Height - size.Height - 4f));
        }
        else
        {
            x = anchor.X + (anchor.Width / 2f) - (size.Width / 2f);
            y = IsBottomDock ? anchor.Y - size.Height - gap : anchor.Bottom + gap;
            x = Math.Clamp(x, 4f, Math.Max(4f, Width - size.Width - 4f));
            y = Math.Clamp(y, 4f, Math.Max(4f, Height - size.Height - 4f));
        }

        return new PointF(x, y);
    }

    private bool ShouldShowCaptureMagnifierAt(Point p)
        => ShowCaptureMagnifier
           && ToolDef.IsCaptureTool(_mode)
           && !IsPointInOverlayUi(p);

    private Rectangle GetSelectionOverlayBounds(Rectangle rect, bool isOcr, bool isScan)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return Rectangle.Empty;

        var dirty = rect;
        dirty.Inflate(8, 8);

        var labelBounds = Rectangle.Ceiling(GetLabelBounds(rect, isOcr, isScan));
        if (!labelBounds.IsEmpty)
            dirty = Rectangle.Union(dirty, InflateForRepaint(labelBounds, 8));

        return dirty;
    }

    private bool IsSelectionCaptureMode()
        => _mode is CaptureMode.Rectangle or CaptureMode.Ocr or CaptureMode.Scan or CaptureMode.Sticker or CaptureMode.Upscale;

    private void InvalidateAutoDetectChrome(Rectangle oldDetect, Rectangle newDetect)
    {
        if (!IsSelectionCaptureMode() || _isSelecting || _hasSelection)
            return;

        if (oldDetect.IsEmpty != newDetect.IsEmpty)
        {
            Invalidate();
            Update();
            return;
        }

        var oldDirty = InflateForRepaint(oldDetect);
        var newDirty = InflateForRepaint(newDetect);
        if (!oldDirty.IsEmpty && !newDirty.IsEmpty)
        {
            Invalidate(Rectangle.Union(oldDirty, newDirty));
            Update();
        }
        else if (!oldDirty.IsEmpty)
            Invalidate(oldDirty);
        else if (!newDirty.IsEmpty)
            Invalidate(newDirty);
    }

    private void UpdateAutoDetectRect(Point location)
    {
        if (_windowDetectionMode == WindowDetectionMode.Off)
        {
            var previousDetect = _autoDetectRect;
            _autoDetectRect = Rectangle.Empty;
            _autoDetectActive = false;
            InvalidateAutoDetectChrome(previousDetect, Rectangle.Empty);
            return;
        }

        var oldDetect = _autoDetectRect;
        var detected = WindowDetector.GetDetectionRectAtPoint(
            location, _virtualBounds, _windowDetectionMode);
        _autoDetectRect = detected;
        _autoDetectActive = detected.Width > 0 && detected.Height > 0;

        if (oldDetect == detected)
            return;

        InvalidateAutoDetectChrome(oldDetect, detected);
    }

    private void MarkCommittedAnnotationsDirty()
    {
        _committedAnnotationsDirty = true;
    }

    private void AddAnnotation(Annotation annotation)
    {
        _undoStack.Add(annotation);
        MarkCommittedAnnotationsDirty();
    }

    /// <summary>Returns the bounding rectangle for any annotation type, for hit-testing.</summary>
    private static Rectangle GetAnnotationBounds(Annotation a) => a switch
    {
        ArrowAnnotation arr => RectFromPoints(arr.From, arr.To, 8),
        CurvedArrowAnnotation ca => BoundsOfPoints(ca.Points, 8),
        LineAnnotation ln => RectFromPoints(ln.From, ln.To, 6),
        RulerAnnotation ru => RectFromPoints(ru.From, ru.To, 10),
        DrawStroke ds => BoundsOfPoints(ds.Points, 4),
        BlurRect br => br.Rect,
        HighlightAnnotation hl => hl.Rect,
        RectShapeAnnotation rs => rs.Rect,
        CircleShapeAnnotation cs => cs.Rect,
        EraserFill ef => ef.Rect,
        StepNumberAnnotation sn => new Rectangle(sn.Pos.X - 14, sn.Pos.Y - 14, 28, 28),
        EmojiAnnotation em => new Rectangle(em.Pos.X, em.Pos.Y, (int)em.Size, (int)em.Size),
        MagnifierAnnotation mg => new Rectangle(mg.Pos.X - 40, mg.Pos.Y - 40, 80, 80),
        TextAnnotation ta => GetTextBounds(ta),
        _ => Rectangle.Empty
    };

    private static Rectangle RectFromPoints(Point a, Point b, int pad)
    {
        int x = Math.Min(a.X, b.X) - pad;
        int y = Math.Min(a.Y, b.Y) - pad;
        int w = Math.Abs(b.X - a.X) + pad * 2;
        int h = Math.Abs(b.Y - a.Y) + pad * 2;
        return new Rectangle(x, y, w, h);
    }

    private static Rectangle BoundsOfPoints(List<Point> pts, int pad)
    {
        if (pts.Count == 0) return Rectangle.Empty;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in pts) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        return new Rectangle(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
    }

    private static Rectangle GetTextBounds(TextAnnotation ta)
    {
        using var font = new Font(ta.FontFamily, ta.FontSize,
            (ta.Bold ? FontStyle.Bold : 0) | (ta.Italic ? FontStyle.Italic : 0));
        var sz = System.Windows.Forms.TextRenderer.MeasureText(ta.Text, font);
        int padX = ta.Background ? 16 : 10;
        int padY = ta.Background ? 12 : 6;
        return new Rectangle(ta.Pos.X - (padX / 2), ta.Pos.Y - (padY / 2), sz.Width + padX, sz.Height + padY);
    }

    /// <summary>Hit-tests all annotations in reverse order (top-most first). Returns index or -1.</summary>
    private int HitTestAnnotation(Point p)
    {
        for (int i = _undoStack.Count - 1; i >= 0; i--)
        {
            var bounds = GetAnnotationBounds(_undoStack[i]);
            if (bounds.Contains(p))
                return i;
        }
        return -1;
    }

    /// <summary>Moves an annotation by a delta. Returns a new annotation with updated position.</summary>
    private static Annotation MoveAnnotation(Annotation a, int dx, int dy) => a switch
    {
        ArrowAnnotation arr => arr with { From = Offset(arr.From, dx, dy), To = Offset(arr.To, dx, dy) },
        CurvedArrowAnnotation ca => ca with { Points = ca.Points.Select(p => Offset(p, dx, dy)).ToList() },
        LineAnnotation ln => ln with { From = Offset(ln.From, dx, dy), To = Offset(ln.To, dx, dy) },
        RulerAnnotation ru => ru with { From = Offset(ru.From, dx, dy), To = Offset(ru.To, dx, dy) },
        DrawStroke ds => ds with { Points = ds.Points.Select(p => Offset(p, dx, dy)).ToList() },
        BlurRect br => br with { Rect = OffsetRect(br.Rect, dx, dy) },
        HighlightAnnotation hl => hl with { Rect = OffsetRect(hl.Rect, dx, dy) },
        RectShapeAnnotation rs => rs with { Rect = OffsetRect(rs.Rect, dx, dy) },
        CircleShapeAnnotation cs => cs with { Rect = OffsetRect(cs.Rect, dx, dy) },
        EraserFill ef => ef with { Rect = OffsetRect(ef.Rect, dx, dy) },
        StepNumberAnnotation sn => sn with { Pos = Offset(sn.Pos, dx, dy) },
        EmojiAnnotation em => em with { Pos = Offset(em.Pos, dx, dy) },
        MagnifierAnnotation mg => mg with { Pos = Offset(mg.Pos, dx, dy) },
        TextAnnotation ta => ta with { Pos = Offset(ta.Pos, dx, dy) },
        _ => a
    };

    private static Point Offset(Point p, int dx, int dy) => new(p.X + dx, p.Y + dy);
    private static Rectangle OffsetRect(Rectangle r, int dx, int dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    /// <summary>Returns the handle index (0=TL,1=TR,2=BL,3=BR) at point, or -1.</summary>
    private int GetSelectHandle(Point p)
    {
        if (_selectedAnnotationIndex < 0 || _selectedAnnotationIndex >= _undoStack.Count)
            return -1;
        var bounds = GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]);
        var selRect = Rectangle.Inflate(bounds, 4, 4);
        var corners = new[] {
            new Point(selRect.X, selRect.Y),
            new Point(selRect.Right - 1, selRect.Y),
            new Point(selRect.X, selRect.Bottom - 1),
            new Point(selRect.Right - 1, selRect.Bottom - 1),
        };
        for (int i = 0; i < 4; i++)
        {
            var hr = WindowsHandleRenderer.HitRect(corners[i]);
            if (hr.Contains(p)) return i;
        }
        return -1;
    }

    /// <summary>Scales an annotation by adjusting its bounds from a corner handle drag.</summary>
    private static Annotation ScaleAnnotation(Annotation a, Rectangle oldBounds, Rectangle newBounds)
    {
        if (oldBounds.Width <= 0 || oldBounds.Height <= 0) return a;
        double sx = (double)newBounds.Width / oldBounds.Width;
        double sy = (double)newBounds.Height / oldBounds.Height;
        int ox = newBounds.X - (int)(oldBounds.X * sx);
        int oy = newBounds.Y - (int)(oldBounds.Y * sy);

        Point ScalePt(Point p) => new((int)(p.X * sx) + ox, (int)(p.Y * sy) + oy);
        Rectangle ScaleRect(Rectangle r) => new((int)(r.X * sx) + ox, (int)(r.Y * sy) + oy,
            Math.Max(1, (int)(r.Width * sx)), Math.Max(1, (int)(r.Height * sy)));

        return a switch
        {
            ArrowAnnotation arr => arr with { From = ScalePt(arr.From), To = ScalePt(arr.To) },
            LineAnnotation ln => ln with { From = ScalePt(ln.From), To = ScalePt(ln.To) },
            RulerAnnotation ru => ru with { From = ScalePt(ru.From), To = ScalePt(ru.To) },
            BlurRect br => br with { Rect = ScaleRect(br.Rect) },
            HighlightAnnotation hl => hl with { Rect = ScaleRect(hl.Rect) },
            RectShapeAnnotation rs => rs with { Rect = ScaleRect(rs.Rect) },
            CircleShapeAnnotation cs => cs with { Rect = ScaleRect(cs.Rect) },
            EraserFill ef => ef with { Rect = ScaleRect(ef.Rect) },
            EmojiAnnotation em => em with { Pos = ScalePt(em.Pos), Size = Math.Max(8f, em.Size * (float)Math.Max(sx, sy)) },
            TextAnnotation ta => ta with { Pos = ScalePt(ta.Pos), FontSize = Math.Clamp(ta.FontSize * (float)Math.Max(sx, sy), 10f, 120f) },
            StepNumberAnnotation sn => sn with { Pos = ScalePt(sn.Pos) },
            DrawStroke ds => ds with { Points = ds.Points.Select(p => ScalePt(p)).ToList() },
            CurvedArrowAnnotation ca => ca with { Points = ca.Points.Select(p => ScalePt(p)).ToList() },
            _ => a
        };
    }

    private bool RemoveAnnotation(Annotation annotation)
    {
        bool removed = _undoStack.Remove(annotation);
        if (removed)
            MarkCommittedAnnotationsDirty();
        return removed;
    }

    private void CommitSelectTransform()
    {
        if (_selectedAnnotationIndex >= 0 &&
            _selectedAnnotationIndex < _undoStack.Count &&
            _selectPreviewAnnotation is not null)
        {
            _undoStack[_selectedAnnotationIndex] = _selectPreviewAnnotation;
            MarkCommittedAnnotationsDirty();
        }

        _selectPreviewAnnotation = null;
    }

    private Annotation RemoveLastAnnotation()
    {
        var last = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        MarkCommittedAnnotationsDirty();
        return last;
    }

    private Bitmap GetCommittedAnnotationsBitmap()
    {
        if (!_committedAnnotationsDirty && _committedAnnotationsBitmap is not null)
            return _committedAnnotationsBitmap;

        _committedAnnotationsBitmap?.Dispose();
        var bitmap = new Bitmap(_bmpW, _bmpH, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_screenshot, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            RenderAnnotationsTo(g);
        }

        _committedAnnotationsBitmap = bitmap;
        _committedAnnotationsDirty = false;
        return bitmap;
    }
}
