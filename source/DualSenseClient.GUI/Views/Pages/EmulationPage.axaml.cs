using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Emulation page hosting the virtual controller settings and the per-mode button
/// remapping editor for the controller selected in the title bar combobox.
/// </summary>
/// <remarks>
/// Resolves <see cref="EmulationPageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created
/// on each navigation. Clicks on the controller illustration toggle the clicked button's
/// membership in the pending remapping selection.
/// </remarks>
public partial class EmulationPage : UserControl
{
    /// <summary>
    /// Initializes the page, resolving the ViewModel from DI.
    /// </summary>
    public EmulationPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<EmulationPageViewModel>();
    }

    /// <summary>
    /// Toggles the clicked illustration button in the pending selection.
    /// </summary>
    private void ControllerView_OnButtonClicked(object? sender, ButtonType e)
    {
        if (DataContext is EmulationPageViewModel viewModel)
        {
            viewModel.ToggleButton(e);
        }
    }

    /// <summary>
    /// Opens an audio file for the current controller's audio player using the platform
    /// file picker.
    /// </summary>
    private async void OnOpenAudioClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EmulationPageViewModel viewModel
            || viewModel.MonitorState?.Audio is not { } audio)
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
            Title = LocalizationService.GetText("EmulationPage.Audio.PickTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationService.GetText("EmulationPage.Audio.FileType"))
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
    /// Toggles the clicked target button in the pending target selection.
    /// </summary>
    private void TargetView_OnTargetClicked(object? sender, string e)
    {
        if (DataContext is EmulationPageViewModel viewModel)
        {
            viewModel.ToggleTarget(e);
        }
    }
}