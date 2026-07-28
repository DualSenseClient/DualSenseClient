using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Settings page providing UI controls for theme, language, and log level configuration.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="SettingsPageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. All settings changes are applied immediately at
/// runtime and persisted to the settings file.
/// </para>
/// <para>
/// <see cref="OnLoaded"/> calls <see cref="SettingsPageViewModel.RefreshSettings"/> to
/// synchronize the UI with the current settings values each time the page is navigated to,
/// preventing stale state from previous visits.
/// </para>
/// </remarks>
public partial class SettingsPage : UserControl
{
    /// <summary>
    /// The ViewModel driving the settings page controls and persistence logic.
    /// </summary>
    private readonly SettingsPageViewModel _viewModel;

    /// <summary>
    /// Initializes the settings page, resolving the ViewModel from DI.
    /// </summary>
    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<SettingsPageViewModel>();
        DataContext = _viewModel;
    }

    /// <summary>
    /// Refreshes the settings UI to reflect current values when the page is loaded.
    /// Prevents stale state when navigating away and back to this page.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _viewModel.RefreshSettings();
    }
}