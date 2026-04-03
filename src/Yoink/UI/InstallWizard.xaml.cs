using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Yoink.Services;

namespace Yoink.UI;

public partial class InstallWizard : Window
{
    public bool InstallCompleted { get; private set; }
    public string InstalledPath { get; private set; } = "";
    public bool LaunchAfter { get; private set; }

    public InstallWizard()
    {
        Theme.Refresh();
        InitializeComponent();
        ApplyTheme();
        InstallPathBox.Text = InstallService.DefaultInstallPath;
    }

    private void OnSourceInit(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.Dwm.DisableBackdrop(hwnd);
    }

    private void ApplyTheme()
    {
        Theme.Refresh();
        Resources["WizBg"] = Theme.Brush(Theme.BgPrimary);
        Resources["WizCardBg"] = Theme.Brush(Theme.BgCard);
        Resources["WizFg"] = Theme.Brush(Theme.TextPrimary);
        Resources["WizFgMuted"] = Theme.Brush(Theme.TextSecondary);
        Resources["WizBorder"] = Theme.Brush(Theme.WindowBorder);
        Resources["WizInputBg"] = Theme.Brush(Theme.BgSecondary);
        Resources["WizBtnPrimaryBg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(240, 240, 240)
            : System.Windows.Media.Color.FromRgb(30, 30, 30));
        Resources["WizBtnPrimaryFg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(26, 26, 26)
            : System.Windows.Media.Color.FromRgb(240, 240, 240));
        Resources["WizBtnSecondaryBg"] = Theme.Brush(Theme.AccentSubtle);
        Resources["WizBtnSecondaryFg"] = Theme.Brush(Theme.TextPrimary);
        Resources["WizBtnPrimaryBorder"] = Theme.Brush(Theme.BorderSubtle);
        Resources["WizShadowColor"] = Theme.IsDark
            ? System.Windows.Media.Color.FromArgb(128, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(72, 0, 0, 0);
        Resources["WizProgressFg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(240, 240, 240)
            : System.Windows.Media.Color.FromRgb(30, 30, 30));
        Resources["WizProgressBg"] = Theme.Brush(Theme.IsDark
            ? System.Windows.Media.Color.FromRgb(45, 45, 45)
            : System.Windows.Media.Color.FromRgb(229, 229, 229));
        Foreground = Theme.Brush(Theme.TextPrimary);
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = InstallPathBox.Text,
            Description = "Choose install location",
            ShowNewFolderButton = true,
        };
        var owner = new WindowHandleWrapper(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            InstallPathBox.Text = Path.Combine(dlg.SelectedPath, "Yoink");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var targetDir = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            MessageBox.Show("Please choose an install location.", "Install", MessageBoxButton.OK);
            return;
        }

        var desktopShortcut = DesktopShortcutCheck.IsChecked == true;
        var startMenuShortcut = StartMenuCheck.IsChecked == true;
        var startWithWindows = StartWithWindowsCheck.IsChecked == true;
        var launchAfter = LaunchAfterCheck.IsChecked == true;
        var dispatcher = Dispatcher;

        // Switch to installing page
        Page1.Visibility = Visibility.Collapsed;
        Page2.Visibility = Visibility.Visible;
        InstallBtn.IsEnabled = false;
        InstallBtn.Visibility = Visibility.Collapsed;
        CancelBtn.IsEnabled = false;

        try
        {
            await Task.Run(() =>
            {
                InstallService.Install(
                    targetDir,
                    desktopShortcut,
                    startMenuShortcut,
                    startWithWindows,
                    status => dispatcher.BeginInvoke(() => StatusDetail.Text = status));
            });

            StatusText.Text = "Installed!";
            StatusDetail.Text = "Yoink has been installed successfully.";
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;

            InstallCompleted = true;
            InstalledPath = targetDir;
            LaunchAfter = launchAfter;

            // Brief pause to show success, then close
            await Task.Delay(800);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation failed";
            StatusDetail.Text = ex.Message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(239, 68, 68));
            CancelBtn.IsEnabled = true;
            CancelBtn.Content = "Close";
            InstallBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class WindowHandleWrapper : System.Windows.Forms.IWin32Window
    {
        public WindowHandleWrapper(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
