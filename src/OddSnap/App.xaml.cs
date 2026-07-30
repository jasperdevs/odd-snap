using System.Windows;
using System.Windows.Threading;
using Velopack;
using OddSnap.Services;
using OddSnap.UI;

namespace OddSnap;

public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static Mutex? _mutex;
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private HistoryService? _historyService;
    private ImageSearchIndexService? _imageSearchIndexService;
    private readonly object _historyGate = new();
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _idleTrimTimer;
    private DispatcherTimer? _captureDelayTimer;
    private int _activeUploadCount;
    private int _isCapturing;
    private bool _historyChangedHooked;
    private bool _historyMaintenanceScheduled;
    private int _historyIndexRefreshScheduled;
    private int _settingsWindowOpening;
    private int _settingsHiddenForCapture;
    private int _idleTrimInProgress;
    private int _isShuttingDown;
    private bool _readyToastShown;
    private long _captureRequestedTimestamp;
    private DateTime _lastIdleTrimUtc = DateTime.MinValue;
    private int _openHistoryWhenSettingsReady;

    private bool TryPostToAppDispatcher(
        Action action,
        DispatcherPriority priority = DispatcherPriority.Normal,
        string diagnosticKey = "app.dispatcher-post")
    {
        if (Volatile.Read(ref _isShuttingDown) != 0 ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return false;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(action, priority);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);
            return false;
        }
    }

    private bool TryPostToAppDispatcherAsync(
        Func<Task> action,
        DispatcherPriority priority = DispatcherPriority.Normal,
        string diagnosticKey = "app.dispatcher-post-async")
    {
        if (Volatile.Read(ref _isShuttingDown) != 0 ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return false;
        }

        try
        {
            Action wrapped = async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    AppDiagnostics.LogError(diagnosticKey, ex);
                }
            };
            _ = Dispatcher.BeginInvoke(wrapped, priority);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            AppDiagnostics.LogWarning(diagnosticKey, ex.Message, ex);
            return false;
        }
    }

    private void ResetCapturingWithoutUiRestore()
    {
        Volatile.Write(ref _isCapturing, 0);
        Interlocked.Exchange(ref _settingsHiddenForCapture, 0);
    }
}
