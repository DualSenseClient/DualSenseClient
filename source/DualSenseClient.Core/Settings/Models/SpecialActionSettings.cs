using System.Text.Json.Serialization;
using DualSenseClient.Core.DualSense.Actions;
using DualSenseClient.Core.DualSense.Enums;

namespace DualSenseClient.Core.Settings.Models;

public class SpecialActionSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("button")]
    public ButtonCombination Combination { get; set; } = new ButtonCombination();

    [JsonPropertyName("swipe")]
    public SwipeActionCombination? SwipeAction { get; set; }

    [JsonPropertyName("action")]
    public SpecialActionType Type { get; set; }

    [JsonPropertyName("settings")]
    public ActionSettings Settings { get; set; } = new ActionSettings();

    [JsonIgnore]
    public bool IsSwipeAction => SwipeAction != null;
}

public class ButtonCombination
{
    [JsonPropertyName("buttons")]
    public List<ButtonType> Buttons { get; set; } = new List<ButtonType>();

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
}

public class ActionSettings
{
    // Battery Indicator Settings
    [JsonPropertyName("batteryIndicatorType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BatteryIndicatorType? BatteryIndicatorType { get; set; }

    // Add action settings here
    // Custom Lightbar Settings
    /*
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Name { get; set; }*/
}