using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
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
        // Use existing install location if upgrading, otherwise default
        InstallPathBox.Text = InstallService.GetInstalledLocation() ?? InstallService.DefaultInstallPath;
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

            StatusText.Text = "Preparing runtimes...";
            await PrepareBundledRuntimesAsync();

            StatusText.Text = "Installed!";
            StatusDetail.Text = "";
            ProgressBar.Visibility = Visibility.Collapsed;

            InstallCompleted = true;
            InstalledPath = targetDir;
            LaunchAfter = launchAfter;

            // Hide bottom bar and text, play completion animation
            CancelBtn.Visibility = Visibility.Collapsed;
            StatusDetail.Visibility = Visibility.Collapsed;

            await PlayCompletionAnimation();
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

    private async Task PrepareBundledRuntimesAsync()
    {
        var warnings = new List<string>();

        try
        {
            StatusDetail.Text = "Installing Argos Translate...";
            await TranslationService.EnsureInstalledAsync(
                new Progress<string>(message => Dispatcher.BeginInvoke(() => StatusDetail.Text = message)));
        }
        catch (Exception ex)
        {
            warnings.Add($"Argos Translate: {TrimInstallWarning(ex.Message)}");
        }

        try
        {
            StatusDetail.Text = "Installing semantic runtime...";
            await LocalClipRuntimeService.EnsureInstalledAsync(
                new Progress<string>(message => Dispatcher.BeginInvoke(() => StatusDetail.Text = message)));
        }
        catch (Exception ex)
        {
            warnings.Add($"Semantic runtime: {TrimInstallWarning(ex.Message)}");
        }

        if (warnings.Count > 0)
        {
            StatusDetail.Text = string.Join("  |  ", warnings);
            await Task.Delay(1400);
        }
    }

    private static string TrimInstallWarning(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "setup failed";

        var singleLine = message.Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ');
        while (singleLine.Contains("  ", StringComparison.Ordinal))
            singleLine = singleLine.Replace("  ", " ");

        return singleLine.Length <= 110 ? singleLine : singleLine[..107] + "...";
    }

    private async Task PlayCompletionAnimation()
    {
        System.Windows.Controls.Panel.SetZIndex(Page2, 10);

        // Fade text out instantly
        StatusText.BeginAnimation(OpacityProperty,
            Motion.FromTo(1, 0, 170, Motion.SmoothIn));

        await Task.Delay(80);

        // Tint the logo toward the window bg color as it grows
        var bgColor = Theme.BgPrimary;
        var tintOverlay = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(bgColor),
            Opacity = 0,
            IsHitTestVisible = false,
            Width = 48,
            Height = 48,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 16),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = LogoScale // share the same scale transform
        };
        InstallingContent.Children.Insert(1, tintOverlay);

        // Logo explodes to 500x with a smoother ramp
        var growDuration = new Duration(TimeSpan.FromMilliseconds(340));
        var ease = new QuinticEase { EasingMode = EasingMode.EaseIn };

        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 500, growDuration) { EasingFunction = ease });
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 500, growDuration) { EasingFunction = ease });

        // Tint overlay on the logo fades in as it grows
        tintOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(280)))
            {
                EasingFunction = Motion.SmoothOut,
                BeginTime = TimeSpan.FromMilliseconds(100)
            });

        // Window bg overlay catches the end
        CompletionOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = Motion.SmoothOut,
                BeginTime = TimeSpan.FromMilliseconds(180)
            });

        await Task.Delay(1100);

        DialogResult = true;
        Close();
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
