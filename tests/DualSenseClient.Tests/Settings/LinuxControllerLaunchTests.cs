using DualSenseClient.Launch;

namespace DualSenseClient.Tests.Settings;

public class LinuxControllerLaunchTests
{
    private string _home = null!;
    private readonly List<string> _calls = new List<string>();
    private bool _enabled;
    private bool _failRestart;

    [SetUp]
    public void Setup()
    {
        _home = Path.Combine(Path.GetTempPath(), "launch test " + Guid.NewGuid());
        _calls.Clear();
        _enabled = false;
        _failRestart = false;
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }

    private Task<LaunchCommandResult> Run(string executable, string[] args)
    {
        string call = executable + " " + string.Join(" ", args);
        _calls.Add(call);
        if (call.Contains("devices Paired"))
        {
            return Task.FromResult(new LaunchCommandResult(0,
                "Device AA:BB:CC:DD:EE:FF DualSense Wireless Controller\nDevice 11:22:33:44:55:66 Headphones\n"));
        }

        if (args.Contains("is-enabled") || args.Contains("is-active"))
        {
            return Task.FromResult(new LaunchCommandResult(_enabled ? 0 : 1));
        }

        if (args.Contains("enable"))
        {
            _enabled = true;
        }

        if (args.Contains("disable"))
        {
            _enabled = false;
        }

        if (args.Contains("restart") && _failRestart)
        {
            _failRestart = false;
            return Task.FromResult(new LaunchCommandResult(1, Error: "test restart failure"));
        }

        return Task.FromResult(new LaunchCommandResult(0));
    }

    [Test]
    public async Task EnableAndDisableOwnAutomationWithoutTouchingAppOrPairing()
    {
        LinuxControllerLaunch service = new LinuxControllerLaunch(_home, Run);
        await service.ApplyAsync(true, ["/Apps/Client With Spaces", "--controller-connect"], []);
        string data = Path.Combine(_home, ".local/share/DualSenseClient/controller-launch");
        Assert.That(File.ReadAllText(Path.Combine(data, "launch.json")), Does.Contain("AA:BB:CC:DD:EE:FF").And.Not.Contain("Headphones"));
        Assert.That(File.ReadAllText(Path.Combine(_home, ".config/systemd/user", LinuxControllerLaunch.UnitName)), Does.Contain("KillMode=process"));
        Assert.That(File.ReadAllText(Path.Combine(_home, ".config/autostart/dualsense-controller-launch.desktop")), Does.Contain("--session-start"));
        Assert.That(await service.IsEnabledAsync(), Is.True);
        await service.ApplyAsync(false, [], []);
        Assert.That(await service.IsEnabledAsync(), Is.False);
        Assert.That(File.Exists(Path.Combine(_home, ".config/autostart/dualsense-controller-launch.desktop")), Is.False);
        Assert.That(_calls.Any(call => call.Contains("kill") || call.Contains("remove ")), Is.False);
    }

    [Test]
    public async Task FailedUpdateRestoresPreviousFilesAndService()
    {
        LinuxControllerLaunch service = new LinuxControllerLaunch(_home, Run);
        await service.ApplyAsync(true, ["/first/client"], []);
        string config = Path.Combine(_home, ".local/share/DualSenseClient/controller-launch/launch.json");
        string original = File.ReadAllText(config);
        _failRestart = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(true, ["/second/client"], []));
        Assert.That(File.ReadAllText(config), Is.EqualTo(original));
        Assert.That(await service.IsEnabledAsync(), Is.True);
    }

    [Test]
    public void UnitQuotesSpacesPercentDollarAndRejectsNewlines()
    {
        string unit = LinuxControllerLaunch.CreateUnit("/a space/100%/$client.py", "/a/config.json");
        Assert.That(unit, Does.Contain("\"/a space/100%%/$$client.py\""));
        Assert.Throws<ArgumentException>(() => LinuxControllerLaunch.Quote("/tmp/path\nExecStart=bad"));
        Assert.That(LinuxControllerLaunch.NormalizeAddress("aabb-ccdd-eeff"), Is.EqualTo("AA:BB:CC:DD:EE:FF"));
        Assert.That(LinuxControllerLaunch.NormalizeAddress("not-a-mac"), Is.Null);
    }

    [Test]
    public void CheckedSavePreservesOtherSettingsAndPropagatesFailure()
    {
        string path = Path.Combine(_home, "config.json");
        var store = new DualSenseClient.Settings.JsonFileStore<DualSenseClient.Settings.Sections.UiSettings>(path);
        store.Item.StartInTray = true;
        store.Item.LaunchOnControllerConnect = true;
        store.SaveChecked();
        string saved = File.ReadAllText(path);
        Assert.That(saved, Does.Contain("\"startInTray\": true").And.Contain("\"launchOnControllerConnect\": true"));
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.Throws<IOException>(() => store.SaveChecked());
        Assert.That(Directory.GetFiles(_home, "*.tmp"), Is.Empty);
    }

    [Test]
    public void AppImageUsesPermanentPathInsteadOfMount()
    {
        Directory.CreateDirectory(_home);
        string appImage = Path.Combine(_home, "Client image.AppImage");
        File.WriteAllText(appImage, "test");
        Assert.That(LinuxControllerLaunch.ResolveCommand(appImage, "/tmp/.mount/Client", "ignored"),
            Is.EqualTo(new[]
            {
                appImage, "--controller-connect"
            }));
        Assert.That(LinuxControllerLaunch.ResolveCommand(null, appImage, "ignored"),
            Is.EqualTo(new[]
            {
                appImage, "--controller-connect"
            }));
    }
}