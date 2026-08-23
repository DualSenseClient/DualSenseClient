using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// The virtual controller kind rendered by <see cref="VirtualControllerTargetView"/>.
/// </summary>
public enum VirtualControllerKind
{
    /// <summary>A virtual Xbox 360 controller.</summary>
    Xbox360,

    /// <summary>A virtual DualShock 4 controller.</summary>
    DualShock4
}

/// <summary>
/// Clickable illustration of a virtual controller (Xbox 360 or DualShock 4) used to pick
/// remapping targets: clicking a button toggles that target in the pending selection, and
/// every selected one is highlighted with its pressed-state overlay sprite.
/// </summary>
public sealed class VirtualControllerTargetView : Canvas
{
    /// <summary>
    /// One clickable button area with its pressed-state overlay sprite (when it has one),
    /// positioned by center point in canvas pixels.
    /// </summary>
    private sealed record Hotspot(string Target, string? Overlay, double CenterX, double CenterY, double Width, double Height);

    /// <summary>
    /// A decorative sprite drawn beneath the base image (triggers, stick caps).
    /// </summary>
    private sealed record Decoration(string Image, double CenterX, double CenterY, double Width, double Height);

    /// <summary>
    /// Full render + hit-test layout of one controller kind, in canvas pixels.
    /// </summary>
    private sealed record Layout
    {
        public required double Width { get; init; }

        public required double Height { get; init; }

        public required string BaseImage { get; init; }

        public required double BaseCenterX { get; init; }

        public required double BaseCenterY { get; init; }

        public required double BaseWidth { get; init; }

        public required double BaseHeight { get; init; }

        public required IReadOnlyList<Decoration> Decorations { get; init; }

        public required IReadOnlyList<Hotspot> Hotspots { get; init; }
    }

    /// <summary>
    /// The controller kind to render.
    /// </summary>
    public static readonly StyledProperty<VirtualControllerKind> KindProperty =
        AvaloniaProperty.Register<VirtualControllerTargetView, VirtualControllerKind>(nameof(Kind));

    /// <summary>
    /// The raw target names currently picked on the illustration, or <c>null</c>.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<string>?> SelectedTargetsProperty =
        AvaloniaProperty.Register<VirtualControllerTargetView, IEnumerable<string>?>(nameof(SelectedTargets));

    /// <summary>
    /// Whether clicking buttons on the illustration picks them.
    /// </summary>
    public static readonly StyledProperty<bool> IsSelectionEnabledProperty =
        AvaloniaProperty.Register<VirtualControllerTargetView, bool>(nameof(IsSelectionEnabled));

    /// <summary>
    /// Display scale applied to the native canvas size of the selected layout.
    /// </summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<VirtualControllerTargetView, double>(nameof(Scale), 0.62);

    public VirtualControllerKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IEnumerable<string>? SelectedTargets
    {
        get => GetValue(SelectedTargetsProperty);
        set => SetValue(SelectedTargetsProperty, value);
    }

    public bool IsSelectionEnabled
    {
        get => GetValue(IsSelectionEnabledProperty);
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>
    /// Raised when the user clicks a button on the illustration while
    /// <see cref="IsSelectionEnabled"/> is set; carries the raw target name.
    /// </summary>
    public event EventHandler<string>? TargetClicked;

    private const string AssetPrefix = "avares://DualSenseClient/Assets/Controllers/";

    /// <summary>
    /// Loaded bitmaps, cached by asset URI.
    /// </summary>
    private static readonly Dictionary<string, Bitmap?> BitmapCache = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Render and hit-test layouts per controller kind, transcribed from the VSCView theme
    /// definitions (all element positions are "center" points in canvas pixels).
    /// </summary>
    private static readonly IReadOnlyDictionary<VirtualControllerKind, Layout> Layouts =
        new Dictionary<VirtualControllerKind, Layout>
        {
            [VirtualControllerKind.Xbox360] = new Layout
            {
                Width = 1095,
                Height = 690,
                BaseImage = "Xbox360/Base/Black.png",
                BaseCenterX = 549,
                BaseCenterY = 345,
                BaseWidth = 1072,
                BaseHeight = 664,
                Decorations =
                [
                    new Decoration("Xbox360/Buttons/LeftTrigger.png", 254, 64, 97, 102),
                    new Decoration("Xbox360/Buttons/RightTrigger.png", 864, 64, 95, 102),
                    new Decoration("Xbox360/Buttons/LeftStick.png", 218, 382, 148, 129),
                    new Decoration("Xbox360/Buttons/RightStick.png", 704, 545, 147, 124)
                ],
                Hotspots =
                [
                    new Hotspot("A", "Xbox360/Buttons/A.png", 873, 416, 89, 73),
                    new Hotspot("B", "Xbox360/Buttons/B.png", 965, 341, 86, 80),
                    new Hotspot("X", "Xbox360/Buttons/X.png", 791, 346, 88, 77),
                    new Hotspot("Y", "Xbox360/Buttons/Y.png", 883, 272, 87, 81),
                    new Hotspot("DPadUp", "Xbox360/Buttons/DPad_Up.png", 386, 477, 76, 81),
                    new Hotspot("DPadDown", "Xbox360/Buttons/DPad_Down.png", 386, 552, 76, 80),
                    new Hotspot("DPadLeft", "Xbox360/Buttons/DPad_Left.png", 341, 515, 95, 76),
                    new Hotspot("DPadRight", "Xbox360/Buttons/DPad_Right.png", 431, 515, 95, 75),
                    new Hotspot("Guide", "Xbox360/Buttons/Guide.png", 550, 349, 119, 98),
                    new Hotspot("Start", "Xbox360/Buttons/Start.png", 668, 351, 63, 44),
                    new Hotspot("Back", "Xbox360/Buttons/Back.png", 431, 351, 62, 44),
                    new Hotspot("LeftShoulder", "Xbox360/Buttons/LeftBumper_Active.png", 217, 152, 214, 97),
                    new Hotspot("RightShoulder", "Xbox360/Buttons/RightBumper_Active.png", 893, 150, 195, 97),
                    new Hotspot("LeftTrigger", "Xbox360/Buttons/LeftTrigger_Active.png", 254, 64, 97, 102),
                    new Hotspot("RightTrigger", "Xbox360/Buttons/RightTrigger_Active.png", 864, 64, 95, 102),
                    new Hotspot("LeftThumb", "Xbox360/Buttons/LeftStick_Click.png", 224, 359, 164, 137),
                    new Hotspot("RightThumb", "Xbox360/Buttons/RightStick_Click.png", 703, 516, 162, 134)
                ]
            },
            [VirtualControllerKind.DualShock4] = new Layout
            {
                Width = 1150,
                Height = 780,
                BaseImage = "DualShock4/Base/Black.png",
                BaseCenterX = 575,
                BaseCenterY = 311,
                BaseWidth = 1091,
                BaseHeight = 583,
                Decorations =
                [
                    new Decoration("DualShock4/Buttons/L2.png", 252, 55, 123, 70),
                    new Decoration("DualShock4/Buttons/R2.png", 898, 56, 123, 70),
                    new Decoration("DualShock4/Buttons/LeftStick.png", 407, 464, 123, 109),
                    new Decoration("DualShock4/Buttons/RightStick.png", 744, 464, 123, 109)
                ],
                Hotspots =
                [
                    // All four face buttons share one highlight glyph, placed on each position.
                    new Hotspot("Cross", "DualShock4/Buttons/Face.png", 904, 385, 73, 67),
                    new Hotspot("Circle", "DualShock4/Buttons/Face.png", 981, 317, 73, 67),
                    new Hotspot("Square", "DualShock4/Buttons/Face.png", 826, 317, 73, 67),
                    new Hotspot("Triangle", "DualShock4/Buttons/Face.png", 903, 249, 73, 67),
                    new Hotspot("DPadUp", "DualShock4/Buttons/DPad_Up.png", 247, 273, 66, 72),
                    new Hotspot("DPadDown", "DualShock4/Buttons/DPad_Down.png", 247, 363, 66, 77),
                    new Hotspot("DPadLeft", "DualShock4/Buttons/DPad_Left.png", 196, 318, 81, 63),
                    new Hotspot("DPadRight", "DualShock4/Buttons/DPad_Right.png", 297, 319, 81, 62),
                    new Hotspot("PS", "DualShock4/Buttons/PS.png", 575, 429, 65, 45),
                    new Hotspot("Options", "DualShock4/Buttons/OptionsShare.png", 791, 219, 39, 63),
                    new Hotspot("Share", "DualShock4/Buttons/OptionsShare.png", 359, 219, 39, 63),
                    new Hotspot("L1", "DualShock4/Buttons/L1_Active.png", 249, 108, 148, 74),
                    new Hotspot("R1", "DualShock4/Buttons/R1_Active.png", 901, 108, 148, 73),
                    new Hotspot("L2", "DualShock4/Buttons/L2_Active.png", 252, 55, 134, 80),
                    new Hotspot("R2", "DualShock4/Buttons/R2_Active.png", 898, 56, 133, 79),
                    new Hotspot("L3", "DualShock4/Buttons/AnalogStick_Click.png", 406, 445, 145, 128),
                    new Hotspot("R3", "DualShock4/Buttons/AnalogStick_Click.png", 744, 445, 145, 128),
                    new Hotspot("Touchpad", "DualShock4/Buttons/Touchpad_Click.png", 575, 239, 359, 215)
                ]
            }
        };

    static VirtualControllerTargetView()
    {
        KindProperty.Changed.AddClassHandler<VirtualControllerTargetView>((view, _) => view.Rebuild());
        SelectedTargetsProperty.Changed.AddClassHandler<VirtualControllerTargetView>((view, _) => view.UpdateSelectionMarker());
        ScaleProperty.Changed.AddClassHandler<VirtualControllerTargetView>((view, _) => view.Rebuild());
    }

    /// <summary>
    /// Builds the initial sprite tree. Required because binding a value equal to the
    /// property default (e.g. the Xbox 360 kind) raises no change notification.
    /// </summary>
    public VirtualControllerTargetView()
    {
        Rebuild();
    }

    /// <summary>
    /// Loads a cached bitmap for an asset path relative to the virtual controllers folder.
    /// </summary>
    private static Bitmap? LoadBitmap(string relativePath)
    {
        string uri = AssetPrefix + relativePath;
        if (BitmapCache.TryGetValue(uri, out Bitmap? cached))
        {
            return cached;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            Bitmap bitmap = new Bitmap(stream);
            BitmapCache[uri] = bitmap;
            return bitmap;
        }
        catch (Exception)
        {
            BitmapCache[uri] = null;
            return null;
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Children.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Rebuilds the sprite tree for the current kind and scale.
    /// </summary>
    private void Rebuild()
    {
        Children.Clear();
        Layout layout = Layouts[Kind];
        double scale = Scale;

        Width = layout.Width * scale;
        Height = layout.Height * scale;

        foreach (Decoration decoration in layout.Decorations)
        {
            AddSprite(decoration.Image, decoration.CenterX, decoration.CenterY, decoration.Width, decoration.Height, scale);
        }

        AddSprite(layout.BaseImage, layout.BaseCenterX, layout.BaseCenterY, layout.BaseWidth, layout.BaseHeight, scale);
        UpdateSelectionMarker();
    }

    /// <summary>
    /// Adds the selected targets' pressed overlay sprites, if any.
    /// </summary>
    private void UpdateSelectionMarker()
    {
        // Remove previous markers: they are the children added after the base image.
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i] is Image { Tag: "selection" })
            {
                Children.RemoveAt(i);
            }
        }

        HashSet<string>? selected = SelectedTargets is not null ? [.. SelectedTargets] : null;
        if (selected is null || selected.Count == 0)
        {
            return;
        }

        Layout layout = Layouts[Kind];
        foreach (Hotspot hotspot in layout.Hotspots)
        {
            if (hotspot.Overlay is null || !selected.Contains(hotspot.Target))
            {
                continue;
            }

            Image marker = AddSprite(hotspot.Overlay, hotspot.CenterX, hotspot.CenterY, hotspot.Width, hotspot.Height, Scale);
            marker.Tag = "selection";
            marker.ZIndex = 100;
        }
    }

    /// <summary>
    /// Adds one centered sprite at the given canvas-space center point.
    /// </summary>
    private Image AddSprite(string image, double centerX, double centerY, double width, double height, double scale)
    {
        Image control = new Image
        {
            Source = LoadBitmap(image),
            Width = width * scale,
            Height = height * scale,
            Stretch = Stretch.Fill
        };
        SetLeft(control, (centerX - width / 2) * scale);
        SetTop(control, (centerY - height / 2) * scale);
        Children.Add(control);
        return control;
    }

    /// <summary>
    /// Hit-tests a pointer press against the hotspots and raises <see cref="TargetClicked"/>
    /// for the first match.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsSelectionEnabled || TargetClicked is null)
        {
            return;
        }

        Point position = e.GetCurrentPoint(this).Position;
        Layout layout = Layouts[Kind];
        foreach (Hotspot hotspot in layout.Hotspots)
        {
            double left = (hotspot.CenterX - hotspot.Width / 2) * Scale;
            double top = (hotspot.CenterY - hotspot.Height / 2) * Scale;
            if (position.X >= left && position.X <= left + hotspot.Width * Scale
                                   && position.Y >= top && position.Y <= top + hotspot.Height * Scale)
            {
                TargetClicked?.Invoke(this, hotspot.Target);
                return;
            }
        }
    }
}