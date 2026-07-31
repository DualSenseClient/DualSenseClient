using Avalonia;
using Avalonia.Controls.Primitives;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// Card that displays a title and description, used for empty states
/// such as when no controller is selected.
/// </summary>
public class EmptyStateCard : TemplatedControl
{
    /// <summary>
    /// The main title text displayed in SemiBold.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyStateCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description shown below the title in a smaller font.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<EmptyStateCard, string?>(nameof(Description));

    /// <summary>
    /// Gets or sets the main title text displayed in SemiBold.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the subtitle/description shown below the title in a smaller font.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}