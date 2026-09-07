using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseClient.Hid.Interop;

/// <summary>
/// Decodes native HIDAPI <c>wchar_t</c> strings, whose width is platform-dependent
/// (UTF-16 on Windows, UTF-32 on Linux).
/// </summary>
internal static unsafe class HidStringMarshal
{
    /// <summary>
    /// Maximum length, in native characters, read from a string that is expected
    /// to be null-terminated. Guards against runaway reads on corrupt memory.
    /// </summary>
    private const int MaxStringLength = 1024;

    /// <summary>
    /// Decodes a null-terminated native <c>wchar_t</c> string pointer.
    /// </summary>
    /// <param name="ptr">Pointer to the native string, or <see cref="IntPtr.Zero"/>.</param>
    /// <returns>The decoded string, or <see cref="string.Empty"/> when the pointer is null.</returns>
    public static string PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }

        return DecodeUtf32((int*)ptr, -1);
    }

    /// <summary>
    /// Decodes a native <c>wchar_t</c> buffer filled by <c>hid_get_*_string</c>.
    /// </summary>
    /// <param name="buffer">Pointer to the native buffer.</param>
    /// <param name="written">Characters written excluding the terminator, or 0 when the native call reports success without a length.</param>
    /// <returns>The decoded string, possibly empty.</returns>
    public static string BufferToString(IntPtr buffer, int written)
    {
        if (buffer == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            char* chars = (char*)buffer;
            if (written > 0)
            {
                int length = Math.Min(written, MaxStringLength);
                return new string(chars, 0, length);
            }

            int nullTerminatedLength = 0;
            while (nullTerminatedLength < MaxStringLength && chars[nullTerminatedLength] != '\0')
            {
                nullTerminatedLength++;
            }

            return nullTerminatedLength > 0 ? new string(chars, 0, nullTerminatedLength) : string.Empty;
        }

        return DecodeUtf32((int*)buffer, written);
    }

    /// <summary>
    /// Decodes up to <paramref name="written"/> UTF-32 code points, or a null-terminated
    /// sequence when <paramref name="written"/> is not positive.
    /// </summary>
    /// <param name="ptr">Pointer to the UTF-32 code points.</param>
    /// <param name="written">Code points written excluding the terminator, or a non-positive value for null-terminated.</param>
    /// <returns>The decoded string, possibly empty.</returns>
    private static string DecodeUtf32(int* ptr, int written)
    {
        int length = 0;
        if (written > 0)
        {
            length = Math.Min(written, MaxStringLength);
        }
        else
        {
            while (length < MaxStringLength && ptr[length] != 0)
            {
                length++;
            }
        }

        if (length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            int codePoint = ptr[i];
            if (codePoint <= 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                continue;
            }

            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString();
    }
}