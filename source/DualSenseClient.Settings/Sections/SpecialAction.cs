using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Action type names supported by special actions. Stored as strings in settings so the
/// settings layer does not reference the controller protocol; the executing side
/// (<c>DualSenseClient.Controllers.SpecialActions.SpecialActionEngine</c>) interprets them.
/// </summary>
public static class SpecialActionTypes
{
    /// <summary>
    /// Disconnects the controller over Bluetooth.
    /// </summary>
    public const string Disconnect = "Disconnect";

    /// <summary>
    /// Sets the lightbar color to the effect's RGB values.
    /// </summary>
    public const string SetLightbarColor = "SetLightbarColor";

    /// <summary>
    /// Sets the player LED layout to the effect's LED mask.
    /// </summary>
    public const string SetPlayerLeds = "SetPlayerLeds";

    /// <summary>
    /// Plays an audio file through the controller speaker, optionally driving the haptic
    /// actuators with the audio.
    /// </summary>
    public const string PlaySound = "PlaySound";

    /// <summary>
    /// Shows the controller's current battery charge on the lightbar: the lightbar is set
    /// to one of 10 level colors (see <see cref="SpecialActionEffect.BatteryColors"/>),
    /// low levels to high levels. Cannot be combined with light-changing effects.
    /// </summary>
    public const string ShowBatteryLevel = "ShowBatteryLevel";
}

/// <summary>
/// Touchpad gesture names supported by special actions. Stored as strings in settings so
/// the settings layer does not reference the controller protocol; the executing side
/// (<c>DualSenseClient.Controllers.SpecialActions.SpecialActionEngine</c>) interprets them.
/// </summary>
public static class TouchpadGestures
{
    /// <summary>
    /// No gesture: the action is triggered by its button combination instead.
    /// </summary>
    public const string None = "";

    /// <summary>
    /// A single finger swiped left across the touchpad.
    /// </summary>
    public const string SwipeLeft = "SwipeLeft";

    /// <summary>
    /// A single finger swiped right across the touchpad.
    /// </summary>
    public const string SwipeRight = "SwipeRight";

    /// <summary>
    /// A single finger swiped up across the touchpad.
    /// </summary>
    public const string SwipeUp = "SwipeUp";

    /// <summary>
    /// A single finger swiped down across the touchpad.
    /// </summary>
    public const string SwipeDown = "SwipeDown";
}

/// <summary>
/// Audio output device names for the play-sound effect. Stored as strings in settings so
/// the settings layer does not reference the controller protocol; the executing side
/// (<c>DualSenseClient.Controllers.SpecialActions</c>) interprets them.
/// </summary>
public static class SoundOutputDevices
{
    /// <summary>
    /// The controller's built-in speaker.
    /// </summary>
    public const string Speaker = "Speaker";

    /// <summary>
    /// A headset connected to the controller's headset jack.
    /// </summary>
    public const string Headset = "Headset";
}

/// <summary>
/// A color used by the show-battery-level effect for one of its 10 charge levels.
/// </summary>
public class BatteryLevelColor
{
    /// <summary>
    /// Gets or sets the red channel (0-255).
    /// </summary>
    [JsonPropertyName("red")]
    public byte Red { get; set; }

    /// <summary>
    /// Gets or sets the green channel (0-255).
    /// </summary>
    [JsonPropertyName("green")]
    public byte Green { get; set; }

    /// <summary>
    /// Gets or sets the blue channel (0-255).
    /// </summary>
    [JsonPropertyName("blue")]
    public byte Blue { get; set; }
}

/// <summary>
/// A single effect of a <see cref="SpecialAction"/>: one action type with its parameters.
/// A special action can carry several effects, at most one per type.
/// </summary>
public class SpecialActionEffect
{
    /// <summary>
    /// Gets or sets the effect to execute, one of <see cref="SpecialActionTypes"/>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = SpecialActionTypes.SetLightbarColor;

    /// <summary>
    /// Gets or sets whether the effect is active. A disabled effect stays in the list with
    /// its parameters (so toggling it back on restores them), but is not executed. Missing
    /// in older files, which behaves as enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the lightbar red channel (0-255), used by <see cref="SpecialActionTypes.SetLightbarColor"/>.
    /// </summary>
    [JsonPropertyName("red")]
    public byte Red { get; set; }

    /// <summary>
    /// Gets or sets the lightbar green channel (0-255), used by <see cref="SpecialActionTypes.SetLightbarColor"/>.
    /// </summary>
    [JsonPropertyName("green")]
    public byte Green { get; set; }

    /// <summary>
    /// Gets or sets the lightbar blue channel (0-255), used by <see cref="SpecialActionTypes.SetLightbarColor"/>.
    /// </summary>
    [JsonPropertyName("blue")]
    public byte Blue { get; set; } = 255;

    /// <summary>
    /// Gets or sets the player LED mask (bit 0 = LED 1, ... bit 4 = LED 5), used by
    /// <see cref="SpecialActionTypes.SetPlayerLeds"/>. Stored as a raw byte; the applying
    /// side casts it to the protocol mask.
    /// </summary>
    [JsonPropertyName("player_leds")]
    public byte PlayerLedMask { get; set; }

    /// <summary>
    /// Gets or sets the audio file played by <see cref="SpecialActionTypes.PlaySound"/>
    /// (mp3, wav, flac, ...).
    /// </summary>
    [JsonPropertyName("sound_path")]
    public string? SoundPath { get; set; }

    /// <summary>
    /// Gets or sets the controller speaker volume (0-255) for
    /// <see cref="SpecialActionTypes.PlaySound"/>.
    /// </summary>
    [JsonPropertyName("sound_volume")]
    public byte SoundVolume { get; set; } = 0x50;

    /// <summary>
    /// Gets or sets the audio output device for <see cref="SpecialActionTypes.PlaySound"/>,
    /// one of <see cref="SoundOutputDevices"/> (the controller speaker or a headset in the
    /// headset jack). Unknown values fall back to the speaker.
    /// </summary>
    [JsonPropertyName("sound_output")]
    public string SoundOutputDevice { get; set; } = SoundOutputDevices.Speaker;

    /// <summary>
    /// Gets or sets whether the sound drives the controller's haptic actuators.
    /// </summary>
    [JsonPropertyName("haptic_feedback")]
    public bool HapticFeedback { get; set; }

    /// <summary>
    /// Gets or sets the haptic vibration strength as a percentage (0-200).
    /// </summary>
    [JsonPropertyName("haptic_strength")]
    public int HapticStrength { get; set; } = 100;

    /// <summary>
    /// Gets or sets the lightbar colors for the 10 charge levels of
    /// <see cref="SpecialActionTypes.ShowBatteryLevel"/> (index 0 = lowest charge,
    /// index 9 = full). Missing entries fall back to <see cref="DefaultBatteryColors"/>.
    /// </summary>
    [JsonPropertyName("battery_colors")]
    public List<BatteryLevelColor>? BatteryColors { get; set; }

    /// <summary>
    /// Default lightbar colors for the 10 charge levels of
    /// <see cref="SpecialActionTypes.ShowBatteryLevel"/>: red at low charge fading
    /// through orange and yellow to green at full charge.
    /// </summary>
    public static readonly BatteryLevelColor[] DefaultBatteryColors =
    [
        new BatteryLevelColor
        {
            Red = 255,
            Green = 60,
            Blue = 60
        },
        new BatteryLevelColor
        {
            Red = 255,
            Green = 90,
            Blue = 50
        },
        new BatteryLevelColor
        {
            Red = 255,
            Green = 120,
            Blue = 40
        },
        new BatteryLevelColor
        {
            Red = 255,
            Green = 160,
            Blue = 30
        },
        new BatteryLevelColor
        {
            Red = 255,
            Green = 200,
            Blue = 30
        },
        new BatteryLevelColor
        {
            Red = 255,
            Green = 230,
            Blue = 40
        },
        new BatteryLevelColor
        {
            Red = 180,
            Green = 235,
            Blue = 50
        },
        new BatteryLevelColor
        {
            Red = 110,
            Green = 220,
            Blue = 60
        },
        new BatteryLevelColor
        {
            Red = 60,
            Green = 200,
            Blue = 80
        },
        new BatteryLevelColor
        {
            Red = 40,
            Green = 180,
            Blue = 110
        }
    ];

    /// <summary>
    /// Resolves the lightbar color for a charge level, falling back to
    /// <see cref="DefaultBatteryColors"/> when the effect has no custom colors or the
    /// level is out of range.
    /// </summary>
    /// <param name="level">The charge level (0-9, low to high).</param>
    public BatteryLevelColor GetBatteryColor(int level)
    {
        if (BatteryColors is { Count: > 0 } && level >= 0 && level < BatteryColors.Count)
        {
            return BatteryColors[level];
        }

        return DefaultBatteryColors[Math.Clamp(level, 0, DefaultBatteryColors.Length - 1)];
    }
}

/// <summary>
/// A user-defined special action: an exact combination of buttons held on the controller,
/// or a single-finger touchpad swipe (<see cref="TouchpadGesture"/>), that executes a set
/// of effects (e.g. disconnect the controller or change the lightbar color), re-arming
/// after the trigger is released.
/// </summary>
/// <remarks>
/// <para>
/// Buttons are stored as button names (e.g. <c>"L1"</c>, <c>"R1"</c>) matching
/// <c>DualSenseClient.Controllers.DualSense.Enum.ButtonType</c> names. They are stored as
/// strings so the settings layer does not reference the controller protocol, mirroring how
/// the player LED mask is stored as a raw byte and cast on the applying side.
/// </para>
/// <para>
/// Actions are global, but disabled until the user enables them for a specific controller.
/// <see cref="EnabledControllers"/> stores the identifiers of the controllers the action
/// is enabled for (Bluetooth MAC address, or HID device path as a fallback) — see
/// <see cref="SpecialActionService.GetControllerId"/>.
/// </para>
/// <para>
/// <see cref="Effects"/> holds the effects executed together when the action fires; an
/// effect type may appear at most once, so "change the lightbar and the player LEDs" is one
/// action with two effects.
/// </para>
/// </remarks>
public class SpecialAction
{
    /// <summary>
    /// Gets or sets the unique identifier used to reference this action from controller
    /// enablement toggles and deletion.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Special Action";

    /// <summary>
    /// Gets or sets the button names forming the combination (exact match; any extra
    /// button held disables it). Stored as <c>ButtonType</c> names. Ignored when
    /// <see cref="TouchpadGesture"/> is set.
    /// </summary>
    [JsonPropertyName("buttons")]
    public List<string> Buttons { get; set; } = [];

    /// <summary>
    /// Gets or sets the touchpad gesture that triggers the action, one of
    /// <see cref="TouchpadGestures"/>. When set, the gesture triggers the action instead
    /// of <see cref="Buttons"/>.
    /// </summary>
    [JsonPropertyName("gesture")]
    public string? TouchpadGesture { get; set; }

    /// <summary>
    /// Gets or sets the effects executed when the action fires, at most one per
    /// <see cref="SpecialActionEffect.Type"/>.
    /// </summary>
    [JsonPropertyName("effects")]
    public List<SpecialActionEffect> Effects { get; set; } = [];

    /// <summary>
    /// Gets or sets how long (in milliseconds) the exact combination must be held before
    /// the action fires. <c>0</c> fires as soon as the combination is complete.
    /// </summary>
    [JsonPropertyName("hold_time_ms")]
    public int HoldTimeMs { get; set; }

    /// <summary>
    /// Gets or sets whether the action's effects apply only while the combination is held:
    /// light effects revert to the bound profile and a sound effect stops playing on
    /// release, instead of staying applied. Ignored when no effect supports it.
    /// </summary>
    [JsonPropertyName("apply_while_held")]
    public bool ApplyWhileHeld { get; set; }

    /// <summary>
    /// Gets or sets how long (in milliseconds) the light effects (lightbar color, player
    /// LEDs, battery level) stay applied after the action fires, before the bound profile
    /// is restored automatically. <c>0</c> keeps them applied. Ignored for sound and
    /// disconnect effects, and when <see cref="ApplyWhileHeld"/> is set (the release
    /// reverts the effects then).
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the controllers this action is enabled for
    /// (see <see cref="SpecialActionService.GetControllerId"/>). An empty list means the
    /// action is defined but disabled everywhere.
    /// </summary>
    [JsonPropertyName("controllers")]
    public List<string> EnabledControllers { get; set; } = [];
}