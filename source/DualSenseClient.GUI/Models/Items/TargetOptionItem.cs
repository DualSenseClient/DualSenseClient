using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// One selectable virtual-button target in the remapping editor's pending selection.
/// </summary>
public sealed class TargetOptionItem : ObservableObject
{
    /// <summary>
    /// Notified after every selection change so the owner can enforce exclusivity rules
    /// and refresh derived state.
    /// </summary>
    private readonly Action<TargetOptionItem, bool> _onChanged;

    /// <summary>
    /// The raw settings name of the target, e.g. "Y" or "None".
    /// </summary>
    public string Raw { get; }

    /// <summary>
    /// Human-readable label shown on the toggle, e.g. "Y (triangle)" or "None".
    /// </summary>
    public string Display { get; }

    /// <summary>
    /// Backing field for <see cref="IsSelected"/>.
    /// </summary>
    private bool _isSelected;

    /// <summary>
    /// Whether the target is part of the pending assignment. Setting it notifies the
    /// owner, which keeps "None" exclusive against the other targets.
    /// </summary>
    public bool IsSelected
    {
        get
        {
            return _isSelected;
        }
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _onChanged(this, value);
        }
    }

    /// <summary>
    /// Creates the row for the given raw target name.
    /// </summary>
    public TargetOptionItem(string raw, string display, Action<TargetOptionItem, bool> onChanged)
    {
        Raw = raw;
        Display = display;
        _onChanged = onChanged;
    }
}