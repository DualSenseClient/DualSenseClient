using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the foreground-app auto profiles page. Lists the rules mapping a
/// focused program to a profile and emulation mode, and edits the selected rule.
/// Rules are stored by <see cref="AutoProfileService"/> and applied temporarily by
/// <see cref="AutoProfileCoordinator"/>; this page only edits and persists them.
/// </summary>
/// <remarks>
/// The rules collection holds the live service objects, so text edits apply to the
/// service state directly; every mutation path saves to disk. <see cref="Refresh"/>
/// is called from the page's <c>OnLoaded</c> to resynchronize on every navigation.
/// </remarks>
public partial class AutoProfilePageViewModel : ObservableObject
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AutoProfilesPage");

    /// <summary>
    /// Service storing the auto profile rules edited on this page.
    /// </summary>
    private readonly AutoProfileService _rules;

    /// <summary>
    /// Service providing the profile names offered for rules.
    /// </summary>
    private readonly ProfileService _profiles;

    /// <summary>
    /// Service providing the known controllers offered as rule targets.
    /// </summary>
    private readonly ControllerInfoService _controllers;

    /// <summary>
    /// Whether foreground-app switching is available on this platform (Windows only).
    /// </summary>
    public bool IsSupported
    {
        get
        {
            return OperatingSystem.IsWindows();
        }
    }

    /// <summary>
    /// Whether foreground-app switching is active. Setting it persists immediately.
    /// </summary>
    public bool Enabled
    {
        get
        {
            return _rules.Settings.Enabled;
        }
        set
        {
            if (_rules.Settings.Enabled == value)
            {
                return;
            }

            _rules.Settings.Enabled = value;
            _rules.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The auto profile rules in priority order (first match wins).
    /// </summary>
    public ObservableCollection<AutoProfileRule> Rules { get; } = [];

    /// <summary>
    /// The rule currently edited, or <c>null</c> when none is selected.
    /// Changing the selection persists pending text edits of the previous rule.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRule))]
    [NotifyPropertyChangedFor(nameof(IsRuleActionless))]
    [NotifyPropertyChangedFor(nameof(SelectedRuleName))]
    [NotifyPropertyChangedFor(nameof(SelectedExePattern))]
    [NotifyPropertyChangedFor(nameof(SelectedWindowTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileIndex))]
    [NotifyPropertyChangedFor(nameof(SelectedEmulationIndex))]
    [NotifyPropertyChangedFor(nameof(SelectedControllerIndex))]
    private AutoProfileRule? selectedRule;

    /// <summary>
    /// Called after <see cref="SelectedRule"/> changes. Persists pending text edits of
    /// the previously selected rule.
    /// </summary>
    partial void OnSelectedRuleChanged(AutoProfileRule? oldValue, AutoProfileRule? newValue) => _rules.Save();

    /// <summary>
    /// Whether a rule is selected and can be edited.
    /// </summary>
    public bool HasSelectedRule
    {
        get
        {
            return SelectedRule is not null;
        }
    }

    /// <summary>
    /// The selected rule's user-visible name. Setting it updates the rule;
    /// the change is persisted when the selection changes or another option is set.
    /// </summary>
    public string SelectedRuleName
    {
        get
        {
            return SelectedRule?.Name ?? string.Empty;
        }
        set
        {
            if (SelectedRule is null || SelectedRule.Name == value)
            {
                return;
            }

            SelectedRule.Name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The selected rule's executable path pattern. Setting it updates the rule;
    /// the change is persisted when the selection changes or another option is set.
    /// </summary>
    public string SelectedExePattern
    {
        get
        {
            return SelectedRule?.ExePattern ?? string.Empty;
        }
        set
        {
            if (SelectedRule is null || SelectedRule.ExePattern == value)
            {
                return;
            }

            SelectedRule.ExePattern = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The selected rule's window title pattern. Setting it updates the rule;
    /// the change is persisted when the selection changes or another option is set.
    /// </summary>
    public string SelectedWindowTitle
    {
        get
        {
            return SelectedRule?.WindowTitle ?? string.Empty;
        }
        set
        {
            if (SelectedRule is null || SelectedRule.WindowTitle == value)
            {
                return;
            }

            SelectedRule.WindowTitle = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the selected rule leaves both profile and emulation unchanged,
    /// making it a no-op. Shown as a hint in the editor.
    /// </summary>
    public bool IsRuleActionless
    {
        get
        {
            return SelectedRule is not null
                   && string.IsNullOrEmpty(SelectedRule.ProfileName)
                   && SelectedRule.EmulationMode is null;
        }
    }

    /// <summary>
    /// Profile options for the selected rule: "leave unchanged" plus every saved profile.
    /// </summary>
    public ObservableCollection<string> ProfileOptions { get; } = [];

    /// <summary>
    /// The selected rule's profile as an option index (0 leaves the profile unchanged).
    /// Setting it persists immediately.
    /// </summary>
    public int SelectedProfileIndex
    {
        get
        {
            if (SelectedRule is null || string.IsNullOrEmpty(SelectedRule.ProfileName))
            {
                return 0;
            }

            int index = ProfileOptions.IndexOf(SelectedRule.ProfileName);
            return index >= 0 ? index : 0;
        }
        set
        {
            if (SelectedRule is null || value < 0 || value >= ProfileOptions.Count)
            {
                return;
            }

            SelectedRule.ProfileName = value == 0 ? string.Empty : ProfileOptions[value];
            _rules.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRuleActionless));
        }
    }

    /// <summary>
    /// Emulation mode options for the selected rule: "leave unchanged", off, and every mode.
    /// </summary>
    public ObservableCollection<string> EmulationOptions { get; } =
    [
        LocalizationService.GetText("AutoProfilesPage.Emulation.Unchanged"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.Off"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.Xbox360"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.DualShock4"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.Mode.DualSense")
    ];

    /// <summary>
    /// The selected rule's emulation mode as an option index (0 leaves the mode unchanged).
    /// Setting it persists immediately.
    /// </summary>
    public int SelectedEmulationIndex
    {
        get
        {
            return SelectedRule?.EmulationMode switch
            {
                null => 0,
                EmulationMode.Off => 1,
                EmulationMode.Xbox360 => 2,
                EmulationMode.DualShock4 => 3,
                EmulationMode.DualSense => 4,
                _ => 0
            };
        }
        set
        {
            if (SelectedRule is null)
            {
                return;
            }

            EmulationMode? mode = value switch
            {
                1 => EmulationMode.Off,
                2 => EmulationMode.Xbox360,
                3 => EmulationMode.DualShock4,
                4 => EmulationMode.DualSense,
                _ => null
            };
            if (SelectedRule.EmulationMode == mode)
            {
                return;
            }

            SelectedRule.EmulationMode = mode;
            _rules.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRuleActionless));
        }
    }

    /// <summary>
    /// Controller target options for the selected rule: "all controllers" plus every
    /// known controller.
    /// </summary>
    public ObservableCollection<string> ControllerOptions { get; } = [];

    /// <summary>
    /// The known controllers behind <see cref="ControllerOptions"/> (index 0 has no entry).
    /// </summary>
    private readonly List<ControllerInfo> _controllerEntries = [];

    /// <summary>
    /// The selected rule's controller target as an option index (0 targets every controller).
    /// Setting it persists immediately.
    /// </summary>
    public int SelectedControllerIndex
    {
        get
        {
            if (SelectedRule is null || SelectedRule.AppliesToAllControllers)
            {
                return 0;
            }

            for (int i = 0; i < _controllerEntries.Count; i++)
            {
                ControllerInfo entry = _controllerEntries[i];
                if (!string.IsNullOrEmpty(SelectedRule.ControllerMac)
                    && string.Equals(entry.MacAddress?.Trim().ToUpperInvariant(),
                        SelectedRule.ControllerMac.Trim().ToUpperInvariant(), StringComparison.Ordinal))
                {
                    return i + 1;
                }

                if (!string.IsNullOrEmpty(SelectedRule.ControllerPath)
                    && string.Equals(entry.DevicePath?.Trim(), SelectedRule.ControllerPath.Trim(), StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }

            return 0;
        }
        set
        {
            if (SelectedRule is null || value < 0 || value > _controllerEntries.Count)
            {
                return;
            }

            if (value == 0)
            {
                SelectedRule.ControllerMac = string.Empty;
                SelectedRule.ControllerPath = string.Empty;
            }
            else
            {
                ControllerInfo entry = _controllerEntries[value - 1];
                SelectedRule.ControllerMac = entry.MacAddress ?? string.Empty;
                SelectedRule.ControllerPath = entry.DevicePath ?? string.Empty;
            }

            _rules.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Creates the page ViewModel and loads the current rules.
    /// </summary>
    public AutoProfilePageViewModel()
    {
        _rules = App.Services.GetRequiredService<AutoProfileService>();
        _profiles = App.Services.GetRequiredService<ProfileService>();
        _controllers = App.Services.GetRequiredService<ControllerInfoService>();
        Refresh();
    }

    /// <summary>
    /// Reloads the rules, profile names, and controller targets from the services.
    /// Reloading replaces the rule objects, so the selection restarts at the first rule.
    /// </summary>
    public void Refresh()
    {
        _rules.Load();
        AutoProfileRule? selected = SelectedRule;

        Rules.Clear();
        foreach (AutoProfileRule rule in _rules.Settings.Rules)
        {
            Rules.Add(rule);
        }

        List<string> profiles = [LocalizationService.GetText("AutoProfilesPage.Profile.Unchanged")];
        profiles.AddRange(_profiles.Settings.Profiles.Select(profile => profile.Name));
        SyncOptions(ProfileOptions, profiles);

        _controllerEntries.Clear();
        _controllerEntries.AddRange(_controllers.Settings.Controllers);
        List<string> controllers = [LocalizationService.GetText("AutoProfilesPage.Controller.All")];
        controllers.AddRange(_controllerEntries.Select(entry => !string.IsNullOrEmpty(entry.Name)
            ? entry.Name
            : !string.IsNullOrEmpty(entry.MacAddress)
                ? entry.MacAddress
                : entry.DevicePath));
        SyncOptions(ControllerOptions, controllers);

        SelectedRule = selected is not null && Rules.Contains(selected) ? selected : Rules.FirstOrDefault();
        OnPropertyChanged(nameof(Enabled));
    }

    /// <summary>
    /// Updates <paramref name="options"/> in place to match <paramref name="desired"/>,
    /// changing only entries that differ.
    /// </summary>
    /// <remarks>
    /// A bound ComboBox coerces a TwoWay <c>SelectedIndex</c> to -1 while its source is
    /// empty. The index setters here ignore out-of-range values without notifying, so a
    /// <c>Clear</c> + refill permanently blanks the selection (the follow-up same-value
    /// notification is dropped by the binding). Syncing in place avoids the transient
    /// empty state, keeping the selection alive.
    /// </remarks>
    private static void SyncOptions(ObservableCollection<string> options, List<string> desired)
    {
        int index = 0;
        while (index < desired.Count && index < options.Count)
        {
            if (!string.Equals(options[index], desired[index], StringComparison.Ordinal))
            {
                options[index] = desired[index];
            }

            index++;
        }

        while (index < desired.Count)
        {
            options.Add(desired[index]);
            index++;
        }

        while (options.Count > desired.Count)
        {
            options.RemoveAt(options.Count - 1);
        }
    }

    /// <summary>
    /// Persists pending text edits. Called when leaving the page; option changes,
    /// add/delete, and selection changes already save immediately.
    /// </summary>
    public void Save() => _rules.Save();

    /// <summary>
    /// Adds a blank rule (all controllers, profile and emulation unchanged) and selects it.
    /// The service instance is added to the list directly: reloading from disk here
    /// would replace its identity and break the selection.
    /// </summary>
    [RelayCommand]
    private void AddRule()
    {
        AutoProfileRule rule = new AutoProfileRule();
        _rules.AddRule(rule);
        _log.Info("Added auto profile rule");
        Rules.Add(rule);
        SelectedRule = rule;
    }

    /// <summary>
    /// Deletes the selected rule.
    /// </summary>
    [RelayCommand]
    private void DeleteRule()
    {
        AutoProfileRule? rule = SelectedRule;
        if (rule is null)
        {
            return;
        }

        _log.Info($"Deleted auto profile rule for '{rule.ExePattern}'");
        _rules.RemoveRule(rule);
        Refresh();
    }
}