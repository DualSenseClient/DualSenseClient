using Avalonia.Controls;
using Avalonia.Threading;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// A user control that displays a splash screen with a status message and progress bar.
/// Used by <see cref="AppSplashScreen"/> to provide visual feedback during application startup.
/// </summary>
public partial class SplashScreenView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SplashScreenView"/> class.
    /// </summary>
    public SplashScreenView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Updates the status message displayed below the progress bar.
    /// This method is thread-safe and dispatches the update to the UI thread if called from a background context.
    /// </summary>
    /// <param name="status">The status text to display (e.g., "Loading resources...").</param>
    public void UpdateStatusMessage(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LoadingText.Text = status;
        });
    }
}