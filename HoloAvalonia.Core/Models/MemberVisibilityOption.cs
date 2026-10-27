using System.ComponentModel;

namespace HoloAvalonia.Models;

public sealed class MemberVisibilityOption : INotifyPropertyChanged
{
    private bool _isVisible;

    public required string MemberName { get; init; }
    public string MemberIcon { get; init; } = string.Empty;
    public required string DisplayName { get; init; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
