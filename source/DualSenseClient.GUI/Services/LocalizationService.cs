using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Manages localization and internationalization. Loads languages, retrieves localized strings, and manages supported languages.
/// Untranslated entries (marked with <see cref="UntranslatedMarker"/> or left empty) are automatically
/// stripped from non-default dictionaries so that Avalonia's MergedDictionaries resolution falls back
/// to the default (English) value — for both code-behind lookups and {DynamicResource} bindings.
/// </summary>
public static class LocalizationService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("LocalizationService");

    // Fields
    /// <summary>
    /// Sentinel value that translators place in resource files to indicate a string has not been translated yet.
    /// The entry is removed at load time, so the default language value is used instead.
    /// </summary>
    public const string UntranslatedMarker = "#NOTTRANSLATED#";

    /// <summary>
    /// The default language code used as the permanent fallback for all localization lookups.
    /// </summary>
    private const string DefaultLanguageCode = "en";

    /// <summary>
    /// The default language resource dictionary, always kept loaded as the permanent fallback.
    /// </summary>
    private static ResourceDictionary? _defaultLanguage;

    /// <summary>
    /// The currently selected language overlay dictionary, dynamically added/removed when switching languages.
    /// </summary>
    private static ResourceDictionary? _currentOverlay;

    /// <summary>
    /// The base URI where language resource files (e.g., "en.axaml", "de.axaml") are located.
    /// </summary>
    private static string _baseUri = "avares://DualSenseClient/Resources/Language/";

    /// <summary>
    /// Array of all supported cultures that can be loaded by the application.
    /// </summary>
    private static readonly CultureInfo[] SupportedLanguages =
    [
        new CultureInfo(DefaultLanguageCode) // English
        // Add Languages here
    ];

    // Functions
    /// <summary>
    /// Initializes the LocalizationService and loads the default language as the permanent fallback.
    /// </summary>
    /// <param name="baseUri">The base URI where language resource files are located.</param>
    /// <exception cref="ArgumentException">Thrown when baseUri is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Application.Current is null.</exception>
    public static void Initialize(string baseUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUri);

        if (Application.Current is null)
        {
            throw new InvalidOperationException("Cannot initialize localization before Application is created.");
        }

        _baseUri = baseUri;
        _defaultLanguage = LoadDictionary(DefaultLanguageCode);
        Application.Current.Resources.MergedDictionaries.Add(_defaultLanguage);

        CultureInfo.CurrentUICulture = new CultureInfo(DefaultLanguageCode);
    }

    /// <summary>
    /// Switches the app language. The default language always remains loaded as a fallback,
    /// so any missing or untranslated keys in the selected language resolve to English automatically.
    /// Every {DynamicResource} in the application updates immediately.
    /// </summary>
    /// <param name="langCode">The language code to switch to (e.g., "en", "de", "fr"). Defaults to "en".</param>
    public static void LoadLanguage(string langCode = DefaultLanguageCode)
    {
        if (Application.Current is null)
        {
            return;
        }

        EnsureInitialized();

        IList<IResourceProvider> merged = Application.Current.Resources.MergedDictionaries;

        if (_currentOverlay is not null)
        {
            merged.Remove(_currentOverlay);
            _currentOverlay = null;
        }

        if (!IsDefaultLanguage(langCode))
        {
            try
            {
                _currentOverlay = LoadDictionary(langCode);

                int removed = RemoveUntranslatedEntries(_currentOverlay);

                if (removed > 0)
                {
                    _log.Warning($"Language '{langCode}': {removed} untranslated string(s) will fall back to '{DefaultLanguageCode}'");
                }

                merged.Add(_currentOverlay);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load language '{langCode}', falling back to '{DefaultLanguageCode}': {ex.Message}");
                langCode = DefaultLanguageCode;
            }
        }

        CultureInfo.CurrentUICulture = new CultureInfo(langCode);
    }

    /// <summary>
    /// Gets a localized string from the current language resources.
    /// Falls back to the default language automatically via Avalonia's MergedDictionaries resolution.
    /// </summary>
    /// <param name="key">The resource key to look up.</param>
    /// <returns>The localized string, or the key in square brackets if not found.</returns>
    public static string GetText(string key)
    {
        // Use dispatcher if called from a background thread
        if (Application.Current is not null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return Dispatcher.UIThread.Invoke(() => GetText(key));
            }

            if (Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value)
                && value is string text)
            {
                return text;
            }
        }

        return $"[{key}]";
    }

    /// <summary>
    /// Gets all supported cultures.
    /// </summary>
    /// <returns>An array of CultureInfo objects representing all supported languages.</returns>
    public static CultureInfo[] GetSupportedLanguages() => SupportedLanguages;

    /// <summary>
    /// Checks whether the specified language code matches the default language.
    /// </summary>
    /// <param name="langCode">The language code to check (e.g., "en", "de", "fr").</param>
    /// <returns>True if the language code matches the default language, otherwise false.</returns>
    private static bool IsDefaultLanguage(string langCode) => langCode.Equals(DefaultLanguageCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a ResourceDictionary from the specified language's AXAML resource file.
    /// </summary>
    /// <param name="langCode">The language code to load (e.g., "en", "de", "fr").</param>
    /// <returns>The loaded ResourceDictionary containing localized string resources.</returns>
    private static ResourceDictionary LoadDictionary(string langCode) => (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"{_baseUri}{langCode}.axaml"));

    /// <summary>
    /// Ensures that the LocalizationService has been properly initialized before use.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Initialize() has not been called.</exception>
    private static void EnsureInitialized()
    {
        if (_defaultLanguage is null || string.IsNullOrEmpty(_baseUri))
        {
            _log.Warning("LocalizationService has not been initialized");
            _log.Info($"Initializing LocalizationService with baseUri: {_baseUri}");
            Initialize(_baseUri);
        }
    }

    /// <summary>
    /// Removes untranslated entries (marked with <see cref="UntranslatedMarker"/> or empty/whitespace) from the dictionary.
    /// This allows Avalonia's resource lookup to fall through to the default dictionary.
    /// </summary>
    /// <param name="dictionary">The ResourceDictionary to clean up.</param>
    /// <returns>The number of entries removed.</returns>
    private static int RemoveUntranslatedEntries(ResourceDictionary dictionary)
    {
        List<object> keysToRemove = [];

        foreach (KeyValuePair<object, object?> entry in dictionary)
        {
            if (entry.Value is not string s || IsUntranslated(s))
            {
                keysToRemove.Add(entry.Key);
            }
        }

        foreach (object key in keysToRemove)
        {
            dictionary.Remove(key);
        }

        return keysToRemove.Count;
    }

    /// <summary>
    /// Determines whether a string value should be considered untranslated.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <returns>True if the value is null, whitespace, or matches the <see cref="UntranslatedMarker"/>, otherwise false.</returns>
    private static bool IsUntranslated(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals(UntranslatedMarker, StringComparison.OrdinalIgnoreCase);
}