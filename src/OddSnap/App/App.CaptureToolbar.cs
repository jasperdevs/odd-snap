using OddSnap.Capture;
using OddSnap.Models;
using OddSnap.UI;

namespace OddSnap;

public partial class App
{
    private CaptureToolbarWindow? _captureToolbarWindow;

    private void ToggleCaptureToolbar()
    {
        if (_captureToolbarWindow is { } existing)
        {
            existing.Close();
            return;
        }
        if (_settingsService is null || Volatile.Read(ref _isCapturing) != 0 || Volatile.Read(ref _isShuttingDown) != 0)
            return;

        var toolbar = new CaptureToolbarWindow(_settingsService.Settings);
        _captureToolbarWindow = toolbar;
        toolbar.ToolRequested += OnTrayToolRequested;
        toolbar.Closed += (_, _) =>
        {
            if (ReferenceEquals(_captureToolbarWindow, toolbar))
                _captureToolbarWindow = null;
        };
        toolbar.Show();
    }

    private void OnTrayToolRequested(string id)
    {
        switch (id)
        {
            case "_fullscreen": OnFullscreenHotkeyPressed(); break;
            case "_activeWindow": OnActiveWindowHotkeyPressed(); break;
            case "_scrollCapture": OnScrollCaptureHotkeyPressed(); break;
            case "_record":
                if (RecordingForm.Current is { } recording) recording.RequestStop();
                else OnGifHotkeyPressed();
                break;
            default:
                var tool = ToolDef.AllTools.FirstOrDefault(tool => tool.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (tool is { Group: 0, Mode: { } mode }) OnToolHotkeyPressed(mode);
                break;
        }
    }
}
