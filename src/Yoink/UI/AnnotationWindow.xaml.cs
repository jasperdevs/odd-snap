using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Yoink.Models;
using Yoink.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace Yoink.UI;

public partial class AnnotationWindow : Window
{
    private readonly Bitmap _originalScreenshot;
    private readonly List<AnnotationItem> _annotations = new();
    private readonly Stack<AnnotationItem> _redoStack = new();

    private AnnotationTool _currentTool = AnnotationTool.Arrow;
    private WpfColor _currentColor = WpfColors.Red;

    private bool _isDrawing;
    private WpfPoint _drawStart;
    private AnnotationItem? _activeAnnotation;

    // For text input
    private TextBox? _activeTextBox;

    public AnnotationWindow(Bitmap screenshot)
    {
        _originalScreenshot = screenshot;
        InitializeComponent();

        Width = Math.Min(screenshot.Width + 40, SystemParameters.WorkArea.Width * 0.85);
        Height = Math.Min(screenshot.Height + 120, SystemParameters.WorkArea.Height * 0.85);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderCanvas();
    }

    private void RenderCanvas()
    {
        DrawCanvas.Children.Clear();

        // Background image
        var img = new System.Windows.Controls.Image
        {
            Source = BitmapToSource(_originalScreenshot),
            Stretch = Stretch.None
        };
        DrawCanvas.Children.Add(img);
        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);

        DrawCanvas.Width = _originalScreenshot.Width;
        DrawCanvas.Height = _originalScreenshot.Height;

        // Annotation overlay
        var overlay = new AnnotationVisual(_annotations);
        overlay.Width = _originalScreenshot.Width;
        overlay.Height = _originalScreenshot.Height;
        DrawCanvas.Children.Add(overlay);
        Canvas.SetLeft(overlay, 0);
        Canvas.SetTop(overlay, 0);
    }

    // -- Tool selection --

    private void ToolChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            _currentTool = Enum.Parse<AnnotationTool>(tag);
        }
    }

    private void ColorPicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string colorName)
        {
            _currentColor = colorName switch
            {
                "Red" => WpfColors.Red,
                "Orange" => WpfColor.FromRgb(0xFF, 0x88, 0x00),
                "Yellow" => WpfColors.Yellow,
                "Green" => WpfColor.FromRgb(0x00, 0xCC, 0x00),
                "Blue" => WpfColor.FromRgb(0x00, 0x88, 0xFF),
                "White" => WpfColors.White,
                _ => WpfColors.Red
            };
        }
    }

    // -- Canvas mouse handling --

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);
        _drawStart = pos;
        _isDrawing = true;
        _redoStack.Clear();

        DrawCanvas.CaptureMouse();

        switch (_currentTool)
        {
            case AnnotationTool.Arrow:
                _activeAnnotation = new ArrowAnnotation
                {
                    Start = pos, End = pos, StrokeColor = _currentColor, StrokeWidth = 2
                };
                break;

            case AnnotationTool.Text:
                _isDrawing = false;
                DrawCanvas.ReleaseMouseCapture();
                ShowTextInput(pos);
                return;

            case AnnotationTool.Highlighter:
                var hl = new HighlighterAnnotation
                {
                    StrokeColor = _currentColor, StrokeWidth = 16
                };
                hl.Points.Add(pos);
                _activeAnnotation = hl;
                break;

            case AnnotationTool.Rectangle:
                _activeAnnotation = new RectangleAnnotation
                {
                    Bounds = new Rect(pos, pos), StrokeColor = _currentColor, StrokeWidth = 2
                };
                break;

            case AnnotationTool.Blur:
                _activeAnnotation = new BlurAnnotation
                {
                    Bounds = new Rect(pos, pos)
                };
                break;
        }

        if (_activeAnnotation != null)
        {
            _annotations.Add(_activeAnnotation);
            RenderCanvas();
        }
    }

    private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDrawing || _activeAnnotation == null) return;

        var pos = e.GetPosition(DrawCanvas);

        switch (_activeAnnotation)
        {
            case ArrowAnnotation arrow:
                arrow.End = pos;
                break;
            case HighlighterAnnotation hl:
                hl.Points.Add(pos);
                break;
            case RectangleAnnotation rect:
                rect.Bounds = new Rect(_drawStart, pos);
                break;
            case BlurAnnotation blur:
                blur.Bounds = new Rect(_drawStart, pos);
                break;
        }

        RenderCanvas();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        _activeAnnotation = null;
        DrawCanvas.ReleaseMouseCapture();
        RenderCanvas();
    }

    private void ShowTextInput(WpfPoint pos)
    {
        _activeTextBox = new TextBox
        {
            FontSize = 16,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(WpfColor.FromArgb(180, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 100,
            AcceptsReturn = false
        };

        Canvas.SetLeft(_activeTextBox, pos.X);
        Canvas.SetTop(_activeTextBox, pos.Y);
        DrawCanvas.Children.Add(_activeTextBox);

        _activeTextBox.Focus();
        _activeTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                CommitText(pos);
                e.Handled = true;
            }
        };
        _activeTextBox.LostFocus += (s, e) => CommitText(pos);
    }

    private void CommitText(WpfPoint pos)
    {
        if (_activeTextBox == null) return;

        string text = _activeTextBox.Text;
        DrawCanvas.Children.Remove(_activeTextBox);
        _activeTextBox = null;

        if (!string.IsNullOrWhiteSpace(text))
        {
            _annotations.Add(new TextAnnotation
            {
                Position = pos,
                Text = text,
                StrokeColor = _currentColor
            });
            RenderCanvas();
        }
    }

    // -- Undo / Redo --

    private void UndoClick(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0) return;
        var last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        _redoStack.Push(last);
        RenderCanvas();
    }

    private void RedoClick(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        _annotations.Add(_redoStack.Pop());
        RenderCanvas();
    }

    // -- Copy / Save --

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        using var result = FlattenAnnotations();
        ClipboardService.CopyToClipboard(result);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"yoink_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() == true)
        {
            using var result = FlattenAnnotations();
            var format = dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg : ImageFormat.Png;
            result.Save(dialog.FileName, format);
        }
    }

    private Bitmap FlattenAnnotations()
    {
        int w = _originalScreenshot.Width;
        int h = _originalScreenshot.Height;

        // Render the canvas to a RenderTargetBitmap
        var visual = new AnnotationVisual(_annotations);
        visual.Width = w;
        visual.Height = h;
        visual.Measure(new System.Windows.Size(w, h));
        visual.Arrange(new Rect(0, 0, w, h));
        visual.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        // Combine original screenshot with annotations
        var result = new Bitmap(_originalScreenshot);
        using var g = Graphics.FromImage(result);

        // Convert RenderTargetBitmap to System.Drawing.Bitmap
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(ms);
        ms.Position = 0;
        using var annotationBmp = new Bitmap(ms);
        g.DrawImage(annotationBmp, 0, 0);

        return result;
    }

    private static BitmapSource BitmapToSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = ms;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}

/// <summary>
/// A lightweight FrameworkElement that renders all annotations via DrawingContext.
/// </summary>
internal sealed class AnnotationVisual : FrameworkElement
{
    private readonly List<AnnotationItem> _items;

    public AnnotationVisual(List<AnnotationItem> items)
    {
        _items = items;
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var item in _items)
            item.Render(dc);
    }
}
