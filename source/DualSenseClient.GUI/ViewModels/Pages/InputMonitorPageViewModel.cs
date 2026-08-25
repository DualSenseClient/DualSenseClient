using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.Settings;
using SoundFlow.Abstracts;

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
/// <para>
/// <see cref="SkinName"/> is the per-controller illustration skin stored via
/// <see cref="ControllerInfoService"/>, reused by the asset-based controller visualization;
/// it falls back to <see cref="ControllerIllustrationService.DefaultSkin"/> when no skin
/// is stored for the selected controller.
/// </para>
/// </remarks>
public partial class InputMonitorPageViewModel : ObservableObject
{
    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// The shared audio engine used by the page's audio player.
    /// </summary>
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Stores and resolves the per-controller illustration skin.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Enumerates the available illustration skins.
    /// </summary>
    private readonly ControllerIllustrationService _illustrationService;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public InputMonitorItem? CurrentDevice { get; private set; }

    /// <summary>
    /// The illustration skin rendered by the controller visualization.
    /// </summary>
    public string SkinName { get; private set; } = string.Empty;

    /// <summary>
    /// Tracks the previous item so its event subscriptions are released on replacement.
    /// </summary>
    private InputMonitorItem? _previousItem;

    /// <summary>
    /// Whether the value/coordinate tag labels are shown.
    /// </summary>
    [ObservableProperty] private bool _showStats;

    /// <summary>
    /// Whether the lightbar, player, and mute LEDs are shown.
    /// </summary>
    [ObservableProperty] private bool _showLightbarLeds = true;

    /// <summary>
    /// Whether a controller is selected and its input state can be displayed.
    /// </summary>
    public bool HasDevice
    {
        get
        {
            return CurrentDevice is not null;
        }
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public InputMonitorPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _audioEngine = App.Services.GetRequiredService<AudioEngine>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _illustrationService = App.Services.GetRequiredService<ControllerIllustrationService>();
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _controllerService.ControllersChanged += OnControllersChanged;
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
    /// Re-resolves the illustration skin when controller info changes (e.g. the user picks
    /// a different skin on the device info page), so the base image updates without needing
    /// to re-select the controller or recreate the page.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e)
    {
        string skin = ResolveSkin(_mainViewModel.SelectedItem);
        if (string.Equals(skin, SkinName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SkinName = skin;
        OnPropertyChanged(nameof(SkinName));
    }

    /// <summary>
    /// Rebuilds <see cref="CurrentDevice"/> from the shell's selected controller.
    /// Releases the previous item's event subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new InputMonitorItem(selected, _audioEngine) : null;
        _previousItem = CurrentDevice;
        SkinName = ResolveSkin(selected);

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(SkinName));
    }

    /// <summary>
    /// Resolves the illustration skin for the selected controller, falling back to the
    /// default skin when none is stored.
    /// </summary>
    private string ResolveSkin(ControllerItem? selected)
    {
        if (selected is null)
        {
            return string.Empty;
        }

        string mac = selected.PairingInfo?.ClientMac ?? string.Empty;
        string path = selected.Device.Info.Path ?? string.Empty;
        string stored = _controllerService.GetSkin(mac, path) ?? string.Empty;
        if (!string.IsNullOrEmpty(stored))
        {
            return stored;
        }

        IReadOnlyList<string> skins = _illustrationService.GetSkins();
        return skins.Contains(ControllerIllustrationService.DefaultSkin, StringComparer.OrdinalIgnoreCase)
            ? ControllerIllustrationService.DefaultSkin
            : skins.FirstOrDefault() ?? string.Empty;
    }
}