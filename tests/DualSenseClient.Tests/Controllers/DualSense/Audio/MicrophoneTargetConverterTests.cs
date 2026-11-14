using System.Buffers.Binary;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

/// <summary>
/// Pins the microphone target converter: the 48 kHz mono source is duplicated into two-channel
/// DualSense frames and low-pass filtered then decimated 3:1 into one-channel DualShock 4 frames,
/// the filter and decimation state persist across calls until <see cref="MicrophoneTargetConverter.Reset"/>,
/// short destinations are rejected, and formats other than the two personas are unsupported.
/// </summary>
[TestFixture]
public sealed class MicrophoneTargetConverterTests
{
    [Test]
    public void Convert_DualSense_Produces192ByteStereoFrame()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualSense);
        short[] source = Enumerable.Range(0, 48).Select(i => (short)(i * 100)).ToArray();
        byte[] target = new byte[192];

        int written = converter.Convert(source, target);

        Assert.That(written, Is.EqualTo(192));
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(target.AsSpan(i * 4, 2)), Is.EqualTo(source[i]),
                    $"left channel sample {i}");
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(target.AsSpan(i * 4 + 2, 2)), Is.EqualTo(source[i]),
                    $"right channel sample {i}");
            });
        }
    }

    [Test]
    public void Convert_DualShock4_Produces32ByteMonoFrame()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualShock4);
        short[] source = new short[48];
        byte[] target = new byte[32];

        int written = converter.Convert(source, target);

        Assert.That(written, Is.EqualTo(32));
    }

    [Test]
    public void Convert_DualShock4_KeepsFilterStateAcrossFrames()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualShock4);
        short[] source = Enumerable.Repeat((short)1000, 48).ToArray();
        byte[] first = new byte[32];
        byte[] second = new byte[32];

        converter.Convert(source, first);
        converter.Convert(source, second);

        // A stateless decimator would produce identical frames; the filter ramps.
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void Reset_ClearsFilterState()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualShock4);
        short[] source = Enumerable.Repeat((short)1000, 48).ToArray();
        byte[] first = new byte[32];
        byte[] second = new byte[32];

        converter.Convert(source, first);
        converter.Reset();
        converter.Convert(source, second);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Convert_ShortDestination_Throws()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualSense);
        Assert.That(() => converter.Convert(new short[48], new byte[1]), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_UnsupportedFormat_Throws()
    {
        Assert.That(
            () => new MicrophoneTargetConverter(default),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_DualShock4_AttenuatesHighFrequencyContent()
    {
        MicrophoneTargetConverter converter = new MicrophoneTargetConverter(AudioFunctionFormat.DualShock4);
        short[] alternating = new short[48];
        for (int i = 0; i < alternating.Length; i++)
        {
            alternating[i] = i % 2 == 0 ? (short)1000 : (short)-1000;
        }

        byte[] frame = new byte[32];
        for (int i = 0; i < 3; i++)
        {
            converter.Convert(alternating, frame);
        }

        // A 7 kHz low-pass at 48 kHz rejects near-Nyquist alternating input once settled.
        int peak = 0;
        for (int i = 0; i < frame.Length; i += sizeof(short))
        {
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(i, 2))));
        }

        Assert.That(peak, Is.LessThan(50));
    }
}