using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OddSnap.Native;

namespace OddSnap.Capture;

internal sealed class RecordingToolbarForm : Form
{
    private readonly RecordingForm _owner;
    private Bitmap? _surface;
    private Graphics? _surfaceGraphics;
    private int _hoveredButton = -1;

    public RecordingToolbarForm(RecordingForm owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = owner.TopMost;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= User32.WS_EX_TOOLWINDOW;
            cp.ExStyle |= User32.WS_EX_NOACTIVATE;
            cp.ExStyle |= User32.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CaptureWindowExclusion.Apply(this);
    }

    public void UpdateSurface()
    {
        var sz = Size;
        if (sz.Width <= 0 || sz.Height <= 0 || IsDisposed || !IsHandleCreated)
            return;

        if (_surface == null || _surface.Width != sz.Width || _surface.Height != sz.Height)
        {
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
            _surface = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
            _surfaceGraphics = Graphics.FromImage(_surface);
        }

        var g = _surfaceGraphics!;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        _owner.PaintRecordingToolbarTo(g, new Rectangle(Point.Empty, sz), _hoveredButton);
        g.Flush(FlushIntention.Sync);

        LayeredWindowSurface.Update(Handle, _surface, new Rectangle(Left, Top, sz.Width, sz.Height));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int previous = _hoveredButton;
        _hoveredButton = RecordingForm.GetRecordingToolbarStopButton(ClientRectangle).Contains(e.Location) ? 0
            : RecordingForm.GetRecordingToolbarDiscardButton(ClientRectangle).Contains(e.Location) ? 1
            : -1;
        Cursor = _hoveredButton >= 0 ? Cursors.Hand : Cursors.Default;
        if (_hoveredButton != previous)
            UpdateSurface();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredButton == -1)
            return;

        _hoveredButton = -1;
        Cursor = Cursors.Default;
        UpdateSurface();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        if (RecordingForm.GetRecordingToolbarStopButton(ClientRectangle).Contains(e.Location))
            _owner.RequestToolbarStop();
        else if (RecordingForm.GetRecordingToolbarDiscardButton(ClientRectangle).Contains(e.Location))
            _owner.RequestToolbarDiscard();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            _owner.RequestToolbarDiscard();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsHandleCreated)
                CaptureWindowExclusion.Unregister(Handle);
            _surfaceGraphics?.Dispose();
            _surface?.Dispose();
        }

        base.Dispose(disposing);
    }
}
