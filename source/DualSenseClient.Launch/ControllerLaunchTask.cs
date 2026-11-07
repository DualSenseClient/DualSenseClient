using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace DualSenseClient.Launch;

/// <summary>Windows controller connection launch registration, scoped to the signed-in user.</summary>
public static class ControllerLaunchTask
{
    public const string TaskName = "DualSense Client - Launch on controller connection";

    public static string CreateXml(string executable, string userSid, IEnumerable<string> addresses)
    {
        ulong[] values = addresses.Select(address => address.Replace(":", "").Replace("-", ""))
            .Where(address => address.Length == 12 && address.All(Uri.IsHexDigit))
            .Select(address => ulong.Parse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .Distinct().ToArray();
        if (values.Length == 0)
        {
            throw new InvalidOperationException("Connect a Bluetooth controller in the app before enabling connection launch.");
        }

        string filter = string.Join(" or ", values.Select(value => $"Data[@Name='bthAddr']='{value}'"));
        const string channel = "Microsoft-Windows-Bluetooth-Policy/Operational";
        string subscription = new XElement("QueryList", new XElement("Query", new XAttribute("Id", "0"),
            new XAttribute("Path", channel), new XElement("Select", new XAttribute("Path", channel),
                $"*[System[EventID=9] and EventData[{filter}]]"))).ToString();
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        XElement E(string name, params object[] content)
        {
            return new XElement(ns + name, content);
        }

        return E("Task", new XAttribute("version", "1.4"),
                E("Triggers", E("EventTrigger", E("Enabled", "true"), E("Subscription", subscription))),
                E("Principals",
                    E("Principal", new XAttribute("id", "User"), E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
                E("Settings", E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"), E("StopIfGoingOnBatteries", "false"),
                    E("ExecutionTimeLimit", "PT0S")),
                E("Actions", new XAttribute("Context", "User"),
                    E("Exec", E("Command", executable), E("Arguments", "--controller-connect"), E("WorkingDirectory", Path.GetDirectoryName(executable)!))))
            .ToString();
    }

    /// <summary>Registers or removes the task. Only enabling the event log needs elevation.</summary>
    public static async Task ApplyAsync(bool enabled, string executable, string userSid, IEnumerable<string> addresses)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!enabled)
        {
            // A missing task is already disabled.
            if (await RunAsync(Path.Combine(system, "schtasks.exe"), ["/Query", "/TN", TaskName]) == 0 &&
                await RunAsync(Path.Combine(system, "schtasks.exe"), ["/Delete", "/TN", TaskName, "/F"]) != 0)
            {
                throw new InvalidOperationException("Could not remove the controller launch task.");
            }

            return;
        }

        string xml = CreateXml(executable, userSid, addresses);
        using Process elevation = Process.Start(new ProcessStartInfo(Path.Combine(system, "wevtutil.exe"),
                                      "sl Microsoft-Windows-Bluetooth-Policy/Operational /e:true")
                                  {
                                      UseShellExecute = true,
                                      Verb = "runas",
                                      WindowStyle = ProcessWindowStyle.Hidden
                                  })
                                  ?? throw new InvalidOperationException("Could not enable the Bluetooth event log.");
        await elevation.WaitForExitAsync();
        if (elevation.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not enable the Bluetooth event log.");
        }

        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, xml);
            if (await RunAsync(Path.Combine(system, "schtasks.exe"), ["/Create", "/TN", TaskName, "/XML", file, "/F"]) != 0)
            {
                throw new InvalidOperationException("Could not register the controller launch task.");
            }
        }
        finally { File.Delete(file); }
    }

    private static async Task<int> RunAsync(string executable, string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows task management.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}