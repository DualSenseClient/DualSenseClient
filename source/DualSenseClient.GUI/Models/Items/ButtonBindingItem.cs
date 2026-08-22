using System;
using System.Linq;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels.Pages;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// One row of the bindings list: the stored rule plus preformatted display strings.
/// </summary>
public sealed class ButtonBindingItem
{
    /// <summary>
    /// The stored rule this row represents.
    /// </summary>
    public ButtonMappingEntry Entry { get; }

    /// <summary>
    /// Human-readable source keys, e.g. "Create + Options".
    /// </summary>
    public string KeysDisplay { get; }

    /// <summary>
    /// Human-readable target, e.g. "L4 (paddle)" or "None".
    /// </summary>
    public string TargetDisplay { get; }

    /// <summary>
    /// Extra badges describing the rule (click-only output, solos kept).
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Creates the row for the given stored entry.
    /// </summary>
    public ButtonBindingItem(ButtonMappingEntry entry, VirtualControllerPageViewModel owner)
    {
        Entry = entry;
        KeysDisplay = owner.DescribeKeys(entry.Keys);
        TargetDisplay = entry.Target.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.GetText("VirtualControllerPage.Mapping.Target.None")
            : entry.Target;
        Details = string.Join(", ", new[]
        {
            string.Equals(entry.TargetOutput, "click", StringComparison.OrdinalIgnoreCase)
                ? LocalizationService.GetText("VirtualControllerPage.Binding.OutputClick")
                : null,
            entry.Keys.Count > 1 && !entry.SuppressSolos
                ? LocalizationService.GetText("VirtualControllerPage.Binding.SolosKept")
                : null
        }.Where(part => part is not null));
    }
}