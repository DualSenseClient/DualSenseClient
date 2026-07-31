using Avalonia;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Controls;
using DualSenseClient.GUI.ViewModels;

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
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// Resolves <see cref="MainWindowViewModel"/> from the DI container, assigns the splash screen,
    /// and extends the window content into the title bar so <see cref="MainView"/> can host the custom chrome.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = _viewModel;
        SplashScreen = new AppSplashScreen();
        TitleBar.ExtendsContentIntoTitleBar = true;
    }
}