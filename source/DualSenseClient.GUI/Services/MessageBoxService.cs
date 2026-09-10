using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DualSenseClient.Core.Utilities;
using FluentAvalonia.UI.Controls;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Specifies which type of dialog to use for message boxes.
/// </summary>
public enum MessageBoxDialogType
{
    /// <summary>
    /// Uses ContentDialog (default). Best for simple dialogs at the window level.
    /// </summary>
    ContentDialog,

    /// <summary>
    /// Uses TaskDialog. Best for dialogs that need to appear over other TaskDialogs.
    /// </summary>
    TaskDialog
}

/// <summary>
/// Provides a service for displaying message dialogs using FluentAvalonia's ContentDialog or TaskDialog.
/// </summary>
public interface IMessageBoxService
{
    /// <summary>
    /// Shows an information message dialog.
    /// </summary>
    Task ShowInfoAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);

    /// <summary>
    /// Shows a warning message dialog.
    /// </summary>
    Task ShowWarningAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);

    /// <summary>
    /// Shows an error message dialog.
    /// </summary>
    Task ShowErrorAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);

    /// <summary>
    /// Shows a custom message dialog with customizable buttons.
    /// </summary>
    Task<FAContentDialogResult> ShowCustomDialogAsync(string title, string message,
        string primaryButtonText, string? secondaryButtonText = null, string? closeButtonText = null,
        MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);

    /// <summary>
    /// Shows a what's-new dialog with one collapsible section per release
    /// (newest first, newest expanded). Release bodies render as markdown.
    /// </summary>
    Task ShowChangelogAsync(string title, IReadOnlyList<Changelog.Entry> releases, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog);
}

/// <summary>
/// Implementation of the MessageBox service using FluentAvalonia's ContentDialog or TaskDialog.
/// </summary>
public class MessageBoxService : IMessageBoxService
{
    /// <summary>
    /// Shows an information message dialog.
    /// </summary>
    public async Task ShowInfoAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog) =>
        await ShowDialogAsync(title, message, dialogType);

    /// <summary>
    /// Shows a warning message dialog.
    /// </summary>
    public async Task ShowWarningAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog) =>
        await ShowDialogAsync(title, message, dialogType);

    /// <summary>
    /// Shows an error message dialog.
    /// </summary>
    public async Task ShowErrorAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog) =>
        await ShowDialogAsync(title, message, dialogType);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// </summary>
    public async Task<bool> ShowConfirmationAsync(string title, string message, MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog)
    {
        if (dialogType == MessageBoxDialogType.TaskDialog)
        {
            return await ShowTaskDialogConfirmationAsync(title, message);
        }
        else
        {
            return await ShowContentDialogConfirmationAsync(title, message);
        }
    }

    /// <summary>
    /// Shows a what's-new dialog with one collapsible section per release
    /// (newest first, newest expanded). Release bodies render as markdown.
    /// </summary>
    public async Task ShowChangelogAsync(string title, IReadOnlyList<Changelog.Entry> releases,
        MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog)
    {
        Control content = BuildChangelogContent(releases);
        if (dialogType == MessageBoxDialogType.TaskDialog)
        {
            await ShowTaskDialogAsync(title, content);
        }
        else
        {
            await ShowContentDialogAsync(title, content);
        }
    }

    /// <summary>
    /// Shows a custom message dialog with customizable buttons.
    /// </summary>
    public async Task<FAContentDialogResult> ShowCustomDialogAsync(string title, string message,
        string primaryButtonText, string? secondaryButtonText = null, string? closeButtonText = null,
        MessageBoxDialogType dialogType = MessageBoxDialogType.ContentDialog)
    {
        if (dialogType == MessageBoxDialogType.TaskDialog)
        {
            return await ShowTaskDialogCustomAsync(title, message, primaryButtonText, secondaryButtonText, closeButtonText);
        }
        else
        {
            return await ShowContentDialogCustomAsync(title, message, primaryButtonText, secondaryButtonText, closeButtonText);
        }
    }

    /// <summary>
    /// Shows a dialog with an OK button (used for info, warning, and error).
    /// </summary>
    private async Task ShowDialogAsync(string title, object? content, MessageBoxDialogType dialogType)
    {
        if (dialogType == MessageBoxDialogType.TaskDialog)
        {
            await ShowTaskDialogAsync(title, content);
        }
        else
        {
            await ShowContentDialogAsync(title, content);
        }
    }

    /// <summary>
    /// Shows a simple message dialog using ContentDialog.
    /// </summary>
    private async Task ShowContentDialogAsync(string title, object? content)
    {
        FAContentDialog dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = LocalizationService.GetText("MessageBox.Ok"),
            DefaultButton = FAContentDialogButton.Primary
        };

        await dialog.ShowAsync();
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons using ContentDialog.
    /// </summary>
    private async Task<bool> ShowContentDialogConfirmationAsync(string title, string message)
    {
        FAContentDialog dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = LocalizationService.GetText("MessageBox.Yes"),
            SecondaryButtonText = LocalizationService.GetText("MessageBox.No"),
            DefaultButton = FAContentDialogButton.Primary
        };

        FAContentDialogResult result = await dialog.ShowAsync();
        return result == FAContentDialogResult.Primary;
    }

    /// <summary>
    /// Shows a custom message dialog with customizable buttons using ContentDialog.
    /// </summary>
    private async Task<FAContentDialogResult> ShowContentDialogCustomAsync(string title, string message,
        string primaryButtonText, string? secondaryButtonText = null, string? closeButtonText = null)
    {
        FAContentDialog dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = FAContentDialogButton.Primary
        };

        if (string.IsNullOrEmpty(secondaryButtonText) && string.IsNullOrEmpty(closeButtonText))
        {
            dialog.SecondaryButtonText = null;
            dialog.CloseButtonText = null;
        }
        else if (!string.IsNullOrEmpty(secondaryButtonText) && string.IsNullOrEmpty(closeButtonText))
        {
            dialog.CloseButtonText = null;
        }
        else if (string.IsNullOrEmpty(closeButtonText))
        {
            dialog.CloseButtonText = LocalizationService.GetText("MessageBox.Cancel");
        }

        return await dialog.ShowAsync();
    }

    /// <summary>
    /// Shows a simple message dialog using TaskDialog.
    /// </summary>
    private async Task ShowTaskDialogAsync(string title, object? content)
    {
        FATaskDialog dialog = new FATaskDialog
        {
            Title = title,
            Content = content,
            XamlRoot = App.MainWindow
        };

        FATaskDialogButton okButton = new FATaskDialogButton
        {
            Text = LocalizationService.GetText("MessageBox.Ok"),
            DialogResult = "OK"
        };

        dialog.Buttons.Add(okButton);
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons using TaskDialog.
    /// </summary>
    private async Task<bool> ShowTaskDialogConfirmationAsync(string title, string message)
    {
        FATaskDialog dialog = new FATaskDialog
        {
            Title = title,
            Content = message,
            XamlRoot = App.MainWindow
        };

        FATaskDialogButton yesButton = new FATaskDialogButton
        {
            Text = LocalizationService.GetText("MessageBox.Yes"),
            DialogResult = "Yes"
        };

        FATaskDialogButton noButton = new FATaskDialogButton
        {
            Text = LocalizationService.GetText("MessageBox.No"),
            DialogResult = "No"
        };

        dialog.Buttons.Add(yesButton);
        dialog.Buttons.Add(noButton);

        object? result = await dialog.ShowAsync();
        return ReferenceEquals(result, "Yes");
    }

    /// <summary>
    /// Shows a custom message dialog with customizable buttons using TaskDialog.
    /// </summary>
    private async Task<FAContentDialogResult> ShowTaskDialogCustomAsync(string title, string message,
        string primaryButtonText, string? secondaryButtonText = null, string? closeButtonText = null)
    {
        FATaskDialog dialog = new FATaskDialog
        {
            Title = title,
            Content = message,
            XamlRoot = App.MainWindow
        };

        FATaskDialogButton primaryButton = new FATaskDialogButton
        {
            Text = primaryButtonText,
            DialogResult = "Primary"
        };

        dialog.Buttons.Add(primaryButton);

        if (!string.IsNullOrEmpty(secondaryButtonText))
        {
            FATaskDialogButton secondaryButton = new FATaskDialogButton
            {
                Text = secondaryButtonText,
                DialogResult = "Secondary"
            };
            dialog.Buttons.Add(secondaryButton);
        }

        if (!string.IsNullOrEmpty(closeButtonText))
        {
            FATaskDialogButton closeButton = new FATaskDialogButton
            {
                Text = closeButtonText,
                DialogResult = "Close"
            };
            dialog.Buttons.Add(closeButton);
        }
        else if (!string.IsNullOrEmpty(secondaryButtonText))
        {
            FATaskDialogButton cancelButton = new FATaskDialogButton
            {
                Text = LocalizationService.GetText("MessageBox.Cancel"),
                DialogResult = "Cancel"
            };
            dialog.Buttons.Add(cancelButton);
        }

        object? result = await dialog.ShowAsync();

        if (ReferenceEquals(result, "Primary"))
        {
            return FAContentDialogResult.Primary;
        }
        else if (ReferenceEquals(result, "Secondary"))
        {
            return FAContentDialogResult.Secondary;
        }
        else
        {
            return FAContentDialogResult.None;
        }
    }

    /// <summary>
    /// Builds a scrollable list with one collapsible section per release
    /// (newest first, newest expanded). Empty bodies show a placeholder line.
    /// </summary>
    private static Control BuildChangelogContent(IReadOnlyList<Changelog.Entry> releases)
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 8
        };

        bool first = true;
        foreach (Changelog.Entry release in releases)
        {
            string header = release.PublishedAt == DateTimeOffset.MinValue
                ? release.Tag
                : $"{release.Tag} ({release.PublishedAt:yyyy-MM-dd})";
            string body = string.IsNullOrWhiteSpace(release.Body) ? "No release notes." : release.Body;
            panel.Children.Add(new Expander
            {
                Header = header,
                Content = BuildMarkdownContent(body),
                IsExpanded = first
            });
            first = false;
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    /// <summary>
    /// Builds a scrollable rendering of <see cref="Markdown"/> blocks. Each
    /// paragraph is a <see cref="WrapPanel"/> of word-sized
    /// <see cref="TextBlock"/>s with identical metrics, so links always sit
    /// on the same baseline as the surrounding words.
    /// </summary>
    private static Control BuildMarkdownContent(string markdown)
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 4
        };

        foreach (Block block in Markdown.ParseBlocks(markdown))
        {
            bool heading = block.Style == BlockStyle.Heading;
            double fontSize = heading ? 16 : 14;
            WrapPanel paragraph = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
            foreach (Word word in block.Words)
            {
                paragraph.Children.Add(BuildWord(word, fontSize, heading));
            }

            panel.Children.Add(paragraph);
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    /// <summary>
    /// Builds one word with explicit metrics. Words with a <see cref="Word.Url"/>
    /// become underlined accent-colored links opening in the default browser.
    /// </summary>
    private static TextBlock BuildWord(Word word, double fontSize, bool heading)
    {
        TextBlock block = new TextBlock
        {
            Text = word.Text,
            FontSize = fontSize,
            FontWeight = heading || word.Bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.NoWrap
        };

        if (word.Url is not null && Uri.TryCreate(word.Url, UriKind.Absolute, out Uri? uri))
        {
            block.Foreground = new SolidColorBrush(GetLinkColor());
            block.TextDecorations = TextDecorations.Underline;
            block.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(block, word.Url);
            block.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(block).Properties.IsLeftButtonPressed)
                {
                    NotificationPopupService.OpenUrl(uri.ToString());
                }
            };
        }

        return block;
    }

    /// <summary>
    /// Returns the accent color of the active theme for link text.
    /// Falls back to the default theme's accent when the resource is missing.
    /// </summary>
    private static Color GetLinkColor()
    {
        if (Application.Current is { } app
            && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out object? value)
            && value is Color color)
        {
            return color;
        }

        return Color.Parse("#FF107C10");
    }
}