using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Profile page providing controls to customize the controller selected in the title
/// bar combobox. Currently hosts the lights section: lightbar color, microphone LED
/// mode, and player LEDs.
/// </summary>
/// <remarks>
/// Resolves <see cref="ProfilePageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created
/// on each navigation.
/// </remarks>
public partial class ProfilePage : UserControl
{
    /// <summary>
    /// The ViewModel driving the profile display.
    /// </summary>
    private readonly ProfilePageViewModel _viewModel;

    /// <summary>
    /// Initializes the profile page, resolving the ViewModel from DI.
    /// </summary>
    public ProfilePage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<ProfilePageViewModel>();
        DataContext = _viewModel;
    }
}