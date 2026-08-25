using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Core.Models;
using DualSenseClient.Core.Utilities;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using DualSenseClient.Settings;

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

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsPageViewModel"/>.
    /// Resolves dependencies from the DI container and loads current settings into UI state.
    /// </summary>
    public SettingsPageViewModel()
    {
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        LoadSettings();
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
        LoadSettings();
        _suppressUpdates = false;
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
    }
}