using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.GUI.Services;

namespace DualSenseClient.GUI.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Gets the title displayed in the main window title bar, localized via <see cref="LocalizationService"/>.
    /// </summary>
    public string WindowTitle { get; } = LocalizationService.GetText("MainWindow.Title");
}