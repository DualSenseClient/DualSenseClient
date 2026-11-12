using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Device info page showing the selected controller's illustration, live status, identification,
/// controller hiding, firmware information, and its assigned profile.
/// </summary>
/// <remarks>
/// Resolves <see cref="DeviceInfoPageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created
/// on each navigation.
/// </remarks>
public partial class DeviceInfoPage : UserControl
{
    /// <summary>
    /// The ViewModel driving the page.
    /// </summary>
    private readonly DeviceInfoPageViewModel _viewModel;

    /// <summary>
    /// The containing window, used to pause auto-refresh while minimized or hidden.
    /// </summary>
    private Window? _window;

    /// <summary>
    /// Initializes the device info page, resolving the ViewModel from DI.
    /// </summary>
    public DeviceInfoPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<DeviceInfoPageViewModel>();
        DataContext = _viewModel;
    }

    /// <summary>
    /// Allows the optional dongle bridge auto-refresh timer to run while the page is visible.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdatePageActive();
    }

    /// <summary>
    /// Stops the dongle bridge auto-refresh timer when the page is hidden.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }

        _viewModel.SetPageActive(false);
        base.OnUnloaded(e);
    }

    /// <summary>
    /// Pauses auto-refresh when the containing window is minimized or hidden.
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        {
            UpdatePageActive();
        }
    }

    /// <summary>
    /// Applies the current page/window visibility to the ViewModel timer.
    /// </summary>
    private void UpdatePageActive()
    {
        bool active = _window is null || (_window.IsVisible && _window.WindowState != WindowState.Minimized);
        _viewModel.SetPageActive(active);
    }
}