using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Core.Models;
using DualSenseClient.Core.Utilities;
using DualSenseClient.GUI.Controls;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Views;

/// <summary>
/// The main application window, hosting <see cref="MainView"/> as its content.
/// </summary>
public partial class MainWindow : FAAppWindow
{
    /// <summary>
    /// The ViewModel providing the main window's title and binding context.
    /// Resolved from the DI container and assigned as the window's <see cref="StyledElement.DataContext"/>.
    /// </summary>
    private MainWindowViewModel _viewModel { get; set; }

    /// <summary>
    /// The settings service used to determine whether closing the window should
    /// hide it to the tray instead of exiting the application.
    /// </summary>
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Whether closing the window hides it to the system tray instead of exiting.
    /// Kept in sync with <see cref="UiSettings.CloseToTray"/> via <see cref="SettingsService.SettingsChanged"/>.
    /// </summary>
    private bool _closeToTray;

    /// <summary>
    /// Whether the main shell content was already created. The splash screen's
    /// preloading is the only caller, so this only guards against double loads.
    /// </summary>
    private bool _mainContentLoaded;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("MainWindow");

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// Resolves <see cref="MainWindowViewModel"/> and <see cref="SettingsService"/> from the DI container,
    /// assigns the splash screen, and extends the window content into the title bar so
    /// <see cref="MainView"/> can host the custom chrome.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainWindowViewModel>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        DataContext = _viewModel;
        SplashScreen = new AppSplashScreen();
        TitleBar.ExtendsContentIntoTitleBar = true;
        _closeToTray = _settingsService.Settings.Ui.CloseToTray;
        _settingsService.SettingsChanged += OnSettingsChanged;
        Closing += OnClosing;
        Opened += OnFirstOpened;
        ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    /// <summary>
    /// Enables the Mica backdrop on Windows 11 and re-applies it when the theme variant
    /// changes (high contrast needs an opaque window again). Other platforms keep the
    /// solid background.
    /// </summary>
    private void OnThemeVariantChanged(object? sender, EventArgs e) => ApplyBackdrop();

    /// <summary>
    /// Applies the window backdrop for the current platform, theme, and setting: Mica on
    /// Windows 11 when enabled and the selected theme is one that supports it (System,
    /// Light, Dark; the AMOLED and PlayStation themes define their own solid backgrounds).
    /// </summary>
    private void ApplyBackdrop()
    {
        Theme theme = _settingsService.Settings.Ui.Theme;
        bool micaAllowed = theme is DualSenseClient.Core.Models.Theme.System
            or DualSenseClient.Core.Models.Theme.Light
            or DualSenseClient.Core.Models.Theme.Dark;
        if (!_settingsService.Settings.Ui.Mica || !micaAllowed
                                               || !IsWindows11 || ActualThemeVariant == FluentAvaloniaTheme.HighContrastTheme)
        {
            ClearValue(BackgroundProperty);
            ClearValue(TransparencyBackgroundFallbackProperty);
            return;
        }

        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Mica];
    }

    /// <summary>
    /// Shows the what's-new popup on first opening after an in-app update.
    /// Never throws; failures are skipped so startup is unaffected.
    /// </summary>
    private async void OnFirstOpened(object? sender, EventArgs e)
    {
        try
        {
            Opened -= OnFirstOpened;
            ApplyBackdrop();
            await ShowWhatsNewAsync();
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to show what's-new popup");
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Shows the aggregated changelog after an in-app update, covering the entries
    /// newer than the last seen version. The changelog is only ever fetched here:
    /// plain version mismatches (manual installs, downgrades) just re-stamp silently.
    /// </summary>
    private async Task ShowWhatsNewAsync()
    {
        try
        {
            UpdateSettings updates = _settingsService.Settings.Update;
            string current = AppInfo.VersionWithCommit;
            bool pending = updates.PendingChangelog;
            string seen = updates.LastSeenVersion;
            if (!pending && seen == current)
            {
                return;
            }

            updates.PendingChangelog = false;
            updates.LastSeenVersion = current;
            _settingsService.SaveSettings();
            if (!pending || string.IsNullOrWhiteSpace(seen))
            {
                return;
            }

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IReadOnlyList<Changelog.Entry>? releases = await Changelog.GetChangesSinceAsync(seen, updates.NightlyVersion, cts.Token);
            if (releases is null || releases.Count == 0)
            {
                return;
            }

            await App.Services.GetRequiredService<IMessageBoxService>()
                .ShowChangelogAsync(LocalizationService.GetText("Notification.Changelog.Title"), releases);
        }
        catch (Exception ex)
        {
            _log.Warning("Skipping what's-new popup");
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Creates the <see cref="MainView"/> shell inside the content placeholder.
    /// Called from the splash screen's preloading so the shell (and the page it
    /// navigates to) is built behind the splash screen instead of before it opens,
    /// which previously delayed the window's first appearance.
    /// </summary>
    internal void LoadMainContent()
    {
        if (_mainContentLoaded)
        {
            return;
        }

        _mainContentLoaded = true;
        MainContent.Content = new MainView();
    }

    /// <summary>
    /// Keeps <see cref="_closeToTray"/> in sync with the "close to tray" setting.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _closeToTray = _settingsService.Settings.Ui.CloseToTray;
        ApplyBackdrop();
    }

    /// <summary>
    /// Hides the window to the tray instead of closing it when the "close to tray" setting
    /// is enabled, unless the application is exiting via the tray menu's Exit item.
    /// When the setting is disabled, closing the window proceeds and exits the application.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (App.IsExiting || !_closeToTray)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}