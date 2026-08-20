using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.HidHide;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the device info page. Displays firmware and hardware information
/// for the controller currently selected in the title bar combobox, lets the user
/// rename it, and configures its virtual controller emulation settings (which are
/// stored per controller, not per profile).
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="MainViewModel"/> from the DI container and mirrors its
/// <see cref="MainViewModel.SelectedItem"/>, so the page always shows the controller
/// that is active in the shell. Navigating away and back creates a fresh page instance
/// (<c>CacheSize=0</c>), which re-subscribes to selection changes.
/// </para>
/// <para>
/// Firmware info is read once when the controller connects; <see cref="RefreshCommand"/>
/// re-reads it on demand (e.g. to retry a read that failed at connect time).
/// </para>
/// </remarks>
public partial class DeviceInfoPageViewModel : ObservableObject
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DeviceInfoPage");

    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Service storing persistent controller info, used to read and rename the custom
    /// display name of the selected controller.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Service providing the embedded controller illustration skins and their bitmaps.
    /// </summary>
    private readonly ControllerIllustrationService _illustrationService;

    /// <summary>
    /// Service creating the selected controller's virtual controller and applying
    /// its emulation settings.
    /// </summary>
    private readonly IEmulationService _emulation;

    /// <summary>
    /// Delay between the last emulation slider change and the controller info save,
    /// so dragging a slider coalesces into a single disk write.
    /// </summary>
    private static readonly TimeSpan EmulationSaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Debounced emulation settings save timer: each slider change restarts it and the
    /// save happens only after changes stop.
    /// </summary>
    private readonly DispatcherTimer _emulationSaveTimer;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

    /// <summary>
    /// Live controller state driving the reusable controller visualization, or <c>null</c>
    /// when no controller is selected.
    /// </summary>
    public InputMonitorItem? MonitorState { get; private set; }

    /// <summary>
    /// Tracks the previous monitor state so its event subscriptions are released on
    /// replacement.
    /// </summary>
    private InputMonitorItem? _previousMonitorState;

    /// <summary>
    /// The user-visible controller name (custom name, or product name when none was set).
    /// Editable: assigning a new name renames the controller in
    /// <see cref="ControllerInfoService"/> and persists it.
    /// </summary>
    public string ControllerName
    {
        get => _controllerName;
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > ControllerInfoService.MaxNameLength)
            {
                trimmed = trimmed[..ControllerInfoService.MaxNameLength];
            }

            if (string.Equals(trimmed, _controllerName, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged();
                return;
            }

            _controllerName = trimmed;
            if (CurrentDevice is not null && !string.IsNullOrEmpty(trimmed))
            {
                bool renamed = _controllerService.RenameController(CurrentMac, CurrentDevicePath, trimmed);
                if (renamed)
                {
                    _log.Info($"Renamed controller '{CurrentMac}' to '{trimmed}'");
                }
                else
                {
                    _controllerName = _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, CurrentDevice.DisplayName);
                }
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Backing field for <see cref="ControllerName"/>.
    /// </summary>
    private string _controllerName = string.Empty;

    /// <summary>
    /// The Bluetooth MAC address of the selected controller, or empty when unavailable.
    /// </summary>
    private string CurrentMac => CurrentDevice?.Controller.PairingInfo?.ClientMac ?? string.Empty;

    /// <summary>
    /// The HID device path of the selected controller.
    /// </summary>
    private string CurrentDevicePath => CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;

    /// <summary>
    /// Tracks the previous item so its event subscriptions are released on replacement.
    /// </summary>
    private DeviceInfoItem? _previousItem;

    /// <summary>
    /// Whether the battery indicator shows the percentage text (instead of the icon).
    /// Clicking the shown element toggles to the other.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBatteryIcon))]
    private bool showBatteryPercentage;

    /// <summary>
    /// Whether the battery indicator shows the icon (instead of the percentage text).
    /// </summary>
    public bool ShowBatteryIcon => !ShowBatteryPercentage;

    /// <summary>
    /// Switches the battery indicator between the percentage text and the icon.
    /// </summary>
    [RelayCommand]
    private void ToggleBatteryDisplay() => ShowBatteryPercentage = !ShowBatteryPercentage;

    // ── Controller illustration ────────────────────────────────

    /// <summary>
    /// The available controller illustration skins, in display order.
    /// </summary>
    public ObservableCollection<string> Skins { get; } = [];

    /// <summary>
    /// The illustration skin rendered by the controller visualization, or empty when no
    /// controller is selected.
    /// </summary>
    public string SkinName => Skins.Count > 0 ? Skins[Math.Clamp(_skinIndex, 0, Skins.Count - 1)] : string.Empty;

    /// <summary>
    /// Backing field for <see cref="SkinIndex"/>.
    /// </summary>
    private int _skinIndex;

    /// <summary>
    /// The index of the selected controller's illustration skin in <see cref="Skins"/>.
    /// Setting it stores the skin per controller in <see cref="ControllerInfoService"/>
    /// and refreshes the illustration.
    /// </summary>
    public int SkinIndex
    {
        get => _skinIndex;
        set
        {
            if (value < 0 || value >= Skins.Count || value == _skinIndex)
            {
                return;
            }

            string skin = Skins[value];
            _log.Info($"Setting illustration skin of {CurrentMac} to '{skin}'");
            _skinIndex = value;
            _controllerService.SetSkin(CurrentMac, CurrentDevicePath, skin);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SkinName));
        }
    }

    /// <summary>
    /// Whether a controller is selected and its info can be displayed.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// Virtual controller emulation mode options for the dropdown, in
    /// <see cref="EmulationMode"/> order (off, Xbox 360, DualShock 4, DualSense).
    /// </summary>
    public ObservableCollection<string> EmulationModes { get; } =
    [
        LocalizationService.GetText("DeviceInfoPage.Emulation.Mode.Off"),
        LocalizationService.GetText("DeviceInfoPage.Emulation.Mode.Xbox360"),
        LocalizationService.GetText("DeviceInfoPage.Emulation.Mode.DualShock4"),
        LocalizationService.GetText("DeviceInfoPage.Emulation.Mode.DualSense")
    ];

    /// <summary>
    /// DualSense hardware variant options for the dropdown, in
    /// <see cref="DualSenseVariant"/> order (standard, Edge).
    /// </summary>
    public ObservableCollection<string> DualSenseVariants { get; } =
    [
        LocalizationService.GetText("DeviceInfoPage.Emulation.DeviceType.Standard"),
        LocalizationService.GetText("DeviceInfoPage.Emulation.DeviceType.Edge")
    ];

    /// <summary>
    /// Forwarded audio output options for the dropdown, in
    /// <see cref="EmulationAudioOutput"/> order (speaker, headset).
    /// </summary>
    public ObservableCollection<string> EmulationAudioOutputs { get; } =
    [
        LocalizationService.GetText("DeviceInfoPage.Emulation.AudioOutput.Speaker"),
        LocalizationService.GetText("DeviceInfoPage.Emulation.AudioOutput.Headset")
    ];

    /// <summary>
    /// The emulation settings stored for the selected controller. The returned instance
    /// is the live stored object; mutating it and calling
    /// <see cref="ControllerInfoService.SaveEmulationSettings"/> persists the change.
    /// </summary>
    private EmulationSettings GetEmulationSettings()
        => _controllerService.GetEmulationSettings(CurrentMac, CurrentDevicePath);

    /// <summary>
    /// The virtual controller emulation mode (<see cref="EmulationMode"/> value) of the
    /// selected controller. Setting it persists the change immediately and recreates
    /// the virtual controller through <see cref="IEmulationService"/>.
    /// </summary>
    public int EmulationModeIndex
    {
        get
        {
            if (!HasDevice)
            {
                return 0;
            }
            return (int)GetEmulationSettings().Mode;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationMode mode = (EmulationMode)Math.Clamp(value, 0, (int)EmulationMode.DualSense);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Mode == mode)
            {
                return;
            }

            _log.Info($"Setting emulation mode of {CurrentMac} to {mode}");
            settings.Mode = mode;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(EmulationModeIndex));
            OnPropertyChanged(nameof(IsDualSenseEmulation));
            OnPropertyChanged(nameof(IsAudioEmulation));
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// Whether the selected controller's emulation mode is DualSense, the only mode
    /// with a DualSense hardware variant.
    /// </summary>
    public bool IsDualSenseEmulation => EmulationModeIndex == (int)EmulationMode.DualSense;

    /// <summary>
    /// Whether the selected controller's emulation mode forwards host audio to the
    /// physical controller (DualSense or DualShock 4).
    /// </summary>
    public bool IsAudioEmulation => EmulationModeIndex is (int)EmulationMode.DualSense or (int)EmulationMode.DualShock4;

    /// <summary>
    /// The DualSense hardware variant (<see cref="DualSenseVariant"/> value) of the
    /// selected controller's virtual device. Setting it persists the change immediately
    /// and recreates the virtual controller through <see cref="IEmulationService"/>.
    /// </summary>
    public int DualSenseVariantIndex
    {
        get
        {
            if (!HasDevice)
            {
                return 0;
            }
            return (int)GetEmulationSettings().DeviceType;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            DualSenseVariant variant = (DualSenseVariant)Math.Clamp(value, 0, (int)DualSenseVariant.Edge);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.DeviceType == variant)
            {
                return;
            }

            _log.Info($"Setting DualSense variant of {CurrentMac} to {variant}");
            settings.DeviceType = variant;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(DualSenseVariantIndex));
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// The concrete DualSense device of the selected controller, or <c>null</c> for
    /// non-DualSense devices.
    /// </summary>
    private DualSenseDevice? CurrentDualSenseDevice => CurrentDevice?.Controller.Device as DualSenseDevice;

    /// <summary>
    /// The physical controller output (<see cref="EmulationAudioOutput"/> value) used
    /// when forwarding host audio. Setting it persists the change immediately and
    /// applies it to the active forwarder without recreating the virtual controller.
    /// </summary>
    public int ForwardAudioOutputIndex
    {
        get
        {
            if (!HasDevice)
            {
                return 0;
            }
            return (int)GetEmulationSettings().ForwardAudioOutput;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationAudioOutput output = (EmulationAudioOutput)Math.Clamp(value, 0, (int)EmulationAudioOutput.Headset);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.ForwardAudioOutput == output)
            {
                return;
            }

            _log.Info($"Setting forwarded audio output of {CurrentMac} to {output}");
            settings.ForwardAudioOutput = output;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(ForwardAudioOutputIndex));
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOutput(device, output == EmulationAudioOutput.Headset);
            }
        }
    }

    /// <summary>
    /// The speaker volume applied to the physical controller when forwarding host
    /// audio (0-255, two-way, persisted). Mirrors the audio player tester's range.
    /// </summary>
    public int ForwardVolume
    {
        get
        {
            if (!HasDevice)
            {
                return 0;
            }
            return GetEmulationSettings().ForwardVolume;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, 255);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.ForwardVolume == clamped)
            {
                return;
            }
            settings.ForwardVolume = clamped;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardVolume));
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOptions(device, (byte)clamped, settings.ForwardHapticStrength / 100f);
            }
        }
    }

    /// <summary>
    /// The haptic vibration strength when forwarding host audio, as a percentage
    /// (0-200, two-way, persisted). Mirrors the audio player tester's range.
    /// </summary>
    public int ForwardHapticStrength
    {
        get
        {
            if (!HasDevice)
            {
                return 0;
            }
            return GetEmulationSettings().ForwardHapticStrength;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, 200);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.ForwardHapticStrength == clamped)
            {
                return;
            }
            settings.ForwardHapticStrength = clamped;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardHapticStrength));
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOptions(device, (byte)settings.ForwardVolume, clamped / 100f);
            }
        }
    }

    /// <summary>
    /// Human-readable description of the selected controller's virtual controller
    /// emulation state, reflecting <see cref="IEmulationService.GetStatus"/>.
    /// </summary>
    public string EmulationStatusText
    {
        get
        {
            if (CurrentDualSenseDevice is not { } device)
            {
                return string.Empty;
            }

            EmulationStatus status = _emulation.GetStatus(device);
            if (status.IsCreating)
            {
                return LocalizationService.GetText("DeviceInfoPage.Emulation.Status.Creating");
            }
            if (!status.Running)
            {
                return status.Detail ?? LocalizationService.GetText("DeviceInfoPage.Emulation.Status.Idle");
            }

            if (status.Variant == DualSenseVariant.Edge)
            {
                return LocalizationService.GetText("DeviceInfoPage.Emulation.Status.RunningEdge");
            }

            string mode = EmulationModes[Math.Clamp((int)status.Mode, 0, EmulationModes.Count - 1)];
            return LocalizationService.GetText("DeviceInfoPage.Emulation.Status.Running").Replace("{mode}", mode);
        }
    }

    /// <summary>
    /// Whether the emulation mode and DualSense variant dropdowns may be changed for the
    /// selected controller. False while its virtual controller is being (re)created:
    /// switching mid-creation races the removal/creation cycle and can leave multiple
    /// virtual devices behind.
    /// </summary>
    public bool CanChangeEmulation => CurrentDualSenseDevice is not { } device || !_emulation.GetStatus(device).IsCreating;

    // ── Controller hiding ───────────────────────────────────────

    /// <summary>
    /// Platform backend for hiding physical controllers from other applications
    /// (HidHide driver on Windows, other backends on other platforms).
    /// </summary>
    private readonly IControllerHidingService _hiding;

    /// <summary>
    /// Whether the hiding backend is installed and operational on this system.
    /// </summary>
    public bool HidingAvailable { get; private set; }

    /// <summary>
    /// Whether the selected controller is hidden from other applications. Setting it
    /// hides or unhides the controller, managing the backend's global hiding state.
    /// </summary>
    public bool IsControllerHidden
    {
        get
        {
            if (!HidingAvailable || !TryGetCurrentInstanceId(out string instanceId))
            {
                return false;
            }

            return _hiding.IsControllerHidden(instanceId);
        }
        set
        {
            if (!HidingAvailable || !TryGetCurrentInstanceId(out string instanceId))
            {
                return;
            }

            _hiding.SetControllerHidden(instanceId, value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether controller hiding is offered at all, i.e. the platform supports it.
    /// </summary>
    public bool IsHidingVisible => OperatingSystem.IsWindows();

    /// <summary>
    /// Card description for the hide toggle; replaced with an explanation when the
    /// hiding backend is missing on a supported platform.
    /// </summary>
    public string HidingDescription
    {
        get
        {
            if (!HidingAvailable)
            {
                return LocalizationService.GetText("DeviceInfoPage.Hiding.Description.NotInstalled");
            }

            return LocalizationService.GetText("DeviceInfoPage.Hiding.HideController.Description");
        }
    }

    /// <summary>
    /// Whether the selected controller can be hidden, i.e. the driver is available and
    /// its HID path can be resolved to a device instance ID.
    /// </summary>
    public bool CanHideController => HidingAvailable && TryGetCurrentInstanceId(out _);

    /// <summary>
    /// Tries to resolve the selected controller's HID device path to the device
    /// instance ID used by HidHide.
    /// </summary>
    private bool TryGetCurrentInstanceId(out string instanceId)
    {
        instanceId = string.Empty;
        if (CurrentDevice is null || !_hiding.TryGetInstanceId(CurrentDevicePath, out string id))
        {
            _log.Debug($"Could not resolve instance ID from device path '{CurrentDevicePath}'");
            return false;
        }

        instanceId = id;
        return true;
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public DeviceInfoPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _illustrationService = App.Services.GetRequiredService<ControllerIllustrationService>();
        _emulation = App.Services.GetRequiredService<IEmulationService>();
        _hiding = App.Services.GetRequiredService<IControllerHidingService>();
        foreach (string skin in _illustrationService.GetSkins())
        {
            Skins.Add(skin);
        }
        _emulation.StateChanged += OnEmulationStateChanged;
        _emulationSaveTimer = new DispatcherTimer { Interval = EmulationSaveDebounce };
        _emulationSaveTimer.Tick += (_, _) => SaveEmulationDebounced();
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        UpdateDevice();
    }

    /// <summary>
    /// Re-reads the selected controller's firmware info report and refreshes the display.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        ControllerItem? selected = _mainViewModel.SelectedItem;
        if (selected is null)
        {
            return;
        }

        selected.FirmwareInfo = FeatureReader.ReadFirmwareInfo(selected.Device);
        if (selected.FirmwareInfo is null)
        {
            _log.Warning("Firmware info refresh returned no data");
        }

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
    /// Refreshes the emulation status line when the emulation service state changes.
    /// May be raised on a background thread; notifying the UI from it is safe here
    /// because Avalonia marshals property changes for bindings.
    /// </summary>
    private void OnEmulationStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EmulationStatusText));
        OnPropertyChanged(nameof(CanChangeEmulation));
    }

    /// <summary>
    /// Rebuilds <see cref="CurrentDevice"/> from the shell's selected controller.
    /// Releases the previous item's event subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();
        _previousMonitorState?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        _previousItem = CurrentDevice;
        MonitorState = selected is not null ? new InputMonitorItem(selected) : null;
        _previousMonitorState = MonitorState;
        _controllerName = selected is null
            ? string.Empty
            : _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, selected.DisplayName);

        string storedSkin = selected is null ? string.Empty : _controllerService.GetSkin(CurrentMac, CurrentDevicePath) ?? string.Empty;
        int storedIndex = Skins.IndexOf(storedSkin);
        _skinIndex = storedIndex >= 0 ? storedIndex : 0;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(MonitorState));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(ControllerName));
        OnPropertyChanged(nameof(SkinIndex));
        OnPropertyChanged(nameof(SkinName));
        OnPropertyChanged(nameof(EmulationModeIndex));
        OnPropertyChanged(nameof(IsDualSenseEmulation));
        OnPropertyChanged(nameof(IsAudioEmulation));
        OnPropertyChanged(nameof(DualSenseVariantIndex));
        OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        OnPropertyChanged(nameof(ForwardVolume));
        OnPropertyChanged(nameof(ForwardHapticStrength));
        OnPropertyChanged(nameof(EmulationStatusText));
        OnPropertyChanged(nameof(CanChangeEmulation));

        HidingAvailable = _hiding.IsAvailable;
        OnPropertyChanged(nameof(HidingAvailable));
        OnPropertyChanged(nameof(IsControllerHidden));
        OnPropertyChanged(nameof(CanHideController));
        OnPropertyChanged(nameof(HidingDescription));
        OnPropertyChanged(nameof(IsHidingVisible));
    }

    /// <summary>
    /// Restarts the debounce timer so the pending emulation settings save is delayed
    /// until slider changes stop, avoiding a disk write per drag step.
    /// </summary>
    private void ScheduleEmulationSave()
    {
        _emulationSaveTimer.Stop();
        _emulationSaveTimer.Start();
    }

    /// <summary>
    /// Flushes the debounced emulation settings save to disk once the debounce period
    /// elapses.
    /// </summary>
    private void SaveEmulationDebounced()
    {
        _emulationSaveTimer.Stop();
        if (!HasDevice)
        {
            return;
        }
        _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, GetEmulationSettings());
    }
}