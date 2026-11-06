using DualSenseClient.Controllers.DualSense.Output;

namespace DualSenseClient.Controllers.DualSense.Lightbar;

/// <summary>
/// The output transport the lighting authority writes resolved payloads to.
/// </summary>
public interface ILightSink
{
    /// <summary>
    /// Sends one resolved output payload to the controller.
    /// </summary>
    /// <param name="payload">The resolved output state.</param>
    void Send(SetStateData payload);
}

/// <summary>
/// The owner of one lighting channel snapshot.
/// </summary>
public enum LightingOwner
{
    /// <summary>
    /// The profile's static light values.
    /// </summary>
    Profile,

    /// <summary>
    /// The animated lightbar effect.
    /// </summary>
    LightbarEffect,

    /// <summary>
    /// A direct light edit from the UI.
    /// </summary>
    Manual,

    /// <summary>
    /// A special action's temporary light override.
    /// </summary>
    SpecialAction
}

/// <summary>
/// One owner's contribution to the resolved lighting state. A <c>null</c> channel means
/// the owner does not drive that channel.
/// </summary>
/// <param name="LightbarColor">Lightbar RGB, or <c>null</c> when the owner leaves it alone.</param>
/// <param name="PlayerLeds">Player LED mask, or <c>null</c> when the owner leaves it alone.</param>
/// <param name="MuteLedMode">Microphone LED mode, or <c>null</c> when the owner leaves it alone.</param>
/// <param name="ForceOverGame">Whether a profile snapshot overrides lighting passthrough.</param>
public readonly record struct LightingSnapshot(
    (byte Red, byte Green, byte Blue)? LightbarColor = null,
    PlayerLedMask? PlayerLeds = null,
    byte? MuteLedMode = null,
    bool ForceOverGame = false);

/// <summary>
/// Owns the precedence between the game's output, profile lights, animated effects,
/// manual light edits and special-action overrides for one controller. Callers submit
/// snapshots; the authority resolves them into one output state.
/// </summary>
public sealed class LightingAuthority
{
    private readonly ILightSink _sink;
    private readonly Lock _lock = new Lock();
    private SetStateData _gameOutput;
    private LightingSnapshot? _profile;
    private bool _profileMutePending;
    private LightingSnapshot? _lightbarEffect;
    private LightingSnapshot? _manual;
    private LightingSnapshot? _specialAction;
    private bool _lightbarPassthroughEnabled;
    private bool _playerLedPassthroughEnabled;

    /// <summary>
    /// Creates an authority that writes resolved payloads through the given sink.
    /// </summary>
    /// <param name="sink">The output transport receiving resolved payloads.</param>
    public LightingAuthority(ILightSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// Replaces the game output payload every override is layered over.
    /// </summary>
    /// <param name="payload">The host's latest output state.</param>
    public void SubmitGameOutput(SetStateData payload)
    {
        SetStateData clone = Clone(payload);
        lock (_lock)
        {
            _gameOutput = clone;
        }
    }

    /// <summary>
    /// Gets or sets whether the game owns the lightbar. While set, profile and effect
    /// lightbar values are ignored unless the profile forces them.
    /// </summary>
    public bool LightbarPassthroughEnabled
    {
        get
        {
            lock (_lock)
            {
                return _lightbarPassthroughEnabled;
            }
        }
        set
        {
            lock (_lock)
            {
                _lightbarPassthroughEnabled = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the game owns the player LEDs. While set, profile and effect
    /// player LED values are ignored unless the profile forces them.
    /// </summary>
    public bool PlayerLedPassthroughEnabled
    {
        get
        {
            lock (_lock)
            {
                return _playerLedPassthroughEnabled;
            }
        }
        set
        {
            lock (_lock)
            {
                _playerLedPassthroughEnabled = value;
            }
        }
    }

    /// <summary>
    /// Gets whether the profile forces its lights over the game's output.
    /// </summary>
    public bool ProfileForcesLights
    {
        get
        {
            lock (_lock)
            {
                return _profile is { ForceOverGame: true };
            }
        }
    }

    private bool CanOwnLightbar
    {
        get
        {
            return _profile?.ForceOverGame == true || !_lightbarPassthroughEnabled;
        }
    }

    private bool CanOwnPlayerLeds
    {
        get
        {
            return _profile?.ForceOverGame == true || !_playerLedPassthroughEnabled;
        }
    }

    /// <summary>
    /// Whether the next Bluetooth audio report must carry the resolved output-state block
    /// because a non-game owner is driving at least one lighting channel.
    /// </summary>
    public bool NeedsStateBlock
    {
        get
        {
            lock (_lock)
            {
                bool profileLightbar = _profile?.LightbarColor is not null && CanOwnLightbar;
                bool profilePlayerLeds = _profile?.PlayerLeds is not null && CanOwnPlayerLeds;
                bool profileMuteLed = _profile?.MuteLedMode is not null
                                      && (_profile.Value.ForceOverGame || _profileMutePending);
                bool effectLightbar = _lightbarEffect?.LightbarColor is not null && CanOwnLightbar;
                bool effectPlayerLeds = _lightbarEffect?.PlayerLeds is not null && CanOwnPlayerLeds;
                bool manual = _manual?.LightbarColor is not null || _manual?.PlayerLeds is not null
                                                                 || _manual?.MuteLedMode is not null;
                bool specialAction = _specialAction?.LightbarColor is not null || _specialAction?.PlayerLeds is not null
                                                                               || _specialAction?.MuteLedMode is not null;
                return profileLightbar || profilePlayerLeds || profileMuteLed || effectLightbar || effectPlayerLeds
                       || manual || specialAction;
            }
        }
    }

    /// <summary>
    /// Stores one owner's latest snapshot, replacing the owner's previous contribution.
    /// </summary>
    /// <param name="owner">The owner submitting the snapshot.</param>
    /// <param name="snapshot">The owner's latest lighting state.</param>
    public void Submit(LightingOwner owner, LightingSnapshot snapshot)
    {
        lock (_lock)
        {
            switch (owner)
            {
                case LightingOwner.Profile:
                    _profile = snapshot;
                    _profileMutePending = snapshot.MuteLedMode is not null && !snapshot.ForceOverGame;
                    break;
                case LightingOwner.LightbarEffect:
                    _lightbarEffect = snapshot;
                    break;
                case LightingOwner.Manual:
                    _manual = snapshot;
                    break;
                case LightingOwner.SpecialAction:
                    _specialAction = snapshot;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(owner), owner, "The owner is not supported yet.");
            }
        }
    }

    /// <summary>
    /// Removes one owner's contribution, returning that owner's channels to the next
    /// lower-precedence owner.
    /// </summary>
    /// <param name="owner">The owner whose contribution is removed.</param>
    public void Clear(LightingOwner owner)
    {
        lock (_lock)
        {
            switch (owner)
            {
                case LightingOwner.Profile:
                    _profile = null;
                    _profileMutePending = false;
                    break;
                case LightingOwner.LightbarEffect:
                    _lightbarEffect = null;
                    break;
                case LightingOwner.Manual:
                    _manual = null;
                    break;
                case LightingOwner.SpecialAction:
                    _specialAction = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(owner), owner, "The owner is not supported.");
            }
        }
    }

    /// <summary>
    /// Resolves the current state and sends it to the controller.
    /// </summary>
    public void Commit()
    {
        SetStateData resolved;
        lock (_lock)
        {
            resolved = ResolveCore(_gameOutput);
            _profileMutePending = false;
        }

        // Send outside the lock so an in-flight audio report can publish its resolved
        // snapshot without waiting for the HID write.
        _sink.Send(resolved);
    }

    /// <summary>
    /// Resolves the stored game output and every submitted override into one output state.
    /// </summary>
    /// <returns>The resolved output state.</returns>
    public SetStateData Resolve()
    {
        lock (_lock)
        {
            return ResolveCore(_gameOutput);
        }
    }

    /// <summary>
    /// Layers every submitted override over the given output payload without changing
    /// the stored game output. Used by the audio lane, whose per-tick payload carries
    /// audio routing and volume that must ride through.
    /// </summary>
    /// <param name="gameOutput">The base payload to resolve over.</param>
    /// <returns>The resolved output state.</returns>
    public SetStateData Resolve(SetStateData gameOutput)
    {
        lock (_lock)
        {
            return ResolveCore(gameOutput);
        }
    }

    private SetStateData ResolveCore(SetStateData gameOutput)
    {
        SetStateData resolved = Clone(gameOutput);
        if (_profile is { } profile)
        {
            resolved = ApplyProfile(resolved, profile);
        }

        if (_lightbarEffect is { } effect)
        {
            resolved = ApplyLightbarEffect(resolved, effect);
        }

        if (_manual is { } manual)
        {
            resolved = ApplyOverride(resolved, manual);
        }

        if (_specialAction is { } specialAction)
        {
            resolved = ApplyOverride(resolved, specialAction);
        }

        return resolved;
    }

    private static SetStateData ApplyOverride(SetStateData state, LightingSnapshot snapshot)
    {
        if (snapshot.LightbarColor is { } color)
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowLedColor,
                ValidFlag2 = state.ValidFlag2 | ValidFlags.AllowColorFadeAnim,
                LightFadeAnimation = 0x02,
                LedRed = color.Red,
                LedGreen = color.Green,
                LedBlue = color.Blue
            };
        }

        if (snapshot.PlayerLeds is { } playerLeds)
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowPlayerIndicators,
                PlayerLeds = playerLeds
            };
        }

        if (snapshot.MuteLedMode is { } muteLed)
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowMuteLight,
                MuteLedMode = muteLed
            };
        }

        return state;
    }

    private SetStateData ApplyLightbarEffect(SetStateData state, LightingSnapshot effect)
    {
        if (effect.LightbarColor is { } color && CanOwnLightbar)
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowLedColor,
                ValidFlag2 = state.ValidFlag2 | ValidFlags.AllowColorFadeAnim | ValidFlags.AllowBrightnessChange,
                LightFadeAnimation = 0x02,
                LightBrightness = 0,
                LedRed = color.Red,
                LedGreen = color.Green,
                LedBlue = color.Blue
            };
        }

        if (effect.PlayerLeds is { } playerLeds && CanOwnPlayerLeds)
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowPlayerIndicators,
                PlayerLeds = playerLeds
            };
        }

        return state;
    }

    private SetStateData ApplyProfile(SetStateData state, LightingSnapshot profile)
    {
        if (profile.LightbarColor is { } color && (profile.ForceOverGame || !LightbarPassthroughEnabled))
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowLedColor,
                ValidFlag2 = state.ValidFlag2 | ValidFlags.AllowColorFadeAnim | ValidFlags.AllowBrightnessChange,
                LightFadeAnimation = 0x02,
                LightBrightness = 0,
                LedRed = color.Red,
                LedGreen = color.Green,
                LedBlue = color.Blue
            };
        }

        if (profile.PlayerLeds is { } playerLeds && (profile.ForceOverGame || !PlayerLedPassthroughEnabled))
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowPlayerIndicators,
                PlayerLeds = playerLeds
            };
        }

        if (profile.MuteLedMode is { } muteLed && (profile.ForceOverGame || _profileMutePending))
        {
            state = state with
            {
                ValidFlag1 = state.ValidFlag1 | ValidFlags.AllowMuteLight,
                MuteLedMode = muteLed
            };
        }

        return state;
    }

    private static SetStateData Clone(SetStateData payload)
    {
        byte[] raw = new byte[SetStateData.PayloadSize];
        if (!payload.AsSpan().IsEmpty)
        {
            payload.CopyTo(raw, 0);
        }

        return new SetStateData(raw, 0);
    }
}