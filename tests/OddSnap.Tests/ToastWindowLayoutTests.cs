using Xunit;
using OddSnap.UI;
using OddSnap.Models;
using System.Windows;

namespace OddSnap.Tests;

public sealed class ToastWindowLayoutTests
{
    [Fact]
    public void ComputeImageOnlyPreviewLayout_UsesConsistentHeightForStandardImages()
    {
        var landscape = ToastWindow.ComputeImageOnlyPreviewLayout(1920, 1080);
        var square = ToastWindow.ComputeImageOnlyPreviewLayout(1080, 1080);

        Assert.False(landscape.Framed);
        Assert.False(square.Framed);
        Assert.InRange(landscape.Height, 180, 188);
        Assert.Equal(188, square.Height);
        Assert.InRange(System.Math.Abs(square.Height - landscape.Height), 0, 8);
    }

    [Fact]
    public void ComputeImageOnlyPreviewLayout_FramesPortraitImages()
    {
        var portrait = ToastWindow.ComputeImageOnlyPreviewLayout(800, 1400);

        Assert.True(portrait.Framed);
        Assert.Equal(188, portrait.Width);
        Assert.Equal(220, portrait.Height);
    }

    [Fact]
    public void GetPlacement_Right_UsesWorkAreaBottomRight()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        var placement = PopupWindowHelper.GetPlacement(
            ToastPosition.Right,
            actualWidth: 320,
            actualHeight: 120,
            workArea,
            edge: 8);

        Assert.Equal(1592, placement.targetLeft);
        Assert.Equal(912, placement.targetTop);
        Assert.True(placement.animateLeft);
        Assert.True(placement.startLeft > workArea.Right);
    }

    [Fact]
    public void PhysicalPixelsToDips_ConvertsWorkAreaForScaledDisplays()
    {
        var physicalWorkArea = new System.Drawing.Rectangle(0, 0, 2560, 1360);
        var converted = PopupWindowHelper.PhysicalPixelsToDips(
            physicalWorkArea,
            new System.Drawing.Point(100, 100));

        Assert.True(converted.Width <= physicalWorkArea.Width);
        Assert.True(converted.Height <= physicalWorkArea.Height);
        Assert.True(converted.Width > 0);
        Assert.True(converted.Height > 0);
    }

    [Fact]
    public void ScreenshotPreviewFrameUsesUnifiedToastEntry()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var applySpecBlock = GetMethodBlock(source, "private void ApplySpec(ToastSpec spec)");
        Assert.Contains("PreparePreviewFrameForUnifiedEntry();", applySpecBlock);
        Assert.Contains("ApplyStrokeFreeShell();", applySpecBlock);

        var prepareBlock = GetMethodBlock(source, "private void PreparePreviewFrameForUnifiedEntry()");
        Assert.Contains("ImageArea.BeginAnimation(UIElement.OpacityProperty, null);", prepareBlock);
        Assert.Contains("ImageArea.Opacity = 1;", prepareBlock);
        Assert.Contains("ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);", prepareBlock);
        Assert.Contains("ImageFrame.Opacity = 1;", prepareBlock);

        var revealBlock = GetMethodBlock(source, "private void RevealPreviewFrame(bool animateEntry)");
        Assert.Contains("ImageArea.Opacity = 1;", revealBlock);
        Assert.Contains("ImageFrame.Opacity = 1;", revealBlock);
        Assert.DoesNotContain("ImageArea.Opacity = 0;", revealBlock);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(45)", revealBlock);
        Assert.DoesNotContain("ImageArea.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation", revealBlock);

        var strokeBlock = GetMethodBlock(source, "private void ApplyStrokeFreeShell()");
        Assert.Contains("OuterShell.BorderBrush = System.Windows.Media.Brushes.Transparent;", strokeBlock);
        Assert.Contains("OuterShell.BorderThickness = new Thickness(0);", strokeBlock);

        var placementBlock = GetMethodBlock(source, "private void ApplyPlacement(bool animateEntry, bool subtleEntry)");
        Assert.Contains("RevealPreviewFrame(animateEntry: false);", placementBlock);
        Assert.Contains("RevealPreviewFrame(animateEntry: true);", placementBlock);

        var resetBlock = GetMethodBlock(source, "private void CancelActiveToastState()");
        Assert.Contains("ImageArea.BeginAnimation(UIElement.OpacityProperty, null);", resetBlock);
        Assert.Contains("ImageArea.Opacity = 1;", resetBlock);
        Assert.Contains("ImageFrame.BeginAnimation(UIElement.OpacityProperty, null);", resetBlock);
        Assert.Contains("ImageFrame.Opacity = 1;", resetBlock);
    }

    [Fact]
    public void ToastShellStrokeIsRemovedForAllToastStates()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));
        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml"));

        var configureBlock = GetMethodBlock(source, "private void ConfigureShell()");
        Assert.Contains("ApplyStrokeFreeShell();", configureBlock);
        Assert.Contains("ImageFrame.BorderBrush = System.Windows.Media.Brushes.Transparent;", configureBlock);
        Assert.Contains("ImageFrame.BorderThickness = new Thickness(0);", configureBlock);
        Assert.Contains("InlinePreviewHost.BorderBrush = System.Windows.Media.Brushes.Transparent;", configureBlock);
        Assert.Contains("InlinePreviewHost.BorderThickness = new Thickness(0);", configureBlock);
        Assert.DoesNotContain("OuterShell.BorderThickness = new Thickness(2.0);", configureBlock);
        Assert.DoesNotContain("OuterShell.BorderBrush = Theme.Brush(Color.FromArgb(180, 255, 255, 255));", configureBlock);

        var applySpecBlock = GetMethodBlock(source, "private void ApplySpec(ToastSpec spec)");
        Assert.Contains("ApplyStrokeFreeShell();", applySpecBlock);
        Assert.DoesNotContain("OuterShell.BorderThickness = new Thickness(2.0);", applySpecBlock);
        Assert.DoesNotContain("OuterShell.BorderBrush = Theme.Brush(Color.FromArgb(160, red.R, red.G, red.B));", applySpecBlock);

        var dragStartBlock = GetMethodBlock(source, "private void BeginDragFeedback()");
        Assert.Contains("ApplyStrokeFreeShell();", dragStartBlock);
        Assert.DoesNotContain("OuterShell.BorderThickness = new Thickness(2.4);", dragStartBlock);

        var resetBlock = GetMethodBlock(source, "private void CancelActiveToastState()");
        Assert.Contains("ApplyStrokeFreeShell();", resetBlock);
        Assert.DoesNotContain("new Thickness(2.0)", resetBlock);

        var inlinePreviewIndex = xaml.IndexOf("x:Name=\"InlinePreviewHost\"", StringComparison.Ordinal);
        Assert.True(inlinePreviewIndex >= 0, "Inline preview host should exist.");
        var inlinePreviewBlock = xaml[inlinePreviewIndex..Math.Min(xaml.Length, inlinePreviewIndex + 420)];
        Assert.Contains("BorderBrush=\"Transparent\"", inlinePreviewBlock);
        Assert.Contains("BorderThickness=\"0\"", inlinePreviewBlock);

        var shellStrokeIndex = xaml.IndexOf("x:Name=\"ShellStroke\"", StringComparison.Ordinal);
        Assert.True(shellStrokeIndex >= 0, "Shell stroke should exist.");
        var shellStrokeBlock = xaml[shellStrokeIndex..Math.Min(xaml.Length, shellStrokeIndex + 420)];
        Assert.Contains("BorderThickness=\"0\"", shellStrokeBlock);
        Assert.DoesNotContain("BorderThickness=\"1\"", shellStrokeBlock);
    }

    [Fact]
    public void InPlaceToastRefreshUsesSubtleUnifiedAnimation()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var updateBlock = GetMethodBlock(source, "internal bool TryUpdateInPlace(ToastSpec spec)");
        Assert.Contains("if (!CanUpdateInPlace(_spec, spec))", updateBlock);
        Assert.Contains("BeginAtomicToastContentSwap();", updateBlock);
        Assert.Contains("ApplyPlacement(animateEntry: true, subtleEntry: true);", updateBlock);
        Assert.DoesNotContain("ApplyPlacement(animateEntry: true, subtleEntry: false);", updateBlock);
        Assert.DoesNotContain("ApplyPlacement(animateEntry: false, subtleEntry: false);", updateBlock);

        var eligibilityBlock = GetMethodBlock(source, "private static bool CanUpdateInPlace(ToastSpec current, ToastSpec next)");
        Assert.Contains("IsTextOnlyToast(current)", eligibilityBlock);
        Assert.Contains("IsTextOnlyToast(next)", eligibilityBlock);
        Assert.Contains("current.IsError == next.IsError", eligibilityBlock);

        var textOnlyBlock = GetMethodBlock(source, "private static bool IsTextOnlyToast(ToastSpec spec)");
        Assert.Contains("spec.PreviewBitmap is null", textOnlyBlock);
        Assert.Contains("spec.InlinePreviewBitmap is null", textOnlyBlock);
        Assert.Contains("!spec.SwatchColor.HasValue", textOnlyBlock);

        var atomicBlock = GetMethodBlock(source, "private void BeginAtomicToastContentSwap()");
        Assert.Contains("BeginAnimation(OpacityProperty, null);", atomicBlock);
        Assert.Contains("Opacity = 1;", atomicBlock);
        Assert.Contains("ImageArea.Opacity = 1;", atomicBlock);

        var placementBlock = GetMethodBlock(source, "private void ApplyPlacement(bool animateEntry, bool subtleEntry)");
        Assert.Contains("BeginCompositedToastAnimation();", placementBlock);
        Assert.Contains("BeginAnimation(LeftProperty, new DoubleAnimation", placementBlock);
        Assert.Contains("BeginAnimation(TopProperty, new DoubleAnimation", placementBlock);
        Assert.Contains("Opacity = 1;", placementBlock);
        Assert.Contains("var cleanupTimer = new DispatcherTimer", placementBlock);
        Assert.DoesNotContain("var opacityAnimation = new DoubleAnimation", placementBlock);
        Assert.DoesNotContain("From = 0,", placementBlock);
        Assert.DoesNotContain("BeginAnimation(OpacityProperty, opacityAnimation)", placementBlock);
        Assert.DoesNotContain("SlideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation", placementBlock);
        Assert.DoesNotContain("SlideTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation", placementBlock);

        var pulseBlock = GetMethodBlock(source, "private void PulseRefreshAnimation()");
        Assert.DoesNotContain("Root.Opacity = 0.94;", pulseBlock);
        Assert.DoesNotContain("Root.BeginAnimation(UIElement.OpacityProperty", pulseBlock);
    }

    [Fact]
    public void ToastEntryWaitsForComposedHiddenFrameAndAnimatesAsOneVisual()
    {
        var source = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml.cs"));

        var xaml = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.xaml"));
        var staticSource = File.ReadAllText(RepoPath("src", "OddSnap", "UI", "ToastWindow.Static.cs"));

        Assert.Contains("AllowsTransparency=\"False\"", xaml);
        Assert.Contains("WindowStartupLocation=\"Manual\"", xaml);

        var staticShowBlock = GetMethodBlock(staticSource, "internal static void Show(ToastSpec spec)");
        Assert.Contains("toast.PrepareForShow();", staticShowBlock);
        Assert.True(staticShowBlock.IndexOf("toast.PrepareForShow();", StringComparison.Ordinal) <
            staticShowBlock.IndexOf("toast.Show();", StringComparison.Ordinal));

        var loadedBlock = GetMethodBlock(source, "private void OnLoaded(object sender, RoutedEventArgs e)");
        Assert.Contains("QueueEntryAfterFirstComposedFrame();", loadedBlock);
        Assert.DoesNotContain("ApplyPlacement(animateEntry: true", loadedBlock);

        var prepareBlock = GetMethodBlock(source, "internal void PrepareForShow()");
        Assert.Contains("Left = startLeft;", prepareBlock);
        Assert.Contains("Top = startTop;", prepareBlock);
        Assert.Contains("Opacity = 1;", prepareBlock);
        Assert.Contains("RevealPreviewFrame(animateEntry: false);", prepareBlock);
        Assert.DoesNotContain("Opacity = 0;", prepareBlock);
        Assert.DoesNotContain("BeginCompositedToastAnimation();", prepareBlock);

        var queueBlock = GetMethodBlock(source, "private void QueueEntryAfterFirstComposedFrame()");
        Assert.Contains("CompositionTarget.Rendering -= rendered;", queueBlock);
        Assert.Contains("Dispatcher.BeginInvoke(new Action(() =>", queueBlock);
        Assert.Contains("ApplyPlacement(animateEntry: true, subtleEntry: false);", queueBlock);
        Assert.Contains("RestartVisibleTimer(_durationSeconds);", queueBlock);

        var placementBlock = GetMethodBlock(source, "private void ApplyPlacement(bool animateEntry, bool subtleEntry)");
        Assert.Contains("Left = entryLeft;", placementBlock);
        Assert.Contains("Top = entryTop;", placementBlock);
        Assert.Contains("To = targetLeft,", placementBlock);
        Assert.Contains("To = targetTop,", placementBlock);
        Assert.DoesNotContain("BeginAnimation(OpacityProperty, opacityAnimation)", placementBlock);
        Assert.DoesNotContain("SlideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation", placementBlock);
        Assert.DoesNotContain("SlideTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation", placementBlock);

        var beginCacheBlock = GetMethodBlock(source, "private void BeginCompositedToastAnimation()");
        Assert.Contains("OuterShell.CacheMode = new BitmapCache", beginCacheBlock);
        Assert.Contains("EnableClearType = true", beginCacheBlock);
        Assert.Contains("SnapsToDevicePixels = true", beginCacheBlock);

        var endCacheBlock = GetMethodBlock(source, "private void EndCompositedToastAnimation()");
        Assert.Contains("OuterShell.CacheMode = null;", endCacheBlock);

        var dismissBlock = GetMethodBlock(source, "private void StartDismissAnimation(TimeSpan duration, bool slide, double offsetX, double offsetY)");
        Assert.Contains("BeginCompositedToastAnimation();", dismissBlock);
        Assert.Contains("StartDismissCloseTimer(duration, dismissToken);", dismissBlock);
        Assert.Contains("return;", dismissBlock);

        var slideCloseIndex = dismissBlock.IndexOf("StartDismissCloseTimer(duration, dismissToken);", StringComparison.Ordinal);
        var opacityAnimationIndex = dismissBlock.IndexOf("var opacityAnimation = new DoubleAnimation", StringComparison.Ordinal);
        Assert.True(slideCloseIndex >= 0 && opacityAnimationIndex > slideCloseIndex,
            "Slide dismiss should close from a timer instead of fading the window opacity.");

        var forceCloseBlock = GetMethodBlock(source, "private bool TryForceClose(bool force = false)");
        Assert.Contains("HideToastSurfaceForClose();", forceCloseBlock);

        var hideForCloseBlock = GetMethodBlock(source, "private void HideToastSurfaceForClose()");
        Assert.Contains("BeginAnimation(OpacityProperty, null);", hideForCloseBlock);
        Assert.Contains("Opacity = 0;", hideForCloseBlock);
        Assert.Contains("Visibility = System.Windows.Visibility.Hidden;", hideForCloseBlock);
        Assert.Contains("EndCompositedToastAnimation();", hideForCloseBlock);
    }

    private static string GetMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");

        var bodyStart = source.IndexOf('{', start);
        Assert.True(bodyStart > start, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not read method: {signature}");
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file: {Path.Combine(parts)}");
    }
}
