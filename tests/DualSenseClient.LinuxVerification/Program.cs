// Opt-in desktop integration probe. Run on the target Linux desktop only, with
// the production app closed. Build into a separate installed directory after
// copying its Config directory; never run this against somebody else's settings.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.GUI;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels.Pages;
using DualSenseClient.GUI.Views.Pages;
using DualSenseClient.Settings;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run this opt-in probe on the Linux desktop.");
string mode = args.FirstOrDefault() ?? "state";
Dispatcher.UIThread.Post(async () =>
{
    try
    {
        await Task.Delay(3000);
        SettingsService settings = App.Services.GetRequiredService<SettingsService>();
        Console.WriteLine($"STARTUP startInTray={settings.Settings.Ui.StartInTray} mainWindowVisible={App.MainWindow?.IsVisible}");
        SettingsPageViewModel vm = App.Services.GetRequiredService<SettingsPageViewModel>();
        if (mode is "enable" or "disable")
        {
            vm.LaunchOnControllerConnect = mode == "enable";
            while (vm.IsControllerLaunchBusy) await Task.Delay(100);
            Console.WriteLine($"TOGGLE visible={vm.IsControllerLaunchSupported} value={vm.LaunchOnControllerConnect} saved={settings.Settings.Ui.LaunchOnControllerConnect} error={vm.ControllerLaunchStatus}");
            if (vm.LaunchOnControllerConnect != (mode == "enable") || vm.ControllerLaunchStatus.Length > 0)
                throw new InvalidOperationException("Toggle did not apply successfully.");
        }
        if (mode == "register-installed")
        {
            // Register a separately published single-file installation after the
            // GUI toggle tests; the probe's own dotnet host is not that executable.
            await new DualSenseClient.Launch.LinuxControllerLaunch().ApplyAsync(true,
                DualSenseClient.Launch.LinuxControllerLaunch.ResolveCommand(null, args[1], ""), []);
            Console.WriteLine("REGISTERED installed executable " + args[1]);
        }
        if (mode == "failure")
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", "/nonexistent-controller-launch-test");
                vm.LaunchOnControllerConnect = false;
                while (vm.IsControllerLaunchBusy) await Task.Delay(100);
                Console.WriteLine($"FAILURE value={vm.LaunchOnControllerConnect} saved={settings.Settings.Ui.LaunchOnControllerConnect} error={vm.ControllerLaunchStatus}");
                if (!vm.LaunchOnControllerConnect || !settings.Settings.Ui.LaunchOnControllerConnect || vm.ControllerLaunchStatus.Length == 0)
                    throw new InvalidOperationException("Failed apply did not preserve the enabled setting and report an error.");
            }
            finally { Environment.SetEnvironmentVariable("PATH", path); }
        }
        if (mode == "screenshot")
        {
            SettingsPage page = new();
            Window window = new() { Content = page, Width = 1000, Height = 800, Title = "DualSense Client — Linux settings" };
            window.Show();
            await Task.Delay(1000);
            FASettingsExpander expander = window.GetVisualDescendants().OfType<FASettingsExpander>().First(item => item.Header?.ToString() == "Window");
            expander.IsExpanded = true;
            await Task.Delay(500);
            expander.BringIntoView();
            await Task.Delay(500);
            using RenderTargetBitmap bitmap = new(new PixelSize(1000, 800));
            bitmap.Render(window);
            bitmap.Save(args[1], PngBitmapEncoderOptions.Default);
            Console.WriteLine("SCREENSHOT " + args[1]);
            window.Close();
        }
        if (mode == "input")
        {
            HashSet<DualSenseDevice> subscribed = new();
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                foreach (DualSenseDevice device in App.Services.GetRequiredService<IControllerTracker>().Controllers.OfType<DualSenseDevice>())
                    if (subscribed.Add(device))
                    {
                        Console.WriteLine("INPUT attached " + device.Info.ProductName);
                        device.ButtonPressed += (_, e) => Console.WriteLine("INPUT pressed " + e.Button);
                        device.ButtonReleased += (_, e) => Console.WriteLine("INPUT released " + e.Button);
                    }
                await Task.Delay(100);
            }
        }
        // Exercise the same Exit handler used by the tray menu, rather than killing
        // the process and claiming that proves the app's explicit Exit behavior.
        object tray = App.Services.GetRequiredService<TrayIconService>();
        typeof(TrayIconService).GetMethod("ExitApplication", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tray, null);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("VERIFICATION FAILED: " + ex);
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1);
    }
});
AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
