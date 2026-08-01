using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Input monitor page displaying the current button, stick, trigger, motion, and
/// touchpad state for the controller selected in the title bar combobox.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="InputMonitorPageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created
/// on each navigation.
/// </para>
/// <para>
/// The indicator dots for the sticks and touchpad are driven directly from the monitor item's
/// <see cref="INotifyPropertyChanged"/> notifications instead of XAML bindings, because the
/// dots must track high-frequency updates that some binding paths do not reliably propagate.
/// The monitor item already coalesces and marshals its notifications to the UI thread.
/// </para>
/// </remarks>
public partial class InputMonitorPage : UserControl
{
    /// <summary>
    /// The ViewModel driving the input monitor display.
    /// </summary>
    private readonly InputMonitorPageViewModel _viewModel;

    /// <summary>
    /// The monitor item currently displayed, or <c>null</c> when no controller is selected.
    /// </summary>
    private InputMonitorItem? _item;

    /// <summary>
    /// Touchpad surface size (must match the AXAML).
    /// </summary>
    private const double TouchSurfaceWidth = 320;

    private const double TouchSurfaceHeight = 180;

    /// <summary>
    /// Touch indicator dot and its label group (must match the AXAML).
    /// </summary>
    private const double TouchDotSize = 16;

    private const double TouchDotGroupWidth = 64;
    private const double TouchDotGroupHeight = 36;

    /// <summary>
    /// Initializes the input monitor page, resolving the ViewModel from DI.
    /// </summary>
    public InputMonitorPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<InputMonitorPageViewModel>();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeItem(_viewModel.CurrentDevice);
        UpdateDots();
        UpdateGraphs();
    }

    /// <summary>
    /// Tracks the shell's controller selection and updates the indicator dots.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InputMonitorPageViewModel.CurrentDevice))
        {
            SubscribeItem(_viewModel.CurrentDevice);
            UpdateDots();
            UpdateGraphs();
        }
    }

    /// <summary>
    /// Resubscribes to the monitor item's notifications, releasing the previous subscription.
    /// </summary>
    private void SubscribeItem(InputMonitorItem? item)
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
        }

        _item = item;

        if (_item is not null)
        {
            _item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <summary>
    /// Repositions the indicator dots and motion graphs whenever the live item reports an update.
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateDots();
            UpdateGraphs();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateDots();
                UpdateGraphs();
            });
        }
    }

    /// <summary>
    /// Moves the stick and touchpad indicator dots to the monitor item's current positions.
    /// </summary>
    private void UpdateDots()
    {
        InputMonitorItem? item = _item;
        if (item is null)
        {
            LeftStickDot.IsVisible = false;
            RightStickDot.IsVisible = false;
            Touch1DotGroup.IsVisible = false;
            Touch2DotGroup.IsVisible = false;
            return;
        }

        LeftStickDot.IsVisible = true;
        LeftStickDot.Margin = new Thickness(item.LeftStickDotX, item.LeftStickDotY, 0, 0);
        RightStickDot.IsVisible = true;
        RightStickDot.Margin = new Thickness(item.RightStickDotX, item.RightStickDotY, 0, 0);

        PositionTouchDotGroup(Touch1DotGroup, item.Touch1Active, item.Touch1DotX, item.Touch1DotY);
        PositionTouchDotGroup(Touch2DotGroup, item.Touch2Active, item.Touch2DotX, item.Touch2DotY);
    }

    /// <summary>
    /// Places a touch indicator group (dot plus its label) at the touch point, keeping
    /// the label centered under the dot and clamped inside the surface.
    /// </summary>
    private static void PositionTouchDotGroup(Grid group, bool active, double dotX, double dotY)
    {
        group.IsVisible = active;
        if (!active)
        {
            return;
        }

        double left = Math.Clamp(dotX - (TouchDotGroupWidth - TouchDotSize) / 2, 0, TouchSurfaceWidth - TouchDotGroupWidth);
        double top = Math.Min(dotY, TouchSurfaceHeight - TouchDotGroupHeight);
        group.Margin = new Thickness(left, top, 0, 0);
    }

    /// <summary>
    /// Points the motion graphs at the monitor item's rolling sample buffer.
    /// </summary>
    private void UpdateGraphs()
    {
        IReadOnlyList<MotionState>? samples = _item?.MotionSamples;
        GyroGraph.Samples = samples;
        AccelGraph.Samples = samples;
    }
}