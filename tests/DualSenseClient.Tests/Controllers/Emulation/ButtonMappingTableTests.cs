using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.Tests.Controllers.Emulation;

[TestFixture]
public sealed class ButtonMappingTableTests
{
    private static InputState State(byte stickX = 128, byte stickY = 128, byte l2 = 0, byte r2 = 0, byte byte7 = 0x08, byte byte8 = 0, byte byte9 = 0)
        => new InputState([
            stickX, stickY, 128, 128, l2, r2, 0, byte7, byte8, byte9
        ], 0);

    private static ButtonMappingEntry Entry(string target, params string[] keys) => new ButtonMappingEntry
    {
        Keys = [.. keys],
        Target = target,
        TargetOutput = null,
        SuppressSolos = true
    };

    // ── Solo remapping ──────────────────────────────────────────

    [Test]
    public void Xbox360_CustomEntry_ReplacesDefaultTarget()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("Y", "Cross")]);
        MappedInputResult result = table.Evaluate(State(byte7: 0x08 | 0x20)); // Cross pressed
        Assert.That((Xbox360Buttons)(uint)result.Buttons, Is.EqualTo(Xbox360Buttons.Y));
    }

    [Test]
    public void Xbox360_NoneTarget_DisablesTheSource()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("None", "Create")]);
        MappedInputResult result = table.Evaluate(State(byte8: 0x10)); // Create pressed
        Assert.That(result.Buttons, Is.EqualTo(0u));
    }

    [Test]
    public void Xbox360_UnmentionedSources_KeepDefaults()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("None", "Mute")]);
        MappedInputResult result = table.Evaluate(State(byte8: 0x10, byte9: 0x04)); // Create + Mute pressed
        Assert.That((Xbox360Buttons)(uint)result.Buttons, Is.EqualTo(Xbox360Buttons.Back));
    }

    [Test]
    public void Xbox360_DPadDirection_CanBeRemappedOntoAnyButton()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("DPadUp", "Cross")]);
        MappedInputResult result = table.Evaluate(State(byte7: 0x08 | 0x20)); // Cross pressed
        Assert.That((Xbox360Buttons)(uint)result.Buttons, Is.EqualTo(Xbox360Buttons.DPadUp));
    }

    // ── Analog trigger handling ─────────────────────────────────

    [Test]
    public void Xbox360_Default_MirrorsAnalogTriggers()
    {
        MappedInputResult result = VirtualInputMapper.Xbox360Table(null).Evaluate(State(l2: 120, r2: 60));
        Assert.That(result.LeftTrigger, Is.EqualTo(120));
        Assert.That(result.RightTrigger, Is.EqualTo(60));
    }

    [Test]
    public void Xbox360_TriggerTarget_ForcesFullPullWhilePressed()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("LeftTrigger", "Cross")]);
        MappedInputResult pressed = table.Evaluate(State(byte7: 0x08 | 0x20));
        Assert.That(pressed.LeftTrigger, Is.EqualTo(255));

        MappedInputResult released = table.Evaluate(State());
        Assert.That(released.LeftTrigger, Is.EqualTo(0));
    }

    [Test]
    public void Xbox360_RemappingPhysicalTriggerAway_StopsMirroring()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("A", "L2")]);
        MappedInputResult clicked = table.Evaluate(State(byte8: 0x04, l2: 200)); // L2 fully clicked
        Assert.That(clicked.LeftTrigger, Is.EqualTo(0));
        Assert.That((Xbox360Buttons)(uint)clicked.Buttons, Is.EqualTo(Xbox360Buttons.A));

        MappedInputResult partial = table.Evaluate(State(l2: 100));
        Assert.That(partial.Buttons, Is.EqualTo(0u));
    }

    [Test]
    public void DualShock4_TriggerTarget_OutputModes()
    {
        InputState cross = State(byte7: 0x08 | 0x20);

        ButtonMappingTable fullPull = VirtualInputMapper.DualShock4Table([Entry("L2", "Cross")]);
        MappedInputResult full = fullPull.Evaluate(cross);
        Assert.That(full.Buttons & (ulong)DualShock4Buttons.L2, Is.Not.EqualTo(0));
        Assert.That(full.LeftTrigger, Is.EqualTo(255));

        ButtonMappingEntry clickEntry = Entry("L2", "Cross");
        clickEntry.TargetOutput = "click";
        ButtonMappingTable clickOnly = VirtualInputMapper.DualShock4Table([clickEntry]);
        MappedInputResult click = clickOnly.Evaluate(cross);
        Assert.That(click.Buttons & (ulong)DualShock4Buttons.L2, Is.Not.EqualTo(0));
        Assert.That(click.LeftTrigger, Is.EqualTo(0));
    }

    [Test]
    public void DualShock4_Default_ClickFlagAndAnalogMirror()
    {
        MappedInputResult result = VirtualInputMapper.DualShock4Table(null).Evaluate(State(byte8: 0x04, l2: 90));
        Assert.That(result.Buttons & (ulong)DualShock4Buttons.L2, Is.Not.EqualTo(0));
        Assert.That(result.LeftTrigger, Is.EqualTo(90));
    }

    [Test]
    public void DualSense_Default_MapsEdgeControlsOneToOne()
    {
        MappedInputResult result = VirtualInputMapper.DualSenseTable(null).Evaluate(State(byte9: 0x40 | 0x80, l2: 5));
        Assert.That(result.Buttons, Is.GreaterThan(0u));
    }

    // ── Combos ──────────────────────────────────────────────────

    [Test]
    public void Combo_FiresOnlyWhileAllKeysAreHeld_AndSuppressesSolosByDefault()
    {
        List<ButtonMappingEntry> entries =
        [
            Entry("LeftThumb", "Create", "Options") // Create+Options -> LS click
        ];
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table(entries);

        MappedInputResult both = table.Evaluate(State(byte8: 0x10 | 0x20));
        Assert.That((Xbox360Buttons)(uint)both.Buttons, Is.EqualTo(Xbox360Buttons.LeftThumb));

        MappedInputResult soloCreate = table.Evaluate(State(byte8: 0x10));
        Assert.That((Xbox360Buttons)(uint)soloCreate.Buttons, Is.EqualTo(Xbox360Buttons.Back));

        MappedInputResult released = table.Evaluate(State());
        Assert.That(released.Buttons, Is.EqualTo(0u));
    }

    [Test]
    public void Combo_SuppressionOff_KeepsSoloOutputs()
    {
        ButtonMappingEntry additive = Entry("LeftThumb", "Create", "Options");
        additive.SuppressSolos = false;
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([additive]);

        MappedInputResult result = table.Evaluate(State(byte8: 0x10 | 0x20));
        Assert.That((Xbox360Buttons)(uint)result.Buttons,
            Is.EqualTo(Xbox360Buttons.Back | Xbox360Buttons.Start | Xbox360Buttons.LeftThumb));
    }

    [Test]
    public void Combo_CanTargetATrigger()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("RightTrigger", "Create", "Options")]);

        MappedInputResult held = table.Evaluate(State(byte8: 0x10 | 0x20, r2: 30));
        Assert.That(held.RightTrigger, Is.EqualTo(255));

        MappedInputResult partialSolo = table.Evaluate(State(r2: 30));
        Assert.That(partialSolo.RightTrigger, Is.EqualTo(30));
    }

    // ── Parsing robustness ──────────────────────────────────────

    [Test]
    public void Resolve_UnknownNames_AreIgnoredWithoutThrowing()
    {
        List<ButtonMappingEntry> entries =
        [
            new ButtonMappingEntry
            {
                Keys = ["NotAButton"],
                Target = "A"
            },
            new ButtonMappingEntry
            {
                Keys = ["Cross"],
                Target = "AlsoNotATarget"
            },
            Entry("Y", "Circle")
        ];
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table(entries);

        MappedInputResult result = table.Evaluate(State(byte7: 0x08 | 0x40)); // Circle pressed
        Assert.That((Xbox360Buttons)(uint)result.Buttons, Is.EqualTo(Xbox360Buttons.Y));
    }

    [Test]
    public void TryParseSource_IsCaseInsensitive()
    {
        Assert.That(ButtonMappingTable.TryParseSource("cross", out ButtonType parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(ButtonType.Cross));
        Assert.That(ButtonMappingTable.TryParseSource("Nope", out _), Is.False);
    }

    [Test]
    public void GetSoloTarget_ReturnsEffectiveSoloOrNothing()
    {
        ButtonMappingTable table = VirtualInputMapper.Xbox360Table([Entry("None", "Create")]);
        Assert.That(table.GetSoloTarget(ButtonType.Cross), Is.Not.Null);
        Assert.That(table.GetSoloTarget(ButtonType.Create), Is.Null);
    }
}