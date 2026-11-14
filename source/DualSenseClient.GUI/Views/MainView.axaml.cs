using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.GUI.Views.Pages;

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

        // Foreground-app auto profiles need Windows focus tracking
        if (!OperatingSystem.IsWindows())
        {
            NavigationView.MenuItems.Remove(AutoProfilesNavItem);
        }

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _navigationService.SetContentFrame(ContentFrame);
        _navigationService.SetNavigationView(NavigationView);

        _ = _navigationService.NavigateToTag("DeviceInfoPage", ContentFrame);
    }

    /// <summary>
    /// Creates and caches every page behind the splash screen so the first navigation to
    /// each page does not pay the XAML construction and GC cost. Pages are not recorded
    /// in the frame's navigation stack and use no transition; the frame returns to the
    /// initial device info page when done.
    /// </summary>
    internal async Task PrewarmPagesAsync()
    {
        FAFrameNavigationOptions options = new FAFrameNavigationOptions
        {
            IsNavigationStackEnabled = false,
            TransitionInfoOverride = new FASuppressNavigationTransitionInfo()
        };

        Type[] pageTypes = OperatingSystem.IsWindows()
            ? [typeof(CustomizationPage), typeof(EmulationPage), typeof(AutoProfilePage), typeof(SettingsPage)]
            : [typeof(CustomizationPage), typeof(EmulationPage), typeof(SettingsPage)];

        foreach (Type pageType in pageTypes)
        {
            ContentFrame.NavigateToType(pageType, null, options);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        }

        ContentFrame.NavigateToType(typeof(DeviceInfoPage), null, options);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
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