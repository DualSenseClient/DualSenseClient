using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseClient.Hid;

/// <summary>
/// Platform-aware helpers for SDL hidapi <c>wchar_t*</c> strings.
/// Windows <c>wchar_t</c> is 2 bytes UTF-16; Linux is 4 bytes UTF-32.
/// Centralizes the branching so future HID string APIs reuse the same logic.
/// </summary>
internal static class HidWchar
{
    /// <summary>
    /// Converts a native null-terminated <c>wchar_t*</c> to a managed string.
    /// </summary>
    public static unsafe string PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }

        // Linux: wchar_t is 4 bytes UTF-32.
        int* p = (int*)ptr;
        int len = 0;
        while (p[len] != 0)
        {
            len++;
        }

        if (len == 0)
        {
            return string.Empty;
        }

        return DecodeUtf32(p, len);
    }

    /// <summary>
    /// Decodes a <c>wchar_t</c> buffer of known length (as returned by
    /// <c>SDL_hid_get_*_string</c>) to a managed string. Stops at first NUL
    /// within the length and skips invalid codepoints.
    /// </summary>
    public static unsafe string BufferToString(int* buffer, int length)
    {
        if (buffer == null || length <= 0)
        {
            return "Unknown";
        }

        if (length > 256)
        {
            length = 256;
        }

        StringBuilder sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            int cp = buffer[i];
            if (cp == 0)
            {
                break;
            }

            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
            {
                continue;
            }

            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.Length > 0 ? sb.ToString() : "Unknown";
    }

    /// <summary>
    /// Marshals a managed string to a native <c>wchar_t*</c> for SDL hidapi.
    /// Caller must free with <see cref="Marshal.FreeCoTaskMem"/>.
    /// </summary>
    public static unsafe IntPtr StringToPtr(string s)
    {
        if (OperatingSystem.IsWindows())
        {
            return Marshal.StringToCoTaskMemUni(s);
        }

        // Linux: wchar_t is 4 bytes UTF-32.
        int runeCount = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                runeCount++;
                i++;
            }
            else
            {
                runeCount++;
            }
        }

        IntPtr ptr = Marshal.AllocCoTaskMem((runeCount + 1) * 4);
        int* p = (int*)ptr;
        int idx = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int cp = char.ConvertToUtf32(s, i);
            if (char.IsSurrogatePair(s, i))
            {
                i++;
            }

            p[idx++] = cp;
        }

        p[idx] = 0;
        return ptr;
    }

    /// <summary>
    /// Decodes a <c>wchar_t</c> UTF-32 buffer (Linux) to a managed string.
    /// Skips invalid codepoints (out of Unicode range or surrogate halves) instead
    /// of throwing, matching SDL hidapi's lenient handling.
    /// </summary>
    /// <param name="p">Pointer to the first UTF-32 codepoint.</param>
    /// <param name="len">Number of codepoints to decode.</param>
    /// <returns>Decoded UTF-16 string, or empty if all codepoints were invalid.</returns>
    private static unsafe string DecodeUtf32(int* p, int len)
    {
        StringBuilder sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            int cp = p[i];
            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
            {
                continue;
            }

            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }
}