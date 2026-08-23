using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Controls;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER.DualSense;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the virtual controller page. Hosts the emulation settings moved off the
/// device info page (mode, variants, audio forwarding, status) plus the per-mode button
/// remapping editor backed by <see cref="ButtonMappingTable"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MainViewModel.SelectedItem"/> like the other page ViewModels so the
/// page always edits the controller active in the shell. Remapping rules are stored per
/// controller in the emulation settings section and applied live through
/// <see cref="IEmulationService.ApplyButtonMappings"/> without recreating the device.
/// </remarks>
public partial class VirtualControllerPageViewModel : ObservableObject
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("VirtualControllerPage");

    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Service storing persistent controller info, including the per-controller emulation
    /// settings edited on this page.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Service providing the embedded controller illustration skins.
    /// </summary>
    private readonly ControllerIllustrationService _illustrationService;

    /// <summary>
    /// Service creating the selected controller's virtual controller and applying its
    /// emulation settings.
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
    /// Tracks the previous monitor state so its event subscriptions are released on replacement.
    /// </summary>
    private InputMonitorItem? _previousMonitorState;

    /// <summary>
    /// The buttons currently multi-selected on the controller illustration (the pending
    /// combo whose target the user is about to assign).
    /// </summary>
    private readonly HashSet<ButtonType> _selectedButtons = new HashSet<ButtonType>();

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
    /// Whether a controller is selected and its info can be displayed.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// Whether the selected controller is a DualSense Edge, the only model with function
    /// keys and back paddles.
    /// </summary>
    public bool IsEdgeController => CurrentDevice?.Controller.Device is DualSenseEdgeDevice;

    private string CurrentMac => CurrentDevice?.Controller.PairingInfo?.ClientMac ?? string.Empty;

    private string CurrentDevicePath => CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;

    /// <summary>
    /// The concrete DualSense device of the selected controller, or <c>null</c> for
    /// non-DualSense devices.
    /// </summary>
    private DualSenseDevice? CurrentDualSenseDevice => CurrentDevice?.Controller.Device as DualSenseDevice;

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
            _skinIndex = value;
            _controllerService.SetSkin(CurrentMac, CurrentDevicePath, skin);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SkinName));
        }
    }

    // ── Emulation settings (moved from the device info page) ───

    /// <summary>
    /// Virtual controller emulation mode options for the dropdown, in
    /// <see cref="EmulationMode"/> order (off, Xbox 360, DualShock 4, DualSense).
    /// </summary>
    public ObservableCollection<string> EmulationModes { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.Off"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.Xbox360"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.DualShock4"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.DualSense")
    ];

    /// <summary>
    /// DualSense hardware variant options for the dropdown, in
    /// <see cref="DualSenseVariant"/> order (standard, Edge).
    /// </summary>
    public ObservableCollection<string> DualSenseVariants { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.DeviceType.Standard"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.DeviceType.Edge")
    ];

    /// <summary>
    /// DualShock 4 hardware generation options for the dropdown, in
    /// <see cref="DualShock4Variant"/> order (V1, V2).
    /// </summary>
    public ObservableCollection<string> DualShock4Variants { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.Ds4Variant.V1"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Ds4Variant.V2")
    ];

    /// <summary>
    /// Forwarded audio output options for the dropdown, in
    /// <see cref="EmulationAudioOutput"/> order (speaker, headset).
    /// </summary>
    public ObservableCollection<string> EmulationAudioOutputs { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioOutput.Speaker"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioOutput.Headset")
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
    /// selected controller. Setting it persists the change immediately, recreates the
    /// virtual controller through <see cref="IEmulationService"/>, and refreshes the
    /// remapping editor for the new mode's target set.
    /// </summary>
    public int EmulationModeIndex
    {
        get
        {
            if (!HasDevice || !EmulationService.IsSupported)
            {
                return 0;
            }

            return (int)GetEmulationSettings().Mode;
        }
        set
        {
            if (!HasDevice || !EmulationService.IsSupported)
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
            OnPropertyChanged(nameof(IsEmulationEnabled));
            OnPropertyChanged(nameof(IsDualSenseEmulation));
            OnPropertyChanged(nameof(IsDualShock4Emulation));
            OnPropertyChanged(nameof(IsAudioEmulation));
            OnPropertyChanged(nameof(IsMappingEditorVisible));
            NotifyTargetPickerState();
            RefreshMappingTargets();
            RefreshBindings();
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// Whether the selected controller's emulation mode is DualSense, the mode with a
    /// DualSense hardware variant.
    /// </summary>
    public bool IsDualSenseEmulation => EmulationModeIndex == (int)EmulationMode.DualSense;

    /// <summary>
    /// Whether the selected controller's emulation mode is DualShock 4, the mode with a
    /// DualShock 4 hardware generation variant.
    /// </summary>
    public bool IsDualShock4Emulation => EmulationModeIndex == (int)EmulationMode.DualShock4;

    /// <summary>
    /// Whether the selected controller's emulation mode forwards host audio to the
    /// physical controller (DualSense or DualShock 4).
    /// </summary>
    public bool IsAudioEmulation => EmulationModeIndex is (int)EmulationMode.DualSense or (int)EmulationMode.DualShock4;

    /// <summary>
    /// The last non-off emulation mode used on this page, restored when the toggle is
    /// switched back on.
    /// </summary>
    private EmulationMode _lastEnabledMode;

    /// <summary>
    /// Whether a virtual controller is created for the selected controller. Turning it on
    /// restores the last used mode (Xbox 360 when none was used yet); turning it off
    /// switches to <see cref="EmulationMode.Off"/>.
    /// </summary>
    public bool IsEmulationEnabled
    {
        get => EmulationModeIndex != (int)EmulationMode.Off;
        set
        {
            if (!HasDevice || !EmulationService.IsSupported)
            {
                OnPropertyChanged();
                return;
            }

            if (value)
            {
                EmulationModeIndex = (int)(_lastEnabledMode == EmulationMode.Off ? EmulationMode.Xbox360 : _lastEnabledMode);
            }
            else
            {
                _lastEnabledMode = (EmulationMode)EmulationModeIndex;
                EmulationModeIndex = (int)EmulationMode.Off;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The DualSense hardware variant (<see cref="DualSenseVariant"/> value) of the
    /// selected controller's virtual device. Setting it persists the change immediately
    /// and recreates the virtual controller through <see cref="IEmulationService"/>.
    /// </summary>
    public int DualSenseVariantIndex
    {
        get => !HasDevice ? 0 : (int)GetEmulationSettings().DeviceType;
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
            RefreshMappingTargets();
            RefreshBindings();
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// The DualShock 4 hardware generation (<see cref="DualShock4Variant"/> value) of the
    /// selected controller's virtual device. Setting it persists the change immediately
    /// and recreates the virtual controller through <see cref="IEmulationService"/>.
    /// </summary>
    public int DualShock4VariantIndex
    {
        get => !HasDevice ? 0 : (int)GetEmulationSettings().Ds4Variant;
        set
        {
            if (!HasDevice)
            {
                return;
            }

            DualShock4Variant variant = (DualShock4Variant)Math.Clamp(value, 0, (int)DualShock4Variant.V2);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Ds4Variant == variant)
            {
                return;
            }

            _log.Info($"Setting DualShock 4 variant of {CurrentMac} to {variant}");
            settings.Ds4Variant = variant;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(DualShock4VariantIndex));
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// The physical controller output (<see cref="EmulationAudioOutput"/> value) used
    /// when forwarding host audio. Setting it persists the change immediately and
    /// applies it to the active forwarder without recreating the virtual controller.
    /// </summary>
    public int ForwardAudioOutputIndex
    {
        get => !HasDevice ? 0 : (int)GetEmulationSettings().ForwardAudioOutput;
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
        get => !HasDevice ? 0 : GetEmulationSettings().ForwardVolume;
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
        get => !HasDevice ? 0 : GetEmulationSettings().ForwardHapticStrength;
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
                return LocalizationService.GetText("VirtualControllerPage.Emulation.Status.Creating");
            }

            if (!status.Running)
            {
                return status.Detail ?? LocalizationService.GetText("VirtualControllerPage.Emulation.Status.Idle");
            }

            if (status.Variant == DualSenseVariant.Edge)
            {
                return LocalizationService.GetText("VirtualControllerPage.Emulation.Status.RunningEdge");
            }

            string mode = EmulationModes[Math.Clamp((int)status.Mode, 0, EmulationModes.Count - 1)];
            return LocalizationService.GetText("VirtualControllerPage.Emulation.Status.Running").Replace("{mode}", mode);
        }
    }

    /// <summary>
    /// Whether the emulation controls may be changed for the selected controller. False
    /// while its virtual controller is being (re)created. Always false on platforms
    /// without emulation support.
    /// </summary>
    public bool CanChangeEmulation
        => EmulationService.IsSupported
           && (CurrentDualSenseDevice is not { } device || !_emulation.GetStatus(device).IsCreating);

    // ── Button remapping ────────────────────────────────────────

    /// <summary>
    /// All mappable source buttons in <see cref="ButtonType"/> declaration order.
    /// </summary>
    public static IReadOnlyList<ButtonType> SourceButtons => ButtonMappingTable.Sources;

    /// <summary>
    /// The buttons currently selected on the illustration, exposed as a fresh list
    /// instance so the view's highlight refreshes on every change.
    /// </summary>
    public IReadOnlyList<ButtonType> SelectedButtonTypes => _selectedButtons.ToList();

    /// <summary>
    /// Human-readable summary of the pending selection, e.g. "Create + Options".
    /// </summary>
    public string SelectionSummary
        => _selectedButtons.Count == 0 ? string.Empty : string.Join(" + ", _selectedButtons.Select(GetSourceDisplayName));

    /// <summary>
    /// Whether at least one button is selected, enabling the assign controls.
    /// </summary>
    public bool HasSelection => _selectedButtons.Count > 0;

    /// <summary>
    /// Whether the pending selection is a combo (more than one key), which unlocks the
    /// solo-suppression option.
    /// </summary>
    public bool IsComboSelection => _selectedButtons.Count > 1;

    /// <summary>
    /// Whether the remapping editor is offered: a virtual controller mode other than Off
    /// is selected and changes are currently allowed.
    /// </summary>
    public bool IsMappingEditorVisible => EmulationModeIndex != (int)EmulationMode.Off && CanChangeEmulation;

    /// <summary>
    /// Display strings of the assignable targets for the current emulation mode
    /// (including the trailing "None" entry).
    /// </summary>
    public ObservableCollection<string> TargetOptions { get; } = [];

    /// <summary>
    /// Backing field for <see cref="TargetIndex"/>.
    /// </summary>
    private int _targetIndex;

    /// <summary>
    /// Raw name of the single selected button's effective solo target (custom or default),
    /// or <c>null</c> when nothing/no default applies. Used to detect no-op assignments.
    /// </summary>
    private string? _effectiveSoloTarget;

    /// <summary>
    /// The selected entry in <see cref="TargetOptions"/>.
    /// </summary>
    public int TargetIndex
    {
        get => _targetIndex;
        set
        {
            if (value < -1 || value >= TargetOptions.Count)
            {
                return;
            }

            _targetIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTriggerOutputVisible));
            OnPropertyChanged(nameof(CanAssignMapping));
            OnPropertyChanged(nameof(SelectedTargetName));
            OnPropertyChanged(nameof(IsNoneTargetSelected));
        }
    }

    /// <summary>
    /// Which virtual controller illustration the target picker renders for the current
    /// emulation mode.
    /// </summary>
    public VirtualControllerKind TargetViewKind => EmulationModeIndex == (int)EmulationMode.Xbox360
        ? VirtualControllerKind.Xbox360
        : VirtualControllerKind.DualShock4;

    /// <summary>
    /// Whether the clickable target controller illustration is offered: Xbox 360 and
    /// DualShock 4 modes have illustrations; DualSense mode falls back to the dropdown.
    /// </summary>
    public bool IsTargetPickerVisible
        => IsMappingEditorVisible && EmulationModeIndex is (int)EmulationMode.Xbox360 or (int)EmulationMode.DualShock4;

    /// <summary>
    /// Whether the plain target dropdown is shown instead of the illustration.
    /// </summary>
    public bool IsTargetComboVisible => IsMappingEditorVisible && !IsTargetPickerVisible;

    /// <summary>
    /// Raises change notifications for everything derived from the emulation mode's target
    /// picking surface: which editor is shown and which illustration it renders.
    /// </summary>
    private void NotifyTargetPickerState()
    {
        OnPropertyChanged(nameof(IsTargetPickerVisible));
        OnPropertyChanged(nameof(IsTargetComboVisible));
        OnPropertyChanged(nameof(TargetViewKind));
    }

    /// <summary>
    /// The raw target name picked on the illustration, or <c>null</c> when nothing is
    /// chosen. Setting it selects the matching entry in <see cref="TargetOptions"/>; setting
    /// the already-selected name clears the choice, so clicking the same button on the
    /// illustration toggles the selection off.
    /// </summary>
    public string? SelectedTargetName
    {
        get => TryGetRawTarget(out string raw) ? raw : null;
        set
        {
            if (value is null)
            {
                return;
            }

            int index = IndexOfRawTarget(value);
            if (index < 0)
            {
                return;
            }

            TargetIndex = TryGetRawTarget(out string current) && current == value ? -1 : index;
        }
    }

    /// <summary>
    /// Whether "None" is currently the chosen target.
    /// </summary>
    public bool IsNoneTargetSelected => SelectedTargetName == "None";

    /// <summary>
    /// Selects "None" as the remapping target, disabling the pending source selection.
    /// </summary>
    [RelayCommand]
    private void SelectNone() => SelectedTargetName = "None";

    /// <summary>
    /// Output style options for trigger targets that have both a click flag and an
    /// analog byte (full pull, click only).
    /// </summary>
    public ObservableCollection<string> TriggerOutputs { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Mapping.Output.Full"),
        LocalizationService.GetText("VirtualControllerPage.Mapping.Output.Click")
    ];

    /// <summary>
    /// Backing field for <see cref="TriggerOutputIndex"/>.
    /// </summary>
    private int _triggerOutputIndex;

    /// <summary>
    /// The selected trigger output style while <see cref="IsTriggerOutputVisible"/>.
    /// </summary>
    public int TriggerOutputIndex
    {
        get => _triggerOutputIndex;
        set
        {
            if (value is not 0 and not 1)
            {
                return;
            }

            _triggerOutputIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the trigger output style choice applies to the current selection: it needs
    /// a trigger target on a device whose triggers have both a flag and an analog byte
    /// (DualShock 4 and DualSense; Xbox 360 triggers are byte-only).
    /// </summary>
    public bool IsTriggerOutputVisible
        => EmulationModeIndex is (int)EmulationMode.DualShock4 or (int)EmulationMode.DualSense
           && TryGetRawTarget(out string raw)
           && raw is "L2" or "R2";

    /// <summary>
    /// Backing field for <see cref="SuppressSolos"/>.
    /// </summary>
    private bool _suppressSolos = true;

    /// <summary>
    /// Whether an assigned combo mutes its member buttons' own outputs while held
    /// (the default). Ignored by single-button assignments.
    /// </summary>
    public bool SuppressSolos
    {
        get => _suppressSolos;
        set
        {
            if (_suppressSolos == value)
            {
                return;
            }

            _suppressSolos = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The stored remapping entries of the current emulation mode, displayed as rows.
    /// </summary>
    public ObservableCollection<ButtonBindingItem> Bindings { get; } = [];

    /// <summary>
    /// Whether any custom binding exists for the current mode (enables reset-to-defaults).
    /// </summary>
    public bool HasBindings => Bindings.Count > 0;

    /// <summary>
    /// Toggles a source button's membership in the pending selection (command wrapper for
    /// the Edge chip buttons).
    /// </summary>
    /// <param name="button">The chip's source button.</param>
    [RelayCommand]
    private void ToggleSelection(ButtonType button) => ToggleButton(button);

    /// <summary>
    /// Toggles a source button's membership in the pending selection. Called from the
    /// page code-behind when the user clicks a button on the illustration.
    /// </summary>
    public void ToggleButton(ButtonType button)
    {
        if (!_selectedButtons.Remove(button))
        {
            _selectedButtons.Add(button);
        }

        OnSelectionChanged();
    }

    /// <summary>
    /// Clears the pending selection.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        _selectedButtons.Clear();
        OnSelectionChanged();
    }

    /// <summary>
    /// Commits the pending selection as one mapping rule, replacing any existing rule
    /// with the same key combination, then saves and applies the rules live.
    /// </summary>
    [RelayCommand]
    private void AssignMapping()
    {
        if (!HasDevice || !TryGetRawTarget(out string target))
        {
            return;
        }

        EmulationSettings settings = GetEmulationSettings();
        List<ButtonMappingEntry> list = GetEntries(settings) ?? [];
        list.RemoveAll(entry => SameKeys(entry.Keys, _selectedButtons));
        list.Add(new ButtonMappingEntry
        {
            Keys = _selectedButtons.Select(button => button.ToString()).ToList(),
            Target = target,
            TargetOutput = IsTriggerOutputVisible && TriggerOutputIndex == 1 ? "click" : null,
            SuppressSolos = SuppressSolos
        });
        SetEntries(settings, list);

        _log.Info($"Assigned {_selectedButtons.Count} button(s) to '{target}' for {CurrentMac}");
        SaveAndApply(settings);
        RefreshBindings();
    }

    /// <summary>
    /// Removes one binding row and applies the remaining rules live.
    /// </summary>
    [RelayCommand]
    private void RemoveBinding(ButtonBindingItem item)
    {
        if (!HasDevice || item is null)
        {
            return;
        }

        EmulationSettings settings = GetEmulationSettings();
        List<ButtonMappingEntry>? list = GetEntries(settings);
        if (list is null || !list.Remove(item.Entry))
        {
            return;
        }

        SetEntries(settings, list);
        _log.Info($"Removed a button binding of {CurrentMac}");
        SaveAndApply(settings);
        RefreshBindings();
    }

    /// <summary>
    /// Restores the built-in default mapping for the current mode by clearing every
    /// custom entry.
    /// </summary>
    [RelayCommand]
    private void ResetMappings()
    {
        if (!HasDevice)
        {
            return;
        }

        EmulationSettings settings = GetEmulationSettings();
        SetEntries(settings, []);
        _log.Info($"Reset button mappings of {CurrentMac}");
        SaveAndApply(settings);
        RefreshBindings();
    }

    /// <summary>
    /// The stored mapping entries of the current emulation mode, or <c>null</c> when the
    /// section was never customized.
    /// </summary>
    private List<ButtonMappingEntry>? GetEntries(EmulationSettings settings) => EmulationModeIndex switch
    {
        (int)EmulationMode.Xbox360 => settings.Xbox360ButtonMappings,
        (int)EmulationMode.DualShock4 => settings.DualShock4ButtonMappings,
        (int)EmulationMode.DualSense => settings.DualSenseButtonMappings,
        _ => null
    };

    /// <summary>
    /// Stores the mapping entries into the current emulation mode's settings slot.
    /// </summary>
    private void SetEntries(EmulationSettings settings, List<ButtonMappingEntry> entries)
    {
        switch ((EmulationMode)EmulationModeIndex)
        {
            case EmulationMode.Xbox360:
                settings.Xbox360ButtonMappings = entries;
                break;
            case EmulationMode.DualShock4:
                settings.DualShock4ButtonMappings = entries;
                break;
            case EmulationMode.DualSense:
                settings.DualSenseButtonMappings = entries;
                break;
        }
    }

    /// <summary>
    /// Persists the emulation settings and pushes the resolved mapping table onto the
    /// running virtual controller without recreating it.
    /// </summary>
    private void SaveAndApply(EmulationSettings settings)
    {
        _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
        if (CurrentDualSenseDevice is { } device)
        {
            _emulation.ApplyButtonMappings(device);
        }
    }

    /// <summary>
    /// Resolves the raw target name behind the current dropdown selection, treating the
    /// trailing "None" entry specially.
    /// </summary>
    private bool TryGetRawTarget(out string raw)
    {
        raw = string.Empty;
        return TargetIndex >= 0 && TargetIndex < TargetOptions.Count
                                && CurrentTargets()[TargetIndex] is string name && (raw = name) != string.Empty;
    }

    /// <summary>
    /// The raw target names aligned with <see cref="TargetOptions"/> for the current mode.
    /// In DualSense mode the Edge-only targets (function keys and paddles) are offered only
    /// when the virtual device presents a DualSense Edge.
    /// </summary>
    private IReadOnlyList<string> CurrentTargets() => EmulationModeIndex switch
    {
        (int)EmulationMode.Xbox360 => Xbox360Targets,
        (int)EmulationMode.DualShock4 => DualShock4Targets,
        (int)EmulationMode.DualSense => IsEdgeVirtualController ? DualSenseTargets : DualSenseStandardTargets,
        _ => []
    };

    /// <summary>
    /// Whether the selected controller's virtual DualSense presents an Edge.
    /// </summary>
    private bool IsEdgeVirtualController
        => HasDevice && GetEmulationSettings().DeviceType == DualSenseVariant.Edge;

    /// <summary>
    /// Rebuilds <see cref="TargetOptions"/> for the current emulation mode and resets the
    /// target/output selections.
    /// </summary>
    private void RefreshMappingTargets()
    {
        TargetOptions.Clear();
        foreach ((_, string display) in CurrentTargets().Select(raw => (raw, GetTargetDisplayName(raw))))
        {
            TargetOptions.Add(display);
        }

        _targetIndex = -1;
        _effectiveSoloTarget = null;
        _triggerOutputIndex = 0;
        OnPropertyChanged(nameof(TargetOptions));
        OnPropertyChanged(nameof(TargetIndex));
        OnPropertyChanged(nameof(TriggerOutputIndex));
        OnPropertyChanged(nameof(IsTriggerOutputVisible));
        OnPropertyChanged(nameof(SelectedTargetName));
        OnPropertyChanged(nameof(IsNoneTargetSelected));
        OnPropertyChanged(nameof(CanAssignMapping));
    }

    /// <summary>
    /// Rebuilds the binding rows from the stored entries of the current mode.
    /// </summary>
    private void RefreshBindings()
    {
        Bindings.Clear();
        if (HasDevice && GetEntries(GetEmulationSettings()) is { } entries)
        {
            foreach (ButtonMappingEntry entry in entries.Where(e => e.Keys is { Count: > 0 }))
            {
                Bindings.Add(new ButtonBindingItem(entry, this));
            }
        }

        OnPropertyChanged(nameof(HasBindings));
    }

    /// <summary>
    /// Notifies every binding-row consumer after selection changes.
    /// </summary>
    internal string DescribeKeys(IReadOnlyList<string> keys)
        => string.Join(" + ", keys.Select(key => ButtonMappingTable.TryParseSource(key, out ButtonType button) ? GetSourceDisplayName(button) : key));

    /// <summary>
    /// Refreshes everything derived from the pending selection.
    /// </summary>
    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedButtonTypes));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsComboSelection));
        UpdateTargetFromEffectiveSolo();
    }

    /// <summary>
    /// Preselects the target dropdown with the selected button's effective solo mapping:
    /// its custom binding when one exists, otherwise its built-in default. Combos start
    /// with an empty target because defaults are defined per single button only.
    /// </summary>
    private void UpdateTargetFromEffectiveSolo()
    {
        if (_selectedButtons.Count != 1 || !HasDevice)
        {
            _effectiveSoloTarget = null;
            _targetIndex = -1;
            OnPropertyChanged(nameof(TargetIndex));
            OnPropertyChanged(nameof(IsTriggerOutputVisible));
            OnPropertyChanged(nameof(CanAssignMapping));
            OnPropertyChanged(nameof(SelectedTargetName));
            OnPropertyChanged(nameof(IsNoneTargetSelected));
            return;
        }

        IEnumerable<ButtonMappingEntry>? entries = GetEntries(GetEmulationSettings());
        ResolvedMappingTarget? effective = EmulationModeIndex switch
        {
            (int)EmulationMode.Xbox360 => VirtualInputMapper.Xbox360Table(entries).GetSoloTarget(_selectedButtons.First()),
            (int)EmulationMode.DualShock4 => VirtualInputMapper.DualShock4Table(entries).GetSoloTarget(_selectedButtons.First()),
            (int)EmulationMode.DualSense => VirtualInputMapper.DualSenseTable(entries).GetSoloTarget(_selectedButtons.First()),
            _ => null
        };

        _effectiveSoloTarget = effective is { } target ? DescribeResolvedTarget(target) : null;
        _targetIndex = _effectiveSoloTarget is null ? -1 : IndexOfRawTarget(_effectiveSoloTarget);
        OnPropertyChanged(nameof(TargetIndex));
        OnPropertyChanged(nameof(IsTriggerOutputVisible));
        OnPropertyChanged(nameof(CanAssignMapping));
    }

    /// <summary>
    /// Whether Assign may commit the pending selection: a target must be chosen, and a
    /// single-button selection whose chosen target equals its current effective mapping
    /// (custom or default) is already in place and needs no entry.
    /// </summary>
    public bool CanAssignMapping
    {
        get
        {
            if (_selectedButtons.Count == 0 || _targetIndex < 0 || _targetIndex >= TargetOptions.Count)
            {
                return false;
            }

            if (_selectedButtons.Count == 1 && _effectiveSoloTarget is not null
                                            && IndexOfRawTarget(_effectiveSoloTarget) == _targetIndex)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Converts a resolved target back into its raw settings name so it can be matched
    /// against the mode's target list.
    /// </summary>
    private string? DescribeResolvedTarget(ResolvedMappingTarget target)
    {
        if (target.IsNone)
        {
            return "None";
        }

        if (target.Trigger == MappableTriggerSide.Left)
        {
            return EmulationModeIndex == (int)EmulationMode.Xbox360 ? "LeftTrigger" : "L2";
        }

        if (target.Trigger == MappableTriggerSide.Right)
        {
            return EmulationModeIndex == (int)EmulationMode.Xbox360 ? "RightTrigger" : "R2";
        }

        if (target.DPad != VirtualDPad.None)
        {
            return target.DPad switch
            {
                VirtualDPad.Up => "DPadUp",
                VirtualDPad.Down => "DPadDown",
                VirtualDPad.Left => "DPadLeft",
                VirtualDPad.Right => "DPadRight",
                _ => null
            };
        }

        return EmulationModeIndex switch
        {
            (int)EmulationMode.Xbox360 => Enum.GetName(typeof(Xbox360Buttons), (Xbox360Buttons)(uint)target.ButtonFlags),
            (int)EmulationMode.DualShock4 => Enum.GetName(typeof(DualShock4Buttons), (DualShock4Buttons)(ushort)target.ButtonFlags),
            (int)EmulationMode.DualSense => Enum.GetName(typeof(DualSenseButtons), (DualSenseButtons)(uint)target.ButtonFlags),
            _ => null
        };
    }

    /// <summary>
    /// Finds a raw name's index in the current mode's target list, or -1.
    /// </summary>
    private int IndexOfRawTarget(string raw)
    {
        IReadOnlyList<string> targets = CurrentTargets();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == raw)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Display label of a physical source button.
    /// </summary>
    internal static string GetSourceDisplayName(ButtonType button) => button switch
    {
        ButtonType.DPadUp => "D-Pad Up",
        ButtonType.DPadDown => "D-Pad Down",
        ButtonType.DPadLeft => "D-Pad Left",
        ButtonType.DPadRight => "D-Pad Right",
        ButtonType.PS => "PS",
        ButtonType.TouchPad => "Touchpad Click",
        ButtonType.Mute => "Mute",
        ButtonType.Edge_LeftFunction => "FnL",
        ButtonType.Edge_RightFunction => "FnR",
        ButtonType.Edge_LeftPaddle => "L4 (paddle)",
        ButtonType.Edge_RightPaddle => "R4 (paddle)",
        _ => button.ToString()
    };

    /// <summary>
    /// Display label of a raw target name.
    /// </summary>
    private static string GetTargetDisplayName(string raw) => raw switch
    {
        "LeftShoulder" => "LB",
        "RightShoulder" => "RB",
        "LeftTrigger" => "LT (analog)",
        "RightTrigger" => "RT (analog)",
        "LeftThumb" => "LS click",
        "RightThumb" => "RS click",
        "Share" => "Share",
        "MicMute" => "Mic Mute",
        "LeftFunction" => "FnL",
        "RightFunction" => "FnR",
        "L4" => "L4 (paddle)",
        "R4" => "R4 (paddle)",
        "DPadUp" => "D-Pad Up",
        "DPadDown" => "D-Pad Down",
        "DPadLeft" => "D-Pad Left",
        "DPadRight" => "D-Pad Right",
        "None" => LocalizationService.GetText("VirtualControllerPage.Mapping.Target.None"),
        _ => raw
    };

    /// <summary>
    /// Raw target names of the Xbox 360 mode (flag members plus the analog-trigger
    /// pseudo-targets and "None").
    /// </summary>
    private static readonly IReadOnlyList<string> Xbox360Targets =
    [
        "A", "B", "X", "Y", "LeftShoulder", "RightShoulder", "LeftTrigger", "RightTrigger",
        "LeftThumb", "RightThumb", "Back", "Start", "Guide", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "None"
    ];

    /// <summary>
    /// Raw target names of the DualShock 4 mode.
    /// </summary>
    private static readonly IReadOnlyList<string> DualShock4Targets =
    [
        "Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3",
        "Share", "Options", "PS", "Touchpad", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "None"
    ];

    /// <summary>
    /// Raw target names of the DualSense mode, including the Edge-only function keys and
    /// back paddles. Only offered when the virtual device presents an Edge.
    /// </summary>
    private static readonly IReadOnlyList<string> DualSenseTargets =
    [
        "Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3",
        "Create", "Options", "PS", "Touchpad", "MicMute", "LeftFunction", "RightFunction", "L4", "R4",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "None"
    ];

    /// <summary>
    /// Raw target names of a standard (non-Edge) virtual DualSense: the full list without
    /// the Edge-only function keys and paddles.
    /// </summary>
    private static readonly IReadOnlyList<string> DualSenseStandardTargets =
    [
        "Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2", "L3", "R3",
        "Create", "Options", "PS", "Touchpad", "MicMute",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "None"
    ];

    /// <summary>
    /// Whether two mapping entries select exactly the same source keys.
    /// </summary>
    private static bool SameKeys(IEnumerable<string> keys, IEnumerable<ButtonType> selection)
        => new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase)
            .SetEquals(selection.Select(button => button.ToString()));

    // ── Lifecycle ───────────────────────────────────────────────

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public VirtualControllerPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _illustrationService = App.Services.GetRequiredService<ControllerIllustrationService>();
        _emulation = App.Services.GetRequiredService<IEmulationService>();
        foreach (string skin in _illustrationService.GetSkins())
        {
            Skins.Add(skin);
        }

        _emulation.StateChanged += OnEmulationStateChanged;
        _emulationSaveTimer = new DispatcherTimer
        {
            Interval = EmulationSaveDebounce
        };
        _emulationSaveTimer.Tick += (_, _) => SaveEmulationDebounced();
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
    /// Refreshes the status line when the emulation service state changes. May be raised
    /// on a background thread; notifying the UI from it is safe here because Avalonia
    /// marshals property changes for bindings.
    /// </summary>
    private void OnEmulationStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EmulationStatusText));
        OnPropertyChanged(nameof(CanChangeEmulation));
        OnPropertyChanged(nameof(IsMappingEditorVisible));
        NotifyTargetPickerState();
    }

    /// <summary>
    /// Rebuilds the device context from the shell's selected controller.
    /// </summary>
    private void UpdateDevice()
    {
        _previousMonitorState?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        MonitorState = selected is not null ? new InputMonitorItem(selected) : null;
        _previousMonitorState = MonitorState;

        string storedSkin = selected is null ? string.Empty : _controllerService.GetSkin(CurrentMac, CurrentDevicePath) ?? string.Empty;
        int storedIndex = Skins.IndexOf(storedSkin);
        _skinIndex = storedIndex >= 0 ? storedIndex : 0;

        _selectedButtons.Clear();
        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(MonitorState));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(IsEdgeController));
        OnPropertyChanged(nameof(SkinIndex));
        OnPropertyChanged(nameof(SkinName));
        OnSelectionChanged();
        RefreshMappingTargets();
        RefreshBindings();

        if (EmulationModeIndex != (int)EmulationMode.Off)
        {
            _lastEnabledMode = (EmulationMode)EmulationModeIndex;
        }

        OnPropertyChanged(nameof(EmulationModeIndex));
        OnPropertyChanged(nameof(IsEmulationEnabled));
        OnPropertyChanged(nameof(IsDualSenseEmulation));
        OnPropertyChanged(nameof(IsDualShock4Emulation));
        OnPropertyChanged(nameof(IsAudioEmulation));
        OnPropertyChanged(nameof(IsMappingEditorVisible));
        NotifyTargetPickerState();
        OnPropertyChanged(nameof(DualSenseVariantIndex));
        OnPropertyChanged(nameof(DualShock4VariantIndex));
        OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        OnPropertyChanged(nameof(ForwardVolume));
        OnPropertyChanged(nameof(ForwardHapticStrength));
        OnPropertyChanged(nameof(EmulationStatusText));
        OnPropertyChanged(nameof(CanChangeEmulation));
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