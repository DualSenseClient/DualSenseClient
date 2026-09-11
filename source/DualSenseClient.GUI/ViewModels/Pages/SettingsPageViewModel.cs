using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Core.Models;
using DualSenseClient.Core.Utilities;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the Settings page. Exposes theme, language, and log level controls
/// that persist changes to the settings file and apply them at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Each setting change triggers an immediate side effect: theme changes swap the active
/// resource dictionary via <see cref="ThemeService"/>, language changes reload the localization
/// overlay via <see cref="LocalizationService"/>, and log level changes update the global
/// minimum via <see cref="DualSenseClientLogger.SetLogLevel"/>.
/// </para>
/// <para>
/// A <see cref="_suppressUpdates"/> flag prevents recursive property-change callbacks when
/// reloading settings during page navigation. Without this, setting <see cref="SelectedLanguageIndex"/>
/// would trigger <see cref="OnSelectedLanguageIndexChanged"/>, which would re-save the same value.
/// </para>
/// </remarks>
public partial class SettingsPageViewModel : ObservableObject
{
    /// <summary>
    /// Service used to read and persist application settings.
    /// </summary>
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Service used to apply theme changes at runtime.
    /// </summary>
    private readonly ThemeService _themeService;

    /// <summary>
    /// Popup service used to preview notifications.
    /// </summary>
    private readonly INotificationPopupService _popups;

    /// <summary>
    /// Dialog service used to confirm the restart before applying an update.
    /// </summary>
    private readonly IMessageBoxService _messageBox;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SettingsPage");

    /// <summary>
    /// When true, property-change callbacks are suppressed to prevent recursive updates
    /// during settings reload. Set by <see cref="RefreshSettings"/>.
    /// </summary>
    private bool _suppressUpdates;

    // ─────────────────────────────────────────────────────────────── Language
    /// <summary>
    /// Available languages for the language dropdown, populated from
    /// <see cref="LocalizationService.GetSupportedLanguages"/>.
    /// </summary>
    public ObservableCollection<LanguageItem> AppLanguages { get; set; } = [];

    /// <summary>
    /// Application version string including the build commit, shown in the page header.
    /// </summary>
    public string ApplicationVersion
    {
        get
        {
            return AppInfo.VersionWithCommit;
        }
    }

    /// <summary>
    /// Index of the currently selected language in <see cref="AppLanguages"/>.
    /// When changed, applies the new language via <see cref="LocalizationService.LoadLanguage"/>
    /// and persists the choice to settings.
    /// </summary>
    [ObservableProperty] private int selectedLanguageIndex;

    /// <summary>
    /// Called after <see cref="SelectedLanguageIndex"/> changes. Applies the new language
    /// to the running application and saves the selection to persistent storage.
    /// </summary>
    partial void OnSelectedLanguageIndexChanged(int oldValue, int newValue)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (newValue < 0 || newValue >= AppLanguages.Count || newValue == oldValue)
        {
            return;
        }

        _log.Info($"Language changed to '{AppLanguages[newValue].Culture.Name}'");

        LocalizationService.LoadLanguage(AppLanguages[newValue].Culture.Name);
        _settingsService.Settings.Ui.Language = AppLanguages[newValue].Culture.Name;
        _settingsService.SaveSettings();
    }

    // ─────────────────────────────────────────────────────────────── Theme
    /// <summary>
    /// Available themes for the theme dropdown, populated from
    /// <see cref="ThemeService.ThemeDisplayItems"/> with localized display names.
    /// </summary>
    public ObservableCollection<ThemeDisplayItem> AppThemeOptions { get; set; } = [];

    /// <summary>
    /// The currently selected <see cref="Theme"/> enum value.
    /// Kept in sync with <see cref="SelectedThemeIndex"/> and persisted to settings.
    /// </summary>
    [ObservableProperty] private Theme selectedTheme;

    /// <summary>
    /// Called after <see cref="SelectedTheme"/> changes. Raises
    /// <see cref="ObservableObject.PropertyChanged"/> for <see cref="SelectedThemeIndex"/>
    /// to keep the ComboBox index in sync with the enum value.
    /// </summary>
    partial void OnSelectedThemeChanged(Theme oldValue, Theme newValue)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (oldValue == newValue)
        {
            return;
        }

        _log.Info($"Theme changed from '{oldValue}' to '{newValue}'");
        OnPropertyChanged(nameof(SelectedThemeIndex));
    }

    /// <summary>
    /// Gets or sets the index of the currently selected theme in <see cref="AppThemeOptions"/>.
    /// Setting this property applies the theme via <see cref="ThemeService.SetTheme"/> and
    /// persists the choice to settings.
    /// </summary>
    /// <remarks>
    /// This property bridges the ComboBox's <c>SelectedIndex</c> binding with the
    /// <see cref="SelectedTheme"/> enum value. The getter searches <see cref="AppThemeOptions"/>
    /// for a matching <see cref="ThemeDisplayItem.ThemeValue"/>, and the setter resolves
    /// the index back to a <see cref="Theme"/> before applying.
    /// </remarks>
    public int SelectedThemeIndex
    {
        get
        {
            for (int i = 0; i < AppThemeOptions.Count; i++)
            {
                if (AppThemeOptions[i].ThemeValue == SelectedTheme)
                {
                    return i;
                }
            }

            return 0;
        }
        set
        {
            if (value < 0 || value >= AppThemeOptions.Count)
            {
                return;
            }

            Theme newTheme = AppThemeOptions[value].ThemeValue;
            if (SelectedTheme == newTheme)
            {
                return;
            }

            SelectedTheme = newTheme;
            _settingsService.Settings.Ui.Theme = newTheme;
            _settingsService.SaveSettings();
            _themeService.SetTheme(newTheme);
        }
    }

    // ─────────────────────────────────────────────────────────────── Log Level
    /// <summary>
    /// All available log levels for the log level dropdown, ordered from least to most severe.
    /// </summary>
    public ObservableCollection<LogLevel> LogLevels { get; set; } =
    [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Info,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical,
        LogLevel.None
    ];

    /// <summary>
    /// Gets or sets the index of the currently selected log level in <see cref="LogLevels"/>.
    /// Setting this property updates the global <see cref="DualSenseClientLogger.MinimumLevel"/>
    /// and persists the choice to settings.
    /// </summary>
    /// <remarks>
    /// The getter scans <see cref="LogLevels"/> for a match against the stored setting value.
    /// On mismatch (e.g., corrupted settings), defaults to index 2 (<see cref="LogLevel.Info"/>).
    /// </remarks>
    public int SelectedLogLevelIndex
    {
        get
        {
            for (int i = 0; i < LogLevels.Count; i++)
            {
                if (LogLevels[i] == _settingsService.Settings.Debug.LogLevel)
                {
                    return i;
                }
            }

            return 2; // Default to Info
        }
        set
        {
            if (value < 0 || value >= LogLevels.Count)
            {
                return;
            }

            LogLevel newLevel = LogLevels[value];
            if (newLevel == _settingsService.Settings.Debug.LogLevel)
            {
                return;
            }

            _log.Info($"Log level changed to '{newLevel}'");

            _settingsService.Settings.Debug.LogLevel = newLevel;
            _settingsService.SaveSettings();
            DualSenseClientLogger.SetLogLevel(newLevel);

            OnPropertyChanged();
        }
    }

    // ─────────────────────────────────────────────────────────────── Tray
    /// <summary>
    /// Whether closing the main window hides it to the system tray instead of exiting.
    /// Persisted to <see cref="Sections.UiSettings.CloseToTray"/>.
    /// </summary>
    [ObservableProperty] private bool closeToTray;

    /// <summary>
    /// Called after <see cref="CloseToTray"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnCloseToTrayChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Close to tray changed to '{newValue}'");
        _settingsService.Settings.Ui.CloseToTray = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether the application starts with its main window hidden in the system tray.
    /// Persisted to <see cref="Sections.UiSettings.StartInTray"/>.
    /// </summary>
    [ObservableProperty] private bool startInTray;

    /// <summary>
    /// Called after <see cref="StartInTray"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnStartInTrayChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Start in tray changed to '{newValue}'");
        _settingsService.Settings.Ui.StartInTray = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether the tray icon shows the selected controller's battery percentage.
    /// Persisted to <see cref="Sections.UiSettings.ShowBatteryPercentage"/>.
    /// </summary>
    [ObservableProperty] private bool showBatteryPercentage;

    /// <summary>
    /// Called after <see cref="ShowBatteryPercentage"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnShowBatteryPercentageChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Show battery percentage changed to '{newValue}'");
        _settingsService.Settings.Ui.ShowBatteryPercentage = newValue;
        _settingsService.SaveSettings();
    }

    // ─────────────────────────────────────────────────────────────── Notifications
    /// <summary>
    /// Whether desktop notification popups are shown at all.
    /// Persisted to <see cref="Sections.NotificationSettings.Enabled"/>.
    /// </summary>
    [ObservableProperty] private bool notifications;

    /// <summary>
    /// Called after <see cref="Notifications"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnNotificationsChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Notifications changed to '{newValue}'");
        _settingsService.Settings.Ui.Notifications.Enabled = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether a popup is shown when a controller connects or disconnects.
    /// Persisted to <see cref="Sections.NotificationSettings.NotifyOnConnection"/>.
    /// </summary>
    [ObservableProperty] private bool notifyOnConnection;

    /// <summary>
    /// Called after <see cref="NotifyOnConnection"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnNotifyOnConnectionChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Notify on connection changed to '{newValue}'");
        _settingsService.Settings.Ui.Notifications.NotifyOnConnection = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether a popup is shown when a controller's battery runs low.
    /// Persisted to <see cref="Sections.NotificationSettings.NotifyOnLowBattery"/>.
    /// </summary>
    [ObservableProperty] private bool notifyOnLowBattery;

    /// <summary>
    /// Called after <see cref="NotifyOnLowBattery"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnNotifyOnLowBatteryChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Notify on low battery changed to '{newValue}'");
        _settingsService.Settings.Ui.Notifications.NotifyOnLowBattery = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether a popup is shown when a charging controller reaches its target.
    /// Persisted to <see cref="Sections.NotificationSettings.NotifyOnCharge"/>.
    /// </summary>
    [ObservableProperty] private bool notifyOnCharge;

    /// <summary>
    /// Called after <see cref="NotifyOnCharge"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnNotifyOnChargeChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Notify on charge changed to '{newValue}'");
        _settingsService.Settings.Ui.Notifications.NotifyOnCharge = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Battery percentage triggering the low battery popup.
    /// Persisted to <see cref="Sections.NotificationSettings.LowBatteryThreshold"/>.
    /// </summary>
    [ObservableProperty] private double lowBatteryThreshold;

    /// <summary>
    /// Called after <see cref="LowBatteryThreshold"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnLowBatteryThresholdChanged(double oldValue, double newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        int threshold = (int)Math.Round(newValue);
        _log.Info($"Low battery threshold changed to '{threshold}'");
        _settingsService.Settings.Ui.Notifications.LowBatteryThreshold = threshold;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Battery percentage triggering the charge popup while charging.
    /// Persisted to <see cref="Sections.NotificationSettings.ChargeThreshold"/>.
    /// </summary>
    [ObservableProperty] private double chargeThreshold;

    /// <summary>
    /// Called after <see cref="ChargeThreshold"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnChargeThresholdChanged(double oldValue, double newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        int threshold = (int)Math.Round(newValue);
        _log.Info($"Charge threshold changed to '{threshold}'");
        _settingsService.Settings.Ui.Notifications.ChargeThreshold = threshold;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Notification positions in dropdown order, matching
    /// <see cref="NotificationPositionOptions"/>.
    /// </summary>
    private static readonly NotificationPosition[] PositionOrder = Enum.GetValues<NotificationPosition>();

    /// <summary>
    /// Localized display names for <see cref="PositionOrder"/>, shown in the
    /// position dropdown. Rebuilt in <see cref="LoadSettings"/>.
    /// </summary>
    public ObservableCollection<string> NotificationPositionOptions { get; set; } = [];

    /// <summary>
    /// Index into <see cref="NotificationPositionOptions"/> for the current
    /// <see cref="Sections.NotificationSettings.NotificationPosition"/>.
    /// When changed, persists the choice to settings.
    /// </summary>
    [ObservableProperty] private int selectedNotificationPositionIndex;

    /// <summary>
    /// Called after <see cref="SelectedNotificationPositionIndex"/> changes.
    /// Persists the choice to settings.
    /// </summary>
    partial void OnSelectedNotificationPositionIndexChanged(int oldValue, int newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        if (newValue < 0 || newValue >= PositionOrder.Length)
        {
            return;
        }

        NotificationPosition position = PositionOrder[newValue];
        _log.Info($"Notification position changed to '{position}'");
        _settingsService.Settings.Ui.Notifications.Position = position;
        _settingsService.SaveSettings();
    }

    // ─────────────────────────────────────────────────────────────── Updates
    /// <summary>
    /// Whether the app checks GitHub for updates once per calendar day at startup.
    /// Persisted to <see cref="Sections.UpdateSettings.AutoCheckForUpdates"/>.
    /// </summary>
    [ObservableProperty] private bool autoCheckForUpdates;

    /// <summary>
    /// Called after <see cref="AutoCheckForUpdates"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnAutoCheckForUpdatesChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Auto check for updates changed to '{newValue}'");
        _settingsService.Settings.Update.AutomaticCheck = newValue;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Whether update checks target nightly (pre-release) builds instead of stable releases.
    /// Off means stable, on means nightly. Persisted to <see cref="Sections.UpdateSettings.IncludeNightlyUpdates"/>.
    /// </summary>
    [ObservableProperty] private bool includeNightlyUpdates;

    /// <summary>
    /// Called after <see cref="IncludeNightlyUpdates"/> changes. Persists the choice,
    /// refreshes <see cref="ChannelLabel"/>, and drops the in-memory update from the other channel.
    /// </summary>
    partial void OnIncludeNightlyUpdatesChanged(bool oldValue, bool newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        _log.Info($"Update channel changed to '{(newValue ? "nightly" : "stable")}'");
        _settingsService.Settings.Update.NightlyVersion = newValue;
        _settingsService.SaveSettings();
        OnPropertyChanged(nameof(ChannelLabel));
        ClearPendingUpdate();
    }

    /// <summary>
    /// The channel toggle title: Stable when off, Nightly when on.
    /// </summary>
    public string ChannelLabel
    {
        get
        {
            return LocalizationService.GetText(IncludeNightlyUpdates
                ? "SettingsPage.Updates.Channel.Nightly"
                : "SettingsPage.Updates.Channel.Stable");
        }
    }

    /// <summary>
    /// Whether the latest check found a newer release. While true the manual check button
    /// is replaced by the release button, which always points at the latest release found.
    /// In-memory only; a fresh check overwrites it.
    /// </summary>
    [ObservableProperty] private bool hasUpdate;

    /// <summary>
    /// The latest release found. In-memory only; a fresh check overwrites it.
    /// </summary>
    private UpdateChecker.ReleaseInfo? _pendingRelease;

    /// <summary>
    /// Whether a manual check is currently running. Disables the check button.
    /// </summary>
    [ObservableProperty] private bool isChecking;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsPageViewModel"/>.
    /// Resolves dependencies from the DI container and loads current settings into UI state.
    /// </summary>
    public SettingsPageViewModel()
    {
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _popups = App.Services.GetRequiredService<INotificationPopupService>();
        _messageBox = App.Services.GetRequiredService<IMessageBoxService>();
        _suppressUpdates = true;
        try
        {
            LoadSettings();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    /// <summary>
    /// Reloads all UI state from the current settings file.
    /// Suppresses property-change callbacks during reload to prevent recursive saves.
    /// </summary>
    /// <remarks>
    /// Called on first construction and on every page navigation via
    /// <see cref="Views.Pages.SettingsPage.OnLoaded"/>. The first call is a no-op for
    /// suppression since the constructor already called <see cref="LoadSettings"/>.
    /// </remarks>
    public void RefreshSettings()
    {
        _suppressUpdates = true;
        try
        {
            LoadSettings();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    /// <summary>
    /// Populates all UI collections and selection indices from the current settings.
    /// Rebuilds the language and theme option lists, then sets the selected values.
    /// </summary>
    private void LoadSettings()
    {
        // Languages
        CultureInfo[] supportedCultures = LocalizationService.GetSupportedLanguages();
        List<LanguageItem> languageItems = supportedCultures.Select(c => new LanguageItem(c)).ToList();

        if (AppLanguages.Count == 0 || AppLanguages.Count != languageItems.Count)
        {
            AppLanguages = new ObservableCollection<LanguageItem>(languageItems);
            OnPropertyChanged(nameof(AppLanguages));
        }
        else
        {
            AppLanguages.Clear();
            foreach (LanguageItem item in languageItems)
            {
                AppLanguages.Add(item);
            }
        }

        string storedLanguage = _settingsService.Settings.Ui.Language;
        SelectedLanguageIndex = AppLanguages.ToList().FindIndex(c => c.Culture.Name == storedLanguage);
        if (SelectedLanguageIndex == -1)
        {
            LanguageItem? defaultItem = AppLanguages.FirstOrDefault(c => c.Culture.Name == "en") ?? AppLanguages.FirstOrDefault();
            if (defaultItem != null)
            {
                SelectedLanguageIndex = AppLanguages.IndexOf(defaultItem);
            }
        }

        // Theme options (localized display names from ThemeService)
        ReadOnlyObservableCollection<ThemeDisplayItem> themeItems = _themeService.ThemeDisplayItems;
        if (AppThemeOptions.Count != themeItems.Count)
        {
            AppThemeOptions = new ObservableCollection<ThemeDisplayItem>(themeItems);
            OnPropertyChanged(nameof(AppThemeOptions));
        }
        else
        {
            AppThemeOptions.Clear();
            foreach (ThemeDisplayItem item in themeItems)
            {
                AppThemeOptions.Add(item);
            }
        }

        SelectedTheme = _settingsService.Settings.Ui.Theme;

        // Log level
        OnPropertyChanged(nameof(SelectedLogLevelIndex));

        // Tray behavior
        CloseToTray = _settingsService.Settings.Ui.CloseToTray;
        StartInTray = _settingsService.Settings.Ui.StartInTray;
        ShowBatteryPercentage = _settingsService.Settings.Ui.ShowBatteryPercentage;

        // Notifications
        Notifications = _settingsService.Settings.Ui.Notifications.Enabled;
        NotifyOnConnection = _settingsService.Settings.Ui.Notifications.NotifyOnConnection;
        NotifyOnLowBattery = _settingsService.Settings.Ui.Notifications.NotifyOnLowBattery;
        NotifyOnCharge = _settingsService.Settings.Ui.Notifications.NotifyOnCharge;
        LowBatteryThreshold = _settingsService.Settings.Ui.Notifications.LowBatteryThreshold;
        ChargeThreshold = _settingsService.Settings.Ui.Notifications.ChargeThreshold;

        // Notification position options (localized display names in enum order)
        List<string> positionNames = PositionOrder.Select(p => LocalizationService.GetText($"SettingsPage.Notifications.Position.{p}")).ToList();
        if (NotificationPositionOptions.Count == 0 || NotificationPositionOptions.Count != positionNames.Count)
        {
            NotificationPositionOptions = new ObservableCollection<string>(positionNames);
            OnPropertyChanged(nameof(NotificationPositionOptions));
        }
        else
        {
            NotificationPositionOptions.Clear();
            foreach (string name in positionNames)
            {
                NotificationPositionOptions.Add(name);
            }
        }

        int positionIndex = Array.IndexOf(PositionOrder, _settingsService.Settings.Ui.Notifications.Position);
        SelectedNotificationPositionIndex = positionIndex >= 0 ? positionIndex : Array.IndexOf(PositionOrder, NotificationPosition.BottomRight);
        NotificationDurationSeconds = _settingsService.Settings.Ui.Notifications.Duration;

        // Updates
        AutoCheckForUpdates = _settingsService.Settings.Update.AutomaticCheck;
        IncludeNightlyUpdates = _settingsService.Settings.Update.NightlyVersion;
        OnPropertyChanged(nameof(ChannelLabel));
    }

    /// <summary>
    /// Shows a sample notification popup so the user can preview the current
    /// position and styling.
    /// </summary>
    [RelayCommand]
    private void TestNotification() =>
        _popups.ShowMessage(LocalizationService.GetText("Notification.Test.Title"), LocalizationService.GetText("Notification.Test.Message"));

    /// <summary>
    /// How long in seconds each notification popup stays visible.
    /// Persisted to <see cref="Sections.NotificationSettings.NotificationDurationSeconds"/>.
    /// </summary>
    [ObservableProperty] private double notificationDurationSeconds;

    /// <summary>
    /// Called after <see cref="NotificationDurationSeconds"/> changes. Persists the choice to settings.
    /// </summary>
    partial void OnNotificationDurationSecondsChanged(double oldValue, double newValue)
    {
        if (_suppressUpdates || oldValue == newValue)
        {
            return;
        }

        int seconds = (int)Math.Round(newValue);
        _log.Info($"Notification duration changed to '{seconds}'");
        _settingsService.Settings.Ui.Notifications.Duration = seconds;
        _settingsService.SaveSettings();
    }

    /// <summary>
    /// Records the latest release found by a check (called by manual and splash checks).
    /// </summary>
    public void SetPendingUpdate(UpdateChecker.ReleaseInfo release)
    {
        _pendingRelease = release;
        HasUpdate = true;
    }

    /// <summary>
    /// Drops the in-memory update, restoring the manual check button.
    /// </summary>
    private void ClearPendingUpdate()
    {
        HasUpdate = false;
        _pendingRelease = null;
    }

    /// <summary>
    /// Checks GitHub for updates now and shows a clickable popup when one is found.
    /// Stamps the daily check date like the splash check does.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            UpdateChecker.ReleaseInfo? latest = await UpdateChecker.GetLatestAsync(IncludeNightlyUpdates, cts.Token);
            UpdateSettings updates = _settingsService.Settings.Update;
            updates.LastUpdateCheck = DateOnly.FromDateTime(DateTime.Today);
            _settingsService.SaveSettings();
            if (latest is not null
                && UpdateChecker.IsNewer(latest.Tag, AppInfo.Version, AppInfo.CommitShaShort, IncludeNightlyUpdates))
            {
                SetPendingUpdate(latest);
                _popups.ShowMessage(
                    LocalizationService.GetText("Notification.Update.Title"),
                    string.Format(LocalizationService.GetText("Notification.Update.Message"), latest.Tag),
                    url: latest.Url);
            }
            else
            {
                ClearPendingUpdate();
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Downloads the pending update, swaps it over the running executable, and restarts.
    /// Falls back to the release page for read-only installs or missing assets.
    /// </summary>
    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (IsChecking || _pendingRelease is null)
        {
            return;
        }

        UpdateChecker.ReleaseInfo release = _pendingRelease;
        string? exePath = Environment.ProcessPath;
        UpdateTarget target = UpdateInstaller.Detect(exePath);
        string? assetUrl = release.GetAssetUrl(target);
        string? directory = string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(assetUrl)
                                          || string.IsNullOrEmpty(directory) || !PathResolver.IsWritable(directory))
        {
            NotificationPopupService.OpenUrl(release.Url);
            return;
        }

        IsChecking = true;
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            string tempDir = Directory.CreateTempSubdirectory("DualSenseClient-update-").FullName;
            try
            {
                UpdateAsset updateAsset = UpdateInstaller.Target(target);
                string downloadPath = Path.Combine(tempDir, updateAsset.FileName);
                await DownloadWithProgressAsync(release.Tag, assetUrl, downloadPath, cts.Token);
                if (!await UpdateInstaller.VerifyHashAsync(downloadPath, assetUrl, token: cts.Token))
                {
                    _log.Warning($"Update {release.Tag} failed hash verification");
                    await _messageBox.ShowErrorAsync(
                        LocalizationService.GetText("Notification.Update.Failed.Title"),
                        LocalizationService.GetText("Notification.Update.Failed.Message"));
                    return;
                }

                string newFile = downloadPath;
                if (updateAsset.Entry is not null)
                {
                    newFile = Path.Combine(tempDir, updateAsset.Entry);
                    UpdateInstaller.ExtractEntry(downloadPath, updateAsset.Entry, newFile);
                }

                if (updateAsset.MakeExecutable)
                {
                    UpdateInstaller.MakeExecutable(newFile);
                }

                UpdateInstaller.SwapExecutable(newFile, exePath);
                _settingsService.Settings.Update.PendingChangelog = true;
                _settingsService.SaveSettings();
                _log.Info($"Update {release.Tag} installed, asking to restart");
                await _messageBox.ShowInfoAsync(
                    LocalizationService.GetText("Notification.Update.Restart.Title"),
                    string.Format(LocalizationService.GetText("Notification.Update.Restart.Message"), release.Tag));

                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });
                App.IsExiting = true;
                App.Desktop?.Shutdown();
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _log.Error($"Could not delete update temp directory '{tempDir}'");
                    _log.LogExceptionDetails(ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Update {release.Tag} failed");
            _log.LogExceptionDetails(ex);
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("Notification.Update.Failed.Title"),
                LocalizationService.GetText("Notification.Update.Failed.Message"));
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Downloads the update asset behind a non-closable progress dialog.
    /// The dialog covers the download only; verification and install stay silent.
    /// </summary>
    private static async Task DownloadWithProgressAsync(string tag, string assetUrl, string downloadPath, CancellationToken token)
    {
        string messageFormat = LocalizationService.GetText("Notification.Update.Downloading.Message");
        TextBlock statusText = new TextBlock
        {
            Text = string.Format(messageFormat, tag, 0)
        };
        ProgressBar progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };
        FAContentDialog dialog = new FAContentDialog
        {
            Title = LocalizationService.GetText("Notification.Update.Downloading.Title"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    statusText,
                    progressBar
                }
            }
        };

        bool downloading = true;
        dialog.Closing += (_, e) =>
        {
            if (downloading)
            {
                e.Cancel = true;
            }
        };

        Exception? downloadError = null;
        dialog.Opened += async (_, _) =>
        {
            try
            {
                Progress<double> progress = new Progress<double>(p =>
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = p;
                    statusText.Text = string.Format(messageFormat, tag, p);
                });
                await UpdateInstaller.DownloadAsync(assetUrl, downloadPath, progress, token);
            }
            catch (Exception ex)
            {
                downloadError = ex;
            }
            finally
            {
                downloading = false;
                dialog.Hide();
            }
        };

        await dialog.ShowAsync();
        if (downloadError is not null)
        {
            ExceptionDispatchInfo.Capture(downloadError).Throw();
        }
    }
}