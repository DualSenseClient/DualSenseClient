using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DualSenseClient.Views.Controls;

public partial class VirtualControllerSettings : UserControl
{
    public VirtualControllerSettings()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}