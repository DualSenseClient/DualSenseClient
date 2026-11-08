using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DualSenseClient.Launch;

/// <summary>Owns the signed-in user's BlueZ connection watcher, without elevation.</summary>
public sealed class LinuxControllerLaunch
{
    public const string UnitName = "dualsense-controller-launch.service";
    private readonly string _home;
    private readonly Func<string, string[], Task<LaunchCommandResult>> _run;
    public LinuxControllerLaunch(string? home = null, Func<string, string[], Task<LaunchCommandResult>>? run = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _run = run ?? RunAsync;
    }

    public static string Quote(string value)
    {
        if (value.Any(c => c is '\n' or '\r' or '\0'))
            throw new ArgumentException("Launch paths cannot contain control characters.");
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("%", "%%").Replace("$", "$$") + "\"";
    }

    public static string[] ResolveCommand(string? appImage, string processPath, string assemblyPath)
    {
        string executable = string.IsNullOrEmpty(appImage) ? processPath : appImage;
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("The installed application could not be found. Run it from its permanent location and try again.");
        return string.IsNullOrEmpty(appImage) && Path.GetFileNameWithoutExtension(processPath) == "dotnet"
            ? [processPath, assemblyPath, "--controller-connect"] : [executable, "--controller-connect"];
    }

    public static string CreateUnit(string script, string config)
    {
        return $"""
            [Unit]
            Description=Launch DualSense Client on Bluetooth connection
            After=graphical-session.target
            PartOf=graphical-session.target
            [Service]
            Type=simple
            KillMode=process
            ExecStart=/usr/bin/python3 {Quote(script)} --config {Quote(config)}
            Restart=on-failure
            RestartSec=5
            [Install]
            WantedBy=graphical-session.target
            """ + "\n";
    }

    public async Task<bool> IsEnabledAsync() =>
        (await _run("systemctl", ["--user", "is-enabled", UnitName])).ExitCode == 0 &&
        (await _run("systemctl", ["--user", "is-active", UnitName])).ExitCode == 0;

    private async Task Check(string command, params string[] arguments)
    {
        LaunchCommandResult result = await _run(command, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{command} failed: {result.Error.Trim()} Check that BlueZ, Python 3 and a systemd desktop user session are available.");
    }

    public async Task ApplyAsync(bool enabled, string[] command, IEnumerable<string> knownAddresses)
    {
        string data = Path.Combine(_home, ".local", "share", "DualSenseClient", "controller-launch");
        string unit = Path.Combine(_home, ".config", "systemd", "user", UnitName);
        string desktop = Path.Combine(_home, ".config", "autostart", "dualsense-controller-launch.desktop");
        string script = Path.Combine(data, "controller-session.py");
        string config = Path.Combine(data, "launch.json");
        bool wasEnabled = await IsEnabledAsync();
        Dictionary<string, byte[]?> previous = new[] { unit, script, config, desktop }.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        try
        {
            if (!enabled)
            {
                if (!File.Exists(unit) && !wasEnabled)
                { if (File.Exists(desktop)) File.Delete(desktop); return; }
                await Check("systemctl", "--user", "disable", "--now", UnitName);
                if (File.Exists(unit))
                    File.Delete(unit);
                if (File.Exists(desktop))
                    File.Delete(desktop);
                await Check("systemctl", "--user", "daemon-reload");
                return;
            }
            await Check("/usr/bin/python3", "--version");
            await Check("bluetoothctl", "list");
            LaunchCommandResult paired = await _run("bluetoothctl", ["devices", "Paired"]);
            if (paired.ExitCode != 0)
                throw new InvalidOperationException("Could not list paired Bluetooth controllers: " + paired.Error);
            HashSet<string> known = knownAddresses.Select(NormalizeAddress).Where(address => address != null).Select(address => address!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] addresses = paired.Output.Split('\n').Select(line => Regex.Match(line, @"^Device ([\dA-Fa-f:]{17}) (.+)$"))
                .Where(match => match.Success && (known.Contains(match.Groups[1].Value) || match.Groups[2].Value.Contains("DualSense", StringComparison.OrdinalIgnoreCase)))
                .Select(match => match.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
            if (addresses.Length == 0)
                throw new InvalidOperationException("Pair a Bluetooth DualSense first, then enable connection launch.");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
            using Stream resource = typeof(LinuxControllerLaunch).Assembly.GetManifestResourceStream("controller-session.py")!;
            using StreamReader reader = new(resource);
            await File.WriteAllTextAsync(script, await reader.ReadToEndAsync());
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { addresses, command }));
            Dictionary<string, string> environment = new();
            foreach (string name in new[] { "DISPLAY", "WAYLAND_DISPLAY", "XAUTHORITY", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS", "XDG_SESSION_TYPE" })
                if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                    environment[name] = value;
            if (environment.Count > 0)
                await Check("systemctl", new[] { "--user", "import-environment" }.Concat(environment.Keys).ToArray());
            await File.WriteAllTextAsync(unit, CreateUnit(script, config));
            Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
            // The XDG desktop supplies fresh DISPLAY/XAUTHORITY after every sign-in,
            // including desktops that do not activate graphical-session.target.
            await File.WriteAllTextAsync(desktop, "[Desktop Entry]\nType=Application\nName=DualSense connection launcher\nTerminal=false\nExec=/usr/bin/python3 " + Quote(script) + " --session-start\n");
            await Check("systemctl", "--user", "daemon-reload");
            await Check("systemctl", "--user", "enable", "--now", UnitName);
            await Check("systemctl", "--user", "restart", UnitName);
            if (!await IsEnabledAsync())
                throw new InvalidOperationException("Controller launch service did not remain active. See journalctl --user -u " + UnitName);
            // Retire only this project's former manual watcher instances, after the
            // managed service is healthy. KillMode=process preserves the client.
            LaunchCommandResult legacy = await _run("systemctl", ["--user", "list-units", "--all", "--plain", "--no-legend", "dualsense-session@*.service"]);
            foreach (string line in legacy.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string name = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (name.StartsWith("dualsense-session@") && name.EndsWith(".service"))
                    await Check("systemctl", "--user", "disable", "--now", name);
            }
        }
        catch (Exception failure)
        {
            try
            {
                foreach ((string path, byte[]? content) in previous)
                {
                    if (content == null)
                    { if (File.Exists(path)) File.Delete(path); }
                    else
                        await File.WriteAllBytesAsync(path, content);
                }
                await Check("systemctl", "--user", "daemon-reload");
                await Check("systemctl", "--user", wasEnabled ? "enable" : "disable", "--now", UnitName);
                if (wasEnabled)
                    await Check("systemctl", "--user", "restart", UnitName);
            }
            catch (Exception rollback) { throw new InvalidOperationException(failure.Message + " Recovery also failed: " + rollback.Message, failure); }
            throw;
        }
    }

    public static string? NormalizeAddress(string address)
    {
        string compact = address.Replace(":", "").Replace("-", "");
        return compact.Length == 12 && compact.All(Uri.IsHexDigit)
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2).ToUpperInvariant())) : null;
    }

    private static async Task<LaunchCommandResult> RunAsync(string executable, string[] arguments)
    {
        ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["LC_ALL"] = "C";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        try
        { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new InvalidOperationException(executable + " timed out. Check your desktop user session."); }
        return new(process.ExitCode, await output, await error);
    }
}

public sealed record LaunchCommandResult(int ExitCode, string Output = "", string Error = "");