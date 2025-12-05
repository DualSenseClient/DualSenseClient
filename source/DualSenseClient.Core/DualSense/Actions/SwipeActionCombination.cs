using System.ComponentModel;
using System.Runtime.CompilerServices;
using DualSenseClient.Core.DualSense.Enums;

namespace DualSenseClient.Core.DualSense.Actions;

/// <summary>
/// Represents a swipe gesture as a special action trigger
/// </summary>
public class SwipeActionCombination : INotifyPropertyChanged
{
    private SwipeDirection _direction;
    private bool _isLongPress = false; // For future extension

    public SwipeDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction != value)
            {
                _direction = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLongPress
    {
        get => _isLongPress;
        set
        {
            if (_isLongPress != value)
            {
                _isLongPress = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum SwipeDirection
{
    Left,
    Right,
    Up,
    Down
}