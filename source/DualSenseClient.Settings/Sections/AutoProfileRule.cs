using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// A foreground-app rule mapping one program to a profile, emulation mode, and hiding.
/// An empty <see cref="ExePattern"/> and <see cref="WindowTitle"/> never matches;
/// when only one is set, only that one is tested (AND when both are set).
/// Both patterns are case-insensitive regular expressions (search semantics);
/// an invalid expression falls back to a literal contains match.
/// </summary>
public class AutoProfileRule : INotifyPropertyChanged
{
    /// <summary>
    /// Raised when a bound property changes. Lets the rule list refresh live while editing.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Backing field for <see cref="Name"/>.
    /// </summary>
    private string _name = string.Empty;

    /// <summary>
    /// Backing field for <see cref="ExePattern"/>.
    /// </summary>
    private string _exePattern = string.Empty;

    /// <summary>
    /// Backing field for <see cref="WindowTitle"/>.
    /// </summary>
    private string _windowTitle = string.Empty;

    /// <summary>
    /// Backing field for <see cref="ProfileName"/>.
    /// </summary>
    private string _profileName = string.Empty;

    /// <summary>
    /// Notifies bound controls that a property changed.
    /// </summary>
    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Gets or sets the user-visible rule name shown in the rule list,
    /// or empty to show the program path instead.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get
        {
            return _name;
        }
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (_name == trimmed)
            {
                return;
            }

            _name = trimmed;
            Notify(nameof(Name));
            Notify(nameof(DisplayName));
        }
    }

    /// <summary>
    /// The name shown in the rule list: <see cref="Name"/>, falling back to the
    /// program path and then the window title when no name is set.
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(_name))
            {
                return _name;
            }

            if (!string.IsNullOrEmpty(_exePattern))
            {
                return _exePattern;
            }

            return _windowTitle;
        }
    }

    /// <summary>
    /// The full program details for tooltips: the executable path with the window
    /// title on a second line when one is set.
    /// </summary>
    [JsonIgnore]
    public string Details
    {
        get
        {
            if (!string.IsNullOrEmpty(_exePattern) && !string.IsNullOrEmpty(_windowTitle))
            {
                return _exePattern + "\n" + _windowTitle;
            }

            return !string.IsNullOrEmpty(_exePattern) ? _exePattern : _windowTitle;
        }
    }

    /// <summary>
    /// Precompiled executable path expression used by <see cref="IsMatch"/>,
    /// or <c>null</c> when the path is empty or not a valid expression.
    /// </summary>
    [JsonIgnore] private Regex? _exeRegex;

    /// <summary>
    /// Precompiled window title expression used by <see cref="IsMatch"/>,
    /// or <c>null</c> when the title is empty or not a valid expression.
    /// </summary>
    [JsonIgnore] private Regex? _titleRegex;

    /// <summary>
    /// Time budget for one expression evaluation, guarding the poll loop
    /// against catastrophic backtracking in user-entered expressions.
    /// </summary>
    private static readonly TimeSpan ExpressionTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the executable path pattern of the foreground program as a
    /// case-insensitive regular expression, or empty to match any program
    /// (the title must then match).
    /// </summary>
    [JsonPropertyName("exe")]
    public string ExePattern
    {
        get
        {
            return _exePattern;
        }
        set
        {
            _exePattern = value?.Trim() ?? string.Empty;
            _exeRegex = Compile(_exePattern);
            Notify(nameof(ExePattern));
            Notify(nameof(DisplayName));
            Notify(nameof(Details));
        }
    }

    /// <summary>
    /// Gets or sets the foreground window title as a case-insensitive regular
    /// expression, or empty to match any window (the exe must then match).
    /// </summary>
    [JsonPropertyName("title")]
    public string WindowTitle
    {
        get
        {
            return _windowTitle;
        }
        set
        {
            _windowTitle = value?.Trim() ?? string.Empty;
            _titleRegex = Compile(_windowTitle);
            Notify(nameof(WindowTitle));
            Notify(nameof(DisplayName));
            Notify(nameof(Details));
        }
    }

    /// <summary>
    /// Gets or sets the MAC address of the controller this rule applies to,
    /// or empty when the rule applies to every controller.
    /// </summary>
    [JsonPropertyName("mac_address")]
    public string ControllerMac { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HID device path of the controller this rule applies to
    /// (fallback when no MAC matches), or empty when the rule applies to every controller.
    /// </summary>
    [JsonPropertyName("device_path")]
    public string ControllerPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the profile applied while the rule matches,
    /// or empty to leave the profile unchanged.
    /// </summary>
    [JsonPropertyName("profile_name")]
    public string ProfileName
    {
        get
        {
            return _profileName;
        }
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (_profileName == trimmed)
            {
                return;
            }

            _profileName = trimmed;
            Notify(nameof(ProfileName));
        }
    }

    /// <summary>
    /// Gets or sets the emulation mode applied while the rule matches,
    /// or <c>null</c> to leave the emulation mode unchanged.
    /// </summary>
    [JsonPropertyName("emulation_mode")]
    public EmulationMode? EmulationMode { get; set; }

    /// <summary>
    /// Gets or sets whether the controller is hidden while the rule matches,
    /// or <c>null</c> to leave the hidden state unchanged.
    /// </summary>
    [JsonPropertyName("hide_controller")]
    public bool? HideController { get; set; }

    /// <summary>
    /// Whether the rule leaves profile, emulation, and hiding unchanged, making it a
    /// no-op. The matcher skips such rules so they never shadow lower rules.
    /// </summary>
    [JsonIgnore]
    public bool IsActionless
    {
        get
        {
            return string.IsNullOrEmpty(_profileName) && EmulationMode is null && HideController is null;
        }
    }

    /// <summary>
    /// Whether the rule targets every controller (no MAC or device path selector).
    /// </summary>
    [JsonIgnore]
    public bool AppliesToAllControllers
    {
        get
        {
            return string.IsNullOrEmpty(ControllerMac?.Trim()) && string.IsNullOrEmpty(ControllerPath?.Trim());
        }
    }

    /// <summary>
    /// Tests the rule against the foreground program.
    /// </summary>
    /// <param name="foregroundExe">Full executable path of the foreground process.</param>
    /// <param name="foregroundTitle">Title of the foreground window.</param>
    public bool IsMatch(string? foregroundExe, string? foregroundTitle)
    {
        if (string.IsNullOrEmpty(_exePattern) && string.IsNullOrEmpty(_windowTitle))
        {
            return false;
        }

        bool exeMatched = true;
        bool titleMatched = true;

        if (!string.IsNullOrEmpty(_exePattern))
        {
            exeMatched = MatchExpression(_exeRegex, _exePattern, (foregroundExe ?? string.Empty).Trim());
        }

        if (exeMatched && !string.IsNullOrEmpty(_windowTitle))
        {
            titleMatched = MatchExpression(_titleRegex, _windowTitle, (foregroundTitle ?? string.Empty).Trim());
        }

        return exeMatched && titleMatched;
    }

    /// <summary>
    /// Compiles a rule pattern, or returns <c>null</c> when it is empty or invalid
    /// (invalid patterns fall back to a literal contains match in
    /// <see cref="MatchExpression"/>).
    /// </summary>
    private static Regex? Compile(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ExpressionTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Matches a value against a precompiled expression, falling back to a
    /// literal contains match when the expression is invalid or times out.
    /// </summary>
    private static bool MatchExpression(Regex? expression, string pattern, string value)
    {
        if (expression is not null)
        {
            try
            {
                return expression.IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests whether the rule applies to a controller: rules without a selector apply to
    /// every controller, otherwise the MAC address is tried first with the HID device
    /// path as fallback (mirroring <see cref="ControllerInfoService"/> lookups).
    /// </summary>
    public bool MatchesController(string? mac, string? devicePath)
    {
        if (AppliesToAllControllers)
        {
            return true;
        }

        string normalizedMac = mac?.Trim().ToUpperInvariant() ?? string.Empty;
        string selectorMac = ControllerMac?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!string.IsNullOrEmpty(normalizedMac) && !string.IsNullOrEmpty(selectorMac)
                                                 && string.Equals(normalizedMac, selectorMac, StringComparison.Ordinal))
        {
            return true;
        }

        string normalizedPath = devicePath?.Trim() ?? string.Empty;
        string selectorPath = ControllerPath?.Trim() ?? string.Empty;
        return !string.IsNullOrEmpty(normalizedPath) && !string.IsNullOrEmpty(selectorPath)
                                                     && string.Equals(normalizedPath, selectorPath, StringComparison.Ordinal);
    }
}