using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using DualSenseClient.GUI.Views.Pages;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Handles navigation between pages in the NavigationView content frame.
/// </summary>
public class NavigationService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Navigation");

    /// <summary>
    /// The content frame where pages are hosted, or <c>null</c> until registered.
    /// </summary>
    private FAFrame? _contentFrame;

    /// <summary>
    /// The navigation view used to update selection state, or <c>null</c> until registered.
    /// </summary>
    private FANavigationView? _navigationView;

    /// <summary>
    /// Tag of the page currently displayed in the content frame.
    /// </summary>
    private string? _currentPageTag;

    /// <summary>
    /// Tag of the page currently displayed in the content frame.
    /// </summary>
    public string? CurrentPageTag => _currentPageTag;

    /// <summary>
    /// Raised after navigation completes with the new page tag.
    /// </summary>
    public event EventHandler<string>? Navigated;

    /// <summary>
    /// Registers the content frame where pages are hosted.
    /// </summary>
    public void SetContentFrame(FAFrame frame) => _contentFrame = frame;

    /// <summary>
    /// Registers the navigation view used to update selection state.
    /// </summary>
    public void SetNavigationView(FANavigationView navigationView) => _navigationView = navigationView;

    /// <summary>
    /// Navigates to the page associated with the invoked navigation item.
    /// </summary>
    public async Task Navigate(FANavigationViewItem item, FAFrame? contentFrame = null)
    {
        string tag = item.Tag?.ToString() ?? string.Empty;
        await NavigateToTag(tag, contentFrame);
    }

    /// <summary>
    /// Navigates to the page matching the given tag string.
    /// </summary>
    public async Task NavigateToTag(string tag, FAFrame? contentFrame = null)
    {
        FAFrame? frame = contentFrame ?? _contentFrame;
        _currentPageTag = tag;

        _log.Debug($"Navigating to '{tag}'");
        switch (tag)
        {
            case "InfoPage":
                frame?.Navigate(typeof(DeviceInfoPage));
                break;
            case "InputMonitorPage":
                frame?.Navigate(typeof(InputMonitorPage));
                break;
            case "ProfilePage":
                frame?.Navigate(typeof(ProfilePage));
                break;
            case "Settings":
                frame?.Navigate(typeof(SettingsPage));
                break;
            default:
                _log.Warning($"No page registered for tag '{tag}'");
                break;
        }

        UpdateSelection(tag);
        Navigated?.Invoke(this, tag);
    }

    /// <summary>
    /// Sets IsSelected on the matching NavigationViewItem to keep the UI in sync.
    /// Searches both <c>MenuItems</c> and <c>FooterMenuItems</c>.
    /// </summary>
    private void UpdateSelection(string tag)
    {
        if (_navigationView == null)
        {
            return;
        }

        SetSelected(_navigationView.MenuItems, tag);
        SetSelected(_navigationView.FooterMenuItems, tag);
    }

    /// <summary>
    /// Iterates a collection of navigation items and sets <c>IsSelected</c>
    /// on the one whose <c>Tag</c> matches the given tag string.
    /// </summary>
    private static void SetSelected(IList<object>? items, string tag)
    {
        if (items == null)
        {
            return;
        }

        foreach (object item in items)
        {
            if (item is FANavigationViewItem navItem)
            {
                navItem.IsSelected = navItem.Tag?.ToString() == tag;
            }
        }
    }
}