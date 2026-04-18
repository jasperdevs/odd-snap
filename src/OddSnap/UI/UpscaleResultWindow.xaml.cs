using System.Drawing;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using OddSnap.Helpers;
using OddSnap.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using Key = System.Windows.Input.Key;

namespace OddSnap.UI;

public partial class UpscaleResultWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly Bitmap _originalBitmap;
    private readonly Action<Bitmap, string> _acceptResult;
    private Bitmap? _processedBitmap;
    private string _providerName = "";
    private bool _isProcessing;
    private bool _isDraggingCompare;
    private double _compareSplit = 0.5;
    private LocalUpscaleEngine _selectedEngine;
    private UpscaleExecutionProvider _selectedExecutionProvider;
    private Rect _compareImageRect = Rect.Empty;

    public UpscaleResultWindow(Bitmap originalBitmap, SettingsService settingsService, Action<Bitmap, string> acceptResult)
    {
        _originalBitmap = new Bitmap(originalBitmap);
        _settingsService = settingsService;
        _acceptResult = acceptResult;
        InitializeComponent();

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(8),
            UseAeroCaptionButtons = false
        });

        Theme.Refresh();
        ApplyTheme();
        LoadInitialState();
    }

    private void LoadInitialState()
    {
        var settings = _settingsService.Settings.UpscaleUploadSettings ?? new UpscaleSettings();
        _selectedExecutionProvider = settings.LocalExecutionProvider;
        var beforeSource = BitmapPerf.ToBitmapSource(_originalBitmap);
        CompareBeforeImage.Source = beforeSource;
        PopulateDownloadedModels(settings);
        LoadIcons();
        UpdateScaleText();
        SetCompareMode(false);
    }

    private void LoadIcons()
    {
        var titleIcon = System.Drawing.Color.FromArgb(210, Theme.TextSecondary.R, Theme.TextSecondary.G, Theme.TextSecondary.B);
        MinimizeTitleIcon.Source = StreamlineIcons.RenderWpf("minimize", titleIcon, 18);
        CloseTitleIcon.Source = StreamlineIcons.RenderWpf("close", titleIcon, 18);
        UseResultIcon.Source = StreamlineIcons.RenderWpf("download", System.Drawing.Color.FromArgb(245, 255, 255, 255), 18);
    }

    private void PopulateDownloadedModels(UpscaleSettings settings)
    {
        var configuredEngine = settings.GetActiveLocalEngine();
        var candidates = GetAllDownloadedModels().ToList();
        if (candidates.Count == 0)
            candidates.Add(configuredEngine);

        ModelCombo.Items.Clear();
        foreach (var engine in candidates.Distinct())
        {
            ModelCombo.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = LocalUpscaleEngineService.GetEngineLabel(engine),
                Tag = engine.ToString()
            });
        }

        var selected = candidates.Contains(configuredEngine) ? configuredEngine : candidates[0];
        SelectModel(selected);
    }

    private IEnumerable<LocalUpscaleEngine> GetAllDownloadedModels()
    {
        var engines = new[] { LocalUpscaleEngine.SwinIrRealWorld, LocalUpscaleEngine.RealEsrganX4Plus };

        return engines.Where(LocalUpscaleEngineService.IsModelDownloaded);
    }

    private void SelectModel(LocalUpscaleEngine engine)
    {
        _selectedEngine = engine;
        foreach (var item in ModelCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, engine.ToString(), StringComparison.Ordinal))
            {
                ModelCombo.SelectedItem = item;
                break;
            }
        }

        int minScale = LocalUpscaleEngineService.GetMinScaleFactor(engine);
        int maxScale = LocalUpscaleEngineService.GetScaleFactor(engine);
        ScaleSlider.Minimum = minScale;
        ScaleSlider.Maximum = maxScale;
        ScaleSlider.Value = Math.Clamp(ScaleSlider.Value, minScale, maxScale);
        StatusText.Text = "Click Upscale to generate a comparison.";
    }

    private void ApplyTheme()
    {
        RootBorder.Background = Theme.Brush(Theme.BgPrimary);
        RootBorder.BorderBrush = Theme.Brush(Theme.WindowBorder);
        RootBorder.BorderThickness = new Thickness(1);
    }

    private void UpdateScaleText()
    {
        if (ScaleValueText is null || ScaleSlider is null)
            return;

        ScaleValueText.Text = $"{(int)ScaleSlider.Value}x";
    }

    private async void UpscaleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
            return;

        _isProcessing = true;
        UpscaleBtn.IsEnabled = false;
        UseResultBtn.IsEnabled = false;
        AfterLoadingOverlay.Visibility = Visibility.Visible;
        StartLoadingAnimation();
        StatusText.Text = "Generating upscale...";

        try
        {
            var upscaleSettings = _settingsService.Settings.UpscaleUploadSettings ?? new UpscaleSettings();
            int requestedScale = (int)ScaleSlider.Value;
            upscaleSettings.ScaleFactor = requestedScale;
            upscaleSettings.LocalExecutionProvider = _selectedExecutionProvider;
            if (_selectedExecutionProvider == UpscaleExecutionProvider.Gpu)
                upscaleSettings.LocalGpuEngine = _selectedEngine;
            else
                upscaleSettings.LocalCpuEngine = _selectedEngine;
            upscaleSettings.LocalEngine = _selectedEngine;
            _settingsService.Save();

            using var input = new Bitmap(_originalBitmap);
            var result = await UpscaleService.ProcessAsync(input, upscaleSettings);
            if (!result.Success || result.Image is null)
            {
                ToastWindow.ShowError("Upscale failed", result.Error);
                return;
            }

            _processedBitmap?.Dispose();
            _processedBitmap = new Bitmap(result.Image);
            _providerName = result.ProviderName;

            CompareAfterImage.Source = BitmapPerf.ToBitmapSource(_processedBitmap);
            UseResultBtn.IsEnabled = true;
            SetCompareMode(true);
            SetCompareSplit(0.5);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogError("upscale.window", ex);
            ToastWindow.ShowError("Upscale failed", ex.Message);
        }
        finally
        {
            StopLoadingAnimation();
            AfterLoadingOverlay.Visibility = Visibility.Collapsed;
            CompareImageBlur.Radius = 0;
            UpscaleBtn.IsEnabled = true;
            _isProcessing = false;
        }
    }

    private void UseResultBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_processedBitmap is null)
            return;

        _acceptResult(new Bitmap(_processedBitmap), _providerName);
        Close();
    }

    private void SetCompareMode(bool enabled)
    {
        CompareAfterImage.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CompareDivider.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CompareHandle.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        BeforeCornerLabel.Visibility = Visibility.Visible;
        StatusText.Text = enabled
            ? "Click or drag on the image to compare."
            : "Click Upscale to generate a comparison.";
        AfterCornerLabel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetCompareSplit(double normalized)
    {
        if (_compareImageRect.IsEmpty)
            return;

        _compareSplit = Math.Clamp(normalized, 0, 1);
        double visibleWidth = _compareImageRect.Width * _compareSplit;
        CompareAfterClip.Rect = new Rect(_compareImageRect.X, _compareImageRect.Y, visibleWidth, _compareImageRect.Height);
        var dividerX = _compareImageRect.X + visibleWidth;
        CompareDivider.Margin = new Thickness(Math.Max(0, dividerX - 1), _compareImageRect.Y, 0, 0);
        CompareDivider.Height = _compareImageRect.Height;
        CompareHandle.Margin = new Thickness(dividerX - 22, 0, 0, 0);
    }

    private void CompareSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompareImageLayout();
        if (CompareAfterImage.Visibility == Visibility.Visible)
            SetCompareSplit(_compareSplit);
    }

    private void CompareSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (CompareAfterImage.Visibility != Visibility.Visible)
            return;

        _isDraggingCompare = true;
        CompareSurface.CaptureMouse();
        UpdateCompareFromPointer(e.GetPosition(CompareSurface).X);
    }

    private void CompareSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingCompare)
            return;

        UpdateCompareFromPointer(e.GetPosition(CompareSurface).X);
    }

    private void CompareSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingCompare)
            return;

        _isDraggingCompare = false;
        CompareSurface.ReleaseMouseCapture();
        UpdateCompareFromPointer(e.GetPosition(CompareSurface).X);
    }

    private void UpdateCompareFromPointer(double pointerX)
    {
        if (_compareImageRect.IsEmpty)
            return;

        var normalized = (pointerX - _compareImageRect.X) / _compareImageRect.Width;
        SetCompareSplit(normalized);
    }

    private void StartLoadingAnimation()
    {
        CompareImageBlur.Radius = 14;
        LoadingTextShimmer.Start(AfterLoadingTitle, Colors.White, opacity: 1.0);
        LoadingTextShimmer.Start(AfterLoadingSubtitle, Colors.White, opacity: 0.62);
    }

    private void StopLoadingAnimation()
    {
        LoadingTextShimmer.Stop(AfterLoadingTitle, Theme.Brush(Theme.TextPrimary), 1.0);
        LoadingTextShimmer.Stop(AfterLoadingSubtitle, Theme.Brush(Theme.TextPrimary), 0.5);
    }

    private void UpdateCompareImageLayout()
    {
        if (CompareSurface.ActualWidth <= 0 || CompareSurface.ActualHeight <= 0 || _originalBitmap.Width <= 0 || _originalBitmap.Height <= 0)
            return;

        double surfaceWidth = CompareSurface.ActualWidth;
        double surfaceHeight = CompareSurface.ActualHeight;
        double imageRatio = _originalBitmap.Width / (double)_originalBitmap.Height;
        double surfaceRatio = surfaceWidth / surfaceHeight;

        double width;
        double height;
        if (imageRatio > surfaceRatio)
        {
            width = surfaceWidth;
            height = width / imageRatio;
        }
        else
        {
            height = surfaceHeight;
            width = height * imageRatio;
        }

        double x = (surfaceWidth - width) / 2d;
        double y = (surfaceHeight - height) / 2d;
        _compareImageRect = new Rect(x, y, width, height);

        ApplyImageRect(CompareBeforeImage, _compareImageRect);
        ApplyImageRect(CompareAfterImage, _compareImageRect);
    }

    private static void ApplyImageRect(System.Windows.Controls.Image image, Rect rect)
    {
        image.Width = rect.Width;
        image.Height = rect.Height;
        image.Margin = new Thickness(rect.X, rect.Y, 0, 0);
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded && ScaleValueText is null)
            return;

        UpdateScaleText();
    }

    private void ModelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item || item.Tag is not string tag)
            return;

        if (!Enum.TryParse<LocalUpscaleEngine>(tag, out var engine))
            return;

        _selectedEngine = engine;
        int minScale = LocalUpscaleEngineService.GetMinScaleFactor(engine);
        int maxScale = LocalUpscaleEngineService.GetScaleFactor(engine);
        ScaleSlider.Minimum = minScale;
        ScaleSlider.Maximum = maxScale;
        ScaleSlider.Value = Math.Clamp(ScaleSlider.Value, minScale, maxScale);
        StatusText.Text = "Click Upscale to generate a comparison.";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => Close();
    private void MinimizeBtn_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void TitleBtn_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border b) return;
        b.Background = Theme.Brush(ReferenceEquals(b, CloseTitleBtn) ? Theme.DangerHover : Theme.AccentHover);
    }

    private void TitleBtn_Leave(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border b)
            b.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopLoadingAnimation();
        _processedBitmap?.Dispose();
        _originalBitmap.Dispose();
        base.OnClosed(e);
    }
}
