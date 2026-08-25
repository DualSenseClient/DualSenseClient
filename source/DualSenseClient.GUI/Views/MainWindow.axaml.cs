using System;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Controls;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.Settings;

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
    private void OnSettingsChanged(object? sender, EventArgs e) => _closeToTray = _settingsService.Settings.Ui.CloseToTray;

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