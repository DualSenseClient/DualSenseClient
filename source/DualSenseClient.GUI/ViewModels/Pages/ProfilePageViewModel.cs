using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the profile page. Exposes per-section edit models for the controller
/// currently selected in the title bar combobox. Currently only the lights section
/// (lightbar color, microphone LED mode, and player LEDs) is implemented.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="MainViewModel"/> from the DI container and mirrors its
/// <see cref="MainViewModel.SelectedItem"/>, so the page always edits the controller
/// that is active in the shell. Navigating away and back creates a fresh page instance
/// (<c>CacheSize=0</c>), which re-subscribes to selection changes.
/// </para>
/// </remarks>
public partial class ProfilePageViewModel : ObservableObject
{
    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public LightsItem? CurrentDevice { get; private set; }

    /// <summary>
    /// Tracks the previous item so its subscriptions are released on replacement.
    /// </summary>
    private LightsItem? _previousItem;

    /// <summary>
    /// Whether a controller is selected and the profile can be edited.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// Microphone LED mode options for the dropdown, in mode order (off, on, pulse).
    /// </summary>
    public ObservableCollection<string> MicLedModes { get; } =
    [
        LocalizationService.GetText("ProfilePage.MicLed.Mode.Off"),
        LocalizationService.GetText("ProfilePage.MicLed.Mode.On"),
        LocalizationService.GetText("ProfilePage.MicLed.Mode.Pulse")
    ];

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public ProfilePageViewModel()
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
    /// Releases the previous item's subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new LightsItem(selected) : null;
        _previousItem = CurrentDevice;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
    }
}