using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using OddSnap.Native;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.Capture;

/// <summary>
/// Two-phase form: first shows fullscreen overlay for region selection,
/// then stays fullscreen but transparent during recording while a separate
/// capture-excluded toolbar window provides recording controls.
/// </summary>
public sealed partial class RecordingForm : Form
{
    private const int RecordingWarmupDelayMs = 260;

    /// <summary>Fires with (filePath, firstFrameBitmap). Caller must dispose the bitmap.</summary>
    public event Action<string, Bitmap?>? RecordingCompleted;
    public event Action<Exception>? RecordingFailed;
    public event Action? RecordingCancelled;
    public event Action? EncodingStarted;

    /// <summary>Static reference to the current recording form for external stop control.</summary>
    public static RecordingForm? Current { get; private set; }

    private enum State { Selecting, Recording, Encoding }

    private Bitmap? _screenshot;
    private readonly Rectangle _virtualBounds;
    private State _state = State.Selecting;

    // Selection
    private bool _isDragging;
    private bool _hasDragged;
    private Point _dragStart;
    private Point _selectionCursor;
    private Rectangle _selection;
    private Rectangle _autoDetectRect;
    private Rectangle _clickAutoDetectRect;
    private readonly WindowDetectionMode _windowDetectionMode;

    // Recording
    private GifRecorder? _recorder;
    private VideoRecorder? _videoRecorder;
    private int _recordingStopRequested;
    private readonly int _fps;
    private readonly int _maxDuration;
    private readonly Models.RecordingFormat _format;
    private readonly int _maxHeight;
    private readonly bool _showCursor;
    private readonly bool _recordMic;
    private readonly string? _micDeviceId;
    private readonly bool _recordDesktop;
    private readonly string? _desktopDeviceId;
    private readonly bool _showMagnifier;
    private readonly CaptureMagnifierHelper? _magHelper;
    private LiveSelectionAdornerForm? _selectionAdorner;
    private CaptureEscapeKeyHook? _escapeHook;
    private readonly CancellationTokenSource _windowSnapshotCts = new();
    private System.Windows.Forms.Timer? _tickTimer;
    private readonly string _savePath;

    // Screen-relative selection (stays valid after phase change)
    private Rectangle _recordRegion; // in form coords, persisted

    // Toolbar (recording phase) - positioned relative to form
    private Rectangle _toolbarRect;
    private RecordingToolbarForm? _recordingToolbarForm;
    private RecordingBorderForm? _recordingBorderForm;

    // TransparencyKey color - any color that won't appear in UI
    private static readonly Color TransKey = Color.FromArgb(1, 2, 3);

    // Cached GDI objects for paint
    private readonly Font _readoutFont = UiChrome.ChromeFont(9f, FontStyle.Bold);
    private readonly Font _hintFont = UiChrome.ChromeFont(UiChrome.ChromeHintSize);
    private readonly SolidBrush _hintBrush = new(UiChrome.SurfaceTextMuted);
    private readonly SolidBrush _dotBrush = new(Color.FromArgb(240, 239, 68, 68));
    private readonly Pen _ringPen = new(Color.FromArgb(80, 239, 68, 68), 1.5f);
    private readonly Font _timeFont = UiChrome.ChromeFont(UiChrome.ChromeTitleSize, FontStyle.Bold);
    private readonly SolidBrush _timeBrush = new(UiChrome.SurfaceTextPrimary);
    private readonly Font _encFont = UiChrome.ChromeFont(10f, FontStyle.Bold);
    private readonly SolidBrush _encTextBrush = new(UiChrome.SurfaceTextSecondary);
    private readonly SolidBrush _spinBrush = new(Color.FromArgb(200, 239, 68, 68));

    public RecordingForm(Bitmap? screenshot, Rectangle virtualBounds, int fps, string savePath,
                         Models.RecordingFormat format = Models.RecordingFormat.GIF, int maxHeight = 0,
                         bool showCursor = false,
                         bool recordMic = false, string? micDeviceId = null,
                         bool recordDesktop = false, string? desktopDeviceId = null,
                         bool showMagnifier = false,
                         WindowDetectionMode windowDetectionMode = WindowDetectionMode.WindowOnly)
    {
        Theme.Refresh();
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _fps = fps;
        _maxDuration = 3600; // effectively unlimited - user stops manually
        _savePath = savePath;
        _format = format;
        _maxHeight = maxHeight;
        _showCursor = showCursor;
        _recordMic = recordMic;
        _micDeviceId = micDeviceId;
        _recordDesktop = recordDesktop;
        _desktopDeviceId = desktopDeviceId;
        _showMagnifier = showMagnifier;
        _windowDetectionMode = Enum.IsDefined(windowDetectionMode)
            ? windowDetectionMode
            : WindowDetectionMode.WindowOnly;
        if (_showMagnifier && screenshot is not null)
        {
            _magHelper = new CaptureMagnifierHelper();
            _magHelper.CachePixelData(screenshot);
        }

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualBounds.X, virtualBounds.Y, virtualBounds.Width, virtualBounds.Height);
        Cursor = Cursors.Cross;
        BackColor = UiChrome.SurfaceWindowBackground;
        if (screenshot is null)
        {
            Opacity = 0.01;
            _selectionAdorner = new LiveSelectionAdornerForm(_virtualBounds, "Drag to select recording area");
        }
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Do not apply WDA_EXCLUDEFROMCAPTURE to this full-screen surface.
        // On some Windows 10 hybrid/HDR systems it blanks the whole recording region.
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(Handle);
        Activate();
        Focus();
        WindowDetector.RegisterIgnoredWindow(Handle);
        _escapeHook = CaptureEscapeKeyHook.Install(this, CancelFromEscape);
        _selectionAdorner?.Show(this);
        QueueWindowSnapshot();
    }

    // ─── Selection phase ──────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            CancelFromEscape();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelFromEscape();
            return;
        }

        base.OnKeyDown(e);
    }

    private void CancelFromEscape()
    {
        if (_state == State.Recording)
        {
            DiscardRecording();
            return;
        }

        if (_state == State.Encoding)
            return;

        RecordingCancelled?.Invoke();
        Close();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_state == State.Selecting && e.Button == MouseButtons.Left)
        {
            _clickAutoDetectRect = _autoDetectRect.Contains(e.Location)
                ? _autoDetectRect
                : Rectangle.Empty;
            var oldAutoDetectRect = _autoDetectRect;
            _autoDetectRect = Rectangle.Empty;
            _isDragging = true;
            _hasDragged = false;
            _dragStart = e.Location;
            _selectionCursor = e.Location;
            _selection = Rectangle.Empty;
            UpdateLiveSelectionAdorner();
            if (!oldAutoDetectRect.IsEmpty)
                Invalidate(InflateForRepaint(oldAutoDetectRect, 16));
        }
        else if (_state == State.Recording && e.Button == MouseButtons.Left)
        {
            // Recording controls live in RecordingToolbarForm so they can be
            // excluded from screen/GIF capture without excluding this full-screen surface.
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_state == State.Selecting)
        {
            if (_isDragging)
            {
                var oldSelection = _selection;
                var oldCursor = _selectionCursor;
                _selection = NormRect(_dragStart, e.Location);
                if (!_hasDragged)
                {
                    int dx = e.Location.X - _dragStart.X;
                    int dy = e.Location.Y - _dragStart.Y;
                    _hasDragged = ((long)dx * dx) + ((long)dy * dy) >= 9;
                }
                _selectionCursor = e.Location;
                UpdateLiveSelectionAdorner();
                InvalidateSelectionChrome(oldSelection, oldCursor, _selection, e.Location);
            }
            else
            {
                UpdateAutoDetectRect(e.Location);
            }
            _magHelper?.Update(e.Location, this, _virtualBounds, _isDragging ? GetMagnifierAvoidBounds() : Rectangle.Empty);
            return;
        }
        if (_state == State.Recording)
        {
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_state == State.Selecting && _isDragging && e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            var detectedWindow = _hasDragged
                ? Rectangle.Empty
                : ResolveClickWindowRect(e.Location);
            _selection = ResolveRecordingSelection(
                _hasDragged,
                NormRect(_dragStart, e.Location),
                detectedWindow);
            _clickAutoDetectRect = Rectangle.Empty;
            _selectionCursor = e.Location;
            UpdateLiveSelectionAdorner();
            if (_selection.Width > 10 && _selection.Height > 10)
                StartRecording();
            else
            {
                Invalidate();
            }
        }
    }

    // ─── Paint ────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        if (_state == State.Selecting)
            PaintSelectionPhase(g);
        else
            PaintRecordingPhase(g);
    }

    private void PaintSelectionPhase(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.AssumeLinear;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var screenshot = _screenshot;
        if (screenshot is null)
            g.Clear(UiChrome.SurfaceWindowBackground);
        else
            g.DrawImage(screenshot, ClientRectangle,
                new Rectangle(0, 0, screenshot.Width, screenshot.Height),
                GraphicsUnit.Pixel);

        if (_selection.Width > 2 && _selection.Height > 2)
        {
            SelectionFrameRenderer.DrawRectangle(g, _selection);
            SelectionSizeReadout.Draw(
                g,
                PointToClient(Cursor.Position),
                _selection,
                _hintFont,
                ClientRectangle,
                GetRecordingReadoutDetails());
        }
        else if (!_autoDetectRect.IsEmpty)
        {
            SelectionFrameRenderer.DrawRectangle(g, _autoDetectRect);
            SelectionSizeReadout.Draw(
                g,
                PointToClient(Cursor.Position),
                _autoDetectRect,
                _hintFont,
                ClientRectangle,
                GetRecordingReadoutDetails());
        }
        else
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            string hint = _windowDetectionMode == WindowDetectionMode.Off
                ? "Drag to select recording area"
                : "Click a window or drag to select recording area";
            var hintSz = g.MeasureString(hint, _hintFont);
            g.DrawString(hint, _hintFont, _hintBrush,
                Width / 2f - hintSz.Width / 2f, Height / 2f - hintSz.Height / 2f);
        }

    }

    private void UpdateLiveSelectionAdorner()
    {
        if (_selectionAdorner is null)
            return;

        _selectionAdorner.SetSelection(_selection, PointToClient(Cursor.Position), GetRecordingReadoutDetails());
    }

    private void QueueWindowSnapshot()
    {
        WindowDetector.ClearSnapshot();
        if (_windowDetectionMode == WindowDetectionMode.Off)
            return;

        var cancellationToken = _windowSnapshotCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                WindowDetector.SnapshotWindows(_virtualBounds);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Live hit testing remains available if snapshot creation fails.
            }
        });
    }

    private void UpdateAutoDetectRect(Point location)
    {
        var detected = ResolveWindowRect(location);
        if (detected == _autoDetectRect)
            return;

        var previous = _autoDetectRect;
        _autoDetectRect = detected;
        var previousDirty = InflateForRepaint(previous, 16);
        var detectedDirty = InflateForRepaint(detected, 16);
        var dirty = previousDirty.IsEmpty
            ? detectedDirty
            : detectedDirty.IsEmpty
                ? previousDirty
                : Rectangle.Union(previousDirty, detectedDirty);
        if (!dirty.IsEmpty)
            Invalidate(dirty);
    }

    private Rectangle ResolveClickWindowRect(Point location)
    {
        if (_clickAutoDetectRect.Contains(location))
            return _clickAutoDetectRect;

        return ResolveWindowRect(location);
    }

    private Rectangle ResolveWindowRect(Point location)
    {
        if (_windowDetectionMode == WindowDetectionMode.Off)
            return Rectangle.Empty;

        if (WindowDetector.TryGetSnapshotDetectionRectAtPoint(
                location,
                _virtualBounds,
                _windowDetectionMode,
                out var detected))
        {
            return detected;
        }

        return WindowDetector.GetFastDetectionRectAtPoint(
            location,
            _virtualBounds,
            _windowDetectionMode);
    }

    internal static Rectangle ResolveRecordingSelection(
        bool hasDragged,
        Rectangle draggedSelection,
        Rectangle detectedWindow) =>
        hasDragged ? draggedSelection : detectedWindow;

    private void PaintRecordingPhase(Graphics g)
    {
        g.Clear(TransKey);
        if (_state == State.Recording)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.AssumeLinear;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_state == State.Encoding)
        {
            WindowsDockRenderer.PaintSurface(g, _toolbarRect);

            float spinX = _toolbarRect.X + 14;
            float spinY = _toolbarRect.Y + _toolbarRect.Height / 2f - 4;
            g.FillEllipse(_spinBrush, spinX, spinY, 8, 8);

            string encLabel = _format == Models.RecordingFormat.GIF ? "Encoding GIF..." : "Saving...";
            var encRect = new RectangleF(spinX + 16, _toolbarRect.Y, _toolbarRect.Width - 30, _toolbarRect.Height);
            using var encFormat = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(encLabel, _encFont, _encTextBrush, encRect, encFormat);
        }
    }

    internal void PaintRecordingToolbarTo(Graphics g, Rectangle bounds, int hoveredButton)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.AssumeLinear;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        WindowsDockRenderer.PaintSurface(g, bounds);

        var elapsed = _recorder?.Elapsed ?? _videoRecorder?.Elapsed ?? TimeSpan.Zero;

        float dotX = bounds.X + 16;
        float dotY = bounds.Y + bounds.Height / 2f - 5;
        bool dotVisible = (int)(elapsed.TotalMilliseconds / 500) % 2 == 0;
        if (dotVisible)
            g.FillEllipse(_dotBrush, dotX, dotY, 10, 10);
        g.DrawEllipse(_ringPen, dotX, dotY, 10, 10);

        string time = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        var stopButton = GetRecordingToolbarStopButton(bounds);
        var discardButton = GetRecordingToolbarDiscardButton(bounds);
        var timeRect = new RectangleF(dotX + 18, bounds.Y, stopButton.X - (dotX + 24), bounds.Height);
        using (var timeFormat = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(time, _timeFont, _timeBrush, timeRect, timeFormat);

        DrawIconBtn(g, stopButton, "stopSquare", hoveredButton == 0,
            UiChrome.SurfaceTextPrimary, active: false);
        DrawIconBtn(g, discardButton, "close", hoveredButton == 1,
            UiChrome.SurfaceTextPrimary, active: false);
    }

    internal static Rectangle GetRecordingToolbarDiscardButton(Rectangle toolbarBounds)
    {
        int btnY = toolbarBounds.Y + (toolbarBounds.Height - WindowsDockRenderer.IconButtonSize) / 2;
        return new Rectangle(toolbarBounds.Right - WindowsDockRenderer.SurfacePadding - WindowsDockRenderer.IconButtonSize,
            btnY, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
    }

    internal static Rectangle GetRecordingToolbarStopButton(Rectangle toolbarBounds)
    {
        var discardButton = GetRecordingToolbarDiscardButton(toolbarBounds);
        return new Rectangle(discardButton.X - WindowsDockRenderer.ButtonSpacing - WindowsDockRenderer.IconButtonSize,
            discardButton.Y, WindowsDockRenderer.IconButtonSize, WindowsDockRenderer.IconButtonSize);
    }

    internal void RequestToolbarStop() => StopRecording();

    internal void RequestToolbarDiscard() => DiscardRecording();

    private void DrawIconBtn(Graphics g, Rectangle rect, string iconId, bool hovered,
        Color iconColor, bool active)
    {
        WindowsDockRenderer.PaintButton(g, rect, active, hovered);
        int alpha = active ? 255 : hovered ? 240 : 200;
        WindowsDockRenderer.PaintIcon(g, iconId, rect, Color.FromArgb(alpha, iconColor.R, iconColor.G, iconColor.B), active);
    }

    private static Rectangle NormRect(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static Rectangle InflateForRepaint(Rectangle rect, int pad = 8)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return Rectangle.Empty;

        rect.Inflate(pad, pad);
        return rect;
    }

    private string GetRecordingFormatLabel() => _format switch
    {
        Models.RecordingFormat.MP4 => "MP4",
        Models.RecordingFormat.WebM => "WebM",
        Models.RecordingFormat.MKV => "MKV",
        _ => "GIF"
    };

    private string[] GetRecordingReadoutDetails()
        => [$"{GetRecordingFormatLabel()}  {_fps} FPS"];

    private Rectangle GetMagnifierAvoidBounds()
    {
        if (_selection.Width <= 2 || _selection.Height <= 2)
            return Rectangle.Empty;

        var readoutBounds = SelectionSizeReadout.GetBounds(
            PointToClient(Cursor.Position),
            _selection,
            _readoutFont,
            ClientRectangle,
            GetRecordingReadoutDetails());
        return readoutBounds.IsEmpty
            ? _selection
            : Rectangle.Union(_selection, InflateForRepaint(readoutBounds, 8));
    }

    private void InvalidateSelectionChrome(Rectangle oldSelection, Point oldCursor, Rectangle newSelection, Point newCursor)
    {
        var oldDirty = GetSelectionChromeBounds(oldSelection, oldCursor);
        var newDirty = GetSelectionChromeBounds(newSelection, newCursor);

        if (!oldDirty.IsEmpty && !newDirty.IsEmpty)
            Invalidate(Rectangle.Union(oldDirty, newDirty));
        else if (!oldDirty.IsEmpty)
            Invalidate(oldDirty);
        else if (!newDirty.IsEmpty)
            Invalidate(newDirty);
    }

    private Rectangle GetSelectionChromeBounds(Rectangle selection, Point cursor)
    {
        if (selection.Width <= 2 || selection.Height <= 2)
            return Rectangle.Empty;

        var dirty = InflateForRepaint(selection, 16);
        var readoutBounds = SelectionSizeReadout.GetBounds(
            cursor,
            selection,
            _readoutFont,
            ClientRectangle,
            GetRecordingReadoutDetails());
        if (!readoutBounds.IsEmpty)
            dirty = Rectangle.Union(dirty, InflateForRepaint(readoutBounds, 10));

        return dirty;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Current = null;
            if (IsHandleCreated)
            {
                CaptureWindowExclusion.Unregister(Handle);
                WindowDetector.UnregisterIgnoredWindow(Handle);
            }
            _windowSnapshotCts.Cancel();
            WindowDetector.ClearSnapshot();
            _escapeHook?.Dispose();
            _escapeHook = null;
            _tickTimer?.Dispose();
            _recordingToolbarForm?.Dispose();
            _recordingToolbarForm = null;
            _recordingBorderForm?.Dispose();
            _recordingBorderForm = null;
            _recorder?.Dispose();
            _videoRecorder?.Dispose();
            _magHelper?.Dispose();
            _selectionAdorner?.Dispose();
            _selectionAdorner = null;
            _screenshot?.Dispose();
            _screenshot = null;
            _readoutFont.Dispose();
            _hintFont.Dispose(); _hintBrush.Dispose();
            _dotBrush.Dispose(); _ringPen.Dispose(); _timeFont.Dispose();
            _timeBrush.Dispose(); _encFont.Dispose();
            _encTextBrush.Dispose(); _spinBrush.Dispose();
            _windowSnapshotCts.Dispose();
        }
        base.Dispose(disposing);
    }

}
