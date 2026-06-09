using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using Microsoft.Win32;
using OddSnap.Helpers;
using OddSnap.Models;
using OddSnap.Services;

namespace OddSnap.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private AppSettings? _settings;
    private Icon? _defaultIcon;
    private Icon? _recordingIcon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _recordItem;
    private readonly UserPreferenceChangedEventHandler _themePreferenceHandler;
    private bool _isShowingRecording;
    private bool _disposed;

    public event Action? OnCapture;
    public event Action? OnFullScreenCapture;
    public event Action? OnOcr;
    public event Action? OnColorPicker;
    public event Action? OnGifRecord;
    public event Action? OnScrollCapture;
    public event Action? OnSettings;
    public event Action? OnHistory;
    public event Action? OnQuit;

    public TrayIcon(AppSettings? settings = null)
    {
        _settings = settings;
        Theme.Refresh();
        _defaultIcon = CreateDefaultIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = T("OddSnap - Click to capture, right-click for menu"),
            Icon = _defaultIcon,
            Visible = true
        };

        _notifyIcon.MouseClick += (_, e) => HandleMouseClick(e.Button);

        _menu = CreateThemedMenu();
        _menu.Closed += (_, _) => _notifyIcon.ContextMenuStrip = null;

        _themePreferenceHandler = (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                RefreshTrayIconThemeOnUiThread();
        };
        SystemEvents.UserPreferenceChanged += _themePreferenceHandler;
    }

    public void UpdateSettings(AppSettings? settings)
    {
        if (_disposed)
            return;

        _settings = settings;
        RefreshLocalization();
    }

    public void UpdateRecordingState(bool isRecording)
    {
        if (_disposed)
            return;

        if (isRecording == _isShowingRecording) return;
        _isShowingRecording = isRecording;
        if (isRecording)
        {
            _recordingIcon ??= CreateRecordingIcon();
            _notifyIcon.Icon = _recordingIcon;
            _notifyIcon.Text = T("OddSnap recording - click to stop, right-click for menu");
        }
        else
        {
            _notifyIcon.Icon = _defaultIcon;
            _notifyIcon.Text = T("OddSnap - Click to capture, right-click for menu");
        }
    }

    public void RefreshLocalization()
    {
        _notifyIcon.Text = _isShowingRecording
            ? T("OddSnap recording - click to stop, right-click for menu")
            : T("OddSnap - Click to capture, right-click for menu");

        var oldMenu = _menu;
        _menu = CreateThemedMenu();
        _menu.Closed += (_, _) => _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.ContextMenuStrip = null;
        oldMenu?.Dispose();
    }

    private ContextMenuStrip CreateThemedMenu()
    {
        Theme.Refresh();
        var menu = WindowsMenuRenderer.Create(showImages: true, minWidth: WindowsMenuRenderer.DefaultWidth);
        bool isRec = Capture.RecordingForm.Current != null;

        var captureItem = WindowsMenuRenderer.Item(T("Screenshot"), HotkeyHint("rect"), "rect");
        var fullscreenItem = WindowsMenuRenderer.Item(T("Full screen"), HotkeyHint("_fullscreen"), "fullscreen");
        var ocrItem = WindowsMenuRenderer.Item(T("Text capture"), HotkeyHint("ocr"), "ocr");
        var pickerItem = WindowsMenuRenderer.Item(T("Color picker"), HotkeyHint("picker"), "picker");
        var recordItem = isRec
            ? WindowsMenuRenderer.Item(T("Stop recording"), null, "record", active: true, danger: true)
            : WindowsMenuRenderer.Item(T("Record"), HotkeyHint("_record"), "record");
        _recordItem = recordItem;
        var scrollItem = WindowsMenuRenderer.Item(T("Scroll capture"), HotkeyHint("_scrollCapture"), "scrollCapture");
        var settingsItem = WindowsMenuRenderer.Item(T("Settings"), iconId: "gear");
        var historyItem = WindowsMenuRenderer.Item(T("History"), iconId: "folder");
        var quitItem = WindowsMenuRenderer.Item(T("Quit"), iconId: "close", danger: true);

        captureItem.Click += (_, _) => OnCapture?.Invoke();
        fullscreenItem.Click += (_, _) => OnFullScreenCapture?.Invoke();
        ocrItem.Click += (_, _) => OnOcr?.Invoke();
        pickerItem.Click += (_, _) => OnColorPicker?.Invoke();
        recordItem.Click += (_, _) =>
        {
            if (Capture.RecordingForm.Current != null)
                Capture.RecordingForm.Current.RequestStop();
            else
                OnGifRecord?.Invoke();
        };
        scrollItem.Click += (_, _) => OnScrollCapture?.Invoke();
        settingsItem.Click += (_, _) => OnSettings?.Invoke();
        historyItem.Click += (_, _) => OnHistory?.Invoke();
        quitItem.Click += (_, _) => OnQuit?.Invoke();

        menu.Items.AddRange(new ToolStripItem[]
        {
            captureItem, fullscreenItem, ocrItem, pickerItem, recordItem, scrollItem,
            new ToolStripSeparator(),
            settingsItem, historyItem,
            new ToolStripSeparator(),
            quitItem,
        });

        WindowsMenuRenderer.NormalizeItemWidths(menu);
        return menu;
    }

    private void HandleMouseClick(MouseButtons button)
    {
        if (button == MouseButtons.Left && Capture.RecordingForm.Current != null)
        {
            Capture.RecordingForm.Current.RequestStop();
            return;
        }

        switch (button)
        {
            case MouseButtons.Left:
                OnCapture?.Invoke();
                break;
            case MouseButtons.Middle:
                OnHistory?.Invoke();
                break;
            case MouseButtons.Right:
                ShowMenu();
                break;
        }
    }

    private string? HotkeyHint(string toolId)
    {
        if (_settings == null) return null;
        var (mod, key) = _settings.GetToolHotkey(toolId);
        if (key == 0) return null;
        var parts = new System.Text.StringBuilder();
        if ((mod & Native.User32.MOD_CONTROL) != 0) parts.Append("Ctrl+");
        if ((mod & Native.User32.MOD_ALT) != 0) parts.Append("Alt+");
        if ((mod & Native.User32.MOD_SHIFT) != 0) parts.Append("Shift+");
        if ((mod & Native.User32.MOD_WIN) != 0) parts.Append("Win+");
        var keyName = ((System.Windows.Forms.Keys)key).ToString();
        keyName = keyName switch
        {
            "Oemtilde" or "OemTilde" => "`",
            "OemMinus" => "-",
            "Oemplus" or "OemPlus" => "=",
            "Snapshot" => "PrtSc",
            "Pause" => "Pause",
            "Cancel" => "Break",
            _ => keyName.Replace("Oem", "")
        };
        parts.Append(keyName);
        return parts.ToString();
    }

    private void ShowMenu()
    {
        if (_menu is null)
            return;

        UpdateRecordingMenuItem();
        WindowsMenuRenderer.NormalizeItemWidths(_menu);
        _notifyIcon.ContextMenuStrip = _menu;

        var showMethod = typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        showMethod?.Invoke(_notifyIcon, null);
    }

    private void UpdateRecordingMenuItem()
    {
        if (_recordItem is null)
            return;

        bool isRec = Capture.RecordingForm.Current != null;
        _recordItem.Text = isRec ? T("Stop recording") : T("Record");
        _recordItem.ShortcutKeyDisplayString = isRec ? string.Empty : HotkeyHint("_record") ?? string.Empty;
        _recordItem.Tag = isRec;
        _recordItem.ForeColor = isRec ? Color.FromArgb(239, 68, 68) : UiChrome.SurfaceTextPrimary;
    }

    private static string T(string text) => LocalizationService.Translate(text);

    // ── Tray icon ────────────────────────────────────────────────

    private static Icon CreateDefaultIcon()
    {
        var tint = IsTrayBackgroundDark() ? Color.White : Color.Black;
        return CreateLogoIcon(tint, recording: false);
    }

    private static Icon CreateRecordingIcon()
    {
        var tint = IsTrayBackgroundDark() ? Color.White : Color.Black;
        return CreateLogoIcon(tint, recording: true);
    }

    private static bool IsTrayBackgroundDark()
    {
        Theme.Refresh();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("SystemUsesLightTheme");
            if (value is int systemUsesLightTheme)
                return systemUsesLightTheme == 0;
        }
        catch
        {
        }

        return Theme.IsDark;
    }

    private static Icon CreateLogoIcon(Color tint, bool recording)
    {
        try
        {
            using var source = LoadLogoBitmap();
            using var mono = CreateTintedLogoBitmap(source, tint);
            var icon = CreateOwnedIcon(mono);
            return recording ? OverlayRecordingDot(icon) : icon;
        }
        catch
        {
            return CreateFallbackIcon(recording, tint);
        }
    }

    private static Bitmap LoadLogoBitmap()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/oddsnap_square.png", UriKind.Absolute));
        if (info == null)
            throw new InvalidOperationException("OddSnap logo resource was not found.");

        using var stream = info.Stream;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        var bitmap = new Bitmap(frame.PixelWidth, frame.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static Bitmap CreateTintedLogoBitmap(Bitmap source, Color tint)
    {
        var tinted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        BitmapData? sourceData = null;
        BitmapData? tintedData = null;
        var row = new byte[source.Width * 4];

        try
        {
            sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            tintedData = tinted.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            for (int y = 0; y < source.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(sourceData.Scan0, y * sourceData.Stride), row, 0, row.Length);
                for (int x = 0; x < source.Width; x++)
                {
                    int i = x * 4;
                    row[i] = tint.B;
                    row[i + 1] = tint.G;
                    row[i + 2] = tint.R;
                }
                Marshal.Copy(row, 0, IntPtr.Add(tintedData.Scan0, y * tintedData.Stride), row.Length);
            }

            return tinted;
        }
        catch
        {
            tinted.Dispose();
            throw;
        }
        finally
        {
            if (sourceData is not null)
                source.UnlockBits(sourceData);
            if (tintedData is not null)
                tinted.UnlockBits(tintedData);
        }
    }

    private static Icon OverlayRecordingDot(Icon baseIcon)
    {
        using var baseBmp = baseIcon.ToBitmap();
        using var bmp = new Bitmap(baseBmp.Width, baseBmp.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(baseBmp, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Math.Max(8, bmp.Width / 3);
            int x = bmp.Width - d - 1, y = bmp.Height - d - 1;
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(white, x - 1, y - 1, d + 2, d + 2);
            using var red = new SolidBrush(Color.FromArgb(239, 68, 68));
            g.FillEllipse(red, x, y, d, d);
        }
        var result = CreateOwnedIcon(bmp);
        baseIcon.Dispose();
        return result;
    }

    private static Icon CreateFallbackIcon(bool recording, Color strokeColor)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            using var pen = new Pen(strokeColor, 3f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLine(pen, 6, 4, 16, 16);
            g.DrawLine(pen, 26, 4, 16, 16);
            g.DrawLine(pen, 16, 16, 16, 28);
            if (recording)
            {
                using var halo = new SolidBrush(strokeColor);
                g.FillEllipse(halo, 20, 21, 12, 12);
                using var red = new SolidBrush(Color.FromArgb(239, 68, 68));
                g.FillEllipse(red, 21, 22, 10, 10);
            }
        }
        return CreateOwnedIcon(bmp);
    }

    private void RefreshAppTheme()
    {
        Theme.OnSystemThemeChanged();
        RefreshTrayIconTheme();
    }

    private void RefreshTrayIconTheme()
    {
        if (_disposed)
            return;

        _defaultIcon?.Dispose();
        _recordingIcon?.Dispose();
        _defaultIcon = CreateDefaultIcon();
        _recordingIcon = null;
        _notifyIcon.Icon = _isShowingRecording ? (_recordingIcon = CreateRecordingIcon()) : _defaultIcon;
    }

    private void RefreshTrayIconThemeOnUiThread()
    {
        if (_disposed)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished))
            return;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            try
            {
                _ = dispatcher.BeginInvoke(
                    new Action(RefreshAppTheme),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (InvalidOperationException ex)
            {
                AppDiagnostics.LogWarning("tray.theme-refresh-post", ex.Message, ex);
            }
            return;
        }

        RefreshAppTheme();
    }

    private static Icon CreateOwnedIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Native.User32.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= _themePreferenceHandler;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _menu?.Dispose();
        _notifyIcon.Dispose();
        _defaultIcon?.Dispose();
        _recordingIcon?.Dispose();
    }
}
