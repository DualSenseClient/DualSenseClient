using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Root settings for the special actions feature: a single global list of special actions.
/// Actions are enabled per controller (see <see cref="SpecialAction.EnabledControllers"/>).
/// </summary>
public class SpecialActionsSettings
{
    /// <summary>
    /// Gets or sets the global list of special actions.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<SpecialAction> Actions { get; set; } = [];
}