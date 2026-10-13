using Avalonia.Controls;
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
    /// Initializes the device info page, resolving the ViewModel from DI.
    /// </summary>
    public DeviceInfoPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DeviceInfoPageViewModel>();
    }
}