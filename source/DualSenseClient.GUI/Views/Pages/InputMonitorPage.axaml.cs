using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
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
/// The controller visualization is the reusable <see cref="Controls.DualSenseControllerView"/>,
/// which updates itself from the monitor item's notifications; this code-behind only drives
/// the motion graphs, which are re-pointed at the item's sample buffer when a motion update lands.
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
    /// Initializes the input monitor page, resolving the ViewModel from DI.
    /// </summary>
    public InputMonitorPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<InputMonitorPageViewModel>();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeItem(_viewModel.CurrentDevice);
        UpdateGraphs();
    }

    /// <summary>
    /// Turns off any active output-test effects (vibration / adaptive triggers) when the
    /// page is unloaded so the controller is not left buzzing or with trigger force applied.
    /// </summary>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _item?.ResetTestOutputs();
        base.OnUnloaded(e);
    }

    /// <summary>
    /// Opens an audio file for the current controller's audio player using the platform
    /// file picker.
    /// </summary>
    private async void OnOpenAudioClick(object? sender, RoutedEventArgs e)
    {
        if (_item?.Audio is not { } audio)
        {
            return;
        }

        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } provider)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetText("InputMonitorPage.Audio.PickTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationService.GetText("InputMonitorPage.Audio.FileType"))
                {
                    Patterns = ["*.wav", "*.mp3", "*.flac", "*.aac", "*.m4a", "*.ogg", "*.wma", "*.aiff", "*.mp4"]
                }
            ]
        });

        if (files.Count > 0)
        {
            audio.OpenFile(files[0].Path.LocalPath);
        }
    }

    /// <summary>
    /// Tracks the shell's controller selection and re-points the motion graphs.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InputMonitorPageViewModel.CurrentDevice))
        {
            SubscribeItem(_viewModel.CurrentDevice);
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
    /// Re-points the motion graphs when the live item reports new motion samples. All other
    /// property notifications are ignored: the item re-raises every tracked property once
    /// per input report, so reacting to each of them would refresh the graphs dozens of
    /// times between actual motion updates.
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InputMonitorItem.MotionSamples))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateGraphs();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateGraphs);
        }
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