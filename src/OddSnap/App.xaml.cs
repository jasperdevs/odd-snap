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
    private int _activeUploadCount;
    private int _isCapturing;
    private bool _historyRecovered;
    private bool _historyChangedHooked;
    private bool _historyMaintenanceScheduled;
    private int _historyIndexRefreshScheduled;
    private int _settingsWindowOpening;
    private int _idleTrimInProgress;
    private DateTime _lastIdleTrimUtc = DateTime.MinValue;
    private int _openHistoryWhenSettingsReady;
}
