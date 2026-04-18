using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OddSnap.Services;

namespace OddSnap.Models;

internal sealed class HistoryItemVM : INotifyPropertyChanged
{
    public HistoryEntry Entry { get; set; } = null!;
    public string ThumbPath { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string FileNameSearchText { get; set; } = "";
    public string NormalizedSearchText { get; set; } = "";
    public string NormalizedFileNameSearchText { get; set; } = "";
    public string OcrSearchText { get; set; } = "";
    public string SemanticSearchText { get; set; } = "";
    public string ImageSearchStatusText { get; set; } = "";
    public string ImageSearchDiagnosticsText { get; set; } = "";
    public string ImageSearchMatchText { get; set; } = "";
    public bool SearchMetadataHydrated { get; set; }
    public BitmapSource? ThumbnailSource { get; set; }
    public bool ThumbnailLoaded { get; set; }
    internal Border? Card { get; set; }
    internal System.Windows.Controls.Image? ThumbnailImage { get; set; }
    internal FrameworkElement? SelectionBadge { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
