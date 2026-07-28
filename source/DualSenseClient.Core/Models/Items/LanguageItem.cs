using System.Globalization;

namespace DualSenseClient.Core.Models.Items;

/// <summary>
/// Represents a language option for the UI, wrapping a <see cref="CultureInfo"/>
/// with its localized display name for use in dropdown controls.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="DisplayName"/> property defaults to the culture's own display name
/// (e.g., "English (United States)") but can be overridden if a custom label is needed.
/// </para>
/// <para>
/// <see cref="ToString"/> returns the display name with its first character capitalized
/// using the associated culture's text info rules, ensuring correct casing for all languages.
/// </para>
/// </remarks>
public class LanguageItem
{
    /// <summary>
    /// The culture associated with this language item.
    /// Used to determine the language code for localization switching.
    /// </summary>
    public CultureInfo Culture { get; }

    /// <summary>
    /// The display name shown in the UI dropdown.
    /// Defaults to the culture's own display name (e.g., "English (United States)").
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageItem"/> class.
    /// </summary>
    /// <param name="culture">The culture to associate with this language item. Cannot be null.</param>
    public LanguageItem(CultureInfo culture)
    {
        Culture = culture;
        DisplayName = culture.DisplayName;
    }

    /// <summary>
    /// Returns the capitalized display name for this language.
    /// Used as the string representation in ComboBox dropdowns.
    /// </summary>
    /// <returns>The display name with its first character capitalized per the culture's rules.</returns>
    public override string ToString() => CapitalizeFirst(DisplayName, Culture);

    /// <summary>
    /// Capitalizes the first character of the given text using the culture's text info rules.
    /// Handles surrogate pairs (non-BMP characters) correctly to ensure proper capitalization.
    /// </summary>
    /// <param name="text">The text to capitalize. If null or empty, returned as-is.</param>
    /// <param name="culture">The culture whose text info rules are used for capitalization.</param>
    /// <returns>The text with its first character capitalized, or the original text if empty.</returns>
    private static string CapitalizeFirst(string text, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        TextInfo textInfo = culture.TextInfo;
        int firstCharLength = char.IsSurrogatePair(text, 0) ? 2 : 1;

        string firstChar = text.Substring(0, firstCharLength);
        string remainingText = text.Substring(firstCharLength);
        return textInfo.ToUpper(firstChar) + remainingText;
    }

    /// <summary>
    /// Determines whether this <see cref="LanguageItem"/> is equal to another object.
    /// Two items are considered equal if they wrap the same <see cref="CultureInfo"/>.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the other object is a <see cref="LanguageItem"/> with the same culture; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is LanguageItem other && Culture.Equals(other.Culture);
    }

    /// <summary>
    /// Returns the hash code based on the underlying <see cref="CultureInfo"/>.
    /// </summary>
    /// <returns>The hash code of the associated culture.</returns>
    public override int GetHashCode() => Culture.GetHashCode();
}