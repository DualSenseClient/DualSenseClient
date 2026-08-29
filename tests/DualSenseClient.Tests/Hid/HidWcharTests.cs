using System.Runtime.InteropServices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Hid;

public class HidWcharTests
{
    [Test]
    public unsafe void BufferToString_Ascii_Decodes()
    {
        const string value = "DualSense Wireless Controller";
        int[] codepoints = value.Select(c => (int)c).ToArray();
        fixed (int* p = codepoints)
        {
            Assert.That(HidWchar.BufferToString(p, codepoints.Length), Is.EqualTo(value));
        }
    }

    [Test]
    public unsafe void BufferToString_AstralCodepoint_DecodesToSurrogatePair()
    {
        // U+1F600 cannot be represented in a single UTF-16 char.
        int[] codepoints = [0x44, 0x1F600, 0x61];
        fixed (int* p = codepoints)
        {
            string result = HidWchar.BufferToString(p, codepoints.Length);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo("D\U0001F600a"));
                Assert.That(result.Length, Is.EqualTo(4));
            });
        }
    }

    [Test]
    public unsafe void BufferToString_StopsAtFirstNul()
    {
        int[] codepoints = [(int)'A', (int)'B', 0, (int)'C'];
        fixed (int* p = codepoints)
        {
            Assert.That(HidWchar.BufferToString(p, codepoints.Length), Is.EqualTo("AB"));
        }
    }

    [Test]
    public unsafe void BufferToString_SkipsInvalidCodepoints()
    {
        // Surrogate halves, out-of-range, and negative values are skipped, not thrown.
        int[] codepoints = [(int)'A', 0xD800, 0xDFFF, 0x110000, -1, (int)'B'];
        fixed (int* p = codepoints)
        {
            Assert.That(HidWchar.BufferToString(p, codepoints.Length), Is.EqualTo("AB"));
        }
    }

    [Test]
    public unsafe void BufferToString_NullBuffer_ReturnsUnknown() => Assert.That(HidWchar.BufferToString(null, 16), Is.EqualTo("Unknown"));

    [Test]
    public unsafe void BufferToString_NonPositiveLength_ReturnsUnknown()
    {
        int[] codepoints = [(int)'A'];
        fixed (int* p = codepoints)
        {
            string zero = HidWchar.BufferToString(p, 0);
            string negative = HidWchar.BufferToString(p, -1);
            Assert.Multiple(() =>
            {
                Assert.That(zero, Is.EqualTo("Unknown"));
                Assert.That(negative, Is.EqualTo("Unknown"));
            });
        }
    }

    [Test]
    public unsafe void BufferToString_AllInvalidCodepoints_ReturnsUnknown()
    {
        int[] codepoints = [0xD800, 0xDFFF, 0x110000];
        fixed (int* p = codepoints)
        {
            Assert.That(HidWchar.BufferToString(p, codepoints.Length), Is.EqualTo("Unknown"));
        }
    }

    [Test]
    public unsafe void BufferToString_LengthAbove256_IsClamped()
    {
        int[] codepoints = Enumerable.Repeat((int)'A', 300).ToArray();
        fixed (int* p = codepoints)
        {
            string result = HidWchar.BufferToString(p, codepoints.Length);
            Assert.That(result, Is.EqualTo(new string('A', 256)));
        }
    }

    [Test]
    public void PtrToString_ZeroPtr_ReturnsEmpty() => Assert.That(HidWchar.PtrToString(IntPtr.Zero), Is.EqualTo(string.Empty));

    [Test]
    public void StringToPtr_RoundtripsThroughPtrToString()
    {
        const string value = "DualSense \U0001F3AE Controller";
        IntPtr ptr = HidWchar.StringToPtr(value);
        try
        {
            Assert.That(HidWchar.PtrToString(ptr), Is.EqualTo(value));
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [Test]
    public void StringToPtr_EmptyString_RoundtripsToEmpty()
    {
        IntPtr ptr = HidWchar.StringToPtr(string.Empty);
        try
        {
            Assert.That(HidWchar.PtrToString(ptr), Is.EqualTo(string.Empty));
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}