using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels;

namespace DualSenseClient.GUI.Views;

/// <summary>
/// Shell view containing the <see cref="FANavigationView"/> and content frame for page navigation.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>
    /// The navigation service that drives page navigation from the shell menu.
    /// Resolved from the DI container and wired to the navigation view and content frame.
    /// </summary>
    private NavigationService _navigationService { get; set; }

    /// <summary>
    /// The ViewModel providing the shell's binding context.
    /// Resolved from the DI container and assigned as the view's <see cref="StyledElement.DataContext"/>.
    /// </summary>
    private MainViewModel _viewModel { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// Resolves <see cref="NavigationService"/> and <see cref="MainViewModel"/> from the DI container,
    /// wires the navigation service to the UI controls, and navigates to the default page.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _navigationService.SetContentFrame(ContentFrame);
        _navigationService.SetNavigationView(NavigationView);

    }

    /// <summary>
    /// Handles navigation item invocations by delegating to <see cref="NavigationService.Navigate"/>.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">Event arguments containing the invoked navigation item.</param>
    private async void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is FANavigationViewItem selectedItem)
        {
            await _navigationService.Navigate(selectedItem, ContentFrame);
        }
    }
}