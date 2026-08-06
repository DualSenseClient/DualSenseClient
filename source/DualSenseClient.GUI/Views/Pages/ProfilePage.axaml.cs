using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Profile page providing a manager for controller profiles (lightbar color, microphone
/// LED mode, and player LEDs), per-controller profile assignment, and a live editor for
/// the lights of the controller selected in the title bar combobox.
/// </summary>
/// <remarks>
/// Resolves <see cref="ProfilePageViewModel"/> from the DI container and sets it as the
/// <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created
/// on each navigation. <see cref="OnLoaded"/> refreshes the ViewModel so the displayed
/// profiles and assignment match the current profile service state on every navigation.
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

    /// <summary>
    /// Resynchronizes the ViewModel with the current profile file and controller selection.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _viewModel.Refresh();
    }

    /// <summary>
    /// Opens the platform file picker for a sound action and stores the chosen file on the
    /// action's item. Mirrors the audio file picker on the input monitor page.
    /// </summary>
    private async void OnBrowseSoundClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SpecialActionItem item)
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
            Title = LocalizationService.GetText("ProfilePage.SpecialActions.Sound.PickTitle"),
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
            item.SetSoundFile(files[0].Path.LocalPath);
        }
    }
}