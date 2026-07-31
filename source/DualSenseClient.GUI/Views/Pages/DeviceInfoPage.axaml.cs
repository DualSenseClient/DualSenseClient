using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Device info page displaying firmware and hardware information for the
/// controller selected in the title bar combobox.
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
    /// The ViewModel driving the device info display.
    /// </summary>
    private readonly DeviceInfoPageViewModel _viewModel;

    /// <summary>
    /// Initializes the device info page, resolving the ViewModel from DI.
    /// </summary>
    public DeviceInfoPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<DeviceInfoPageViewModel>();
        DataContext = _viewModel;
    }
}