using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.GUI.Views;
using DualSenseClient.Hid;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Manages the application's system tray icon. The tray icon shows the active
/// controller's battery percentage as plain numbers, or the app icon when no
/// controller with a known battery level is active or the battery percentage
/// display is disabled. The tray menu shows the window, lists connected
/// controllers (with their name and battery percentage) for selection and
/// profile switching, and exits the application.
/// </summary>
public sealed class TrayIconService
{
    /// <summary>
    /// Side length of the rendered percentage icon, in pixels. The OS downscales it to
    /// the actual tray size, so a higher resolution keeps the percentage text crisp.
    /// </summary>
    private const int IconSize = 64;

    /// <summary>
    /// The main application window, shown and hidden by the tray menu.
    /// </summary>
    private readonly MainWindow _mainWindow;

    /// <summary>
    /// ViewModel owning the controller list shown in the tray menu.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Tracks the active controller; the tray icon's battery percentage is read from
    /// <see cref="IControllerTracker.ActiveController"/> regardless of the selection
    /// in the main window.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Profile service listing the profiles offered in each controller's tray menu.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// Service storing each controller's display name and bound profile.
    /// </summary>
    private readonly ControllerInfoService _controllerInfoService;

    /// <summary>
    /// Settings service providing the tray icon's battery percentage visibility.
    /// </summary>
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Theme service whose changes re-render the fallback icon, because its accent
    /// color follows the active theme.
    /// </summary>
    private readonly ThemeService _themeService;

    /// <summary>
    /// Emulation service used to recreate the virtual controller when its mode is
    /// changed from the tray menu.
    /// </summary>
    private readonly IEmulationService _emulation;

    /// <summary>
    /// The system tray icon.
    /// </summary>
    private readonly TrayIcon _trayIcon;

    /// <summary>
    /// The tray menu, rebuilt whenever the controller list, selection, battery,
    /// or profiles change.
    /// </summary>
    private readonly NativeMenu _menu = new NativeMenu();

    /// <summary>
    /// The accent color the fallback icon was last rendered with. The icon is
    /// re-rendered when the active theme's accent color changes.
    /// </summary>
    private Color _lastAppIconAccent;

    /// <summary>
    /// The active controller currently being watched for battery changes.
    /// </summary>
    private DualSenseDevice? _batterySource;

    /// <summary>
    /// The battery percentage of the last rendered icon, or <c>null</c> when the
    /// percentage is not shown. Guards against re-rendering identical icons.
    /// </summary>
    private int? _lastPercentage;

    /// <summary>
    /// The text color of the last rendered icon, so the icon is re-rendered when
    /// the active theme changes the color.
    /// </summary>
    private Color _lastTextColor;

    /// <summary>
    /// Whether the tray icon currently shows the battery percentage.
    /// Kept in sync with <see cref="UiSettings.ShowBatteryPercentage"/>.
    /// </summary>
    private bool _showBatteryPercentage;

    /// <summary>
    /// Periodic refresh so the icon appears as soon as the active controller's
    /// first input report arrives, even when the battery value does not change
    /// (battery events only fire on changes).
    /// </summary>
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// Creates the tray icon and wires it to the application's controller state.
    /// </summary>
    public TrayIconService(MainWindow mainWindow, MainViewModel mainViewModel, IControllerTracker tracker, ProfileService profileService,
        ControllerInfoService controllerInfoService, SettingsService settingsService, IEmulationService emulation, ThemeService themeService)
    {
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _tracker = tracker;
        _profileService = profileService;
        _controllerInfoService = controllerInfoService;
        _settingsService = settingsService;
        _emulation = emulation;
        _themeService = themeService;

        _lastAppIconAccent = GetAccentColor();
        _trayIcon = new TrayIcon
        {
            ToolTipText = LocalizationService.GetText("MainWindow.Title"),
            Menu = _menu,
            Icon = CreateAppIcon(_lastAppIconAccent),
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons
        {
            _trayIcon
        });

        _showBatteryPercentage = _settingsService.Settings.Ui.ShowBatteryPercentage;
        _settingsService.SettingsChanged += OnSettingsChanged;

        // Re-render immediately on theme switches: the fallback icon's accent tile and
        // the percentage icon's text color both follow the theme. The variant-changed
        // event covers the OS switching light/dark while the System theme follows it,
        // which never raises the service's own ThemeChanged.
        _themeService.ThemeChanged += OnThemeChanged;
        Application.Current!.ActualThemeVariantChanged += OnActualThemeVariantChanged;

        _mainViewModel.Controllers.CollectionChanged += OnControllersChanged;
        _tracker.ActiveControllerChanged += OnActiveControllerChanged;
        _profileService.ProfilesChanged += OnSettingsChanged;
        _controllerInfoService.ControllersChanged += OnSettingsChanged;
        _emulation.StateChanged += OnEmulationStateChanged;

        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => UpdateTrayState());

        UpdateBatterySource();
        _refreshTimer.Start();
        RebuildMenu();
    }

    /// <summary>
    /// Shows and activates the main window, restoring it if it was minimized.
    /// </summary>
    private void ShowWindow()
    {
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Exits the application. The window's close handler lets the close through once
    /// <see cref="App.IsExiting"/> is set, so the tray menu is the only way to quit.
    /// </summary>
    private void ExitApplication()
    {
        App.IsExiting = true;
        App.Desktop?.Shutdown();
    }

    /// <summary>
    /// Rebuilds the tray menu from the current controller list, selection, and profiles.
    /// </summary>
    private void RebuildMenu()
    {
        _menu.Items.Clear();

        NativeMenuItem showItem = new NativeMenuItem(LocalizationService.GetText("Tray.Show"));
        showItem.Click += (_, _) => ShowWindow();
        _menu.Items.Add(showItem);

        _menu.Items.Add(new NativeMenuItemSeparator());

        if (_mainViewModel.Controllers.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem(LocalizationService.GetText("Tray.NoControllers"))
            {
                IsEnabled = false
            });
        }
        else
        {
            foreach (ControllerItem item in _mainViewModel.Controllers)
            {
                _menu.Items.Add(BuildControllerItem(item));
            }
        }

        _menu.Items.Add(new NativeMenuItemSeparator());

        NativeMenuItem exitItem = new NativeMenuItem(LocalizationService.GetText("Tray.Exit"));
        exitItem.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exitItem);
    }

    /// <summary>
    /// Builds the menu entry for one connected controller: its name and battery
    /// percentage, with a submenu to select it, change its bound profile, change its
    /// virtual controller emulation mode, and disconnect it (Bluetooth only).
    /// </summary>
    private NativeMenuItem BuildControllerItem(ControllerItem item)
    {
        string label = item.DisplayName;
        if (item.Device is DualSenseDevice device && device.InputReport is { } report)
        {
            int percentage = report.Battery.DisplayPercentage;
            if (percentage >= 0)
            {
                label = $"{label} ({percentage}%)";
            }
        }

        NativeMenuItem controllerItem = new NativeMenuItem(label);
        NativeMenu subMenu = new NativeMenu();

        NativeMenuItem selectItem = new NativeMenuItem(LocalizationService.GetText("Tray.Select"))
        {
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = ReferenceEquals(item, _mainViewModel.SelectedItem)
        };
        selectItem.Click += (_, _) => _mainViewModel.SelectedItem = item;
        subMenu.Items.Add(selectItem);

        NativeMenuItem profilesItem = new NativeMenuItem(LocalizationService.GetText("Tray.Profiles"))
        {
            Menu = BuildProfilesMenu(item)
        };
        subMenu.Items.Add(profilesItem);

        if (item.Device is DualSenseDevice emulationDevice)
        {
            NativeMenuItem emulationItem = new NativeMenuItem(LocalizationService.GetText("Tray.Emulation"))
            {
                Menu = BuildEmulationMenu(item),
                IsEnabled = !_emulation.GetStatus(emulationDevice).IsCreating
            };
            subMenu.Items.Add(emulationItem);
        }

        if (item.Device.ConnectionType == ConnectionType.Bluetooth)
        {
            NativeMenuItem disconnectItem = new NativeMenuItem(LocalizationService.GetText("Tray.Disconnect"));
            disconnectItem.Click += (_, _) => _ = _mainViewModel.DisconnectControllerAsync(item.Device);
            subMenu.Items.Add(disconnectItem);
        }

        controllerItem.Menu = subMenu;
        return controllerItem;
    }

    /// <summary>
    /// Builds the virtual controller emulation submenu for a controller. The mode stored
    /// in the controller's own emulation settings is checked; choosing a mode persists
    /// it and recreates the virtual controller through <see cref="IEmulationService"/>.
    /// </summary>
    private NativeMenu BuildEmulationMenu(ControllerItem item)
    {
        NativeMenu menu = new NativeMenu();
        EmulationSettings settings = GetEmulationSettings(item);
        EmulationMode current = settings.Mode;
        bool supported = EmulationService.IsSupported;

        foreach (EmulationMode mode in Enum.GetValues<EmulationMode>())
        {
            NativeMenuItem modeItem = new NativeMenuItem(LocalizationService.GetText($"VirtualControllerPage.Emulation.Mode.{mode}"))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mode == current,
                IsEnabled = supported
            };
            modeItem.Click += (_, _) => ApplyEmulationMode(item, mode);
            menu.Items.Add(modeItem);
        }

        return menu;
    }

    /// <summary>
    /// Gets the emulation settings stored for a controller, defaulting to emulation off,
    /// matching the emulation service's resolution.
    /// </summary>
    private EmulationSettings GetEmulationSettings(ControllerItem item)
    {
        string? mac = (item.Device as DualSenseDevice)?.PairingInfo?.ClientMac;
        return _controllerInfoService.GetEmulationSettings(mac, item.Device.Info.Path);
    }

    /// <summary>
    /// Sets the emulation mode on the controller's own emulation settings, persists it,
    /// and recreates the virtual controller. The menu rebuilds via the controller info
    /// save notification so the checkmark moves.
    /// </summary>
    private void ApplyEmulationMode(ControllerItem item, EmulationMode mode)
    {
        if (!EmulationService.IsSupported || item.Device is not DualSenseDevice device)
        {
            return;
        }

        EmulationSettings settings = GetEmulationSettings(item);
        if (settings.Mode == mode)
        {
            return;
        }

        settings.Mode = mode;
        _controllerInfoService.SaveEmulationSettings(device.PairingInfo?.ClientMac, device.Info.Path, settings);
        _emulation.Refresh();
    }

    /// <summary>
    /// Builds the profile submenu for a controller. The profile the controller is
    /// currently using (its bound profile, or the default when unbound) is checked;
    /// choosing a profile binds it to the controller and applies it.
    /// </summary>
    private NativeMenu BuildProfilesMenu(ControllerItem item)
    {
        NativeMenu menu = new NativeMenu();
        string? mac = (item.Device as DualSenseDevice)?.PairingInfo?.ClientMac;
        string path = item.Device.Info.Path;
        string usedProfile = _controllerInfoService.GetBoundProfileName(mac, path) ?? ProfileService.DefaultProfileName;

        foreach (Profile profile in _profileService.Settings.Profiles)
        {
            NativeMenuItem profileItem = new NativeMenuItem(profile.Name)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(profile.Name, usedProfile, StringComparison.OrdinalIgnoreCase)
            };
            profileItem.Click += (_, _) => ApplyProfile(item, profile);
            menu.Items.Add(profileItem);
        }

        return menu;
    }

    /// <summary>
    /// Binds the profile to the controller, applies it immediately, and refreshes
    /// the tray menu so the checkmark moves.
    /// </summary>
    private void ApplyProfile(ControllerItem item, Profile profile)
    {
        if (item.Device is not DualSenseDevice device)
        {
            return;
        }

        _controllerInfoService.SetControllerProfile(device.PairingInfo?.ClientMac, device.Info.Path, profile.Name);
        device.ApplyProfile(profile);
    }

    /// <summary>
    /// Watches the active controller for battery changes. Called when the tracker's
    /// active controller changes; battery events themselves are dispatched from the
    /// read loop thread.
    /// </summary>
    private void UpdateBatterySource()
    {
        if (_batterySource is not null)
        {
            _batterySource.BatteryStateChanged -= OnBatteryStateChanged;
        }

        _batterySource = _tracker.ActiveController as DualSenseDevice;
        if (_batterySource is not null)
        {
            _batterySource.BatteryStateChanged += OnBatteryStateChanged;
        }

        UpdateTrayState();
    }

    /// <summary>
    /// Refreshes the tray icon and menu. The icon shows the active controller's
    /// battery percentage as plain numbers when the "show battery percentage"
    /// setting is enabled, or the app icon otherwise. The icon is only re-rendered
    /// when the percentage or the theme text color actually changed.
    /// </summary>
    private void UpdateTrayState()
    {
        DualSenseDevice? device = _tracker.ActiveController as DualSenseDevice;
        int percentage = device?.InputReport?.Battery.DisplayPercentage ?? -1;
        bool showPercentage = _showBatteryPercentage && percentage >= 0;

        if (showPercentage)
        {
            Color textColor = GetTextColor();
            if (percentage != _lastPercentage || textColor != _lastTextColor)
            {
                _trayIcon.Icon = CreatePercentageIcon(percentage);
                _lastPercentage = percentage;
                _lastTextColor = textColor;
            }
        }
        else
        {
            Color accent = GetAccentColor();
            if (_lastPercentage is not null || accent != _lastAppIconAccent)
            {
                _trayIcon.Icon = CreateAppIcon(accent);
                _lastAppIconAccent = accent;
                _lastPercentage = null;
            }
        }

        RebuildMenu();
    }

    /// <summary>
    /// Renders the fallback tray icon in the app UI's icon style: the transparent
    /// controller illustration on an accent-colored rounded tile with a matching
    /// border. The border, padding, and corner radius mirror the title bar and
    /// settings page tiles (18px: 1px border + 2px padding + 3px radius) scaled to
    /// the tray's 64px render size.
    /// </summary>
    /// <param name="accent">The accent color of the active theme.</param>
    private static WindowIcon CreateAppIcon(Color accent)
    {
        const double size = IconSize;
        const double border = 3;
        const double padding = 7;
        const float radius = 11;

        using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize((int)size, (int)size), new Vector(96, 96));
        using DrawingContext context = bitmap.CreateDrawingContext();

        context.FillRectangle(new SolidColorBrush(accent), new Rect(0, 0, size, size), radius);

        using Stream stream = AssetLoader.Open(new Uri("avares://DualSenseClient/Assets/icon-transparent.png"));
        using Bitmap illustration = new Bitmap(stream);
        double image = size - (border + padding) * 2;
        context.DrawImage(illustration, new Rect(border + padding, border + padding, image, image));

        return new WindowIcon(bitmap);
    }

    /// <summary>
    /// Returns the accent color of the active theme, resolved from the
    /// <c>SystemAccentColor</c> resource. Falls back to the default theme's
    /// accent when the resource is missing.
    /// </summary>
    private static Color GetAccentColor()
    {
        if (Application.Current is { } app
            && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out object? value)
            && value is Color color)
        {
            return color;
        }

        return Color.Parse("#FF107C10");
    }

    /// <summary>
    /// Renders a tray icon showing the given battery percentage as bold numbers,
    /// drawn in the active theme's primary text color. The font size is picked
    /// per label length so every percentage stays legible without measuring.
    /// </summary>
    private static WindowIcon CreatePercentageIcon(int percentage)
    {
        const double size = IconSize;
        string label = percentage.ToString();
        using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize((int)size, (int)size), new Vector(96, 96));
        using DrawingContext context = bitmap.CreateDrawingContext();

        SolidColorBrush textBrush = new SolidColorBrush(GetTextColor());
        double fontSize = label.Length switch
        {
            1 => 56,
            2 => 56,
            _ => 40
        };

        FormattedText text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), fontSize, textBrush);
        context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));

        return new WindowIcon(bitmap);
    }

    /// <summary>
    /// Returns the primary text color of the active theme, resolved from the
    /// <c>TextFillColorPrimaryBrush</c> resource. The variant-aware lookup searches
    /// FluentAvalonia's theme resources (defined in Template.axaml and supplied by
    /// FluentAvalonia for the Light/Dark variants), so the percentage icon follows
    /// the application theme. Falls back to white when the resource is missing.
    /// </summary>
    private static Color GetTextColor()
    {
        if (Application.Current is { } app
            && app.TryGetResource("TextFillColorPrimaryBrush", app.ActualThemeVariant, out object? value)
            && value is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Colors.White;
    }

    /// <summary>
    /// Refreshes the tray state when a controller connects or disconnects.
    /// Raised on the UI thread.
    /// </summary>
    private void OnControllersChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateTrayState();

    /// <summary>
    /// Re-watches the active controller for battery changes when it changes.
    /// Raised on the UI thread (marshaled, because the tracker may raise the
    /// event from a background thread on disconnect).
    /// </summary>
    private void OnActiveControllerChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateBatterySource);

    /// <summary>
    /// Refreshes the tray state when the emulation service state changes, so the
    /// emulation menu re-enables after a (re)creation finishes. May be raised on a
    /// background thread, so it is marshaled to the UI thread.
    /// </summary>
    private void OnEmulationStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateTrayState);

    /// <summary>
    /// Re-renders the tray icon right away when the user switches the app theme.
    /// Raised on the UI thread (ThemeService.SetTheme callers).
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e) => UpdateTrayState();

    /// <summary>
    /// Re-renders the tray icon when the effective light/dark variant changes, e.g.
    /// the OS switches while the System theme follows it. Raised on the UI thread.
    /// </summary>
    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => UpdateTrayState();

    /// <summary>
    /// Refreshes the tray state when the selected controller's battery changes.
    /// Raised from the device read loop, so it is marshaled to the UI thread.
    /// </summary>
    private void OnBatteryStateChanged(object? sender, BatteryStateEventArgs e) => Dispatcher.UIThread.Post(UpdateTrayState);

    /// <summary>
    /// Refreshes the tray state when settings, profiles, or controller info change
    /// (e.g. the battery percentage visibility toggle, a rename, or a profile edit
    /// in the main window). Raised on the UI thread.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _showBatteryPercentage = _settingsService.Settings.Ui.ShowBatteryPercentage;
        UpdateTrayState();
    }
}