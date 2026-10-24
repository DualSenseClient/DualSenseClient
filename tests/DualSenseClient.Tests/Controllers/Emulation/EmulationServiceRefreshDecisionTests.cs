using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Unit tests of <see cref="EmulationService.ShouldRecreateEntry"/>, the pure predicate behind
/// <see cref="EmulationService.Refresh"/>'s rebuild check: a running entry that still matches its
/// effective settings is left untouched, and any recorded creation input that differs (mode,
/// DualSense variant, DualShock 4 variant) rebuilds it. An entry without a virtual controller is
/// always rebuilt. Audio source changes are applied in place by the audio sync and are
/// deliberately not part of this decision.
/// </summary>
[TestFixture]
public sealed class EmulationServiceRefreshDecisionTests
{
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Edge, null)]
    [TestCase(EmulationMode.DualShock4, null, DualShock4Variant.V2)]
    public void ShouldRecreateEntry_IdenticalSettingsAndARunningVirtual_DoesNotRebuild(EmulationMode mode,
        DualSenseVariant? variant, DualShock4Variant? ds4Variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true, mode, variant, ds4Variant, mode, variant, ds4Variant),
            Is.False);
    }

    [Test]
    public void ShouldRecreateEntry_NoVirtualController_Rebuilds()
    {
        Assert.That(EmulationService.ShouldRecreateEntry(false,
            EmulationMode.DualSense, DualSenseVariant.Standard, null,
            EmulationMode.DualSense, DualSenseVariant.Standard, null), Is.True);
    }

    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationMode.DualShock4, null, DualShock4Variant.V2)]
    [TestCase(EmulationMode.DualShock4, null, DualShock4Variant.V2, EmulationMode.DualSense, DualSenseVariant.Standard, null)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationMode.Off, null, null)]
    [TestCase(EmulationMode.Xbox360, null, null, EmulationMode.DualSense, DualSenseVariant.Standard, null)]
    public void ShouldRecreateEntry_ModeChange_Rebuilds(EmulationMode createdMode, DualSenseVariant? createdVariant,
        DualShock4Variant? createdDs4Variant, EmulationMode mode, DualSenseVariant? variant, DualShock4Variant? ds4Variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            createdMode, createdVariant, createdDs4Variant,
            mode, variant, ds4Variant), Is.True);
    }

    [TestCase(DualSenseVariant.Standard, DualSenseVariant.Edge)]
    [TestCase(DualSenseVariant.Edge, DualSenseVariant.Standard)]
    public void ShouldRecreateEntry_DualSenseVariantChange_Rebuilds(DualSenseVariant createdVariant, DualSenseVariant variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualSense, createdVariant, null,
            EmulationMode.DualSense, variant, null), Is.True);
    }

    [TestCase(DualShock4Variant.V1, DualShock4Variant.V2)]
    [TestCase(DualShock4Variant.V2, DualShock4Variant.V1)]
    public void ShouldRecreateEntry_Ds4VariantChange_Rebuilds(DualShock4Variant createdDs4Variant, DualShock4Variant ds4Variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualShock4, null, createdDs4Variant,
            EmulationMode.DualShock4, null, ds4Variant), Is.True);
    }

    [Test]
    public void ShouldRecreateEntry_CombinedChange_Rebuilds()
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualSense, DualSenseVariant.Standard, null,
            EmulationMode.DualShock4, null, DualShock4Variant.V1), Is.True);
    }
}