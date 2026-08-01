using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Models.Items;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the input monitor page. Displays the live button, stick, trigger,
/// motion, and touchpad state for the controller currently selected in the title
/// bar combobox.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="MainViewModel"/> from the DI container and mirrors its
/// <see cref="MainViewModel.SelectedItem"/>, so the page always shows the controller
/// that is active in the shell. Navigating away and back creates a fresh page instance
/// (<c>CacheSize=0</c>), which re-subscribes to selection changes.
/// </para>
/// </remarks>
public partial class InputMonitorPageViewModel : ObservableObject
{
    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public InputMonitorItem? CurrentDevice { get; private set; }

    /// <summary>
    /// Tracks the previous item so its event subscriptions are released on replacement.
    /// </summary>
    private InputMonitorItem? _previousItem;

    /// <summary>
    /// Whether a controller is selected and its input state can be displayed.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public InputMonitorPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        UpdateDevice();
    }

    /// <summary>
    /// Tracks the shell's controller selection.
    /// </summary>
    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedItem))
        {
            UpdateDevice();
        }
    }

    /// <summary>
    /// Rebuilds <see cref="CurrentDevice"/> from the shell's selected controller.
    /// Releases the previous item's event subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new InputMonitorItem(selected) : null;
        _previousItem = CurrentDevice;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
    }
}