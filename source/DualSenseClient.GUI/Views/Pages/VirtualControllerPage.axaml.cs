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
/// Virtual controller page hosting the emulation settings and the per-mode button
/// remapping editor for the controller selected in the title bar combobox.
/// </summary>
/// <remarks>
/// Resolves <see cref="VirtualControllerPageViewModel"/> from the DI container and sets it as
/// the <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance (and fresh selection subscription) is created on
/// each navigation. Clicks on the controller illustration toggle the clicked button's
/// membership in the pending remapping selection.
/// </remarks>
public partial class VirtualControllerPage : UserControl
{
    /// <summary>
    /// The ViewModel driving the page.
    /// </summary>
    private readonly VirtualControllerPageViewModel _viewModel;

    /// <summary>
    /// Initializes the page, resolving the ViewModel from DI.
    /// </summary>
    public VirtualControllerPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<VirtualControllerPageViewModel>();
        DataContext = _viewModel;
    }

    /// <summary>
    /// Toggles the clicked illustration button in the pending selection.
    /// </summary>
    private void ControllerView_OnButtonClicked(object? sender, ButtonType e) => _viewModel.ToggleButton(e);

    /// <summary>
    /// Toggles the clicked target button in the pending target selection.
    /// </summary>
    private void TargetView_OnTargetClicked(object? sender, string e) => _viewModel.ToggleTarget(e);

    /// <summary>
    /// Opens the platform file picker for an executable and stores the chosen path as
    /// the application whose audio is forwarded to this controller.
    /// </summary>
    /// <param name="sender">The button that was clicked.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OnBrowseAudioAppClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } provider)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.GetText("AudioSource.Executable.Picker"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationService.GetText("AudioSource.Executable.FileType"))
                {
                    Patterns = ["*.exe"]
                }
            ]
        });

        if (files.Count > 0)
        {
            _viewModel.SelectAudioApplication(files[0].Path.LocalPath);
        }
    }
}