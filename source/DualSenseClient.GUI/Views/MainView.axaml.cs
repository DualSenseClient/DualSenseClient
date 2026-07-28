using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Services;

namespace DualSenseClient.GUI.Views;

/// <summary>
/// Shell view containing the <see cref="FANavigationView"/> and content frame for page navigation.
/// </summary>
public partial class MainView : UserControl
{
    private NavigationService _navigationService { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainView"/> class.
    /// Resolves <see cref="NavigationService"/> from the DI container, wires it to the UI controls,
    /// and navigates to the default page.
    /// </summary>
    public MainView()
    {
        InitializeComponent();

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