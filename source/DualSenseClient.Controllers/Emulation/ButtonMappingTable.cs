using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// An immutable, resolved set of button remapping rules for one virtual controller:
/// single-button mappings (built-in defaults overlaid with user overrides) plus multi-button
/// combo rules. <see cref="Evaluate"/> translates one input report statelessly.
/// </summary>
public sealed class ButtonMappingTable
{
    /// <summary>
    /// Parses a mode-specific target name into its resolved form, or <c>null</c> when unknown.
    /// </summary>
    public delegate ResolvedMappingTarget? TryParseTarget(string name);

    /// <summary>
    /// One combo rule: a set of source buttons mapped jointly to one target while all keys
    /// are held together.
    /// </summary>
    internal sealed class ComboRule(HashSet<ButtonType> keys, ResolvedMappingTarget target, bool suppressSolos)
    {
        public HashSet<ButtonType> Keys { get; } = keys;

        public ResolvedMappingTarget Target { get; } = target;

        public bool SuppressSolos { get; } = suppressSolos;
    }

    /// <summary>
    /// Effective single-button mapping per physical source (defaults overlaid with user
    /// overrides); sources absent from the dictionary send nothing.
    /// </summary>
    private readonly Dictionary<ButtonType, ResolvedMappingTarget> _solos;

    /// <summary>
    /// Multi-button combo rules.
    /// </summary>
    private readonly List<ComboRule> _combos;

    /// <summary>
    /// Creates a table from pre-resolved parts. Use <see cref="Resolve"/> to build from settings.
    /// </summary>
    internal ButtonMappingTable(IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> solos, IEnumerable<ComboRule> combos)
    {
        _solos = new Dictionary<ButtonType, ResolvedMappingTarget>(solos);
        _combos = combos.ToList();
    }

    /// <summary>
    /// All mappable physical buttons, in <see cref="ButtonType"/> declaration order.
    /// </summary>
    public static IReadOnlyList<ButtonType> Sources { get; } = Enum.GetValues<ButtonType>();

    /// <summary>
    /// Whether the given physical button is pressed in the input state. The analog triggers
    /// count as pressed when their digital click bit is set.
    /// </summary>
    public static bool IsPressed(InputState input, ButtonType button) => button switch
    {
        ButtonType.Cross => input.Cross,
        ButtonType.Circle => input.Circle,
        ButtonType.Square => input.Square,
        ButtonType.Triangle => input.Triangle,
        ButtonType.DPadUp => input.DPadUp,
        ButtonType.DPadDown => input.DPadDown,
        ButtonType.DPadLeft => input.DPadLeft,
        ButtonType.DPadRight => input.DPadRight,
        ButtonType.L1 => input.L1,
        ButtonType.R1 => input.R1,
        ButtonType.L2 => input.L2Click,
        ButtonType.R2 => input.R2Click,
        ButtonType.L3 => input.L3,
        ButtonType.R3 => input.R3,
        ButtonType.Create => input.Create,
        ButtonType.Options => input.Options,
        ButtonType.PS => input.PS,
        ButtonType.TouchPad => input.TouchPad,
        ButtonType.Mute => input.Mute,
        ButtonType.Edge_LeftFunction => input.EdgeFunctionLeft,
        ButtonType.Edge_RightFunction => input.EdgeFunctionRight,
        ButtonType.Edge_LeftPaddle => input.EdgePaddleLeft,
        ButtonType.Edge_RightPaddle => input.EdgePaddleRight,
        _ => false
    };

    /// <summary>
    /// Parses a physical source button name (a <c>ButtonType</c> member name).
    /// </summary>
    public static bool TryParseSource(string name, out ButtonType button)
    {
        if (Enum.TryParse(name, ignoreCase: true, out button) && Enum.IsDefined(button))
        {
            return true;
        }

        button = default;
        return false;
    }

    /// <summary>
    /// Builds a table from built-in defaults overlaid with user mapping entries: single-key
    /// entries replace their source's default target, multi-key entries add combo rules
    /// (replacing an existing rule with the same key set). Entries with unknown source or
    /// target names are skipped and reported through <paramref name="logWarning"/>.
    /// </summary>
    public static ButtonMappingTable Resolve(
        IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> defaults,
        IEnumerable<ButtonMappingEntry>? entries,
        TryParseTarget parseTarget,
        Action<string>? logWarning)
    {
        Dictionary<ButtonType, ResolvedMappingTarget> solos = new(defaults);
        List<ComboRule> combos = [];

        foreach (ButtonMappingEntry entry in entries ?? [])
        {
            HashSet<ButtonType> keys = [];
            bool valid = true;
            foreach (string key in entry.Keys ?? [])
            {
                if (TryParseSource(key, out ButtonType parsed))
                {
                    keys.Add(parsed);
                    continue;
                }

                logWarning?.Invoke($"Unknown source button '{key}' in a button mapping; ignoring the entry");
                valid = false;
                break;
            }

            if (!valid || keys.Count == 0)
            {
                continue;
            }

            string targetName = entry.Target?.Trim() ?? string.Empty;
            ResolvedMappingTarget target;
            if (targetName.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                target = ResolvedMappingTarget.None;
            }
            else if (parseTarget(targetName) is { } parsed)
            {
                target = parsed;
                if (IsClickOutput(entry.TargetOutput))
                {
                    target = target with
                    {
                        Output = MappingTriggerOutput.ClickOnly
                    };
                }
            }
            else
            {
                logWarning?.Invoke($"Unknown target '{entry.Target}' in a button mapping; ignoring the entry");
                continue;
            }

            if (keys.Count == 1)
            {
                solos[keys.First()] = target;
            }
            else
            {
                ComboRule rule = new ComboRule(keys, target, entry.SuppressSolos);
                int existing = combos.FindIndex(candidate => candidate.Keys.SetEquals(keys));
                if (existing >= 0)
                {
                    combos[existing] = rule;
                }
                else
                {
                    combos.Add(rule);
                }
            }
        }

        return new ButtonMappingTable(solos, combos);
    }

    /// <summary>
    /// Translates one input report into the virtual controller's button result. Evaluation is
    /// stateless: active combos mute their member buttons' own single-button outputs for this
    /// report only, and released inputs stop contributing immediately.
    /// </summary>
    public MappedInputResult Evaluate(InputState input)
    {
        HashSet<ButtonType>? pressed = null;
        foreach (ButtonType source in Sources)
        {
            if (IsPressed(input, source))
            {
                (pressed ??= []).Add(source);
            }
        }

        HashSet<ButtonType>? muted = null;
        List<(HashSet<ButtonType> Keys, ResolvedMappingTarget Target)> activeCombos = [];
        foreach (ComboRule combo in _combos)
        {
            if (pressed is null || !combo.Keys.IsSubsetOf(pressed))
            {
                continue;
            }

            if (combo.SuppressSolos && combo.Keys.Count > 1)
            {
                (muted ??= []).UnionWith(combo.Keys);
            }

            activeCombos.Add((combo.Keys, combo.Target));
        }

        ulong buttons = 0;
        VirtualDPad dpad = VirtualDPad.None;
        byte leftTrigger = 0;
        byte rightTrigger = 0;

        // The physical triggers' analog bytes mirror into the virtual triggers while their
        // own solo mapping still requests mirroring (the built-in default). This runs before
        // the other contributions so custom full-pull rules override the mirrored byte.
        MirrorTrigger(input, muted, ButtonType.L2, input.L2, input.L2Click, ref buttons, ref leftTrigger);
        MirrorTrigger(input, muted, ButtonType.R2, input.R2, input.R2Click, ref buttons, ref rightTrigger);

        foreach (KeyValuePair<ButtonType, ResolvedMappingTarget> solo in _solos)
        {
            if ((pressed is null || !pressed.Contains(solo.Key)) || (muted is not null && muted.Contains(solo.Key)))
            {
                continue;
            }

            Apply(solo.Value, ref buttons, ref dpad, ref leftTrigger, ref rightTrigger);
        }

        foreach ((_, ResolvedMappingTarget target) in activeCombos)
        {
            Apply(target, ref buttons, ref dpad, ref leftTrigger, ref rightTrigger);
        }

        return new MappedInputResult
        {
            Buttons = buttons,
            DPad = dpad,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger
        };
    }

    /// <summary>
    /// The effective target of a single physical source button, or <c>null</c> when it sends
    /// nothing. Surfaced for the remapping UI.
    /// </summary>
    public ResolvedMappingTarget? GetSoloTarget(ButtonType source)
        => _solos.TryGetValue(source, out ResolvedMappingTarget target) && !target.IsNone ? target : null;

    /// <summary>
    /// Applies a resolved target's flag, D-pad, and trigger contributions to the accumulators.
    /// </summary>
    private void Apply(ResolvedMappingTarget target, ref ulong buttons, ref VirtualDPad dpad, ref byte leftTrigger, ref byte rightTrigger)
    {
        buttons |= target.ButtonFlags;
        dpad |= target.DPad;
        if (target.Trigger == MappableTriggerSide.Left && target.Output == MappingTriggerOutput.FullPull)
        {
            leftTrigger = 255;
        }
        else if (target.Trigger == MappableTriggerSide.Right && target.Output == MappingTriggerOutput.FullPull)
        {
            rightTrigger = 255;
        }
    }

    /// <summary>
    /// Mirrors the physical analog trigger byte into the virtual trigger while the source's
    /// solo mapping still requests mirroring, setting the device's trigger click flag on
    /// digital presses where the target carries one.
    /// </summary>
    private void MirrorTrigger(
        InputState input,
        HashSet<ButtonType>? muted,
        ButtonType source,
        byte analog,
        bool clicked,
        ref ulong buttons,
        ref byte trigger)
    {
        if (!_solos.TryGetValue(source, out ResolvedMappingTarget solo)
            || solo.Trigger == MappableTriggerSide.None
            || solo.Output != MappingTriggerOutput.Mirror
            || (muted is not null && muted.Contains(source)))
        {
            return;
        }

        trigger = analog;
        if (clicked)
        {
            buttons |= solo.ButtonFlags;
        }
    }

    /// <summary>
    /// Whether a mapping entry requests click-only output for a trigger target.
    /// </summary>
    private static bool IsClickOutput(string? output) => output?.Trim().Equals("click", StringComparison.OrdinalIgnoreCase) == true;
}