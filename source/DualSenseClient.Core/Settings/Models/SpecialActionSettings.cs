using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using DualSenseClient.Core.DualSense.Actions;
using DualSenseClient.Core.DualSense.Enums;

namespace DualSenseClient.Core.Settings.Models;

public class SpecialActionSettings : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = string.Empty;
    private ButtonCombination _combination = new ButtonCombination();
    private SwipeActionCombination? _swipeAction;
    private SpecialActionType _type;
    private ActionSettings _settings = new ActionSettings();

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("button")]
    public ButtonCombination Combination
    {
        get => _combination;
        set
        {
            if (_combination != value)
            {
                _combination = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("swipe")]
    public SwipeActionCombination? SwipeAction
    {
        get => _swipeAction;
        set
        {
            if (_swipeAction != value)
            {
                _swipeAction = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSwipeAction));
            }
        }
    }

    [JsonPropertyName("action")]
    public SpecialActionType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("settings")]
    public ActionSettings Settings
    {
        get => _settings;
        set
        {
            if (_settings != value)
            {
                _settings = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public bool IsSwipeAction => SwipeAction != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ButtonCombination : INotifyPropertyChanged
{
    private List<ButtonType> _buttons = new List<ButtonType>();

    [JsonPropertyName("buttons")]
    public List<ButtonType> Buttons
    {
        get => _buttons;
        set
        {
            if (_buttons != value)
            {
                _buttons = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [JsonIgnore]
    public bool IsEmpty => Buttons.Count == 0;

    public ButtonCombination()
    {
        Buttons = new List<ButtonType>();
    }

    public ButtonCombination(params ButtonType[] buttons) : this()
    {
        Buttons.AddRange(buttons);
    }

    public ButtonCombination(IEnumerable<ButtonType> buttons) : this()
    {
        Buttons.AddRange(buttons);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ActionSettings : INotifyPropertyChanged
{
    private BatteryIndicatorType? _batteryIndicatorType;

    // Battery Indicator Settings
    [JsonPropertyName("batteryIndicatorType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BatteryIndicatorType? BatteryIndicatorType
    {
        get => _batteryIndicatorType;
        set
        {
            if (_batteryIndicatorType != value)
            {
                _batteryIndicatorType = value;
                OnPropertyChanged();
            }
        }
    }

    // Add action settings here
    // Custom Lightbar Settings
    /*
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Name { get; set; }*/

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}