using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

/// <summary>
/// Covers the application selector resolution: blank and unknown selections must resolve
/// to nothing rather than throwing, and enumeration must always return a list.
/// </summary>
public class AudioApplicationServiceTests
{
    [Test]
    public void TryResolveProcessId_BlankSelection_ReturnsFalse()
    {
        AudioApplicationService service = new AudioApplicationService();

        Assert.That(service.TryResolveProcessId("   ", out uint processId), Is.False);
        Assert.That(processId, Is.Zero);
    }

    [Test]
    public void TryResolveProcessId_UnknownExecutable_ReturnsFalse()
    {
        AudioApplicationService service = new AudioApplicationService();

        bool resolved = service.TryResolveProcessId(@"C:\definitely-not-a-real-app-7f3c.exe", out uint processId);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(processId, Is.Zero);
        });
    }

    [Test]
    public void TryResolveProcessId_CurrentProcessExecutable_ResolvesToThisProcess()
    {
        string? executablePath = Environment.ProcessPath;
        Assert.That(executablePath, Is.Not.Null.And.Not.Empty, "the test host must expose its own executable path");

        AudioApplicationService service = new AudioApplicationService();
        bool resolved = service.TryResolveProcessId(executablePath!, out uint processId);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(processId, Is.EqualTo((uint)Environment.ProcessId));
        });
    }

    [Test]
    public void GetPlayingApplications_NeverReturnsNull()
    {
        AudioApplicationService service = new AudioApplicationService();

        IReadOnlyList<AudioApplicationInfo> applications = service.GetPlayingApplications();

        Assert.That(applications, Is.Not.Null);
    }

    [Test]
    public void GetPlayingApplications_ReportsDistinctExecutablePaths()
    {
        AudioApplicationService service = new AudioApplicationService();

        List<string> paths = service.GetPlayingApplications().Select(app => app.ExecutablePath).ToList();

        Assert.That(paths, Is.Unique);
    }
}