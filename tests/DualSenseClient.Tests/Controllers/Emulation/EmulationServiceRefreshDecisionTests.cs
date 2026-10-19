using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Unit tests of <see cref="EmulationService.ShouldRecreateEntry"/>, the pure predicate behind
/// <see cref="EmulationService.Refresh"/>'s rebuild check: a running entry that still matches its
/// effective settings is left untouched, and any recorded creation input that differs (mode,
/// DualSense variant, DualShock 4 variant, engine) rebuilds it — in particular the engine-only
/// change, which no service-level test can drive because the rebuild on the libVIIPER side reaches
/// the native library. An entry without a virtual controller is always rebuilt. Audio source
/// changes are applied in place by the audio sync and are deliberately not part of this decision.
/// </summary>
[TestFixture]
public sealed class EmulationServiceRefreshDecisionTests
{
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationEngine.Viiper)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Edge, null, EmulationEngine.UsbIp)]
    [TestCase(EmulationMode.DualShock4, null, DualShock4Variant.V2, EmulationEngine.UsbIp)]
    public void ShouldRecreateEntry_IdenticalSettingsAndARunningVirtual_DoesNotRebuild(EmulationMode mode,
        DualSenseVariant? variant, DualShock4Variant? ds4Variant, EmulationEngine engine)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true, mode, variant, ds4Variant, engine, mode, variant, ds4Variant, engine),
            Is.False);
    }

    [TestCase(EmulationEngine.Viiper)]
    [TestCase(EmulationEngine.UsbIp)]
    public void ShouldRecreateEntry_NoVirtualController_Rebuilds(EmulationEngine engine)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(false,
            EmulationMode.DualSense, DualSenseVariant.Standard, null, engine,
            EmulationMode.DualSense, DualSenseVariant.Standard, null, engine), Is.True);
    }

    [TestCase(EmulationEngine.Viiper, EmulationEngine.UsbIp)]
    [TestCase(EmulationEngine.UsbIp, EmulationEngine.Viiper)]
    public void ShouldRecreateEntry_EngineOnlyChange_Rebuilds(EmulationEngine createdEngine, EmulationEngine engine)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualSense, DualSenseVariant.Standard, null, createdEngine,
            EmulationMode.DualSense, DualSenseVariant.Standard, null, engine), Is.True);
    }

    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationMode.DualShock4, null, DualShock4Variant.V2)]
    [TestCase(EmulationMode.DualShock4, null, DualShock4Variant.V2, EmulationMode.DualSense, DualSenseVariant.Standard, null)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationMode.Off, null, null)]
    [TestCase(EmulationMode.Xbox360, null, null, EmulationMode.DualSense, DualSenseVariant.Standard, null)]
    public void ShouldRecreateEntry_ModeChange_Rebuilds(EmulationMode createdMode, DualSenseVariant? createdVariant,
        DualShock4Variant? createdDs4Variant, EmulationMode mode, DualSenseVariant? variant, DualShock4Variant? ds4Variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            createdMode, createdVariant, createdDs4Variant, EmulationEngine.UsbIp,
            mode, variant, ds4Variant, EmulationEngine.UsbIp), Is.True);
    }

    [TestCase(DualSenseVariant.Standard, DualSenseVariant.Edge)]
    [TestCase(DualSenseVariant.Edge, DualSenseVariant.Standard)]
    public void ShouldRecreateEntry_DualSenseVariantChange_Rebuilds(DualSenseVariant createdVariant, DualSenseVariant variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualSense, createdVariant, null, EmulationEngine.Viiper,
            EmulationMode.DualSense, variant, null, EmulationEngine.Viiper), Is.True);
    }

    [TestCase(DualShock4Variant.V1, DualShock4Variant.V2)]
    [TestCase(DualShock4Variant.V2, DualShock4Variant.V1)]
    public void ShouldRecreateEntry_Ds4VariantChange_Rebuilds(DualShock4Variant createdDs4Variant, DualShock4Variant ds4Variant)
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualShock4, null, createdDs4Variant, EmulationEngine.UsbIp,
            EmulationMode.DualShock4, null, ds4Variant, EmulationEngine.UsbIp), Is.True);
    }

    [Test]
    public void ShouldRecreateEntry_CombinedChange_Rebuilds()
    {
        Assert.That(EmulationService.ShouldRecreateEntry(true,
            EmulationMode.DualSense, DualSenseVariant.Standard, null, EmulationEngine.Viiper,
            EmulationMode.DualShock4, null, DualShock4Variant.V1, EmulationEngine.UsbIp), Is.True);
    }
}