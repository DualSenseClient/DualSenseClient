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
    /// Maximum number of lightbar-tinted monitor base bitmaps kept in memory. A tint is
    /// ~4.8 MB, so this caps the cache at ~38 MB.
    /// </summary>
    private const int MaxMonitorBaseTintCacheSize = 8;

    /// <summary>
    /// Synchronizes mutations to the tinted monitor base LRU cache.
    /// </summary>
    private readonly object _monitorBaseTintLock = new object();

    /// <summary>
    /// Lightbar-tinted monitor base bitmaps, cached by file name and lightbar color.
    /// </summary>
    private readonly
        Dictionary<(string File, byte R, byte G, byte B), LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)>>
        _monitorBaseTintCache =
            new Dictionary<(string File, byte R, byte G, byte B),
                LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)>>();

    /// <summary>
    /// LRU tracking order for tinted monitor base cache entries.
    /// </summary>
    private readonly LinkedList<((string File, byte R, byte G, byte B) Key, Bitmap Value)> _monitorBaseTintLru =
        new LinkedList<((string File, byte R, byte G, byte B) Key, Bitmap Value)>();

    /// <summary>
    /// Raw un-tinted BGRA base pixel buffers cached by file name so that tinting does not repeatedly re-read the source bitmap.
    /// </summary>
    private readonly Dictionary<string, byte[]> _basePixelBuffers = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

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
    /// The player LED sprite (a lit capsule with its light beam), generated once and shared
    /// by every controller view.
    /// </summary>
    private static Bitmap? _playerLedSprite;

    /// <summary>
    /// Synchronizes lazy generation of <see cref="_playerLedSprite"/>.
    /// </summary>
    private static readonly object _playerLedSpriteLock = new object();

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
    /// Plastic color of the monitor bases around the player LED band, used both to fill the
    /// band and to fade the lightbar's side strips into it.
    /// </summary>
    private const byte BandRed = 23;

    /// <summary>
    /// Plastic color of the monitor bases around the player LED band (green channel).
    /// </summary>
    private const byte BandGreen = 25;

    /// <summary>
    /// Plastic color of the monitor bases around the player LED band (blue channel).
    /// </summary>
    private const byte BandBlue = 30;

    /// <summary>
    /// Number of asset rows above the player LED band over which the lightbar's side strips
    /// fade into the band's plastic instead of ending in a hard edge.
    /// </summary>
    private const int LightbarFadeRows = 44;

    /// <summary>
    /// Width of one player LED sprite in asset pixels: the light beam plus its transparent
    /// side margins.
    /// </summary>
    public const int PlayerLedSpriteWidth = 68;

    /// <summary>
    /// Height of one player LED sprite in asset pixels. The sprite covers exactly the rows of
    /// the band below the touchpad, so its top row sits on the touchpad line and its bottom
    /// row on the line below the band.
    /// </summary>
    public const int PlayerLedSpriteHeight = 14;

    /// <summary>
    /// Horizontal radius of the light beam rising out of a player LED, in asset pixels.
    /// </summary>
    private const double PlayerLedBeamRadiusX = 28;

    /// <summary>
    /// Vertical radius of the light beam, in asset pixels: the beam stops at the touchpad line.
    /// </summary>
    private const double PlayerLedBeamRadiusY = 14;

    /// <summary>
    /// Opacity of the light beam at the player LED.
    /// </summary>
    private const double PlayerLedBeamPeak = 1.0;

    /// <summary>
    /// Falloff exponent of the light beam: lower values keep the beam brighter further out.
    /// </summary>
    private const double PlayerLedBeamFalloff = 0.55;

    /// <summary>
    /// Width of the lit capsule in asset pixels.
    /// </summary>
    private const double PlayerLedCapsuleWidth = 28;

    /// <summary>
    /// Height of the lit capsule in asset pixels: half its full capsule height, so the shape
    /// is the top half of a capsule resting on the line below the band.
    /// </summary>
    private const double PlayerLedCapsuleHeight = 6;

    /// <summary>
    /// Opacity of the lit capsule.
    /// </summary>
    private const double PlayerLedCapsuleOpacity = 0.97;

    /// <summary>
    /// Red channel of a lit player LED (near-white, like the physical indicator LEDs).
    /// </summary>
    private const byte PlayerLedRed = 252;

    /// <summary>
    /// Green channel of a lit player LED.
    /// </summary>
    private const byte PlayerLedGreen = 253;

    /// <summary>
    /// Blue channel of a lit player LED.
    /// </summary>
    private const byte PlayerLedBlue = 255;

    /// <summary>
    /// Resolution multiplier of the generated player LED sprite. The sprite is rendered this
    /// many times larger than its asset-space footprint so the view can scale it down without
    /// aliasing the capsule edge or banding the beam gradient.
    /// </summary>
    private const int PlayerLedSpriteRenderScale = 4;

    /// <summary>
    /// Minimum number of mask pixels a row needs to count as part of the lightbar's
    /// horizontal bottom segment (the line below the touchpad). Side rows carry only the
    /// two vertical strips (at most ~80 pixels), the line rows carry the full run (~200
    /// and up) in the default skin the shared mask is extracted from.
    /// </summary>
    private const int BottomSegmentMinRowCount = 150;

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
            _log.Warning($"Could not load illustration skin '{skin}'", ex);
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
            _log.Warning($"Could not load monitor base '{file}'", ex);

            return string.Equals(file, GetBaseFile(DefaultSkin), StringComparison.OrdinalIgnoreCase)
                ? null
                : GetMonitorBase(DefaultSkin);
        }
    }

    /// <summary>
    /// Gets the monitor base bitmap for a skin with its baked lightbar recolored to the
    /// given RGB, or <c>null</c> when it cannot be loaded. The lightbar pixels are found
    /// through the shared mask (see <see cref="GetLightbarMask"/>) and recolored while
    /// preserving the baked falloff, normalized to the skin's brightest strip pixel so the
    /// requested color renders at full strength. The segment below the touchpad is filled
    /// with the body plastic instead, because the physical controller shows the player LEDs
    /// there rather than the lightbar. Tinted bitmaps are cached by file name and color.
    /// </summary>
    /// <param name="skin">The skin name (e.g. "Midnight Black"), falling back to the default skin.</param>
    /// <param name="red">Lightbar red channel (0-255).</param>
    /// <param name="green">Lightbar green channel (0-255).</param>
    /// <param name="blue">Lightbar blue channel (0-255).</param>
    public Bitmap? GetMonitorBase(string? skin, byte red, byte green, byte blue)
    {
        string file = GetBaseFile(skin);

        (string file, byte red, byte green, byte blue) key = (file, red, green, blue);
        lock (_monitorBaseTintLock)
        {
            if (_monitorBaseTintCache.TryGetValue(key, out LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)>? node))
            {
                _monitorBaseTintLru.Remove(node);
                _monitorBaseTintLru.AddFirst(node);
                return node.Value.Value;
            }
        }

        Bitmap? original = GetMonitorBase(skin ?? string.Empty);
        bool dev = string.Equals(skin, DevModeSkin, StringComparison.OrdinalIgnoreCase);
        Bitmap? tinted = original is null ? null : TintLightbar(file, original, red, green, blue, dev);
        if (tinted is not null)
        {
            lock (_monitorBaseTintLock)
            {
                if (_monitorBaseTintCache.TryGetValue(key,
                        out LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)>? existingNode))
                {
                    tinted.Dispose();
                    _monitorBaseTintLru.Remove(existingNode);
                    _monitorBaseTintLru.AddFirst(existingNode);
                    return existingNode.Value.Value;
                }

                while (_monitorBaseTintCache.Count >= MaxMonitorBaseTintCacheSize && _monitorBaseTintLru.Last is { } oldest)
                {
                    // A view may still display the evicted bitmap, so only drop the cache
                    // reference and let it be collected once its last view releases it.
                    _monitorBaseTintCache.Remove(oldest.Value.Key);
                    _monitorBaseTintLru.RemoveLast();
                }

                LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)> newNode =
                    new LinkedListNode<((string File, byte R, byte G, byte B) Key, Bitmap Value)>((key, tinted));
                _monitorBaseTintLru.AddFirst(newNode);
                _monitorBaseTintCache[key] = newNode;
            }
        }

        return tinted ?? original;
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> whose lightbar pixels are recolored to
    /// the given RGB, or <c>null</c> when the mask or the pixel format are unavailable. Only
    /// the two vertical side segments carry the lightbar color: the segment right below the
    /// touchpad is filled with the body plastic, because the physical controller shows the
    /// player LEDs there rather than the lightbar. Each skin bakes the strip at its own
    /// brightness, so the baked luminance is normalized to that skin's peak and the
    /// requested color renders at full strength. The side strips fade into the plastic band
    /// instead of ending in a hard edge. Dev Mode bases render the lightbar in black, so they
    /// are recolored from the default render's luminance instead.
    /// </summary>
    private Bitmap? TintLightbar(string file, Bitmap source, byte red, byte green, byte blue, bool useSharedLuminance)
    {
        List<(int X, int Y, byte Luminance)> mask = GetLightbarMask();
        if (mask.Count == 0 || source.Format != PixelFormat.Bgra8888)
        {
            return null;
        }

        int width = source.PixelSize.Width;
        int height = source.PixelSize.Height;
        int rowBytes = width * 4;

        byte[] template;
        lock (_basePixelBuffers)
        {
            if (!_basePixelBuffers.TryGetValue(file, out byte[]? cachedTemplate))
            {
                cachedTemplate = ReadPixels(source, width, height, rowBytes);
                _basePixelBuffers[file] = cachedTemplate;
            }

            template = cachedTemplate;
        }

        byte[] buffer = new byte[template.Length];
        Buffer.BlockCopy(template, 0, buffer, 0, template.Length);
        int bandStart = BottomSegmentStartRow(mask);

        // First pass: the luminance of every lightbar pixel above the band, so the skin's
        // peak can be found and the requested color scales to exactly its full value there.
        double[] luminances = new double[mask.Count];
        int peak = 0;
        for (int i = 0; i < mask.Count; i++)
        {
            (int x, int y, byte luminance) = mask[i];
            if ((uint)x >= (uint)width || (uint)y >= (uint)height || y >= bandStart)
            {
                continue;
            }

            if (!useSharedLuminance)
            {
                int offset = y * rowBytes + x * 4;
                luminance = (byte)((buffer[offset] + buffer[offset + 1] + buffer[offset + 2]) / 3);
            }

            luminances[i] = luminance;
            if (luminance > peak)
            {
                peak = luminance;
            }
        }

        double normalization = peak > 0 ? 255.0 / peak : 0;
        for (int i = 0; i < mask.Count; i++)
        {
            (int x, int y, _) = mask[i];
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                continue;
            }

            int offset = y * rowBytes + x * 4;

            // The segment below the touchpad is body plastic on the physical controller; the
            // player LED sprites are drawn on top of it by the view.
            if (y >= bandStart)
            {
                buffer[offset] = BandBlue;
                buffer[offset + 1] = BandGreen;
                buffer[offset + 2] = BandRed;
                continue;
            }

            double intensity = luminances[i] * normalization / 255.0;
            double fade = LightbarFade(bandStart - y);
            buffer[offset] = BlendChannel(BandBlue, blue * intensity, fade);
            buffer[offset + 1] = BlendChannel(BandGreen, green * intensity, fade);
            buffer[offset + 2] = BlendChannel(BandRed, red * intensity, fade);
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
    /// Fade factor of a lightbar pixel at the given distance above the player LED band: 1 far
    /// from the band and 0 on its top edge, with a smooth ramp in between so the side strips
    /// taper into the plastic instead of ending in a hard line.
    /// </summary>
    private static double LightbarFade(int rowsAboveBand)
    {
        if (rowsAboveBand >= LightbarFadeRows)
        {
            return 1;
        }

        if (rowsAboveBand <= 0)
        {
            return 0;
        }

        double t = rowsAboveBand / (double)LightbarFadeRows;
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Blends one lightbar channel from the body plastic at <paramref name="fade"/> 0 to the
    /// given lit value at 1.
    /// </summary>
    private static byte BlendChannel(byte plastic, double lit, double fade)
        => (byte)Math.Clamp(plastic + (lit - plastic) * fade, 0, 255);

    /// <summary>
    /// Finds the first row of the lightbar's horizontal bottom segment (the line below the
    /// touchpad): scanning upward from the mask's lowest row, rows belong to the segment
    /// while they are filled across, unlike the sparse side strips.
    /// </summary>
    private static int BottomSegmentStartRow(List<(int X, int Y, byte Luminance)> mask)
    {
        Dictionary<int, int> rowCounts = new Dictionary<int, int>();
        int maxY = 0;
        foreach ((int x, int y, _) in mask)
        {
            rowCounts[y] = rowCounts.TryGetValue(y, out int count) ? count + 1 : 1;
            if (y > maxY)
            {
                maxY = y;
            }
        }

        int start = maxY + 1;
        while (rowCounts.TryGetValue(start - 1, out int count) && count >= BottomSegmentMinRowCount)
        {
            start--;
        }

        return start;
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
    /// Gets the shared player LED sprite: a lit capsule resting on the line below the band,
    /// with a half-ellipse of light rising from it that stops at the touchpad line. Generated
    /// once; the view shows one copy per lit LED.
    /// </summary>
    public Bitmap GetPlayerLedSprite()
    {
        Bitmap? sprite = _playerLedSprite;
        if (sprite is not null)
        {
            return sprite;
        }

        lock (_playerLedSpriteLock)
        {
            if (_playerLedSprite is null)
            {
                _playerLedSprite = CreatePlayerLedSprite();
            }

            return _playerLedSprite;
        }
    }

    /// <summary>
    /// Renders the player LED sprite: the top half of a capsule sitting on the sprite's
    /// bottom row, plus a half-ellipse of light rising from it that stops at the sprite's
    /// top row (the touchpad line). Supersampled for smooth edges.
    /// </summary>
    private static Bitmap CreatePlayerLedSprite()
    {
        int scale = PlayerLedSpriteRenderScale;
        int assetWidth = PlayerLedSpriteWidth;
        int assetHeight = PlayerLedSpriteHeight;
        int width = assetWidth * scale;
        int height = assetHeight * scale;
        int rowBytes = width * 4;
        byte[] buffer = new byte[rowBytes * height];

        for (int y = 0; y < height; y++)
        {
            double dy = (y + 0.5) / scale - assetHeight;
            for (int x = 0; x < width; x++)
            {
                double dx = (x + 0.5) / scale - assetWidth / 2.0;
                int alpha = (int)Math.Round(PlayerLedAlpha(dx, dy) * 255);
                int offset = y * rowBytes + x * 4;
                buffer[offset] = (byte)(PlayerLedBlue * alpha / 255);
                buffer[offset + 1] = (byte)(PlayerLedGreen * alpha / 255);
                buffer[offset + 2] = (byte)(PlayerLedRed * alpha / 255);
                buffer[offset + 3] = (byte)alpha;
            }
        }

        WriteableBitmap sprite = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer target = sprite.Lock())
        {
            Marshal.Copy(buffer, 0, target.Address, buffer.Length);
        }

        return sprite;
    }

    /// <summary>
    /// Opacity of one point of the player LED sprite, taken as an offset from the middle of
    /// the capsule's base line; <paramref name="dy"/> is negative above that line.
    /// </summary>
    private static double PlayerLedAlpha(double dx, double dy)
    {
        double alpha = 0;
        double beamX = dx / PlayerLedBeamRadiusX;
        double beamY = dy / PlayerLedBeamRadiusY;

        if (dy <= 0)
        {
            double distance = Math.Sqrt(beamX * beamX + beamY * beamY);
            if (distance < 1)
            {
                alpha = PlayerLedBeamPeak * Math.Pow(1 - distance, PlayerLedBeamFalloff);
            }
        }

        // Top half of a capsule: flat base on the line, rounded top corners, so the lit window
        // reads as cut off by the seam instead of floating inside the band.
        if (dy >= -PlayerLedCapsuleHeight && dy <= 0)
        {
            double radius = PlayerLedCapsuleHeight;
            double absX = Math.Abs(dx);
            double overhang = absX - (PlayerLedCapsuleWidth / 2 - radius);
            if (overhang <= 0 || overhang * overhang + dy * dy <= radius * radius)
            {
                alpha = Math.Max(alpha, PlayerLedCapsuleOpacity);
            }
        }

        return Math.Min(alpha, 1);
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
            _log.Warning($"Could not load monitor base '{file}'", ex);
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
                _log.Warning($"Could not load overlay sprite '{folder}/{name}.png'", ex);
            }

            return null;
        }
    }
}