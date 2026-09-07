using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Hid.Interop;

namespace DualSenseClient.Tests.Hid;

[TestFixture]
public sealed class HidStringMarshalTests
{
    [Test]
    public void PtrToString_Null_ReturnsEmpty() => Assert.That(HidStringMarshal.PtrToString(IntPtr.Zero), Is.EqualTo(string.Empty));

    [Test]
    public void PtrToString_DecodesAscii()
    {
        string decoded;
        unsafe
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                char* buf = stackalloc char[10];
                WriteAscii(buf, "DualSense");
                decoded = HidStringMarshal.PtrToString((IntPtr)buf);
            }
            else
            {
                int* buf = stackalloc int[10];
                WriteAscii(buf, "DualSense");
                decoded = HidStringMarshal.PtrToString((IntPtr)buf);
            }
        }

        Assert.That(decoded, Is.EqualTo("DualSense"));
    }

    [Test]
    public void BufferToString_Null_ReturnsEmpty() => Assert.That(HidStringMarshal.BufferToString(IntPtr.Zero, 9), Is.EqualTo(string.Empty));

    [Test]
    public void BufferToString_DecodesWithExplicitLength()
    {
        string decoded;
        unsafe
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                char* buf = stackalloc char[10];
                WriteAscii(buf, "DualSense");
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, 9);
            }
            else
            {
                int* buf = stackalloc int[10];
                WriteAscii(buf, "DualSense");
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, 9);
            }
        }

        Assert.That(decoded, Is.EqualTo("DualSense"));
    }

    [Test]
    public void BufferToString_DecodesNullTerminatedWithoutLength()
    {
        string decoded;
        unsafe
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                char* buf = stackalloc char[10];
                WriteAscii(buf, "Sony");
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, 0);
            }
            else
            {
                int* buf = stackalloc int[10];
                WriteAscii(buf, "Sony");
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, 0);
            }
        }

        Assert.That(decoded, Is.EqualTo("Sony"));
    }

    [Test]
    public void BufferToString_NegativeResult_DecodesEmpty()
    {
        string decoded;
        unsafe
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                char* buf = stackalloc char[1];
                buf[0] = '\0';
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, -1);
            }
            else
            {
                int* buf = stackalloc int[1];
                buf[0] = 0;
                decoded = HidStringMarshal.BufferToString((IntPtr)buf, -1);
            }
        }

        Assert.That(decoded, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Utf8ToString_Null_ReturnsEmpty()
    {
        string decoded;
        unsafe
        {
            decoded = HidStringMarshal.Utf8ToString(null);
        }

        Assert.That(decoded, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Utf8ToString_Empty_ReturnsEmpty()
    {
        byte[] bytes = [0];
        string decoded;
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                decoded = HidStringMarshal.Utf8ToString(ptr);
            }
        }

        Assert.That(decoded, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Utf8ToString_DecodesAscii()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\\\\.\\HID#VID_054C&PID_0CE6\0");
        string decoded;
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                decoded = HidStringMarshal.Utf8ToString(ptr);
            }
        }

        Assert.That(decoded, Is.EqualTo("\\\\.\\HID#VID_054C&PID_0CE6"));
    }

    [Test]
    public void Utf8ToString_DecodesMultibyte()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Contrôleur 🎮\0");
        string decoded;
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                decoded = HidStringMarshal.Utf8ToString(ptr);
            }
        }

        Assert.That(decoded, Is.EqualTo("Contrôleur 🎮"));
    }

    [Test]
    public void PtrToString_DecodesSupplementaryPlaneCharacterOnLinux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("UTF-32 decoding is Linux-only.");
        }

        string decoded;
        unsafe
        {
            int* buf = stackalloc int[2];
            buf[0] = 0x1F3AE;
            buf[1] = 0;
            decoded = HidStringMarshal.PtrToString((IntPtr)buf);
        }

        Assert.That(decoded, Is.EqualTo(char.ConvertFromUtf32(0x1F3AE)));
    }

    [Test]
    public void PtrToString_SkipsInvalidCodePointsOnLinux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("UTF-32 decoding is Linux-only.");
        }

        string decoded;
        unsafe
        {
            int* buf = stackalloc int[2];
            buf[0] = 0xD800;
            buf[1] = 0;
            decoded = HidStringMarshal.PtrToString((IntPtr)buf);
        }

        Assert.That(decoded, Is.EqualTo(string.Empty));
    }

    private static unsafe void WriteAscii(char* buf, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            buf[i] = text[i];
        }

        buf[text.Length] = '\0';
    }

    private static unsafe void WriteAscii(int* buf, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            buf[i] = text[i];
        }

        buf[text.Length] = 0;
    }
}