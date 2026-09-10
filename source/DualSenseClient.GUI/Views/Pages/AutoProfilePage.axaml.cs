using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Controls.Cards;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels.Pages;

namespace DualSenseClient.GUI.Views.Pages;

/// <summary>
/// Foreground-app auto profiles page: lists the rules mapping a focused program to a
/// profile and emulation mode, and edits the selected rule.
/// </summary>
/// <remarks>
/// Resolves <see cref="AutoProfilePageViewModel"/> from the DI container and sets it as
/// the <see cref="UserControl.DataContext"/>. The page is hosted in a frame with
/// <c>CacheSize=0</c>, so a fresh instance is created on each navigation.
/// <see cref="OnLoaded"/> refreshes the ViewModel and <see cref="OnUnloaded"/> persists
/// pending text edits on every navigation.
/// </remarks>
public partial class AutoProfilePage : UserControl
{
    /// <summary>
    /// The ViewModel driving the auto profile display.
    /// </summary>
    private readonly AutoProfilePageViewModel _viewModel;

    /// <summary>
    /// Initializes the auto profiles page, resolving the ViewModel from DI.
    /// </summary>
    public AutoProfilePage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<AutoProfilePageViewModel>();
        DataContext = _viewModel;
    }

    /// <summary>
    /// Resynchronizes the ViewModel with the current auto profile file on every navigation.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _viewModel.Refresh();
        ResyncComboSelection(ControllerCard, _viewModel.SelectedControllerIndex);
        ResyncComboSelection(ProfileCard, _viewModel.SelectedProfileIndex);
    }

    /// <summary>
    /// Re-applies the ViewModel selection to a card whose TwoWay <c>SelectedIndex</c>
    /// binding may have desynchronized: rebuilding the options can coerce the ComboBox
    /// to -1, and the follow-up same-value notification is then dropped by the binding,
    /// leaving the dropdown blank. Setting the value directly always lands; the guard
    /// skips it when already in sync so no spurious save is triggered.
    /// </summary>
    private static void ResyncComboSelection(ComboBoxCard card, int selectedIndex)
    {
        if (card.SelectedIndex != selectedIndex)
        {
            card.SelectedIndex = selectedIndex;
        }
    }

    /// <summary>
    /// Persists pending text edits when leaving the page.
    /// </summary>
    /// <param name="e">The routed event arguments.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _viewModel.Save();
    }

    /// <summary>
    /// Opens the platform file picker for a program executable and stores the chosen
    /// path on the selected rule.
    /// </summary>
    private async void OnBrowseExeClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRule is null)
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
            Title = LocalizationService.GetText("AutoProfilesPage.Exe.Picker"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationService.GetText("AutoProfilesPage.Exe.FileType"))
                {
                    Patterns = ["*.exe"]
                }
            ]
        });

        if (files.Count > 0)
        {
            _viewModel.SelectedExePattern = files[0].Path.LocalPath;
            _viewModel.Save();
        }
    }
}