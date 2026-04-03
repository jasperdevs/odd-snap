using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Yoink.Services;

namespace Yoink.Models;

internal sealed class HistoryItemVM : INotifyPropertyChanged
{
    public HistoryEntry Entry { get; set; } = null!;
    public string ThumbPath { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    internal Border? Card { get; set; }
    internal FrameworkElement? SelectionBadge { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
