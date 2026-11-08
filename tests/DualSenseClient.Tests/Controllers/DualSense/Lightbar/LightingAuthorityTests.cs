using DualSenseClient.Controllers.DualSense.Lightbar;
using DualSenseClient.Controllers.DualSense.Output;

namespace DualSenseClient.Tests.Controllers.DualSense.Lightbar;

[TestFixture]
public sealed class LightingAuthorityTests
{
    [Test]
    public void Resolve_NoOwners_ReturnsGameOutputUnchanged()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        SetStateData gameOutput = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators | ValidFlags.AllowMuteLight,
            ValidFlag2 = ValidFlags.AllowColorFadeAnim,
            LightFadeAnimation = 0x02,
            LedRed = 10,
            LedGreen = 20,
            LedBlue = 30,
            PlayerLeds = PlayerLedMask.Led2 | PlayerLedMask.Led4,
            MuteLedMode = 1
        };

        authority.SubmitGameOutput(gameOutput);
        SetStateData resolved = authority.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.LedRed, Is.EqualTo((byte)10));
            Assert.That(resolved.LedGreen, Is.EqualTo((byte)20));
            Assert.That(resolved.LedBlue, Is.EqualTo((byte)30));
            Assert.That(resolved.PlayerLeds, Is.EqualTo(PlayerLedMask.Led2 | PlayerLedMask.Led4));
            Assert.That(resolved.MuteLedMode, Is.EqualTo((byte)1));
            Assert.That(resolved.LightFadeAnimation, Is.EqualTo((byte)0x02));
        });
    }

    [Test]
    public void Resolve_Profile_OverridesGameLightbarAndPlayerLeds()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        authority.SubmitGameOutput(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            LedRed = 10,
            LedGreen = 20,
            LedBlue = 30,
            PlayerLeds = PlayerLedMask.Led1
        });
        authority.Submit(LightingOwner.Profile, new LightingSnapshot(
            (100, 110, 120),
            PlayerLedMask.Led5,
            2,
            true));

        SetStateData resolved = authority.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.LedRed, Is.EqualTo((byte)100));
            Assert.That(resolved.LedGreen, Is.EqualTo((byte)110));
            Assert.That(resolved.LedBlue, Is.EqualTo((byte)120));
            Assert.That(resolved.PlayerLeds, Is.EqualTo(PlayerLedMask.Led5));
            Assert.That(resolved.MuteLedMode, Is.EqualTo((byte)2));
            Assert.That(authority.NeedsStateBlock, Is.True);
        });
    }

    [Test]
    public void Resolve_LightbarEffect_OverridesProfileLightbarButKeepsProfilePlayerLeds()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        authority.SubmitGameOutput(new SetStateData());
        authority.Submit(LightingOwner.Profile, new LightingSnapshot(
            (100, 110, 120),
            PlayerLedMask.Led5,
            2,
            true));
        authority.Submit(LightingOwner.LightbarEffect, new LightingSnapshot(
            (200, 210, 220)));

        SetStateData resolved = authority.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.LedRed, Is.EqualTo((byte)200));
            Assert.That(resolved.LedGreen, Is.EqualTo((byte)210));
            Assert.That(resolved.LedBlue, Is.EqualTo((byte)220));
            Assert.That(resolved.PlayerLeds, Is.EqualTo(PlayerLedMask.Led5));
            Assert.That(resolved.MuteLedMode, Is.EqualTo((byte)2));
            Assert.That(authority.NeedsStateBlock, Is.True);
        });
    }

    [Test]
    public void Resolve_SpecialAction_OverridesManualProfileAndEffect()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        authority.SubmitGameOutput(new SetStateData());
        authority.Submit(LightingOwner.Profile, new LightingSnapshot(
            (10, 11, 12),
            PlayerLedMask.Led1,
            0,
            true));
        authority.Submit(LightingOwner.LightbarEffect, new LightingSnapshot(
            (20, 21, 22),
            PlayerLedMask.Led2));
        authority.Submit(LightingOwner.Manual, new LightingSnapshot(
            (30, 31, 32),
            PlayerLedMask.Led3,
            2));
        authority.Submit(LightingOwner.SpecialAction, new LightingSnapshot(
            (40, 41, 42),
            PlayerLedMask.Led4));

        SetStateData resolved = authority.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.LedRed, Is.EqualTo((byte)40));
            Assert.That(resolved.LedGreen, Is.EqualTo((byte)41));
            Assert.That(resolved.LedBlue, Is.EqualTo((byte)42));
            Assert.That(resolved.PlayerLeds, Is.EqualTo(PlayerLedMask.Led4));
            Assert.That(resolved.MuteLedMode, Is.EqualTo((byte)2));
            Assert.That(authority.NeedsStateBlock, Is.True);
        });
    }

    [Test]
    public void Commit_SendsResolvedPayload()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        authority.SubmitGameOutput(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor,
            LedRed = 10,
            LedGreen = 20,
            LedBlue = 30
        });
        authority.Submit(LightingOwner.Profile, new LightingSnapshot(
            (100, 110, 120),
            ForceOverGame: true));

        authority.Commit();

        Assert.That(sink.Sent, Has.Count.EqualTo(1));
        Assert.That(sink.Sent[0].LedRed, Is.EqualTo((byte)100));
        Assert.That(sink.Sent[0].LedGreen, Is.EqualTo((byte)110));
        Assert.That(sink.Sent[0].LedBlue, Is.EqualTo((byte)120));
    }

    [Test]
    public void ClearProfile_StopsProfileLightRestore()
    {
        RecordingLightSink sink = new RecordingLightSink();
        LightingAuthority authority = new LightingAuthority(sink);
        authority.SubmitGameOutput(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            LedRed = 10,
            LedGreen = 20,
            LedBlue = 30,
            PlayerLeds = PlayerLedMask.Led1
        });
        authority.Submit(LightingOwner.Profile, new LightingSnapshot(
            (100, 110, 120),
            PlayerLedMask.Led5,
            2,
            true));

        authority.Clear(LightingOwner.Profile);
        SetStateData resolved = authority.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.LedRed, Is.EqualTo((byte)10));
            Assert.That(resolved.LedGreen, Is.EqualTo((byte)20));
            Assert.That(resolved.LedBlue, Is.EqualTo((byte)30));
            Assert.That(resolved.PlayerLeds, Is.EqualTo(PlayerLedMask.Led1));
            Assert.That(resolved.MuteLedMode, Is.EqualTo((byte)0));
            Assert.That(authority.NeedsStateBlock, Is.False);
        });
    }

    private sealed class RecordingLightSink : ILightSink
    {
        public List<SetStateData> Sent { get; } = new List<SetStateData>();

        public void Send(SetStateData payload) => Sent.Add(payload);
    }
}