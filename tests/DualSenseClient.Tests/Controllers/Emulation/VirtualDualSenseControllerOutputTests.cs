using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Tests.Controllers.Emulation;

public class VirtualDualSenseControllerOutputTests
{
    private static DSOutputState Output(byte[] payload)
    {
        Assert.That(payload.Length, Is.EqualTo(47));
        byte[] right = new byte[11];
        byte[] left = new byte[11];
        Buffer.BlockCopy(payload, 10, right, 0, 11);
        Buffer.BlockCopy(payload, 21, left, 0, 11);
        return new DSOutputState
        {
            Flags0 = payload[0],
            Flags1 = payload[1],
            RumbleSmall = payload[2],
            RumbleLarge = payload[3],
            VolumeHeadphones = payload[4],
            VolumeSpeaker = payload[5],
            VolumeMic = payload[6],
            AudioControl = payload[7],
            MuteLightMode = payload[8],
            MuteControl = payload[9],
            TriggerRight = right,
            TriggerLeft = left,
            HostTimestamp = (uint)(payload[32] | (payload[33] << 8) | (payload[34] << 16) | (payload[35] << 24)),
            MotorPower = payload[36],
            AudioControl2 = payload[37],
            Flags3 = payload[38],
            HapticFilter = payload[39],
            UnkByte = payload[40],
            LightFade = payload[41],
            LightBrightness = payload[42],
            PlayerLeds = payload[43],
            LedRed = payload[44],
            LedGreen = payload[45],
            LedBlue = payload[46]
        };
    }

    /// <summary>
    /// A rich game write: v1 rumble at distinct magnitudes, volumes, audio control,
    /// mute LED, both trigger blocks allowed, timestamp, motor power reduction,
    /// brightness/fade flags and lightbar state.
    /// </summary>
    private static byte[] RichGamePayload()
    {
        byte[] p = new byte[47];
        p[0] = 0x3F; // flag0: v1 rumble | haptics select | R2 FFB | L2 FFB | headphone vol | speaker vol
        p[2] = 0xAA; // right motor
        p[3] = 0xBB; // left motor
        p[4] = 0x11; // headphone volume
        p[5] = 0x22; // speaker volume
        p[6] = 0x33; // mic volume
        p[7] = 0x01; // audio control
        p[8] = 0x01; // mute LED on
        p[9] = 0x44; // power save control
        p[10] = 0x25; // R2 block: effect mode
        p[11] = 0x10;
        p[12] = 0x20;
        p[13] = 0x30;
        p[21] = 0x21; // L2 block: effect mode
        p[22] = 0x40;
        p[23] = 0x50;
        p[24] = 0x60;
        p[32] = 0xEF; // host timestamp 0xDEADBEEF
        p[33] = 0xBE;
        p[34] = 0xAD;
        p[35] = 0xDE;
        p[36] = 0x55; // motor power reduction
        p[37] = 0x77; // audio control 2
        p[38] = 0x03; // flag2: brightness change | color fade anim
        p[39] = 0x01; // haptic low-pass filter enable
        p[41] = 0x01; // light fade animation
        p[42] = 0x02; // lightbar brightness low
        p[43] = 0x1F; // player LEDs
        p[44] = 0xC0; // red
        p[45] = 0xFF; // green
        p[46] = 0xEE; // blue
        return p;
    }

    [Test]
    public void V1GameOnV2Pad_TranslatesSelectorAndPassesEverythingElseThrough()
    {
        SetStateData payload = VirtualDualSenseController.BuildOutputPayload(Output(RichGamePayload()), true);

        byte[] expected = RichGamePayload();
        Assert.Multiple(() =>
        {
            // v1 selector translated to the improved-rumble encoding.
            Assert.That((byte)payload.ValidFlag0 & 0x01, Is.Zero, "the v1 compatibility bit must be cleared for a v2 pad");
            Assert.That((byte)payload.ValidFlag0 & 0x02, Is.EqualTo(0x02), "haptics select must stay set");
            Assert.That((byte)payload.ValidFlag2 & 0x04, Is.EqualTo(0x04), "the v2 selector must be set for a v2 pad");

            // Everything else rides through exactly as the game wrote it.
            Assert.That((byte)payload.ValidFlag0 & 0x0C, Is.EqualTo(0x0C), "the game's trigger allow bits must be preserved");
            Assert.That((byte)payload.ValidFlag0 & 0x30, Is.EqualTo(0x30), "the game's volume bits must be preserved");
            Assert.That(payload.RumbleRight, Is.EqualTo(0xAA), "the game's motor bytes must ride through unchanged");
            Assert.That(payload.RumbleLeft, Is.EqualTo(0xBB));
            Assert.That(payload.HeadphoneVolume, Is.EqualTo(0x11));
            Assert.That(payload.SpeakerVolume, Is.EqualTo(0x22));
            Assert.That(payload.MicVolume, Is.EqualTo(0x33));
            Assert.That((byte)payload.AudioControl, Is.EqualTo(0x01));
            Assert.That(payload.MuteLedMode, Is.EqualTo(0x01));
            Assert.That(payload.PowerSaveControl, Is.EqualTo(0x44));
            Assert.That(payload.HostTimestamp, Is.EqualTo(0xDEADBEEFu));
            Assert.That(payload.MotorPowerReduction, Is.EqualTo(0x55));
            Assert.That(payload.AudioControl2, Is.EqualTo(0x77));
            Assert.That((byte)payload.ValidFlag2 & 0x03, Is.EqualTo(0x03), "the game's brightness/fade bits must be preserved");
            Assert.That(payload.HapticLowPassFilter, Is.EqualTo(0x01));
            Assert.That(payload.LightFadeAnimation, Is.EqualTo(0x01));
            Assert.That(payload.LightBrightness, Is.EqualTo(0x02));
            Assert.That((PlayerLedMask)payload.PlayerLeds, Is.EqualTo((PlayerLedMask)0x1F));
            Assert.That(payload.LedRed, Is.EqualTo(0xC0));
            Assert.That(payload.LedGreen, Is.EqualTo(0xFF));
            Assert.That(payload.LedBlue, Is.EqualTo(0xEE));
            Assert.That((byte)payload.R2TriggerEffect.Mode, Is.EqualTo(0x25), "the R2 effect block must ride the report");
            Assert.That((byte)payload.L2TriggerEffect.Mode, Is.EqualTo(0x21), "the L2 effect block must ride the report");
        });
    }

    [Test]
    public void V2GameOnV1Pad_TranslatesSelector()
    {
        byte[] p = new byte[47];
        p[0] = 0x02 | 0x0C; // flag0: haptics select + trigger FFB only
        p[38] = 0x04; // flag2: v2 selector

        SetStateData payload = VirtualDualSenseController.BuildOutputPayload(Output(p), false);

        Assert.Multiple(() =>
        {
            Assert.That((byte)payload.ValidFlag0 & 0x01, Is.EqualTo(0x01), "the v1 compatibility bit must be set for a v1 pad");
            Assert.That((byte)payload.ValidFlag0 & 0x02, Is.EqualTo(0x02));
            Assert.That((byte)payload.ValidFlag2 & 0x04, Is.Zero, "the v2 selector must be cleared for a v1 pad");
            Assert.That((byte)payload.ValidFlag0 & 0x0C, Is.EqualTo(0x0C), "the trigger allow bits must be preserved");
        });
    }

    [Test]
    public void ReportWithoutRumble_ClearsSelectorSoThePadRetainsMotors()
    {
        byte[] p = new byte[47];
        p[0] = 0x0C; // trigger-only write: no rumble selector
        p[2] = 0x7F; // stale motor bytes that the pad must ignore
        p[3] = 0x7F;

        SetStateData payload = VirtualDualSenseController.BuildOutputPayload(Output(p), true);

        Assert.Multiple(() =>
        {
            Assert.That((byte)payload.ValidFlag0 & 0x03, Is.Zero, "no selector bit may be set when the game did not touch rumble");
            Assert.That((byte)payload.ValidFlag2 & 0x04, Is.Zero);
            Assert.That(payload.RumbleRight, Is.EqualTo(0x7F),
                "the stale motor bytes ride through while the selector bits stay clear, so the pad ignores them");
            Assert.That(payload.RumbleLeft, Is.EqualTo(0x7F));
        });
    }

    [Test]
    public void TriggerBitsFollowTheGameExactly()
    {
        byte[] p = new byte[47];
        p[0] = 0x04; // only R2 allowed; L2 bit deliberately clear while its block bytes are present
        p[21] = 0x26;

        SetStateData payload = VirtualDualSenseController.BuildOutputPayload(Output(p), false);

        Assert.Multiple(() =>
        {
            Assert.That((byte)payload.ValidFlag0 & 0x04, Is.EqualTo(0x04), "the R2 allow bit must pass through");
            Assert.That((byte)payload.ValidFlag0 & 0x08, Is.Zero, "the L2 allow bit must stay clear so the pad retains its effect");
            Assert.That((byte)payload.L2TriggerEffect.Mode, Is.EqualTo(0x26), "the block bytes ride along regardless of gating");
        });
    }

    [Test]
    public void NullTriggerArrays_MapsFieldsOneToOne()
    {
        DSOutputState output = new DSOutputState
        {
            Flags0 = 0x0C,
            Flags1 = 0x14,
            RumbleSmall = 0x11,
            RumbleLarge = 0x22,
            VolumeHeadphones = 0x33,
            LedRed = 1,
            LedGreen = 2,
            LedBlue = 3,
            PlayerLeds = 0x07,
            MuteLightMode = 0x02,
            TriggerRight = null!,
            TriggerLeft = null!
        };

        SetStateData payload = VirtualDualSenseController.BuildOutputPayload(output, true);

        Assert.Multiple(() =>
        {
            Assert.That(payload.RumbleRight, Is.EqualTo(0x11));
            Assert.That(payload.RumbleLeft, Is.EqualTo(0x22));
            Assert.That(payload.LedRed, Is.EqualTo(1));
            Assert.That(payload.LedGreen, Is.EqualTo(2));
            Assert.That(payload.LedBlue, Is.EqualTo(3));
            Assert.That((byte)payload.PlayerLeds, Is.EqualTo(0x07));
            Assert.That(payload.MuteLedMode, Is.EqualTo(0x02));
            Assert.That(payload.HeadphoneVolume, Is.EqualTo(0x33));
            Assert.That((byte)payload.ValidFlag0 & 0x0C, Is.EqualTo(0x0C));
        });
    }
}