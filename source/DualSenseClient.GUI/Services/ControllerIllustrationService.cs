using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Provides DualSense controller illustration skins embedded as Avalonia resources under
/// <c>Assets/Controllers/DualSense/Skins</c>. Skin names are the embedded file names (e.g. "Cosmic Red"),
/// each mapping to a single <c>.png</c> controller illustration.
/// </summary>
/// <remarks>
/// <para>
/// Skins are enumerated at runtime from the assembly's Avalonia resource index, so adding a
/// new <c>*.png</c> file to the skin folder automatically makes it available as a skin.
/// </para>
/// <para>
/// Also provides the live-monitor assets: "no analog stick and triggers" base images under
/// <c>Assets/Controllers/DualSense/Base</c> and overlay sprites under <c>Assets/Controllers/DualSense/Buttons</c>,
/// used by the reusable <see cref="DualSenseClient.GUI.Controls.DualSenseControllerView"/>.
/// </para>
/// </remarks>
public sealed class ControllerIllustrationService
{
    /// <summary>
    /// The skin used when no per-controller skin is stored.
    /// </summary>
    public const string DefaultSkin = "Midnight Black";

    /// <summary>
    /// The skin name whose monitor renders with the Dev Mode (colored overlay) sprite set.
    /// </summary>
    public const string DevModeSkin = "Dev Mode";

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Illustration");

    /// <summary>
    /// Embedded resource path prefix of the skin folder (avares URI form).
    /// </summary>
    private static readonly string SkinsFolderUri =
        $"avares://{typeof(ControllerIllustrationService).Assembly.GetName().Name}/Assets/Controllers/DualSense/Skins/";

    /// <summary>
    /// Embedded resource path prefix of the monitor base folder (avares URI form).
    /// </summary>
    private static readonly string BaseFolderUri =
        $"avares://{typeof(ControllerIllustrationService).Assembly.GetName().Name}/Assets/Controllers/DualSense/Base/";

    /// <summary>
    /// Embedded resource path prefix of the overlay sprite folder (avares URI form).
    /// </summary>
    private static readonly string ButtonsFolderUri =
        $"avares://{typeof(ControllerIllustrationService).Assembly.GetName().Name}/Assets/Controllers/DualSense/Buttons/";

    /// <summary>
    /// Gets the monitor base file name of a skin: the skin display name plus extension
    /// (base images are named after their skin), falling back to the default skin when blank.
    /// </summary>
    private static string GetBaseFile(string? skin) => $"{(string.IsNullOrWhiteSpace(skin) ? DefaultSkin : skin)}.png";

    /// <summary>
    /// Loaded skin bitmaps, cached by skin name.
    /// </summary>
    private readonly Dictionary<string, Bitmap> _bitmapCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loaded monitor base bitmaps, cached by file name.
    /// </summary>
    private readonly Dictionary<string, Bitmap> _monitorBaseCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lightbar-tinted monitor base bitmaps, cached by file name, lightbar color, and
    /// player LED layout.
    /// </summary>
    private readonly Dictionary<(string File, byte R, byte G, byte B, byte Leds), Bitmap> _monitorBaseTintCache =
        new Dictionary<(string File, byte R, byte G, byte B, byte Leds), Bitmap>();

    /// <summary>
    /// Loaded overlay sprite bitmaps, cached by sprite key (skin variant prefixed).
    /// </summary>
    private readonly Dictionary<string, Bitmap> _spriteCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pixel positions of the lightbar (the U-shape around the touchpad) with the luminance
    /// of the default skin's render, extracted once from the default skin's monitor base and
    /// shared by every skin because the lightbar geometry is identical across renders. Its
    /// bottom segment (right below the touchpad) is where the five player LEDs are drawn.
    /// </summary>
    private static List<(int X, int Y, byte Luminance)>? _lightbarMask;

    /// <summary>
    /// Synchronizes lazy extraction of <see cref="_lightbarMask"/>.
    /// </summary>
    private static readonly object _lightbarMaskLock = new object();

    /// <summary>
    /// Pixel positions of the baked microphone LED dot just below the mute button,
    /// extracted once from the default skin's monitor base and shared by every skin. The
    /// dot is darkened in tinted bases so the live mic LED sprite can light it instead.
    /// </summary>
    private static List<(int X, int Y)>? _micLedMask;

    /// <summary>
    /// Synchronizes lazy extraction of <see cref="_micLedMask"/>.
    /// </summary>
    private static readonly object _micLedMaskLock = new object();

    /// <summary>
    /// The animated microphone LED sprite (an orange glow over the mute button),
    /// generated once.
    /// </summary>
    private static Bitmap? _micLedSprite;

    /// <summary>
    /// Synchronizes lazy generation of <see cref="_micLedSprite"/>.
    /// </summary>
    private static readonly object _micLedSpriteLock = new object();

    /// <summary>
    /// Scan band of the baked microphone LED dot on the monitor base (just below the
    /// mute button) and the brightness threshold separating it from the dark body.
    /// </summary>
    private const int MicLedBandXStart = 715;

    private const int MicLedBandXEnd = 760;
    private const int MicLedBandYStart = 645;
    private const int MicLedBandYEnd = 658;
    private const int MicLedBrightness = 150;

    /// <summary>
    /// Body color used to darken the baked microphone LED dot.
    /// </summary>
    private const byte MicLedOffRed = 23;

    private const byte MicLedOffGreen = 25;
    private const byte MicLedOffBlue = 30;

    /// <summary>
    /// Core color of the microphone LED sprite (warm orange, like the physical mute LED).
    /// </summary>
    private const byte MicLedCoreRed = 255;

    private const byte MicLedCoreGreen = 153;
    private const byte MicLedCoreBlue = 0;

    /// <summary>
    /// Shape of the orange glow sprite: the mute button pill plus a halo margin so the
    /// light bleeds softly around the button.
    /// </summary>
    private const int MicLedPillWidth = 75;

    private const int MicLedPillHeight = 16;
    private const int MicLedGlowMargin = 6;

    /// <summary>
    /// Corner radius of the glow core (a pill: semicircular ends, so half the height).
    /// </summary>
    private const double MicLedPillCornerRadius = MicLedPillHeight / 2.0;

    /// <summary>
    /// Peak intensity of the glow on the button face (lets the baked button detail show
    /// through slightly).
    /// </summary>
    private const double MicLedPeakIntensity = 0.9;

    /// <summary>
    /// Number of player LEDs rendered on the lightbar line below the touchpad (LED 1 = leftmost).
    /// </summary>
    private const int PlayerLedCount = 5;

    /// <summary>
    /// Spacing between the inner player LEDs (LED 2-3 and 3-4) in asset pixels.
    /// </summary>
    private const int PlayerLedInnerSpacing = 48;

    /// <summary>
    /// Spacing between the outer and inner player LEDs (LED 1-2 and 4-5) in asset pixels,
    /// wider so the outer LEDs sit further from the center.
    /// </summary>
    private const int PlayerLedOuterSpacing = 96;

    /// <summary>
    /// Horizontal offset of the whole player LED row from the lightbar line's center, in
    /// asset pixels (positive moves the row right).
    /// </summary>
    private const int PlayerLedCenterOffsetX = -32;

    /// <summary>
    /// Vertical offset of the whole player LED row from the lightbar line's center, in
    /// asset pixels (positive moves the row down).
    /// </summary>
    private const int PlayerLedCenterOffsetY = 0;

    /// <summary>
    /// Radius of the bright core of each player LED light effect, in asset pixels.
    /// </summary>
    private const int PlayerLedCoreRadius = 8;

    /// <summary>
    /// Radius of the soft glow halo around each player LED core, in asset pixels.
    /// </summary>
    private const int PlayerLedGlowRadius = 20;

    /// <summary>
    /// Number of pixel rows above the lightbar mask's bottom edge that make up the
    /// horizontal lightbar segment right below the touchpad, on which the player LEDs sit.
    /// </summary>
    private const int PlayerLedLineHeight = 1;

    /// <summary>
    /// Gets all available skin names, in the order they are presented to the user.
    /// </summary>
    public IReadOnlyList<string> GetSkins()
    {
        List<string> skins = AssetLoader.GetAssets(new Uri(SkinsFolderUri), null)
            .Select(uri => Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath)))
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _log.Debug($"Discovered {skins.Count} illustration skins");
        return skins;
    }

    /// <summary>
    /// Gets the illustration bitmap for a skin, or <c>null</c> when the skin does not exist
    /// or its image cannot be loaded. Loaded bitmaps are cached by skin name.
    /// </summary>
    /// <param name="skin">The skin name (e.g. "Cosmic Red").</param>
    public Bitmap? GetSkinImage(string skin)
    {
        if (string.IsNullOrWhiteSpace(skin))
        {
            return null;
        }

        if (_bitmapCache.TryGetValue(skin, out Bitmap? cached))
        {
            return cached;
        }

        try
        {
            Uri uri = new Uri($"{SkinsFolderUri}{Uri.EscapeDataString(skin)}.png");
            using Stream stream = AssetLoader.Open(uri);
            Bitmap bitmap = new Bitmap(stream);
            _bitmapCache[skin] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not load illustration skin '{skin}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the monitor base bitmap for a skin, or <c>null</c> when it cannot be loaded.
    /// The monitor bases are the "no analog stick and triggers" controller renders with
    /// holes where the live overlay sprites are drawn. Loaded bitmaps are cached by file name.
    /// </summary>
    /// <param name="skin">The skin name (e.g. "Midnight Black"), falling back to the default skin.</param>
    public Bitmap? GetMonitorBase(string skin)
    {
        string file = GetBaseFile(skin);

        if (_monitorBaseCache.TryGetValue(file, out Bitmap? cached))
        {
            return cached;
        }

        try
        {
            Uri uri = new Uri($"{BaseFolderUri}{Uri.EscapeDataString(file)}");
            using Stream stream = AssetLoader.Open(uri);
            Bitmap bitmap = new Bitmap(stream);
            _monitorBaseCache[file] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not load monitor base '{file}': {ex.Message}");

            return string.Equals(file, GetBaseFile(DefaultSkin), StringComparison.OrdinalIgnoreCase)
                ? null
                : GetMonitorBase(DefaultSkin);
        }
    }

    /// <summary>
    /// Gets the monitor base bitmap for a skin with its baked lightbar recolored to the
    /// given RGB and its lit player LEDs drawn as bright dots on the lightbar segment
    /// below the touchpad, or <c>null</c> when it cannot be loaded. The lightbar pixels
    /// are found through the shared mask (see <see cref="GetLightbarMask"/>) and recolored
    /// while preserving the baked luminance, so the U-shape and its glow match the original
    /// render. Tinted bitmaps are cached by file name, color, and LED layout.
    /// </summary>
    /// <param name="skin">The skin name (e.g. "Midnight Black"), falling back to the default skin.</param>
    /// <param name="red">Lightbar red channel (0-255).</param>
    /// <param name="green">Lightbar green channel (0-255).</param>
    /// <param name="blue">Lightbar blue channel (0-255).</param>
    /// <param name="playerLeds">Player LED mask: bit 0 = LED 1 (leftmost) through bit 4 = LED 5.</param>
    public Bitmap? GetMonitorBase(string? skin, byte red, byte green, byte blue, byte playerLeds)
    {
        string file = GetBaseFile(skin);

        (string file, byte red, byte green, byte blue, byte playerLeds) key = (file, red, green, blue, playerLeds);
        if (_monitorBaseTintCache.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        Bitmap? original = GetMonitorBase(skin ?? string.Empty);
        bool dev = string.Equals(skin, DevModeSkin, StringComparison.OrdinalIgnoreCase);
        Bitmap? tinted = original is null ? null : TintLightbar(original, red, green, blue, playerLeds, dev);
        if (tinted is not null)
        {
            _monitorBaseTintCache[key] = tinted;
        }

        return tinted ?? original;
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> whose lightbar pixels are recolored to
    /// the given RGB (preserving luminance and alpha) and whose lit player LEDs are drawn
    /// as bright dots on the lightbar segment below the touchpad, or <c>null</c> when the
    /// mask or the pixel format are unavailable. Dev Mode bases render the lightbar in
    /// black, so they are recolored from the default render's luminance instead.
    /// </summary>
    private static Bitmap? TintLightbar(Bitmap source, byte red, byte green, byte blue, byte playerLeds, bool useSharedLuminance)
    {
        List<(int X, int Y, byte Luminance)> mask = GetLightbarMask();
        if (mask.Count == 0 || source.Format != PixelFormat.Bgra8888)
        {
            return null;
        }

        int width = source.PixelSize.Width;
        int height = source.PixelSize.Height;
        int rowBytes = width * 4;
        byte[] buffer = ReadPixels(source, width, height, rowBytes);

        for (int i = 0; i < mask.Count; i++)
        {
            (int x, int y, byte luminance) = mask[i];
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                continue;
            }

            int offset = y * rowBytes + x * 4;
            if (!useSharedLuminance)
            {
                luminance = (byte)((buffer[offset] + buffer[offset + 1] + buffer[offset + 2]) / 3);
            }

            buffer[offset] = (byte)(luminance * blue / 255);
            buffer[offset + 1] = (byte)(luminance * green / 255);
            buffer[offset + 2] = (byte)(luminance * red / 255);
        }

        if (playerLeds != 0)
        {
            DrawPlayerLeds(buffer, width, height, rowBytes, mask, playerLeds);
        }

        foreach ((int x, int y) in GetMicLedMask())
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                continue;
            }

            int offset = y * rowBytes + x * 4;
            buffer[offset] = (byte)((buffer[offset] + MicLedOffBlue * 9) / 10);
            buffer[offset + 1] = (byte)((buffer[offset + 1] + MicLedOffGreen * 9) / 10);
            buffer[offset + 2] = (byte)((buffer[offset + 2] + MicLedOffRed * 9) / 10);
        }

        WriteableBitmap tinted = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer target = tinted.Lock())
        {
            Marshal.Copy(buffer, 0, target.Address, buffer.Length);
        }

        return tinted;
    }

    /// <summary>
    /// Draws the lit player LEDs as semicircular white light effects on the lightbar's
    /// bottom segment (the horizontal line right below the touchpad): a bright core with a
    /// soft glow halo fading into the surrounding pixels, the flat side resting on the
    /// line. The segment's geometry is derived from the lightbar mask; the inner LEDs sit
    /// 28px apart and the outer ones 42px from their neighbors, all centered on the line.
    /// </summary>
    private static void DrawPlayerLeds(byte[] buffer, int width, int height, int rowBytes, List<(int X, int Y, byte Luminance)> mask, byte playerLeds)
    {
        int maxY = 0;
        int minX = int.MaxValue;
        int maxX = 0;
        long sumY = 0;
        int count = 0;
        int lineYStart = mask.Max(p => p.Y) - PlayerLedLineHeight + 1;

        foreach ((int x, int y, _) in mask)
        {
            if (y < lineYStart)
            {
                continue;
            }

            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            if (y > maxY)
            {
                maxY = y;
            }

            sumY += y;
            count++;
        }

        if (count == 0)
        {
            return;
        }

        int centerX = (int)Math.Round((minX + maxX) / 2.0) + PlayerLedCenterOffsetX;
        int centerY = (int)Math.Round(sumY / (double)count) + PlayerLedCenterOffsetY;
        int[] ledOffsets =
        {
            -PlayerLedOuterSpacing - PlayerLedInnerSpacing, -PlayerLedInnerSpacing, 0, PlayerLedInnerSpacing, PlayerLedOuterSpacing + PlayerLedInnerSpacing
        };

        for (int led = 0; led < PlayerLedCount; led++)
        {
            if ((playerLeds & (1 << led)) == 0)
            {
                continue;
            }

            int cx = centerX + ledOffsets[led];
            int cy = centerY;
            int coreSquared = PlayerLedCoreRadius * PlayerLedCoreRadius;
            int glowSquared = PlayerLedGlowRadius * PlayerLedGlowRadius;

            for (int y = cy - PlayerLedGlowRadius; y <= cy; y++)
            {
                if ((uint)y >= (uint)height)
                {
                    continue;
                }

                int dy = y - cy;
                for (int x = cx - PlayerLedGlowRadius; x <= cx + PlayerLedGlowRadius; x++)
                {
                    if ((uint)x >= (uint)width)
                    {
                        continue;
                    }

                    int dx = x - cx;
                    int distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared > glowSquared)
                    {
                        continue;
                    }

                    double intensity = distanceSquared <= coreSquared
                        ? 1.0
                        : 1.0 - (Math.Sqrt(distanceSquared) - PlayerLedCoreRadius) / (PlayerLedGlowRadius - PlayerLedCoreRadius);

                    int offset = y * rowBytes + x * 4;
                    buffer[offset] = (byte)(buffer[offset] + (255 - buffer[offset]) * intensity);
                    buffer[offset + 1] = (byte)(buffer[offset + 1] + (255 - buffer[offset + 1]) * intensity);
                    buffer[offset + 2] = (byte)(buffer[offset + 2] + (255 - buffer[offset + 2]) * intensity);
                }
            }
        }
    }

    /// <summary>
    /// Gets the shared lightbar pixel mask with the default render's luminance, extracting
    /// it lazily from the default skin's monitor base: within the touchpad band, pixels
    /// whose blue channel dominates the surrounding body and touchpad surface (which are
    /// gray).
    /// </summary>
    private static List<(int X, int Y, byte Luminance)> GetLightbarMask()
    {
        List<(int X, int Y, byte Luminance)>? mask = _lightbarMask;
        if (mask is not null)
        {
            return mask;
        }

        lock (_lightbarMaskLock)
        {
            if (_lightbarMask is null)
            {
                _lightbarMask = ExtractLightbarMask(GetMonitorBaseFromFile(GetBaseFile(DefaultSkin))) ?? [];
                _log.Debug($"Extracted lightbar mask with {_lightbarMask.Count} pixels");
            }

            return _lightbarMask;
        }
    }

    /// <summary>
    /// Scans the given base for the lightbar U-shape around the touchpad, or returns
    /// <c>null</c> when the bitmap or its pixel format is unavailable.
    /// </summary>
    private static List<(int X, int Y, byte Luminance)>? ExtractLightbarMask(Bitmap? baseImage)
    {
        if (baseImage is null || baseImage.Format != PixelFormat.Bgra8888)
        {
            return null;
        }

        int width = baseImage.PixelSize.Width;
        int height = baseImage.PixelSize.Height;
        int rowBytes = width * 4;
        byte[] buffer = ReadPixels(baseImage, width, height, rowBytes);

        List<(int X, int Y, byte Luminance)> pixels = new List<(int X, int Y, byte Luminance)>();
        int xStart = Math.Min(300, width);
        int xEnd = Math.Min(1160, width);
        int yStart = Math.Min(140, height);
        int yEnd = Math.Min(500, height);

        for (int y = yStart; y < yEnd; y++)
        {
            int row = y * rowBytes;
            for (int x = xStart; x < xEnd; x++)
            {
                int offset = row + x * 4;
                if (buffer[offset] > 90 && buffer[offset] > buffer[offset + 2] + 10)
                {
                    byte luminance = (byte)((buffer[offset] + buffer[offset + 1] + buffer[offset + 2]) / 3);
                    pixels.Add((x, y, luminance));
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Gets the shared microphone LED pixel mask, extracting it lazily from the default
    /// skin's monitor base: the bright dot just below the mute button.
    /// </summary>
    private static List<(int X, int Y)> GetMicLedMask()
    {
        List<(int X, int Y)>? mask = _micLedMask;
        if (mask is not null)
        {
            return mask;
        }

        lock (_micLedMaskLock)
        {
            if (_micLedMask is null)
            {
                _micLedMask = ExtractMicLedMask(GetMonitorBaseFromFile(GetBaseFile(DefaultSkin))) ?? [];
                _log.Debug($"Extracted mic LED mask with {_micLedMask.Count} pixels");
            }

            return _micLedMask;
        }
    }

    /// <summary>
    /// Scans the given base for the baked microphone LED dot below the mute button, or
    /// returns <c>null</c> when the bitmap or its pixel format is unavailable.
    /// </summary>
    private static List<(int X, int Y)>? ExtractMicLedMask(Bitmap? baseImage)
    {
        if (baseImage is null || baseImage.Format != PixelFormat.Bgra8888)
        {
            return null;
        }

        int width = baseImage.PixelSize.Width;
        int height = baseImage.PixelSize.Height;
        int rowBytes = width * 4;
        byte[] buffer = ReadPixels(baseImage, width, height, rowBytes);

        List<(int X, int Y)> pixels = new List<(int X, int Y)>();
        int xStart = Math.Min(MicLedBandXStart, width);
        int xEnd = Math.Min(MicLedBandXEnd, width);
        int yStart = Math.Min(MicLedBandYStart, height);
        int yEnd = Math.Min(MicLedBandYEnd, height);

        for (int y = yStart; y < yEnd; y++)
        {
            int row = y * rowBytes;
            for (int x = xStart; x < xEnd; x++)
            {
                int offset = row + x * 4;
                if (buffer[offset] + buffer[offset + 1] + buffer[offset + 2] > MicLedBrightness)
                {
                    pixels.Add((x, y));
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Gets the shared microphone LED sprite: an orange glow matching the mute button
    /// shape (bright pill with a soft halo fading to transparent), generated once. The
    /// control animates its opacity to show the LED steady or pulsing.
    /// </summary>
    public Bitmap GetMicLedSprite()
    {
        Bitmap? sprite = _micLedSprite;
        if (sprite is not null)
        {
            return sprite;
        }

        lock (_micLedSpriteLock)
        {
            if (_micLedSprite is null)
            {
                _micLedSprite = CreateMicLedSprite();
            }

            return _micLedSprite;
        }
    }

    /// <summary>
    /// Renders the orange glow used for the microphone LED: a rounded pill matching the
    /// mute button shape with a soft halo fading to transparent.
    /// </summary>
    private static Bitmap CreateMicLedSprite()
    {
        int sizeX = MicLedPillWidth + MicLedGlowMargin * 2;
        int sizeY = MicLedPillHeight + MicLedGlowMargin * 2;
        int rowBytes = sizeX * 4;
        byte[] buffer = new byte[rowBytes * sizeY];
        double centerX = (sizeX - 1) / 2.0;
        double centerY = (sizeY - 1) / 2.0;
        double straight = MicLedPillWidth / 2.0 - MicLedPillCornerRadius;

        for (int y = 0; y < sizeY; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < sizeX; x++)
            {
                double dx = Math.Max(Math.Abs(x - centerX) - straight, 0);
                double dy = Math.Abs(y - centerY);
                double dist = Math.Sqrt(dx * dx + dy * dy) - MicLedPillCornerRadius;
                if (dist >= MicLedGlowMargin)
                {
                    continue;
                }

                double intensity = dist <= 0
                    ? MicLedPeakIntensity
                    : MicLedPeakIntensity * (1 - dist / MicLedGlowMargin);
                int alpha = (int)(intensity * 255);

                int offset = row + x * 4;
                buffer[offset] = (byte)(MicLedCoreBlue * alpha / 255);
                buffer[offset + 1] = (byte)(MicLedCoreGreen * alpha / 255);
                buffer[offset + 2] = (byte)(MicLedCoreRed * alpha / 255);
                buffer[offset + 3] = (byte)alpha;
            }
        }

        WriteableBitmap sprite = new WriteableBitmap(new PixelSize(sizeX, sizeY), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer target = sprite.Lock())
        {
            Marshal.Copy(buffer, 0, target.Address, buffer.Length);
        }

        return sprite;
    }

    /// <summary>
    /// Copies the bitmap's raw BGRA pixels into a byte array.
    /// </summary>
    private static byte[] ReadPixels(Bitmap source, int width, int height, int stride)
    {
        byte[] buffer = new byte[stride * height];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    /// <summary>
    /// Loads a monitor base bitmap directly from a file name, or <c>null</c> on failure.
    /// Used for the shared lightbar mask so the tinted path never depends on a skin name.
    /// </summary>
    private static Bitmap? GetMonitorBaseFromFile(string file)
    {
        try
        {
            Uri uri = new Uri($"{BaseFolderUri}{Uri.EscapeDataString(file)}");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not load monitor base '{file}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the overlay sprite bitmap for the given name and skin, or <c>null</c> when it
    /// cannot be loaded. Dev Mode skins resolve the colored variant under <c>Buttons/Dev</c>
    /// and fall back to the standard sprite under <c>Buttons/Base</c> when no variant exists
    /// (e.g. the trigger and stick sprites). Loaded bitmaps are cached by sprite key.
    /// </summary>
    /// <param name="skin">The skin name (e.g. "Midnight Black").</param>
    /// <param name="name">The sprite file name without extension (e.g. "Cross").</param>
    public Bitmap? GetSprite(string skin, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        bool dev = skin.Equals(DevModeSkin, StringComparison.OrdinalIgnoreCase);
        string key = (dev ? "Dev:" : string.Empty) + name;

        if (_spriteCache.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        Bitmap? bitmap = dev ? TryLoadSprite("Dev", name, false) : null;
        bitmap ??= TryLoadSprite("Base", name);

        if (bitmap is not null)
        {
            _spriteCache[key] = bitmap;
        }

        return bitmap;
    }

    /// <summary>
    /// Loads a sprite bitmap from a subfolder of the buttons folder, or <c>null</c> on failure.
    /// </summary>
    /// <param name="folder">The sprite variant subfolder ("Base" or "Dev").</param>
    /// <param name="name">The sprite file name without extension.</param>
    /// <param name="logOnFailure">
    /// Whether a failed load is logged as a warning. Pass <c>false</c> for the expected
    /// Dev Mode variant lookup, whose fallback attempt still logs when it also fails.
    /// </param>
    private static Bitmap? TryLoadSprite(string folder, string name, bool logOnFailure = true)
    {
        try
        {
            Uri uri = new Uri($"{ButtonsFolderUri}{folder}/{Uri.EscapeDataString(name)}.png");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            if (logOnFailure)
            {
                _log.Warning($"Could not load overlay sprite '{folder}/{name}.png': {ex.Message}");
            }

            return null;
        }
    }
}