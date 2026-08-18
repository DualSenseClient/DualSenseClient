using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Provides DualSense controller illustration skins embedded as Avalonia resources under
/// <c>Assets/DualSense/Skins</c>. Skin names are the embedded file names (e.g. "Cosmic Red"),
/// each mapping to a single <c>.png</c> controller illustration.
/// </summary>
/// <remarks>
/// Skins are enumerated at runtime from the assembly's Avalonia resource index, so adding a
/// new <c>*.png</c> file to the skin folder automatically makes it available as a skin.
/// </remarks>
public sealed class ControllerIllustrationService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Illustration");

    /// <summary>
    /// Embedded resource path prefix of the skin folder (avares URI form).
    /// </summary>
    private static readonly string SkinsFolderUri = $"avares://{typeof(ControllerIllustrationService).Assembly.GetName().Name}/Assets/DualSense/Skins/";

    /// <summary>
    /// Loaded skin bitmaps, cached by skin name.
    /// </summary>
    private readonly Dictionary<string, Bitmap> _bitmapCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

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
}